using System.Threading;

namespace Infinity.Runtime.Rendering.Pipelines.Infinity;

/// <summary>
/// Selects which VolumeComponent family the cameras collect.
/// This changes post-processing without creating a second VolumeProfile or Vulkan device.
///
/// The Infinity components read this flag in their own <see cref="VolumeComponent.IsActive"/>, so switching it does
/// not need to rebuild a profile: the Infinity family deactivates and the stock Prowl components take over.
/// </summary>
public static class InfinityPipelineMode
{
    // On by default: the Infinity components are the ones a project using this pipeline expects to see
    private static int _enabled = 1;

    /// <summary>True selects Infinity Volume components; false keeps the stock Prowl Volume components.</summary>
    public static bool Enabled
    {
        get => Volatile.Read(ref _enabled) != 0;
        set => Volatile.Write(ref _enabled, value ? 1 : 0);
    }

    public static void SetEnabled(bool enabled) => Enabled = enabled;
}
