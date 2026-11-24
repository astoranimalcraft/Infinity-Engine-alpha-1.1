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

/// <summary>A material graph as the shared node editor works on it.</summary>
public sealed class MaterialGraphDomain : GraphDomain<GraphNodeRecord>
{
    public const string OutputPort = "out0";

    private static readonly Dictionary<MaterialGraphValueKind, PortKind> s_kinds = new()
    {
        [MaterialGraphValueKind.Float] = new PortKind("float", "Float", Color.FromArgb(190, 220, 120)),
        [MaterialGraphValueKind.Vector2] = new PortKind("vec2", "Vector 2", Color.FromArgb(120, 200, 220)),
        [MaterialGraphValueKind.Vector3] = new PortKind("vec3", "Vector 3", Color.FromArgb(120, 160, 240)),
        [MaterialGraphValueKind.Color] = new PortKind("color", "Color", Color.FromArgb(240, 190, 110)),
        [MaterialGraphValueKind.Texture] = new PortKind("texture", "Texture", Color.FromArgb(230, 130, 200)),
    };

    public MaterialGraphDomain(MaterialGraph graph) : base(graph) => Graph = graph;

    public MaterialGraph Graph { get; }

    public override List<GraphNodeRecord> Nodes => Graph.Nodes;
    public override List<GraphGroupRecord> Groups => Graph.Groups;
    public override List<GraphNoteRecord> Notes => Graph.Notes;

    public override EchoObject Snapshot() => Serializer.Serialize(typeof(object), Graph);

    public override bool Restore(EchoObject snapshot, bool compiles)
    {
        MaterialGraph? restored = Serializer.Deserialize<MaterialGraph>(snapshot.Clone());
        if (restored.IsNotValid()) return false;
        Graph.Nodes = restored.Nodes;
        Graph.Groups = restored.Groups;
        Graph.Notes = restored.Notes;
        Graph.OutputNodeId = restored.OutputNodeId;
        Graph.Version++;
        if (compiles) Graph.Compile();
        return true;
    }

    public override void Invalidate() => Graph.Version++;

    public override string NewId(IdKind kind) => Guid.NewGuid().ToString("N")[..8];

    public override string IdOf(GraphNodeRecord record) => record.Id;
    public override string TypeOf(GraphNodeRecord record) => record.Type;
    public override string TitleOf(GraphNodeRecord record) => record.Title;
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
    }

    public override bool IsKnownType(GraphNodeRecord record) => MaterialGraphNodes.Get(record.Type) != null;

    public override IReadOnlyList<PortInfo> Inputs(GraphNodeRecord record)
    {
        MaterialGraphNodeDef? def = MaterialGraphNodes.Get(record.Type);
        if (def == null) return [];
        var ports = new List<PortInfo>(def.Inputs.Length);
        for (int i = 0; i < def.Inputs.Length; i++)
        {
            MaterialGraphPin pin = def.Inputs[i];
            ports.Add(new PortInfo
            {
                Id = $"in{i}",
                Name = pin.Id,
                Label = pin.Label,
                Kind = s_kinds[pin.Kind],
                Optional = true,
                Index = i,
            });
        }
        return ports;
    }

    public override IReadOnlyList<PortInfo> Outputs(GraphNodeRecord record)
    {
        MaterialGraphNodeDef? def = MaterialGraphNodes.Get(record.Type);
        if (def == null || record.Type == MaterialGraphNodes.Output) return [];
        var ports = new List<PortInfo>(def.Outputs.Length);
        for (int i = 0; i < def.Outputs.Length; i++)
        {
            MaterialGraphPin pin = def.Outputs[i];
            ports.Add(new PortInfo { Id = $"out{i}", Name = pin.Id, Label = pin.Label, Kind = s_kinds[pin.Kind] });
        }
        return ports;
    }

    public override PortInfo? InputPort(GraphNodeRecord record, string portId)
    {
        MaterialGraphNodeDef? def = MaterialGraphNodes.Get(record.Type);
        if (def == null || !portId.StartsWith("in", StringComparison.Ordinal)) return null;
        if (!int.TryParse(portId[2..], out int index) || index < 0 || index >= def.Inputs.Length) return null;
        MaterialGraphPin pin = def.Inputs[index];
        return new PortInfo { Id = portId, Name = pin.Id, Label = pin.Label, Kind = s_kinds[pin.Kind], Optional = true, Index = index };
    }

    public override PortInfo? PortAt(GraphNodeRecord record, int index) => InputPort(record, $"in{index}");

    public override IEnumerable<(string Port, WireEnd Source)> AllSources(GraphNodeRecord record)
    {
        for (int i = 0; i < record.Inputs.Count; i++)
            if (record.Inputs[i].Node.Length > 0)
                yield return ($"in{i}", new WireEnd(record.Inputs[i].Node, OutputPort));
    }

    public override WireEnd? Source(GraphNodeRecord record, string inputPort)
    {
        if (!inputPort.StartsWith("in", StringComparison.Ordinal)) return null;
        if (!int.TryParse(inputPort[2..], out int index) || index < 0 || index >= record.Inputs.Count) return null;
        return record.Inputs[index].Node.Length > 0 ? new WireEnd(record.Inputs[index].Node, OutputPort) : null;
    }

    public override void SetSource(GraphNodeRecord record, string inputPort, WireEnd? source)
    {
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

    public override IReadOnlyList<NodeTypeInfo> Catalog
    {
        get
        {
            var catalog = new List<NodeTypeInfo>();
            foreach (MaterialGraphNodeDef def in MaterialGraphNodes.All.Values)
            {
                var inputs = new List<PortKind>(def.Inputs.Length);
                foreach (MaterialGraphPin pin in def.Inputs) inputs.Add(s_kinds[pin.Kind]);
                var outputs = new List<PortKind>(def.Outputs.Length);
                foreach (MaterialGraphPin pin in def.Outputs) outputs.Add(s_kinds[pin.Kind]);
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
            return catalog;
        }
    }

    public override GraphNodeRecord? Create(string typeId, Float2 at, string owner)
    {
        GraphNodeRecord record = Graph.AddNode(typeId, at);
        record.Owner = owner;
        return record;
    }

    public override bool IsRemovable(GraphNodeRecord record) => record.Type != MaterialGraphNodes.Output;
    public override bool IsCopyable(GraphNodeRecord record) => record.Type != MaterialGraphNodes.Output;

    public override void ValidateGraph(string scope, List<GraphProblem> problems)
    {
        if (scope.Length == 0 && Graph.OutputNode == null)
            problems.Add(new GraphProblem("", "The graph has no Material Output node, so it compiles to a default material.", true));
    }

    public override void Validate(GraphNodeRecord record, NodeProblems problems)
    {
        if (record.Type == MaterialGraphNodes.Texture &&
            (!record.Properties.TryGetValue("Texture", out NodeValue? texture) || (texture?.Texture.IsNotValid() ?? true)))
            problems.Unfinished("Pick a texture, or the sample reads white.");
    }
}
