// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Infinity.Vector;

namespace Infinity.Runtime.Rendering.Pipelines.Infinity;

/// <summary>
/// Quantises the displayed colour to a fixed number of levels per channel, the banded look of an old palette.
/// Dithering trades the clean bands for noise, which keeps gradients from showing hard steps.
/// </summary>
[VolumeComponentMenu("Infinity/Image/Posterize")]
public sealed class InfinityPosterize : PostEffect
{
    /// <summary>Levels per channel, 2 is black and white plus one step.</summary>
    [Range(2, 64)] public VolumeParameter<int> Steps = new(8);

    /// <summary>Noise added before quantising, in level widths.</summary>
    [Range(0f, 1f)] public VolumeParameter<float> Dither = new(0.5f);

    /// <summary>Blends the result over the source, 1 replaces it.</summary>
    [Range(0f, 1f)] public VolumeParameter<float> Amount = new(1f);

    public override bool IsActive => InfinityPipelineMode.Enabled && Amount.Value > 0f && Steps.Value > 1;

    protected override PostEffectStage Stage => PostEffectStage.AfterTonemap;
    protected override int Order => 30;

    protected override string Shader => """
        uniform int _Steps;
        uniform float _Dither;
        uniform float _Amount;

        void main()
        {
            vec4 source = Graphite_SampleSource(vUV);
            float steps = max(float(_Steps) - 1.0, 1.0);

            vec2 pixel = floor(vUV * vec2(Graphite_Size(Graphite_Source, 0)));
            float noise = fract(sin(dot(pixel, vec2(12.9898, 78.233))) * 43758.5453) - 0.5;
            vec3 color = max(source.rgb + noise * _Dither / steps, vec3(0.0));

            color = floor(color * steps + 0.5) / steps;
            outColor = vec4(mix(source.rgb, color, _Amount), source.a);
        }
        """;
}

/// <summary>
/// Maps the displayed luminance onto a two colour ramp, the tinted print look. Luminance preservation keeps the
/// original brightness, so only the hue changes.
/// </summary>
[VolumeComponentMenu("Infinity/Image/Duotone")]
public sealed class InfinityDuotone : PostEffect
{
    /// <summary>The colour shadows take.</summary>
    public VolumeParameter<Float4> ShadowColor = new(new Float4(0.05f, 0.03f, 0.16f, 1f));

    /// <summary>The colour highlights take.</summary>
    public VolumeParameter<Float4> HighlightColor = new(new Float4(1f, 0.72f, 0.32f, 1f));

    /// <summary>Keeps the source luminance instead of the ramp's own, so a darker pixel stays darker.</summary>
    public VolumeParameter<bool> PreserveLuminance = new(true);

    /// <summary>Blends the ramp over the source, 1 replaces it.</summary>
    [Range(0f, 1f)] public VolumeParameter<float> Amount = new(1f);

    public override bool IsActive => InfinityPipelineMode.Enabled && Amount.Value > 0f;

    protected override PostEffectStage Stage => PostEffectStage.AfterTonemap;
    protected override int Order => 32;

    protected override string Shader => """
        uniform vec4 _ShadowColor;
        uniform vec4 _HighlightColor;
        uniform float _PreserveLuminance;
        uniform float _Amount;

        const vec3 LUMA = vec3(0.2126, 0.7152, 0.0722);

        void main()
        {
            vec4 source = Graphite_SampleSource(vUV);
            float luma = dot(source.rgb, LUMA);
            vec3 ramp = mix(_ShadowColor.rgb, _HighlightColor.rgb, smoothstep(0.0, 1.0, luma));

            if (_PreserveLuminance > 0.5)
                ramp *= luma / max(dot(ramp, LUMA), 1e-4);

            outColor = vec4(mix(source.rgb, clamp(ramp, 0.0, 1.0), _Amount), source.a);
        }
        """;
}

/// <summary>
/// Green phosphor night vision: luminance amplified through a green tint with sensor noise on top, as a tube
/// camera sees a dark scene.
/// </summary>
[VolumeComponentMenu("Infinity/Image/Night Vision")]
public sealed class InfinityNightVision : PostEffect
{
    /// <summary>Blends the tube over the source, 1 replaces it.</summary>
    [Range(0f, 1f)] public VolumeParameter<float> Amount = new(1f);

    /// <summary>Multiplies the incoming luminance before the phosphor response.</summary>
    [Range(0.5f, 8f)] public VolumeParameter<float> Gain = new(3f);

    /// <summary>How much sensor noise is added.</summary>
    [Range(0f, 1f)] public VolumeParameter<float> Noise = new(0.25f);

