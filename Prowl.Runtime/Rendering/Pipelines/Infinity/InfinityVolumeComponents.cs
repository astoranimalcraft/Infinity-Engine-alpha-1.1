using Infinity.Echo;
using Infinity.Runtime;
using Infinity.Runtime.Rendering;
using Infinity.Runtime.Resources;
using Infinity.Vector;
using G = Infinity.Graphite.Rendering;

namespace Infinity.Runtime.Rendering.Pipelines.Infinity;

public enum InfinityToneMappingMode
{
    Disabled,
    ACES,
    AgX,
    Reinhard,
    Uncharted2
}

/// <summary>Infinity HDR tone mapping. Runs in the exclusive Graphite Tonemap slot.</summary>
[VolumeComponentMenu("Infinity/Image/Tonemapping")]
public sealed class InfinityTonemapping : PostEffect
{
    public VolumeParameter<InfinityToneMappingMode> Mode = new(InfinityToneMappingMode.AgX);

    public override bool IsActive => InfinityPipelineMode.Enabled && Mode.Value != InfinityToneMappingMode.Disabled;
    protected override PostEffectStage Stage => PostEffectStage.Tonemap;

    protected override string Shader => """
        uniform int _Mode;

        vec3 InfinityACES(vec3 x)
        {
            const float a = 2.51;
            const float b = 0.03;
            const float c = 2.43;
            const float d = 0.59;
            const float e = 0.14;
            return clamp((x * (a * x + b)) / (x * (c * x + d) + e), 0.0, 1.0);
        }

        vec3 InfinityAgX(vec3 x)
        {
            // AgX, the minimal fit: inset the primaries, encode over the usable EV range, apply the sigmoid
            // polynomial, undo the inset. Its output is display referred, so it is decoded back to linear here:
            // the Tonemap stage contract is linear in, linear out, and the display encoding happens after it.
            const mat3 inset = mat3(
                0.842479062253094, 0.0423282422610123, 0.0423756549057051,
                0.0784335999999992, 0.878468636469772, 0.0784336,
                0.0792237451477643, 0.0791661274605434, 0.879142973793104);

            const mat3 outset = mat3(
                1.19687900512017, -0.0528968517574562, -0.0529716355144438,
                -0.0980208811401368, 1.15190312990417, -0.0980434501171241,
                -0.0990297440797205, -0.0989611768448433, 1.15107367264116);

            const float minEV = -12.47393;
            const float maxEV = 4.026069;

            vec3 c = inset * max(x, vec3(0.0));
            c = clamp(log2(max(c, vec3(1e-10))), minEV, maxEV);
            c = (c - minEV) / (maxEV - minEV);

            vec3 c2 = c * c;
            vec3 c4 = c2 * c2;
            c = 15.5 * c4 * c2 - 40.14 * c4 * c + 31.96 * c4 - 6.868 * c2 * c + 0.4298 * c2 + 0.1191 * c - 0.00232;

            c = clamp(outset * c, 0.0, 1.0);

            // sRGB decode, so a mid grey comes back out of the stage as a mid grey
            return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
        }

        vec3 InfinityUncharted2(vec3 x)
        {
            const float A = 0.15;
            const float B = 0.50;
            const float C = 0.10;
            const float D = 0.20;
            const float E = 0.02;
            const float F = 0.30;
            vec3 numerator = x * (A * x + C * B) + D * E;
            vec3 denominator = x * (A * x + B) + D * F;
            return numerator / max(denominator, vec3(1e-5)) - E / F;
        }

        void main()
        {
            vec4 source = Graphite_SampleSource(vUV);
            vec3 color = max(source.rgb, vec3(0.0));
            if (_Mode == 1)
                color = InfinityACES(color);
            else if (_Mode == 2)
                color = InfinityAgX(color);
            else if (_Mode == 3)
                color = color / (1.0 + color);
            else if (_Mode == 4)
                color = clamp(InfinityUncharted2(color * 2.0) / max(InfinityUncharted2(vec3(11.2)), vec3(1e-5)), 0.0, 1.0);
            outColor = vec4(color, source.a);
        }
        """;
}

