// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Infinity.Vector;

namespace Infinity.Runtime.Rendering.Pipelines.Infinity;

/// <summary>
/// Comic-book dots: the screen is split into a rotated grid and each cell gets a dot whose radius follows the
/// luminance under it, so shadows swell into ink and highlights vanish.
/// </summary>
[VolumeComponentMenu("Infinity/Image/Halftone")]
public sealed class InfinityHalftone : PostEffect
{
    /// <summary>Cells across the screen height. Higher means finer dots.</summary>
    [Range(8f, 400f)] public VolumeParameter<float> Scale = new(90f);

    /// <summary>Rotation of the dot grid, in degrees, which hides the pattern's alignment with the image.</summary>
    [Range(-180f, 180f)] public VolumeParameter<float> Angle = new(45f);

    /// <summary>The colour the dots are drawn in.</summary>
    public VolumeParameter<Float4> Ink = new(new Float4(0.05f, 0.05f, 0.07f, 1f));

    /// <summary>Blends the result over the source, 1 replaces it.</summary>
    [Range(0f, 1f)] public VolumeParameter<float> Amount = new(1f);

    public override bool IsActive => InfinityPipelineMode.Enabled && Amount.Value > 0f;

    protected override PostEffectStage Stage => PostEffectStage.AfterTonemap;
    protected override int Order => 40;

    protected override string Shader => """
        uniform float _Scale;
        uniform float _Angle;
        uniform vec4 _Ink;
        uniform float _Amount;

        const vec3 LUMA = vec3(0.2126, 0.7152, 0.0722);

        void main()
        {
            vec4 source = Graphite_SampleSource(vUV);
            vec2 size = vec2(Graphite_Size(Graphite_Source, 0));
            float aspect = size.x / max(size.y, 1.0);

            float angle = radians(_Angle);
            mat2 rotate = mat2(cos(angle), -sin(angle), sin(angle), cos(angle));
            vec2 grid = rotate * (vUV * vec2(aspect, 1.0)) * _Scale;

            vec2 cell = fract(grid) - 0.5;
            float radius = 0.55 * sqrt(clamp(1.0 - dot(source.rgb, LUMA), 0.0, 1.0));
            float mask = 1.0 - smoothstep(radius - 0.1, radius + 0.1, length(cell));

            vec3 color = mix(vec3(1.0), _Ink.rgb, mask);
            outColor = vec4(mix(source.rgb, color, _Amount), source.a);
        }
        """;
}

/// <summary>
/// Mirrors the screen into wedge shaped slices around its centre, the tunnel look of a kaleidoscope. Zoom pulls the
/// sampled image in or out of the wedges.
/// </summary>
[VolumeComponentMenu("Infinity/Image/Kaleidoscope")]
public sealed class InfinityKaleidoscope : PostEffect
{
    /// <summary>Number of mirror wedges around the centre.</summary>
    [Range(2f, 32f)] public VolumeParameter<float> Segments = new(8f);

    /// <summary>Rotation of the whole pattern, in degrees.</summary>
    [Range(-180f, 180f)] public VolumeParameter<float> Angle = new(0f);

    /// <summary>Scales what the wedges sample, below 1 pushes it outward.</summary>
    [Range(0.1f, 4f)] public VolumeParameter<float> Zoom = new(1f);

    /// <summary>Blends the result over the source, 1 replaces it.</summary>
    [Range(0f, 1f)] public VolumeParameter<float> Amount = new(1f);

    public override bool IsActive => InfinityPipelineMode.Enabled && Amount.Value > 0f;

    protected override PostEffectStage Stage => PostEffectStage.AfterTonemap;
    protected override int Order => 41;

    protected override string Shader => """
        uniform float _Segments;
        uniform float _Angle;
        uniform float _Zoom;
        uniform float _Amount;

        void main()
        {
            vec4 source = Graphite_SampleSource(vUV);
            vec2 size = vec2(Graphite_Size(Graphite_Source, 0));
            float aspect = size.x / max(size.y, 1.0);

            vec2 centered = vUV - 0.5;
            centered.x *= aspect;

            float radius = length(centered);
            float wedge = 6.2831853 / max(_Segments, 2.0);
            float angle = atan(centered.y, centered.x) + radians(_Angle);

            angle = mod(angle, wedge);
            angle = abs(angle - wedge * 0.5);

            vec2 mirrored = vec2(cos(angle), sin(angle)) * radius;
            mirrored.x /= aspect;

            vec2 uv = clamp(mirrored / max(_Zoom, 0.01) + 0.5, 0.0, 1.0);
            vec4 sampled = Graphite_SampleSource(uv);

            outColor = vec4(mix(source.rgb, sampled.rgb, _Amount), source.a);
        }
        """;
}

