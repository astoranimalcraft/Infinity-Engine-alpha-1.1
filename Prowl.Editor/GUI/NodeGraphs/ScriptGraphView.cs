// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Infinity.Editor.GraphEditor;
using Infinity.Editor.Theming;
using Infinity.OrigamiUI;
using Infinity.PaperUI;
using Infinity.PaperUI.LayoutEngine;
using Infinity.Runtime;
using Infinity.Runtime.Graphs;
using Infinity.Vector;

namespace Infinity.Editor.NodeGraphs;

/// <summary>Draws script graph cards, with events and actions in their own colours and the common settings inline.</summary>
public sealed class ScriptGraphView : GraphView<GraphNodeRecord>
{
    /// <summary>Applies a change without rebuilding the cards, so a field keeps focus while it is typed in.</summary>
    public Action<string, Action>? Edit;

    /// <summary>Applies a change and rebuilds the cards, for a setting that changes a port's type.</summary>
    public Action<string, Action>? EditRebuild;

    protected override GraphNode CreateNode(GraphNodeRecord record)
    {
        ScriptGraphNodeDef? def = ScriptGraphNodes.Get(record.Type);
        var node = new GraphNode
        {
            Title = def?.DisplayName ?? record.Type,
            Width = 198f,
            Icon = def?.Category == "Events" ? EditorIcons.Play_I : EditorIcons.DiagramProject_I,
            Accent = CategoryColor(def?.Category),
            Tooltip = def?.Description,
        };
        AddInlineRows(node, record);
        return node;
    }

    private void AddInlineRows(GraphNode node, GraphNodeRecord record)
    {
        switch (record.Type)
        {
            case ScriptGraphNodes.Float:
                NumberRow(node, record, "Value", "Value", ScriptPinKind.Float);
                break;
            case ScriptGraphNodes.Int:
                NumberRow(node, record, "Int", "Value", ScriptPinKind.Int);
                break;
            case ScriptGraphNodes.Bool:
                BoolRow(node, record, "Bool", "Value");
                break;
            case ScriptGraphNodes.String:
                TextRow(node, record, "Text", "Text");
                break;
            case ScriptGraphNodes.Vector3:
                VectorRow(node, record, "Vector");
                break;
            case ScriptGraphNodes.Compare:
                ChoiceRow(node, record, "Operator", ["Greater", "Less", "Greater Or Equal", "Less Or Equal", "Equal", "Not Equal"]);
                break;
            case ScriptGraphNodes.Logic:
                ChoiceRow(node, record, "Operator", ["And", "Or", "Xor", "Not"]);
                break;
            case ScriptGraphNodes.VectorSplit:
                ChoiceRow(node, record, "Axis", ["X", "Y", "Z"]);
                break;
            case ScriptGraphNodes.OnKeyDown:
            case ScriptGraphNodes.GetKey:
                TextRow(node, record, "Key", "Key");
                break;
            case ScriptGraphNodes.Log:
                TextRow(node, record, "Text", "Message");
                break;
            case ScriptGraphNodes.GetVariable:
            case ScriptGraphNodes.SetVariable:
                VariableRow(node, record);
                break;
        }
    }

    private void NumberRow(GraphNode node, GraphNodeRecord record, string key, string label, ScriptPinKind kind)
    {
        node.Rows.Add(new GraphNodeRow(24f, ctx =>
        {
            Paper paper = ctx.Paper;
            using (paper.Row(ctx.Id("row")).Width(UnitValue.Stretch()).Height(24f).Gap(4).AlignItems(LayoutAlignment.Center).Enter())
            {
                RowLabel(paper, ctx.Id("label"), label);
                if (kind == ScriptPinKind.Int)
                    Origami.NumericField<int>(paper, ctx.Id("field"), Integer(record, key), v =>
                        Edit?.Invoke($"Set {label}", () => record.Properties[key] = NodeValue.FromInteger(v))).Width(UnitValue.Stretch()).Show();
                else
                    Origami.NumericField<float>(paper, ctx.Id("field"), Number(record, key), v =>
                        Edit?.Invoke($"Set {label}", () => record.Properties[key] = NodeValue.FromNumber(v))).Width(UnitValue.Stretch()).Show();
            }
        }));
    }

    private void BoolRow(GraphNode node, GraphNodeRecord record, string key, string label)
    {
        node.Rows.Add(new GraphNodeRow(24f, ctx =>
        {
            Paper paper = ctx.Paper;
            using (paper.Row(ctx.Id("row")).Width(UnitValue.Stretch()).Height(24f).Gap(4).AlignItems(LayoutAlignment.Center).Enter())
            {
                RowLabel(paper, ctx.Id("label"), label);
                Origami.Checkbox(paper, ctx.Id("field"), Flag(record, key), v =>
                    Edit?.Invoke($"Set {label}", () => record.Properties[key] = NodeValue.FromFlag(v))).Show();
            }
        }));
    }

    private void TextRow(GraphNode node, GraphNodeRecord record, string key, string label)
    {
        node.Rows.Add(new GraphNodeRow(24f, ctx =>
        {
            Paper paper = ctx.Paper;
            using (paper.Row(ctx.Id("row")).Width(UnitValue.Stretch()).Height(24f).Gap(4).AlignItems(LayoutAlignment.Center).Enter())
            {
                RowLabel(paper, ctx.Id("label"), label);
                Origami.TextField(paper, ctx.Id("field"), Text(record, key), v =>
                    Edit?.Invoke($"Set {label}", () => record.Properties[key] = NodeValue.FromText(v))).Width(UnitValue.Stretch()).Show();
            }
        }));
    }

