// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

using Infinity.Runtime.Animation;
using Infinity.Runtime.Resources;
using Infinity.Vector;

namespace Infinity.Runtime.Graphs;

/// <summary>One input or output of a material graph node.</summary>
public sealed record MaterialGraphPin(string Id, string Label, MaterialGraphValueKind Kind);

/// <summary>The value a material graph pin carries.</summary>
public enum MaterialGraphValueKind : byte
{
    Float,
    Vector2,
    Vector3,
    Color,
    Texture,
}

/// <summary>What a material graph node is and what it plugs into.</summary>
public sealed record MaterialGraphNodeDef(
    string Type,
    string DisplayName,
    string Category,
    string Description,
    MaterialGraphPin[] Inputs,
    MaterialGraphPin[] Outputs)
{
    public string Title => DisplayName;
}

/// <summary>The node catalogue of the material graph editor, and the pins the canvas draws.</summary>
public static class MaterialGraphNodes
{
    public const string Output = "mat.output";
    public const string ConstFloat = "mat.constFloat";
    public const string ConstColor = "mat.constColor";
    public const string ConstVector = "mat.constVector";
    public const string UV = "mat.uv";
    public const string Time = "mat.time";
    public const string WorldPosition = "mat.worldPosition";
    public const string Normal = "mat.normal";
    public const string VertexColor = "mat.vertexColor";
    public const string SceneColor = "mat.sceneColor";
    public const string Texture = "mat.texture";
    public const string Add = "mat.add";
    public const string Subtract = "mat.subtract";
    public const string Multiply = "mat.multiply";
    public const string Divide = "mat.divide";
    public const string Lerp = "mat.lerp";
    public const string Dot = "mat.dot";
    public const string Normalize = "mat.normalize";
    public const string OneMinus = "mat.oneMinus";
    public const string Clamp = "mat.clamp";
    public const string Saturate = "mat.saturate";
    public const string Step = "mat.step";
    public const string Smoothstep = "mat.smoothstep";
    public const string Power = "mat.power";
    public const string Fresnel = "mat.fresnel";
    public const string Channel = "mat.channel";
    public const string Panner = "mat.panner";

    private static readonly Dictionary<string, MaterialGraphNodeDef> s_defs = Build();

    public static IReadOnlyDictionary<string, MaterialGraphNodeDef> All => s_defs;

    public static MaterialGraphNodeDef? Get(string type) => s_defs.GetValueOrDefault(type);

