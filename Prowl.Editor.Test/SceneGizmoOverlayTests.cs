using System.Linq;

using Infinity.Editor.Core;
using Infinity.Editor.GUI.Panels;
using Infinity.OrigamiUI.Gizmo;
using Infinity.Runtime;
using Infinity.Runtime.Resources;
using Infinity.Vector;

using Xunit;

namespace Infinity.Editor.Test;

/// <summary>Draws one line and one triangle, in world space, where a test can find them.</summary>
public sealed class OverlayProbe : Component
{
    public static readonly Float3 LineA = new(-2f, 1f, 0.5f);
    public static readonly Float3 LineB = new(2f, 1f, 0.5f);

    public override void DrawGizmos()
    {
        Debug.DrawLine(LineA, LineB, Color.White);
        Debug.DrawTriangle(new Float3(0, 0, 0), new Float3(1, 0, 0), new Float3(0, 1, 0), Color.Red);
    }
}

/// <summary>
/// The scene view draws gizmos on its overlay, since Graphite ships no gizmo rasteriser. These cover the two halves of
/// that: the frame's draws reach the batch the overlay reads, and the overlay's projection agrees with the camera.
/// </summary>
public class SceneGizmoOverlayTests : EditorTestHarness
{
    private static readonly Float2 Screen = new(800, 600);

    private static SceneInstance MakeScene()
    {
        var scene = new SceneInstance(SceneMode.Edit);
        scene.Activate();
        return scene;
    }

    private static Camera MakeCamera(SceneInstance scene)
    {
        var go = new GameObject("Camera");
        go.Transform.Position = new Float3(0, 0, -10);
        Camera cam = go.AddComponent<Camera>();
        cam.Aspect = Screen.X / Screen.Y;
        scene.Add(go);
        return cam;
    }

    [Fact]
    public void ComponentGizmosReachTheBatchTheOverlayDraws()
    {
        using SceneInstance scene = MakeScene();
        var go = new GameObject("Probe");
        go.AddComponent<OverlayProbe>();
        scene.Add(go);

        var editor = new EditorApplication();
        editor.BeginRender();
        new SceneViewPanel().DrawSceneGizmos(editor, scene);

        GizmoBatch batch = scene.Gizmos.Current;

        Assert.False(batch.IsEmpty);
        Assert.Contains(batch.Lines, line => line.A == OverlayProbe.LineA && line.B == OverlayProbe.LineB);
        Assert.NotEmpty(batch.Triangles);

        // The overlay draws before the frame ends, so the batch still holds the frame it is drawing.
        Debug.ClearGizmos();
    }

    // The projection the handles and the overlay both use, built the way the camera builds it for a viewport this shape.
    private static Float4x4 ViewProjection(Camera camera)
        => Float4x4.CreatePerspectiveFov(Maths.ToRadians(camera.FieldOfView), (float)(Screen.X / Screen.Y),
            camera.NearClipPlane, camera.FarClipPlane) * camera.ViewMatrix;

    [Fact]
    public void OverlayProjectionPutsTheCameraTargetAtTheCentre()
    {
        using SceneInstance scene = MakeScene();
        Camera camera = MakeCamera(scene);

        var rect = new Rect(0, 0, (float)Screen.X, (float)Screen.Y);
        Float2? projected = GizmoUtils.WorldToScreen(rect, ViewProjection(camera), new Float3(0, 0, 0));

        Assert.NotNull(projected);
        Assert.Equal(400f, projected!.Value.X, 1);
        Assert.Equal(300f, projected.Value.Y, 1);
    }

    [Fact]
    public void OverlayProjectionPutsRightUpAndYDown()
    {
        using SceneInstance scene = MakeScene();
        Camera camera = MakeCamera(scene);
        Float4x4 mvp = ViewProjection(camera);
        var rect = new Rect(0, 0, (float)Screen.X, (float)Screen.Y);

        Float2 right = GizmoUtils.WorldToScreen(rect, mvp, new Float3(1, 0, 0))!.Value;
        Float2 up = GizmoUtils.WorldToScreen(rect, mvp, new Float3(0, 1, 0))!.Value;

        Assert.True(right.X > 400f, $"world +X should draw right of centre, got {right.X}");
        Assert.True(up.Y < 300f, $"world +Y should draw above centre, got {up.Y}");
    }

    [Fact]
    public void OverlayProjectionMovesWithTheViewport()
    {
        using SceneInstance scene = MakeScene();
        Camera camera = MakeCamera(scene);
        Float4x4 mvp = ViewProjection(camera);

        var world = new Float3(1, 0, 0);
        Float2 atOrigin = GizmoUtils.WorldToScreen(new Rect(0, 0, (float)Screen.X, (float)Screen.Y), mvp, world)!.Value;
        Float2 offset = GizmoUtils.WorldToScreen(new Rect(120, 40, (float)Screen.X + 120, (float)Screen.Y + 40), mvp, world)!.Value;

        Assert.Equal(120f, offset.X - atOrigin.X, 2);
        Assert.Equal(40f, offset.Y - atOrigin.Y, 2);
    }

    [Fact]
    public void APointBehindTheCameraProjectsToNothing()
    {
        using SceneInstance scene = MakeScene();
        Camera camera = MakeCamera(scene);
        Float2? behind = GizmoUtils.WorldToScreen(new Rect(0, 0, (float)Screen.X, (float)Screen.Y), ViewProjection(camera), new Float3(0, 0, -20));

        Assert.Null(behind);
    }
}
