
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Infinity.Runtime.Rendering.Pipelines.Infinity;

public enum InfinityShaderStage
{
    Vertex,
    Fragment,
    Compute
}

public sealed class InfinityShader
{
    public string Name { get; }

    public string FilePath { get; }

    public InfinityShaderStage Stage { get; }

    public byte[] SpirV { get; }

    public int SizeInBytes => SpirV.Length;

    public InfinityShader(
        string name,
        string filePath,
        InfinityShaderStage stage,
        byte[] spirV)
    {
        Name = name;
        FilePath = filePath;
        Stage = stage;
        SpirV = spirV ?? throw new ArgumentNullException(nameof(spirV));
    }
}

public sealed class InfinityShaderLibrary : IDisposable
{
    private readonly Dictionary<string, InfinityShader> _shaders =
        new(StringComparer.OrdinalIgnoreCase);

    private bool _disposed;

    public bool IsLoaded { get; private set; }

    public int ShaderCount => _shaders.Count;

    public IEnumerable<InfinityShader> Shaders => _shaders.Values;

    public string? LoadedDirectory { get; private set; }

    private const uint SpirVMagic = 0x07230203;

    /// <summary>
    /// Loads all compiled SPIR-V shaders.
    /// </summary>
    public void Load()
    {
        ThrowIfDisposed();

        if (IsLoaded)
            return;

        string directory = GetShaderDirectory();

        LoadFromDirectory(directory);
    }

    /// <summary>
    /// Loads shaders from a specific directory.
    /// Useful for development and custom project layouts.
    /// </summary>
    public void LoadFromDirectory(string directory)
    {
        ThrowIfDisposed();

        if (IsLoaded)
            throw new InvalidOperationException(
                "Infinity Shader Library is already loaded.");

        if (string.IsNullOrWhiteSpace(directory))
            throw new ArgumentException(
                "Shader directory cannot be empty.",
                nameof(directory));

        directory = Path.GetFullPath(directory);

        if (!Directory.Exists(directory))
            throw new DirectoryNotFoundException(
                $"Infinity shaders directory not found: {directory}");

        string[] files = Directory.GetFiles(
            directory,
            "*.spv",
            SearchOption.AllDirectories);

        if (files.Length == 0)
            throw new InvalidOperationException(
                $"No compiled SPIR-V shaders were found in: {directory}");

        var loaded = new Dictionary<string, InfinityShader>(
            StringComparer.OrdinalIgnoreCase);

        // A build can carry the same shader in more than one folder, for instance a staged copy next to the
        // canonical one. The copy closest to the shader root wins, so a stray duplicate cannot stop the load.
        IEnumerable<string> ordered = files
            .OrderBy(x => Path.GetRelativePath(directory, x).Count(c => c == Path.DirectorySeparatorChar))
            .ThenBy(x => x, StringComparer.OrdinalIgnoreCase);

        foreach (string file in ordered)
        {
            InfinityShader shader = ReadShader(file);

            if (loaded.ContainsKey(shader.Name))
                continue;

            loaded.Add(shader.Name, shader);
        }

        foreach (var entry in loaded)
            _shaders.Add(entry.Key, entry.Value);

        LoadedDirectory = directory;
        IsLoaded = true;
    }

    private static InfinityShader ReadShader(string file)
    {
        string fileName = Path.GetFileName(file);

        string name = fileName.EndsWith(
            ".spv",
            StringComparison.OrdinalIgnoreCase)
            ? fileName[..^4]
            : fileName;

        InfinityShaderStage stage = GetStage(name);

        byte[] spirV = File.ReadAllBytes(file);

        ValidateSpirV(spirV, file);

        return new InfinityShader(
            name,
            file,
            stage,
            spirV);
    }

    /// <summary>
    /// Validates the SPIR-V binary header and basic structure.
    /// </summary>
    private static void ValidateSpirV(
        byte[] spirV,
        string file)
    {
        if (spirV.Length < 20 || spirV.Length % 4 != 0)
        {
            throw new InvalidDataException(
                $"Invalid SPIR-V file size: {file}");
        }

        uint magic = BitConverter.ToUInt32(spirV, 0);

        if (magic != SpirVMagic)
        {
            throw new InvalidDataException(
                $"Invalid SPIR-V magic number: {file}");
        }

        uint version = BitConverter.ToUInt32(spirV, 4);

        if (version == 0)
        {
            throw new InvalidDataException(
                $"Invalid SPIR-V version: {file}");
        }

        uint bound = BitConverter.ToUInt32(spirV, 12);

        if (bound == 0)
        {
            throw new InvalidDataException(
                $"Invalid SPIR-V ID bound: {file}");
        }
    }

    private static InfinityShaderStage GetStage(string name)
    {
        if (name.EndsWith(
            ".vert",
            StringComparison.OrdinalIgnoreCase))
            return InfinityShaderStage.Vertex;

        if (name.EndsWith(
            ".frag",
            StringComparison.OrdinalIgnoreCase))
            return InfinityShaderStage.Fragment;

        if (name.EndsWith(
            ".comp",
            StringComparison.OrdinalIgnoreCase))
            return InfinityShaderStage.Compute;

        throw new NotSupportedException(
            $"Unsupported shader stage in filename: {name}");
    }

    /// <summary>
    /// Locates the Infinity shader directory.
    /// </summary>
    private static string GetShaderDirectory()
    {
        string assemblyDirectory = Path.GetDirectoryName(
            Assembly.GetExecutingAssembly().Location)
            ?? AppContext.BaseDirectory;

        string[] candidates =
        {
            Path.Combine(
                assemblyDirectory,
                "Rendering",
                "Pipelines",
                "Infinity",
                "Shaders"),

            Path.Combine(
                AppContext.BaseDirectory,
                "Rendering",
                "Pipelines",
                "Infinity",
                "Shaders"),

            Path.Combine(
                AppContext.BaseDirectory,
                "Infinity.Runtime",
                "Rendering",
                "Pipelines",
                "Infinity",
                "Shaders")
        };

        foreach (string candidate in candidates)
        {
            if (Directory.Exists(candidate))
                return candidate;
        }

        throw new DirectoryNotFoundException(
            "Infinity shader directory could not be located. " +
            "Use LoadFromDirectory() to specify its location.");
    }

    /// <summary>
    /// Gets a shader by its filename without the .spv extension.
    /// Example: InfinityBloomExtract.frag
    /// </summary>
    public InfinityShader Get(string name)
    {
        ThrowIfDisposed();

        if (!IsLoaded)
            throw new InvalidOperationException(
                "Infinity Shader Library is not loaded.");

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException(
                "Shader name cannot be empty.",
                nameof(name));

        if (!_shaders.TryGetValue(name, out InfinityShader? shader))
        {
            throw new KeyNotFoundException(
                $"Infinity shader not found: {name}");
        }

        return shader;
    }

    public bool TryGet(
        string name,
        out InfinityShader? shader)
    {
        ThrowIfDisposed();

        return _shaders.TryGetValue(name, out shader);
    }

    public bool Contains(string name)
    {
        ThrowIfDisposed();

        return _shaders.ContainsKey(name);
    }

    public IEnumerable<InfinityShader> GetByStage(
        InfinityShaderStage stage)
    {
        ThrowIfDisposed();

        return _shaders.Values.Where(
            shader => shader.Stage == stage);
    }

    public void Unload()
    {
        ThrowIfDisposed();

        _shaders.Clear();

        IsLoaded = false;

        LoadedDirectory = null;
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _shaders.Clear();

        IsLoaded = false;

        LoadedDirectory = null;

        _disposed = true;
    }
}
