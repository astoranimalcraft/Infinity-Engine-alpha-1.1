// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Infinity.Runtime.Graphs;

namespace Infinity.Runtime.Rendering;

/// <summary>
/// Draws a material graph as a full-screen post process. Set the graph's Post Process flag, author it with Scene
/// Color and the math nodes, then add this override to a volume or to the camera's own profile.
/// </summary>
[VolumeComponentMenu("Post Processing/Post Process Material")]
public sealed class PostProcessMaterialEffect : PostEffect
{
    /// <summary>The material graph to draw. It must have Post Process enabled.</summary>
    public VolumeParameter<MaterialGraph> Graph = new(null!);

    /// <summary>Blends the effect against the image the camera already drew.</summary>
    public VolumeParameter<float> Intensity = new(1f);

    public override bool IsActive => Graph.Value.IsValid() && Graph.Value!.PostProcess && Intensity.Value > 0.0001f;

    protected override PostEffectStage Stage => PostEffectStage.AfterTonemap;

    protected override string Shader
    {
        get
        {
            MaterialGraph? graph = Graph.Value;
            string source = graph.IsValid()
                ? MaterialGraphCompiler.CompilePostProcessFragment(graph!, "InfinityPostProcessGraph")
                : "void InfinityPostProcessGraph() { outColor = Graphite_SampleSource(vUV); }";

            // Intensity is a built-in uniform of this effect rather than a graph node, so the graph stays reusable.
            return $$"""
                uniform float _Intensity;
                {{source}}
                void main()
                {
                    InfinityPostProcessGraph();
                    vec4 source = Graphite_SampleSource(vUV);
                    outColor = vec4(mix(source.rgb, outColor.rgb, clamp(_Intensity, 0.0, 1.0)), outColor.a);
                }
                """;
        }
    }
}
