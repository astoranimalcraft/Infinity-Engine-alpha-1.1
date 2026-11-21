
using System;

namespace Prowl.Runtime.Rendering.Pipelines.Infinity;

public sealed class InfinityRenderPipeline : IDisposable
{
    public string Name => "Infinity Modern Pipeline";

    public bool IsInitialized { get; private set; }

    public bool IsDisposed { get; private set; }

    public InfinityRenderSettings Settings { get; } = new();

    public InfinityShaderLibrary Shaders { get; } = new();

    public void Initialize()
    {
        if (IsDisposed)
            throw new ObjectDisposedException(
                nameof(InfinityRenderPipeline));

        if (IsInitialized)
            return;

        try
        {
            InitializeShaders();

            InitializeRenderingSystems();

            IsInitialized = true;
        }
        catch
        {
            Shutdown();
            throw;
        }
    }

    private void InitializeShaders()
    {
        // Load throws when the shader directory is missing or holds no SPIR-V, so a broken install fails here
        // instead of at the first draw
        Shaders.Load();
    }

    private void InitializeRenderingSystems()
    {
        // Próximas etapas:
        //
        // 1. Vulkan backend.
        // 2. Shader modules.
        // 3. Descriptor layouts.
        // 4. Graphics pipelines.
        // 5. Compute pipelines.
        // 6. Render graph.
        // 7. G-Buffer.
        // 8. Deferred lighting.
        // 9. Atmosphere.
        // 10. Volumetric clouds.
        // 11. Post-processing.
    }

    public void Render()
    {
        if (!IsInitialized || IsDisposed)
            return;

        // Futuro:
        // Executar os passes do Infinity Render Graph.
    }

    public void Shutdown()
    {
        if (!IsInitialized && !Shaders.IsLoaded)
            return;

        try
        {
            ShutdownRenderingSystems();
        }
        finally
        {
            // Unload rather than dispose: a shutdown pipeline must be able to initialize again
            Shaders.Unload();
            IsInitialized = false;
        }
    }

    private void ShutdownRenderingSystems()
    {
        // Liberar recursos Vulkan na ordem inversa
        // da inicialização.
    }

    public void Dispose()
    {
        if (IsDisposed)
            return;

        Shutdown();
        Shaders.Dispose();

        IsDisposed = true;

        GC.SuppressFinalize(this);
    }
}
