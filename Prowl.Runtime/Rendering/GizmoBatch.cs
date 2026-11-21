using System;
using System.Collections.Generic;

using Prowl.Graphite.Rendering;
using Prowl.Vector;

using GTexture = Prowl.Graphite.Texture;

namespace Prowl.Runtime;

/// <summary>One line of a <see cref="GizmoBatch"/>, already in world space.</summary>
public readonly record struct GizmoLine(Float3 A, Float3 B, Color Color);

/// <summary>One filled triangle of a <see cref="GizmoBatch"/>, already in world space.</summary>
public readonly record struct GizmoTriangle(Float3 A, Float3 B, Float3 C, Color Color);

/// <summary>A camera facing icon of a <see cref="GizmoBatch"/>, its centre already in world space.</summary>
public readonly record struct GizmoIcon(Float3 Center, GTexture Texture, float Scale, Color Color);

/// <summary>
/// A frame of gizmos. <see cref="Debug"/>'s draw calls append to one of these and the renderer draws it once the scene
/// hands it over. Shapes are flattened to lines and triangles as they are added, so drawing a batch costs one upload
/// and no shape walking.
/// </summary>
public sealed class GizmoBatch
{
    private readonly List<GizmoLine> _lines = [];
    private readonly List<GizmoTriangle> _triangles = [];
    private readonly List<GizmoIcon> _icons = [];
    private readonly Stack<Float4x4> _transforms = new();

    /// <summary>Whether the renderer draws this batch. A batch being filled is hidden until the frame hands it over.</summary>
    public bool Visible { get; set; }

    /// <summary>The world whose views draw this batch, or null for none.</summary>
    public RenderWorld? World { get; set; }

    /// <summary>The line count, which is what the hitbox pass walks.</summary>
    public int PrimitiveCount => _lines.Count;

    public IReadOnlyList<GizmoLine> Lines => _lines;
    public IReadOnlyList<GizmoTriangle> Triangles => _triangles;
    public IReadOnlyList<GizmoIcon> Icons => _icons;

    /// <summary>Whether anything was drawn into this batch.</summary>
    public bool IsEmpty => _lines.Count == 0 && _triangles.Count == 0 && _icons.Count == 0;

    // ---- transforms ---------------------------------------------------------------------------------

    /// <summary>Transforms what is drawn until <see cref="Pop"/> by a matrix, after the ones pushed before it.</summary>
    public void Push(Float4x4 matrix)
        => _transforms.Push(_transforms.Count > 0 ? _transforms.Peek() * matrix : matrix);

    public void Pop()
    {
        if (_transforms.Count > 0) _transforms.Pop();
    }

    private Float3 At(Float3 point)
        => _transforms.Count > 0 ? Float4x4.TransformPoint(point, _transforms.Peek()) : point;

    public void Clear()
    {
        _lines.Clear();
        _triangles.Clear();
        _icons.Clear();
        _transforms.Clear();
    }

    public bool TryGetLine(int index, out Float3 a, out Float3 b)
    {
        if ((uint)index >= (uint)_lines.Count)
        {
            a = b = Float3.Zero;
            return false;
        }

        GizmoLine line = _lines[index];
        (a, b) = (line.A, line.B);
        return true;
    }

    // ---- primitives ---------------------------------------------------------------------------------

    public void Line(Float3 a, Float3 b, Color color) => _lines.Add(new GizmoLine(At(a), At(b), color));

    public void Triangle(Float3 a, Float3 b, Float3 c, Color color)
        => _triangles.Add(new GizmoTriangle(At(a), At(b), At(c), color));

    /// <summary>A camera facing icon. Its centre is taken as given, since the caller has already placed it.</summary>
    public void WorldIcon(Float3 center, GTexture texture, float scale, Color color)
        => _icons.Add(new GizmoIcon(center, texture, scale, color));

    // ---- wire shapes --------------------------------------------------------------------------------

    public void Box(Float3 center, Float3 halfExtents, Quaternion rotation, Color color)
    {
        Span<Float3> corners = stackalloc Float3[8];
        Corners(center, halfExtents, rotation, corners);

        for (int i = 0; i < 8; i++)
            for (int bit = 1; bit < 8; bit <<= 1)
                if ((i & bit) == 0)
                    Line(corners[i], corners[i | bit], color);
    }

