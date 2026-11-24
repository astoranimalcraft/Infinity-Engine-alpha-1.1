// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Infinity.Runtime.Animation;
using Infinity.Vector;

namespace Infinity.Runtime.Graphs;

/// <summary>What a script graph pin carries. Exec pins carry control flow; the rest carry values.</summary>
public enum ScriptPinKind : byte
{
    Exec,
    Float,
    Int,
    Bool,
    Vector3,
    String,
    GameObject,
    /// <summary>A two-component vector, used by sprites, UVs and screen-space APIs.</summary>
    Vector2,
    /// <summary>An RGBA colour.</summary>
    Color,
    /// <summary>A rotation, shown as Euler angles in the editor.</summary>
    Quaternion,
}

/// <summary>One input or output of a script graph node.</summary>
public sealed record ScriptGraphPin(string Id, string Label, ScriptPinKind Kind);

/// <summary>What a script graph node is and what it plugs into.</summary>
public sealed record ScriptGraphNodeDef(
    string Type,
    string DisplayName,
    string Category,
    string Description,
    ScriptGraphPin[] Inputs,
    ScriptGraphPin[] Outputs)
{
    public string Title => DisplayName;
}

/// <summary>The node catalogue of the visual C# script editor.</summary>
public static class ScriptGraphNodes
{
    public const string OnStart = "script.onStart";
    public const string OnUpdate = "script.onUpdate";
    public const string OnLateUpdate = "script.onLateUpdate";
    public const string OnEnable = "script.onEnable";
    public const string OnDisable = "script.onDisable";
    public const string OnKeyDown = "script.onKeyDown";
    public const string ConditionalRoute = "script.conditionalRoute";
    public const string Log = "script.log";
    public const string SetActive = "script.setActive";
    public const string SetPosition = "script.setPosition";
    public const string MoveBy = "script.moveBy";
    public const string RotateBy = "script.rotateBy";
    public const string SetLocalScale = "script.setLocalScale";
    public const string SetVariable = "script.setVariable";
    public const string Float = "script.float";
    public const string Int = "script.int";
    public const string Bool = "script.bool";
    public const string String = "script.string";
    public const string Vector3 = "script.vector3";
    public const string Add = "script.add";
    public const string Subtract = "script.subtract";
    public const string Multiply = "script.multiply";
    public const string Divide = "script.divide";
    public const string Random = "script.random";
    public const string Compare = "script.compare";
    public const string Logic = "script.logic";
    public const string Lerp = "script.lerp";
    public const string Clamp = "script.clamp";
    public const string VectorSplit = "script.vectorSplit";
    public const string VectorJoin = "script.vectorJoin";
    public const string GetPosition = "script.getPosition";
    public const string GetLocalPosition = "script.getLocalPosition";
    public const string DeltaTime = "script.deltaTime";
    public const string GetKey = "script.getKey";
    public const string GetMousePosition = "script.getMousePosition";
    public const string GetVariable = "script.getVariable";

    /// <summary>The old name of <see cref="ConditionalRoute"/>, kept so saved graphs still load.</summary>
    public const string Branch = ConditionalRoute;

    private static readonly Dictionary<string, ScriptGraphNodeDef> s_defs = Build();

    public static IReadOnlyDictionary<string, ScriptGraphNodeDef> All => s_defs;

    public static ScriptGraphNodeDef? Get(string type)
    {
        if (s_defs.TryGetValue(type, out ScriptGraphNodeDef? def)) return def;
        return ScriptGraphApiCatalog.Definition(type);
    }

