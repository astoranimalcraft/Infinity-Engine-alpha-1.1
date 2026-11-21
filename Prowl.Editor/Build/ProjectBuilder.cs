using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Infinity.Editor.Build;

/// <summary>Starts player builds and hands back the progress the editor and the CLI follow.</summary>
public static class ProjectBuilder
{
    private static BuildProgress? s_running;

    /// <summary>
    /// Every build pipeline in the loaded assemblies. The settings panel lists one card per pipeline from this, so it
    /// and the build itself agree on what exists.
    /// </summary>
    public static IEnumerable<BuildPipeline> DiscoverPipelines()
    {
        foreach (Type type in Infinity.Editor.Utils.EditorUtils.GetAllTypes())
        {
            if (type.IsAbstract || !typeof(BuildPipeline).IsAssignableFrom(type)) continue;
            if (Activator.CreateInstance(type) is BuildPipeline pipeline) yield return pipeline;
        }
    }

    /// <summary>
    /// Starts a build into <paramref name="outputPath"/>, or returns null when one is already running or the project
    /// cannot be built. The build runs in the background and reports through the returned progress.
    /// </summary>
    public static BuildProgress? StartBuildAsync(bool runWhenFinished, string outputPath)
    {
        if (s_running is { IsComplete: false })
            return null;

        var progress = new BuildProgress();
        s_running = progress;

        _ = Task.Run(() => Run(progress, runWhenFinished, outputPath));
        return progress;
    }

    private static void Run(BuildProgress progress, bool runWhenFinished, string outputPath)
    {
        DateTime start = DateTime.UtcNow;

        try
        {
            progress.Report("Preparing the build", 0.05f);

            if (progress.Token.IsCancellationRequested)
            {
                progress.Complete(new BuildResult
                {
                    Cancelled = true,
                    OutputPath = outputPath,
                    Duration = DateTime.UtcNow - start,
                });
                return;
            }

            // No player pipeline ships with this snapshot, so there is nothing to compile or copy yet. Failing here
            // rather than pretending keeps the editor and the CLI honest about what was produced.
            progress.Complete(new BuildResult
            {
                OutputPath = outputPath,
                Duration = DateTime.UtcNow - start,
                Errors = ["No player build pipeline is installed, so nothing was built."],
            });
        }
        catch (Exception exception)
        {
            progress.Complete(new BuildResult
            {
                OutputPath = outputPath,
                Duration = DateTime.UtcNow - start,
                Errors = [exception.Message],
            });
        }
    }
}

/// <summary>Draws a platform profile's own fields in the build settings panel.</summary>
public static class BuildProfileDrawers
{
    /// <summary>
    /// Draws <paramref name="profile"/>. A profile with no drawer of its own shows nothing, which is why pipelines
    /// that need fields register one through <see cref="Register{T}"/>.
    /// </summary>
    public static void Draw(Infinity.PaperUI.Paper paper, PlatformBuildProfile profile)
    {
        if (Drawers.TryGetValue(profile.GetType(), out Action<Infinity.PaperUI.Paper, PlatformBuildProfile>? drawer))
            drawer(paper, profile);
    }

    private static readonly System.Collections.Generic.Dictionary<Type, Action<Infinity.PaperUI.Paper, PlatformBuildProfile>> Drawers = [];

    /// <summary>Registers the drawer for a profile type, which replaces any drawer already registered for it.</summary>
    public static void Register<T>(Action<Infinity.PaperUI.Paper, T> drawer) where T : PlatformBuildProfile
        => Drawers[typeof(T)] = (paper, profile) => drawer(paper, (T)profile);
}
