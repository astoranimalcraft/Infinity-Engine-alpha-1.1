namespace Infinity.Runtime.Rendering.Pipelines.Legacy;

public sealed class LegacyRenderPipeline
{
    public string Name => "Infinity Legacy";

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
