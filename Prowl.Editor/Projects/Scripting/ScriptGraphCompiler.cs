using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

using Infinity.Editor;
using Infinity.Runtime.Graphs;

namespace Infinity.Editor.Projects.Scripting;

/// <summary>Compiles a <see cref="ScriptGraph"/> to a small assembly the runner loads at play time.</summary>
public static class ScriptGraphCompiler
{
    /// <summary>Generates, compiles and saves the graph. Returns false and a diagnostic message on failure.</summary>
    public static bool Compile(ScriptGraph graph, out string message)
    {
        string source = graph.ToCSharp();
        graph.GeneratedCode = source;
        graph.ClassName = graph.ResolveClassName();

        SyntaxTree tree = CSharpSyntaxTree.ParseText(source,
            new CSharpParseOptions(LanguageVersion.Latest), path: $"{graph.Name}.scriptgraph.cs");

        var compilation = CSharpCompilation.Create(
            graph.ClassName,
            [tree],
            CollectReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Debug,
                allowUnsafe: true,
                nullableContextOptions: NullableContextOptions.Disable));

        using var image = new MemoryStream();
        var result = compilation.Emit(image);
        if (!result.Success)
        {
            message = string.Join("\n", result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.ToString()));
            return false;
        }

        string directory = Project.Current?.ScriptAssemblyPath
            ?? Path.Combine(Path.GetTempPath(), "InfinityScriptGraphs");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"{graph.ClassName}.dll");
        File.WriteAllBytes(path, image.ToArray());

        graph.AssemblyPath = path;
        message = path;
        return true;
    }

    private static IEnumerable<MetadataReference> CollectReferences()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var references = new List<MetadataReference>();

        void AddPath(string? path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path) || !seen.Add(path)) return;
            try { references.Add(MetadataReference.CreateFromFile(path)); }
            catch { /* native or mixed-mode assemblies cannot be referenced */ }
        }

        if (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string tpa)
            foreach (string path in tpa.Split(Path.PathSeparator)) AddPath(path);

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic) continue;
            try { AddPath(assembly.Location); } catch { }
        }

        return references;
    }
}
