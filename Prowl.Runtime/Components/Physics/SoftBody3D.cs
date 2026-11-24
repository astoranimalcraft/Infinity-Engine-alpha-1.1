using System;
using System.Collections.Generic;

using Jitter2;
using Jitter2.Dynamics;
using Jitter2.Dynamics.Constraints;
using Jitter2.LinearMath;
using Jitter2.SoftBodies;

using Infinity.Echo;
using Infinity.Runtime;
using Infinity.Runtime.Resources;
using Infinity.Vector;

namespace Infinity.Runtime;

public enum SoftBodyType
{
    // Elastic solid: bends under force and tends to recover its original shape.
    Rubber,

    // Soft solid: squashes and wobbles while trying to preserve its volume.
    Jelly,

    // Thin fabric/shell: stretch, area and bending resistance; no volume pressure.
    Advanced
}

[AddComponentMenu("Physics/Soft Body 3D", "\ufb12")]
public sealed class SoftBody3D : Component
{
    [SerializeField] private SoftBodyType type = SoftBodyType.Rubber;
    [SerializeField] private float size = 1f;

    // Jitter2's SetMassInertia(..., true) receives inverse mass in this component.
    [SerializeField] private float vertexMass = 0.01f;

    [SerializeField] private float springSoftness = 0.003f;
    [SerializeField] private float springBias = 0.22f;
    [SerializeField] private float bendingSoftness = 0.035f;
    [SerializeField] private float damping = 0.05f;

    [SerializeField] private float pressure = 5f;
    [SerializeField] private bool preserveVolume = true;
    [SerializeField] private float collisionThickness = 0.05f;
    [SerializeField] private float weldTolerance = 0.0001f;

    // This is a hard cap. Vertices are welded only when they are already within Weld Tolerance.
    // The component will not merge distant vertices to force a dense model under this limit,
    // because doing that can collapse triangles and destroy the physics topology.
    [SerializeField] private int maxPhysicsVertices = 400;

    [SerializeField] private int visualUpdateEveryNFrames = 1;
    [SerializeField] private bool pinTopEdge;
    [SerializeField] private float pinTopEdgeFraction = 0.08f;

    // Additional triangle-area resistance, primarily used by the Advanced fabric mode.
    [SerializeField] private float areaStiffness = 1.5f;

    private SoftBody? softBody;
    private readonly List<MeshBinding> meshBindings = new();
    private readonly HashSet<EdgeKey> createdSpringEdges = new();
    private readonly List<RigidBody> pinAnchorBodies = new();

    private bool componentEnabled;
    private int frameCounter;

    public SoftBody? Native => softBody;

    public SoftBodyType Type
    {
        get => type;
        set
        {
            if (type == value)
                return;

            type = value;
            ConfigureType();
            if (componentEnabled)
                CreateSoftBody();
        }
    }

    public float Size
    {
        get => size;
        set
        {
            size = MathF.Max(0.01f, value);
            if (componentEnabled)
                CreateSoftBody();
        }
    }

    public float VertexMass
    {
        get => vertexMass;
        set => vertexMass = MathF.Max(0.001f, value);
    }

    public float SpringSoftness
    {
        get => springSoftness;
        set => springSoftness = MathF.Max(0f, value);
    }

    public float SpringBias
    {
        get => springBias;
        set => springBias = Math.Clamp(value, 0f, 1f);
    }

    public float BendingSoftness
    {
        get => bendingSoftness;
        set => bendingSoftness = MathF.Max(0f, value);
    }

    public float Damping
    {
        get => damping;
        set => damping = MathF.Max(0f, value);
    }

    public float Pressure
    {
        get => pressure;
        set
        {
            pressure = MathF.Max(0f, value);
            if (softBody is MeshSoftBody meshBody)
                meshBody.Pressure = type == SoftBodyType.Advanced || !preserveVolume ? 0f : pressure;
        }
    }

    public bool PreserveVolume
    {
        get => preserveVolume;
        set
        {
            preserveVolume = value;
            if (softBody is MeshSoftBody meshBody)
                meshBody.Pressure = preserveVolume && type != SoftBodyType.Advanced ? pressure : 0f;
        }
    }

    public float CollisionThickness
    {
        get => collisionThickness;
        set => collisionThickness = MathF.Max(0.001f, value);
    }

    public float WeldTolerance
    {
        get => weldTolerance;
        set => weldTolerance = MathF.Max(0.000001f, value);
    }

    public int MaxPhysicsVertices
    {
        get => maxPhysicsVertices;
        set
        {
            maxPhysicsVertices = Math.Max(3, value);
            if (componentEnabled)
                CreateSoftBody();
        }
    }

    public int VisualUpdateEveryNFrames
    {
        get => visualUpdateEveryNFrames;
        set => visualUpdateEveryNFrames = Math.Clamp(value, 1, 8);
    }

    public float AreaStiffness
    {
        get => areaStiffness;
        set
        {
            areaStiffness = MathF.Max(0f, value);
            if (softBody is MeshSoftBody meshBody)
                meshBody.AreaStiffness = areaStiffness;
        }
    }

    /// <summary>Anchors the upper strip of Advanced fabric using soft distance constraints.</summary>
    public bool PinTopEdge
    {
        get => pinTopEdge;
        set
        {
            pinTopEdge = value;
            if (componentEnabled && type == SoftBodyType.Advanced)
                CreateSoftBody();
        }
    }

    public float PinTopEdgeFraction
    {
        get => pinTopEdgeFraction;
        set => pinTopEdgeFraction = Math.Clamp(value, 0.01f, 0.5f);
    }

