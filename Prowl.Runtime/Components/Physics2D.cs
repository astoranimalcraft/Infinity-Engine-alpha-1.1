// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Infinity.Runtime.Resources;
using Infinity.Vector;

namespace Infinity.Runtime;

/// <summary>A 2D shape used by <see cref="Physics2D"/> queries. The editor draws and moves these in the XY plane.</summary>
public abstract class Collider2D : Component
{
    public bool IsTrigger;
    public Float2 Offset;

    /// <summary>World-space minimum corner of the collider's bounding box.</summary>
    public abstract Float2 BoundsMin { get; }

    /// <summary>World-space maximum corner of the collider's bounding box.</summary>
    public abstract Float2 BoundsMax { get; }

    public Float2 Center => new Float2(Transform.Position.X, Transform.Position.Y) + Offset;

    public virtual bool Contains(Float2 point)
    {
        Float2 min = BoundsMin, max = BoundsMax;
        return point.X >= min.X && point.X <= max.X && point.Y >= min.Y && point.Y <= max.Y;
    }

    public virtual bool OverlapsCircle(Float2 center, float radius)
    {
        Float2 min = BoundsMin, max = BoundsMax;
        float x = Math.Clamp(center.X, min.X, max.X);
        float y = Math.Clamp(center.Y, min.Y, max.Y);
        return Float2.DistanceSquared(center, new Float2(x, y)) <= radius * radius;
    }

    public virtual bool OverlapsBox(Float2 min, Float2 max)
    {
        Float2 a = BoundsMin, b = BoundsMax;
        return a.X <= max.X && b.X >= min.X && a.Y <= max.Y && b.Y >= min.Y;
    }

    public virtual bool Raycast(Float2 origin, Float2 direction, float distance, out float hitDistance)
    {
        hitDistance = 0f;
        Float2 min = BoundsMin, max = BoundsMax;
        if (MathF.Abs(direction.X) < 1e-6f)
        {
            if (origin.X < min.X || origin.X > max.X) return false;
        }
        if (MathF.Abs(direction.Y) < 1e-6f)
        {
            if (origin.Y < min.Y || origin.Y > max.Y) return false;
        }

        float tmin = 0f, tmax = distance;
        for (int axis = 0; axis < 2; axis++)
        {
            float o = axis == 0 ? origin.X : origin.Y;
            float d = axis == 0 ? direction.X : direction.Y;
            float lo = axis == 0 ? min.X : min.Y;
            float hi = axis == 0 ? max.X : max.Y;
            if (MathF.Abs(d) < 1e-6f) continue;
            float t1 = (lo - o) / d, t2 = (hi - o) / d;
            if (t1 > t2) (t1, t2) = (t2, t1);
            tmin = MathF.Max(tmin, t1);
            tmax = MathF.Min(tmax, t2);
            if (tmin > tmax) return false;
        }

        hitDistance = tmin;
        return true;
    }
}

/// <summary>An axis-aligned box collider in the XY plane. Rotation is intentionally not applied to the bounds.</summary>
[AddComponentMenu("2D/Box Collider 2D")]
[ComponentIcon("")]
public sealed class BoxCollider2D : Collider2D
{
    public Float2 Size = new(1f, 1f);

    public override Float2 BoundsMin
    {
        get
        {
            Float3 scale = Transform.LossyScale;
            Float2 half = new(MathF.Abs(Size.X * scale.X) * 0.5f, MathF.Abs(Size.Y * scale.Y) * 0.5f);
            Float2 center = Center;
            return new Float2(center.X - half.X, center.Y - half.Y);
        }
    }

    public override Float2 BoundsMax
    {
        get
        {
            Float3 scale = Transform.LossyScale;
            Float2 half = new(MathF.Abs(Size.X * scale.X) * 0.5f, MathF.Abs(Size.Y * scale.Y) * 0.5f);
            Float2 center = Center;
            return new Float2(center.X + half.X, center.Y + half.Y);
        }
    }
}

/// <summary>A circle collider in the XY plane, scaled by the transform's largest X/Y scale.</summary>
[AddComponentMenu("2D/Circle Collider 2D")]
[ComponentIcon("")]
public sealed class CircleCollider2D : Collider2D
{
    public float Radius = 0.5f;

    public float WorldRadius
    {
        get
        {
            Float3 scale = Transform.LossyScale;
            return MathF.Abs(Radius) * MathF.Max(MathF.Abs(scale.X), MathF.Abs(scale.Y));
        }
    }

    public override Float2 BoundsMin => Center - new Float2(WorldRadius, WorldRadius);
    public override Float2 BoundsMax => Center + new Float2(WorldRadius, WorldRadius);

    public override bool Contains(Float2 point) => Float2.DistanceSquared(Center, point) <= WorldRadius * WorldRadius;

