// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Infinity.Editor.Core;
using Infinity.Editor.GraphEditor;
using Infinity.Editor.GUI;
using Infinity.Editor.Projects.Scripting;
using Infinity.Editor.Theming;
using Infinity.OrigamiUI;
using Infinity.PaperUI;
using Infinity.PaperUI.LayoutEngine;
using Infinity.Runtime;
using Infinity.Runtime.Animation;
using Infinity.Runtime.Graphs;
using Infinity.Vector;

namespace Infinity.Editor.NodeGraphs;

/// <summary>The visual C# script editor. Nodes compile to a class a ScriptGraphRunner plays.</summary>
public class ScriptGraphWindow : GraphWindow<ScriptGraphWindow, GraphNodeRecord>
{
    private readonly ScriptGraphView _view = new();
    private string _compileMessage = "";

    public override string Icon => EditorIcons.CodeFork;

    protected override string ToolName => "Script Graph";
    protected override string EmptyText => "No script graph is open.";
    protected override GraphView<GraphNodeRecord> View => _view;
    protected override IOrigamiIcon AssetIcon => EditorIcons.CodeFork_I;

    private ScriptGraph? Graph => Asset as ScriptGraph;

    protected override Asset? LoadAsset(Guid guid) => AssetDatabase.Load<ScriptGraph>(guid);
    protected override Asset? LoadedAsset(Guid guid) => AssetDatabase.Get<ScriptGraph>(guid);
    protected override GraphDomain<GraphNodeRecord> CreateDomain(Asset asset) => new ScriptGraphDomain((ScriptGraph)asset);

    protected override void DrawInspector(Paper paper, object item)
    {
        if (item is not GraphNodeRecord node) return;
        ScriptGraphNodeDef? def = ScriptGraphNodes.Get(node.Type);
        EditorGUI.SectionHeader(paper, "sg_node", def?.DisplayName ?? node.Type, first: true);
        if (def != null) EditorGUI.Note(paper, "sg_nodeDesc", def.Description);

        switch (node.Type)
        {
            case ScriptGraphNodes.OnKeyDown:
            case ScriptGraphNodes.GetKey:
                EditorGUI.Row(paper, "sg_key", "Key", () =>
                    Origami.TextField(paper, "sg_key_v", Text(node, "Key", "Space"), v =>
                        Set("Set Key", () => node.Properties["Key"] = NodeValue.FromText(v.Trim()), false))
                        .Placeholder("Space").CommitOnEnter().Show());
                EditorGUI.Note(paper, "sg_keyHint", "Use a KeyCode name, such as Space, W or LeftShift.");
                break;

            case ScriptGraphNodes.Float:
                EditorGUI.Row(paper, "sg_float", "Value", () =>
                    Origami.NumericField<float>(paper, "sg_float_v", Number(node, "Value"), v => SetNumber(node, "Value", v)).Show());
                break;

            case ScriptGraphNodes.Int:
                EditorGUI.Row(paper, "sg_int", "Value", () =>
                    Origami.NumericField<int>(paper, "sg_int_v", Integer(node, "Int"), v =>
                        Set("Set Int", () => node.Properties["Int"] = NodeValue.FromInteger(v), false)).Show());
                break;

            case ScriptGraphNodes.Bool:
                EditorGUI.SettingsToggle(paper, "sg_bool", "Value", Flag(node, "Bool"),
                    v => Set("Set Bool", () => node.Properties["Bool"] = NodeValue.FromFlag(v), false), separator: false);
                break;

            case ScriptGraphNodes.String:
                EditorGUI.Row(paper, "sg_text", "Text", () =>
                    Origami.TextField(paper, "sg_text_v", Text(node, "Text"), v =>
                        Set("Set Text", () => node.Properties["Text"] = NodeValue.FromText(v), false)).Show());
                break;

            case ScriptGraphNodes.Vector3:
                EditorGUI.Row(paper, "sg_vec", "Vector", () =>
                {
                    Float3 value = Vector(node, "Vector");
                    using (paper.Row("sg_vecRow").Width(UnitValue.Stretch()).Height(UnitValue.Auto).Gap(4).Enter())
                    {
                        Origami.NumericField<float>(paper, "sg_vec_x", value.X, v => SetVector(node, "Vector", new Float3(v, value.Y, value.Z))).Width(UnitValue.Stretch()).Show();
                        Origami.NumericField<float>(paper, "sg_vec_y", value.Y, v => SetVector(node, "Vector", new Float3(value.X, v, value.Z))).Width(UnitValue.Stretch()).Show();
                        Origami.NumericField<float>(paper, "sg_vec_z", value.Z, v => SetVector(node, "Vector", new Float3(value.X, value.Y, v))).Width(UnitValue.Stretch()).Show();
                    }
                });
                break;

            case ScriptGraphNodes.Compare:
            case ScriptGraphNodes.Logic:
                EditorGUI.Row(paper, "sg_op", "Operator", () =>
                {
                    string[] options = node.Type == ScriptGraphNodes.Compare
                        ? ["Greater", "Less", "Greater Or Equal", "Less Or Equal", "Equal", "Not Equal"]
                        : ["And", "Or", "Xor", "Not"];
                    int current = Math.Max(0, Array.IndexOf(options, Text(node, "Operator", options[0])));
                    Origami.Dropdown(paper, "sg_op_v", current, v =>
                        Set("Set Operator", () => node.Properties["Operator"] = NodeValue.FromText(options[v]), false), options).Show();
                });
                break;

            case ScriptGraphNodes.VectorSplit:
                EditorGUI.Row(paper, "sg_axis", "Axis", () =>
                {
                    string[] options = ["X", "Y", "Z"];
                    int current = Math.Max(0, Array.IndexOf(options, Text(node, "Axis", "X")));
                    Origami.Dropdown(paper, "sg_axis_v", current, v =>
                        Set("Set Axis", () => node.Properties["Axis"] = NodeValue.FromText(options[v]), false), options).Show();
                });
                break;

            case ScriptGraphNodes.SetVariable:
            case ScriptGraphNodes.GetVariable:
                DrawVariablePicker(paper, node);
                break;

            case ScriptGraphNodes.OnStart:
            case ScriptGraphNodes.OnUpdate:
            case ScriptGraphNodes.OnLateUpdate:
            case ScriptGraphNodes.OnEnable:
            case ScriptGraphNodes.OnDisable:
                EditorGUI.Note(paper, "sg_event", "Wire this event's Then pin to the actions that should run.");
                break;

            default:
                if (ScriptGraphApiCatalog.Find(node.Type) is { } api)
                    DrawApiInspector(paper, node, api);
                else
                    EditorGUI.Note(paper, "sg_flow", "This node runs from its exec input and continues from its Then output.");
                break;
        }
    }

