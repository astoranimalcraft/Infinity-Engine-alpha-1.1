// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Infinity.Vector;

namespace Infinity.Runtime;

/// <summary>
/// Moves a GameObject in the XY plane with velocity and gravity. Collision response is deliberately left
/// to gameplay through <see cref="Physics2D"/> queries, so this works the same in 2D and in a 3D world
/// viewed by an orthographic camera.
/// </summary>
[AddComponentMenu("2D/Rigidbody 2D")]
[ComponentIcon("")]
public sealed class Rigidbody2D : Component
{
    public Float2 LinearVelocity;
    public float GravityScale = 1f;
    public bool UseGravity = true;
    public bool Simulate = true;

    /// <summary>Multiplier applied by <see cref="AddForce"/> and <see cref="AddExplosionForce"/>.</summary>
    public float Mass = 1f;

    /// <summary>Drag per second, 0 keeps the current velocity.</summary>
    public float LinearDamping;

    public void AddForce(Float2 force)
        => LinearVelocity += force / MathF.Max(0.0001f, Mass) * Time.DeltaTime;

    public void AddImpulse(Float2 impulse)
        => LinearVelocity += impulse / MathF.Max(0.0001f, Mass);

    /// <summary>Moves by <paramref name="delta"/> immediately, without touching the velocity.</summary>
    public void Move(Float2 delta)
    {
        Float3 p = Transform.Position;
        Transform.Position = new Float3(p.X + delta.X, p.Y + delta.Y, p.Z);
    }

    public void SetVelocity(Float2 velocity) => LinearVelocity = velocity;
    public void Stop() => LinearVelocity = Float2.Zero;

    /// <summary>Pushes this body away from <paramref name="center"/> with a falloff over <paramref name="radius"/>.</summary>
    public void AddExplosionForce(float strength, Float2 center, float radius)
    {
        Float2 position = new(Transform.Position.X, Transform.Position.Y);
        Float2 offset = position - center;
        float distance = Float2.Length(offset);
        if (distance >= radius || distance < 1e-4f) return;
        AddImpulse(Float2.Normalize(offset) * (strength * (1f - distance / radius)));
    }

    public override void FixedUpdate()
    {
        if (!Simulate) return;

        if (UseGravity)
            LinearVelocity += Physics2D.Gravity * (GravityScale * Time.FixedDeltaTime);

        if (LinearDamping > 0f)
            LinearVelocity *= MathF.Max(0f, 1f - LinearDamping * Time.FixedDeltaTime);

        Move(LinearVelocity * Time.FixedDeltaTime);
    }
}
