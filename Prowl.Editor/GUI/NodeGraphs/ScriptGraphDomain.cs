// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Infinity.Echo;
using Infinity.Editor.GraphEditor;
using Infinity.Runtime;
using Infinity.Runtime.Animation;
using Infinity.Runtime.Graphs;
using Infinity.Vector;

using Color = System.Drawing.Color;

namespace Infinity.Editor.NodeGraphs;

/// <summary>A visual C# script graph as the shared node editor works on it.</summary>
public sealed class ScriptGraphDomain : GraphDomain<GraphNodeRecord>
{
    public const string DataOutput = "out0";

    private static readonly Dictionary<ScriptPinKind, PortKind> s_kinds = new()
    {
        [ScriptPinKind.Exec] = new PortKind("exec", "Execution", Color.FromArgb(235, 235, 235)),
        [ScriptPinKind.Float] = new PortKind("float", "Float", Color.FromArgb(190, 220, 120)),
        [ScriptPinKind.Int] = new PortKind("int", "Int", Color.FromArgb(160, 210, 150)),
        [ScriptPinKind.Bool] = new PortKind("bool", "Bool", Color.FromArgb(220, 120, 120)),
        [ScriptPinKind.Vector3] = new PortKind("vec3", "Vector 3", Color.FromArgb(120, 160, 240)),
        [ScriptPinKind.String] = new PortKind("string", "String", Color.FromArgb(230, 170, 120)),
        [ScriptPinKind.GameObject] = new PortKind("gameobject", "Game Object", Color.FromArgb(120, 200, 220)),
        [ScriptPinKind.Vector2] = new PortKind("vec2", "Vector 2", Color.FromArgb(120, 200, 220)),
        [ScriptPinKind.Color] = new PortKind("color", "Color", Color.FromArgb(240, 190, 110)),
        [ScriptPinKind.Quaternion] = new PortKind("quat", "Rotation", Color.FromArgb(230, 140, 235)),
    };

    public ScriptGraphDomain(ScriptGraph graph) : base(graph) => Graph = graph;

    public ScriptGraph Graph { get; }

    public override List<GraphNodeRecord> Nodes => Graph.Nodes;
    public override List<GraphGroupRecord> Groups => Graph.Groups;
    public override List<GraphNoteRecord> Notes => Graph.Notes;

    public override EchoObject Snapshot() => Serializer.Serialize(typeof(object), Graph);

    public override bool Restore(EchoObject snapshot, bool compiles)
    {
        ScriptGraph? restored = Serializer.Deserialize<ScriptGraph>(snapshot.Clone());
        if (restored.IsNotValid()) return false;
        Graph.Nodes = restored.Nodes;
        Graph.Groups = restored.Groups;
        Graph.Notes = restored.Notes;
        Graph.Variables = restored.Variables;
        Graph.Version++;
        return true;
    }

    public override void Invalidate() => Graph.Version++;

    public override string NewId(IdKind kind) => Guid.NewGuid().ToString("N")[..8];

    public override string IdOf(GraphNodeRecord record) => record.Id;
    public override string TypeOf(GraphNodeRecord record) => record.Type;
    public override string TitleOf(GraphNodeRecord record) => ScriptGraphNodes.Get(record.Type)?.DisplayName ?? record.Title;
    public override string OwnerOf(GraphNodeRecord record) => record.Owner;
    public override void SetOwner(GraphNodeRecord record, string owner) => record.Owner = owner;
    public override Float2 PositionOf(GraphNodeRecord record) => record.EditorPosition;
    public override void SetPosition(GraphNodeRecord record, Float2 position) => record.EditorPosition = position;
    public override bool CollapsedOf(GraphNodeRecord record) => record.Collapsed;
    public override void SetCollapsed(GraphNodeRecord record, bool collapsed) => record.Collapsed = collapsed;
    public override GraphNodeRecord Clone(GraphNodeRecord record) => record.Clone();

    public override void RemapIds(GraphNodeRecord record, Func<string, string> remap)
    {
        record.Id = remap(record.Id);
        foreach (GraphInputRecord input in record.Inputs) input.Node = remap(input.Node);
        foreach (string key in new List<string>(record.PropertyInputs.Keys))
            record.PropertyInputs[key] = remap(record.PropertyInputs[key]);
    }

    public override bool IsKnownType(GraphNodeRecord record) => ScriptGraphNodes.Get(record.Type) != null;

