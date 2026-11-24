using System;
using System.Collections.Generic;
using System.Linq;

using Infinity.Editor.Build;
using Infinity.Editor.Core;
using Infinity.Editor.GUI;
using Infinity.Editor.GUI.SceneView;
using Infinity.Editor.Inspector;
using Infinity.Editor.Theming;
using Infinity.Editor.Utils;
using Infinity.OrigamiUI;
using Infinity.PaperUI;
using Infinity.PaperUI.LayoutEngine;
using Infinity.Runtime;

namespace Infinity.Editor.Projects.Settings;

public enum BuildTarget { Windows, Linux, MacOS }
public enum BuildConfiguration { Debug, Release }
public enum AssetExportMode { AllAssets, DependenciesOnly }

public class SceneBuildEntry
{
    public string Path = "";
    public Guid SceneGuid;
    public bool Enabled = true;
}

/// <summary> Project settings for configuring builds, including scenes, build configuration, output directory, asset packaging, and platform profiles. </summary>
[ProjectSettings("Build", EditorIcons.Hammer, order: 50, exportToBuild: false)]
public sealed class BuildSettings : ProjectSettingsBase
{
    public override bool DrawInProjectSettingsPanel => true;

    public List<SceneBuildEntry> Scenes = [];

    /// <summary>Adds every listed scene with a file that is not in <paramref name="scenes"/> yet, in order. Returns how many.</summary>
    internal static int AddOpenScenes(List<SceneBuildEntry> scenes)
    {
        int added = 0;
        foreach (OpenScene open in EditorSceneManager.OpenScenes)
        {
            if (open.Scene.IsNotValid() || open.Path is not { } path || scenes.Any(s => s.SceneGuid == open.Scene.AssetID)) continue;
            scenes.Add(new SceneBuildEntry { Path = path, SceneGuid = open.Scene.AssetID });
            added++;
        }
        return added;
    }

    public BuildConfiguration Config = BuildConfiguration.Release;
    public string OutputDirectory = "Builds";

    /// <summary>File name of the built game, without the extension. Empty uses the product name.</summary>
    public string ExecutableName = "";

