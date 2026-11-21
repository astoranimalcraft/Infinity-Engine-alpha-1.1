using Prowl.Runtime.Rendering;

namespace Prowl.Runtime.Rendering.Pipelines.Infinity;

/// <summary>
/// Infinity bloom, in linear HDR before tonemapping.
///
/// Graphite's bloom is a multi-pass dual filter feature. This is the single-pass form: one gather of the bright pass
/// over a widening cross, which costs one draw and no intermediate targets, so it fits cameras that cannot afford the
/// extra passes. Use Graphite's stock Bloom component where the full dual filter is wanted.
/// </summary>
[VolumeComponentMenu("Infinity/Image/Bloom")]
public sealed class InfinityBloom : PostEffect, IInfinityVolumeComponent
{
    [Range(0f, 10f)] public VolumeParameter<float> Intensity = new(1.0f);

    [Range(0f, 10f)] public VolumeParameter<float> Threshold = new(1.15f);

    [Range(0f, 1f)] public VolumeParameter<float> SoftKnee = new(0.5f);

    [Range(0f, 65000f)] public VolumeParameter<float> Clamp = new(65000.0f);

    /// <summary>Weights each bright sample by its own luminance, so a single very bright pixel cannot flicker.</summary>
    public VolumeParameter<bool> AntiFlicker = new(true);

    /// <summary>How many rings of taps the gather walks, each ring further out than the last.</summary>
    [Range(1, 8)] public VolumeParameter<int> Iterations = new(5);

    /// <summary>How far the gather reaches, in pixels per ring.</summary>
    [Range(0f, 1f)] public VolumeParameter<float> Scatter = new(0.72f);

    public override bool IsActive => InfinityPipelineMode.Enabled && Intensity.Value > 0.0f;

    protected override PostEffectStage Stage => PostEffectStage.BeforeTonemap;
    protected override int Order => 5;

    protected override string Shader => """
        uniform float _Intensity;
        uniform float _Threshold;
        uniform float _SoftKnee;
        uniform float _Clamp;
        uniform float _AntiFlicker;
        uniform int _Iterations;
        uniform float _Scatter;

        vec3 InfinityPrefilter(vec3 c)
        {
            c = min(c, vec3(_Clamp));
            float brightness = max(c.r, max(c.g, c.b));
            float knee = _Threshold * _SoftKnee + 1e-5;
            float soft = clamp(brightness - _Threshold + knee, 0.0, 2.0 * knee);
            soft = soft * soft / (4.0 * knee);
            float contribution = max(soft, brightness - _Threshold) / max(brightness, 1e-5);
            vec3 bright = c * contribution;

            // Karis average: divide by luminance so one hot pixel weighs no more than a bright area
            if (_AntiFlicker > 0.5)
                bright /= 1.0 + dot(bright, vec3(0.2126, 0.7152, 0.0722));

            return bright;
        }

        void main()
        {
            vec4 source = Graphite_SampleSource(vUV);
            vec2 texel = 1.0 / vec2(Graphite_Size(Graphite_Source, 0));

            vec3 bloom = InfinityPrefilter(source.rgb);
            float total = 1.0;
            float reach = max(_Scatter, 0.05) * 24.0;
            int rings = clamp(_Iterations, 1, 8);

            for (int i = 1; i <= 8; i++)
            {
                if (i > rings) break;
                float offset = float(i) * reach;
                float weight = 1.0 / (1.0 + float(i) * 0.5);

                bloom += InfinityPrefilter(Graphite_SampleSource(vUV + vec2(texel.x * offset, 0.0)).rgb) * weight;
                bloom += InfinityPrefilter(Graphite_SampleSource(vUV - vec2(texel.x * offset, 0.0)).rgb) * weight;
                bloom += InfinityPrefilter(Graphite_SampleSource(vUV + vec2(0.0, texel.y * offset)).rgb) * weight;
                bloom += InfinityPrefilter(Graphite_SampleSource(vUV - vec2(0.0, texel.y * offset)).rgb) * weight;
                total += 4.0 * weight;
            }

            bloom /= total;
            outColor = vec4(source.rgb + bloom * _Intensity, source.a);
        }
        """;
}