    private static Dictionary<string, ScriptGraphNodeDef> Build()
    {
        ScriptGraphPin P(string id, string label, ScriptPinKind kind) => new(id, label, kind);
        ScriptGraphPin Exec(string id) => new(id, id, ScriptPinKind.Exec);

        var defs = new Dictionary<string, ScriptGraphNodeDef>(StringComparer.Ordinal);
        void Add(ScriptGraphNodeDef def) => defs[def.Type] = def;

        Add(new ScriptGraphNodeDef(OnStart, "On Start", "Events", "Runs once when the object starts.",
            [], [Exec("Then")]));
        Add(new ScriptGraphNodeDef(OnUpdate, "On Update", "Events", "Runs every frame.",
            [], [Exec("Then")]));
        Add(new ScriptGraphNodeDef(OnLateUpdate, "On Late Update", "Events", "Runs after every Update.",
            [], [Exec("Then")]));
        Add(new ScriptGraphNodeDef(OnEnable, "On Enable", "Events", "Runs when the object is enabled.",
            [], [Exec("Then")]));
        Add(new ScriptGraphNodeDef(OnDisable, "On Disable", "Events", "Runs when the object is disabled.",
            [], [Exec("Then")]));
        Add(new ScriptGraphNodeDef(OnKeyDown, "On Key Down", "Events", "Runs the frame a key goes down.",
            [P("Key", "Key", ScriptPinKind.String)], [Exec("Then")]));

        Add(new ScriptGraphNodeDef(ConditionalRoute, "Conditional Route", "Flow", "Sends execution down the True or False path.",
            [Exec("In"), P("Condition", "Condition", ScriptPinKind.Bool)],
            [Exec("True"), Exec("False")]));

        Add(new ScriptGraphNodeDef(Log, "Log", "Actions", "Writes a message to the console.",
            [Exec("In"), P("Message", "Message", ScriptPinKind.String)], [Exec("Then")]));
        Add(new ScriptGraphNodeDef(SetActive, "Set Active", "Actions", "Shows or hides the object.",
            [Exec("In"), P("Active", "Active", ScriptPinKind.Bool)], [Exec("Then")]));
        Add(new ScriptGraphNodeDef(SetPosition, "Set Position", "Actions", "Sets the world position.",
            [Exec("In"), P("Position", "Position", ScriptPinKind.Vector3)], [Exec("Then")]));
        Add(new ScriptGraphNodeDef(MoveBy, "Move By", "Actions", "Moves by a vector each second.",
            [Exec("In"), P("Delta", "Delta", ScriptPinKind.Vector3)], [Exec("Then")]));
        Add(new ScriptGraphNodeDef(RotateBy, "Rotate By", "Actions", "Turns by Euler angles each second.",
            [Exec("In"), P("Euler", "Euler", ScriptPinKind.Vector3)], [Exec("Then")]));
        Add(new ScriptGraphNodeDef(SetLocalScale, "Set Local Scale", "Actions", "Sets the local scale.",
            [Exec("In"), P("Scale", "Scale", ScriptPinKind.Vector3)], [Exec("Then")]));
        Add(new ScriptGraphNodeDef(SetVariable, "Set Variable", "Variables", "Writes a graph variable.",
            [Exec("In"), P("Value", "Value", ScriptPinKind.Float)], [Exec("Then")]));

        Add(new ScriptGraphNodeDef(Float, "Float", "Values", "A number.",
            [], [P("Out", "Out", ScriptPinKind.Float)]));
        Add(new ScriptGraphNodeDef(Int, "Int", "Values", "A whole number.",
            [], [P("Out", "Out", ScriptPinKind.Int)]));
        Add(new ScriptGraphNodeDef(Bool, "Bool", "Values", "True or false.",
            [], [P("Out", "Out", ScriptPinKind.Bool)]));
        Add(new ScriptGraphNodeDef(String, "String", "Values", "Text.",
            [], [P("Out", "Out", ScriptPinKind.String)]));
        Add(new ScriptGraphNodeDef(Vector3, "Vector 3", "Values", "Three numbers.",
            [], [P("Out", "Out", ScriptPinKind.Vector3)]));
        Add(new ScriptGraphNodeDef(ScriptGraphNodes.Add, "Add", "Math", "A + B.",
            [P("A", "A", ScriptPinKind.Float), P("B", "B", ScriptPinKind.Float)], [P("Out", "Out", ScriptPinKind.Float)]));
        Add(new ScriptGraphNodeDef(Subtract, "Subtract", "Math", "A - B.",
            [P("A", "A", ScriptPinKind.Float), P("B", "B", ScriptPinKind.Float)], [P("Out", "Out", ScriptPinKind.Float)]));
        Add(new ScriptGraphNodeDef(Multiply, "Multiply", "Math", "A * B.",
            [P("A", "A", ScriptPinKind.Float), P("B", "B", ScriptPinKind.Float)], [P("Out", "Out", ScriptPinKind.Float)]));
        Add(new ScriptGraphNodeDef(Divide, "Divide", "Math", "A / B.",
            [P("A", "A", ScriptPinKind.Float), P("B", "B", ScriptPinKind.Float)], [P("Out", "Out", ScriptPinKind.Float)]));
        Add(new ScriptGraphNodeDef(Random, "Random", "Math", "A random number between Min and Max.",
            [P("Min", "Min", ScriptPinKind.Float), P("Max", "Max", ScriptPinKind.Float)], [P("Out", "Out", ScriptPinKind.Float)]));
        Add(new ScriptGraphNodeDef(Compare, "Compare", "Math", "Compares A and B and returns a bool.",
            [P("A", "A", ScriptPinKind.Float), P("B", "B", ScriptPinKind.Float), P("Operator", "Operator", ScriptPinKind.String)],
            [P("Out", "Out", ScriptPinKind.Bool)]));
        Add(new ScriptGraphNodeDef(Logic, "Logic", "Math", "Combines two bools with And, Or or Xor.",
            [P("A", "A", ScriptPinKind.Bool), P("B", "B", ScriptPinKind.Bool), P("Operator", "Operator", ScriptPinKind.String)],
            [P("Out", "Out", ScriptPinKind.Bool)]));
        Add(new ScriptGraphNodeDef(Lerp, "Blend", "Math", "Mixes A and B by T.",
            [P("A", "A", ScriptPinKind.Vector3), P("B", "B", ScriptPinKind.Vector3), P("T", "T", ScriptPinKind.Float)],
            [P("Out", "Out", ScriptPinKind.Vector3)]));
        Add(new ScriptGraphNodeDef(Clamp, "Clamp", "Math", "Keeps A between Min and Max.",
            [P("A", "A", ScriptPinKind.Float), P("Min", "Min", ScriptPinKind.Float), P("Max", "Max", ScriptPinKind.Float)],
            [P("Out", "Out", ScriptPinKind.Float)]));
        Add(new ScriptGraphNodeDef(VectorSplit, "Split Vector", "Values", "Reads one axis of a vector.",
            [P("Vector", "Vector", ScriptPinKind.Vector3), P("Axis", "Axis", ScriptPinKind.String)],
            [P("Out", "Out", ScriptPinKind.Float)]));
        Add(new ScriptGraphNodeDef(VectorJoin, "Join Vector", "Values", "Builds a vector from three numbers.",
            [P("X", "X", ScriptPinKind.Float), P("Y", "Y", ScriptPinKind.Float), P("Z", "Z", ScriptPinKind.Float)],
            [P("Out", "Out", ScriptPinKind.Vector3)]));
        Add(new ScriptGraphNodeDef(GetPosition, "Get Position", "Values", "The world position.",
            [], [P("Out", "Out", ScriptPinKind.Vector3)]));
        Add(new ScriptGraphNodeDef(GetLocalPosition, "Get Local Position", "Values", "The local position.",
            [], [P("Out", "Out", ScriptPinKind.Vector3)]));
        Add(new ScriptGraphNodeDef(DeltaTime, "Delta Time", "Values", "Seconds since the last frame.",
            [], [P("Out", "Out", ScriptPinKind.Float)]));
        Add(new ScriptGraphNodeDef(GetKey, "Get Key", "Input", "Whether a key is held.",
            [P("Key", "Key", ScriptPinKind.String)], [P("Out", "Out", ScriptPinKind.Bool)]));
        Add(new ScriptGraphNodeDef(GetMousePosition, "Get Mouse Position", "Input", "The pointer in window pixels.",
            [], [P("Out", "Out", ScriptPinKind.Vector2)]));
        Add(new ScriptGraphNodeDef(GetVariable, "Get Variable", "Variables", "Reads a graph variable.",
            [], [P("Out", "Out", ScriptPinKind.Float)]));

        return defs;
    }

