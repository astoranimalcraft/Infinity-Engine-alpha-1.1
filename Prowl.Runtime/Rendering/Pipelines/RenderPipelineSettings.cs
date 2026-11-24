// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Infinity.Runtime.Rendering.Pipelines.Infinity;

namespace Infinity.Runtime.Rendering.Pipelines;

/// <summary>Project level rendering settings, saved with the player settings and applied before the first frame.</summary>
public sealed class RenderPipelineSettings
{
    public RenderPipelineType Pipeline = RenderPipelineType.Legacy;

    public bool InfinityEnabled = true;

    public InfinityQuality Quality = InfinityQuality.High;

    public bool HDR = true;
    public bool Shadows = true;
    public bool SSAO = true;
    public bool Bloom = true;
    public bool SSR = true;
    public bool VolumetricFog;

    public int ShadowResolution = 2048;
    public float RenderScale = 1.0f;

    public int version = 2;
}
