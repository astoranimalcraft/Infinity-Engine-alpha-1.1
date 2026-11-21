namespace Prowl.Runtime.Rendering.Pipelines.Legacy;

public sealed class LegacyRenderPipeline
{
    public string Name => "Prowl Legacy";

    public bool IsInitialized { get; private set; }

    public void Initialize()
    {
        if (IsInitialized)
            return;

        IsInitialized = true;
    }

    public void Shutdown()
    {
        if (!IsInitialized)
            return;

        IsInitialized = false;
    }
}