    private sealed class MeshBinding
    {
        public MeshRenderer Renderer = null!;
        public Mesh OriginalMesh = null!;
        public Mesh DeformedMesh = null!;
        public Float3[] WorldVertices = Array.Empty<Float3>();
        public Float3[] UpdateBuffer = Array.Empty<Float3>();
        public int[] VertexToParticle = Array.Empty<int>();
    }

    private sealed class TriangleInfo
    {
        public int A;
        public int B;
        public int C;

        public TriangleInfo(int a, int b, int c)
        {
            A = a;
            B = b;
            C = c;
        }
    }

    private readonly struct SurfaceTriangle
    {
        public readonly int A;
        public readonly int B;
        public readonly int C;

        public SurfaceTriangle(int a, int b, int c)
        {
            A = a;
            B = b;
            C = c;
        }
    }

    private readonly struct AreaTriangle
    {
        public readonly int A;
        public readonly int B;
        public readonly int C;
        public readonly float RestArea;

        public AreaTriangle(int a, int b, int c, float restArea)
        {
            A = a;
            B = b;
            C = c;
            RestArea = restArea;
        }
    }

    private readonly struct EdgeUse
    {
        public readonly int TriangleIndex;
        public readonly int Opposite;
        public readonly bool Forward;

        public EdgeUse(int triangleIndex, int opposite, bool forward)
        {
            TriangleIndex = triangleIndex;
            Opposite = opposite;
            Forward = forward;
        }
    }

    private readonly struct EdgeKey : IEquatable<EdgeKey>
    {
        public readonly int A;
        public readonly int B;

        public EdgeKey(int a, int b)
        {
            if (a <= b)
            {
                A = a;
                B = b;
            }
            else
            {
                A = b;
                B = a;
            }
        }

        public bool Equals(EdgeKey other) => A == other.A && B == other.B;
        public override bool Equals(object? obj) => obj is EdgeKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(A, B);
    }

    private readonly struct TriangleKey : IEquatable<TriangleKey>
    {
        private readonly int a;
        private readonly int b;
        private readonly int c;

        public TriangleKey(int x, int y, int z)
        {
            if (x > y) (x, y) = (y, x);
            if (y > z) (y, z) = (z, y);
            if (x > y) (x, y) = (y, x);
            a = x;
            b = y;
            c = z;
        }

        public bool Equals(TriangleKey other) => a == other.a && b == other.b && c == other.c;
        public override bool Equals(object? obj) => obj is TriangleKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(a, b, c);
    }

    // Spatial hash cell. Neighboring cells are checked as well, so vertices close to a cell
    // boundary can still be welded. A geometric distance check prevents merging distant points.
    private readonly struct PositionKey : IEquatable<PositionKey>
    {
        private readonly long x;
        private readonly long y;
        private readonly long z;

        public PositionKey(Float3 position, Float3 origin, float inverseCellSize, int offsetX = 0, int offsetY = 0, int offsetZ = 0)
        {
            x = (long)Math.Floor(((double)position.X - origin.X) * inverseCellSize) + offsetX;
            y = (long)Math.Floor(((double)position.Y - origin.Y) * inverseCellSize) + offsetY;
            z = (long)Math.Floor(((double)position.Z - origin.Z) * inverseCellSize) + offsetZ;
        }

        public bool Equals(PositionKey other) => x == other.x && y == other.y && z == other.z;
        public override bool Equals(object? obj) => obj is PositionKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(x, y, z);
    }

    /// <summary>
    /// Jitter2 soft body with bounded volume recovery and optional triangle-area resistance.
    /// Pressure forces use the correct per-vertex volume gradient; applying the same force to
    /// all three triangle vertices would create artificial translation and unstable expansion.
    /// </summary>
    private sealed class MeshSoftBody : SoftBody
    {
        private SurfaceTriangle[] pressureTriangles = Array.Empty<SurfaceTriangle>();
        private AreaTriangle[] areaTriangles = Array.Empty<AreaTriangle>();
        private JVector[] areaForces = Array.Empty<JVector>();
        private JVector[] pressureForces = Array.Empty<JVector>();
        private double restVolume;

        public float Pressure { get; set; }
        public float AreaStiffness { get; set; }

        public MeshSoftBody(World world) : base(world) { }

        public void SetPressureSurface(List<SurfaceTriangle> triangles, double volume)
        {
            pressureTriangles = triangles.ToArray();
            restVolume = volume;
        }

        public void SetAreaSurface(List<AreaTriangle> triangles, float stiffness)
        {
            areaTriangles = triangles.ToArray();
            AreaStiffness = MathF.Max(0f, stiffness);
            areaForces = new JVector[Vertices.Count];
        }

        protected override void WorldOnPostStep(TimeStep timeStep)
        {
            base.WorldOnPostStep(timeStep);

            if (!IsActive || Vertices.Count == 0)
                return;

            ApplyAreaResistance();
            ApplyVolumePressure();
        }