    public void SolidBox(Float3 center, Float3 halfExtents, Quaternion rotation, Color color)
    {
        Span<Float3> corners = stackalloc Float3[8];
        Corners(center, halfExtents, rotation, corners);

        ReadOnlySpan<int> faces =
        [
            0, 1, 3, 2, 4, 6, 7, 5, 0, 2, 6, 4,
            1, 5, 7, 3, 0, 4, 5, 1, 2, 3, 7, 6,
        ];

        for (int f = 0; f < faces.Length; f += 4)
        {
            Triangle(corners[faces[f]], corners[faces[f + 1]], corners[faces[f + 2]], color);
            Triangle(corners[faces[f]], corners[faces[f + 2]], corners[faces[f + 3]], color);
        }
    }

    private static void Corners(Float3 center, Float3 halfExtents, Quaternion rotation, Span<Float3> corners)
    {
        for (int i = 0; i < 8; i++)
        {
            var offset = new Float3(
                (i & 1) == 0 ? -halfExtents.X : halfExtents.X,
                (i & 2) == 0 ? -halfExtents.Y : halfExtents.Y,
                (i & 4) == 0 ? -halfExtents.Z : halfExtents.Z);
            corners[i] = center + rotation * offset;
        }
    }

    public void Circle(Float3 center, Float3 normal, float radius, Color color, int segments = 32)
        => Ring(center, normal, radius, color, segments, connect: true);

    public void Sphere(Float3 center, float radius, Color color, int segments = 32)
    {
        Circle(center, Float3.UnitY, radius, color, segments);
        Circle(center, Float3.UnitX, radius, color, segments);
        Circle(center, Float3.UnitZ, radius, color, segments);
    }

    public void SolidSphere(Float3 center, float radius, Color color, int segments = 16)
    {
        int rings = Math.Max(2, segments / 2);
        for (int r = 0; r < rings; r++)
        {
            float a0 = MathF.PI * r / rings, a1 = MathF.PI * (r + 1) / rings;
            float y0 = MathF.Cos(a0) * radius, r0 = MathF.Sin(a0) * radius;
            float y1 = MathF.Cos(a1) * radius, r1 = MathF.Sin(a1) * radius;

            for (int s = 0; s < segments; s++)
            {
                float t0 = MathF.Tau * s / segments, t1 = MathF.Tau * (s + 1) / segments;
                Float3 p0 = center + new Float3(MathF.Cos(t0) * r0, y0, MathF.Sin(t0) * r0);
                Float3 p1 = center + new Float3(MathF.Cos(t1) * r0, y0, MathF.Sin(t1) * r0);
                Float3 p2 = center + new Float3(MathF.Cos(t1) * r1, y1, MathF.Sin(t1) * r1);
                Float3 p3 = center + new Float3(MathF.Cos(t0) * r1, y1, MathF.Sin(t0) * r1);

                Triangle(p0, p1, p2, color);
                Triangle(p0, p2, p3, color);
            }
        }
    }

    /// <summary>Part of a circle about <paramref name="normal"/>, starting at the <paramref name="from"/> direction.</summary>
    public void Arc(Float3 center, Float3 normal, Float3 from, float sweepRadians, float radius, Color color, int segments = 24)
    {
        Float3 axis = Float3.Normalize(normal);
        if (Float3.LengthSquared(axis) <= 0f) axis = Float3.UnitY;

        Float3 start = from - axis * Dot(from, axis);
        start = Float3.LengthSquared(start) <= 0f ? Perpendicular(axis) : Float3.Normalize(start);
        Float3 side = Float3.Cross(axis, start);

        int steps = Math.Max(2, segments);
        Float3 previous = center + start * radius;
        for (int i = 1; i <= steps; i++)
        {
            float angle = sweepRadians * i / steps;
            Float3 point = center + (start * MathF.Cos(angle) + side * MathF.Sin(angle)) * radius;
            Line(previous, point, color);
            previous = point;
        }
    }

