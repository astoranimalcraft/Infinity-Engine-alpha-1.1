// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Infinity.Editor.GraphEditor;
using Infinity.Editor.Theming;
using Infinity.OrigamiUI;
using Infinity.Runtime;
using Infinity.Runtime.Graphs;

namespace Infinity.Editor.NodeGraphs;

/// <summary>Draws material graph cards, with a header colour per category.</summary>
public sealed class MaterialGraphView : GraphView<GraphNodeRecord>
{
    protected override GraphNode CreateNode(GraphNodeRecord record)
    {
        MaterialGraphNodeDef? def = MaterialGraphNodes.Get(record.Type);
        return new GraphNode
        {
            Title = def?.DisplayName ?? record.Type,
            Width = 196f,
            Icon = EditorIcons.DiagramProject_I,
            Accent = CategoryColor(def?.Category),
            Tooltip = def?.Description,
        };
    }

    private static System.Drawing.Color CategoryColor(string? category) => category switch
    {
        "Output" => System.Drawing.Color.FromArgb(220, 160, 80),
        "Constants" => System.Drawing.Color.FromArgb(120, 180, 220),
        "Inputs" => System.Drawing.Color.FromArgb(120, 200, 150),
        "Textures" => System.Drawing.Color.FromArgb(210, 130, 200),
        _ => System.Drawing.Color.FromArgb(150, 150, 210),
    };
}
