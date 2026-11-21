using Prowl.Runtime.Rendering.Pipelines;

namespace Prowl.Runtime.Rendering.Settings;

public sealed class BuildGraphicsSettings
{
    public RenderPipelineType Pipeline = RenderPipelineType.Legacy;

    public bool HDR = true;

    public bool Shadows = true;

    public bool Bloom = true;

    public int ShadowResolution = 2048;

    public float RenderScale = 1.0f;
}