    /// <summary>A cone with its apex at <paramref name="apex"/>, opening to <paramref name="halfAngle"/> from its axis.</summary>
    public void Cone(Float3 apex, Float3 axis, float length, float halfAngle, Color color, int segments = 32)
    {
        Float3 direction = Float3.Normalize(axis);
        if (Float3.LengthSquared(direction) <= 0f || length <= 0f) return;

        Float3 baseCenter = apex + direction * length;
        float radius = MathF.Tan(halfAngle) * length;

        Circle(baseCenter, direction, radius, color, segments);
        for (int i = 0; i < segments; i++)
            Line(apex, RingPoint(baseCenter, direction, radius, i, segments), color);
    }

    /// <summary>A cone whose <paramref name="slantLength"/> is measured along its surface rather than its axis.</summary>
    public void SlantCone(Float3 apex, Float3 axis, float halfAngle, float slantLength, Color color, int segments = 32)
    {
        Float3 direction = Float3.Normalize(axis);
        if (Float3.LengthSquared(direction) <= 0f || slantLength <= 0f) return;

        Float3 baseCenter = apex + direction * (MathF.Cos(halfAngle) * slantLength);
        float radius = MathF.Sin(halfAngle) * slantLength;

        Circle(baseCenter, direction, radius, color, segments);
        for (int i = 0; i < segments; i++)
            Line(apex, RingPoint(baseCenter, direction, radius, i, segments), color);
    }

    public void Cylinder(Float3 start, Float3 end, float radius, Color color, int segments = 32)
    {
        Float3 axis = end - start;
        Float3 direction = Float3.Normalize(axis);
        if (Float3.LengthSquared(direction) <= 0f) return;

        Circle(start, direction, radius, color, segments);
        Circle(end, direction, radius, color, segments);
        for (int i = 0; i < segments; i += Math.Max(1, segments / 4))
            Line(RingPoint(start, direction, radius, i, segments), RingPoint(end, direction, radius, i, segments), color);
    }

    public void Capsule(Float3 start, Float3 end, float radius, Color color, int segments = 32)
    {
        Cylinder(start, end, radius, color, segments);

        Float3 axis = Float3.Normalize(end - start);
        if (Float3.LengthSquared(axis) <= 0f) return;

        Arc(end, axis, Perpendicular(axis), MathF.PI, radius, color, segments / 2);
        Arc(start, -axis, Perpendicular(axis), MathF.PI, radius, color, segments / 2);
    }

    /// <summary>A line with a head at <paramref name="end"/>.</summary>
    public void Arrow(Float3 start, Float3 end, Color color)
    {
        Line(start, end, color);

        Float3 direction = Float3.Normalize(end - start);
        if (Float3.LengthSquared(direction) <= 0f) return;

        float length = Float3.Distance(start, end);
        float head = Math.Clamp(length * 0.2f, length * 0.05f, 0.5f);

        Float3 side = Float3.Normalize(Float3.Cross(direction, Perpendicular(direction)));
        Float3 back = end - direction * head;
        Line(end, back + side * (head * 0.5f), color);
        Line(end, back - side * (head * 0.5f), color);
    }

    /// <summary>An axis through a point, drawn both ways with a head on the positive side.</summary>
    public void AxisLine(Float3 center, Float3 direction, float length, Color color)
    {
        Float3 axis = Float3.Normalize(direction);
        if (Float3.LengthSquared(axis) <= 0f) return;

        Line(center - axis * length, center, color);
        Arrow(center, center + axis * length, color);
    }

    /// <summary>A bar across an axis, for marking a hard stop along it.</summary>
    public void CrossBar(Float3 position, Float3 axis, float size, Color color)
    {
        Debug.PerpendicularAxes(axis, out Float3 u, out Float3 v);
        Line(position - u * size, position + u * size, color);
        Line(position - v * size, position + v * size, color);
    }

    /// <summary>An angular band about <paramref name="normal"/>, measured from the <paramref name="zero"/> direction.</summary>
    public void ArcRange(Float3 center, Float3 normal, Float3 zero, float radius,
        float minRadians, float maxRadians, Color rangeColor, Color limitColor)
    {
        Float3 axis = Float3.Normalize(normal);
        if (Float3.LengthSquared(axis) <= 0f) return;

        Float3 from = Rotate(zero, axis, minRadians);
        Arc(center, axis, from, maxRadians - minRadians, radius, rangeColor, 24);

        Line(center, center + Float3.Normalize(from) * radius, limitColor);
        Line(center, center + Rotate(from, axis, maxRadians - minRadians) * radius, limitColor);
    }

