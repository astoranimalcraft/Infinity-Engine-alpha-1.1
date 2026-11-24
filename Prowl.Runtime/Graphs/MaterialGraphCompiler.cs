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

/// <summary>Turns a <see cref="MaterialGraph"/> into the Graphite shader source it draws with.</summary>
public static class MaterialGraphCompiler
{
    public static string Compile(MaterialGraph graph)
    {
        GraphNodeRecord? output = graph.OutputNode;
        if (output == null)
            return FallbackShader(graph);

        var context = new Context(graph);
        (string baseColor, MaterialGraphValueKind baseColorKind) = context.InputWithKind(output, 0, "vec3(0.8, 0.8, 0.8)", MaterialGraphValueKind.Vector3);
        (string metallic, MaterialGraphValueKind metallicKind) = context.InputWithKind(output, 1, "0.0", MaterialGraphValueKind.Float);
        (string roughness, MaterialGraphValueKind roughnessKind) = context.InputWithKind(output, 2, "0.5", MaterialGraphValueKind.Float);
        (string normal, MaterialGraphValueKind normalKind) = context.InputWithKind(output, 3, "i.geometryNormal", MaterialGraphValueKind.Vector3);
        (string emission, MaterialGraphValueKind emissionKind) = context.InputWithKind(output, 4, "vec3(0.0)", MaterialGraphValueKind.Vector3);
        (string opacity, MaterialGraphValueKind opacityKind) = context.InputWithKind(output, 5, "1.0", MaterialGraphValueKind.Float);
        (string occlusion, MaterialGraphValueKind occlusionKind) = context.InputWithKind(output, 6, "1.0", MaterialGraphValueKind.Float);

        string surface = graph.Unlit ? "Unlit" : "Lit";
        var shader = new StringBuilder();
        shader.Append("Shader \"Infinity/MaterialGraph/").Append(Escape(graph.Name)).Append("\"\n{\n");

        if (context.Properties.Length > 0)
        {
            shader.Append("    Properties\n    {\n");
            shader.Append(context.Properties);
            shader.Append("    }\n\n");
        }

        shader.Append("    Surface \"").Append(surface).Append("\"\n    {\n        GLSLPROGRAM\n");
        if (context.UsesTime) shader.Append("#pragma global float _Time\n");
        if (context.Uniforms.Length > 0) shader.Append(context.Uniforms);
        shader.Append("\n        void Surface(inout ").Append(graph.Unlit ? "Unlit_Params" : "PBR_Params")
            .Append(" p, Graphite_SurfaceData i)\n        {\n");
        shader.Append(context.Body);
        shader.Append("            p.baseColor = ").Append(context.ToVec3(baseColor, baseColorKind)).Append(";\n");

        if (graph.Unlit)
        {
            shader.Append("            p.emissionLuminance = 20000.0;\n");
            shader.Append("            p.alpha = clamp(").Append(context.ToFloat(opacity, opacityKind)).Append(", 0.0, 1.0);\n");
        }
        else
        {
            shader.Append("            p.baseMetalness = clamp(").Append(context.ToFloat(metallic, metallicKind)).Append(", 0.0, 1.0);\n");
            shader.Append("            p.specularRoughness = clamp(").Append(context.ToFloat(roughness, roughnessKind)).Append(", 0.02, 1.0);\n");
            shader.Append("            p.geometryNormal = normalize(").Append(context.ToVec3(normal, normalKind)).Append(");\n");
            shader.Append("            p.emissionColor = ").Append(context.ToVec3(emission, emissionKind)).Append(";\n");
            shader.Append("            p.emissionLuminance = 1.0;\n");
            shader.Append("            p.geometryOpacity = clamp(").Append(context.ToFloat(opacity, opacityKind)).Append(", 0.0, 1.0);\n");
            shader.Append("            p.occlusion = clamp(").Append(context.ToFloat(occlusion, occlusionKind)).Append(", 0.0, 1.0);\n");
        }

        shader.Append("        }\n        ENDGLSL\n    }\n}\n");
        return shader.ToString();
    }

