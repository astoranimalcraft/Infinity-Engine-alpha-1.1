using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

using Infinity.Echo;
using Infinity.Editor.Projects;
using Infinity.Editor.Projects.Settings;
using Infinity.Editor.Projects.Scripting;
using Infinity.Runtime;

namespace Infinity.Editor.Build;

/// <summary>
/// A player build pipeline: what the build settings panel lists one card per, and what a build runs. A platform
/// assembly declares one and the editor discovers it, so nothing here needs to know the platforms in advance.
/// </summary>
public abstract class BuildPipeline
{
    /// <summary>The name the settings panel shows.</summary>
    public abstract string DisplayName { get; }

    /// <summary>The icon the settings panel shows, or empty for none.</summary>
    public virtual string Icon => "";

    /// <summary>Runs the build. Implementations report progress and return a result rather than throwing.</summary>
    public abstract Task<BuildResult> BuildAsync(string projectRoot, BuildSettings settings, string outputDirectory,
        BuildProgress? progress = null, CancellationToken token = default);

    /// <summary>The executable a finished build produced.</summary>
    public abstract string GetExecutablePath(string outputDirectory, BuildSettings settings);

    /// <summary>Starts the built game, when the user asked to build and run.</summary>
    public virtual void Launch(string outputDirectory, BuildSettings settings)
    {
        string executable = GetExecutablePath(outputDirectory, settings);
        if (!File.Exists(executable)) return;

        Process.Start(new ProcessStartInfo(executable)
        {
            WorkingDirectory = outputDirectory,
            UseShellExecute = true,
        });
    }

    /// <summary>
    /// Removes the data a prefab instance only needs while editing, so a shipped prefab carries what the player reads.
    /// Returns whether anything was removed.
    /// </summary>
    public static bool StripEditorOnlyPrefabData(EchoObject root) => Strip(root);

    // What a prefab instance records for the editor's benefit: the override table, how many components and children
    // the template had, and where each object came from. A player reads none of it.
    private static readonly string[] EditorOnlyPrefabKeys =
    [
        "Overrides", "SourceComponentCount", "SourceChildCount", "SourceIdentifier", "_prefabTemplateIdentity",
    ];

    private static bool Strip(EchoObject node)
    {
        bool changed = false;

        if (node.TagType == EchoType.Compound)
        {
            foreach (string key in EditorOnlyPrefabKeys)
                changed |= node.Remove(key);
            foreach (EchoObject child in node.Tags.Values)
                changed |= Strip(child);
        }
        else if (node.TagType == EchoType.List)
        {
            for (int i = 0; i < node.Count; i++)
                changed |= Strip(node[i]);
        }

        return changed;
    }
}

/// <summary>The pipeline that builds the desktop player, which is the one this tree ships.</summary>
public sealed class DesktopBuildPipeline : BuildPipeline
{
    public override string DisplayName => "Desktop";

    public override string Icon => "";

    public override string GetExecutablePath(string outputDirectory, BuildSettings settings)
        => Path.Combine(outputDirectory, settings.ResolveExecutableName() + (OperatingSystem.IsWindows() ? ".exe" : ""));

