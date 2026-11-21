// This file is part of the Prowl Game Engine fork.
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime;
using Prowl.Runtime.Rendering;

namespace Prowl.Runtime.Rendering.Pipelines.Infinity.PostProcessing;

/// <summary>
/// Reads Prowl's blended VolumeStack and copies its CPU values into an Infinity-owned settings object.
/// IMPORTANT: This class does not call VolumeStack.CollectFeatures and does not create Graphite RenderFeatures.
/// Infinity's Vulkan renderer must upload these values to its own uniform/storage buffers.
/// </summary>
public static class InfinityVolumeAdapter
{
    public static InfinityPostProcessingSettings Capture(Camera camera)
    {
        ArgumentNullException.ThrowIfNull(camera);

        var result = new InfinityPostProcessingSettings
        {
            HdrEnabled = camera.HDR,
            CameraEV100 = camera.EV100
        };

        if (!camera.PostProcessing)
            return result;

        // Resolve blends camera Profile + scene Volumes by priority, weight and local-volume falloff.
        // Resolve() blends VolumeParameter values only. Graphite features are created separately by CollectFeatures().
        camera.ResolveVolumes();
        VolumeStack stack = camera.Volumes;

        if (stack.Get<Tonemapping>() is { } tonemapping)
            result.TonemappingMode = (int)tonemapping.Mode.Value;

        if (stack.Get<AutoExposure>() is { } autoExposure)
        {
            result.AutoExposureEnabled = autoExposure.Enabled.Value;
            result.ExposureCompensation = autoExposure.ExposureCompensation.Value;
            result.AdaptSpeedUp = autoExposure.AdaptSpeedUp.Value;
            result.AdaptSpeedDown = autoExposure.AdaptSpeedDown.Value;
            result.MinEV100 = autoExposure.MinEV100.Value;
            result.MaxEV100 = autoExposure.MaxEV100.Value;
        }

        if (stack.Get<Bloom>() is { } bloom)
        {
            result.BloomEnabled = bloom.IsActive;
            result.BloomIntensity = bloom.Intensity.Value;
            result.BloomThreshold = bloom.Threshold.Value;
            result.BloomSoftKnee = bloom.SoftKnee.Value;
            result.BloomClamp = bloom.Clamp.Value;
            result.BloomAntiFlicker = bloom.AntiFlicker.Value;
            result.BloomIterations = bloom.Iterations.Value;
            result.BloomScatter = bloom.Scatter.Value;
        }

        if (stack.Get<AmbientOcclusion>() is { } gtao)
        {
            result.GtaoEnabled = gtao.IsActive;
            result.GtaoIntensity = gtao.Intensity.Value;
            result.GtaoRadius = gtao.Radius.Value;
            result.GtaoSlices = gtao.Slices.Value;
            result.GtaoDirectionSamples = gtao.DirectionSamples.Value;
            result.GtaoBlurRadius = gtao.BlurRadius.Value;
            result.GtaoTemporal = gtao.UseTemporal.Value;
            result.GtaoTemporalResponse = gtao.TemporalResponse.Value;
            result.GtaoResolution = (int)gtao.Resolution.Value;
        }

        if (stack.Get<ScreenSpaceReflections>() is { } ssr)
        {
            result.SsrEnabled = ssr.Enabled.Value;
            result.SsrRayResolution = (int)ssr.RayResolution.Value;
            result.SsrRayDistance = ssr.RayDistance.Value;
            result.SsrBrdfBias = ssr.BRDFBias.Value;
            result.SsrRayReuse = ssr.RayReuse.Value;
            result.SsrNormalization = ssr.Normalization.Value;
            result.SsrReduceFireflies = ssr.ReduceFireflies.Value;
            result.SsrUseMipMap = ssr.UseMipMap.Value;
            result.SsrTemporal = ssr.UseTemporal.Value;
            result.SsrTemporalScale = ssr.TemporalScale.Value;
            result.SsrTemporalResponse = ssr.TemporalResponse.Value;
            result.SsrReflectionVelocity = ssr.ReflectionVelocity.Value;
            result.SsrScreenFadeSize = ssr.ScreenFadeSize.Value;
        }

        if (stack.Get<DepthOfField>() is { } dof)
        {
            result.DepthOfFieldMode = (int)dof.Mode.Value;
            result.FocusDistance = dof.FocusDistance.Value;
            result.FocusStrength = dof.FocusStrength.Value;
            result.DepthOfFieldMaxBlurRadius = dof.MaxBlurRadius.Value;
            result.DepthOfFieldResolution = (int)dof.Resolution.Value;
            result.DepthOfFieldQuality = (int)dof.Quality.Value;
            result.FocusSpeed = dof.FocusSpeed.Value;
        }

        if (stack.Get<MotionBlur>() is { } motionBlur)
        {
            result.MotionBlurEnabled = motionBlur.IsActive;
            result.MotionBlurIntensity = motionBlur.Intensity.Value;
            result.MotionBlurSamples = motionBlur.Samples.Value;
            result.MotionBlurMaxRadius = motionBlur.MaxBlurRadius.Value;
        }

        if (stack.Get<Vignette>() is { } vignette)
        {
            result.VignetteIntensity = vignette.Intensity.Value;
            result.VignetteSmoothness = vignette.Smoothness.Value;
            result.VignetteRoundness = vignette.Roundness.Value;
        }

        if (stack.Get<ChromaticAberration>() is { } chromatic)
        {
            result.ChromaticAberrationIntensity = chromatic.Intensity.Value;
            result.ChromaticAberrationDistortion = chromatic.Distortion.Value;
        }

        if (stack.Get<FilmGrain>() is { } filmGrain)
        {
            result.FilmGrainIntensity = filmGrain.Intensity.Value;
            result.FilmGrainResponse = filmGrain.Response.Value;
        }

        if (stack.Get<ColorGrading>() is { } grading)
        {
            result.ColorGradingPostExposure = grading.PostExposure.Value;
            result.ColorGradingContrast = grading.Contrast.Value;
            result.ColorGradingSaturation = grading.Saturation.Value;
            result.ColorGradingTemperature = grading.Temperature.Value;
        }

        if (stack.Get<Sharpen>() is { } sharpen)
        {
            result.SharpenAmount = sharpen.Amount.Value;
            result.SharpenRadius = sharpen.Radius.Value;
        }

        if (stack.Get<GodRays>() is { } godRays)
        {
            result.GodRaysIntensity = godRays.Intensity.Value;
            result.GodRaysDecay = godRays.Decay.Value;
            result.GodRaysDensity = godRays.Density.Value;
            result.GodRaysWeight = godRays.Weight.Value;
            result.GodRaysSamples = godRays.Samples.Value;
            result.GodRaysThreshold = godRays.Threshold.Value;
        }

        if (stack.Get<Pixelation>() is { } pixelation)
            result.PixelSize = pixelation.PixelSize.Value;

        if (stack.Get<EdgeDetection>() is { } edgeDetection)
        {
            result.EdgeDetectionIntensity = edgeDetection.Intensity.Value;
            result.EdgeDetectionBackgroundFade = edgeDetection.BackgroundFade.Value;
        }

        return result;
    }
}
