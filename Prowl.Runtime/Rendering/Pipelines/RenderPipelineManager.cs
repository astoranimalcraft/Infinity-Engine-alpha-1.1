
using System;

using Infinity.Runtime.Rendering.Pipelines.Legacy;
using Infinity.Runtime.Rendering.Pipelines.Infinity;

namespace Infinity.Runtime.Rendering.Pipelines;

public static class RenderPipelineManager
{
    private static readonly LegacyRenderPipeline legacy = new();
    private static readonly InfinityRenderPipeline infinity = new();

    private static bool isInitialized;

    public static RenderPipelineType ActivePipelineType { get; private set; }
        = RenderPipelineType.Legacy;

    public static RenderPipelineSettings Settings { get; private set; }
        = new();

    public static LegacyRenderPipeline Legacy => legacy;
    public static InfinityRenderPipeline Infinity => infinity;

    public static string ActivePipelineName =>
        ActivePipelineType == RenderPipelineType.Legacy
            ? legacy.Name
            : infinity.Name;

    public static bool IsInitialized => isInitialized;

    public static void LoadSettings(RenderPipelineSettings settings)
    {
        Settings = settings ?? new RenderPipelineSettings();

        if (!Enum.IsDefined(Settings.Pipeline))
            Settings.Pipeline = RenderPipelineType.Legacy;

        if (!Settings.InfinityEnabled)
            Settings.Pipeline = RenderPipelineType.Legacy;

        CopyInfinitySettings();

        if (isInitialized)
            SetPipeline(Settings.Pipeline);
        else
        {
            ActivePipelineType = Settings.Pipeline;
            ApplyPipelineMode();
        }
    }

    private static void CopyInfinitySettings()
    {
        InfinityRenderSettings target = infinity.Settings;
        target.Quality = Settings.Quality;
        target.HDR = Settings.HDR;
        target.Shadows = Settings.Shadows;
        target.SSAO = Settings.SSAO;
        target.Bloom = Settings.Bloom;
        target.SSR = Settings.SSR;
        target.VolumetricFog = Settings.VolumetricFog;
        target.ShadowResolution = Math.Max(256, Settings.ShadowResolution);
        target.RenderScale = Math.Clamp(Settings.RenderScale, 0.1f, 2f);
    }

    // The modern look is a shader variant plus the Infinity Volume components. Keep the mode flag in step with the
    // selected pipeline so a camera renders the variant even when no custom pipeline is installed.
    private static void ApplyPipelineMode()
    {
        InfinityPipelineMode.SetEnabled(ActivePipelineType == RenderPipelineType.InfinityModern && Settings.InfinityEnabled);
    }

    public static void SetPipeline(RenderPipelineType type)
    {
        if (!Enum.IsDefined(type))
            throw new ArgumentOutOfRangeException(nameof(type));

        if (type == RenderPipelineType.InfinityModern &&
            !Settings.InfinityEnabled)
        {
            throw new InvalidOperationException(
                "Infinity Modern Pipeline is disabled in project settings.");
        }

        if (!isInitialized)
        {
            ActivePipelineType = type;
            Settings.Pipeline = type;
            ApplyPipelineMode();
            return;
        }

        if (type == ActivePipelineType)
            return;

        switch (type)
        {
            case RenderPipelineType.Legacy:
                infinity.Shutdown();
                legacy.Initialize();
                break;

            case RenderPipelineType.InfinityModern:
                legacy.Shutdown();
                infinity.Initialize();
                break;
        }

        ActivePipelineType = type;
        Settings.Pipeline = type;
        ApplyPipelineMode();
    }

    public static void Initialize()
    {
        if (isInitialized)
            return;

        isInitialized = true;

        switch (Settings.Pipeline)
        {
            case RenderPipelineType.Legacy:
                legacy.Initialize();
                break;

            case RenderPipelineType.InfinityModern:
                if (Settings.InfinityEnabled)
                    infinity.Initialize();
                else
                {
                    Settings.Pipeline = RenderPipelineType.Legacy;
                    ActivePipelineType = RenderPipelineType.Legacy;
                    legacy.Initialize();
                }
                break;

            default:
                Settings.Pipeline = RenderPipelineType.Legacy;
                ActivePipelineType = RenderPipelineType.Legacy;
                legacy.Initialize();
                break;
        }

        ApplyPipelineMode();
    }

    public static void RenderInfinity()
    {
        if (!isInitialized ||
            ActivePipelineType != RenderPipelineType.InfinityModern)
            return;

        infinity.Render();
    }

    public static void Shutdown()
    {
        if (!isInitialized)
            return;

        legacy.Shutdown();
        infinity.Shutdown();

        isInitialized = false;
        ActivePipelineType = RenderPipelineType.Legacy;
        ApplyPipelineMode();
    }
}