    public override bool OverlapsCircle(Float2 center, float radius)
    {
        float r = radius + WorldRadius;
        return Float2.DistanceSquared(center, Center) <= r * r;
    }

    public override bool OverlapsBox(Float2 min, Float2 max)
    {
        float x = Math.Clamp(Center.X, min.X, max.X);
        float y = Math.Clamp(Center.Y, min.Y, max.Y);
        return Float2.DistanceSquared(Center, new Float2(x, y)) <= WorldRadius * WorldRadius;
    }

    public override bool Raycast(Float2 origin, Float2 direction, float distance, out float hitDistance)
    {
        hitDistance = 0f;
        Float2 toSphere = origin - Center;
        float a = Float2.Dot(direction, direction);
        float b = 2f * Float2.Dot(toSphere, direction);
        float c = Float2.Dot(toSphere, toSphere) - WorldRadius * WorldRadius;
        float disc = b * b - 4f * a * c;
        if (disc < 0f) return false;
        float root = MathF.Sqrt(disc);
        float t = (-b - root) / (2f * a);
        if (t < 0f) t = (-b + root) / (2f * a);
        if (t < 0f || t > distance) return false;
        hitDistance = t;
        return true;
    }
}

/// <summary>The result of a <see cref="Physics2D"/> ray or shape query.</summary>
public readonly struct RaycastHit2D
{
    public readonly Collider2D Collider;
    public readonly Float2 Point;
    public readonly Float2 Normal;
    public readonly float Distance;

    public RaycastHit2D(Collider2D collider, Float2 point, Float2 normal, float distance)
    {
        Collider = collider;
        Point = point;
        Normal = normal;
        Distance = distance;
    }

    public GameObject GameObject => Collider.GameObject;
}

/// <summary>
/// Lightweight 2D queries over <see cref="Collider2D"/> components in a scene. There is no solver:
/// <see cref="Rigidbody2D"/> owns integration and the query API lets gameplay resolve its own contacts,
/// which keeps the 2D layer deterministic and usable from editor tools as well.
/// </summary>
public static class Physics2D
{
    public static Float2 Gravity = new(0f, -9.81f);

    private static SceneInstance? SceneOrMain(SceneInstance? scene)
        => scene.IsValid() ? scene : (SceneInstance.HasMain ? SceneInstance.Main : null);

    public static Collider2D[] OverlapCircle(Float2 center, float radius, SceneInstance? scene = null)
    {
        SceneInstance? world = SceneOrMain(scene);
        if (world.IsNotValid()) return [];
        var result = new List<Collider2D>();
        foreach (Collider2D collider in world!.GetAll<Collider2D>())
            if (collider.IsValid() && !collider.IsTrigger && collider.OverlapsCircle(center, radius))
                result.Add(collider);
        return [.. result];
    }

    public static Collider2D[] OverlapBox(Float2 center, Float2 size, SceneInstance? scene = null)
    {
        SceneInstance? world = SceneOrMain(scene);
        if (world.IsNotValid()) return [];
        Float2 half = new(MathF.Abs(size.X) * 0.5f, MathF.Abs(size.Y) * 0.5f);
        Float2 min = center - half, max = center + half;
        var result = new List<Collider2D>();
        foreach (Collider2D collider in world!.GetAll<Collider2D>())
            if (collider.IsValid() && !collider.IsTrigger && collider.OverlapsBox(min, max))
                result.Add(collider);
        return [.. result];
    }

    public static Collider2D? OverlapPoint(Float2 point, SceneInstance? scene = null)
    {
        SceneInstance? world = SceneOrMain(scene);
        if (world.IsNotValid()) return null;
        foreach (Collider2D collider in world!.GetAll<Collider2D>())
            if (collider.IsValid() && !collider.IsTrigger && collider.Contains(point))
                return collider;
        return null;
    }

    public static bool Raycast(Float2 origin, Float2 direction, float distance, out RaycastHit2D hit, SceneInstance? scene = null)
    {
        hit = default;
        SceneInstance? world = SceneOrMain(scene);
        if (world.IsNotValid()) return false;
        Float2 dir = Float2.Normalize(direction);
        float nearest = distance;
        Collider2D? best = null;
        foreach (Collider2D collider in world!.GetAll<Collider2D>())
        {
            if (collider.IsNotValid() || collider.IsTrigger) continue;
            if (!collider.Raycast(origin, dir, distance, out float t) || t >= nearest) continue;
            nearest = t;
            best = collider;
        }
        if (best.IsNotValid()) return false;
        hit = new RaycastHit2D(best!, origin + dir * nearest, -dir, nearest);
        return true;
    }

    public static bool Raycast(Float2 origin, Float2 direction, float distance, SceneInstance? scene = null)
        => Raycast(origin, direction, distance, out _, scene);
}