    public static GraphNodeRecord Create(string type, Float2 position, string id)
    {
        var record = new GraphNodeRecord
        {
            Id = id,
            Type = type,
            EditorPosition = position,
        };

        ScriptGraphNodeDef? def = Get(type);
        if (def != null)
        {
            for (int i = 0; i < def.Inputs.Length; i++)
            {
                record.Inputs.Add(new GraphInputRecord());
                ScriptGraphPin pin = def.Inputs[i];
                if (pin.Id.StartsWith("arg", StringComparison.Ordinal))
                    record.Properties[pin.Id] = DefaultProperty(pin.Kind);
            }
        }

        record.Properties["Value"] = NodeValue.FromNumber(0f);
        record.Properties["Int"] = NodeValue.FromInteger(0);
        record.Properties["Bool"] = NodeValue.FromFlag(false);
        record.Properties["Text"] = NodeValue.FromText(string.Empty);
        record.Properties["Vector"] = NodeValue.FromVector(Float3.Zero);
        record.Properties["Key"] = NodeValue.FromText("Space");
        record.Properties["Variable"] = NodeValue.FromText(string.Empty);
        record.Properties["Operator"] = NodeValue.FromText("Greater");
        record.Properties["Axis"] = NodeValue.FromText("X");
        return record;
    }

    private static NodeValue DefaultProperty(ScriptPinKind kind) => kind switch
    {
        ScriptPinKind.Int => NodeValue.FromInteger(0),
        ScriptPinKind.Bool => NodeValue.FromFlag(false),
        ScriptPinKind.String => NodeValue.FromText(string.Empty),
        ScriptPinKind.Vector2 or ScriptPinKind.Vector3 or ScriptPinKind.Color or ScriptPinKind.Quaternion => NodeValue.FromVector(Float3.Zero),
        _ => NodeValue.FromNumber(0f),
    };
}