/// <summary>
/// The curved glass of an old monitor: the image bows toward its edges, dark gaps run between scanlines, and the
/// corners fall off. The area outside the tube is black rather than smeared, which is what sells the shape.
/// </summary>
[VolumeComponentMenu("Infinity/Image/CRT Screen")]
public sealed class InfinityCrtScreen : PostEffect
{
    /// <summary>How far the glass bows. Zero leaves the image flat.</summary>
    [Range(0f, 1f)] public VolumeParameter<float> Curvature = new(0.25f);

    /// <summary>Depth of the dark lines between scanlines.</summary>
    [Range(0f, 1f)] public VolumeParameter<float> Scanlines = new(0.35f);

    /// <summary>Scanlines across the screen height.</summary>
    [Range(50f, 2000f)] public VolumeParameter<float> ScanlineCount = new(480f);

    /// <summary>Corner darkening of the tube.</summary>
    [Range(0f, 1f)] public VolumeParameter<float> Vignette = new(0.4f);

    /// <summary>Blends the result over the source, 1 replaces it.</summary>
    [Range(0f, 1f)] public VolumeParameter<float> Amount = new(1f);

    public override bool IsActive => InfinityPipelineMode.Enabled && Amount.Value > 0f;

    protected override PostEffectStage Stage => PostEffectStage.AfterTonemap;
    protected override int Order => 42;

    protected override string Shader => """
        uniform float _Curvature;
        uniform float _Scanlines;
        uniform float _ScanlineCount;
        uniform float _Vignette;
        uniform float _Amount;

        void main()
        {
            vec4 source = Graphite_SampleSource(vUV);

            vec2 curved = vUV * 2.0 - 1.0;
            vec2 offset = abs(curved.yx) / vec2(6.0, 4.0) * _Curvature;
            curved += curved * offset * offset;

            vec2 uv = curved * 0.5 + 0.5;
            float inside = step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);

            vec3 color = Graphite_SampleSource(clamp(uv, 0.0, 1.0)).rgb;
            color *= mix(1.0, 0.5 + 0.5 * sin(uv.y * _ScanlineCount * 6.2831853), _Scanlines);
            color *= 1.0 - smoothstep(0.15, 0.85, length(uv - 0.5)) * _Vignette;
            color *= inside;

            outColor = vec4(mix(source.rgb, color, _Amount), source.a);
        }
        """;
}

/// <summary>
/// Overlays the luminance back over the colour, which crushes the midtones and keeps the highlights metallic: the
/// washed out, contrasty look of bleach bypass processing.
/// </summary>
[VolumeComponentMenu("Infinity/Color/Bleach Bypass")]
public sealed class InfinityBleachBypass : PostEffect
{
    /// <summary>How much of the bypassed colour is used, before the blend below.</summary>
    [Range(0f, 1f)] public VolumeParameter<float> Strength = new(0.7f);

    /// <summary>Pulls saturation out afterwards, which the process does as a side effect.</summary>
    [Range(0f, 1f)] public VolumeParameter<float> Desaturate = new(0.15f);

    /// <summary>Blends the result over the source, 1 replaces it.</summary>
    [Range(0f, 1f)] public VolumeParameter<float> Amount = new(1f);

    public override bool IsActive => InfinityPipelineMode.Enabled && Amount.Value > 0f;

    protected override PostEffectStage Stage => PostEffectStage.AfterTonemap;
    protected override int Order => 43;

    protected override string Shader => """
        uniform float _Strength;
        uniform float _Desaturate;
        uniform float _Amount;

        const vec3 LUMA = vec3(0.2126, 0.7152, 0.0722);

        void main()
        {
            vec4 source = Graphite_SampleSource(vUV);

            float luma = dot(source.rgb, LUMA);
            vec3 overlay = mix(2.0 * source.rgb * luma,
                               1.0 - 2.0 * (1.0 - source.rgb) * (1.0 - luma),
                               step(0.5, source.rgb));

            vec3 color = mix(source.rgb, overlay, _Strength);
            color = mix(color, vec3(dot(color, LUMA)), _Desaturate);

            outColor = vec4(mix(source.rgb, color, _Amount), source.a);
        }
        """;
}
