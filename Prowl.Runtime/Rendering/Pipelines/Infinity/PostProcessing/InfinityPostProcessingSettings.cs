// This file is part of the Infinity Engine fork.
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Infinity.Runtime.Rendering.Pipelines.Infinity.PostProcessing;

/// <summary>
/// Plain CPU-side settings consumed by Infinity's own Vulkan post-processing passes.
/// This type deliberately contains no Graphite RenderFeature or GPU resource references.
/// </summary>
public sealed class InfinityPostProcessingSettings
{
    // Camera input.
    public bool HdrEnabled = true;
    public float CameraEV100 = 14f;

    // Tonemapping / exposure.
    public int TonemappingMode;
    public bool AutoExposureEnabled;
    public float ExposureCompensation = 2.5f;
    public float AdaptSpeedUp = 3f;
    public float AdaptSpeedDown = 1.5f;
    public float MinEV100 = -6f;
    public float MaxEV100 = 20f;

    // Bloom.
    public bool BloomEnabled;
    public float BloomIntensity;
    public float BloomThreshold = 1.2f;
    public float BloomSoftKnee = 0.5f;
    public float BloomClamp = 65000f;
    public bool BloomAntiFlicker;
    public int BloomIterations = 6;
    public float BloomScatter = 0.7f;

    // Ground-truth ambient occlusion (GTAO).
    public bool GtaoEnabled;
    public float GtaoIntensity;
    public float GtaoRadius = 1f;
    public int GtaoSlices = 6;
    public int GtaoDirectionSamples = 32;
    public float GtaoBlurRadius = 1f;
    public bool GtaoTemporal = true;
    public float GtaoTemporalResponse = 0.9f;
    public int GtaoResolution;

    // Screen-space reflections.
    public bool SsrEnabled;
    public int SsrRayResolution;
    public float SsrRayDistance = 70f;
    public float SsrBrdfBias = 0.7f;
    public bool SsrRayReuse = true;
    public bool SsrNormalization = true;
    public bool SsrReduceFireflies = true;
    public bool SsrUseMipMap = true;
    public bool SsrTemporal = true;
    public float SsrTemporalScale = 2f;
    public float SsrTemporalResponse = 0.85f;
    public bool SsrReflectionVelocity = true;
    public float SsrScreenFadeSize = 0.25f;

    // Depth of field.
    public int DepthOfFieldMode;
    public float FocusDistance = 10f;
    public float FocusStrength = 2f;
    public float DepthOfFieldMaxBlurRadius = 2f;
    public int DepthOfFieldResolution;
    public int DepthOfFieldQuality;
    public float FocusSpeed = 4f;

    // Motion blur.
    public bool MotionBlurEnabled;
    public float MotionBlurIntensity;
    public int MotionBlurSamples = 8;
    public float MotionBlurMaxRadius = 40f;

    // Cinematic effects.
    public float VignetteIntensity;
    public float VignetteSmoothness = 0.2f;
    public float VignetteRoundness = 1f;
    public float ChromaticAberrationIntensity;
    public float ChromaticAberrationDistortion = 0.5f;
    public float FilmGrainIntensity;
    public float FilmGrainResponse = 0.5f;

    // Color grading scalar controls.
    public float ColorGradingPostExposure;
    public float ColorGradingContrast;
    public float ColorGradingSaturation;
    public float ColorGradingTemperature;

    // Sharpening.
    public float SharpenAmount;
    public float SharpenRadius = 1f;

    // God rays.
    public float GodRaysIntensity;
    public float GodRaysDecay = 0.96f;
    public float GodRaysDensity = 0.5f;
    public float GodRaysWeight = 0.6f;
    public int GodRaysSamples = 64;
    public float GodRaysThreshold = 0.8f;

    // Pixelation / edge detection.
    public float PixelSize = 1f;
    public float EdgeDetectionIntensity;
    public float EdgeDetectionBackgroundFade = 1f;
}