    private static Dictionary<string, MaterialGraphNodeDef> Build()
    {
        MaterialGraphPin P(string id, string label, MaterialGraphValueKind kind) => new(id, label, kind);

        var defs = new Dictionary<string, MaterialGraphNodeDef>(StringComparer.Ordinal);
        void Add(MaterialGraphNodeDef def) => defs[def.Type] = def;

        Add(new MaterialGraphNodeDef(Output, "Surface Result", "Output", "The surface the material draws.",
        [
            P("BaseColor", "Base Color", MaterialGraphValueKind.Color),
            P("Metallic", "Metallic", MaterialGraphValueKind.Float),
            P("Roughness", "Roughness", MaterialGraphValueKind.Float),
            P("Normal", "Normal", MaterialGraphValueKind.Vector3),
            P("Emission", "Emission", MaterialGraphValueKind.Color),
            P("Opacity", "Opacity", MaterialGraphValueKind.Float),
            P("AmbientOcclusion", "Ambient Occlusion", MaterialGraphValueKind.Float),
        ], []));

        Add(new MaterialGraphNodeDef(ConstFloat, "Scalar", "Constants", "A constant number.",
            [], [P("Out", "Out", MaterialGraphValueKind.Float)]));
        Add(new MaterialGraphNodeDef(ConstColor, "Color", "Constants", "A constant color.",
            [], [P("Out", "Out", MaterialGraphValueKind.Color)]));
        Add(new MaterialGraphNodeDef(ConstVector, "Vector", "Constants", "A constant 3D vector.",
            [], [P("Out", "Out", MaterialGraphValueKind.Vector3)]));
        Add(new MaterialGraphNodeDef(UV, "UV Coordinates", "Inputs", "The mesh's first UV set.",
            [], [P("Out", "UV", MaterialGraphValueKind.Vector2)]));
        Add(new MaterialGraphNodeDef(Time, "Time", "Inputs", "Seconds since the shader started.",
            [], [P("Out", "Time", MaterialGraphValueKind.Float)]));
        Add(new MaterialGraphNodeDef(WorldPosition, "World Position", "Inputs", "The fragment's world position.",
            [], [P("Out", "Position", MaterialGraphValueKind.Vector3)]));
        Add(new MaterialGraphNodeDef(Normal, "Surface Normal", "Inputs", "The fragment's world-space normal.",
            [], [P("Out", "Normal", MaterialGraphValueKind.Vector3)]));
        Add(new MaterialGraphNodeDef(VertexColor, "Vertex Color", "Inputs", "The vertex color times the object color.",
            [], [P("Out", "Color", MaterialGraphValueKind.Color)]));
        Add(new MaterialGraphNodeDef(SceneColor, "Scene Sample", "Post Process", "The colour the camera has drawn so far. Post-process graphs only.",
            [], [P("Out", "Color", MaterialGraphValueKind.Color)]));
        Add(new MaterialGraphNodeDef(Texture, "Texture Read", "Textures", "Samples a texture at a UV.",
        [
            P("UV", "UV", MaterialGraphValueKind.Vector2),
        ], [P("Out", "RGBA", MaterialGraphValueKind.Color)]));
        Add(new MaterialGraphNodeDef(MaterialGraphNodes.Add, "Add", "Math", "A + B.",
        [P("A", "A", MaterialGraphValueKind.Vector3), P("B", "B", MaterialGraphValueKind.Vector3)],
        [P("Out", "Out", MaterialGraphValueKind.Vector3)]));
        Add(new MaterialGraphNodeDef(Subtract, "Subtract", "Math", "A - B.",
        [P("A", "A", MaterialGraphValueKind.Vector3), P("B", "B", MaterialGraphValueKind.Vector3)],
        [P("Out", "Out", MaterialGraphValueKind.Vector3)]));
        Add(new MaterialGraphNodeDef(Multiply, "Multiply", "Math", "A * B.",
        [P("A", "A", MaterialGraphValueKind.Vector3), P("B", "B", MaterialGraphValueKind.Vector3)],
        [P("Out", "Out", MaterialGraphValueKind.Vector3)]));
        Add(new MaterialGraphNodeDef(Divide, "Divide", "Math", "A / B, guarding against zero.",
        [P("A", "A", MaterialGraphValueKind.Vector3), P("B", "B", MaterialGraphValueKind.Vector3)],
        [P("Out", "Out", MaterialGraphValueKind.Vector3)]));
        Add(new MaterialGraphNodeDef(Lerp, "Blend", "Math", "Mix A and B by T.",
        [P("A", "A", MaterialGraphValueKind.Vector3), P("B", "B", MaterialGraphValueKind.Vector3), P("T", "T", MaterialGraphValueKind.Float)],
        [P("Out", "Out", MaterialGraphValueKind.Vector3)]));
        Add(new MaterialGraphNodeDef(Dot, "Dot", "Math", "The dot product of A and B.",
        [P("A", "A", MaterialGraphValueKind.Vector3), P("B", "B", MaterialGraphValueKind.Vector3)],
        [P("Out", "Out", MaterialGraphValueKind.Float)]));
        Add(new MaterialGraphNodeDef(Normalize, "Normalize", "Math", "A unit-length vector.",
        [P("A", "A", MaterialGraphValueKind.Vector3)],
        [P("Out", "Out", MaterialGraphValueKind.Vector3)]));
        Add(new MaterialGraphNodeDef(OneMinus, "Invert", "Math", "1 - A.",
        [P("A", "A", MaterialGraphValueKind.Vector3)],
        [P("Out", "Out", MaterialGraphValueKind.Vector3)]));
        Add(new MaterialGraphNodeDef(Clamp, "Clamp", "Math", "Clamps A between Min and Max.",
        [P("A", "A", MaterialGraphValueKind.Vector3), P("Min", "Min", MaterialGraphValueKind.Float), P("Max", "Max", MaterialGraphValueKind.Float)],
        [P("Out", "Out", MaterialGraphValueKind.Vector3)]));
        Add(new MaterialGraphNodeDef(Saturate, "Saturate", "Math", "Clamps A between 0 and 1.",
        [P("A", "A", MaterialGraphValueKind.Vector3)],
        [P("Out", "Out", MaterialGraphValueKind.Vector3)]));
        Add(new MaterialGraphNodeDef(Step, "Step", "Math", "0 below Edge, 1 at or above it.",
        [P("Edge", "Edge", MaterialGraphValueKind.Float), P("A", "A", MaterialGraphValueKind.Vector3)],
        [P("Out", "Out", MaterialGraphValueKind.Vector3)]));
        Add(new MaterialGraphNodeDef(Smoothstep, "Smooth Step", "Math", "A soft ramp from Min to Max.",
        [P("Min", "Min", MaterialGraphValueKind.Float), P("Max", "Max", MaterialGraphValueKind.Float), P("A", "A", MaterialGraphValueKind.Vector3)],
        [P("Out", "Out", MaterialGraphValueKind.Vector3)]));
        Add(new MaterialGraphNodeDef(Power, "Power", "Math", "A raised to B.",
        [P("A", "A", MaterialGraphValueKind.Vector3), P("B", "B", MaterialGraphValueKind.Vector3)],
        [P("Out", "Out", MaterialGraphValueKind.Vector3)]));
        Add(new MaterialGraphNodeDef(Fresnel, "Rim Light", "Math", "A rim term from the view angle.",
        [P("Power", "Power", MaterialGraphValueKind.Float)],
        [P("Out", "Out", MaterialGraphValueKind.Float)]));
        Add(new MaterialGraphNodeDef(Channel, "Channel Extract", "Math", "One channel of a vector.",
        [P("In", "In", MaterialGraphValueKind.Color)],
        [P("Out", "Out", MaterialGraphValueKind.Float)]));
        Add(new MaterialGraphNodeDef(Panner, "UV Scroll", "Textures", "Moves a UV by speed times time.",
        [P("UV", "UV", MaterialGraphValueKind.Vector2), P("Speed", "Speed", MaterialGraphValueKind.Vector2)],
        [P("Out", "UV", MaterialGraphValueKind.Vector2)]));

        return defs;
    }

    /// <summary>A fresh node record with its default values.</summary>
    public static GraphNodeRecord Create(string type, Float2 position, string id)
    {
        MaterialGraphNodeDef? def = Get(type);
        var record = new GraphNodeRecord
        {
            Id = id,
            Type = type,
            EditorPosition = position,
        };

        if (def != null)
        {
            for (int i = 0; i < def.Inputs.Length; i++) record.Inputs.Add(new GraphInputRecord());
            record.Properties["Value"] = NodeValue.FromNumber(0.5f);
            record.Properties["Color"] = NodeValue.FromVector(new Float3(0.8f, 0.8f, 0.8f));
            record.Properties["Vector"] = NodeValue.FromVector(new Float3(1f, 1f, 1f));
            record.Properties["Channel"] = NodeValue.FromInteger(0);
            record.Properties["Speed"] = NodeValue.FromVector(new Float3(0.1f, 0.1f, 0f));
            record.Properties["Power"] = NodeValue.FromNumber(4f);
            record.Properties["Edge"] = NodeValue.FromNumber(0.5f);
            record.Properties["Min"] = NodeValue.FromNumber(0f);
            record.Properties["Max"] = NodeValue.FromNumber(1f);
        }

        return record;
    }
}