    public override IReadOnlyList<PortInfo> Inputs(GraphNodeRecord record)
    {
        ScriptGraphNodeDef? def = ScriptGraphNodes.Get(record.Type);
        if (def == null) return [];
        var ports = new List<PortInfo>(def.Inputs.Length);
        for (int i = 0; i < def.Inputs.Length; i++)
        {
            ScriptGraphPin pin = def.Inputs[i];
            ScriptPinKind kind = KindOfPin(record, i, pin.Kind);
            ports.Add(new PortInfo
            {
                Id = $"in{i}",
                Name = pin.Id,
                Label = pin.Label,
                Kind = s_kinds[kind],
                Optional = kind != ScriptPinKind.Exec,
                Index = i,
            });
        }
        return ports;
    }

    public override IReadOnlyList<PortInfo> Outputs(GraphNodeRecord record)
    {
        ScriptGraphNodeDef? def = ScriptGraphNodes.Get(record.Type);
        if (def == null) return [];
        var ports = new List<PortInfo>(def.Outputs.Length);
        for (int i = 0; i < def.Outputs.Length; i++)
        {
            ScriptGraphPin pin = def.Outputs[i];
            ScriptPinKind kind = KindOfPin(record, -1, pin.Kind);
            ports.Add(new PortInfo
            {
                Id = kind == ScriptPinKind.Exec ? pin.Id : DataOutput,
                Name = pin.Id,
                Label = pin.Label,
                Kind = s_kinds[kind],
            });
        }
        return ports;
    }

    public override PortInfo? InputPort(GraphNodeRecord record, string portId)
    {
        ScriptGraphNodeDef? def = ScriptGraphNodes.Get(record.Type);
        if (def == null || !portId.StartsWith("in", StringComparison.Ordinal)) return null;
        if (!int.TryParse(portId[2..], out int index) || index < 0 || index >= def.Inputs.Length) return null;
        ScriptGraphPin pin = def.Inputs[index];
        ScriptPinKind kind = KindOfPin(record, index, pin.Kind);
        return new PortInfo
        {
            Id = portId, Name = pin.Id, Label = pin.Label, Kind = s_kinds[kind],
            Optional = kind != ScriptPinKind.Exec, Index = index,
        };
    }

    private ScriptPinKind KindOfPin(GraphNodeRecord record, int pin, ScriptPinKind fallback)
    {
        if (record.Type == ScriptGraphNodes.GetVariable && pin < 0)
            return FindVariable(Text(record, "Variable"))?.Type ?? ScriptPinKind.Float;
        if (record.Type == ScriptGraphNodes.SetVariable && pin == 1)
            return FindVariable(Text(record, "Variable"))?.Type ?? ScriptPinKind.Float;
        return fallback;
    }

    private ScriptGraphVariable? FindVariable(string name)
        => Graph.Variables.Find(v => v.Name == name);

    private static string Text(GraphNodeRecord record, string key)
        => record.Properties.TryGetValue(key, out NodeValue value) ? value.Text : string.Empty;

    public override PortInfo? PortAt(GraphNodeRecord record, int index) => InputPort(record, $"in{index}");

    public override IEnumerable<(string Port, WireEnd Source)> AllSources(GraphNodeRecord record)
    {
        for (int i = 0; i < record.Inputs.Count; i++)
            if (record.Inputs[i].Node.Length > 0)
                yield return ($"in{i}", new WireEnd(record.Inputs[i].Node, DataOutput));

        foreach (GraphNodeRecord other in Graph.Nodes)
            foreach ((string key, string target) in other.PropertyInputs)
                if (target == record.Id)
                    yield return ("in0", new WireEnd(other.Id, key));
    }

    public override WireEnd? Source(GraphNodeRecord record, string inputPort)
    {
        if (inputPort == "in0" && ScriptGraphNodes.Get(record.Type) is { Inputs.Length: > 0 } def &&
            def.Inputs[0].Kind == ScriptPinKind.Exec)
        {
            foreach (GraphNodeRecord other in Graph.Nodes)
                foreach ((string key, string target) in other.PropertyInputs)
                    if (target == record.Id) return new WireEnd(other.Id, key);
            return null;
        }

        if (!inputPort.StartsWith("in", StringComparison.Ordinal)) return null;
        if (!int.TryParse(inputPort[2..], out int index) || index < 0 || index >= record.Inputs.Count) return null;
        return record.Inputs[index].Node.Length > 0 ? new WireEnd(record.Inputs[index].Node, DataOutput) : null;
    }