    /// <summary>The name the built executable gets: the explicit name, else the product name, sanitised for a file name.</summary>
    public string ResolveExecutableName()
    {
        string source = !string.IsNullOrWhiteSpace(ExecutableName)
            ? ExecutableName
            : EditorRegistries.GetSettings<GeneralSettings>()?.ProductName ?? "";
        if (string.IsNullOrWhiteSpace(source))
            source = Project.Current?.Name ?? "InfinityGame";
        if (source.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            source = source[..^4];
        source = source.Trim().TrimEnd('.');

        var builder = new System.Text.StringBuilder(source.Length);
        foreach (char c in source.Trim())
            builder.Append(char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' ? c : '_');
        string clean = builder.ToString().Trim(' ', '.');
        if (clean.Length == 0) return "InfinityGame";
        return s_reservedNames.Contains(clean) ? clean + "_" : clean;
    }

    private static readonly HashSet<string> s_reservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    public AssetPackagingMode PackagingMode = AssetPackagingMode.ProwlPak;
    public AssetExportMode AssetMode = AssetExportMode.DependenciesOnly;
    public int MaxPakSizeMB = 2048;

    /// <summary>Full name of the <see cref="BuildPipeline"/> to build with. Empty means desktop.</summary>
    public string SelectedPipeline = "";

    /// <summary>
    /// Per-platform profiles.  Missing entries will be created with
    /// defaults on first access.
    /// </summary>
    public List<PlatformBuildProfile> PlatformProfiles = [];

    /// <summary>
    /// Returns (or lazily creates) the profile for the given target.
    /// </summary>
    public T GetProfile<T>(Type pipelineType) where T : PlatformBuildProfile
    {
        PlatformBuildProfile? profile = PlatformProfiles.FirstOrDefault(profile => pipelineType == profile.GetPipelineType());
        if (profile == null)
        {
            profile = System.Activator.CreateInstance<T>();
            PlatformProfiles.Add(profile);
        }
        return (T)profile;
    }

    /// <summary>
    /// Returns or creates the profile for the given target.
    /// If not present, returns null.
    /// </summary>
    public PlatformBuildProfile GetOrCreateProfile(Type pipelineType)
    {
        PlatformBuildProfile? profile = PlatformProfiles.FirstOrDefault(profile => pipelineType == profile.GetPipelineType());

        // If the profile is null, create the profile
        if (profile == null)
        {
            Type targetType = null;
            foreach (Type type in EditorUtils.GetAllTypes())
            {
                if (!type.IsSubclassOf(typeof(PlatformBuildProfile)) || type == typeof(PlatformBuildProfile)) continue;
                var check = Activator.CreateInstance(type) as PlatformBuildProfile;
                if (check?.GetPipelineType() == pipelineType)
                {
                    targetType = type;
                    break;
                }
            }

            if (targetType != null)
            {
                profile = (PlatformBuildProfile)System.Activator.CreateInstance(targetType);
                PlatformProfiles.Add(profile);
                EditorRegistries.SaveSettings();
            }
        }
        return profile;
    }

    /// <summary> Draws the build settings UI in the project settings panel. </summary>
    public override void OnGUI(Paper paper, float width)
    {
        Scribe.FontFile? font = EditorTheme.DefaultFont;
        if (font == null) return;

        // Scene List
        Origami.Header(paper, "bld_scenes_h", $"{EditorIcons.Shapes}  Scenes").Underline().Show();

        for (int i = 0; i < Scenes.Count; i++)
        {
            int idx = i;
            SceneBuildEntry scene = Scenes[i];

            using (paper.Row($"bld_scene_{i}").Height(EditorTheme.RowHeight).Gap(4).PaddingLeft(4).Enter())
            {
                // Index
                paper.Box($"bld_si_{i}")
                    .Width(20).Height(EditorTheme.RowHeight)
                    .Text(i.ToString(), font).TextColor(EditorTheme.Ink400)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleCenter);

                // Enable toggle
                Origami.Checkbox(paper, $"bld_se_{i}", scene.Enabled,
                        v => { scene.Enabled = v; EditorRegistries.SaveSettings(); })
                    .NoLabel().Show();

                // Scene name
                string displayName = !string.IsNullOrEmpty(scene.Path)
                    ? System.IO.Path.GetFileNameWithoutExtension(scene.Path)
                    : scene.SceneGuid.ToString()[..8];

                paper.Box($"bld_sn_{i}")
                    .Height(EditorTheme.RowHeight).PaddingLeft(4)
                    .Text(displayName, font).TextColor(EditorTheme.Ink500)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft);

                // Move up/down
                if (i > 0)
                {
                    Origami.IconButton(paper, $"bld_su_{i}", EditorIcons.ArrowUp_I, () =>
                        {
                            (Scenes[idx - 1], Scenes[idx]) = (Scenes[idx], Scenes[idx - 1]);
                            EditorRegistries.SaveSettings();
                        })
                        .Small().Width(EditorTheme.RowHeight).Height(EditorTheme.RowHeight).Ghost().Show();
                }

                // Remove
                Origami.IconButton(paper, $"bld_sr_{i}", EditorIcons.Xmark_I, () =>
                    {
                        Scenes.RemoveAt(idx);
                        EditorRegistries.SaveSettings();
                    })
                    .Small().Width(EditorTheme.RowHeight).Height(EditorTheme.RowHeight).Ghost().Show();
            }
        }

        // Add scene buttons
        using (paper.Row("bld_add_row").Height(EditorTheme.RowHeight).Gap(6).PaddingLeft(4).Enter())
        {
            Origami.Button(paper, "bld_add_open", $"{EditorIcons.Plus} Add Open Scenes", () =>
                {
                    if (AddOpenScenes(Scenes) > 0) EditorRegistries.SaveSettings();
                }).Width(140).Show();
        }

        paper.Box("bld_sp1").Height(12);

        // Build Configuration
        Origami.Header(paper, "bld_config_h", $"{EditorIcons.Gear}  Configuration").Underline().Show();

        EditorGUI.SettingsEnumDropdown(paper, "bld_config", "Configuration", Config, v => Config = v);
        EditorGUI.SettingsRow(paper, "bld_output", "Build Output Folder", () =>
        {
            using (paper.Row("bld_output_row").Width(UnitValue.Stretch()).Height(UnitValue.Auto).Gap(4).Enter())
            {
                Origami.TextField(paper, "bld_output_v", OutputDirectory,
                        v => { OutputDirectory = v; EditorRegistries.SaveSettings(); })
                    .CommitOnEnter().Width(UnitValue.Stretch()).Show();
                Origami.IconButton(paper, "bld_output_browse", EditorIcons.FolderOpen_I, PickOutputFolder)
                    .Tooltip("Choose the folder the final build is written to").Show();
            }
        }, separator: false);

        EditorGUI.SettingsTextField(paper, "bld_exe", "Executable Name", ExecutableName, v => ExecutableName = v);
        EditorGUI.Note(paper, "bld_exe_hint", string.IsNullOrWhiteSpace(ExecutableName)
            ? $"Empty uses the product name: {ResolveExecutableName()}"
            : $"The build writes {ResolveExecutableName()}{(OperatingSystem.IsWindows() ? ".exe" : "")}.");

        EditorGUI.SettingsEnumDropdown(paper, "bld_packaging", "Asset Packaging", PackagingMode, v => PackagingMode = v);
        EditorGUI.SettingsEnumDropdown(paper, "bld_assetmode", "Asset Export", AssetMode, v => AssetMode = v);

        if (PackagingMode == AssetPackagingMode.ProwlPak)
            EditorGUI.SettingsIntSlider(paper, "bld_maxpak", "Max Pak Size (MB)", MaxPakSizeMB, 256, 4096, v => MaxPakSizeMB = v);

        paper.Box("bld_sp3").Height(12);

    }

    private void PickOutputFolder()
    {
        string start = Project.Current is { } project
            ? System.IO.Path.GetFullPath(System.IO.Path.Combine(project.RootPath,
                string.IsNullOrWhiteSpace(OutputDirectory) ? "." : OutputDirectory))
            : Project.Current?.RootPath;

        EditorApplication.OpenFileDialog(FileDialogMode.SelectFolder, path =>
        {
            if (path == null) return;

            if (Project.Current is { } current)
            {
                string relative = System.IO.Path.GetRelativePath(current.RootPath, path);
                OutputDirectory = !relative.StartsWith("..", StringComparison.Ordinal) && !System.IO.Path.IsPathRooted(relative)
                    ? relative
                    : path;
            }
            else OutputDirectory = path;
            EditorRegistries.SaveSettings();
        }, start);
    }

    public override void ResetToDefaults()
    {
        Scenes.Clear();
        ExecutableName = "";
        OutputDirectory = "Builds";
        PlatformProfiles.ForEach(p => p.ToDefault());
    }
}
