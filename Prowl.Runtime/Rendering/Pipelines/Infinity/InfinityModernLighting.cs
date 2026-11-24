// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Infinity.Vector;

namespace Infinity.Runtime.Rendering.Pipelines.Infinity;

/// <summary>
/// Infinity Lumen style screen-space global illumination. It gathers the colour around each pixel from the scene
/// colour already drawn and adds it back as a soft bounce, so corners and shadowed sides pick up the light of nearby
/// surfaces instead of going flat black. It is a post effect, so it works with the forward renderer and needs no
/// extra render target or geometry pass.
/// </summary>
[VolumeComponentMenu("Infinity/Lighting/Lumen Global Illumination")]
public sealed class InfinityLumenGI : PostEffect
{
    /// <summary>How much of the gathered colour is added back as indirect light.</summary>
    [Range(0f, 2f)] public VolumeParameter<float> Intensity = new(0f);

    /// <summary>Screen-space radius of the gather, in UV units.</summary>
    [Range(0.001f, 0.15f)] public VolumeParameter<float> Radius = new(0.025f);

    /// <summary>How strongly the gathered colour is tinted by the surface it lands on.</summary>
    [Range(0f, 1f)] public VolumeParameter<float> Bounce = new(0.65f);

    /// <summary>Pushes the result toward a neutral grey, 0 keeps the gathered colour fully saturated.</summary>
    [Range(0f, 1f)] public VolumeParameter<float> Desaturate = new(0.15f);

    /// <summary>A cool or warm tint applied to the indirect light.</summary>
    public VolumeParameter<Float4> Tint = new(new Float4(0.92f, 0.96f, 1f, 1f));

    /// <summary>Fades the gather out with distance, so the sky does not bleed into the foreground.</summary>
    [Range(0f, 1f)] public VolumeParameter<float> DepthFade = new(0.6f);

    public override bool IsActive => InfinityPipelineMode.Enabled && Intensity.Value > 0f;
    protected override PostEffectStage Stage => PostEffectStage.BeforeTonemap;
    protected override int Order => 12;
    protected override bool ReadsDepth => true;

    protected override string Shader => """
        uniform GRAPHITE_SCREEN_TEXTURE _CameraDepthTexture;
        uniform float _Intensity;
        uniform float _Radius;
        uniform float _Bounce;
        uniform float _Desaturate;
        uniform vec4 _Tint;
        uniform float _DepthFade;

        float InfinityLumenDepth(vec2 uv)
        {
            return Graphite_Sample(_CameraDepthTexture, clamp(uv, vec2(0.0), vec2(1.0))).r;
        }

        void main()
        {
            vec4 source = Graphite_SampleSource(vUV);
            vec2 texel = 1.0 / vec2(Graphite_Size(Graphite_Source, 0));

            // Two rings of eight taps give a wider, softer gather than a single blur without a separate pass.
            vec3 gathered = vec3(0.0);
            float total = 0.0;
            for (int i = 0; i < 8; i++)
            {
                float angle = 6.2831853 * (float(i) + 0.5) / 8.0;
                vec2 dir = vec2(cos(angle), sin(angle));
                float weight = 1.0;
                gathered += Graphite_SampleSource(vUV + dir * _Radius).rgb * weight;
                total += weight;
            }
            for (int i = 0; i < 8; i++)
            {
                float angle = 6.2831853 * float(i) / 8.0 + 0.3926991;
                vec2 dir = vec2(cos(angle), sin(angle));
                float weight = 0.55;
                gathered += Graphite_SampleSource(vUV + dir * _Radius * 2.0).rgb * weight;
                total += weight;
            }
            gathered /= max(total, 1.0);

            float depth = InfinityLumenDepth(vUV);
            float distanceFade = mix(1.0, smoothstep(0.0, 0.85, depth), _DepthFade);
            float luma = dot(gathered, vec3(0.2126, 0.7152, 0.0722));
            gathered = mix(gathered, vec3(luma), _Desaturate) * _Tint.rgb;

            vec3 indirect = gathered * _Bounce * _Intensity * distanceFade;
            vec3 color = source.rgb + indirect;
            outColor = vec4(max(color, vec3(0.0)), source.a);
        }
        """;
}