    private void DrawApiInspector(Paper paper, GraphNodeRecord node, ScriptApiMember api)
    {
        EditorGUI.Note(paper, "sg_apiDesc", api.Description);
        EditorGUI.Note(paper, "sg_apiSource", api.Target == ScriptApiTarget.Component
            ? "Read from a " + api.DeclaringType.Replace("global::", "") + " on this object."
            : "Engine API from " + api.DeclaringType.Replace("global::", "") + ".");

        if (api.ParameterTypes.Length == 0)
        {
            EditorGUI.Note(paper, "sg_apiNoArgs", "This member takes no arguments.");
            return;
        }

        for (int i = 0; i < api.ParameterTypes.Length; i++)
        {
            int parameter = i;
            string key = "arg" + parameter;
            ScriptPinKind kind = api.ParameterKinds[parameter];
            string label = api.Definition.Inputs[parameter + (api.HasExecInput ? 1 : 0)].Label;
            switch (kind)
            {
                case ScriptPinKind.Float:
                    EditorGUI.Row(paper, $"sg_api{parameter}", label, () =>
                        Origami.NumericField<float>(paper, $"sg_api{parameter}_v", Number(node, key), v =>
                            Set("Set API Argument", () => node.Properties[key] = NodeValue.FromNumber(v), false)).Show());
                    break;
                case ScriptPinKind.Int:
                    EditorGUI.Row(paper, $"sg_api{parameter}", label, () =>
                        Origami.NumericField<int>(paper, $"sg_api{parameter}_v", Integer(node, key), v =>
                            Set("Set API Argument", () => node.Properties[key] = NodeValue.FromInteger(v), false)).Show());
                    break;
                case ScriptPinKind.Bool:
                    EditorGUI.SettingsToggle(paper, $"sg_api{parameter}", label, Flag(node, key),
                        v => Set("Set API Argument", () => node.Properties[key] = NodeValue.FromFlag(v), false), separator: false);
                    break;
                case ScriptPinKind.String:
                    EditorGUI.Row(paper, $"sg_api{parameter}", label, () =>
                        Origami.TextField(paper, $"sg_api{parameter}_v", Text(node, key), v =>
                            Set("Set API Argument", () => node.Properties[key] = NodeValue.FromText(v), false))
                            .Placeholder(api.ParameterTypes[parameter].Replace("global::", "")).CommitOnEnter().Show());
                    break;
                case ScriptPinKind.Vector2:
                case ScriptPinKind.Vector3:
                case ScriptPinKind.Color:
                case ScriptPinKind.Quaternion:
                    EditorGUI.Row(paper, $"sg_api{parameter}", label, () =>
                    {
                        Float3 value = Vector(node, key);
                        using (paper.Row($"sg_api{parameter}_row").Width(UnitValue.Stretch()).Height(UnitValue.Auto).Gap(4).Enter())
                        {
                            Origami.NumericField<float>(paper, $"sg_api{parameter}_x", value.X, v => SetVector(node, key, new Float3(v, value.Y, value.Z))).Width(UnitValue.Stretch()).Show();
                            Origami.NumericField<float>(paper, $"sg_api{parameter}_y", value.Y, v => SetVector(node, key, new Float3(value.X, v, value.Z))).Width(UnitValue.Stretch()).Show();
                            Origami.NumericField<float>(paper, $"sg_api{parameter}_z", value.Z, v => SetVector(node, key, new Float3(value.X, value.Y, v))).Width(UnitValue.Stretch()).Show();
                        }
                    });
                    break;
                default:
                    EditorGUI.Note(paper, $"sg_api{parameter}_hint", label + " is wired from another node.");
                    break;
            }
        }
    }