    public override void SetSource(GraphNodeRecord record, string inputPort, WireEnd? source)
    {
        if (inputPort == "in0" && ScriptGraphNodes.Get(record.Type) is { Inputs.Length: > 0 } def &&
            def.Inputs[0].Kind == ScriptPinKind.Exec)
        {
            foreach (GraphNodeRecord other in Graph.Nodes)
                foreach (string key in new List<string>(other.PropertyInputs.Keys))
                    if (other.PropertyInputs[key] == record.Id) other.PropertyInputs.Remove(key);

            if (source is { } exec && Find(exec.Node) is { } from)
                from.PropertyInputs[exec.Port] = record.Id;
            return;
        }

        if (!inputPort.StartsWith("in", StringComparison.Ordinal)) return;
        if (!int.TryParse(inputPort[2..], out int index) || index < 0) return;
        while (record.Inputs.Count <= index) record.Inputs.Add(new GraphInputRecord());
        record.Inputs[index].Node = source?.Node ?? string.Empty;
    }

    public override int InputCount(GraphNodeRecord record) => record.Inputs.Count;

    public override void SetInputCount(GraphNodeRecord record, int count)
    {
        while (record.Inputs.Count < count) record.Inputs.Add(new GraphInputRecord());
        if (record.Inputs.Count > count) record.Inputs.RemoveRange(count, record.Inputs.Count - count);
    }

    private static IReadOnlyList<NodeTypeInfo>? s_catalog;

    public override IReadOnlyList<NodeTypeInfo> Catalog => s_catalog ??= BuildCatalog();

    // The node catalogue is immutable, so every graph window shares one copy instead of rebuilding 2,500
    // entries each time the create picker opens.
    private static IReadOnlyList<NodeTypeInfo> BuildCatalog()
    {
        var catalog = new List<NodeTypeInfo>();
        foreach (ScriptGraphNodeDef def in ScriptGraphNodes.All.Values)
        {
            var inputs = new List<PortKind>(def.Inputs.Length);
            foreach (ScriptGraphPin pin in def.Inputs) inputs.Add(s_kinds[pin.Kind]);
            var outputs = new List<PortKind>(def.Outputs.Length);
            foreach (ScriptGraphPin pin in def.Outputs) outputs.Add(s_kinds[pin.Kind]);
            catalog.Add(new NodeTypeInfo
            {
                Id = def.Type,
                DisplayName = def.DisplayName,
                Category = def.Category,
                Description = def.Description,
                InputKinds = inputs,
                OutputKinds = outputs,
            });
        }

        // The reflected API catalogue: every engine method, property and field that can cross a wire.
        foreach (ScriptApiMember member in ScriptGraphApiCatalog.Members)
        {
            ScriptGraphNodeDef def = member.Definition;
            var inputs = new List<PortKind>(def.Inputs.Length);
            foreach (ScriptGraphPin pin in def.Inputs) inputs.Add(s_kinds[pin.Kind]);
            var outputs = new List<PortKind>(def.Outputs.Length);
            foreach (ScriptGraphPin pin in def.Outputs) outputs.Add(s_kinds[pin.Kind]);
            catalog.Add(new NodeTypeInfo
            {
                Id = member.TypeId,
                DisplayName = member.DisplayName,
                Category = member.Category,
                Description = member.Description,
                InputKinds = inputs,
                OutputKinds = outputs,
            });
        }
        return catalog;
    }

    public override GraphNodeRecord? Create(string typeId, Float2 at, string owner)
    {
        GraphNodeRecord record = Graph.AddNode(typeId, at);
        record.Owner = owner;
        return record;
    }

    public override bool Accepts(PortKind input, PortKind output)
    {
        if (ReferenceEquals(input, output) || input.Name == "exec" || output.Name == "exec") return true;

        // Floats and ints travel together: the generated code converts at the call site, so a
        // Time node can feed an int pin and an int variable can drive a float parameter.
        static bool Numeric(PortKind kind) => kind.Name is "float" or "int";
        return Numeric(input) && Numeric(output);
    }

    public override void ValidateGraph(string scope, List<GraphProblem> problems)
    {
        if (scope.Length > 0) return;
        if (!Graph.Nodes.Exists(n => n.Type is ScriptGraphNodes.OnStart or ScriptGraphNodes.OnUpdate or
                ScriptGraphNodes.OnLateUpdate or ScriptGraphNodes.OnEnable or ScriptGraphNodes.OnDisable or ScriptGraphNodes.OnKeyDown))
            problems.Add(new GraphProblem("", "The graph has no event node, so it never runs. Add On Start or On Update.", true));
    }
}