    /// <summary>Compiles the graph to the fragment body a full-screen post effect draws with.</summary>
    public static string CompilePostProcessFragment(MaterialGraph graph, string entryPoint = "main")
    {
        GraphNodeRecord? output = graph.OutputNode;
        if (output == null)
            return $"void {entryPoint}() {{ outColor = Graphite_SampleSource(vUV); }}\n";

        var context = new Context(graph, postProcess: true);
        (string color, MaterialGraphValueKind colorKind) = context.InputWithKind(output, 0, "Graphite_SampleSource(vUV)", MaterialGraphValueKind.Color);
        (string opacity, MaterialGraphValueKind opacityKind) = context.InputWithKind(output, 5, "1.0", MaterialGraphValueKind.Float);

        var shader = new StringBuilder();
        if (context.UsesTime) shader.Append("#pragma global float _Time\n");
        if (context.Uniforms.Length > 0) shader.Append(context.Uniforms);
        shader.Append("\nvoid ").Append(entryPoint).Append("()\n{\n");
        shader.Append(context.Body);
        shader.Append("    outColor = vec4(").Append(context.ToVec3(color, colorKind)).Append(", clamp(")
            .Append(context.ToFloat(opacity, opacityKind)).Append(", 0.0, 1.0));\n}\n");
        return shader.ToString();
    }

    private static string FallbackShader(MaterialGraph graph)
    {
        string name = Escape(graph.Name);
        return graph.Unlit
            ? $"Shader \"Infinity/MaterialGraph/{name}\"\n{{\n    Surface \"Unlit\"\n    {{\n        GLSLPROGRAM\n        void Surface(inout Unlit_Params p, Graphite_SurfaceData i)\n        {{\n            p.baseColor = vec3(0.8);\n            p.emissionLuminance = 20000.0;\n            p.alpha = 1.0;\n        }}\n        ENDGLSL\n    }}\n}}\n"
            : $"Shader \"Infinity/MaterialGraph/{name}\"\n{{\n    Surface \"Lit\"\n    {{\n        GLSLPROGRAM\n        void Surface(inout PBR_Params p, Graphite_SurfaceData i)\n        {{\n            p.baseColor = vec3(0.8);\n            p.specularRoughness = 0.5;\n        }}\n        ENDGLSL\n    }}\n}}\n";
    }

    private static string Escape(string name)
    {
        var text = new StringBuilder(name.Length);
        foreach (char c in name)
            text.Append(char.IsLetterOrDigit(c) || c is '/' or '_' or '-' or ' ' ? c : '_');
        return text.Length == 0 ? "Material Graph" : text.ToString();
    }

    private sealed class Context
    {
        private readonly MaterialGraph _graph;
        private readonly bool _postProcess;
        private readonly Dictionary<string, (string Expression, MaterialGraphValueKind Kind)> _cache = new(StringComparer.Ordinal);
        private readonly HashSet<string> _visiting = new(StringComparer.Ordinal);
        private readonly StringBuilder _properties = new();
        private readonly StringBuilder _uniforms = new();
        private readonly StringBuilder _body = new();
        private int _temporaries;
        private bool _usesTime;

        public Context(MaterialGraph graph, bool postProcess = false)
        {
            _graph = graph;
            _postProcess = postProcess;
        }

        private string Uv => _postProcess ? "vUV" : "i.uv";
        private string Normal => _postProcess ? "vec3(0.0, 0.0, 1.0)" : "i.geometryNormal";
        private string ViewDir => _postProcess ? "vec3(0.0, 0.0, 1.0)" : "i.viewDir";
        private string VertexColor => _postProcess ? "vec4(1.0)" : "i.vertexColor";

        public string Properties => _properties.ToString();
        public string Uniforms => _uniforms.ToString();
        public bool UsesTime => _usesTime;
        public string Body => _body.ToString();

