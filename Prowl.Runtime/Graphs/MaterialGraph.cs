// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Infinity.Runtime.Animation;
using Infinity.Runtime.Resources;
using Infinity.Vector;

namespace Infinity.Runtime.Graphs;

/// <summary>The shape the material editor previews a graph on.</summary>
public enum PreviewMesh : byte
{
    Sphere,
    Cube,
    Cylinder,
    Plane,
}

/// <summary>
/// A node-authored material. The graph compiles to a Graphite shader and a material, so it can be dropped on a
/// renderer or used as a post-process material. Opaque, cutout and transparent are material settings rather than
/// separate graphs.
/// </summary>
[CreateAssetMenu("Material/Material Graph", Extension = ".matgraph", Order = 1110)]
public sealed class MaterialGraph : Asset
{
    /// <summary>Every node, including the output node.</summary>
    public List<GraphNodeRecord> Nodes = [];
    public List<GraphGroupRecord> Groups = [];
    public List<GraphNoteRecord> Notes = [];

    /// <summary>The output node the compiler reads. Empty picks the first Material Output node.</summary>
    public string OutputNodeId = string.Empty;

    /// <summary>Compile as the unlit surface model instead of PBR.</summary>
    public bool Unlit;

    /// <summary>Opaque, cutout or transparent.</summary>
    public SurfaceType Surface = SurfaceType.Opaque;

    /// <summary>
    /// Compiles to a full-screen post-processing shader instead of a surface shader. Assign the graph to a
    /// Post Process Material volume override.
    /// </summary>
    public bool PostProcess;

    /// <summary>Draw both sides of the mesh.</summary>
    public bool TwoSided;

    /// <summary>The shape the editor previews the material on. Editor only.</summary>
    public PreviewMesh PreviewMesh = PreviewMesh.Sphere;

    /// <summary>Draw the preview over a checkerboard so transparency is visible. Editor only.</summary>
    public bool PreviewTransparentBackground = true;

    /// <summary>The shader the last compile produced, kept on the asset so the material survives a reload.</summary>
    public Shader? GeneratedShader;

    /// <summary>The material the last compile produced.</summary>
    public Material? GeneratedMaterial;

    /// <summary>The fragment source the last post-process compile produced.</summary>
    public string GeneratedPostProcessSource = string.Empty;

    /// <summary>The error the last compile reported, empty when it succeeded.</summary>
    public string LastCompileError = string.Empty;

    /// <summary>Goes up on every edit, so a preview knows it is stale.</summary>
    public int Version;

    public MaterialGraph() : base("Material Graph") { }

    public MaterialGraph(string name) : base(name) { }

    public GraphNodeRecord? OutputNode
    {
        get
        {
            if (OutputNodeId.Length > 0)
            {
                GraphNodeRecord? named = Nodes.Find(n => n.Id == OutputNodeId);
                if (named != null) return named;
            }
            return Nodes.Find(n => n.Type == MaterialGraphNodes.Output);
        }
    }

    public GraphNodeRecord AddNode(string type, Float2 position)
    {
        var record = MaterialGraphNodes.Create(type, position, Guid.NewGuid().ToString("N"));
        Nodes.Add(record);
        if (type == MaterialGraphNodes.Output && OutputNodeId.Length == 0)
            OutputNodeId = record.Id;
        Version++;
        return record;
    }

    public bool RemoveNode(string id)
    {
        int index = Nodes.FindIndex(n => n.Id == id);
        if (index < 0) return false;

        Nodes.RemoveAt(index);
        foreach (GraphNodeRecord node in Nodes)
            for (int i = 0; i < node.Inputs.Count; i++)
                if (node.Inputs[i].Node == id) node.Inputs[i].Node = string.Empty;

        if (OutputNodeId == id) OutputNodeId = string.Empty;
        Version++;
        return true;
    }

    /// <summary>The shader source the graph compiles to, without compiling it.</summary>
    public string ToShaderSource() => MaterialGraphCompiler.Compile(this);

    /// <summary>Compiles the graph and returns the material, replacing the previous one.</summary>
    public Material? Compile()
    {
        LastCompileError = string.Empty;
        try
        {
            if (PostProcess)
            {
                GeneratedPostProcessSource = MaterialGraphCompiler.CompilePostProcessFragment(this);
                GeneratedShader = null;
                GeneratedMaterial = null;
                Version++;
                return null;
            }

            string source = ToShaderSource();
            GeneratedShader = Shader.FromSource(source, $"{Name}.shader");
            GeneratedMaterial = new Material(GeneratedShader) { Name = Name };
            GeneratedMaterial.Surface = new SurfaceSettings(Surface, SurfaceBlend.Alpha,
                TwoSided ? SurfaceCull.Off : SurfaceCull.Back);
            return GeneratedMaterial;
        }
        catch (Exception ex)
        {
            LastCompileError = ex.Message;
            Debug.LogError($"[MaterialGraph] '{Name}' failed to compile: {ex.Message}");
            return null;
        }
    }

    /// <summary>Sets a node's numeric property and bumps the version.</summary>
    public void SetNumber(GraphNodeRecord node, string key, float value)
    {
        node.Properties[key] = NodeValue.FromNumber(value);
        Version++;
    }

    /// <summary>Sets a node's vector property and bumps the version.</summary>
    public void SetVector(GraphNodeRecord node, string key, Float3 value)
    {
        node.Properties[key] = NodeValue.FromVector(value);
        Version++;
    }

    /// <summary>Sets a node's texture property and bumps the version.</summary>
    public void SetTexture(GraphNodeRecord node, string key, Texture2D? value)
    {
        node.Properties[key] = NodeValue.FromTexture(value);
        Version++;
    }
}