        private void ApplyAreaResistance()
        {
            if (AreaStiffness <= 0f || areaTriangles.Length == 0)
                return;

            if (areaForces.Length != Vertices.Count)
                areaForces = new JVector[Vertices.Count];
            else
                Array.Clear(areaForces, 0, areaForces.Length);

            for (int i = 0; i < areaTriangles.Length; i++)
            {
                AreaTriangle triangle = areaTriangles[i];
                RigidBody bodyA = Vertices[triangle.A];
                RigidBody bodyB = Vertices[triangle.B];
                RigidBody bodyC = Vertices[triangle.C];

                JVector a = bodyA.Position;
                JVector b = bodyB.Position;
                JVector c = bodyC.Position;
                JVector cross = (b - a) % (c - a);
                float twiceAreaSquared = (float)cross.LengthSquared();

                if (twiceAreaSquared <= 1e-12f || triangle.RestArea <= 1e-8f)
                    continue;

                float twiceArea = MathF.Sqrt(twiceAreaSquared);
                float currentArea = twiceArea * 0.5f;
                float areaError = triangle.RestArea - currentArea;

                if (MathF.Abs(areaError) <= triangle.RestArea * 0.0025f)
                    continue;

                JVector normal = cross * (1f / twiceArea);
                float scale = areaError * AreaStiffness * 0.5f;

                // Gradient of triangle area with respect to each vertex. The three forces
                // sum to approximately zero, so this resists stretching without pushing the
                // complete fabric in an arbitrary direction.
                JVector forceA = ((b - c) % normal) * scale;
                JVector forceB = ((c - a) % normal) * scale;
                JVector forceC = ((a - b) % normal) * scale;

                areaForces[triangle.A] += forceA;
                areaForces[triangle.B] += forceB;
                areaForces[triangle.C] += forceC;
            }

            const float maxAreaForce = 1.0f;
            const float maxAreaForceSquared = maxAreaForce * maxAreaForce;

            for (int i = 0; i < areaForces.Length; i++)
            {
                JVector force = areaForces[i];
                float lengthSquared = (float)force.LengthSquared();

                if (lengthSquared <= 1e-12f)
                    continue;

                if (lengthSquared > maxAreaForceSquared)
                    force *= maxAreaForce / MathF.Sqrt(lengthSquared);

                Vertices[i].AddForce(force, false);
            }
        }

        private void ApplyVolumePressure()
        {
            if (Pressure <= 0f || pressureTriangles.Length == 0 || restVolume <= 1e-8)
                return;

            if (pressureForces.Length != Vertices.Count)
                pressureForces = new JVector[Vertices.Count];
            else
                Array.Clear(pressureForces, 0, pressureForces.Length);

            // Use a particle-relative origin to reduce precision loss for models far from
            // the world origin. The closed-surface volume gradient is accumulated per particle.
            JVector origin = Vertices[pressureTriangles[0].A].Position;
            double signedVolume6 = 0.0;

            for (int i = 0; i < pressureTriangles.Length; i++)
            {
                SurfaceTriangle t = pressureTriangles[i];
                JVector a = Vertices[t.A].Position - origin;
                JVector b = Vertices[t.B].Position - origin;
                JVector c = Vertices[t.C].Position - origin;

                signedVolume6 += Dot(a, b % c);
            }

            double signedVolume = signedVolume6 / 6.0;
            double currentVolume = Math.Abs(signedVolume);
            if (currentVolume <= 1e-8)
                return;

            float volumeError = (float)((restVolume - currentVolume) / restVolume);
            volumeError = Math.Clamp(volumeError, -0.25f, 0.45f);

            if (MathF.Abs(volumeError) < 0.0005f)
                return;

            // If the entire surface is inverted, reverse the gradient so the pressure
            // still attempts to recover the positive rest volume.
            float orientation = signedVolume < 0.0 ? -1f : 1f;
            float factor = Pressure * volumeError * orientation / 6f;

            for (int i = 0; i < pressureTriangles.Length; i++)
            {
                SurfaceTriangle t = pressureTriangles[i];
                JVector a = Vertices[t.A].Position - origin;
                JVector b = Vertices[t.B].Position - origin;
                JVector c = Vertices[t.C].Position - origin;

                // Gradient of signed tetrahedron volume:
                // dV/da = cross(b,c)/6, dV/db = cross(c,a)/6,
                // dV/dc = cross(a,b)/6.
                pressureForces[t.A] += (b % c) * factor;
                pressureForces[t.B] += (c % a) * factor;
                pressureForces[t.C] += (a % b) * factor;
            }

            // Clamp the final sum per particle, not each triangle contribution. A vertex
            // shared by many triangles would otherwise receive an excessive accumulated force.
            const float maxPressureForce = 0.35f;
            const float maxPressureForceSquared = maxPressureForce * maxPressureForce;

            for (int i = 0; i < pressureForces.Length; i++)
            {
                JVector force = pressureForces[i];
                float lengthSquared = (float)force.LengthSquared();

                if (lengthSquared <= 1e-12f)
                    continue;

                if (lengthSquared > maxPressureForceSquared)
                    force *= maxPressureForce / MathF.Sqrt(lengthSquared);

                Vertices[i].AddForce(force, false);
            }
        }

        private static double Dot(JVector a, JVector b)
            => (double)a.X * b.X + (double)a.Y * b.Y + (double)a.Z * b.Z;
    }

    public override void OnEnable()
    {
        componentEnabled = true;
        ConfigureType();
        CreateSoftBody();
    }

    public override void OnDisable()
    {
        componentEnabled = false;
        DestroySoftBody();
        RestoreOriginalMeshes();
    }

