using System;
using System.Collections.Generic;

using Infinity.Editor.Build;
using Infinity.Editor.Core;
using Infinity.Editor.GUI;
using Infinity.Editor.Inspector;
using Infinity.Editor.Theming;
using Infinity.OrigamiUI;
using Infinity.PaperUI;

namespace Infinity.Editor.Projects.Settings;

/// <summary> Project settings for general project information such as company name, product name, version, and the last opened scene. </summary>
[ProjectSettings("General", EditorIcons.Gear, order: 0)]
public class GeneralSettings : ProjectSettingsBase
{
    public string CompanyName = "DefaultCompany";
    public string ProductName = "My Game";
    public string Version = "0.1.0";

    /// <summary>The scenes open when the project was last used, restored on project open.</summary>
    public List<GUI.SceneView.SceneSetup> LastSetup = [];

    /// <summary>The scenes hidden in the scene view, restored with the setup.</summary>
    public List<System.Guid> HiddenScenes = [];

    /// <summary>
    /// The application icon: a .png or a .ico inside the project. Builds copy it next to the executable, where the
    /// player window, the taskbar button and the desktop shortcut take it from. It never replaces the editor's own icon.
    /// </summary>
    public string AppIconPath = "";

    /// <summary>The icon as an absolute path, or null when none is set or the file is gone.</summary>
    public string? ResolvedIconPath
    {
        get
        {
            if (string.IsNullOrWhiteSpace(AppIconPath)) return null;
            if (System.IO.Path.IsPathRooted(AppIconPath)) return System.IO.File.Exists(AppIconPath) ? AppIconPath : null;
            if (Project.Current is not { } project) return null;

            string full = System.IO.Path.Combine(project.RootPath, AppIconPath);
            return System.IO.File.Exists(full) ? full : null;
        }
    }

    public override void Apply() => ApplyIcon();

    /// <summary>
    /// Writes the player's copy of the icon into the build output directory. The editor window deliberately keeps the
    /// engine icon: a project icon belongs to the game, not to the tool that builds it.
    /// </summary>
    public void ApplyIcon()
    {
        if (ResolvedIconPath is not { } source) return;

        if (Project.Current is not { } project) return;
        string output = System.IO.Path.Combine(project.RootPath, FindSettings<BuildSettings>()?.OutputDirectory ?? "Builds");
        AppIconExporter.TryExport(source, output, out _);
    }

    private static T? FindSettings<T>() where T : ProjectSettingsBase
    {
        foreach (EditorRegistries.SettingsEntry entry in EditorRegistries.SettingsEntries)
            if (entry.Instance is T typed) return typed;
        return null;
    }

    /// <summary> Draws the General settings panel in the Project Settings window. </summary>
    public override void OnGUI(Paper paper, float width)
    {
        Origami.Header(paper, "gen_header", $"{EditorIcons.Gear}  General").Underline().Show();

        EditorGUI.SettingsTextField(paper, "gen_company", "Company Name", CompanyName, v => CompanyName = v);
        EditorGUI.SettingsTextField(paper, "gen_product", "Product Name", ProductName, v => ProductName = v);
        EditorGUI.SettingsTextField(paper, "gen_version", "Version", Version, v => Version = v);

        DrawIconSettings(paper);
    }

    private void DrawIconSettings(Paper paper)
    {
        Scribe.FontFile? font = EditorTheme.DefaultFont;
        if (font == null) return;

        paper.Box("gen_icon_gap").Height(14);
        Origami.Header(paper, "gen_icon_header", $"{EditorIcons.Image}  Application Icon").Underline().Show();

        paper.Box("gen_icon_help").Height(30).PaddingLeft(4)
            .Text("A .png or .ico shown in the window, the taskbar and the desktop. Builds copy it next to the executable.",
                font).FontSize(EditorTheme.FontSizeSmall).TextColor(EditorTheme.Ink400)
            .Alignment(TextAlignment.Left).IsNotInteractable();

        using (paper.Row("gen_icon_row").Height(EditorTheme.RowHeight).Gap(6).PaddingLeft(4).Enter())
        {
            paper.Box("gen_icon_path").Height(EditorTheme.RowHeight).PaddingLeft(6)
                .Text(string.IsNullOrWhiteSpace(AppIconPath) ? "No icon chosen" : AppIconPath, font)
                .FontSize(EditorTheme.FontSizeSmall)
                .TextColor(string.IsNullOrWhiteSpace(AppIconPath) ? EditorTheme.Ink400 : EditorTheme.Ink200)
                .Alignment(TextAlignment.MiddleLeft).IsNotInteractable();

            Origami.Button(paper, "gen_icon_browse", $"{EditorIcons.FolderOpen}  Browse", PickIcon).Width(100).Show();

            if (!string.IsNullOrWhiteSpace(AppIconPath))
                Origami.Button(paper, "gen_icon_clear", "Clear", () => SetIcon("")).Width(60).Show();
        }

        if (IconStatus() is { Length: > 0 } status)
            paper.Box("gen_icon_status").Height(18).PaddingLeft(4).Text(status, font)
                .FontSize(EditorTheme.FontSizeSmall).TextColor(EditorTheme.Ink400)
                .Alignment(TextAlignment.Left).IsNotInteractable();
    }

    private string? IconStatus()
    {
        if (string.IsNullOrWhiteSpace(AppIconPath)) return null;
        if (ResolvedIconPath is not { } source) return "The file was not found.";

        (int Width, int Height)? size = AppIconExporter.SizeOf(source);
        return size is { } s ? $"Ready: {s.Width}x{s.Height}." : "The file is not an image this engine can read.";
    }

    private void PickIcon()
    {
        if (Project.Current is not { } project) return;

        EditorApplication.OpenFileDialog(FileDialogMode.Open, path =>
        {
            if (path == null) return;
            // Kept relative when it is inside the project, so it works on every machine that opens it.
            string relative = System.IO.Path.GetRelativePath(project.RootPath, path);
            SetIcon(relative.StartsWith("..") || System.IO.Path.IsPathRooted(relative) ? path : relative);
        }, project.RootPath, AppIconExporter.Extensions, ["Images"]);
    }

    private void SetIcon(string path)
    {
        AppIconPath = path;
        EditorRegistries.SaveSettings();
        ApplyIcon();
    }
}
