// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Infinity.Vector;

namespace Infinity.Runtime;

/// <summary>How a billboard turns to face the camera.</summary>
public enum BillboardMode : byte
{
    /// <summary>Point the object's forward axis at the camera.</summary>
    FaceCamera,
    /// <summary>Copy the camera's rotation, which keeps a flat sprite parallel to the screen.</summary>
    CameraPlane,
    /// <summary>Face the camera while keeping a chosen world axis upright.</summary>
    AxisLocked,
}

/// <summary>Turns an object towards the active camera, the usual way to show a sprite or a health bar in 3D.</summary>
[AddComponentMenu("Rendering/Billboard")]
[ComponentDisplayName("Billboard")]
[ComponentIcon("\uf03e")]
public class Billboard : Component
{
    /// <summary>The camera to face. Empty uses <see cref="Camera.Main"/>.</summary>
    public Camera? TargetCamera;

    public BillboardMode Mode = BillboardMode.FaceCamera;

    /// <summary>The world up used by <see cref="BillboardMode.AxisLocked"/>.</summary>
    public Float3 UpAxis = Float3.UnitY;

    /// <summary>Turn the object away from the camera instead of towards it.</summary>
    public bool FaceAway;

    public override void LateUpdate() => Face();

    /// <summary>Turns the object now, without waiting for the next frame.</summary>
    public void Face()
    {
        Camera? camera = TargetCamera.IsValid() ? TargetCamera : Camera.Main;
        if (camera.IsNotValid()) return;

        Transform cameraTransform = camera!.Transform;
        if (Mode == BillboardMode.CameraPlane)
        {
            Transform.Rotation = FaceAway
                ? cameraTransform.Rotation * Quaternion.FromEuler(0f, 180f, 0f)
                : cameraTransform.Rotation;
            return;
        }

        Float3 target = FaceAway
            ? Transform.Position + (Transform.Position - cameraTransform.Position)
            : cameraTransform.Position;
        Transform.LookAt(target, Mode == BillboardMode.AxisLocked ? UpAxis : Float3.UnitY);
    }
}

/// <summary>Spins an object at a constant rate, for pickups, fans and simple environment motion.</summary>
[AddComponentMenu("Utility/Rotator")]
[ComponentDisplayName("Rotator")]
[ComponentIcon("\uf01e")]
public class Rotator : Component
{
    public Float3 Axis = Float3.UnitY;

    /// <summary>Degrees per second around <see cref="Axis"/>.</summary>
    public float DegreesPerSecond = 90f;

    /// <summary>Rotate around the object's own axes instead of the world axes.</summary>
    public bool LocalSpace = true;

    /// <summary>Scale the rate by <see cref="Time.TimeScale"/> when false.</summary>
    public bool Unscaled;

    public override void Update()
    {
        float delta = Unscaled ? Time.UnscaledDeltaTime : Time.DeltaTime;
        Transform.Rotate(Axis, DegreesPerSecond * delta, LocalSpace);
    }

    /// <summary>Rotates by an explicit number of degrees, for a graph or a script.</summary>
    public void RotateBy(float degrees) => Transform.Rotate(Axis, degrees, LocalSpace);
}

/// <summary>Follows another transform with damping, for cameras and companions.</summary>
[AddComponentMenu("Utility/Smooth Follow")]
[ComponentDisplayName("Smooth Follow")]
[ComponentIcon("\uf0c1")]
public class SmoothFollow : Component
{
    public Transform? Target;

    /// <summary>Offset from the target, in the target's local space when <see cref="LocalOffset"/> is true.</summary>
    public Float3 Offset = new(0f, 2f, -5f);

    public bool LocalOffset;

    /// <summary>Higher values catch up faster. Zero leaves the object where it is.</summary>
    public float Damping = 8f;

    /// <summary>Point the object at the target after moving.</summary>
    public bool LookAtTarget = true;

    /// <summary>Use unscaled time, so pausing the game does not freeze the follow.</summary>
    public bool Unscaled;

    public override void LateUpdate()
    {
        if (Target == null) return;

        Float3 desired = LocalOffset
            ? Target!.Position + Target.TransformDirection(Offset)
            : Target!.Position + Offset;

        float delta = Unscaled ? Time.UnscaledDeltaTime : Time.DeltaTime;
        float t = Damping <= 0f ? 1f : 1f - MathF.Exp(-Damping * delta);
        Transform.Position = Maths.Lerp(Transform.Position, desired, t);

        if (LookAtTarget) Transform.LookAt(Target!.Position);
    }

    /// <summary>Snaps to the target immediately.</summary>
    public void SnapToTarget()
    {
        if (Target == null) return;
        Transform.Position = LocalOffset
            ? Target!.Position + Target.TransformDirection(Offset)
            : Target!.Position + Offset;
        if (LookAtTarget) Transform.LookAt(Target!.Position);
    }
}

/// <summary>Destroys an object after a number of seconds, for temporary effects and spawned props.</summary>
[AddComponentMenu("Utility/Lifetime")]
[ComponentDisplayName("Lifetime")]
[ComponentIcon("\uf017")]
public class Lifetime : Component
{
    /// <summary>Seconds before the object expires.</summary>
    public float Duration = 5f;

    /// <summary>Destroy the object when the time runs out. Turn off to raise only <see cref="Expired"/>.</summary>
    public bool DestroyOnExpire = true;

    /// <summary>Count while the game is paused.</summary>
    public bool Unscaled;

    private float _age;

    /// <summary>Seconds this object has been alive.</summary>
    public float Age => _age;

    /// <summary>Seconds left before expiry.</summary>
    public float Remaining => MathF.Max(0f, Duration - _age);

    /// <summary>True once the lifetime has run out.</summary>
    public bool IsExpired => _age >= Duration;

    /// <summary>Raised once when the lifetime runs out, before the object is destroyed.</summary>
    public event Action<Lifetime>? Expired;

    public override void OnEnable() => _age = 0f;

    public override void Update()
    {
        if (Duration <= 0f) return;

        _age += Unscaled ? Time.UnscaledDeltaTime : Time.DeltaTime;
        if (_age < Duration) return;

        _age = Duration;
        Expired?.Invoke(this);
        if (DestroyOnExpire) GameObject.Destroy();
    }

    /// <summary>Restarts the countdown, the usual call when reusing a pooled object.</summary>
    public void ResetLifetime()
    {
        _age = 0f;
        Enabled = true;
    }
}
