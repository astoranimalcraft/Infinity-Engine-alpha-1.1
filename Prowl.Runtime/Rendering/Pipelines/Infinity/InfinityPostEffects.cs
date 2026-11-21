using Infinity.Runtime.Rendering;
using Infinity.Vector;

namespace Infinity.Runtime.Rendering.Pipelines.Infinity;

public enum InfinityColorBlindnessMode
{
    Protanopia,
    Deuteranopia,
    Tritanopia,
    Achromatopsia
}

public enum InfinityDitherPattern
{
    InterleavedGradient,
    Hash
}

/// <summary>
/// Radial lens distortion with optional per-channel separation at the edges, the lens breathing of a wide angle.
/// Runs on the displayed colour, so the distortion also moves the tonemapped highlights with the image.
/// </summary>
[VolumeComponentMenu("Infinity/Image/Lens Distortion")]
public sealed class InfinityLensDistortion : PostEffect
{
    /// <summary>Positive pinches the image in, negative bulges it out.</summary>
    [Range(-1f, 1f)] public VolumeParameter<float> Amount = new(0f);

    /// <summary>Zooms the distorted image, so the corners are not pulled inside the frame.</summary>
    [Range(0.5f, 2f)] public VolumeParameter<float> Scale = new(1f);

    /// <summary>Separation between the red and blue channels toward the edges, in screen widths.</summary>
    [Range(0f, 1f)] public VolumeParameter<float> ChromaticEdge = new(0f);

    /// <summary>The point the distortion grows from, in UV space.</summary>
    public VolumeParameter<Float2> Center = new(new Float2(0.5f, 0.5f));

    public override bool IsActive => InfinityPipelineMode.Enabled
        && (Amount.Value != 0f || Scale.Value != 1f || ChromaticEdge.Value > 0f);

    protected override PostEffectStage Stage => PostEffectStage.AfterTonemap;
    protected override int Order => 15;

    protected override string Shader => """
        uniform float _Amount;
        uniform float _Scale;
        uniform float _ChromaticEdge;
        uniform vec2 _Center;

        void main()
        {
            vec2 delta = vUV - _Center;
            float r2 = dot(delta, delta);
            vec2 baseUV = _Center + delta * (1.0 + _Amount * r2) * _Scale;

            // Channels pull apart toward the rim, as a real lens does away from the optical centre
            float separation = clamp(r2 * 2.0, 0.0, 1.0) * _ChromaticEdge * 0.0035;
            vec2 redUV = clamp(baseUV + delta * separation, vec2(0.0), vec2(1.0));
            vec2 greenUV = clamp(baseUV, vec2(0.0), vec2(1.0));
            vec2 blueUV = clamp(baseUV - delta * separation, vec2(0.0), vec2(1.0));

            vec3 color = vec3(
                Graphite_SampleSource(redUV).r,
                Graphite_SampleSource(greenUV).g,
                Graphite_SampleSource(blueUV).b);

            outColor = vec4(color, Graphite_SampleSource(greenUV).a);
        }
        """;
}

/// <summary>
/// Simulates a colour vision deficiency, and optionally shifts the error back into the visible range (daltonization).
/// Runs first in the display chain so later effects grade what the simulated viewer sees.
/// </summary>
[VolumeComponentMenu("Infinity/Image/Color Blindness")]
public sealed class InfinityColorBlindness : PostEffect
{
    public VolumeParameter<InfinityColorBlindnessMode> Mode = new(InfinityColorBlindnessMode.Deuteranopia);

    /// <summary>0 shows the original colour, 1 shows it as the simulated viewer sees it.</summary>
    [Range(0f, 1f)] public VolumeParameter<float> Amount = new(0f);

    /// <summary>How much of the lost contrast is pushed into channels the viewer still sees.</summary>
    [Range(0f, 1f)] public VolumeParameter<float> Daltonize = new(0f);

    public override bool IsActive => InfinityPipelineMode.Enabled
        && (Amount.Value > 0f || Daltonize.Value > 0f);

    protected override PostEffectStage Stage => PostEffectStage.AfterTonemap;
    protected override int Order => 5;