/// <summary>Histogram auto exposure using Graphite's native temporal exposure feature, selected only in Infinity mode.</summary>
[VolumeComponentMenu("Infinity/Camera/Auto Exposure")]
public sealed class InfinityAutoExposure : VolumeComponent<G.AutoExposure>
{
    public VolumeParameter<bool> Enabled = new(false);
    [Range(-5f, 5f)] public VolumeParameter<float> ExposureCompensation = new(2.5f);
    public VolumeParameter<float> AdaptSpeedUp = new(3f);
    public VolumeParameter<float> AdaptSpeedDown = new(1.5f);
    public VolumeParameter<float> MinEV100 = new(-6f);
    public VolumeParameter<float> MaxEV100 = new(20f);

    public override bool IsActive => InfinityPipelineMode.Enabled && Enabled.Value;
    protected override G.AutoExposure CreateFeature(G.Renderer renderer) => new(renderer);

    protected override void Apply(G.AutoExposure feature, Camera camera)
    {
        feature.ExposureCompensation = ExposureCompensation;
        feature.AdaptSpeedUp = AdaptSpeedUp;
        feature.AdaptSpeedDown = AdaptSpeedDown;
        feature.MinEV100 = MinEV100;
        feature.MaxEV100 = MaxEV100;
    }
}

/// <summary>Infinity multi-pass bloom using the custom Graphite dual-filter feature.</summary>


/// <summary>Infinity ambient occlusion. Uses Graphite GTAO, which already includes temporal accumulation and bilateral blur.</summary>
[VolumeComponentMenu("Infinity/Lighting/Ambient Occlusion")]
public sealed class InfinityAmbientOcclusion : VolumeComponent<G.Gtao>
{
    [Range(0f, 4f)] public VolumeParameter<float> Intensity = new(0f);
    public VolumeParameter<float> Radius = new(1f);
    [Range(1f, 16f)] public VolumeParameter<int> Slices = new(6);
    [Range(1f, 64f)] public VolumeParameter<int> DirectionSamples = new(32);
    public VolumeParameter<float> BlurRadius = new(1f);
    public VolumeParameter<bool> UseTemporal = new(true);
    [Range(0f, 1f)] public VolumeParameter<float> TemporalResponse = new(0.9f);
    public VolumeParameter<EffectResolution> Resolution = new(EffectResolution.Quarter);

    public override bool IsActive => InfinityPipelineMode.Enabled && Intensity.Value > 0f;
    protected override G.Gtao CreateFeature(G.Renderer renderer) => new(renderer);

    protected override void Apply(G.Gtao feature, Camera camera)
    {
        feature.Slices = Slices;
        feature.DirectionSamples = DirectionSamples;
        feature.Radius = Radius;
        feature.Intensity = Intensity;
        feature.BlurRadius = BlurRadius;
        feature.UseTemporal = UseTemporal;
        feature.TemporalResponse = TemporalResponse;
        feature.Resolution = (G.EffectResolution)Resolution.Value;
    }
}

/// <summary>Infinity screen-space reflections. Uses Graphite's screen-space ray reuse and temporal accumulation.</summary>
[VolumeComponentMenu("Infinity/Lighting/Screen Space Reflections")]
public sealed class InfinityScreenSpaceReflections : VolumeComponent<G.ScreenSpaceReflections>
{
    public VolumeParameter<bool> Enabled = new(false);
    public VolumeParameter<EffectResolution> RayResolution = new(EffectResolution.Half);
    [Range(1f, 100f)] public VolumeParameter<float> RayDistance = new(70f);
    [Range(0f, 1f)] public VolumeParameter<float> BRDFBias = new(0.7f);
    public VolumeParameter<bool> RayReuse = new(true);
    public VolumeParameter<bool> Normalization = new(true);
    public VolumeParameter<bool> ReduceFireflies = new(true);
    public VolumeParameter<bool> UseMipMap = new(true);
    public VolumeParameter<bool> UseTemporal = new(true);
    public VolumeParameter<float> TemporalScale = new(2f);
    [Range(0f, 1f)] public VolumeParameter<float> TemporalResponse = new(0.85f);
    public VolumeParameter<bool> ReflectionVelocity = new(true);
    [Range(0f, 1f)] public VolumeParameter<float> ScreenFadeSize = new(0.25f);