    private void ConfigureType()
    {
        switch (type)
        {
            case SoftBodyType.Rubber:
                springSoftness = 0.003f;
                springBias = 0.24f;
                bendingSoftness = 0.025f;
                damping = 0.055f;
                pressure = 5f;
                preserveVolume = true;
                collisionThickness = 0.05f;
                areaStiffness = 0.65f;
                break;

            case SoftBodyType.Jelly:
                springSoftness = 0.075f;
                springBias = 0.14f;
                bendingSoftness = 0.12f;
                damping = 0.025f;
                pressure = 7f;
                preserveVolume = true;
                collisionThickness = 0.06f;
                areaStiffness = 0.35f;
                break;

            case SoftBodyType.Advanced:
                // Lower compliance on stretch links, softer bending links, and no volume force.
                springSoftness = 0.006f;
                springBias = 0.18f;
                bendingSoftness = 0.22f;
                damping = 0.045f;
                pressure = 0f;
                preserveVolume = false;
                collisionThickness = 0.05f;
                areaStiffness = 1.5f;
                break;
        }
    }

    private void CreateSoftBody()
    {
        size = MathF.Max(0.01f, size);
        vertexMass = MathF.Max(0.001f, vertexMass);
        springSoftness = MathF.Max(0f, springSoftness);
        springBias = Math.Clamp(springBias, 0f, 1f);
        bendingSoftness = MathF.Max(0f, bendingSoftness);
        damping = MathF.Max(0f, damping);
        pressure = MathF.Max(0f, pressure);
        collisionThickness = MathF.Max(0.001f, collisionThickness);
        weldTolerance = MathF.Max(0.000001f, weldTolerance);
        maxPhysicsVertices = Math.Max(3, maxPhysicsVertices);
        visualUpdateEveryNFrames = Math.Clamp(visualUpdateEveryNFrames, 1, 8);
        pinTopEdgeFraction = Math.Clamp(pinTopEdgeFraction, 0.01f, 0.5f);
        areaStiffness = MathF.Max(0f, areaStiffness);

        DestroySoftBody();
        RestoreOriginalMeshes();
        frameCounter = 0;

        if (GameObject.IsNotValid() || GameObject.Scene.IsNotValid())
            return;

        World world = GameObject.Scene.Physics.World;
        if (world == null)
            return;

        // The Prowl PhysicsWorld constructor supplied earlier already installs
        // DynamicTreeCollisionFilter and BroadPhaseCollisionFilter. Do not wrap them again here.
        CollectMeshes();

        if (meshBindings.Count == 0)
        {
            Debug.LogWarning("[SoftBody3D] No loaded MeshRenderer/Mesh found on this object or its children.");
            return;
        }

        List<Float3> particlePositions = BuildParticleMap(out bool welded);
        if (particlePositions.Count < 3)
        {
            RestoreOriginalMeshes();
            Debug.LogWarning(
                $"[SoftBody3D] Soft Body creation cancelled. The mesh has fewer than 3 unique particles or exceeds Max Physics Vertices ({maxPhysicsVertices}). Increase the limit or use a simpler physics mesh.");
            return;
        }

        softBody = new MeshSoftBody(world);
        CreatePhysicsVertices(particlePositions);

        List<TriangleInfo> triangles = BuildTriangles();
        if (triangles.Count == 0)
        {
            DestroySoftBody();
            RestoreOriginalMeshes();
            Debug.LogWarning("[SoftBody3D] No valid triangle surface was found. Check the source mesh topology and index data.");
            return;
        }

        Dictionary<EdgeKey, List<EdgeUse>> edgeMap = BuildEdgeMap(triangles);
        List<SurfaceTriangle> pressureSurface = OrientTriangleComponents(triangles, edgeMap, out double restVolume);
        List<AreaTriangle> areaSurface = BuildAreaSurface(triangles);

        CreateStructuralSprings(edgeMap);
        CreateBendingSprings(edgeMap);

        if (softBody is MeshSoftBody meshBody)
        {
            meshBody.SetPressureSurface(pressureSurface, restVolume);
            meshBody.Pressure = type != SoftBodyType.Advanced && preserveVolume && pressureSurface.Count > 0
                ? pressure
                : 0f;
            meshBody.SetAreaSurface(areaSurface, areaStiffness);
        }

        CreateCollisionTriangles(triangles);

        foreach (MeshBinding binding in meshBindings)
        {
            binding.DeformedMesh = CloneMesh(binding.OriginalMesh);
            binding.UpdateBuffer = new Float3[binding.VertexToParticle.Length];
            binding.Renderer.Mesh = binding.DeformedMesh;
            binding.Renderer.MarkVisuallyDirty();
        }

        UpdateVisualMeshes();

        if (welded)
            Debug.Log("[SoftBody3D] Coincident/seam vertices were welded within Weld Tolerance. The render mesh resolution was preserved.");

        if (type != SoftBodyType.Advanced && preserveVolume && pressureSurface.Count == 0)
            Debug.LogWarning("[SoftBody3D] Volume preservation is unavailable because the physics surface is open, non-manifold, or inconsistently connected.");

        Debug.Log(
            $"[SoftBody3D] {type}: {particlePositions.Count}/{maxPhysicsVertices} particles, {triangles.Count} physics/collision triangles, {edgeMap.Count} unique edges, {pressureSurface.Count} volume triangles.");
    }

    private void CollectMeshes()
    {
        meshBindings.Clear();

        foreach (MeshRenderer renderer in GameObject.GetComponentsInChildren<MeshRenderer>())
        {
            if (renderer.IsNotValid())
                continue;

            Mesh? source = renderer.Mesh;
            if (source.IsNotValid() || !source.IsLoaded)
                continue;

            Float3[] vertices = source.Vertices;
            uint[] indices = source.Indices;
            if (vertices.Length < 3 || indices.Length < 3)
                continue;

            Float4x4 localToWorld = renderer.Transform.LocalToWorldMatrix;
            Float3[] worldVertices = new Float3[vertices.Length];

            for (int i = 0; i < vertices.Length; i++)
                worldVertices[i] = Float4x4.TransformPoint(vertices[i], localToWorld);

            meshBindings.Add(new MeshBinding
            {
                Renderer = renderer,
                OriginalMesh = source,
                WorldVertices = worldVertices
            });
        }
    }

