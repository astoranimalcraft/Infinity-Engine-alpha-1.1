// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;

using Infinity.Editor.Core;
using Infinity.Editor.GraphEditor;
using Infinity.Editor.GUI;
using Infinity.Editor.Inspector;
using Infinity.Editor.Projects;
using Infinity.Editor.Theming;
using Infinity.OrigamiUI;
using Infinity.PaperUI;
using Infinity.PaperUI.LayoutEngine;
using Infinity.Runtime;
using Infinity.Runtime.Animation;
using Infinity.Runtime.Graphs;
using Infinity.Runtime.Resources;
using Infinity.Vector;

using Color = Infinity.Vector.Color;

namespace Infinity.Editor.NodeGraphs;

/// <summary>The UE5-style material graph editor: nodes, a preview, surface settings and a compile button.</summary>
public class MaterialGraphWindow : GraphWindow<MaterialGraphWindow, GraphNodeRecord>
{
    private readonly MaterialGraphView _view = new();
    private int _compiledVersion = -1;
    private string _compileMessage = "";

    public override string Icon => EditorIcons.Palette;

    protected override string ToolName => "Material Graph";
    protected override string EmptyText => "No material graph is open.";
    protected override GraphView<GraphNodeRecord> View => _view;
    protected override IOrigamiIcon AssetIcon => EditorIcons.Palette_I;

    private MaterialGraph? Graph => Asset as MaterialGraph;

    protected override Asset? LoadAsset(Guid guid) => AssetDatabase.Load<MaterialGraph>(guid);
    protected override Asset? LoadedAsset(Guid guid) => AssetDatabase.Get<MaterialGraph>(guid);
    protected override GraphDomain<GraphNodeRecord> CreateDomain(Asset asset) => new MaterialGraphDomain((MaterialGraph)asset);

    protected override void BeforeDraw()
    {
        if (Graph.IsNotValid()) return;
        if (Graph!.GeneratedMaterial.IsNotValid() && !Graph.PostProcess || _compiledVersion != Graph.Version)
            Compile();
    }