    public override bool IsActive => InfinityPipelineMode.Enabled && Enabled.Value;
    protected override G.ScreenSpaceReflections CreateFeature(G.Renderer renderer) => new(renderer);

    protected override void Apply(G.ScreenSpaceReflections feature, Camera camera)
    {
        feature.RayResolution = (G.EffectResolution)RayResolution.Value;
        feature.RayDistance = RayDistance;
        feature.BrdfBias = BRDFBias;
        feature.RayReuse = RayReuse;
        feature.Normalization = Normalization;
        feature.ReduceFireflies = ReduceFireflies;
        feature.UseMipMap = UseMipMap;
        feature.UseTemporal = UseTemporal;
        feature.TemporalScale = TemporalScale;
        feature.TemporalResponse = TemporalResponse;
        feature.ReflectionVelocity = ReflectionVelocity;
        feature.ScreenFadeSize = ScreenFadeSize;
    }
}

/// <summary>Infinity color grading before tone mapping, in linear HDR.</summary>
[VolumeComponentMenu("Infinity/Color/Color Grading")]
public sealed class InfinityColorGrading : PostEffect
{
    [Range(-5f, 5f)] public VolumeParameter<float> ExposureEV = new(0f);
    [Range(-1f, 1f)] public VolumeParameter<float> Contrast = new(0f);
    [Range(-1f, 1f)] public VolumeParameter<float> Saturation = new(0f);
    [Range(-1f, 1f)] public VolumeParameter<float> Temperature = new(0f);
    [Range(-1f, 1f)] public VolumeParameter<float> Tint = new(0f);
    public VolumeParameter<Float4> Lift = new(new Float4(0f, 0f, 0f, 0f));
    public VolumeParameter<Float4> Gamma = new(new Float4(0f, 0f, 0f, 0f));
    public VolumeParameter<Float4> Gain = new(new Float4(0f, 0f, 0f, 0f));

    public override bool IsActive => InfinityPipelineMode.Enabled && (ExposureEV.Value != 0f || Contrast.Value != 0f || Saturation.Value != 0f || Temperature.Value != 0f || Tint.Value != 0f
        || Lift.Value.X != 0f || Lift.Value.Y != 0f || Lift.Value.Z != 0f
        || Gamma.Value.X != 0f || Gamma.Value.Y != 0f || Gamma.Value.Z != 0f
        || Gain.Value.X != 0f || Gain.Value.Y != 0f || Gain.Value.Z != 0f);
    protected override PostEffectStage Stage => PostEffectStage.BeforeTonemap;
    protected override int Order => 10;

    protected override string Shader => """
        uniform float _ExposureEV;
        uniform float _Contrast;
        uniform float _Saturation;
        uniform float _Temperature;
        uniform float _Tint;
        uniform vec4 _Lift;
        uniform vec4 _Gamma;
        uniform vec4 _Gain;

        void main()
        {
            vec4 source = Graphite_SampleSource(vUV);
            vec3 color = max(source.rgb, vec3(0.0)) * exp2(_ExposureEV);
            color = (color - vec3(0.18)) * (1.0 + _Contrast) + vec3(0.18);
            float luminance = dot(color, vec3(0.2126, 0.7152, 0.0722));
            color = mix(vec3(luminance), color, 1.0 + _Saturation);
            color *= vec3(1.0 + 0.10 * _Temperature, 1.0 + 0.04 * _Tint, 1.0 - 0.10 * _Temperature);
            color += vec3(-0.015 * _Tint, 0.0, 0.015 * _Tint);
            color = max(color + _Lift.rgb, vec3(0.0));
            color = pow(color, 1.0 / max(vec3(1.0) + _Gamma.rgb, vec3(0.01)));
            color *= max(vec3(0.0), vec3(1.0) + _Gain.rgb);
            outColor = vec4(max(color, vec3(0.0)), source.a);
        }
        """;
}