    private List<Float3> BuildParticleMap(out bool welded)
    {
        welded = false;
        Float3 min = new(float.MaxValue, float.MaxValue, float.MaxValue);
        int totalVertices = 0;

        foreach (MeshBinding binding in meshBindings)
        {
            totalVertices += binding.WorldVertices.Length;
            foreach (Float3 p in binding.WorldVertices)
            {
                min = new Float3(
                    MathF.Min(min.X, p.X),
                    MathF.Min(min.Y, p.Y),
                    MathF.Min(min.Z, p.Z));
            }
        }

        if (totalVertices < 3)
            return new List<Float3>();

        // Apply Size exactly once, in world space, around the combined source-mesh center.
        Float3 max = new(float.MinValue, float.MinValue, float.MinValue);
        foreach (MeshBinding binding in meshBindings)
        {
            foreach (Float3 p in binding.WorldVertices)
            {
                max = new Float3(
                    MathF.Max(max.X, p.X),
                    MathF.Max(max.Y, p.Y),
                    MathF.Max(max.Z, p.Z));
            }
        }

        Float3 center = (min + max) * 0.5f;
        float cellSize = MathF.Max(weldTolerance, 0.000001f);
        float inverseCellSize = 1f / cellSize;
        double toleranceSquared = (double)weldTolerance * weldTolerance;

        int initialCapacity = (int)Math.Min((long)totalVertices, (long)maxPhysicsVertices * 2L);
        Dictionary<PositionKey, List<int>> lookup = new(initialCapacity);
        List<Float3> positions = new(Math.Min(totalVertices, maxPhysicsVertices));

        foreach (MeshBinding binding in meshBindings)
        {
            binding.VertexToParticle = new int[binding.WorldVertices.Length];

            for (int i = 0; i < binding.WorldVertices.Length; i++)
            {
                Float3 sourcePosition = binding.WorldVertices[i];
                Float3 p = center + (sourcePosition - center) * size;
                PositionKey baseKey = new(p, center, inverseCellSize);
                int particleIndex = -1;

                // Search adjacent cells. A vertex is merged only when its actual distance
                // is <= WeldTolerance; cell size is never enlarged to force downsampling.
                for (int ox = -1; ox <= 1 && particleIndex < 0; ox++)
                {
                    for (int oy = -1; oy <= 1 && particleIndex < 0; oy++)
                    {
                        for (int oz = -1; oz <= 1 && particleIndex < 0; oz++)
                        {
                            PositionKey key = new(p, center, inverseCellSize, ox, oy, oz);
                            if (!lookup.TryGetValue(key, out List<int>? candidates))
                                continue;

                            foreach (int candidate in candidates)
                            {
                                Float3 existing = positions[candidate];
                                double dx = (double)p.X - existing.X;
                                double dy = (double)p.Y - existing.Y;
                                double dz = (double)p.Z - existing.Z;

                                if (dx * dx + dy * dy + dz * dz <= toleranceSquared)
                                {
                                    particleIndex = candidate;
                                    break;
                                }
                            }
                        }
                    }
                }

                if (particleIndex < 0)
                {
                    if (positions.Count >= maxPhysicsVertices)
                    {
                        Debug.LogWarning(
                            $"[SoftBody3D] Source mesh exceeds Max Physics Vertices ({maxPhysicsVertices}) after safe welding. Soft Body creation was cancelled to prevent broken triangles. Increase the limit or use a lower-poly physics mesh.");
                        welded = false;
                        return new List<Float3>();
                    }

                    particleIndex = positions.Count;
                    positions.Add(p);

                    if (!lookup.TryGetValue(baseKey, out List<int>? candidates))
                    {
                        candidates = new List<int>(1);
                        lookup.Add(baseKey, candidates);
                    }
                    candidates.Add(particleIndex);
                }

                binding.VertexToParticle[i] = particleIndex;
            }
        }

        welded = positions.Count < totalVertices;
        return positions;
    }

    private void CreatePhysicsVertices(List<Float3> positions)
    {
        if (softBody == null)
            return;

        float maxY = float.MinValue;
        float minY = float.MaxValue;

        foreach (Float3 p in positions)
        {
            maxY = MathF.Max(maxY, p.Y);
            minY = MathF.Min(minY, p.Y);
        }

        float pinThreshold = maxY - MathF.Max(0.001f, (maxY - minY) * pinTopEdgeFraction);
        World world = softBody.World;
        float inverseMass = 1f / MathF.Max(vertexMass, 0.001f);

        for (int i = 0; i < positions.Count; i++)
        {
            Float3 p = positions[i];
            RigidBody vertex = world.CreateRigidBody();
            vertex.SetMassInertia(JSymmetricMatrix.Zero, inverseMass, true);
            vertex.Position = new JVector(p.X, p.Y, p.Z);
            vertex.Damping = (damping, damping);
            vertex.MotionType = MotionType.Dynamic;
            softBody.Vertices.Add(vertex);

            bool shouldPin = type == SoftBodyType.Advanced && pinTopEdge && p.Y >= pinThreshold;
            if (!shouldPin)
                continue;

            RigidBody anchor = world.CreateRigidBody();
            anchor.Position = vertex.Position;
            anchor.MotionType = MotionType.Static;
            pinAnchorBodies.Add(anchor);

            SpringConstraint pin = world.CreateConstraint<SpringConstraint>(vertex, anchor);
            pin.Initialize(vertex.Position, anchor.Position);
            pin.Softness = 0.0001f;
            pin.Bias = 0.35f;
            softBody.AddSpring(pin);
        }
    }