    protected override string Shader => """
        uniform int _Mode;
        uniform float _Amount;
        uniform float _Daltonize;

        vec3 InfinityToLms(vec3 c)
        {
            return vec3(
                dot(c, vec3(0.31399022, 0.63951294, 0.04649755)),
                dot(c, vec3(0.15537241, 0.75789446, 0.08673299)),
                dot(c, vec3(0.01775239, 0.10945274, 0.87279487)));
        }

        vec3 InfinityFromLms(vec3 c)
        {
            return vec3(
                dot(c, vec3(5.47221206, -4.64196010, -0.29749203)),
                dot(c, vec3(-1.12524190, 2.29317094, -0.16781100)),
                dot(c, vec3(0.02980165, -0.19318073, 1.16337908)));
        }

        vec3 InfinitySimulate(vec3 rgb)
        {
            if (_Mode == 3)
            {
                float grey = dot(rgb, vec3(0.2126, 0.7152, 0.0722));
                return vec3(grey);
            }

            vec3 lms = InfinityToLms(rgb);
            if (_Mode == 0)
                lms.x = 2.02344 * lms.y - 2.52581 * lms.z;
            else if (_Mode == 1)
                lms.y = 0.49421 * lms.x + 0.50579 * lms.z;
            else
                lms.z = -0.39591 * lms.x + 1.39591 * lms.y;

            return InfinityFromLms(lms);
        }

        void main()
        {
            vec4 source = Graphite_SampleSource(vUV);
            vec3 simulated = clamp(InfinitySimulate(source.rgb), 0.0, 1.0);

            // Daltonize: the lost information is the difference, pushed into green and blue
            vec3 error = source.rgb - simulated;
            vec3 corrected = clamp(source.rgb + vec3(0.0, 0.7 * error.r + error.g, 0.7 * error.r + error.b), 0.0, 1.0);

            vec3 result = mix(source.rgb, simulated, _Amount);
            result = mix(result, corrected, _Daltonize);
            outColor = vec4(clamp(result, 0.0, 1.0), source.a);
        }
        """;
}

/// <summary>
/// Draws an outline where the scene depth jumps, which separates objects from each other without a normal buffer.
/// The scene depth is reversed, 1 at the near plane, so the gradient is a step in that reversed value.
/// </summary>
[VolumeComponentMenu("Infinity/Image/Depth Outline")]
public sealed class InfinityDepthOutline : PostEffect
{
    /// <summary>How far apart the depth taps are, in pixels.</summary>
    [Range(0.5f, 8f)] public VolumeParameter<float> Thickness = new(1.5f);

    /// <summary>How large a reversed-depth step counts as an edge.</summary>
    [Range(0f, 0.25f)] public VolumeParameter<float> Threshold = new(0.01f);

    /// <summary>The outline colour, its alpha the strength.</summary>
    public VolumeParameter<Float4> Color = new(new Float4(0f, 0f, 0f, 0.75f));

    /// <summary>Fades the outline toward the camera, 0 keeps it even over the whole depth range.</summary>
    [Range(0f, 1f)] public VolumeParameter<float> Fade = new(0f);

    public override bool IsActive => InfinityPipelineMode.Enabled && Color.Value.W > 0f;

    protected override PostEffectStage Stage => PostEffectStage.AfterTonemap;
    protected override int Order => 80;
    protected override bool ReadsDepth => true;

    protected override string Shader => """
        uniform GRAPHITE_SCREEN_TEXTURE _CameraDepthTexture;
        uniform float _Thickness;
        uniform float _Threshold;
        uniform vec4 _Color;
        uniform float _Fade;

        float InfinityDepth(vec2 uv)
        {
            return Graphite_Sample(_CameraDepthTexture, clamp(uv, vec2(0.0), vec2(1.0))).r;
        }

        void main()
        {
            vec4 source = Graphite_SampleSource(vUV);
            vec2 texel = _Thickness / vec2(Graphite_Size(Graphite_Source, 0));
            float center = InfinityDepth(vUV);

            float gradient = 0.0;
            gradient += abs(InfinityDepth(vUV + vec2(texel.x, 0.0)) - center);
            gradient += abs(InfinityDepth(vUV - vec2(texel.x, 0.0)) - center);
            gradient += abs(InfinityDepth(vUV + vec2(0.0, texel.y)) - center);
            gradient += abs(InfinityDepth(vUV - vec2(0.0, texel.y)) - center);
            gradient += abs(InfinityDepth(vUV + texel) - center);
            gradient += abs(InfinityDepth(vUV - texel) - center);
            gradient += abs(InfinityDepth(vUV + vec2(texel.x, -texel.y)) - center);
            gradient += abs(InfinityDepth(vUV + vec2(-texel.x, texel.y)) - center);

            float edge = smoothstep(_Threshold, _Threshold * 2.0 + 1e-5, gradient * 0.125);
            float fade = mix(1.0, clamp(1.0 - center, 0.0, 1.0), _Fade);

            outColor = vec4(mix(source.rgb, _Color.rgb, edge * _Color.a * fade), source.a);
        }
        """;
}

