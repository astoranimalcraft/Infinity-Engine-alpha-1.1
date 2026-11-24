// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Infinity.Vector;

namespace Infinity.Runtime;

/// <summary>
/// Draws the selected camera's frustum as coloured world lines. Add it to a camera to verify that
/// culling matches what the camera sees, in the editor and in a built game. It only draws lines; it
/// does not change what the camera culls.
/// </summary>
[AddComponentMenu("Rendering/Camera Frustum Debug")]
public sealed class CameraFrustumDebug : Component
{
    public Camera? TargetCamera;

    public Color NearColor = new(0.30f, 0.80f, 1f, 1f);
    public Color FarColor = new(0.75f, 0.55f, 1f, 0.9f);
    public Color EdgeColor = new(0.45f, 0.65f, 1f, 0.85f);
    public bool DrawAxes = true;
    public bool DrawInGame = true;

    private bool _requested;

    public override void OnEnable()
    {
        if (!TargetCamera.IsValid())
            TargetCamera = GetComponent<Camera>();

        if (DrawInGame && !_requested)
        {
            _requested = true;
            Debug.RequestRuntimeGizmos();
        }
    }

    public override void OnDisable()
    {
        if (!_requested) return;
        _requested = false;
        Debug.ReleaseRuntimeGizmos();
    }

    public override void LateUpdate()
    {
        if (DrawInGame) Draw();
    }

    public override void DrawGizmosSelected() => Draw();

    private void Draw()
    {
        if (!TargetCamera.IsValid()) return;

        Float3[] corners = TargetCamera.GetFrustum().GetCorners();

        // 0..3 are near, 4..7 far: left-bottom, right-bottom, left-top, right-top.
        Debug.DrawLine(corners[0], corners[1], NearColor);
        Debug.DrawLine(corners[1], corners[3], NearColor);
        Debug.DrawLine(corners[3], corners[2], NearColor);
        Debug.DrawLine(corners[2], corners[0], NearColor);

        Debug.DrawLine(corners[4], corners[5], FarColor);
        Debug.DrawLine(corners[5], corners[7], FarColor);
        Debug.DrawLine(corners[7], corners[6], FarColor);
        Debug.DrawLine(corners[6], corners[4], FarColor);

        Debug.DrawLine(corners[0], corners[4], EdgeColor);
        Debug.DrawLine(corners[1], corners[5], EdgeColor);
        Debug.DrawLine(corners[2], corners[6], EdgeColor);
        Debug.DrawLine(corners[3], corners[7], EdgeColor);

        if (!DrawAxes) return;
        Float3 position = TargetCamera.Transform.Position;
        Debug.DrawLine(position, position + TargetCamera.Transform.Forward * 1.25f, new Color(1f, 1f, 0.2f, 1f));
        Debug.DrawLine(position, position + TargetCamera.Transform.Up * 0.75f, new Color(0.45f, 1f, 0.45f, 1f));
    }
}
