
using System;

using Prowl.Runtime;
using Prowl.Runtime.Rendering;

namespace Prowl.Runtime.Rendering.Pipelines.Infinity;

public enum InfinityQuality
{
    Low,
    Medium,
    High,
    Ultra
}

public sealed class InfinityRenderSettings
{
    public InfinityQuality Quality = InfinityQuality.High;

    public bool HDR = true;
    public bool PBR = true;

    public bool Shadows = true;
    public bool SSAO = true;
    public bool Bloom = true;

    public bool SSR = true;
    public bool VolumetricFog = false;

    public int ShadowResolution = 2048;

    public float RenderScale = 1.0f;

    // Ambient Occlusion
    public float SSAOIntensity = 1.0f;
    public float SSAORadius = 1.0f;

    public float SSAOBlurRadius = 1.0f;

    public bool SSAOTemporal = true;
    public float SSAOTemporalResponse = 0.9f;

    private (EffectResolution Resolution, int Slices, int Samples)
        GetSSAOQuality()
    {
        return Quality switch
        {
            InfinityQuality.Low =>
                (EffectResolution.Quarter, 2, 8),

            InfinityQuality.Medium =>
                (EffectResolution.Quarter, 4, 16),

            InfinityQuality.High =>
                (EffectResolution.Quarter, 6, 32),

            InfinityQuality.Ultra =>
                (EffectResolution.Half, 8, 48),

            _ =>
                (EffectResolution.Quarter, 6, 32)
        };
    }

    public void ApplyAmbientOcclusion(Camera camera)
    {
        ArgumentNullException.ThrowIfNull(camera);

        if (SSAO)
            camera.PostProcessing = true;

        camera.Profile =
            VolumeProfile.CreateCameraDefault("Infinity Camera Profile");

        AmbientOcclusion ao =
            camera.Profile.Add<AmbientOcclusion>(overrideAll: true);

        var quality = GetSSAOQuality();

        ao.Active = SSAO;

        ao.Intensity.Set(
            SSAO ? Math.Clamp(SSAOIntensity, 0f, 4f) : 0f
        );

        ao.Radius.Set(Math.Max(0f, SSAORadius));

        ao.Slices.Set(quality.Slices);

        ao.DirectionSamples.Set(quality.Samples);

        ao.BlurRadius.Set(Math.Max(0f, SSAOBlurRadius));

        ao.UseTemporal.Set(SSAOTemporal);

        ao.TemporalResponse.Set(
            Math.Clamp(SSAOTemporalResponse, 0f, 1f)
        );

        ao.Resolution.Set(quality.Resolution);
    }
}