    public override async Task<BuildResult> BuildAsync(string projectRoot, BuildSettings settings, string outputDirectory,
        BuildProgress? progress = null, CancellationToken token = default)
    {
        DateTime start = DateTime.UtcNow;

        try
        {
            progress?.Report("Preparing the desktop player", 0.03f);
            Project? project = Project.Current;
            if (project == null)
                return Failure(outputDirectory, start, "No project is open.");

            string? engineRoot = FindEngineRoot();
            if (engineRoot == null)
                return Failure(outputDirectory, start,
                    "The player source (Players/Desktop) was not found. Run the editor from the engine source tree.");

            if (string.IsNullOrWhiteSpace(outputDirectory))
                return Failure(outputDirectory, start, "The build output directory is empty.");
            outputDirectory = Path.GetFullPath(outputDirectory);
            Directory.CreateDirectory(outputDirectory);

            if (token.IsCancellationRequested) return Cancelled(outputDirectory, start);

            GeneralSettings general = EditorRegistries.GetSettings<GeneralSettings>();
            var scenes = new List<Guid>();
            foreach (SceneBuildEntry entry in settings.Scenes)
                if (entry.Enabled && entry.SceneGuid != Guid.Empty)
                    scenes.Add(entry.SceneGuid);
            if (scenes.Count == 0)
                return Failure(outputDirectory, start, "No scene is enabled in the build settings.");

            string buildDir = project.BuildTempPath;
            if (Directory.Exists(buildDir)) Directory.Delete(buildDir, true);
            Directory.CreateDirectory(buildDir);

            progress?.Report("Compiling game scripts", 0.12f);
            string scriptDir = Path.Combine(buildDir, "Scripts");
            Directory.CreateDirectory(scriptDir);
            ScriptCompiler.PlayerCompileResult compile = ScriptCompiler.CompileForPlayer(
                project, "Desktop", ["PROWL"], settings.Config, scriptDir);
            if (!compile.Result.Success)
                return Failure(outputDirectory, start, string.IsNullOrWhiteSpace(compile.Result.Errors)
                    ? "The game scripts failed to compile."
                    : compile.Result.Errors);

            if (token.IsCancellationRequested) return Cancelled(outputDirectory, start);

            progress?.Report("Publishing the player", 0.3f);
            string publishDir = Path.Combine(buildDir, "Publish");
            Directory.CreateDirectory(publishDir);
            string projectFile = WritePlayerProject(engineRoot, buildDir, compile.Assemblies, general, settings);
            string publish = $"publish \"{projectFile}\" -c {settings.Config} -o \"{publishDir}\" --nologo -v minimal";
            string log = await RunDotNet(publish, buildDir, token).ConfigureAwait(false);
            if (!File.Exists(GetExecutablePath(publishDir, settings)))
            {
                // ReadyToRun needs a runtime pack that some offline installs don't have. The game is the same
                // either way, only its first launch is slower, so a missing pack must not fail the build.
                progress?.Report("Publishing the player without ReadyToRun", 0.42f);
                log += "\n" + await RunDotNet(publish + " -p:InfinityReadyToRun=false", buildDir, token)
                    .ConfigureAwait(false);
            }
            if (!File.Exists(GetExecutablePath(publishDir, settings)))
                return Failure(outputDirectory, start, "The player did not publish.\n" + log);

            if (token.IsCancellationRequested) return Cancelled(outputDirectory, start);

            progress?.Report("Exporting assets", 0.68f);
            string contentDir = Path.Combine(publishDir, "Content");
            Directory.CreateDirectory(contentDir);
            int assetCount = PlayerAssetExporter.Export(project, settings, contentDir, scenes,
                message => progress?.Report(message, 0.72f));

            progress?.Report("Writing player settings", 0.86f);
            ExportSettings(contentDir);

            progress?.Report("Writing player manifest", 0.92f);
            WriteManifest(project, general, settings, scenes, compile.Assemblies, publishDir);

            foreach (ScriptCompiler.BuildAssembly assembly in compile.Assemblies)
                CopyFile(assembly.DllPath, Path.Combine(publishDir, Path.GetFileName(assembly.DllPath)));

            CopyManagedDependencies(project, publishDir, compile.Assemblies);
            ExportPlugins(project, publishDir);
            ExportIcon(general, publishDir);
            OrganizeOutput(publishDir, compile.Assemblies);

            progress?.Report("Finalizing the build", 0.97f);
            RemovePreviousPlayer(outputDirectory, settings);
            CopyDirectory(publishDir, outputDirectory);
            File.WriteAllText(Path.Combine(outputDirectory, ".infinity-player"), settings.ResolveExecutableName());

            progress?.Report($"Build finished: {assetCount} assets", 1f);
            return new BuildResult
            {
                Success = true,
                OutputPath = outputDirectory,
                Duration = DateTime.UtcNow - start,
            };
        }
        catch (OperationCanceledException)
        {
            return Cancelled(outputDirectory, start);
        }
        catch (Exception exception)
        {
            return Failure(outputDirectory, start, exception.Message);
        }
    }