    protected override void DrawInspector(Paper paper, object item)
    {
        if (item is not GraphNodeRecord node) return;
        Scribe.FontFile? font = EditorTheme.DefaultFont;
        if (font == null) return;

        MaterialGraphNodeDef? def = MaterialGraphNodes.Get(node.Type);
        EditorGUI.SectionHeader(paper, "mg_node", def?.DisplayName ?? node.Type, first: true);
        if (def != null) EditorGUI.Note(paper, "mg_nodeDesc", def.Description);

        switch (node.Type)
        {
            case MaterialGraphNodes.ConstFloat:
                EditorGUI.Row(paper, "mg_value", "Value", () =>
                    Origami.NumericField<float>(paper, "mg_value_v", Number(node, "Value"), v => SetNumber(node, "Value", v)).Show());
                break;

            case MaterialGraphNodes.ConstColor:
                EditorGUI.Row(paper, "mg_color", "Color", () =>
                {
                    Float3 rgb = Vector(node, "Color");
                    Origami.ColorField(paper, "mg_color_v", new Color(rgb.X, rgb.Y, rgb.Z, 1f), v =>
                        Set("Set Color", () => node.Properties["Color"] = NodeValue.FromVector(new Float3(v.R, v.G, v.B)), false))
                        .Width(UnitValue.Stretch()).Show();
                });
                break;

            case MaterialGraphNodes.ConstVector:
                EditorGUI.Row(paper, "mg_vec", "Vector", () =>
                {
                    Float3 value = Vector(node, "Vector");
                    using (paper.Row("mg_vecRow").Width(UnitValue.Stretch()).Height(UnitValue.Auto).Gap(4).Enter())
                    {
                        Origami.NumericField<float>(paper, "mg_vec_x", value.X, v => SetVector(node, "Vector", new Float3(v, value.Y, value.Z))).Width(UnitValue.Stretch()).Show();
                        Origami.NumericField<float>(paper, "mg_vec_y", value.Y, v => SetVector(node, "Vector", new Float3(value.X, v, value.Z))).Width(UnitValue.Stretch()).Show();
                        Origami.NumericField<float>(paper, "mg_vec_z", value.Z, v => SetVector(node, "Vector", new Float3(value.X, value.Y, v))).Width(UnitValue.Stretch()).Show();
                    }
                });
                break;

            case MaterialGraphNodes.Texture:
                EditorGUI.Row(paper, "mg_tex", "Texture", () =>
                    AnimationNodeFields.AssetField<Texture2D>(paper, "mg_tex_v", "", Texture(node), v =>
                        Set("Set Texture", () => node.Properties["Texture"] = NodeValue.FromTexture(v), false)));
                break;

            case MaterialGraphNodes.Clamp:
                EditorGUI.Row(paper, "mg_min", "Min", () =>
                    Origami.NumericField<float>(paper, "mg_min_v", Number(node, "Min"), v => SetNumber(node, "Min", v)).Show());
                EditorGUI.Row(paper, "mg_max", "Max", () =>
                    Origami.NumericField<float>(paper, "mg_max_v", Number(node, "Max", 1f), v => SetNumber(node, "Max", v)).Show());
                break;

            case MaterialGraphNodes.Step:
                EditorGUI.Row(paper, "mg_edge", "Edge", () =>
                    Origami.NumericField<float>(paper, "mg_edge_v", Number(node, "Edge", 0.5f), v => SetNumber(node, "Edge", v)).Show());
                break;

            case MaterialGraphNodes.Smoothstep:
                EditorGUI.Row(paper, "mg_smoothMin", "Min", () =>
                    Origami.NumericField<float>(paper, "mg_smoothMin_v", Number(node, "Min"), v => SetNumber(node, "Min", v)).Show());
                EditorGUI.Row(paper, "mg_smoothMax", "Max", () =>
                    Origami.NumericField<float>(paper, "mg_smoothMax_v", Number(node, "Max", 1f), v => SetNumber(node, "Max", v)).Show());
                break;

            case MaterialGraphNodes.Channel:
                EditorGUI.Row(paper, "mg_channel", "Channel", () =>
                    Origami.Dropdown(paper, "mg_channel_v", (int)Number(node, "Channel"), v =>
                        Set("Set Channel", () => node.Properties["Channel"] = NodeValue.FromInteger(v), false),
                        ["R", "G", "B", "A"]).Show());
                break;

            case MaterialGraphNodes.Panner:
                EditorGUI.Row(paper, "mg_speed", "Speed", () =>
                {
                    Float3 speed = Vector(node, "Speed");
                    using (paper.Row("mg_speedRow").Width(UnitValue.Stretch()).Height(UnitValue.Auto).Gap(4).Enter())
                    {
                        Origami.NumericField<float>(paper, "mg_speed_x", speed.X, v => SetVector(node, "Speed", new Float3(v, speed.Y, 0f))).Width(UnitValue.Stretch()).Show();
                        Origami.NumericField<float>(paper, "mg_speed_y", speed.Y, v => SetVector(node, "Speed", new Float3(speed.X, v, 0f))).Width(UnitValue.Stretch()).Show();
                    }
                });
                break;

            case MaterialGraphNodes.Fresnel:
                EditorGUI.Row(paper, "mg_power", "Power", () =>
                    Origami.NumericField<float>(paper, "mg_power_v", Number(node, "Power", 4f), v => SetNumber(node, "Power", v)).Show());
                break;

            case MaterialGraphNodes.Output:
                EditorGUI.Note(paper, "mg_output", "Wire a Base Color here, then use the Compile button in the side panel. Opacity and the surface type decide whether the material is opaque, cutout or transparent.");
                break;

            default:
                EditorGUI.Note(paper, "mg_wired", "This node is driven by its inputs. Wire values into it on the canvas.");
                break;
        }
    }