/// <summary>
/// Anamorphic lens streaks: the bright parts of the HDR image are stretched sideways and tinted, as an anamorphic
/// lens flares. Runs in linear light before tonemapping so the streaks roll off with the rest of the highlights.
/// </summary>
[VolumeComponentMenu("Infinity/Image/Anamorphic Streaks")]
public sealed class InfinityAnamorphicStreaks : PostEffect
{
    [Range(0f, 4f)] public VolumeParameter<float> Intensity = new(0f);

    /// <summary>Luminance a pixel needs before it flares.</summary>
    [Range(0f, 8f)] public VolumeParameter<float> Threshold = new(1f);

    /// <summary>How far the streaks reach, in pixels per tap.</summary>
    [Range(0.25f, 8f)] public VolumeParameter<float> Stretch = new(2f);

    /// <summary>How quickly the streaks fall off along their length, 0 is a flat smear.</summary>
    [Range(0.05f, 1f)] public VolumeParameter<float> Falloff = new(0.35f);

    /// <summary>The streak colour, its alpha the strength.</summary>
    public VolumeParameter<Float4> Tint = new(new Float4(0.35f, 0.6f, 1f, 1f));

    public override bool IsActive => InfinityPipelineMode.Enabled
        && Intensity.Value > 0f && Tint.Value.W > 0f;

    protected override PostEffectStage Stage => PostEffectStage.BeforeTonemap;
    protected override int Order => 20;

    protected override string Shader => """
        uniform float _Intensity;
        uniform float _Threshold;
        uniform float _Stretch;
        uniform float _Falloff;
        uniform vec4 _Tint;

        vec3 InfinityFlare(vec3 c)
        {
            float luminance = dot(c, vec3(0.2126, 0.7152, 0.0722));
            return c * max(luminance - _Threshold, 0.0);
        }

        void main()
        {
            vec4 source = Graphite_SampleSource(vUV);
            vec2 texel = 1.0 / vec2(Graphite_Size(Graphite_Source, 0));

            vec3 streak = vec3(0.0);
            float total = 0.0;
            float reach = max(_Stretch, 0.25) * 16.0;

            for (int i = 1; i <= 16; i++)
            {
                float offset = float(i) * reach;
                float weight = exp(-float(i) * _Falloff * 0.25);
                streak += InfinityFlare(Graphite_SampleSource(vUV + vec2(texel.x * offset, 0.0)).rgb) * weight;
                streak += InfinityFlare(Graphite_SampleSource(vUV - vec2(texel.x * offset, 0.0)).rgb) * weight;
                total += 2.0 * weight;
            }

            streak /= max(total, 1e-5);
            outColor = vec4(source.rgb + streak * _Tint.rgb * _Tint.a * _Intensity, source.a);
        }
        """;
}

/// <summary>
/// Film halation: the bright parts of the frame bleed a soft, tinted glow into their surroundings, as light scattering
/// inside film does. The glow is red-orange by default because film halation is strongest in the red layer.
/// </summary>
[VolumeComponentMenu("Infinity/Image/Film Halation")]
public sealed class InfinityFilmHalation : PostEffect
{
    [Range(0f, 4f)] public VolumeParameter<float> Intensity = new(0f);

    /// <summary>Luminance a pixel needs before it halates.</summary>
    [Range(0f, 8f)] public VolumeParameter<float> Threshold = new(0.9f);

    /// <summary>The radius of the glow, in pixels.</summary>
    [Range(0f, 64f)] public VolumeParameter<float> Radius = new(12f);

    /// <summary>The halation colour, its alpha the strength.</summary>
    public VolumeParameter<Float4> Tint = new(new Float4(1f, 0.35f, 0.15f, 1f));

    public override bool IsActive => InfinityPipelineMode.Enabled
        && Intensity.Value > 0f && Radius.Value > 0f && Tint.Value.W > 0f;

    protected override PostEffectStage Stage => PostEffectStage.BeforeTonemap;
    protected override int Order => 25;