    private static BuildResult Failure(string output, DateTime start, string error) => new()
    {
        OutputPath = output,
        Duration = DateTime.UtcNow - start,
        Errors = [error],
    };

    private static BuildResult Cancelled(string output, DateTime start) => new()
    {
        Cancelled = true,
        OutputPath = output,
        Duration = DateTime.UtcNow - start,
    };

    private static string? FindEngineRoot()
    {
        foreach (string start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            DirectoryInfo? directory = new(start);
            while (directory != null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Players", "Desktop", "Prowl.Player.Desktop.csproj")) &&
                    File.Exists(Path.Combine(directory.FullName, "Prowl.Runtime", "Prowl.Runtime.csproj")))
                    return directory.FullName;
                directory = directory.Parent;
            }
        }
        return null;
    }

    private static string WritePlayerProject(string engineRoot, string buildDir,
        IReadOnlyList<ScriptCompiler.BuildAssembly> assemblies, GeneralSettings general, BuildSettings settings)
    {
        string player = Path.Combine(engineRoot, "Players", "Desktop", "Prowl.Player.Desktop.csproj");
        string runtime = Path.Combine(engineRoot, "Prowl.Runtime", "Prowl.Runtime.csproj");
        string icon = Path.Combine(engineRoot, "Prowl.Editor", "Resources", "Branding", "InfinityProject.ico");
        if (general.ResolvedIconPath is { } source)
        {
            if (string.Equals(Path.GetExtension(source), ".ico", StringComparison.OrdinalIgnoreCase))
                icon = source;
            else if (AppIconExporter.TryExport(source, buildDir, out _))
                icon = Path.Combine(buildDir, "icon.ico");
        }

        var references = new System.Text.StringBuilder();
        foreach (ScriptCompiler.BuildAssembly assembly in assemblies)
            references.Append("    <Reference Include=\"").Append(EscapeXml(assembly.Name)).Append("\">")
                .Append("<HintPath>").Append(EscapeXml(assembly.DllPath)).Append("</HintPath>")
                .Append("<Private>true</Private></Reference>\n");

        string executableName = settings.ResolveExecutableName();
        string product = string.IsNullOrWhiteSpace(general.ProductName) ? executableName : general.ProductName;
        string company = string.IsNullOrWhiteSpace(general.CompanyName) ? "Astora" : general.CompanyName;
        string version = System.Text.RegularExpressions.Regex.IsMatch(general.Version ?? "", @"^\d+\.\d+(\.\d+){0,2}$")
            ? general.Version!
            : "1.0.0";
        string outputType = OperatingSystem.IsWindows() ? "WinExe" : "Exe";
        string rid = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;

        string xml = $"""
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>{outputType}</OutputType>
    <TargetFramework>net11.0</TargetFramework>
    <AssemblyName>{EscapeXml(executableName)}</AssemblyName>
    <RootNamespace>Infinity.Player</RootNamespace>
    <StartupObject>Infinity.Player.BuildProgram</StartupObject>
    <Nullable>enable</Nullable>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <ImplicitUsings>disable</ImplicitUsings>
    <ApplicationIcon>{EscapeXml(icon)}</ApplicationIcon>
    <ApplicationManifest>{EscapeXml(Path.Combine(buildDir, "app.manifest"))}</ApplicationManifest>
    <SelfContained>false</SelfContained>
    <PublishSingleFile>false</PublishSingleFile>
    <PublishTrimmed>false</PublishTrimmed>
    <UseAppHost>true</UseAppHost>
    <RuntimeIdentifier>{EscapeXml(rid)}</RuntimeIdentifier>
    <PublishReadyToRun Condition="'$(InfinityReadyToRun)' != 'false'">true</PublishReadyToRun>
    <TieredPGO>true</TieredPGO>
    <TieredCompilation>true</TieredCompilation>
    <DebugType Condition="'$(Configuration)' == 'Release'">none</DebugType>
    <DebugType Condition="'$(Configuration)' != 'Release'">portable</DebugType>
    <AssemblyTitle>{EscapeXml(product)}</AssemblyTitle>
    <Product>{EscapeXml(product)}</Product>
    <Company>{EscapeXml(company)}</Company>
    <Version>{EscapeXml(version)}</Version>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="{EscapeXml(player)}" />
    <ProjectReference Include="{EscapeXml(runtime)}" />
{references}  </ItemGroup>
</Project>
""";

        string path = Path.Combine(buildDir, "InfinityPlayer.Build.csproj");
        File.WriteAllText(path, xml);
        File.WriteAllText(Path.Combine(buildDir, "Program.cs"), """
namespace Infinity.Player;

public static class BuildProgram
{
    public static int Main(string[] args) => PlayerEntryPoint.Main(args);
}
""");
        File.WriteAllText(Path.Combine(buildDir, "app.manifest"), """
<?xml version="1.0" encoding="utf-8"?>
<assembly manifestVersion="1.0" xmlns="urn:schemas-microsoft-com:asm.v1">
  <assemblyIdentity version="1.0.0.0" name="Infinity.Player"/>
  <application xmlns="urn:schemas-microsoft-com:asm.v3">
    <windowsSettings>
      <dpiAware xmlns="http://schemas.microsoft.com/SMI/2005/WindowsSettings">true/pm</dpiAware>
      <dpiAwareness xmlns="http://schemas.microsoft.com/SMI/2016/WindowsSettings">permonitorv2</dpiAwareness>
    </windowsSettings>
  </application>
</assembly>
""");
        return path;
    }

    private static async Task<string> RunDotNet(string arguments, string workingDirectory, CancellationToken token)
    {
        var info = new ProcessStartInfo("dotnet", arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start dotnet.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();

        while (!process.WaitForExit(100))
        {
            if (token.IsCancellationRequested)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                token.ThrowIfCancellationRequested();
            }
        }

        string output = await stdout.ConfigureAwait(false);
        string error = await stderr.ConfigureAwait(false);
        return string.IsNullOrWhiteSpace(error) ? output : output + "\n" + error;
    }

    private static void WriteManifest(Project project, GeneralSettings general, BuildSettings settings,
        IReadOnlyList<Guid> scenes, IReadOnlyList<ScriptCompiler.BuildAssembly> assemblies, string publishDir)
    {
        var manifest = new PlayerManifest
        {
            ProductName = string.IsNullOrWhiteSpace(general.ProductName) ? project.Name : general.ProductName,
            CompanyName = general.CompanyName,
            Version = general.Version,
            TargetId = "desktop",
            BuildDateUtcTicks = DateTime.UtcNow.Ticks,
            DefaultScene = scenes[0].ToString(),
            Scenes = [.. scenes.Select(scene => scene.ToString())],
            Packaging = settings.PackagingMode,
            WindowWidth = 1280,
            WindowHeight = 720,
            SecurityCheck = true,
            SecurityConfirmClicks = 2,
        };

        foreach (ScriptCompiler.BuildAssembly assembly in assemblies)
            manifest.AssemblyLoadOrder.Add(assembly.Name);

        manifest.Save(publishDir);
    }

    private static void ExportSettings(string contentDir)
    {
        string settingsDir = Path.Combine(contentDir, "Settings");
        Directory.CreateDirectory(settingsDir);

        foreach (EditorRegistries.SettingsEntry entry in EditorRegistries.SettingsEntries)
        {
            if (!entry.ExportToBuild || entry.Instance == null) continue;
            try
            {
                EchoObject echo = Serializer.Serialize(entry.Instance, TypeMode.Auto);
                string yaml = echo.WriteToYaml();
                File.WriteAllText(Path.Combine(settingsDir, $"{entry.Type.Name}.yaml"), yaml);
                if (!string.Equals(entry.Name, entry.Type.Name, StringComparison.Ordinal))
                    File.WriteAllText(Path.Combine(settingsDir, $"{entry.Name}.yaml"), yaml);
            }
            catch (Exception exception)
            {
                Infinity.Runtime.Debug.LogWarning($"[Build] Could not export {entry.Type.Name}: {exception.Message}");
            }
        }
    }

    private static void ExportIcon(GeneralSettings general, string publishDir)
    {
        string? source = general.ResolvedIconPath;
        if (source == null && FindEngineRoot() is { } engineRoot)
            source = Path.Combine(engineRoot, "Prowl.Editor", "Resources", "Branding", "InfinityProject.ico");
        if (source == null || !File.Exists(source)) return;

        // A build always ships both formats: icon.ico for Explorer and the executable, icon.png for the runtime window.
        if (AppIconExporter.TryExport(source, publishDir, out _))
            return;

        string extension = Path.GetExtension(source).ToLowerInvariant();
        CopyFile(source, Path.Combine(publishDir, extension == ".ico" ? "icon.ico" : "icon.png"));
    }

    private static void CopyManagedDependencies(Project project, string publishDir,
        IReadOnlyList<ScriptCompiler.BuildAssembly> assemblies)
    {
        if (!Directory.Exists(project.ScriptAssemblyPath)) return;

        var shipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ScriptCompiler.BuildAssembly assembly in assemblies)
            shipped.Add(Path.GetFileName(assembly.DllPath));

        foreach (string dll in Directory.EnumerateFiles(project.ScriptAssemblyPath, "*.dll", SearchOption.TopDirectoryOnly))
        {
            string name = Path.GetFileName(dll);
            if (name.EndsWith(".Editor.dll", StringComparison.OrdinalIgnoreCase)) continue;
            if (!shipped.Add(name)) continue;

            string destination = Path.Combine(publishDir, name);
            if (File.Exists(destination)) continue;
            try { File.Copy(dll, destination, overwrite: false); }
            catch (IOException) { }
        }
    }

    /// <summary>
    /// Ships the project's runtime plugins. Managed plugins go under <c>runtimes/&lt;assembly name&gt;.dll</c>, which is
    /// where the player's resolver probes, and native ones under <c>runtimes/&lt;rid&gt;/native</c>. Editor-only plugins
    /// never ship, and a plugin whose file name differs from its assembly name ships under the assembly name.
    /// </summary>
    private static void ExportPlugins(Project project, string publishDir)
    {
        foreach (PluginInfo plugin in PluginScanner.ScanAll(project))
        {
            if (!plugin.AppliesToBuild(BuildPlatforms.Windows)) continue;

            string destination;
            if (plugin.IsNative)
            {
                string rid = plugin.RuntimeIdentifierFor(BuildPlatforms.Windows);
                destination = Path.Combine(publishDir, "runtimes", rid, "native", plugin.FileName);
            }
            else
            {
                string name;
                try { name = AssemblyName.GetAssemblyName(plugin.AbsolutePath).Name ?? Path.GetFileNameWithoutExtension(plugin.FileName); }
                catch { name = Path.GetFileNameWithoutExtension(plugin.FileName); }
                destination = Path.Combine(publishDir, "runtimes", name + ".dll");
            }

            CopyFile(plugin.AbsolutePath, destination);
        }
    }

    private static readonly HashSet<string> s_engineLibraryNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Aperture", "Clay", "Echo", "Motion", "Origami", "Paper", "Photonic", "Quill", "Quire",
        "Rosetta", "Scaffold", "Scribe", "Unwrapper", "Vector",
    };

    /// <summary>
    /// Arranges a finished publish into the layout players expect: the executable and the game assembly at the
    /// root, engine assemblies and icons in Engine, a copy of the framework in Runtime, and assets in Content.
    /// Engine DLL paths are written into the deps file before they move, which is what lets the host find them
    /// from a subfolder; framework assemblies stay at the root because a self contained host resolves those before
    /// any managed code runs.
    /// </summary>
    private static void OrganizeOutput(string publishDir, IReadOnlyList<ScriptCompiler.BuildAssembly> assemblies)
    {
        string engineDir = Path.Combine(publishDir, "Engine");
        string runtimeDir = Path.Combine(publishDir, "Runtime");
        string iconDir = Path.Combine(engineDir, "Icons");
        Directory.CreateDirectory(engineDir);
        Directory.CreateDirectory(runtimeDir);
        Directory.CreateDirectory(iconDir);

        var game = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ScriptCompiler.BuildAssembly assembly in assemblies)
            game.Add(Path.GetFileName(assembly.DllPath));

        var moved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string file in Directory.EnumerateFiles(publishDir, "*", SearchOption.TopDirectoryOnly))
        {
            string name = Path.GetFileName(file);
            string extension = Path.GetExtension(name);
            string stem = Path.GetFileNameWithoutExtension(name);
            if (game.Contains(name)) continue;
            if (name.Equals("Prowl.Player.Desktop.dll", StringComparison.OrdinalIgnoreCase)) continue;

            if (extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) && IsEngineAssemblyName(stem))
            {
                string target = Path.Combine(engineDir, name);
                File.Move(file, target, overwrite: true);
                moved[name] = "Engine/" + name;
                MoveSiblingFiles(publishDir, engineDir, stem, moved);
                continue;
            }

            if (extension.Equals(".ico", StringComparison.OrdinalIgnoreCase) || extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
            {
                string target = Path.Combine(iconDir, name);
                File.Move(file, target, overwrite: true);
                continue;
            }

            if (IsFrameworkAssemblyName(stem))
            {
                // Copy, not move: the self contained host probes the root before the deps file is read.
                File.Copy(file, Path.Combine(runtimeDir, name), overwrite: true);
            }
        }

        // Native plugins keep the runtimes/<rid>/native layout the host probes, but get a Runtime mirror.
        string runtimes = Path.Combine(publishDir, "runtimes");
        if (Directory.Exists(runtimes))
            CopyDirectory(runtimes, Path.Combine(runtimeDir, "Native"));

        RewriteDepsPaths(publishDir, moved);
    }

    private static void MoveSiblingFiles(string sourceDir, string targetDir, string stem, Dictionary<string, string> moved)
    {
        foreach (string extension in new[] { ".pdb", ".xml" })
        {
            string sibling = Path.Combine(sourceDir, stem + extension);
            if (!File.Exists(sibling)) continue;
            string name = Path.GetFileName(sibling);
            File.Move(sibling, Path.Combine(targetDir, name), overwrite: true);
            moved[name] = "Engine/" + name;
        }
    }

    private static bool IsEngineAssemblyName(string stem)
        => stem.StartsWith("Prowl.", StringComparison.OrdinalIgnoreCase)
           || stem.StartsWith("Infinity.", StringComparison.OrdinalIgnoreCase)
           || s_engineLibraryNames.Contains(stem);

    private static bool IsFrameworkAssemblyName(string stem)
        => stem.StartsWith("System", StringComparison.OrdinalIgnoreCase)
           || stem.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase)
           || stem.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase)
           || stem.StartsWith("mscorlib", StringComparison.OrdinalIgnoreCase)
           || stem.StartsWith("WindowsBase", StringComparison.OrdinalIgnoreCase);

    /// <summary>Points each moved runtime asset at its new folder in every deps target.</summary>
    private static void RewriteDepsPaths(string publishDir, IReadOnlyDictionary<string, string> moved)
    {
        if (moved.Count == 0) return;
        string? deps = Directory.EnumerateFiles(publishDir, "*.deps.json", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (deps == null) return;

        try
        {
            JsonNode? root = JsonNode.Parse(File.ReadAllText(deps));
            if (root?["targets"] is not JsonObject targets) return;

            foreach ((string _, JsonNode? target) in targets)
            {
                if (target is not JsonObject libraries) continue;
                foreach ((string _, JsonNode? library) in libraries)
                {
                    if (library is not JsonObject entry) continue;
                    foreach (string groupName in new[] { "runtime", "native", "runtimeTargets" })
                    {
                        if (entry[groupName] is not JsonObject group) continue;
                        var replacements = new List<(string Old, string New, JsonNode? Value)>();
                        foreach ((string assetPath, JsonNode? value) in group)
                        {
                            string name = Path.GetFileName(assetPath);
                            if (!moved.TryGetValue(name, out string? newPath)) continue;
                            replacements.Add((assetPath, newPath, value?.DeepClone()));
                        }
                        foreach ((string oldPath, string newPath, JsonNode? value) in replacements)
                        {
                            group.Remove(oldPath);
                            group[newPath] = value;
                        }
                    }
                }
            }

            File.WriteAllText(deps, root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception e)
        {
            Infinity.Runtime.Debug.LogWarning($"[Build] Could not rewrite {Path.GetFileName(deps)}: {e.Message}");
        }
    }

    /// <summary>True for the assemblies a self contained publish puts in the output root.</summary>
    public static bool IsFrameworkAssembly(string fileName) => IsFrameworkAssemblyName(Path.GetFileNameWithoutExtension(fileName));

    /// <summary>
    /// Deletes the executables a previous build wrote when the game's name changed, so the output folder never has
    /// two runnable players. Only names this pipeline recorded are touched.
    /// </summary>
    private static void RemovePreviousPlayer(string outputDirectory, BuildSettings settings)
    {
        string marker = Path.Combine(outputDirectory, ".infinity-player");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ProwlPlayer" };
        try
        {
            if (File.Exists(marker) && File.ReadAllText(marker).Trim() is { Length: > 0 } previous)
                names.Add(previous);
        }
        catch (IOException) { }
        names.Add(settings.ResolveExecutableName());

        string[] suffixes = ["", ".exe", ".dll", ".pdb", ".deps.json", ".runtimeconfig.json"];
        foreach (string name in names)
            foreach (string suffix in suffixes)
            {
                string candidate = Path.Combine(outputDirectory, name + suffix);
                try { if (File.Exists(candidate)) File.Delete(candidate); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(source, file);
            string target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static void CopyFile(string source, string destination)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.Copy(source, destination, overwrite: true);
    }

    private static string EscapeXml(string value)
        => value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}

/// <summary>A platform a pipeline can build for, as a provider hands it to the registry.</summary>
public sealed record PlatformTarget(string Name, Type PipelineType);

/// <summary>
/// What a platform assembly implements so its targets reach the registry. The editor instantiates every
/// implementation it finds in a loaded assembly and registers it.
/// </summary>
public interface IBuildTargetProvider
{
    IEnumerable<PlatformTarget> Targets { get; }
}

/// <summary>The targets every loaded provider offers, which the build settings panel lists.</summary>
public sealed class TargetRegistry
{
    public static TargetRegistry Shared { get; } = new();

    private readonly List<PlatformTarget> _targets = [];

    public IReadOnlyList<PlatformTarget> Targets => _targets;

    public void RegisterFrom(IBuildTargetProvider provider)
    {
        foreach (PlatformTarget target in provider.Targets)
            if (!_targets.Contains(target))
                _targets.Add(target);
    }

    /// <summary>Forgets every registered target, which a registry reload does before scanning the assemblies again.</summary>
    public void ResetToBuiltIns() => _targets.Clear();
}