    private List<TriangleInfo> BuildTriangles()
    {
        List<TriangleInfo> triangles = new();
        HashSet<TriangleKey> unique = new();

        foreach (MeshBinding binding in meshBindings)
        {
            Mesh mesh = binding.OriginalMesh;
            uint[] indices = mesh.Indices;

            for (int subIndex = 0; subIndex < mesh.SubMeshCount; subIndex++)
            {
                SubMeshDescriptor sub = mesh.GetSubMesh(subIndex);
                int start = Math.Clamp(sub.IndexStart, 0, indices.Length);
                int end = (int)Math.Clamp((long)sub.IndexStart + sub.IndexCount, start, indices.Length);
                int count = end - start;

                switch (sub.Topology)
                {
                    case Topology.Triangles:
                        for (int i = start; i + 2 < end; i += 3)
                            TryAddTriangle(triangles, unique, binding, indices[i], indices[i + 1], indices[i + 2]);
                        break;

                    case Topology.TriangleFan:
                        for (int i = 1; i + 1 < count; i++)
                            TryAddTriangle(triangles, unique, binding, indices[start], indices[start + i], indices[start + i + 1]);
                        break;

                    case Topology.Quads:
                        for (int i = 0; i + 3 < count; i += 4)
                        {
                            TryAddTriangle(triangles, unique, binding,
                                indices[start + i], indices[start + i + 1], indices[start + i + 2]);
                            TryAddTriangle(triangles, unique, binding,
                                indices[start + i], indices[start + i + 2], indices[start + i + 3]);
                        }
                        break;

                    default:
                        Debug.LogWarning(
                            $"[SoftBody3D] Unsupported topology '{sub.Topology}' in submesh {subIndex}. This submesh was skipped.");
                        break;
                }
            }
        }

        return triangles;
    }

    private void TryAddTriangle(
        List<TriangleInfo> triangles,
        HashSet<TriangleKey> unique,
        MeshBinding binding,
        uint renderA,
        uint renderB,
        uint renderC)
    {
        if (renderA >= (uint)binding.VertexToParticle.Length ||
            renderB >= (uint)binding.VertexToParticle.Length ||
            renderC >= (uint)binding.VertexToParticle.Length)
            return;

        int a = binding.VertexToParticle[(int)renderA];
        int b = binding.VertexToParticle[(int)renderB];
        int c = binding.VertexToParticle[(int)renderC];

        // Welding can collapse a triangle. Such a face must not enter the physics topology.
        if (a == b || b == c || c == a)
            return;

        JVector pa = softBody!.Vertices[a].Position;
        JVector pb = softBody.Vertices[b].Position;
        JVector pc = softBody.Vertices[c].Position;
        float areaVectorSquared = (float)(((pb - pa) % (pc - pa)).LengthSquared());

        if (!float.IsFinite(areaVectorSquared) || areaVectorSquared <= 1e-12f)
            return;

        if (unique.Add(new TriangleKey(a, b, c)))
            triangles.Add(new TriangleInfo(a, b, c));
    }

    private static Dictionary<EdgeKey, List<EdgeUse>> BuildEdgeMap(List<TriangleInfo> triangles)
    {
        Dictionary<EdgeKey, List<EdgeUse>> edges = new();

        for (int i = 0; i < triangles.Count; i++)
        {
            TriangleInfo t = triangles[i];
            AddEdgeUse(edges, t.A, t.B, i, t.C);
            AddEdgeUse(edges, t.B, t.C, i, t.A);
            AddEdgeUse(edges, t.C, t.A, i, t.B);
        }

        return edges;
    }

    private static void AddEdgeUse(
        Dictionary<EdgeKey, List<EdgeUse>> edges,
        int from,
        int to,
        int triangleIndex,
        int opposite)
    {
        EdgeKey key = new(from, to);
        if (!edges.TryGetValue(key, out List<EdgeUse>? uses))
        {
            uses = new List<EdgeUse>(2);
            edges.Add(key, uses);
        }

        uses.Add(new EdgeUse(triangleIndex, opposite, from < to));
    }