        public string Input(GraphNodeRecord node, int index, string fallback, MaterialGraphValueKind kind)
            => InputWithKind(node, index, fallback, kind).Expression;

        public (string Expression, MaterialGraphValueKind Kind) InputWithKind(GraphNodeRecord node, int index, string fallback, MaterialGraphValueKind kind)
        {
            if (index >= 0 && index < node.Inputs.Count)
            {
                string id = node.Inputs[index].Node;
                if (id.Length > 0)
                {
                    GraphNodeRecord? source = _graph.Nodes.Find(n => n.Id == id);
                    if (source != null && source.Type != MaterialGraphNodes.Output)
                        return Emit(source);
                }
            }
            return (fallback, kind);
        }

        private (string Expression, MaterialGraphValueKind Kind) Emit(GraphNodeRecord node)
        {
            if (_cache.TryGetValue(node.Id, out var cached)) return cached;
            if (!_visiting.Add(node.Id)) return ("vec3(0.0)", MaterialGraphValueKind.Vector3);

            (string Expression, MaterialGraphValueKind Kind) result = node.Type switch
            {
                MaterialGraphNodes.ConstFloat => Constant(node, "Value", "Float", MaterialGraphValueKind.Float),
                MaterialGraphNodes.ConstColor => ColorConstant(node),
                MaterialGraphNodes.ConstVector => VectorConstant(node),
                MaterialGraphNodes.UV => Declare(node, Uv, MaterialGraphValueKind.Vector2),
                MaterialGraphNodes.Time => TimeNode(node),
                MaterialGraphNodes.WorldPosition => Declare(node, _postProcess ? "vec3(0.0)" : "Graphite_RelativeToWorld(i.relativePos)", MaterialGraphValueKind.Vector3),
                MaterialGraphNodes.Normal => Declare(node, Normal, MaterialGraphValueKind.Vector3),
                MaterialGraphNodes.VertexColor => Declare(node, VertexColor, MaterialGraphValueKind.Color),
                MaterialGraphNodes.SceneColor => Declare(node, _postProcess ? "Graphite_SampleSource(vUV)" : "vec4(0.0)", MaterialGraphValueKind.Color),
                MaterialGraphNodes.Texture => Texture(node),
                MaterialGraphNodes.Add => Binary(node, "+", "vec3(0.0)"),
                MaterialGraphNodes.Subtract => Binary(node, "-", "vec3(0.0)"),
                MaterialGraphNodes.Multiply => Binary(node, "*", "vec3(1.0)"),
                MaterialGraphNodes.Divide => Divide(node),
                MaterialGraphNodes.Lerp => Lerp(node),
                MaterialGraphNodes.Dot => Dot(node),
                MaterialGraphNodes.Normalize => Unary(node, "normalize"),
                MaterialGraphNodes.OneMinus => Declare(node, $"vec3(1.0) - {Vec3Input(node, 0, "vec3(0.0)")}", MaterialGraphValueKind.Vector3),
                MaterialGraphNodes.Clamp => Clamp(node),
                MaterialGraphNodes.Saturate => Saturate(node),
                MaterialGraphNodes.Step => Step(node),
                MaterialGraphNodes.Smoothstep => SmoothStep(node),
                MaterialGraphNodes.Power => Power(node),
                MaterialGraphNodes.Fresnel => Fresnel(node),
                MaterialGraphNodes.Channel => Channel(node),
                MaterialGraphNodes.Panner => Panner(node),
                _ => Declare(node, "vec3(0.0)", MaterialGraphValueKind.Vector3),
            };

            _visiting.Remove(node.Id);
            _cache[node.Id] = result;
            return result;
        }

