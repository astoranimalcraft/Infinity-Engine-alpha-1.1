// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Infinity.Echo;
using Infinity.Editor.Core;
using Infinity.Editor.GUI;
using Infinity.Editor.Projects;
using Infinity.Editor.Theming;
using Infinity.OrigamiUI;
using Infinity.PaperUI;
using Infinity.Runtime;
using Infinity.Runtime.Rendering;
using Infinity.Vector;

namespace Infinity.Editor.Inspector;

/// <summary>The .volume asset inspector: the profile's components, edited live. Edits are undo records that put the profile in the dirty asset set.</summary>
[CustomAssetEditor(typeof(VolumeProfile))]
public class VolumeProfileAssetEditor : AssetImporterEditor
{
    public override void OnGUI(Paper paper, string id, AssetEntry entry, EngineObject? asset)
    {
        if (asset is not VolumeProfile profile || profile.IsNotValid()) return;
        EditorGUI.SectionHeader(paper, $"{id}_hdr", $"{EditorIcons.LayerGroup}  Volume Profile", first: true);
        VolumeProfileGUI.DrawComponents(paper, $"{id}_list", profile);
        DrawSaveRevertBar(paper, id, profile);
    }

    /// <summary>The Save and Revert bar for a database profile edited outside its own inspector.</summary>
    internal static void DrawPendingBar(Paper paper, string id, VolumeProfile profile)
    {
        if (profile.IsFromDatabase) DrawSaveRevertBar(paper, id, profile);
    }
}

/// <summary>The Volume inspector: where and how strongly it applies, then its profile's components.</summary>
[CustomEditor(typeof(Volume))]
public class VolumeEditor : CustomEditor
{
    public override void OnGUI(Paper paper, string id, object target)
    {
        var volume = (Volume)target;

        PropertyGridUtils.DrawField(paper, $"{id}_global", "Is Global", typeof(bool), volume.IsGlobal, v =>
        {
            Undo.RecordObject(volume, "Change Is Global");
            volume.IsGlobal = (bool)v!;
        });
        if (!volume.IsGlobal)
        {
            PropertyGridUtils.DrawField(paper, $"{id}_size", "Size", typeof(Vector.Float3), volume.Size, v =>
            {
                Undo.RecordObject(volume, "Change Size");
                volume.Size = (Vector.Float3)v!;
            });
            PropertyGridUtils.DrawField(paper, $"{id}_blend", "Blend Distance", typeof(float), volume.BlendDistance, v =>
            {
                Undo.RecordObject(volume, "Change Blend Distance");
                volume.BlendDistance = System.MathF.Max(0f, (float)v!);
            });
            PropertyGridUtils.DrawField(paper, $"{id}_outside", "Apply Outside Bounds", typeof(bool), volume.ApplyOutsideBounds, v =>
            {
                Undo.RecordObject(volume, "Change Apply Outside Bounds");
                volume.ApplyOutsideBounds = (bool)v!;
            });
            PropertyGridUtils.DrawField(paper, $"{id}_smooth", "Smooth Blend", typeof(bool), volume.SmoothBlend, v =>
            {
                Undo.RecordObject(volume, "Change Smooth Blend");
                volume.SmoothBlend = (bool)v!;
            });
        }
        EditorGUI.SliderRow(paper, $"{id}_weight", "Weight", volume.Weight, 0f, 1f, v =>
        {
            Undo.RecordObject(volume, "Change Weight");
            volume.Weight = v;
        });
        PropertyGridUtils.DrawField(paper, $"{id}_priority", "Priority", typeof(float), volume.Priority, v =>
        {
            Undo.RecordObject(volume, "Change Priority");
            volume.Priority = (float)v!;
        });

        VolumeProfileGUI.DrawProfileField(paper, $"{id}_profile", "Profile", volume.SharedProfile, volume.GameObject.Name, p =>
        {
            Undo.RecordObject(volume, "Change Profile");
            volume.SharedProfile = p;
        });

        VolumeProfile? profile = volume.SharedProfile;
        if (profile.IsNotValid()) return;
        VolumeProfileGUI.DrawComponents(paper, $"{id}_list", profile, volume);
        VolumeProfileAssetEditor.DrawPendingBar(paper, $"{id}_pending", profile);
    }
}