    private List<SurfaceTriangle> OrientTriangleComponents(
        List<TriangleInfo> triangles,
        Dictionary<EdgeKey, List<EdgeUse>> edgeMap,
        out double totalRestVolume)
    {
        totalRestVolume = 0.0;
        List<SurfaceTriangle> pressureTriangles = new();

        List<(int Other, bool SameDirection)>[] adjacency = new List<(int Other, bool SameDirection)>[triangles.Count];
        for (int i = 0; i < adjacency.Length; i++)
            adjacency[i] = new List<(int Other, bool SameDirection)>(3);

        foreach (List<EdgeUse> uses in edgeMap.Values)
        {
            if (uses.Count < 2)
                continue;

            EdgeUse first = uses[0];
            for (int i = 1; i < uses.Count; i++)
            {
                EdgeUse other = uses[i];
                bool sameDirection = first.Forward == other.Forward;
                adjacency[first.TriangleIndex].Add((other.TriangleIndex, sameDirection));
                adjacency[other.TriangleIndex].Add((first.TriangleIndex, sameDirection));
            }
        }

        bool[] visited = new bool[triangles.Count];
        bool[] flip = new bool[triangles.Count];
        Queue<int> queue = new();

        for (int start = 0; start < triangles.Count; start++)
        {
            if (visited[start])
                continue;

            List<int> component = new();
            bool closed = true;
            bool orientable = true;

            visited[start] = true;
            flip[start] = false;
            queue.Enqueue(start);

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                component.Add(current);
                TriangleInfo t = triangles[current];

                if (edgeMap[new EdgeKey(t.A, t.B)].Count != 2 ||
                    edgeMap[new EdgeKey(t.B, t.C)].Count != 2 ||
                    edgeMap[new EdgeKey(t.C, t.A)].Count != 2)
                    closed = false;

                foreach ((int other, bool sameDirection) in adjacency[current])
                {
                    bool requiredFlip = flip[current] ^ sameDirection;
                    if (!visited[other])
                    {
                        visited[other] = true;
                        flip[other] = requiredFlip;
                        queue.Enqueue(other);
                    }
                    else if (flip[other] != requiredFlip)
                    {
                        orientable = false;
                    }
                }
            }

            foreach (int triangleIndex in component)
            {
                if (flip[triangleIndex])
                    FlipTriangle(triangles[triangleIndex]);
            }

            if (!closed || !orientable)
                continue;

            double volume = CalculateComponentVolume(component, triangles);
            if (Math.Abs(volume) <= 1e-8)
                continue;

            if (volume < 0.0)
            {
                foreach (int triangleIndex in component)
                    FlipTriangle(triangles[triangleIndex]);
                volume = -volume;
            }

            totalRestVolume += volume;
            foreach (int triangleIndex in component)
            {
                TriangleInfo t = triangles[triangleIndex];
                pressureTriangles.Add(new SurfaceTriangle(t.A, t.B, t.C));
            }
        }