    /// <summary>Adds the horizontal scan lines of a tube.</summary>
    [Range(0f, 1f)] public VolumeParameter<float> Scanlines = new(0.25f);

    public override bool IsActive => InfinityPipelineMode.Enabled && Amount.Value > 0f;

    protected override PostEffectStage Stage => PostEffectStage.AfterTonemap;
    protected override int Order => 34;

    protected override string Shader => """
        uniform float _Amount;
        uniform float _Gain;
        uniform float _Noise;
        uniform float _Scanlines;
        uniform float _Time;

        void main()
        {
            vec4 source = Graphite_SampleSource(vUV);
            float luma = dot(source.rgb, vec3(0.2126, 0.7152, 0.0722)) * _Gain;

            vec2 pixel = vUV * vec2(Graphite_Size(Graphite_Source, 0));
            float grain = fract(sin(dot(pixel + _Time * 61.7, vec2(12.9898, 78.233))) * 43758.5453) - 0.5;
            luma += grain * _Noise;

            float lines = 0.5 + 0.5 * sin(pixel.y * 3.14159265);
            luma *= mix(1.0, lines, _Scanlines);

            // The phosphor is green, with a little blue in it as a real tube has
            vec3 tube = vec3(luma * 0.15, luma, luma * 0.35);
            outColor = vec4(mix(source.rgb, clamp(tube, 0.0, 1.0), _Amount), source.a);
        }
        """;
}

/// <summary>
/// The scan lines and aperture mask of a CRT, optionally scrolling. Runs on the displayed colour so the lines stay
/// crisp instead of being blurred by a later stage.
/// </summary>
[VolumeComponentMenu("Infinity/Image/Scanlines")]
public sealed class InfinityScanlines : PostEffect
{
    /// <summary>Depth of the dark lines, 0 leaves the image alone.</summary>
    [Range(0f, 1f)] public VolumeParameter<float> Amount = new(0.35f);

    /// <summary>Lines per screen height at 1080p.</summary>
    [Range(0.5f, 8f)] public VolumeParameter<float> Density = new(2f);

    /// <summary>Lines scrolled per second, 0 holds them still.</summary>
    [Range(-8f, 8f)] public VolumeParameter<float> Speed = new(0f);

    /// <summary>Strength of the red, green and blue stripes across each pixel row.</summary>
    [Range(0f, 1f)] public VolumeParameter<float> ApertureMask = new(0.3f);

    public override bool IsActive => InfinityPipelineMode.Enabled && (Amount.Value > 0f || ApertureMask.Value > 0f);

    protected override PostEffectStage Stage => PostEffectStage.AfterTonemap;
    protected override int Order => 36;

    protected override string Shader => """
        uniform float _Amount;
        uniform float _Density;
        uniform float _Speed;
        uniform float _ApertureMask;
        uniform float _Time;

        void main()
        {
            vec4 source = Graphite_SampleSource(vUV);
            vec2 size = vec2(Graphite_Size(Graphite_Source, 0));

            // Density is measured against 1080 lines so the look does not change with the window size
            float lines = size.y / 1080.0 * _Density * 540.0;
            float wave = 0.5 + 0.5 * sin((vUV.y * lines + _Time * _Speed) * 6.2831853);
            float shade = mix(1.0, wave, _Amount);

            float stripe = mod(floor(vUV.x * size.x), 3.0);
            vec3 mask = stripe < 1.0 ? vec3(1.0, 0.65, 0.65)
                      : stripe < 2.0 ? vec3(0.65, 1.0, 0.65)
                                     : vec3(0.65, 0.65, 1.0);
            mask = mix(vec3(1.0), mask, _ApertureMask);

            outColor = vec4(source.rgb * shade * mask, source.a);
        }
        """;
}

/// <summary>
/// Snaps the image to a grid of blocks, the look of a low resolution display. The block colour is the source at the
/// centre of the block, so the image keeps its overall brightness.
/// </summary>
[VolumeComponentMenu("Infinity/Image/Pixelate")]
public sealed class InfinityPixelate : PostEffect
{
    /// <summary>Block size in pixels at the current resolution.</summary>
    [Range(1f, 64f)] public VolumeParameter<float> PixelSize = new(4f);

    /// <summary>Blends the blocks over the source, 1 replaces it.</summary>
    [Range(0f, 1f)] public VolumeParameter<float> Amount = new(1f);

    public override bool IsActive => InfinityPipelineMode.Enabled && Amount.Value > 0f && PixelSize.Value > 1f;

    protected override PostEffectStage Stage => PostEffectStage.AfterTonemap;
    protected override int Order => 38;

