using Infinity.Runtime.Rendering.Pipelines;

namespace Infinity.Runtime.Rendering.Settings;

public sealed class BuildGraphicsSettings
{
    public RenderPipelineType Pipeline = RenderPipelineType.Legacy;

    public bool HDR = true;

    public bool Shadows = true;

    public bool Bloom = true;

    public int ShadowResolution = 2048;

    public float RenderScale = 1.0f;
}