/// <summary>Cinematic display-space effects: vignette, chromatic aberration, film grain and edge sharpening.</summary>
[VolumeComponentMenu("Infinity/Image/Cinematic Effects")]
public sealed class InfinityImageEffects : PostEffect
{
    [Range(0f, 1f)] public VolumeParameter<float> Vignette = new(0f);
    [Range(0.1f, 1f)] public VolumeParameter<float> VignetteRadius = new(0.72f);
    [Range(0.01f, 1f)] public VolumeParameter<float> VignetteSmoothness = new(0.25f);
    [Range(0f, 1f)] public VolumeParameter<float> VignetteRoundness = new(1f);
    [Range(0f, 2f)] public VolumeParameter<float> ChromaticAberration = new(0f);
    [Range(0f, 2f)] public VolumeParameter<float> ChromaticDistortion = new(0.5f);
    [Range(0f, 1f)] public VolumeParameter<float> FilmGrain = new(0f);
    [Range(0f, 1f)] public VolumeParameter<float> FilmGrainResponse = new(0.5f);
    [Range(0f, 2f)] public VolumeParameter<float> Sharpen = new(0f);
    [Range(0.5f, 4f)] public VolumeParameter<float> SharpenRadius = new(1f);
    [Range(0.5f, 4f)] public VolumeParameter<float> GrainScale = new(1.5f);

    public override bool IsActive => InfinityPipelineMode.Enabled
        && (Vignette.Value > 0f || ChromaticAberration.Value > 0f || FilmGrain.Value > 0f || Sharpen.Value > 0f);
    protected override PostEffectStage Stage => PostEffectStage.AfterTonemap;
    protected override int Order => 40;

    protected override string Shader => """
        #include "Graphite/Math.glsl"
        uniform float _Vignette;
        uniform float _VignetteRadius;
        uniform float _VignetteSmoothness;
        uniform float _VignetteRoundness;
        uniform float _ChromaticAberration;
        uniform float _ChromaticDistortion;
        uniform float _FilmGrain;
        uniform float _FilmGrainResponse;
        uniform float _Sharpen;
        uniform float _SharpenRadius;
        uniform float _GrainScale;

        float InfinityHash(vec2 p)
        {
            p = fract(p * vec2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return fract(p.x * p.y);
        }

        void main()
        {
            vec2 uv = vUV;
            vec2 centered = uv - vec2(0.5);
            float radius = length(centered);
            vec2 direction = normalize(centered + vec2(1e-5));
            float chromaOffset = _ChromaticAberration * _ChromaticDistortion * 0.0035 * radius;

            vec4 color;
            color.r = Graphite_SampleSource(uv + direction * chromaOffset).r;
            color.g = Graphite_SampleSource(uv).g;
            color.b = Graphite_SampleSource(uv - direction * chromaOffset).b;
            color.a = Graphite_SampleSource(uv).a;

            vec2 texel = _SharpenRadius / vec2(Graphite_Size(Graphite_Source, 0));
            vec3 north = Graphite_SampleSource(uv + vec2(0.0, texel.y)).rgb;
            vec3 south = Graphite_SampleSource(uv - vec2(0.0, texel.y)).rgb;
            vec3 east  = Graphite_SampleSource(uv + vec2(texel.x, 0.0)).rgb;
            vec3 west  = Graphite_SampleSource(uv - vec2(texel.x, 0.0)).rgb;
            vec3 averageColor = (north + south + east + west) * 0.25;
            color.rgb += (color.rgb - averageColor) * (_Sharpen * 0.5);

            float vignetteRadius = length(vec2(centered.x * mix(0.75, 1.25, _VignetteRoundness), centered.y)) * 1.41421356;
            float vignette = smoothstep(_VignetteRadius, _VignetteRadius + max(_VignetteSmoothness, 1e-3), vignetteRadius);
            color.rgb *= 1.0 - vignette * _Vignette;

            float noise = InfinityHash(floor(uv * vec2(Graphite_Size(Graphite_Source, 0)) * _GrainScale)) - 0.5;
            color.rgb += noise * _FilmGrain * mix(0.5, 1.5, _FilmGrainResponse) * 0.04;
            outColor = vec4(max(color.rgb, vec3(0.0)), color.a);
        }
        """;
}

