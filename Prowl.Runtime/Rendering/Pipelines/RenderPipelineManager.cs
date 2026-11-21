
using System;

using Prowl.Runtime.Rendering.Pipelines.Legacy;
using Prowl.Runtime.Rendering.Pipelines.Infinity;

namespace Prowl.Runtime.Rendering.Pipelines;

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

        if (isInitialized)
            SetPipeline(Settings.Pipeline);
        else
            ActivePipelineType = Settings.Pipeline;
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
    }
}