        private (string, MaterialGraphValueKind) Constant(GraphNodeRecord node, string key, string label, MaterialGraphValueKind kind)
        {
            string name = UniformName(node);
            float value = Number(node, key, 0.5f);
            _properties.Append("        ").Append(name).Append("(\"").Append(label).Append("\", Float) = ")
                .Append(value.ToString("0.######", CultureInfo.InvariantCulture)).Append('\n');
            _uniforms.Append("        uniform float ").Append(name).Append(";\n");
            return (name, kind);
        }

        private (string, MaterialGraphValueKind) ColorConstant(GraphNodeRecord node)
        {
            string name = UniformName(node);
            Float3 value = Vector(node, "Color", new Float3(0.8f));
            _properties.Append("        ").Append(name).Append("(\"Color\", Color) = (")
                .Append(Format(value.X)).Append(", ").Append(Format(value.Y)).Append(", ").Append(Format(value.Z)).Append(", 1.0)\n");
            _uniforms.Append("        uniform vec4 ").Append(name).Append(";\n");
            return (name, MaterialGraphValueKind.Color);
        }

        private (string, MaterialGraphValueKind) VectorConstant(GraphNodeRecord node)
        {
            string name = UniformName(node);
            Float3 value = Vector(node, "Vector", Float3.One);
            _properties.Append("        ").Append(name).Append("(\"Vector\", Vector) = (")
                .Append(Format(value.X)).Append(", ").Append(Format(value.Y)).Append(", ").Append(Format(value.Z)).Append(", 0.0)\n");
            _uniforms.Append("        uniform vec4 ").Append(name).Append(";\n");
            return Declare(node, $"{name}.xyz", MaterialGraphValueKind.Vector3);
        }

        private (string, MaterialGraphValueKind) Texture(GraphNodeRecord node)
        {
            string name = UniformName(node);
            _properties.Append("        ").Append(name).Append("(\"Texture\", Texture2D) = \"white\"\n");
            _uniforms.Append("        uniform sampler2D ").Append(name).Append(";\n");
            string uv = ToVec2(Input(node, 0, Uv, MaterialGraphValueKind.Vector2), MaterialGraphValueKind.Vector2);
            return Declare(node, $"texture({name}, {uv})", MaterialGraphValueKind.Color);
        }

        private (string, MaterialGraphValueKind) Binary(GraphNodeRecord node, string op, string fallback)
        {
            string a = ToVec3(Input(node, 0, fallback, MaterialGraphValueKind.Vector3), MaterialGraphValueKind.Vector3);
            string b = ToVec3(Input(node, 1, fallback, MaterialGraphValueKind.Vector3), MaterialGraphValueKind.Vector3);
            return Declare(node, $"{a} {op} {b}", MaterialGraphValueKind.Vector3);
        }

        private (string, MaterialGraphValueKind) Divide(GraphNodeRecord node)
        {
            string a = ToVec3(Input(node, 0, "vec3(0.0)", MaterialGraphValueKind.Vector3), MaterialGraphValueKind.Vector3);
            string b = ToVec3(Input(node, 1, "vec3(1.0)", MaterialGraphValueKind.Vector3), MaterialGraphValueKind.Vector3);
            return Declare(node, $"{a} / max({b}, vec3(0.00001))", MaterialGraphValueKind.Vector3);
        }

        private (string, MaterialGraphValueKind) Lerp(GraphNodeRecord node)
        {
            string a = ToVec3(Input(node, 0, "vec3(0.0)", MaterialGraphValueKind.Vector3), MaterialGraphValueKind.Vector3);
            string b = ToVec3(Input(node, 1, "vec3(1.0)", MaterialGraphValueKind.Vector3), MaterialGraphValueKind.Vector3);
            string t = ToFloat(Input(node, 2, "0.5", MaterialGraphValueKind.Float), MaterialGraphValueKind.Float);
            return Declare(node, $"mix({a}, {b}, clamp({t}, 0.0, 1.0))", MaterialGraphValueKind.Vector3);
        }

