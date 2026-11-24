// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Infinity.Editor.Core;
using Infinity.Editor.GUI;
using Infinity.Editor.Inspector;
using Infinity.Editor.Theming;
using Infinity.OrigamiUI;
using Infinity.PaperUI;
using Infinity.PaperUI.LayoutEngine;
using Infinity.Runtime;
using Infinity.Runtime.Graphs;

using Color = System.Drawing.Color;

namespace Infinity.Editor.NodeGraphs;

/// <summary>Inspector and double-click entry for material graph assets.</summary>
[CustomAssetEditor(typeof(MaterialGraph))]
public class MaterialGraphAssetEditor : AssetImporterEditor
{
    [AssetDoubleClickHandler(".matgraph")]
    private static bool OnDoubleClick(string relativePath, Guid guid)
    {
        MaterialGraphWindow.OpenFor(guid);
        return true;
    }

    public override void OnGUI(Paper paper, string id, AssetEntry entry, EngineObject? asset)
    {
        Scribe.FontFile? font = EditorTheme.DefaultFont;
        if (font == null || asset is not MaterialGraph graph) return;
        OrigamiMetrics m = Origami.Current.Metrics;

        using (paper.Row($"{id}_open").Width(UnitValue.Stretch()).Height(34)
            .Margin(m.PaddingLarge, m.PaddingLarge, m.Spacing, m.SpacingMedium).Enter())
        {
            paper.Box($"{id}_openBtn").Width(UnitValue.Stretch()).Height(30)
                .Rounded(m.SmallRounding).BackgroundColor(EditorTheme.Accent)
                .Hovered.BackgroundColor(EditorTheme.AccentBright).End()
                .Text("Open Material Graph", font).TextColor(Color.White)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter)
                .OnClick(0, (_, _) => MaterialGraphWindow.OpenFor(entry.Guid));
        }

        EditorGUI.SectionHeader(paper, $"{id}_summary", "Summary", first: true);
        Line(paper, font, m, $"{id}_nodes", "Nodes", graph.Nodes.Count.ToString());
        Line(paper, font, m, $"{id}_surface", "Surface", graph.Surface.ToString());
        Line(paper, font, m, $"{id}_shader", "Shader", graph.GeneratedShader.IsValid() ? "compiled" : "not compiled");
    }

    private static void Line(Paper paper, Scribe.FontFile font, OrigamiMetrics m, string id, string label, string value)
    {
        using (paper.Row(id).Width(UnitValue.Stretch()).Height(m.RowHeight).Padding(m.PaddingLarge, m.PaddingLarge, 0, 0).Enter())
        {
            paper.Box($"{id}_l").Width(UnitValue.Stretch()).Height(m.RowHeight).IsNotInteractable()
                .Text(label, font).TextColor(EditorTheme.Ink500).FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
            paper.Box($"{id}_v").Width(UnitValue.Auto).Height(m.RowHeight).IsNotInteractable()
                .Text(value, font).TextColor(EditorTheme.Ink700).FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleRight);
        }
    }
}

/// <summary>Inspector and double-click entry for script graph assets.</summary>
[CustomAssetEditor(typeof(ScriptGraph))]
public class ScriptGraphAssetEditor : AssetImporterEditor
{
    [AssetDoubleClickHandler(".scriptgraph")]
    private static bool OnDoubleClick(string relativePath, Guid guid)
    {
        ScriptGraphWindow.OpenFor(guid);
        return true;
    }

    public override void OnGUI(Paper paper, string id, AssetEntry entry, EngineObject? asset)
    {
        Scribe.FontFile? font = EditorTheme.DefaultFont;
        if (font == null || asset is not ScriptGraph graph) return;
        OrigamiMetrics m = Origami.Current.Metrics;

        using (paper.Row($"{id}_open").Width(UnitValue.Stretch()).Height(34)
            .Margin(m.PaddingLarge, m.PaddingLarge, m.Spacing, m.SpacingMedium).Enter())
        {
            paper.Box($"{id}_openBtn").Width(UnitValue.Stretch()).Height(30)
                .Rounded(m.SmallRounding).BackgroundColor(EditorTheme.Accent)
                .Hovered.BackgroundColor(EditorTheme.AccentBright).End()
                .Text("Open Script Graph", font).TextColor(Color.White)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter)
                .OnClick(0, (_, _) => ScriptGraphWindow.OpenFor(entry.Guid));
        }

        EditorGUI.SectionHeader(paper, $"{id}_summary", "Summary", first: true);
        Line(paper, font, m, $"{id}_nodes", "Nodes", graph.Nodes.Count.ToString());
        Line(paper, font, m, $"{id}_vars", "Variables", graph.Variables.Count.ToString());
        Line(paper, font, m, $"{id}_class", "Class", graph.ResolveClassName());
        Line(paper, font, m, $"{id}_build", "Assembly", graph.AssemblyPath.Length > 0 ? "compiled" : "not compiled");
    }

    private static void Line(Paper paper, Scribe.FontFile font, OrigamiMetrics m, string id, string label, string value)
    {
        using (paper.Row(id).Width(UnitValue.Stretch()).Height(m.RowHeight).Padding(m.PaddingLarge, m.PaddingLarge, 0, 0).Enter())
        {
            paper.Box($"{id}_l").Width(UnitValue.Stretch()).Height(m.RowHeight).IsNotInteractable()
                .Text(label, font).TextColor(EditorTheme.Ink500).FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);
            paper.Box($"{id}_v").Width(UnitValue.Auto).Height(m.RowHeight).IsNotInteractable()
                .Text(value, font).TextColor(EditorTheme.Ink700).FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleRight);
        }
    }
}