/// <summary>The Camera inspector: its fields, then the components of its base profile.</summary>
[CustomEditor(typeof(Camera))]
public class CameraEditor : CustomEditor
{
    public override void OnGUI(Paper paper, string id, object target)
    {
        var camera = (Camera)target;

        void Field<T>(string key, string label, T value, Action<T> apply)
        {
            PropertyGridUtils.DrawField(paper, $"{id}_{key}", label, typeof(T), value, v =>
            {
                if (v is not T typed) return;
                Undo.RecordObject(camera, $"Change {label}");
                apply(typed);
            });
        }

        EditorGUI.SectionHeader(paper, $"{id}_camera", "Camera", first: true);
        Field("projection", "Projection", camera.ProjectionMode, v => camera.ProjectionMode = v);
        if (camera.ProjectionMode == Camera.ProjectionType.Perspective)
            Field("fov", "Field of View", camera.FieldOfView, v => camera.FieldOfView = Math.Clamp(v, 1f, 179f));
        else
            Field("ortho", "Orthographic Size", camera.OrthographicSize, v => camera.OrthographicSize = MathF.Max(0.01f, v));
        Field("near", "Near Clip Plane", camera.NearClipPlane, v => camera.NearClipPlane = MathF.Max(0.001f, v));
        Field("far", "Far Clip Plane", camera.FarClipPlane, v => camera.FarClipPlane = MathF.Max(camera.NearClipPlane + 0.001f, v));
        Field("viewport", "Viewport Rect", camera.ViewportRect, v => camera.ViewportRect = v);
        Field("target", "Target Texture", camera.Target, v => camera.Target = v);
        Field("depth", "Depth", camera.Depth, v => camera.Depth = v);
        Field("renderScale", "Render Scale", camera.RenderScale, v => camera.RenderScale = Math.Clamp(v, 0.1f, 2f));
        Field("hdr", "HDR", camera.HDR, v => camera.HDR = v);

        EditorGUI.SectionHeader(paper, $"{id}_rendering", "Rendering", compact: true);
        Field("background", "Background", camera.Background, v => camera.Background = v);
        if (camera.Background == CameraBackground.Color)
            Field("bgColor", "Background Color", camera.BackgroundColor, v => camera.BackgroundColor = v);
        Field("culling", "Culling Mask", camera.CullingMask, v => camera.CullingMask = v);
        Field("aa", "Anti Aliasing", camera.AntiAliasing, v => camera.AntiAliasing = v);
        if (camera.AntiAliasing == AntiAliasingMode.FXAA)
            Field("fxaa", "FXAA Settings", camera.Fxaa, v => camera.Fxaa = v);
        else if (camera.AntiAliasing == AntiAliasingMode.SMAA)
            Field("smaa", "SMAA Settings", camera.Smaa, v => camera.Smaa = v);
        else if (camera.AntiAliasing == AntiAliasingMode.TAA)
            Field("taa", "TAA Settings", camera.Taa, v => camera.Taa = v);

        EditorGUI.SectionHeader(paper, $"{id}_post", "Post Processing", compact: true);
        Field("postEnabled", "Post Processing", camera.PostProcessing, v => camera.PostProcessing = v);
        if (camera.PostProcessing)
        {
            Field("applyVolumes", "Aplicar volumes fora dos limites", camera.ApplyVolumesOutsideTheirBounds,
                v => camera.ApplyVolumesOutsideTheirBounds = v);
            EditorGUI.Note(paper, $"{id}_volumeHint",
                "Desmarcado: a câmera ou o Volume Trigger precisa entrar no volume local. Marcado: o volume aplica em qualquer lugar, sem entrar nele. Use Volume Trigger no objeto do player para testar volumes a partir dele.");

            VolumeProfileGUI.DrawProfileField(paper, $"{id}_profile", "Base Profile", camera.Profile, camera.GameObject.Name, p =>
            {
                Undo.RecordObject(camera, "Change Base Profile");
                camera.Profile = p;
            });
            Field("volumeMask", "Volume Mask", camera.VolumeMask, v => camera.VolumeMask = v);
            Field("volumeTrigger", "Volume Trigger", camera.VolumeTrigger, v => camera.VolumeTrigger = v);
            Field("ev100", "EV100", camera.EV100, v => camera.EV100 = v);
        }

        Field("stereo", "Stereo Target Eye", camera.StereoTargetEye, v => camera.StereoTargetEye = v);

        VolumeProfile? profile = camera.Profile;
        EditorGUI.SectionHeader(paper, $"{id}_base_hdr", "Base Profile");
        using (paper.Row($"{id}_base_btns").Height(Infinity.PaperUI.LayoutEngine.UnitValue.Auto)
            .Padding(Origami.Current.Metrics.PaddingLarge, Origami.Current.Metrics.PaddingLarge, 0, 0).Gap(Origami.Current.Metrics.Padding)
            .Margin(0, 0, 0, Origami.Current.Metrics.SpacingLarge).Enter())
        {
            Origami.Button(paper, $"{id}_base_new", $"{EditorIcons.Plus}  New", () =>
            {
                Undo.RecordObject(camera, "New Profile");
                camera.Profile = new VolumeProfile { Name = $"{camera.GameObject.Name} Profile" };
            }).Small().Show();
            if (profile.IsValid())
            {
                Origami.Button(paper, $"{id}_base_clone", $"{EditorIcons.Clone}  Clone", () =>
                {
                    Undo.RecordObject(camera, "Clone Profile");
                    VolumeProfile copy = profile.Clone();
                    copy.Name = $"{profile.Name} Copy";
                    camera.Profile = copy;
                }).Small().Show();
                if (!profile.IsFromDatabase)
                    Origami.Button(paper, $"{id}_base_save", $"{EditorIcons.FloppyDisk}  Save As Asset", () => VolumeProfileGUI.SaveAsAsset(profile)).Small().Show();
            }
        }

        if (profile.IsNotValid()) return;
        VolumeProfileGUI.DrawComponents(paper, $"{id}_base", profile, camera);
        VolumeProfileAssetEditor.DrawPendingBar(paper, $"{id}_base_pending", profile);
    }
}