    protected override void DrawPanelSection(Paper paper, Scribe.FontFile font, OrigamiMetrics m, float width, float height)
    {
        if (Graph.IsNotValid()) return;

        EditorGUI.SectionHeader(paper, "mg_surface", "Material", first: true);
        int materialType = Graph.PostProcess ? 2 : Graph.Unlit ? 1 : 0;
        EditorGUI.Row(paper, "mg_typeRow", "Material Type", () =>
            Origami.Dropdown(paper, "mg_type_v", materialType, v => Set("Set Material Type", () =>
            {
                Graph.PostProcess = v == 2;
                Graph.Unlit = v == 1;
            }), ["Surface (Lit)", "Unlit", "Post Process"]).Show());
        EditorGUI.Note(paper, "mg_type", Graph.PostProcess ? "Material type: Post Process" : "Material type: Surface");

        if (Graph.PostProcess)
        {
            EditorGUI.Note(paper, "mg_postprocess_note", "Post-process graph. Wire Base Color from Scene Color and math nodes. Add a Post Process Material override to a volume or the camera profile and assign this graph.");
        }
        else
        {
            EditorGUI.SettingsToggle(paper, "mg_unlit", "Unlit", Graph.Unlit,
                v => Set("Set Unlit", () => Graph.Unlit = v));
            EditorGUI.SettingsToggle(paper, "mg_twosided", "Two Sided", Graph.TwoSided,
                v => Set("Set Two Sided", () => Graph.TwoSided = v));

            EditorGUI.Row(paper, "mg_surfaceType", "Surface", () =>
                Origami.EnumDropdown(paper, "mg_surfaceType_v", Graph.Surface, v => Set("Set Surface", () => Graph.Surface = v)).Show());
        }

        EditorGUI.Row(paper, "mg_compileRow", "", () =>
            Origami.Button(paper, "mg_compile", "Compile Graph", Compile).Width(UnitValue.Stretch()).Show());

        if (!Graph.PostProcess)
        {
            EditorGUI.Row(paper, "mg_makeRow", "", () =>
                Origami.Button(paper, "mg_makeMaterial", "Create Material Asset", CreateMaterialAsset)
                    .Width(UnitValue.Stretch()).Show());
        }

        EditorGUI.SectionHeader(paper, "mg_previewHeader", "Preview", compact: true);
        EditorGUI.Row(paper, "mg_previewMesh", "Shape", () =>
            Origami.EnumDropdown(paper, "mg_previewMesh_v", Graph.PreviewMesh, v =>
                Set("Set Preview Shape", () =>
                {
                    Graph.PreviewMesh = v;
                    PreviewWidget.Invalidate(Graph.AssetID);
                }, false)).Show());
        EditorGUI.SettingsToggle(paper, "mg_previewAlpha", "Transparent Background", Graph.PreviewTransparentBackground,
            v => Set("Set Preview Background", () =>
            {
                Graph.PreviewTransparentBackground = v;
                PreviewWidget.Invalidate(Graph.AssetID);
            }, false), separator: false);

        if (Graph.GeneratedMaterial.IsValid())
        {
            PreviewWidget.For(Graph.AssetID).Get(Graph.GeneratedMaterial, p =>
            {
                p.TransparentBackground = Graph.PreviewTransparentBackground;
                p.SetupForMaterial(Graph.GeneratedMaterial!, Graph.PreviewMesh);
            }).DrawPreview(paper, "mg_preview", 240, 240);
        }

        if (_compileMessage.Length > 0)
            EditorGUI.Note(paper, "mg_compileMessage", _compileMessage);

        if (Graph.GeneratedShader.IsValid())
            EditorGUI.Note(paper, "mg_compiled", "Compiled. The material is on the asset, ready for a renderer.");
        else if (Graph.PostProcess && Graph.GeneratedPostProcessSource.Length > 0)
            EditorGUI.Note(paper, "mg_compiled", "Compiled as a post-process fragment. Assign this graph to a Post Process Material override.");
    }