        private (string, MaterialGraphValueKind) Dot(GraphNodeRecord node)
        {
            string a = ToVec3(Input(node, 0, "vec3(0.0)", MaterialGraphValueKind.Vector3), MaterialGraphValueKind.Vector3);
            string b = ToVec3(Input(node, 1, "vec3(0.0)", MaterialGraphValueKind.Vector3), MaterialGraphValueKind.Vector3);
            return Declare(node, $"dot({a}, {b})", MaterialGraphValueKind.Float);
        }

        private (string, MaterialGraphValueKind) Unary(GraphNodeRecord node, string function)
        {
            string a = ToVec3(Input(node, 0, "vec3(0.0)", MaterialGraphValueKind.Vector3), MaterialGraphValueKind.Vector3);
            return Declare(node, $"{function}({a})", MaterialGraphValueKind.Vector3);
        }

        private (string, MaterialGraphValueKind) Clamp(GraphNodeRecord node)
        {
            string a = ToVec3(Input(node, 0, "vec3(0.0)", MaterialGraphValueKind.Vector3), MaterialGraphValueKind.Vector3);
            float min = Number(node, "Min", 0f);
            float max = Number(node, "Max", 1f);
            return Declare(node, $"clamp({a}, vec3({Format(min)}), vec3({Format(max)}))", MaterialGraphValueKind.Vector3);
        }

        private (string, MaterialGraphValueKind) Saturate(GraphNodeRecord node)
        {
            string a = ToVec3(Input(node, 0, "vec3(0.0)", MaterialGraphValueKind.Vector3), MaterialGraphValueKind.Vector3);
            return Declare(node, $"clamp({a}, vec3(0.0), vec3(1.0))", MaterialGraphValueKind.Vector3);
        }

        private (string, MaterialGraphValueKind) Step(GraphNodeRecord node)
        {
            float edge = Number(node, "Edge", 0.5f);
            string a = ToVec3(Input(node, 1, "vec3(0.0)", MaterialGraphValueKind.Vector3), MaterialGraphValueKind.Vector3);
            return Declare(node, $"step(vec3({Format(edge)}), {a})", MaterialGraphValueKind.Vector3);
        }

        private (string, MaterialGraphValueKind) SmoothStep(GraphNodeRecord node)
        {
            float min = Number(node, "Min", 0f);
            float max = Number(node, "Max", 1f);
            string a = ToVec3(Input(node, 2, "vec3(0.0)", MaterialGraphValueKind.Vector3), MaterialGraphValueKind.Vector3);
            return Declare(node, $"smoothstep(vec3({Format(min)}), vec3({Format(max)}), {a})", MaterialGraphValueKind.Vector3);
        }

        private (string, MaterialGraphValueKind) Power(GraphNodeRecord node)
        {
            string a = ToVec3(Input(node, 0, "vec3(1.0)", MaterialGraphValueKind.Vector3), MaterialGraphValueKind.Vector3);
            string b = ToVec3(Input(node, 1, "vec3(1.0)", MaterialGraphValueKind.Vector3), MaterialGraphValueKind.Vector3);
            return Declare(node, $"pow(max({a}, vec3(0.0)), max({b}, vec3(0.00001)))", MaterialGraphValueKind.Vector3);
        }

        private (string, MaterialGraphValueKind) Fresnel(GraphNodeRecord node)
        {
            string power = ToFloat(Input(node, 0, "4.0", MaterialGraphValueKind.Float), MaterialGraphValueKind.Float);
            return Declare(node,
                $"pow(1.0 - clamp(dot(normalize({Normal}), normalize({ViewDir})), 0.0, 1.0), max({power}, 0.0001))",
                MaterialGraphValueKind.Float);
        }

        private (string, MaterialGraphValueKind) Channel(GraphNodeRecord node)
        {
            string input = ToVec4(Input(node, 0, "vec4(0.0)", MaterialGraphValueKind.Color), MaterialGraphValueKind.Color);
            int channel = Math.Clamp((int)Number(node, "Channel", 0f), 0, 3);
            return Declare(node, $"{input}[{channel}]", MaterialGraphValueKind.Float);
        }