    protected override string Shader => """
        uniform float _Intensity;
        uniform float _Threshold;
        uniform float _Radius;
        uniform vec4 _Tint;

        void main()
        {
            vec4 source = Graphite_SampleSource(vUV);
            vec2 texel = _Radius / vec2(Graphite_Size(Graphite_Source, 0));

            vec3 halo = vec3(0.0);
            const int TAPS = 12;
            for (int i = 0; i < TAPS; i++)
            {
                // A spiral of taps, each ring further out, so twelve samples cover the disc evenly
                float angle = float(i) * 2.39996323;
                float distance = sqrt(float(i) + 0.5) / sqrt(float(TAPS));
                vec2 offset = vec2(cos(angle), sin(angle)) * texel * distance;
                vec3 tap = Graphite_SampleSource(vUV + offset).rgb;
                halo += max(tap - vec3(_Threshold), vec3(0.0));
            }

            halo /= float(TAPS);
            outColor = vec4(source.rgb + halo * _Tint.rgb * _Tint.a * _Intensity, source.a);
        }
        """;
}

/// <summary>
/// Quantizes the displayed colour to fewer levels and dithers the result, so an 8 bit target shows gradients
/// instead of bands. Leave Levels at 256 to pass the image through untouched.
/// </summary>
[VolumeComponentMenu("Infinity/Image/Dither")]
public sealed class InfinityDither : PostEffect
{
    /// <summary>The colour depth to quantize to, in levels per channel.</summary>
    [Range(2f, 256f)] public VolumeParameter<int> Levels = new(256);

    /// <summary>How much noise is added before quantizing, in steps. 1 is a full step.</summary>
    [Range(0f, 2f)] public VolumeParameter<float> Amount = new(1f);

    /// <summary>The size of the noise cells, in pixels.</summary>
    [Range(0.25f, 8f)] public VolumeParameter<float> Scale = new(1f);

    public VolumeParameter<InfinityDitherPattern> Pattern = new(InfinityDitherPattern.InterleavedGradient);

    public override bool IsActive => InfinityPipelineMode.Enabled && Levels.Value < 256;

    protected override PostEffectStage Stage => PostEffectStage.AfterTonemap;
    protected override int Order => 90;

    protected override string Shader => """
        uniform int _Levels;
        uniform float _Amount;
        uniform float _Scale;
        uniform int _Pattern;

        float InfinityInterleavedGradient(vec2 p)
        {
            return fract(52.9829189 * fract(dot(p, vec2(0.06711056, 0.00583715))));
        }

        float InfinityHash(vec2 p)
        {
            p = fract(p * vec2(443.897, 441.423));
            p += dot(p, p.yx + 19.19);
            return fract((p.x + p.y) * p.x);
        }

        void main()
        {
            vec4 source = Graphite_SampleSource(vUV);
            vec2 pixel = vUV * vec2(Graphite_Size(Graphite_Source, 0)) / max(_Scale, 0.25);
            float noise = _Pattern == 0 ? InfinityInterleavedGradient(pixel) : InfinityHash(floor(pixel));

            float levels = max(float(_Levels) - 1.0, 1.0);
            vec3 steps = source.rgb * levels + (noise - 0.5) * _Amount;
            outColor = vec4(clamp(floor(steps + 0.5) / levels, 0.0, 1.0), source.a);
        }
        """;
}

/// <summary>
/// Fades the displayed image to a flat colour, for transitions and cutscenes. Amount 0 keeps the image,
/// Amount 1 is the colour alone.
/// </summary>
[VolumeComponentMenu("Infinity/Image/Screen Fade")]
public sealed class InfinityScreenFade : PostEffect
{
    [Range(0f, 1f)] public VolumeParameter<float> Amount = new(0f);

    /// <summary>The colour the frame fades to.</summary>
    public VolumeParameter<Float4> Color = new(new Float4(0f, 0f, 0f, 1f));

    public override bool IsActive => InfinityPipelineMode.Enabled && Amount.Value > 0f;

    protected override PostEffectStage Stage => PostEffectStage.AfterTonemap;
    protected override int Order => 100;

    protected override string Shader => """
        uniform float _Amount;
        uniform vec4 _Color;

        void main()
        {
            vec4 source = Graphite_SampleSource(vUV);
            vec3 color = mix(source.rgb, _Color.rgb, clamp(_Amount, 0.0, 1.0));
            outColor = vec4(color, source.a);
        }
        """;
}