        return pressureTriangles;
    }

    private List<AreaTriangle> BuildAreaSurface(List<TriangleInfo> triangles)
    {
        List<AreaTriangle> result = new(triangles.Count);
        if (softBody == null)
            return result;

        foreach (TriangleInfo triangle in triangles)
        {
            JVector a = softBody.Vertices[triangle.A].Position;
            JVector b = softBody.Vertices[triangle.B].Position;
            JVector c = softBody.Vertices[triangle.C].Position;
            float restArea = 0.5f * MathF.Sqrt((float)(((b - a) % (c - a)).LengthSquared()));

            if (float.IsFinite(restArea) && restArea > 1e-8f)
                result.Add(new AreaTriangle(triangle.A, triangle.B, triangle.C, restArea));
        }

        return result;
    }

    private double CalculateComponentVolume(List<int> component, List<TriangleInfo> triangles)
    {
        if (component.Count == 0 || softBody == null)
            return 0.0;

        JVector origin = softBody.Vertices[triangles[component[0]].A].Position;
        double volume6 = 0.0;

        foreach (int index in component)
        {
            TriangleInfo t = triangles[index];
            JVector a = softBody.Vertices[t.A].Position - origin;
            JVector b = softBody.Vertices[t.B].Position - origin;
            JVector c = softBody.Vertices[t.C].Position - origin;
            volume6 += Dot(a, b % c);
        }

        return volume6 / 6.0;
    }

    private static double Dot(JVector a, JVector b)
        => (double)a.X * b.X + (double)a.Y * b.Y + (double)a.Z * b.Z;

    private static void FlipTriangle(TriangleInfo triangle)
        => (triangle.B, triangle.C) = (triangle.C, triangle.B);

    private void CreateStructuralSprings(Dictionary<EdgeKey, List<EdgeUse>> edgeMap)
    {
        createdSpringEdges.Clear();

        foreach (EdgeKey edge in edgeMap.Keys)
            AddSpring(edge.A, edge.B, springSoftness, springBias);
    }

    private void CreateBendingSprings(Dictionary<EdgeKey, List<EdgeUse>> edgeMap)
    {
        if (softBody == null)
            return;

        foreach (List<EdgeUse> uses in edgeMap.Values)
        {
            if (uses.Count != 2)
                continue;

            int oppositeA = uses[0].Opposite;
            int oppositeB = uses[1].Opposite;
            if (oppositeA == oppositeB)
                continue;

            AddSpring(oppositeA, oppositeB, bendingSoftness, springBias * 0.65f);
        }
    }

    private void AddSpring(int indexA, int indexB, float softness, float bias)
    {
        if (softBody == null || indexA == indexB)
            return;

        EdgeKey key = new(indexA, indexB);
        if (!createdSpringEdges.Add(key))
            return;

        RigidBody a = softBody.Vertices[indexA];
        RigidBody b = softBody.Vertices[indexB];

        SpringConstraint spring = softBody.World.CreateConstraint<SpringConstraint>(a, b);
        spring.Initialize(a.Position, b.Position);
        spring.Softness = MathF.Max(0f, softness);
        spring.Bias = Math.Clamp(bias, 0f, 1f);
        softBody.AddSpring(spring);
    }

    private void CreateCollisionTriangles(List<TriangleInfo> triangles)
    {
        if (softBody == null || triangles.Count == 0)
            return;

        // One collision proxy per valid physics triangle. Never stride-sample this list:
        // skipping triangles creates holes that allow rigid bodies to tunnel through the mesh.
        foreach (TriangleInfo t in triangles)
        {
            SoftBodyTriangle shape = new(
                softBody,
                softBody.Vertices[t.A],
                softBody.Vertices[t.B],
                softBody.Vertices[t.C]);

            shape.Thickness = collisionThickness;
            softBody.AddShape(shape);
        }
    }

    private static Mesh CloneMesh(Mesh source)
    {
        Mesh clone = new()
        {
            IndexFormat = source.IndexFormat,
            MeshTopology = source.MeshTopology,
            Vertices = (Float3[])source.Vertices.Clone(),
            Indices = (uint[])source.Indices.Clone()
        };

        if (source.HasNormals) clone.Normals = (Float3[])source.Normals.Clone();
        if (source.HasTangents) clone.Tangents = (Float4[])source.Tangents.Clone();
        if (source.HasColors) clone.Colors = (Color[])source.Colors.Clone();
        if (source.HasColors32) clone.Colors32 = (Color32[])source.Colors32.Clone();
        if (source.HasUV) clone.UV = (Float2[])source.UV.Clone();
        if (source.HasUV2) clone.UV2 = (Float2[])source.UV2.Clone();
        if (source.HasBoneIndices) clone.BoneIndices = (Float4[])source.BoneIndices.Clone();
        if (source.HasBoneWeights) clone.BoneWeights = (Float4[])source.BoneWeights.Clone();

        clone.SetSubMeshCount(source.SubMeshCount);
        for (int i = 0; i < source.SubMeshCount; i++)
            clone.SetSubMesh(i, source.GetSubMesh(i));

        if (source.HasBlendShapes)
            clone.BlendShapes = CloneBlendShapes(source.BlendShapes);

        clone.RecalculateBounds();
        return clone;
    }

    private static BlendShape[] CloneBlendShapes(BlendShape[] source)
    {
        BlendShape[] result = new BlendShape[source.Length];

        for (int i = 0; i < source.Length; i++)
        {
            BlendShape original = source[i];
            BlendShape copy = new()
            {
                Name = original.Name,
                Frames = new BlendShapeFrame[original.Frames.Length]
            };

            for (int f = 0; f < original.Frames.Length; f++)
            {
                BlendShapeFrame frame = original.Frames[f];
                copy.Frames[f] = new BlendShapeFrame
                {
                    Weight = frame.Weight,
                    DeltaVertices = (Float3[])frame.DeltaVertices.Clone(),
                    DeltaNormals = frame.DeltaNormals == null ? null : (Float3[])frame.DeltaNormals.Clone(),
                    DeltaTangents = frame.DeltaTangents == null ? null : (Float3[])frame.DeltaTangents.Clone()
                };
            }

            result[i] = copy;
        }

        return result;
    }

    public override void Update()
    {
        if (softBody == null || softBody.Vertices.Count == 0)
            return;

        frameCounter++;
        if (frameCounter % visualUpdateEveryNFrames != 0)
            return;

        UpdateVisualMeshes();
    }

    private void UpdateVisualMeshes()
    {
        if (softBody == null || softBody.Vertices.Count == 0)
            return;

        foreach (MeshBinding binding in meshBindings)
        {
            if (binding.Renderer.IsNotValid() || binding.DeformedMesh.IsNotValid() ||
                binding.VertexToParticle.Length != binding.UpdateBuffer.Length)
                continue;

            // Validate the complete mapping before changing the render mesh. A partially
            // invalid map would leave some vertices at old positions and create long spikes.
            bool validMapping = true;
            for (int i = 0; i < binding.VertexToParticle.Length; i++)
            {
                if ((uint)binding.VertexToParticle[i] >= (uint)softBody.Vertices.Count)
                {
                    validMapping = false;
                    break;
                }
            }

            if (!validMapping)
            {
                Debug.LogWarning("[SoftBody3D] Invalid vertex-to-particle mapping detected. This mesh update was skipped.");
                continue;
            }

            Float4x4 worldToLocal = binding.Renderer.Transform.WorldToLocalMatrix;
            Float3[] updatedVertices = binding.UpdateBuffer;

            for (int i = 0; i < updatedVertices.Length; i++)
            {
                int particleIndex = binding.VertexToParticle[i];
                JVector p = softBody.Vertices[particleIndex].Position;
                Float3 worldPosition = new((float)p.X, (float)p.Y, (float)p.Z);
                updatedVertices[i] = Float4x4.TransformPoint(worldPosition, worldToLocal);
            }

            // Refresh all geometry data together. Deferring normals/bounds for several frames
            // made the deformed surface appear to twist, inflate, or lag behind the physics.
            binding.DeformedMesh.Vertices = updatedVertices;
            binding.DeformedMesh.RecalculateNormals();
            binding.DeformedMesh.RecalculateBounds();
            binding.Renderer.MarkVisuallyDirty();
        }
    }

    private void DestroySoftBody()
    {
        if (softBody != null)
        {
            World world = softBody.World;
            softBody.Destroy();
            softBody = null;

            // Anchors are independent static bodies, so remove them after SoftBody.Destroy().
            if (!world.IsDisposed)
            {
                foreach (RigidBody anchor in pinAnchorBodies)
                {
                    if (anchor.IsValid)
                        world.Remove(anchor);
                }
            }
        }

        pinAnchorBodies.Clear();
        createdSpringEdges.Clear();
    }

    private void RestoreOriginalMeshes()
    {
        foreach (MeshBinding binding in meshBindings)
        {
            if (binding.Renderer.IsValid())
            {
                binding.Renderer.Mesh = binding.OriginalMesh;
                binding.Renderer.MarkVisuallyDirty();
            }
        }

        meshBindings.Clear();
    }
}
