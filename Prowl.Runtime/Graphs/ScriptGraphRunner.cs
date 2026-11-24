// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace Infinity.Runtime.Graphs;

/// <summary>
/// Runs a compiled <see cref="ScriptGraph"/> on this object. The graph editor compiles the C# assembly; this
/// component loads it and forwards the engine's lifecycle calls to the generated class.
/// </summary>
[AddComponentMenu("Scripting/Script Graph Runner")]
[ComponentIcon("\uf121")] // Code
public class ScriptGraphRunner : Component
{
    /// <summary>The graph to run. The editor fills in the compiled assembly path when it compiles.</summary>
    public ScriptGraph? Graph;

    private ScriptGraphBehaviour? _instance;
    private Assembly? _assembly;
    private AssemblyLoadContext? _context;

    /// <summary>The generated object, for scripts that need to poke it directly.</summary>
    public object? Instance => _instance;

    /// <summary>True when the assembly loaded and the generated class is running.</summary>
    public bool IsRunning => _instance != null;

    public override void OnEnable()
    {
        Load();
        _instance?.OnEnable();
    }

    public override void OnDisable()
    {
        _instance?.OnDisable();
        _instance = null;
        _assembly = null;
        _context?.Unload();
        _context = null;
    }

    public override void Start() => _instance?.OnStart();

    public override void Update() => _instance?.OnUpdate();

    public override void LateUpdate() => _instance?.OnLateUpdate();

    /// <summary>Reloads the compiled assembly, picking up a new compile without restarting the game.</summary>
    [Button("Reload Graph")]
    public void Reload()
    {
        _instance?.OnDisable();
        _instance = null;
        _assembly = null;
        _context?.Unload();
        _context = null;
        Load();
        _instance?.OnEnable();
    }

    private void Load()
    {
        if (Graph.IsNotValid())
            return;

        string? path = ResolveAssemblyPath(Graph!);
        if (path == null)
        {
            Debug.LogWarning($"[ScriptGraphRunner] '{GameObject.Name}' has no compiled assembly for '{Graph!.Name}'. Compile it in the script graph editor.");
            return;
        }

        try
        {
            _context = new AssemblyLoadContext($"ScriptGraph-{Graph!.AssetID}", isCollectible: true);
            using var stream = new MemoryStream(File.ReadAllBytes(path));
            _assembly = _context.LoadFromStream(stream);
            Type? type = _assembly.GetType(Graph!.ResolveClassName());
            if (type == null)
            {
                Debug.LogError($"[ScriptGraphRunner] '{path}' has no class named '{Graph.ResolveClassName()}'.");
                return;
            }

            if (Activator.CreateInstance(type) is not ScriptGraphBehaviour instance)
            {
                Debug.LogError($"[ScriptGraphRunner] '{type.FullName}' does not derive from ScriptGraphBehaviour.");
                return;
            }

            instance.GameObject = GameObject;
            _instance = instance;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ScriptGraphRunner] Could not load '{path}': {ex.Message}");
        }
    }

    private static string? ResolveAssemblyPath(ScriptGraph graph)
    {
        if (graph.AssemblyPath.Length == 0) return null;
        if (Path.IsPathRooted(graph.AssemblyPath)) return File.Exists(graph.AssemblyPath) ? graph.AssemblyPath : null;

        string fromWorkingDirectory = Path.GetFullPath(graph.AssemblyPath);
        if (File.Exists(fromWorkingDirectory)) return fromWorkingDirectory;

        string? assetPath = AssetDatabase.GetAssetPath(graph.AssetID);
        if (assetPath != null)
        {
            string? directory = Path.GetDirectoryName(assetPath);
            if (directory != null)
            {
                string besideAsset = Path.GetFullPath(Path.Combine(directory, Path.GetFileName(graph.AssemblyPath)));
                if (File.Exists(besideAsset)) return besideAsset;
            }
        }

        return null;
    }
}