        private (string, MaterialGraphValueKind) TimeNode(GraphNodeRecord node)
        {
            _usesTime = true;
            return Declare(node, "_Time", MaterialGraphValueKind.Float);
        }

        private (string, MaterialGraphValueKind) Panner(GraphNodeRecord node)
        {
            string uv = ToVec2(Input(node, 0, Uv, MaterialGraphValueKind.Vector2), MaterialGraphValueKind.Vector2);
            Float3 speed = Vector(node, "Speed", new Float3(0.1f, 0.1f, 0f));
            _usesTime = true;
            return Declare(node, $"{uv} + vec2({Format(speed.X)}, {Format(speed.Y)}) * _Time", MaterialGraphValueKind.Vector2);
        }

        private (string, MaterialGraphValueKind) Declare(GraphNodeRecord node, string expression, MaterialGraphValueKind kind)
        {
            string name = $"n_{Sanitize(node.Id)}_{_temporaries++}";
            _body.Append("            ").Append(GlslType(kind)).Append(' ').Append(name).Append(" = ").Append(expression).Append(";\n");
            return (name, kind);
        }

        private string Vec3Input(GraphNodeRecord node, int index, string fallback)
            => ToVec3(Input(node, index, fallback, MaterialGraphValueKind.Vector3), MaterialGraphValueKind.Vector3);

        public string ToVec3(string expression, MaterialGraphValueKind kind) => kind switch
        {
            MaterialGraphValueKind.Float => $"vec3({expression})",
            MaterialGraphValueKind.Vector2 => $"vec3({expression}, 0.0)",
            MaterialGraphValueKind.Color => $"{expression}.rgb",
            _ => expression,
        };

        public string ToVec2(string expression, MaterialGraphValueKind kind) => kind switch
        {
            MaterialGraphValueKind.Float => $"vec2({expression})",
            MaterialGraphValueKind.Vector3 or MaterialGraphValueKind.Color => $"{expression}.xy",
            _ => expression,
        };

        public string ToVec4(string expression, MaterialGraphValueKind kind) => kind switch
        {
            MaterialGraphValueKind.Float => $"vec4(vec3({expression}), 1.0)",
            MaterialGraphValueKind.Vector2 => $"vec4({expression}, 0.0, 1.0)",
            MaterialGraphValueKind.Vector3 => $"vec4({expression}, 1.0)",
            _ => expression,
        };

        public string ToFloat(string expression, MaterialGraphValueKind kind) => kind switch
        {
            MaterialGraphValueKind.Vector2 => $"{expression}.x",
            MaterialGraphValueKind.Vector3 => $"{expression}.x",
            MaterialGraphValueKind.Color => $"{expression}.r",
            _ => expression,
        };

        private static string GlslType(MaterialGraphValueKind kind) => kind switch
        {
            MaterialGraphValueKind.Float => "float",
            MaterialGraphValueKind.Vector2 => "vec2",
            MaterialGraphValueKind.Color => "vec4",
            _ => "vec3",
        };

        private static string UniformName(GraphNodeRecord node) => $"_g_{Sanitize(node.Id)}";

        private static string Sanitize(string id)
        {
            var text = new StringBuilder(id.Length);
            foreach (char c in id)
                text.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
            return text.Length == 0 ? "node" : text.ToString();
        }

        private static string Format(float value) => value.ToString("0.######", CultureInfo.InvariantCulture);

        private static float Number(GraphNodeRecord node, string key, float fallback)
            => node.Properties.TryGetValue(key, out NodeValue value) ? value.Number : fallback;

        private static Float3 Vector(GraphNodeRecord node, string key, Float3 fallback)
            => node.Properties.TryGetValue(key, out NodeValue value) ? value.Vector : fallback;
    }
}