/// <summary>Creates a VolumeProfile containing the Infinity effects. It does not modify the stock Prowl profile.</summary>
public static class InfinityVolumeDefaults
{
    /// <summary>Creates a new profile with Infinity post-processing defaults.</summary>
    public static VolumeProfile CreateDefaultProfile(string name = "Infinity Modern Volume")
    {
        // Start with Prowl's own camera defaults, then add Infinity equivalents with copied values.
        // This profile therefore works in both modes without removing the stock Tonemapping/Auto Exposure components.
        VolumeProfile profile = VolumeProfile.CreateCameraDefault(name);
        AddInfinityOverrides(profile, copyStockValues: true);
        return profile;
    }

    /// <summary>
    /// Adds Infinity equivalents to an existing Prowl profile. Existing components are preserved.
    /// When copyStockValues is true, settings from stock components are copied into newly-created Infinity components,
    /// allowing one VolumeProfile to switch between Standard and Infinity modes.
    /// </summary>
    public static void AddInfinityOverrides(VolumeProfile profile, bool copyStockValues = true)
    {
        if (profile.IsNotValid()) throw new System.ArgumentNullException(nameof(profile));

        bool addTonemap = !profile.Has<InfinityTonemapping>();
        bool addExposure = !profile.Has<InfinityAutoExposure>();
        bool addBloom = !profile.Has<InfinityBloom>();
        bool addColor = !profile.Has<InfinityColorGrading>();
        bool addImage = !profile.Has<InfinityImageEffects>();
        bool addAo = !profile.Has<InfinityAmbientOcclusion>();
        bool addSsr = !profile.Has<InfinityScreenSpaceReflections>();
        bool addMotion = !profile.Has<InfinityMotionBlur>();
        bool addDof = !profile.Has<InfinityDepthOfField>();
        bool addGodRays = !profile.Has<InfinityGodRays>();
        bool addLumen = !profile.Has<InfinityLumenGI>();
        bool addRayTracing = !profile.Has<InfinityRayTracing>();

        InfinityTonemapping tone = profile.Add<InfinityTonemapping>();
        InfinityAutoExposure exposure = profile.Add<InfinityAutoExposure>();
        InfinityBloom bloom = profile.Add<InfinityBloom>();
        InfinityColorGrading color = profile.Add<InfinityColorGrading>();
        InfinityImageEffects image = profile.Add<InfinityImageEffects>();
        InfinityAmbientOcclusion ao = profile.Add<InfinityAmbientOcclusion>();
        InfinityScreenSpaceReflections ssr = profile.Add<InfinityScreenSpaceReflections>();
        InfinityMotionBlur motion = profile.Add<InfinityMotionBlur>();
        InfinityDepthOfField dof = profile.Add<InfinityDepthOfField>();
        InfinityGodRays rays = profile.Add<InfinityGodRays>();
        // Added inactive: the user turns Lumen or ray tracing on in the volume, so a new profile stays predictable.
        if (addLumen) profile.Add<InfinityLumenGI>();
        if (addRayTracing) profile.Add<InfinityRayTracing>();

        if (!copyStockValues) return;

        if (addTonemap && profile.Get<Tonemapping>() is { } stockTone)
        {
            tone.Mode.Value = stockTone.Mode.Value switch
            {
                TonemappingMode.ACES => InfinityToneMappingMode.ACES,
                TonemappingMode.AgXNeutral or TonemappingMode.AgXPunchy => InfinityToneMappingMode.AgX,
                TonemappingMode.Reinhard => InfinityToneMappingMode.Reinhard,
                TonemappingMode.Uncharted2 => InfinityToneMappingMode.Uncharted2,
                _ => InfinityToneMappingMode.Disabled
            };
            tone.Mode.Override = stockTone.Mode.Override;
            tone.Active = stockTone.Active;
        }

        if (addExposure && profile.Get<AutoExposure>() is { } stockExposure)
        {
            Copy(stockExposure.Enabled, exposure.Enabled);
            Copy(stockExposure.ExposureCompensation, exposure.ExposureCompensation);
            Copy(stockExposure.AdaptSpeedUp, exposure.AdaptSpeedUp);
            Copy(stockExposure.AdaptSpeedDown, exposure.AdaptSpeedDown);
            Copy(stockExposure.MinEV100, exposure.MinEV100);
            Copy(stockExposure.MaxEV100, exposure.MaxEV100);
            exposure.Active = stockExposure.Active;
        }
        if (addBloom && profile.Get<Bloom>() is { } stockBloom)
        {
            Copy(stockBloom.Intensity, bloom.Intensity); Copy(stockBloom.Threshold, bloom.Threshold);
            Copy(stockBloom.SoftKnee, bloom.SoftKnee); Copy(stockBloom.Clamp, bloom.Clamp);
            Copy(stockBloom.AntiFlicker, bloom.AntiFlicker); Copy(stockBloom.Iterations, bloom.Iterations);
            Copy(stockBloom.Scatter, bloom.Scatter);
            bloom.Active = stockBloom.Active;
        }
        if (addColor && profile.Get<ColorGrading>() is { } stockColor)
        {
            Copy(stockColor.PostExposure, color.ExposureEV); Copy(stockColor.Contrast, color.Contrast);
            Copy(stockColor.Saturation, color.Saturation); Copy(stockColor.Temperature, color.Temperature);
            color.Lift.Value = ToFloat4(stockColor.Lift.Value); color.Lift.Override = stockColor.Lift.Override;
            color.Gamma.Value = ToFloat4(stockColor.Gamma.Value); color.Gamma.Override = stockColor.Gamma.Override;
            color.Gain.Value = ToFloat4(stockColor.Gain.Value); color.Gain.Override = stockColor.Gain.Override;
            color.Active = stockColor.Active;
        }
        if (addImage)
        {
            bool hasImageSource = false;
            bool anyImageSourceActive = false;
            if (profile.Get<Vignette>() is { } stockVignette)
            {
                hasImageSource = true;
                anyImageSourceActive |= stockVignette.Active;
                Copy(stockVignette.Intensity, image.Vignette);
                Copy(stockVignette.Smoothness, image.VignetteSmoothness);
                Copy(stockVignette.Roundness, image.VignetteRoundness);
                if (!stockVignette.Active) image.Vignette.Value = 0f;
            }
            if (profile.Get<ChromaticAberration>() is { } stockChroma)
            {
                hasImageSource = true;
                anyImageSourceActive |= stockChroma.Active;
                Copy(stockChroma.Intensity, image.ChromaticAberration);
                Copy(stockChroma.Distortion, image.ChromaticDistortion);
                if (!stockChroma.Active) image.ChromaticAberration.Value = 0f;
            }
            if (profile.Get<FilmGrain>() is { } stockGrain)
            {
                hasImageSource = true;
                anyImageSourceActive |= stockGrain.Active;
                Copy(stockGrain.Intensity, image.FilmGrain);
                Copy(stockGrain.Response, image.FilmGrainResponse);
                if (!stockGrain.Active) image.FilmGrain.Value = 0f;
            }
            if (profile.Get<Sharpen>() is { } stockSharpen)
            {
                hasImageSource = true;
                anyImageSourceActive |= stockSharpen.Active;
                Copy(stockSharpen.Amount, image.Sharpen);
                Copy(stockSharpen.Radius, image.SharpenRadius);
                if (!stockSharpen.Active) image.Sharpen.Value = 0f;
            }
            if (hasImageSource) image.Active = anyImageSourceActive;
        }
        if (addAo && profile.Get<AmbientOcclusion>() is { } stockAo)
        {
            Copy(stockAo.Intensity, ao.Intensity); Copy(stockAo.Radius, ao.Radius);
            Copy(stockAo.Slices, ao.Slices); Copy(stockAo.DirectionSamples, ao.DirectionSamples);
            Copy(stockAo.BlurRadius, ao.BlurRadius); Copy(stockAo.UseTemporal, ao.UseTemporal);
            Copy(stockAo.TemporalResponse, ao.TemporalResponse); Copy(stockAo.Resolution, ao.Resolution);
            ao.Active = stockAo.Active;
        }
        if (addSsr && profile.Get<ScreenSpaceReflections>() is { } stockSsr)
        {
            Copy(stockSsr.Enabled, ssr.Enabled); Copy(stockSsr.RayResolution, ssr.RayResolution);
            Copy(stockSsr.RayDistance, ssr.RayDistance); Copy(stockSsr.BRDFBias, ssr.BRDFBias);
            Copy(stockSsr.RayReuse, ssr.RayReuse); Copy(stockSsr.Normalization, ssr.Normalization);
            Copy(stockSsr.ReduceFireflies, ssr.ReduceFireflies); Copy(stockSsr.UseMipMap, ssr.UseMipMap);
            Copy(stockSsr.UseTemporal, ssr.UseTemporal); Copy(stockSsr.TemporalScale, ssr.TemporalScale);
            Copy(stockSsr.TemporalResponse, ssr.TemporalResponse); Copy(stockSsr.ReflectionVelocity, ssr.ReflectionVelocity);
            Copy(stockSsr.ScreenFadeSize, ssr.ScreenFadeSize);
            ssr.Active = stockSsr.Active;
        }
        if (addMotion && profile.Get<MotionBlur>() is { } stockMotion)
        {
            Copy(stockMotion.Intensity, motion.Intensity); Copy(stockMotion.Samples, motion.Samples);
            Copy(stockMotion.MaxBlurRadius, motion.MaxBlurRadius);
            motion.Active = stockMotion.Active;
        }
        if (addDof && profile.Get<DepthOfField>() is { } stockDof)
        {
            Copy(stockDof.Mode, dof.Mode); Copy(stockDof.FocusDistance, dof.FocusDistance);
            Copy(stockDof.FocusStrength, dof.FocusStrength); Copy(stockDof.MaxBlurRadius, dof.MaxBlurRadius);
            Copy(stockDof.Resolution, dof.Resolution); Copy(stockDof.Quality, dof.Quality);
            Copy(stockDof.FocusSpeed, dof.FocusSpeed);
            dof.Active = stockDof.Active;
        }
        if (addGodRays && profile.Get<GodRays>() is { } stockRays)
        {
            Copy(stockRays.Intensity, rays.Intensity); Copy(stockRays.Decay, rays.Decay);
            Copy(stockRays.Density, rays.Density); Copy(stockRays.Weight, rays.Weight);
            Copy(stockRays.Samples, rays.Samples); Copy(stockRays.Threshold, rays.Threshold);
            rays.Active = stockRays.Active;
        }
    }