    private string _apiSearch = "";

    protected override void DrawPanelSection(Paper paper, Scribe.FontFile font, OrigamiMetrics m, float width, float height)
    {
        if (Graph.IsNotValid()) return;

        Origami.ScrollView(paper, "sg_panel_scroll", width - 2f, height)
            .Body(() => DrawPanelContents(paper, font, width));
    }

    private void DrawPanelContents(Paper paper, Scribe.FontFile font, float width)
    {
        DrawApiBrowser(paper, font);

        EditorGUI.SectionHeader(paper, "sg_compileHeader", "Build");
        EditorGUI.Row(paper, "sg_compileRow", "", () =>
            Origami.Button(paper, "sg_compile", "Compile to DLL", Compile).Width(UnitValue.Stretch()).Show());

        if (Graph.AssemblyPath.Length > 0)
        {
            EditorGUI.Note(paper, "sg_built", "Compiled assembly: " + Graph.AssemblyPath);
            EditorGUI.Note(paper, "sg_builtHint", "Add a Script Graph Runner component and assign this graph.");
        }
        else
        {
            EditorGUI.Note(paper, "sg_notBuilt", "Not compiled yet. Compile to produce a .dll for this graph.");
        }

        if (_compileMessage.Length > 0)
            EditorGUI.Note(paper, "sg_message", _compileMessage);

        EditorGUI.SectionHeader(paper, "sg_vars", "Variables", compact: true);
        if (Graph.Variables.Count == 0)
            EditorGUI.Note(paper, "sg_noVarsYet", "Variables are fields the graph can read and write. Add one below.");

        for (int i = 0; i < Graph.Variables.Count; i++)
        {
            ScriptGraphVariable variable = Graph.Variables[i];
            int index = i;
            using (paper.Column($"sg_var{index}").Width(UnitValue.Stretch()).Height(UnitValue.Auto)
                .Margin(0, 0, 0, 6).Padding(6, 6, 4, 6).Gap(3)
                .Rounded(Origami.Current.Metrics.SmallRounding)
                .BackgroundColor(EditorTheme.Neutral300)
                .BorderColor(EditorTheme.BorderSoft).BorderWidth(1).Enter())
            {
                using (paper.Row($"sg_varHead{index}").Width(UnitValue.Stretch()).Height(24).Gap(4).Enter())
                {
                    paper.Box($"sg_varDrag{index}").Width(18).Height(24)
                        .Text(EditorIcons.Bars, EditorTheme.DefaultFont!)
                        .TextColor(EditorTheme.Ink300).FontSize(11f).Alignment(TextAlignment.MiddleCenter)
                        .Cursor(PaperCursor.Grab)
                        .Tooltip("Drag onto the graph to add a Get node. Use G or S for either node.")
                        .OnDragStart(_ => DragDrop.StartDrag(new ScriptVariableDrag(variable.Name, false)));

                    Origami.Button(paper, $"sg_varGet{index}", "G", () => AddVariableNode(variable.Name, false))
                        .Small().Width(24).Tooltip("Add a Get node").Show();
                    Origami.Button(paper, $"sg_varSet{index}", "S", () => AddVariableNode(variable.Name, true))
                        .Small().Width(24).Tooltip("Add a Set node").Show();

                    Origami.TextField(paper, $"sg_varName{index}", variable.Name, v =>
                        Set("Rename Variable", () => RenameVariable(variable, v), false))
                        .Width(UnitValue.Stretch()).OnEditEnd(() =>
                            Set("Clean Variable Name", () => RenameVariable(variable, CleanVariableName(variable.Name)), false))
                        .Show();

                    Origami.EnumDropdown(paper, $"sg_varType{index}", variable.Type, v =>
                        Set("Set Variable Type", () => variable.Type = v)).Width(82).Show();

                    Origami.IconButton(paper, $"sg_varUp{index}", EditorIcons.ArrowUp_I,
                        () => Set("Move Variable Up", () =>
                        {
                            if (index <= 0) return;
                            (Graph.Variables[index - 1], Graph.Variables[index]) = (Graph.Variables[index], Graph.Variables[index - 1]);
                        })).Small().Width(22).Height(22).Show();

                    Origami.IconButton(paper, $"sg_varDel{index}", EditorIcons.Xmark_I,
                        () => Set("Delete Variable", () => Graph.Variables.RemoveAt(index))).Small().Width(22).Height(22).Show();
                }

                DrawVariableDefault(paper, variable, index);

                using (paper.Row($"sg_varNodes{index}").Width(UnitValue.Stretch()).Height(22).Gap(4).Enter())
                {
                    Origami.Button(paper, $"sg_varGet{index}", "Get", () => AddVariableNode(variable.Name, false)).Small().Height(22).Show();
                    Origami.Button(paper, $"sg_varSet{index}", "Set", () => AddVariableNode(variable.Name, true)).Small().Height(22).Show();
                    paper.Box($"sg_varHint{index}").Width(UnitValue.Stretch()).Height(22).IsNotInteractable()
                        .Text("Drag the handle onto the canvas", EditorTheme.DefaultFont!)
                        .TextColor(EditorTheme.Ink300).FontSize(EditorTheme.FontSizeSmall)
                        .Alignment(TextAlignment.MiddleRight);
                }
            }
        }

        Origami.Button(paper, "sg_varAdd", "Add Variable", () =>
        {
            int n = Graph.Variables.Count + 1;
            Set("Add Variable", () => Graph.Variables.Add(new ScriptGraphVariable { Name = $"value{n}" }));
        }).Subtle().Width(UnitValue.Stretch()).Show();
    }