    private void Compile()
    {
        if (Graph.IsNotValid()) return;
        Material? material = Graph!.Compile();
        _compiledVersion = Graph.Version;
        _compileMessage = Graph.PostProcess
            ? (Graph.GeneratedPostProcessSource.Length > 0 ? "Post-process fragment compiled." : $"Post-process compile failed: {Detail(Graph.LastCompileError)}")
            : (material.IsValid() ? "Material compiled and previewed." : $"Compile failed: {Detail(Graph.LastCompileError)}");
        PreviewWidget.Invalidate(Graph.AssetID);
        EditorUtility.SetDirty(Graph);
    }

    private static string Detail(string error)
        => string.IsNullOrWhiteSpace(error) ? "check the console for the shader error." : error;

    /// <summary>
    /// Writes the compiled shader and material into the project so the graph can be used by a renderer. The shader is
    /// a real .shader asset, not only the runtime shader in GeneratedShader, so the material survives a reload.
    /// </summary>
    private void CreateMaterialAsset()
    {
        if (Graph.IsNotValid() || Graph!.PostProcess) return;

        Material? material = Graph.Compile();
        if (material.IsNotValid() || Graph.GeneratedShader.IsNotValid())
        {
            _compileMessage = "Compile failed. Check the console for the shader error.";
            return;
        }

        string folder = AssetCreateMenu.GetCurrentFolder();
        string absoluteFolder = AssetCreateMenu.GetAbsoluteFolder(folder);
        if (!Directory.Exists(absoluteFolder))
        {
            _compileMessage = "Choose a project folder before creating the material.";
            return;
        }

        string shaderName = AssetCreateMenu.FindUniqueName(absoluteFolder, Graph.Name, ".shader");
        string shaderRelative = string.IsNullOrEmpty(folder) ? shaderName : $"{folder}/{shaderName}";
        File.WriteAllText(Path.Combine(absoluteFolder, shaderName), Graph.ToShaderSource());
        EditorAssetBackend.Instance?.ImportFile(shaderRelative);

        Guid shaderGuid = EditorAssetBackend.Instance?.PathToGuid(shaderRelative) ?? Guid.Empty;
        Shader? shader = shaderGuid != Guid.Empty ? AssetDatabase.Load<Shader>(shaderGuid) : null;
        if (shader.IsNotValid())
        {
            _compileMessage = "The shader asset was written but could not be imported.";
            return;
        }

        material!.Shader = shader;
        string materialName = AssetCreateMenu.FindUniqueName(absoluteFolder, Graph.Name, ".mat");
        string materialRelative = string.IsNullOrEmpty(folder) ? materialName : $"{folder}/{materialName}";
        EditorAssetBackend.Instance?.CreateAsset(material, materialRelative);
        EditorAssetBackend.Instance?.ImportFile(materialRelative);

        Guid materialGuid = EditorAssetBackend.Instance?.PathToGuid(materialRelative) ?? Guid.Empty;
        if (materialGuid != Guid.Empty)
        {
            Material? loaded = AssetDatabase.Get<Material>(materialGuid);
            if (loaded.IsValid()) Selection.Select(loaded!);
            EditorRegistries.DispatchDoubleClick(materialRelative, materialGuid);
        }

        _compileMessage = $"Created {materialRelative} and {shaderRelative}.";
    }

    private void SetNumber(GraphNodeRecord node, string key, float value)
        => Set("Set " + key, () => node.Properties[key] = NodeValue.FromNumber(value), false);

    private void SetVector(GraphNodeRecord node, string key, Float3 value)
        => Set("Set " + key, () => node.Properties[key] = NodeValue.FromVector(value), false);

    private static float Number(GraphNodeRecord node, string key, float fallback = 0f)
        => node.Properties.TryGetValue(key, out NodeValue? value) ? value.Number : fallback;

    private static Float3 Vector(GraphNodeRecord node, string key)
        => node.Properties.TryGetValue(key, out NodeValue? value) ? value.Vector : Float3.Zero;

    private static Texture2D? Texture(GraphNodeRecord node)
        => node.Properties.TryGetValue("Texture", out NodeValue? value) ? value.Texture : null;
}