    protected override string Shader => """
        uniform float _PixelSize;
        uniform float _Amount;

        void main()
        {
            vec4 source = Graphite_SampleSource(vUV);
            vec2 size = vec2(Graphite_Size(Graphite_Source, 0));
            vec2 blocks = max(floor(size / max(_PixelSize, 1.0)), vec2(1.0));
            vec2 blockUV = (floor(vUV * blocks) + 0.5) / blocks;

            outColor = vec4(mix(source.rgb, Graphite_SampleSource(blockUV).rgb, _Amount), source.a);
        }
        """;
}

/// <summary>
/// Zoom blur: samples are taken along the line from the centre to the pixel, so motion seems to rush outward from
/// it. With the centre on the screen middle it reads as speed, off centre as a whip pan.
/// </summary>
[VolumeComponentMenu("Infinity/Image/Radial Blur")]
public sealed class InfinityRadialBlur : PostEffect
{
    /// <summary>How far the samples spread toward the centre, 0 leaves the image alone.</summary>
    [Range(0f, 0.5f)] public VolumeParameter<float> Amount = new(0.08f);

    /// <summary>Taps along the line, more is smoother and costs more.</summary>
    [Range(2, 32)] public VolumeParameter<int> Samples = new(8);

    /// <summary>The point the blur grows from, in UV space.</summary>
    public VolumeParameter<Float2> Center = new(new Float2(0.5f, 0.5f));

    public override bool IsActive => InfinityPipelineMode.Enabled && Amount.Value > 0f;

    protected override PostEffectStage Stage => PostEffectStage.AfterTonemap;
    protected override int Order => 42;

    protected override string Shader => """
        uniform float _Amount;
        uniform int _Samples;
        uniform vec2 _Center;

        void main()
        {
            vec4 source = Graphite_SampleSource(vUV);
            int samples = clamp(_Samples, 2, 32);
            float count = float(samples);

            vec3 sum = vec3(0.0);
            for (int i = 0; i < 32; i++)
            {
                if (i >= samples) break;
                float t = float(i) / (count - 1.0);
                vec2 uv = _Center + (vUV - _Center) * (1.0 - _Amount * t);
                sum += Graphite_SampleSource(clamp(uv, vec2(0.0), vec2(1.0))).rgb;
            }

            outColor = vec4(sum / count, source.a);
        }
        """;
}

/// <summary>
/// Wavers the image with a moving noise field, the shimmer of hot air over a fire or a road. Amount is in screen
/// widths, so the same setting reads the same at any resolution.
/// </summary>
[VolumeComponentMenu("Infinity/Image/Heat Haze")]
public sealed class InfinityHeatHaze : PostEffect
{
    /// <summary>How far the image wavers, in thousandths of the screen.</summary>
    [Range(0f, 20f)] public VolumeParameter<float> Amount = new(4f);

    /// <summary>Size of the wobbles, higher is a finer pattern.</summary>
    [Range(0.5f, 16f)] public VolumeParameter<float> Scale = new(4f);

    /// <summary>How fast the pattern moves.</summary>
    [Range(-4f, 4f)] public VolumeParameter<float> Speed = new(1f);

    public override bool IsActive => InfinityPipelineMode.Enabled && Amount.Value > 0f;

    protected override PostEffectStage Stage => PostEffectStage.AfterTonemap;
    protected override int Order => 44;

    protected override string Shader => """
        uniform float _Amount;
        uniform float _Scale;
        uniform float _Speed;
        uniform float _Time;

        void main()
        {
            float t = _Time * _Speed;
            vec2 p = vUV * _Scale;
            float wobbleX = sin(p.y * 12.9898 + t * 1.7) + 0.5 * sin(p.y * 31.7 - t * 2.3);
            float wobbleY = cos(p.x * 11.1357 + t * 1.3) + 0.5 * cos(p.x * 27.1 + t * 1.9);

            vec2 offset = vec2(wobbleX, wobbleY) * _Amount * 0.001;
            outColor = Graphite_SampleSource(clamp(vUV + offset, vec2(0.0), vec2(1.0)));
        }
        """;
}

/// <summary>
/// Sobel edges of the displayed luminance, drawn in one colour. Unlike the depth outline this finds the edges inside
/// a surface too, so it works as a comic or blueprint look.
/// </summary>
[VolumeComponentMenu("Infinity/Image/Outline")]
public sealed class InfinityOutline : PostEffect
{
    /// <summary>How far apart the taps are, in pixels.</summary>
    [Range(0.5f, 8f)] public VolumeParameter<float> Thickness = new(1f);

    /// <summary>Luminance change that counts as an edge.</summary>
    [Range(0f, 1f)] public VolumeParameter<float> Threshold = new(0.15f);

    /// <summary>The edge colour, its alpha the strength.</summary>
    public VolumeParameter<Float4> Color = new(new Float4(0f, 0f, 0f, 1f));

