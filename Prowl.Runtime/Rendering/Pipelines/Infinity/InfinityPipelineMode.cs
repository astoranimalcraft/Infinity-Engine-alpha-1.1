using System.Threading;

namespace Prowl.Runtime.Rendering.Pipelines.Infinity;

/// <summary>
/// Selects which VolumeComponent family Camera.BuildFeatures collects.
/// This changes post-processing without creating a second VolumeProfile or Vulkan device.
/// </summary>
public static class InfinityPipelineMode
{
    private static int _enabled;

    /// <summary>True selects Infinity Volume components; false keeps the stock Prowl Volume components.</summary>
    public static bool Enabled
    {
        get => Volatile.Read(ref _enabled) != 0;
        set => Volatile.Write(ref _enabled, value ? 1 : 0);
    }

    public static void SetEnabled(bool enabled) => Enabled = enabled;
}