    private void VectorRow(GraphNode node, GraphNodeRecord record, string key)
    {
        node.Rows.Add(new GraphNodeRow(24f, ctx =>
        {
            Paper paper = ctx.Paper;
            Float3 value = Vector(record, key);
            using (paper.Row(ctx.Id("row")).Width(UnitValue.Stretch()).Height(24f).Gap(3).AlignItems(LayoutAlignment.Center).Enter())
            {
                Origami.NumericField<float>(paper, ctx.Id("x"), value.X, v =>
                    Edit?.Invoke("Set Vector", () => record.Properties[key] = NodeValue.FromVector(new Float3(v, value.Y, value.Z)))).Width(UnitValue.Stretch()).Show();
                Origami.NumericField<float>(paper, ctx.Id("y"), value.Y, v =>
                    Edit?.Invoke("Set Vector", () => record.Properties[key] = NodeValue.FromVector(new Float3(value.X, v, value.Z)))).Width(UnitValue.Stretch()).Show();
                Origami.NumericField<float>(paper, ctx.Id("z"), value.Z, v =>
                    Edit?.Invoke("Set Vector", () => record.Properties[key] = NodeValue.FromVector(new Float3(value.X, value.Y, v)))).Width(UnitValue.Stretch()).Show();
            }
        }));
    }

    private void ChoiceRow(GraphNode node, GraphNodeRecord record, string key, string[] options)
    {
        node.Rows.Add(new GraphNodeRow(24f, ctx =>
        {
            Paper paper = ctx.Paper;
            int current = Math.Max(0, Array.IndexOf(options, Text(record, key, options[0])));
            using (paper.Row(ctx.Id("row")).Width(UnitValue.Stretch()).Height(24f).Gap(4).AlignItems(LayoutAlignment.Center).Enter())
            {
                RowLabel(paper, ctx.Id("label"), "Mode");
                Origami.Dropdown(paper, ctx.Id("field"), current, v =>
                    EditRebuild?.Invoke($"Set {key}", () => record.Properties[key] = NodeValue.FromText(options[v])), options)
                    .Width(UnitValue.Stretch()).Show();
            }
        }));
    }

    private void VariableRow(GraphNode node, GraphNodeRecord record)
    {
        node.Rows.Add(new GraphNodeRow(24f, ctx =>
        {
            Paper paper = ctx.Paper;
            ScriptGraph? graph = (Records as ScriptGraphDomain)?.Graph;
            if (graph.IsNotValid() || graph!.Variables.Count == 0)
            {
                RowLabel(paper, ctx.Id("label"), "Variable");
                paper.Box(ctx.Id("empty")).Width(UnitValue.Stretch()).Height(24f).IsNotInteractable()
                    .Text("Add one in the side panel", EditorTheme.DefaultFont!)
                    .TextColor(EditorTheme.Ink300).FontSize(EditorTheme.FontSizeSmall)
                    .Alignment(TextAlignment.MiddleLeft);
                return;
            }

            var names = new string[graph.Variables.Count];
            for (int i = 0; i < names.Length; i++) names[i] = graph.Variables[i].Name;
            int current = Math.Max(0, Array.IndexOf(names, Text(record, "Variable", names[0])));
            using (paper.Row(ctx.Id("row")).Width(UnitValue.Stretch()).Height(24f).Gap(4).AlignItems(LayoutAlignment.Center).Enter())
            {
                RowLabel(paper, ctx.Id("label"), "Variable");
                Origami.Dropdown(paper, ctx.Id("field"), current, v =>
                    EditRebuild?.Invoke("Set Variable", () => record.Properties["Variable"] = NodeValue.FromText(names[v])), names)
                    .Width(UnitValue.Stretch()).Show();
            }
        }));
    }

    private static void RowLabel(Paper paper, string id, string text)
    {
        paper.Box(id).Width(48f).Height(24f).IsNotInteractable()
            .Text(text, EditorTheme.DefaultFont!).TextColor(EditorTheme.Ink400)
            .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft).TextTruncate();
    }

    private static float Number(GraphNodeRecord node, string key, float fallback = 0f)
        => node.Properties.TryGetValue(key, out NodeValue? value) ? value.Number : fallback;

    private static int Integer(GraphNodeRecord node, string key, int fallback = 0)
        => node.Properties.TryGetValue(key, out NodeValue? value) ? value.Integer : fallback;

    private static bool Flag(GraphNodeRecord node, string key, bool fallback = false)
        => node.Properties.TryGetValue(key, out NodeValue? value) ? value.Flag : fallback;

    private static string Text(GraphNodeRecord node, string key, string fallback = "")
        => node.Properties.TryGetValue(key, out NodeValue? value) ? value.Text : fallback;

    private static Float3 Vector(GraphNodeRecord node, string key)
        => node.Properties.TryGetValue(key, out NodeValue? value) ? value.Vector : Float3.Zero;

    private static System.Drawing.Color CategoryColor(string? category) => category switch
    {
        "Events" => System.Drawing.Color.FromArgb(220, 120, 110),
        "Flow" => System.Drawing.Color.FromArgb(220, 170, 90),
        "Actions" => System.Drawing.Color.FromArgb(110, 180, 220),
        "Values" => System.Drawing.Color.FromArgb(140, 200, 150),
        "Math" => System.Drawing.Color.FromArgb(150, 160, 230),
        "Variables" => System.Drawing.Color.FromArgb(200, 140, 210),
        "Input" => System.Drawing.Color.FromArgb(210, 180, 110),
        _ => System.Drawing.Color.FromArgb(150, 150, 210),
    };
}