    /// <summary>A range along an axis. Infinite ends run on for <paramref name="openLength"/> with no stop drawn.</summary>
    public void LinearRange(Float3 origin, Float3 direction, float minDistance, float maxDistance,
        float openLength, float stopSize, Color rangeColor, Color limitColor)
    {
        Float3 axis = Float3.Normalize(direction);
        if (Float3.LengthSquared(axis) <= 0f) return;

        bool minOpen = float.IsInfinity(minDistance), maxOpen = float.IsInfinity(maxDistance);
        Float3 start = minOpen ? origin - axis * openLength : origin + axis * minDistance;
        Float3 end = maxOpen ? origin + axis * openLength : origin + axis * maxDistance;

        Line(start, end, rangeColor);
        if (!minOpen) CrossBar(start, axis, stopSize, limitColor);
        if (!maxOpen) CrossBar(end, axis, stopSize, limitColor);
    }

    /// <summary>An arc with a head on the swept end, for showing a rotation direction.</summary>
    public void SpinArrow(Float3 center, Float3 normal, Float3 from, float radius, float sweepRadians, Color color)
    {
        Arc(center, normal, from, sweepRadians, radius, color, 24);

        Float3 axis = Float3.Normalize(normal);
        if (Float3.LengthSquared(axis) <= 0f) return;

        Float3 tip = center + Rotate(from, axis, sweepRadians) * radius;
        Float3 tangent = Float3.Normalize(Float3.Cross(axis, tip - center)) * (MathF.Sign(sweepRadians) * radius * 0.2f);
        Line(tip, tip - tangent + (tip - center) * 0.2f, color);
        Line(tip, tip - tangent - (tip - center) * 0.2f, color);
    }

    /// <summary>An orientation tripod, X/Y/Z in red/green/blue.</summary>
    public void Axes(Float3 center, Quaternion rotation, float scale)
    {
        Line(center, center + rotation * Float3.UnitX * scale, new Color(1f, 0.2f, 0.2f, 1f));
        Line(center, center + rotation * Float3.UnitY * scale, new Color(0.2f, 1f, 0.2f, 1f));
        Line(center, center + rotation * Float3.UnitZ * scale, new Color(0.3f, 0.5f, 1f, 1f));
    }

    // ---- helpers ------------------------------------------------------------------------------------

    private void Ring(Float3 center, Float3 normal, float radius, Color color, int segments, bool connect)
    {
        Float3 axis = Float3.Normalize(normal);
        if (Float3.LengthSquared(axis) <= 0f) axis = Float3.UnitY;

        int steps = Math.Max(3, segments);
        Float3 previous = RingPoint(center, axis, radius, 0, steps);
        for (int i = 1; i <= (connect ? steps : steps - 1); i++)
        {
            Float3 point = RingPoint(center, axis, radius, i % steps, steps);
            Line(previous, point, color);
            previous = point;
        }
    }

    private Float3 RingPoint(Float3 center, Float3 axis, float radius, int index, int segments)
    {
        Debug.PerpendicularAxes(axis, out Float3 u, out Float3 v);
        float angle = MathF.Tau * index / Math.Max(3, segments);
        return center + (u * MathF.Cos(angle) + v * MathF.Sin(angle)) * radius;
    }

    private static Float3 Perpendicular(Float3 axis)
    {
        Debug.PerpendicularAxes(axis, out Float3 u, out _);
        return u;
    }

    private static float Dot(Float3 a, Float3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    /// <summary>Rodrigues rotation of a vector about a unit axis.</summary>
    private static Float3 Rotate(Float3 vector, Float3 axis, float radians)
    {
        float cos = MathF.Cos(radians), sin = MathF.Sin(radians);
        return vector * cos + Float3.Cross(axis, vector) * sin + axis * (Dot(axis, vector) * (1f - cos));
    }
}