    private static Float4 ToFloat4(Color color) => new(color.R, color.G, color.B, color.A);

    private static void Copy<T>(VolumeParameter<T> source, VolumeParameter<T> destination)
    {
        destination.Value = source.Value;
        destination.Override = source.Override;
    }
}

/// <summary>Infinity motion blur, routed to the existing Graphite temporal motion blur implementation.</summary>
[VolumeComponentMenu("Infinity/Camera/Motion Blur")]
public sealed class InfinityMotionBlur : VolumeComponent<G.MotionBlur>
{
    [Range(0f, 2f)] public VolumeParameter<float> Intensity = new(0f);
    [Range(1f, 32f)] public VolumeParameter<int> Samples = new(8);
    public VolumeParameter<float> MaxBlurRadius = new(40f);

    public override bool IsActive => InfinityPipelineMode.Enabled && Intensity.Value > 0f;
    protected override G.MotionBlur CreateFeature(G.Renderer renderer) => new(renderer);

    protected override void Apply(G.MotionBlur feature, Camera camera)
    {
        feature.Intensity = Intensity;
        feature.Samples = Samples;
        feature.MaxBlurRadius = MaxBlurRadius;
    }
}

/// <summary>Infinity depth of field routed to Graphite's bokeh DOF.</summary>
[VolumeComponentMenu("Infinity/Camera/Depth Of Field")]
public sealed class InfinityDepthOfField : VolumeComponent<G.DepthOfField>
{
    public VolumeParameter<DepthOfFieldMode> Mode = new(DepthOfFieldMode.Off);
    public VolumeParameter<float> FocusDistance = new(10f);
    public VolumeParameter<float> FocusStrength = new(2f);
    public VolumeParameter<float> MaxBlurRadius = new(2f);
    public VolumeParameter<DepthOfFieldResolution> Resolution = new(DepthOfFieldResolution.Half);
    public VolumeParameter<DepthOfFieldQuality> Quality = new(DepthOfFieldQuality.Medium);
    public VolumeParameter<float> FocusSpeed = new(4f);

