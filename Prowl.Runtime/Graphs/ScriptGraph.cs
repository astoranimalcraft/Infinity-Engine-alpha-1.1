// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Infinity.Runtime.Animation;
using Infinity.Vector;

namespace Infinity.Runtime.Graphs;

/// <summary>A value the script graph keeps between frames.</summary>
public sealed class ScriptGraphVariable
{
    public string Name = "value";
    public ScriptPinKind Type = ScriptPinKind.Float;
    public float Number;
    public int Integer;
    public bool Flag;
    public string Text = string.Empty;
    public Float3 Vector;

    public ScriptGraphVariable Clone() => (ScriptGraphVariable)MemberwiseClone();
}

/// <summary>
/// A visual C# script. Nodes compile to a small class derived from <see cref="ScriptGraphBehaviour"/>, which a
/// <see cref="ScriptGraphRunner"/> instantiates on the object. The same engine APIs scripts use are available in
/// the generated code.
/// </summary>
[CreateAssetMenu("Scripting/Script Graph", Extension = ".scriptgraph", Order = 1120)]
public sealed class ScriptGraph : Asset
{
    public List<GraphNodeRecord> Nodes = [];
    public List<GraphGroupRecord> Groups = [];
    public List<GraphNoteRecord> Notes = [];
    public List<ScriptGraphVariable> Variables = [];

    /// <summary>The C# the graph generated on its last compile.</summary>
    public string GeneratedCode = string.Empty;

    /// <summary>Path of the compiled assembly, relative to the project root.</summary>
    public string AssemblyPath = string.Empty;

    /// <summary>The class the runner instantiates. Empty derives one from the asset id.</summary>
    public string ClassName = string.Empty;

    /// <summary>Goes up on every edit.</summary>
    public int Version;

    public ScriptGraph() : base("Script Graph") { }

    public ScriptGraph(string name) : base(name) { }

    public GraphNodeRecord AddNode(string type, Float2 position)
    {
        var record = ScriptGraphNodes.Create(type, position, Guid.NewGuid().ToString("N"));
        Nodes.Add(record);
        Version++;
        return record;
    }

    public bool RemoveNode(string id)
    {
        int index = Nodes.FindIndex(n => n.Id == id);
        if (index < 0) return false;

        Nodes.RemoveAt(index);
        foreach (GraphNodeRecord node in Nodes)
        {
            for (int i = 0; i < node.Inputs.Count; i++)
                if (node.Inputs[i].Node == id) node.Inputs[i].Node = string.Empty;
            foreach (string key in new List<string>(node.PropertyInputs.Keys))
                if (node.PropertyInputs[key] == id) node.PropertyInputs.Remove(key);
        }

        Version++;
        return true;
    }

    public ScriptGraphVariable AddVariable(string name, ScriptPinKind type)
    {
        var variable = new ScriptGraphVariable { Name = name, Type = type };
        Variables.Add(variable);
        Version++;
        return variable;
    }

    /// <summary>The C# source the graph generates, without compiling it.</summary>
    public string ToCSharp() => ScriptGraphCodeGen.Generate(this);

    /// <summary>The class name the runner looks for.</summary>
    public string ResolveClassName()
    {
        if (ClassName.Length > 0) return ClassName;
        string id = AssetID.ToString("N");
        return $"ScriptGraph_{id[..Math.Min(12, id.Length)]}";
    }
}