    public override bool IsActive => InfinityPipelineMode.Enabled && Color.Value.W > 0f;

    protected override PostEffectStage Stage => PostEffectStage.AfterTonemap;
    protected override int Order => 82;

    protected override string Shader => """
        uniform float _Thickness;
        uniform float _Threshold;
        uniform vec4 _Color;

        const vec3 LUMA = vec3(0.2126, 0.7152, 0.0722);

        float InfinityLuma(vec2 uv)
        {
            return dot(Graphite_SampleSource(clamp(uv, vec2(0.0), vec2(1.0))).rgb, LUMA);
        }

        void main()
        {
            vec4 source = Graphite_SampleSource(vUV);
            vec2 texel = _Thickness / vec2(Graphite_Size(Graphite_Source, 0));

            float topLeft = InfinityLuma(vUV + vec2(-texel.x, -texel.y));
            float top = InfinityLuma(vUV + vec2(0.0, -texel.y));
            float topRight = InfinityLuma(vUV + vec2(texel.x, -texel.y));
            float left = InfinityLuma(vUV + vec2(-texel.x, 0.0));
            float right = InfinityLuma(vUV + vec2(texel.x, 0.0));
            float bottomLeft = InfinityLuma(vUV + vec2(-texel.x, texel.y));
            float bottom = InfinityLuma(vUV + vec2(0.0, texel.y));
            float bottomRight = InfinityLuma(vUV + vec2(texel.x, texel.y));

            float gradientX = -topLeft - 2.0 * left - bottomLeft + topRight + 2.0 * right + bottomRight;
            float gradientY = -topLeft - 2.0 * top - topRight + bottomLeft + 2.0 * bottom + bottomRight;
            float edge = smoothstep(_Threshold, _Threshold * 2.0 + 1e-5, length(vec2(gradientX, gradientY)) * 0.25);

            outColor = vec4(mix(source.rgb, _Color.rgb, edge * _Color.a), source.a);
        }
        """;
}

/// <summary>
/// Blurs by depth away from a focus band, the shallow focus of a tilt shift lens or a miniature photo. Runs in
/// linear light before tonemapping, so the blurred highlights still roll off with the rest of the image.
/// </summary>
[VolumeComponentMenu("Infinity/Image/Tilt Shift")]
public sealed class InfinityTiltShift : PostEffect
{
    /// <summary>The depth that stays sharp, as reversed depth: 0 far, 1 at the near plane.</summary>
    [Range(0f, 1f)] public VolumeParameter<float> Focus = new(0.5f);

    /// <summary>Half width of the sharp band, in reversed depth.</summary>
    [Range(0f, 0.5f)] public VolumeParameter<float> Range = new(0.05f);

    /// <summary>Blur radius at the far edge of the band, in pixels.</summary>
    [Range(0f, 32f)] public VolumeParameter<float> Amount = new(6f);

    /// <summary>Taps in the blur, more is smoother and costs more.</summary>
    [Range(1, 24)] public VolumeParameter<int> Samples = new(8);

    public override bool IsActive => InfinityPipelineMode.Enabled && Amount.Value > 0f;

    protected override PostEffectStage Stage => PostEffectStage.BeforeTonemap;
    protected override int Order => 12;
    protected override bool ReadsDepth => true;

    protected override string Shader => """
        uniform GRAPHITE_SCREEN_TEXTURE _CameraDepthTexture;
        uniform float _Focus;
        uniform float _Range;
        uniform float _Amount;
        uniform int _Samples;

        void main()
        {
            vec4 source = Graphite_SampleSource(vUV);
            float depth = Graphite_Sample(_CameraDepthTexture, vUV).r;
            float distance = abs(depth - _Focus);
            float blur = smoothstep(_Range, max(_Range * 2.0, _Range + 1e-3), distance) * _Amount;

            if (blur < 0.01)
            {
                outColor = source;
                return;
            }

            int samples = clamp(_Samples, 1, 24);
            float count = float(samples);
            vec2 texel = blur / vec2(Graphite_Size(Graphite_Source, 0));

            vec3 sum = vec3(0.0);
            for (int i = 0; i < 24; i++)
            {
                if (i >= samples) break;

                // A golden angle spiral spreads the taps evenly instead of leaving the boxy look of a grid
                float angle = float(i) * 2.39996323;
                float radius = sqrt((float(i) + 0.5) / count);
                vec2 offset = vec2(cos(angle), sin(angle)) * radius * texel * 2.0;
                sum += Graphite_SampleSource(clamp(vUV + offset, vec2(0.0), vec2(1.0))).rgb;
            }

            outColor = vec4(sum / count, source.a);
        }
        """;
}
