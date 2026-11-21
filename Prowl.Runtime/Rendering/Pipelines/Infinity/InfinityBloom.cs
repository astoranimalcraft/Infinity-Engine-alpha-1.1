
using System.ComponentModel.DataAnnotations;

using Prowl.Runtime.Rendering;

using G = Prowl.Graphite.Rendering;

namespace Prowl.Runtime.Rendering.Pipelines.Infinity;

/// <summary>
/// Infinity Bloom Volume Component.
///
/// Controls the independent InfinityBloomFeature
/// through the existing Prowl Volume Profile.
/// </summary>
[VolumeComponentMenu("Infinity/Image/Bloom")]
public sealed class InfinityBloom
    : VolumeComponent<G.InfinityBloomFeature>,
      IInfinityVolumeComponent
{
    [Range(0f, 10f)]
    public VolumeParameter<float> Intensity = new(1.0f);

    [Range(0f, 10f)]
    public VolumeParameter<float> Threshold = new(1.15f);

    [Range(0f, 1f)]
    public VolumeParameter<float> SoftKnee = new(0.5f);

    [Range(0f, 65000f)]
    public VolumeParameter<float> Clamp = new(65000.0f);

    public VolumeParameter<bool> AntiFlicker = new(true);

    [Range(1, 8)]
    public VolumeParameter<int> Iterations = new(5);

    [Range(0f, 1f)]
    public VolumeParameter<float> Scatter = new(0.72f);

    public override bool IsActive =>
        Active && Intensity.Value > 0.0f;

    protected override G.InfinityBloomFeature CreateFeature(
        G.Renderer renderer)
    {
        return new G.InfinityBloomFeature(renderer);
    }

    protected override void Apply(
        G.InfinityBloomFeature feature,
        Camera camera)
    {
        feature.Intensity = Intensity.Value;
        feature.Threshold = Threshold.Value;
        feature.SoftKnee = SoftKnee.Value;
        feature.Clamp = Clamp.Value;
        feature.AntiFlicker = AntiFlicker.Value;
        feature.Iterations = Iterations.Value;
        feature.Scatter = Scatter.Value;
    }
}