    private void DrawApiBrowser(Paper paper, Scribe.FontFile font)
    {
        EditorGUI.SectionHeader(paper, "sg_apiHeader", "Engine API", first: true);
        EditorGUI.Note(paper, "sg_apiHint", "Every public engine method, property and field becomes a node with pins that follow its signature.");

        Origami.TextField(paper, "sg_apiSearch", _apiSearch, v => _apiSearch = v)
            .Placeholder("Search animation, sprite, transform...").Show();

        string query = _apiSearch.Trim();
        int shown = 0;
        foreach (ScriptApiMember api in ScriptGraphApiCatalog.Members)
        {
            if (query.Length > 0)
            {
                if (!api.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                    !api.Category.Contains(query, StringComparison.OrdinalIgnoreCase) &&
                    !api.Description.Contains(query, StringComparison.OrdinalIgnoreCase)) continue;
            }
            else if (!IsAnimationOrSpriteApi(api)) continue;

            if (shown++ >= 10) break;

            using (paper.Row($"sg_api_{shown}").Width(UnitValue.Stretch()).Height(24).Gap(4).Enter())
            {
                paper.Box($"sg_apiName_{shown}").Width(UnitValue.Stretch()).Height(24).IsNotInteractable()
                    .Text(api.DisplayName, font).TextColor(EditorTheme.Ink500)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft).TextTruncate()
                    .Tooltip(api.Description);
                Origami.Button(paper, $"sg_apiAdd_{shown}", "+", () =>
                    Set($"Add {api.DisplayName}", () => View.Add(api.TypeId, NextFreeSpot())))
                    .Small().Width(26).Tooltip("Add this API node").Show();
            }
        }

