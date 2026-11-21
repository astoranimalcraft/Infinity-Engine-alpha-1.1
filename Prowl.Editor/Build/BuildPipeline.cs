using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Infinity.Echo;
using Infinity.Editor.Projects.Settings;

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

    /// <summary>
    /// Builds the player into <paramref name="outputDirectory"/>. The player itself ships with this tree, but the
    /// asset staging and packaging stages do not, so this reports why it produced nothing instead of half a build.
    /// </summary>
    public Task<BuildResult> BuildAsync(string projectRoot, BuildSettings settings, string outputDirectory,
        BuildProgress? progress = null, CancellationToken token = default)
    {
        DateTime start = DateTime.UtcNow;
        progress?.Report("Preparing the desktop player", 0.05f);

        if (token.IsCancellationRequested)
            return Task.FromResult(new BuildResult { Cancelled = true, OutputPath = outputDirectory, Duration = DateTime.UtcNow - start });

        return Task.FromResult(new BuildResult
        {
            OutputPath = outputDirectory,
            Duration = DateTime.UtcNow - start,
            Errors = ["The desktop player's staging and packaging stages are not part of this source tree, so nothing was built."],
        });
    }

    /// <summary>Where the player's executable ends up for a finished build.</summary>
    public string GetExecutablePath(string outputDirectory, BuildSettings settings)
        => Path.Combine(outputDirectory, OperatingSystem.IsWindows() ? "ProwlPlayer.exe" : "ProwlPlayer");
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