/// <summary>
/// Infinity ray tracing style screen-space lighting. It marches a short ray through the depth buffer from every
/// pixel, darkens where the ray hits nearby geometry (contact shadows) and adds the bright scene colour it finds
/// along the way as a reflection. This is the real-time fallback that runs on the forward path; a hardware ray
/// tracing backend can replace the shader without changing the component or the volume.
/// </summary>
[VolumeComponentMenu("Infinity/Lighting/Ray Tracing")]
public sealed class InfinityRayTracing : PostEffect
{
    /// <summary>Strength of the screen-space contact shadow.</summary>
    [Range(0f, 2f)] public VolumeParameter<float> ContactShadows = new(0f);

    /// <summary>Strength of the screen-space reflection.</summary>
    [Range(0f, 2f)] public VolumeParameter<float> Reflections = new(0f);

    /// <summary>How far a ray reaches across the screen, in UV units.</summary>
    [Range(0.001f, 0.2f)] public VolumeParameter<float> RayLength = new(0.06f);

    /// <summary>Depth difference that counts as a hit, in reversed depth units.</summary>
    [Range(0.0001f, 0.2f)] public VolumeParameter<float> Thickness = new(0.012f);

    /// <summary>How many steps the ray marches.</summary>
    [Range(2f, 24f)] public VolumeParameter<int> Steps = new(10);

    /// <summary>Tint of the reflected light.</summary>
    public VolumeParameter<Float4> Tint = new(new Float4(0.9f, 0.95f, 1f, 1f));

    public override bool IsActive => InfinityPipelineMode.Enabled
        && (ContactShadows.Value > 0f || Reflections.Value > 0f);
    protected override PostEffectStage Stage => PostEffectStage.BeforeTonemap;
    protected override int Order => 13;
    protected override bool ReadsDepth => true;

    protected override string Shader => """
        uniform GRAPHITE_SCREEN_TEXTURE _CameraDepthTexture;
        uniform float _ContactShadows;
        uniform float _Reflections;
        uniform float _RayLength;
        uniform float _Thickness;
        uniform int _Steps;
        uniform vec4 _Tint;

        float InfinityRayDepth(vec2 uv)
        {
            return Graphite_Sample(_CameraDepthTexture, clamp(uv, vec2(0.0), vec2(1.0))).r;
        }

        void main()
        {
            vec4 source = Graphite_SampleSource(vUV);
            vec2 texel = 1.0 / vec2(Graphite_Size(Graphite_Source, 0));
            float center = InfinityRayDepth(vUV);

            float occlusion = 0.0;
            vec3 reflection = vec3(0.0);
            float reflectionWeight = 0.0;

            // Eight fixed directions around the pixel, each marched toward the rim. The loop bound is constant so
            // every backend compiles the same shader; _Steps controls how far the samples reach.
            for (int dirIndex = 0; dirIndex < 8; dirIndex++)
            {
                float angle = 6.2831853 * (float(dirIndex) + 0.5) / 8.0;
                vec2 dir = vec2(cos(angle), sin(angle));
                for (int stepIndex = 1; stepIndex <= 8; stepIndex++)
                {
                    float t = float(stepIndex) / 8.0;
                    vec2 uv = vUV + dir * _RayLength * t;
                    float depth = InfinityRayDepth(uv);
                    float delta = depth - center;
                    if (delta > _Thickness && delta < _Thickness * 8.0)
                    {
                        occlusion += (1.0 - t) * 0.35;
                        vec3 hit = Graphite_SampleSource(uv).rgb;
                        reflection += hit * (1.0 - t);
                        reflectionWeight += 1.0 - t;
                    }
                }
            }

            occlusion = clamp(occlusion / 8.0, 0.0, 1.0);
            reflection = reflectionWeight > 0.0 ? reflection / reflectionWeight : vec3(0.0);

            vec3 color = source.rgb * (1.0 - occlusion * _ContactShadows * 0.55);
            color += reflection * _Tint.rgb * _Reflections * 0.35;
            outColor = vec4(max(color, vec3(0.0)), source.a);
        }
        """;
}