        if (shown == 0)
            EditorGUI.Note(paper, "sg_apiNone", query.Length == 0
                ? "No animation or sprite API matched. Type a type or member name to search all APIs."
                : "No API node matches that search.");

        Origami.Button(paper, "sg_apiPicker", "Browse All API Nodes", () => OpenPicker(paper, NextFreeSpot(), null, null, frame: true))
            .Subtle().Width(UnitValue.Stretch()).Show();
    }

    private static bool IsAnimationOrSpriteApi(ScriptApiMember api)
        => api.DeclaringType.Contains("Animation", StringComparison.OrdinalIgnoreCase) ||
           api.DeclaringType.Contains("Animator", StringComparison.OrdinalIgnoreCase) ||
           api.DeclaringType.Contains("Sprite", StringComparison.OrdinalIgnoreCase) ||
           api.Category.Contains("Animation", StringComparison.OrdinalIgnoreCase) ||
           api.Category.Contains("Sprite", StringComparison.OrdinalIgnoreCase);

    private void DrawVariableDefault(Paper paper, ScriptGraphVariable variable, int index)
    {
        switch (variable.Type)
        {
            case ScriptPinKind.Int:
                EditorGUI.Row(paper, $"sg_varDef{index}", "Default", () =>
                    Origami.NumericField<int>(paper, $"sg_varDefV{index}", variable.Integer, v =>
                        Set("Set Variable Default", () => variable.Integer = v, false)).Show());
                break;
            case ScriptPinKind.Bool:
                EditorGUI.SettingsToggle(paper, $"sg_varDef{index}", "Default", variable.Flag,
                    v => Set("Set Variable Default", () => variable.Flag = v, false), separator: false);
                break;
            case ScriptPinKind.String:
                EditorGUI.Row(paper, $"sg_varDef{index}", "Default", () =>
                    Origami.TextField(paper, $"sg_varDefV{index}", variable.Text, v =>
                        Set("Set Variable Default", () => variable.Text = v, false)).CommitOnEnter().Show());
                break;
            case ScriptPinKind.Vector2:
            case ScriptPinKind.Vector3:
            case ScriptPinKind.Color:
            case ScriptPinKind.Quaternion:
                EditorGUI.Row(paper, $"sg_varDef{index}", "Default", () =>
                {
                    Float3 value = variable.Vector;
                    using (paper.Row($"sg_varDefRow{index}").Width(UnitValue.Stretch()).Height(UnitValue.Auto).Gap(4).Enter())
                    {
                        Origami.NumericField<float>(paper, $"sg_varDefX{index}", value.X, v => Set("Set Variable Default", () => variable.Vector = new Float3(v, value.Y, value.Z), false)).Width(UnitValue.Stretch()).Show();
                        Origami.NumericField<float>(paper, $"sg_varDefY{index}", value.Y, v => Set("Set Variable Default", () => variable.Vector = new Float3(value.X, v, value.Z), false)).Width(UnitValue.Stretch()).Show();
                        Origami.NumericField<float>(paper, $"sg_varDefZ{index}", value.Z, v => Set("Set Variable Default", () => variable.Vector = new Float3(value.X, value.Y, v), false)).Width(UnitValue.Stretch()).Show();
                    }
                });
                break;
            default:
                EditorGUI.Row(paper, $"sg_varDef{index}", "Default", () =>
                    Origami.NumericField<float>(paper, $"sg_varDefV{index}", variable.Number, v =>
                        Set("Set Variable Default", () => variable.Number = v, false)).Show());
                break;
        }
    }

    private void DrawVariablePicker(Paper paper, GraphNodeRecord node)
    {
        string current = Text(node, "Variable");
        string[] names = new string[Graph!.Variables.Count];
        for (int i = 0; i < names.Length; i++) names[i] = Graph.Variables[i].Name;
        int selected = Array.IndexOf(names, current);

        EditorGUI.Row(paper, "sg_varPick", "Variable", () =>
        {
            if (names.Length == 0)
            {
                EditorGUI.Note(paper, "sg_noVars", "Add a variable in the side panel first.");
                return;
            }
            Origami.Dropdown(paper, "sg_varPick_v", Math.Max(0, selected), v =>
                Set("Set Variable", () => node.Properties["Variable"] = NodeValue.FromText(names[v]), false), names).Show();
        });
    }

    protected override void BeforeDraw()
    {
        _view.Edit = (description, change) => Set(description, change, false);
        _view.EditRebuild = (description, change) => Set(description, change, true);
    }

    /// <summary>A variable dropped from the side panel becomes a Get or Set node where it was let go.</summary>
    protected override void Drop(DragPayload payload, Float2 at)
    {
        if (payload is not ScriptVariableDrag) return;
        ScriptVariableDrag? drag = DragDrop.AcceptDrop<ScriptVariableDrag>(true);
        if (drag == null) return;
        AddVariableNode(drag.Name, drag.Set, at);
    }

    private void AddVariableNode(string name, bool set, Float2? at = null)
    {
        if (Graph.IsNotValid() || string.IsNullOrWhiteSpace(name)) return;
        GraphNodeRecord? added = null;
        Set(set ? $"Add Set {name}" : $"Add Get {name}", () =>
        {
            Float2 position = at ?? new Float2(240f, 120f + Graph!.Nodes.Count * 6f);
            added = View.Add(set ? ScriptGraphNodes.SetVariable : ScriptGraphNodes.GetVariable, position);
            if (added != null) added.Properties["Variable"] = NodeValue.FromText(name);
        });
        if (added != null) Controller.SelectNodes([added.Id]);
    }

    private void RenameVariable(ScriptGraphVariable variable, string name)
    {
        string oldName = variable.Name;
        string newName = name ?? string.Empty;
        if (newName == oldName) return;

        variable.Name = newName;
        if (oldName.Length == 0) return;
        foreach (GraphNodeRecord node in Graph!.Nodes)
            if (node.Type is ScriptGraphNodes.GetVariable or ScriptGraphNodes.SetVariable &&
                Text(node, "Variable") == oldName)
                node.Properties["Variable"] = NodeValue.FromText(newName);
    }

    private static string CleanVariableName(string name)
    {
        string trimmed = (name ?? string.Empty).Trim();
        if (trimmed.Length == 0) return "value";

        var text = new System.Text.StringBuilder(trimmed.Length);
        foreach (char c in trimmed)
            text.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
        if (text.Length == 0) return "value";
        if (char.IsDigit(text[0])) text.Insert(0, '_');
        return text.ToString();
    }

    private void Compile()
    {
        if (Graph.IsNotValid()) return;
        if (ScriptGraphCompiler.Compile(Graph!, out string message))
        {
            _compileMessage = "Compiled to " + message;
            EditorAssetBackend.Instance?.SaveAsset(Graph!);
            EditorUtility.ClearAssetDirty(Graph!.AssetID);
        }
        else
        {
            _compileMessage = message;
        }
        EditorUtility.SetDirty(Graph!);
    }

    private void SetNumber(GraphNodeRecord node, string key, float value)
        => Set("Set " + key, () => node.Properties[key] = NodeValue.FromNumber(value), false);

    private void SetVector(GraphNodeRecord node, string key, Float3 value)
        => Set("Set " + key, () => node.Properties[key] = NodeValue.FromVector(value), false);

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
}


/// <summary>A variable dragged out of the side panel, to be dropped as a Get or Set node.</summary>
internal sealed class ScriptVariableDrag(string name, bool set) : DragPayload
{
    public string Name { get; } = name;
    public bool Set { get; } = set;

    public override string DisplayName => Set ? $"Set {Name}" : $"Get {Name}";
    public override string Icon => EditorIcons.CodeFork;
}