    public override bool IsActive => InfinityPipelineMode.Enabled && Mode.Value != DepthOfFieldMode.Off;
    protected override G.DepthOfField CreateFeature(G.Renderer renderer) => new(renderer);

    protected override void Apply(G.DepthOfField feature, Camera camera)
    {
        feature.UseAutoFocus = Mode.Value == DepthOfFieldMode.Auto;
        feature.ManualFocusPoint = FocusDistance;
        feature.FocusStrength = FocusStrength;
        feature.MaxBlurRadius = MaxBlurRadius;
        feature.Resolution = (G.DepthOfFieldResolution)Resolution.Value;
        feature.Quality = (G.DepthOfFieldQuality)Quality.Value;
        feature.FocusSpeed = FocusSpeed;
    }
}

/// <summary>Infinity god rays routed to the Graphite light-shaft feature.</summary>
[VolumeComponentMenu("Infinity/Image/God Rays")]
public sealed class InfinityGodRays : VolumeComponent<G.GodRays>
{
    [Range(0f, 2f)] public VolumeParameter<float> Intensity = new(0f);
    [Range(0.9f, 1f)] public VolumeParameter<float> Decay = new(0.96f);
    [Range(0.1f, 2f)] public VolumeParameter<float> Density = new(0.5f);
    [Range(0f, 1f)] public VolumeParameter<float> Weight = new(0.6f);
    [Range(8f, 128f)] public VolumeParameter<int> Samples = new(64);
    [Range(0f, 1f)] public VolumeParameter<float> Threshold = new(0.8f);

    public override bool IsActive => InfinityPipelineMode.Enabled && Intensity.Value > 0f;
    protected override G.GodRays CreateFeature(G.Renderer renderer) => new(renderer);

    protected override void Apply(G.GodRays feature, Camera camera)
    {
        feature.Intensity = Intensity;
        feature.Decay = Decay;
        feature.Density = Density;
        feature.Weight = Weight;
        feature.Samples = Samples;
        feature.Threshold = Threshold;
    }
}
