using System;
using System.Collections.Generic;
using System.IO;

using Infinity.Editor.Core;
using Infinity.Editor.Migration;
using Infinity.Editor.Projects;
using Infinity.Editor.Theming;
using Infinity.Editor.Utils;
using Infinity.OrigamiUI;
using Infinity.PaperUI;
using Infinity.PaperUI.LayoutEngine;
using Infinity.Quill;
using Infinity.Rosetta;
using Infinity.Runtime;
using Infinity.Runtime.Resources;
using Infinity.Vector;

using static Infinity.Editor.GUI.EditorGUI;

using Color = System.Drawing.Color;

namespace Infinity.Editor.GUI;

/// <summary>
/// Full-screen project launcher shown at startup before the editor loads.
/// Shows recent projects, and allows creating/opening projects.
/// </summary>
public static class ProjectLauncher
{
    /// <summary> Gets whether the project launcher is currently open. </summary>
    public static bool IsOpen { get; private set; } = true;

    private static string _newProjectName = "Untitled";
    private static string _newProjectPath = "";
    private static string _search = "";
    private static int _tab;   // 0 = Recent, 1 = New Project
    private enum ProjectTemplate { Empty3D, Empty2D }
    private static ProjectTemplate _template = ProjectTemplate.Empty3D;
    private static float _animTime;
    private static NebulaBackground? _nebula;

    // Favorite-star icon in both Font Awesome weights: outline when unfavored, filled when favored.
    // EditorGlyphIcon resolves the face at draw time, so building these before fonts load is fine.
    private static readonly IOrigamiIcon _starOutline = new EditorGlyphIcon(EditorIcons.Star, weight: GlyphWeight.Outline);
    private static readonly IOrigamiIcon _starSolid = new EditorGlyphIcon(EditorIcons.Star, weight: GlyphWeight.Solid);

    private const float TS = 1.4f;

    private static Color Col(int r, int g, int b, float a = 1f) => Color.FromArgb((int)Math.Round(a * 255), r, g, b);
    private static Color WinGlass => EditorTheme.WithAlpha(EditorTheme.Neutral300, 184);
    private static Color Raised => EditorTheme.WithAlpha(EditorTheme.Neutral400, 204);
    private static Color Bd => Color.FromArgb(33, EditorTheme.Accent.R, EditorTheme.Accent.G, EditorTheme.Accent.B);
    private static Color InputBd => Color.FromArgb(41, EditorTheme.Accent.R, EditorTheme.Accent.G, EditorTheme.Accent.B);
    private static Color CardBg => Col(255, 255, 255, 0.025f);
    private static OrigamiMetrics M => Origami.Current.Metrics;

    // Cycled tip strip drawn at the bottom of the launcher background.
    private static readonly string[] _tipKeys =
    [
        "launcher.tip.orbit",
        "launcher.tip.dolly",
        "launcher.tip.pan",
        "launcher.tip.fly",
        "launcher.tip.fly_speed",
        "launcher.tip.fly_updown",
        "launcher.tip.focus",
        "launcher.tip.gizmos",
        "launcher.tip.snap",
        "launcher.tip.duplicate",
        "launcher.tip.rename",
        "launcher.tip.math",
        "launcher.tip.math_ops",
        "launcher.tip.drag_asset",
        "launcher.tip.orient_cube",
        "launcher.tip.undo",
        "launcher.tip.discord",
        "launcher.tip.assignref",
        "launcher.tip.pingref",
        "launcher.tip.pickref",
        "launcher.tip.go_to_component",
        "launcher.tip.create_menu",
        "launcher.tip.component_menu",
        "launcher.tip.escape_unlock",
        "launcher.tip.nuget",
    ];

    private const float _tipDuration = 8f;
    private const float _tipFadeTime = 0.45f;
    private static int _tipIndex;
    private static float _tipTimer;

    private static readonly Dictionary<string, string> _versions = [];

    /// <summary> Sets the default new-project path and resets the launcher to its initial state. </summary>
    public static void Initialize()
    {
        _newProjectPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Infinity Engine Projects");
        IsOpen = true;
        Window.SetEngineIcon();
        _tab = 0;
        _animTime = 0;
        _tipIndex = Random.Shared.Next(_tipKeys.Length);
        _tipTimer = 0;
        _versions.Clear();
    }

    private static string VersionOf(string path)
    {
        if (!_versions.TryGetValue(path, out string? version))
            _versions[path] = version = Project.ReadVersion(path);
        return version;
    }

    private static void AdvanceTip()
    {
        _tipIndex = (_tipIndex + 1) % _tipKeys.Length;
        _tipTimer = 0;
    }

    /// <summary> Closes the project launcher. </summary>
    public static void Close()
    {
        IsOpen = false;
    }

    /// <summary> Draws the project launcher window and its contents. Has no effect if IsOpen is false and forceDraw is false. </summary>
    public static void Draw(Paper paper, float dt, bool forceDraw = false)
    {
        if (!IsOpen && !forceDraw) return;
        Scribe.FontFile? font = EditorTheme.DefaultFont;
        if (font == null) return;

        _animTime += dt;

        float w = paper.ScreenRect.Size.X;
        float h = paper.ScreenRect.Size.Y;

        // Animated Nebula backdrop.
        _nebula ??= new NebulaBackground(paper);
        NebulaBackground.DrawEditorBackground(paper, _nebula, "pl_bg", w, h, dt, showStarsAndComets: true);

        using (paper.Box("pl_container").PositionType(PositionType.SelfDirected).Position(0, 0).Size(w, h).Enter())
        using (paper.Column("pl_window")
            .Width(940).Height(620)
            .Margin(UnitValue.StretchOne, UnitValue.StretchOne, UnitValue.StretchOne, UnitValue.StretchOne)
            .BackgroundColor(WinGlass)
            .BackdropBlur(22)
            .BorderColor(Bd).BorderWidth(1)
            .Rounded(M.ContainerRounding)
            .DropShadow(0, 16, 44, -8, Col(0, 0, 0, 0.9f))
            .Clip()
            .Enter())
        {
            Header(paper, font);
            EditorGUI.Divider(paper, "pl_head_div");
            if (_tab == 0) RecentBody(paper, font);
            else if (_tab == 1) NewProjectBody(paper, font);
            else if (_tab == 2) NewsBody(paper, font);
            else EngineBody(paper, font);
        }
    }

    // ---- Header (brand + tab pills) ---------------------------------
    private static void Header(Paper P, Scribe.FontFile font)
    {
        Scribe.FontFile display = EditorTheme.FontDisplay ?? EditorTheme.DefaultBoldFont ?? font;

        using (P.Row("pl_header").Height(64).Padding(26, 26, 0, 0).Enter())
        {
            using (P.Box("pl_logo").Width(60).Height(60).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne).Enter())
                P.Draw((vg, r) => EditorIcons.InfinityProject.Draw(vg, r, Color.White, 1f));

            // Wordmark scaled up to fill the header height (version line removed), vertically centered.
            P.Box("pl_prowl").Width(UnitValue.Auto).Height(UnitValue.Auto).Margin(13, 0, UnitValue.StretchOne, UnitValue.StretchOne)
                .Text("INFINITY ENGINE", EditorTheme.FontLogo ?? display).FontSize(21f * TS).LetterSpacing(3f).TextColor(EditorTheme.Ink500).Alignment(TextAlignment.MiddleLeft);

            P.Box("pl_hspacer");

            using (P.Row("pl_lang_wrap").Width(UnitValue.Auto).Height(34).Margin(0, 10, UnitValue.StretchOne, UnitValue.StretchOne).Enter())
                LanguageDropdown(P, font);

            using (P.Row("pl_tabs").Width(UnitValue.Auto).Height(34).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne)
                .BackgroundColor(EditorTheme.Glass).BorderColor(InputBd).BorderWidth(1).Rounded(M.ContainerRounding).Enter())
            {
                TabChip(P, Loc.Get("launcher.tab_recent"), 0);
                TabChip(P, Loc.Get("launcher.new_project"), 1);
                TabChip(P, Loc.Get("launcher.tab_news"), 2);
                TabChip(P, Loc.Get("launcher.tab_engine"), 3);
            }
        }
    }

    // Language picker: globe + name + region chip + chevron trigger; rows show chip + name + a
    // checkmark on the current locale.
    private static void LanguageDropdown(Paper P, Scribe.FontFile font)
    {
        int cur = LocaleHelper.GetIndex(EditorSettings.Instance.Locale);
        var globe = new FontIcon(font, EditorIcons.Globe);

        int[] items = new int[LocaleHelper.Codes.Length];
        for (int i = 0; i < items.Length; i++) items[i] = i;

        Origami.Dropdown<int>(P, "pl_lang", cur, LocaleHelper.SetLocale, items)
            .Width(UnitValue.Auto).Height(34)
            .PopoverWidth(210).ItemHeight(30)
            .CustomTrigger(ctx =>
            {
                P.Box("pl_lang_ic").Width(20).Margin(12, 8, UnitValue.StretchOne, UnitValue.StretchOne).IsNotInteractable()
                    .Icon(P, globe, EditorTheme.Ink300, size: 15f);
                P.Box("pl_lang_nm").Width(UnitValue.Auto).Margin(0, 8, UnitValue.StretchOne, UnitValue.StretchOne).IsNotInteractable()
                    .Text(LocaleHelper.Names[cur], font).FontSize(13f * TS).TextColor(EditorTheme.Ink500).Alignment(TextAlignment.MiddleLeft);
                LangChip(P, "pl_lang_tag", LocaleHelper.Tags[cur], font);
                P.Box("pl_lang_chev").Width(16).Margin(6, 12, UnitValue.StretchOne, UnitValue.StretchOne).IsNotInteractable()
                    .Icon(P, new FontIcon(font, ctx.IsOpen ? EditorIcons.ChevronUp : EditorIcons.ChevronDown), EditorTheme.Ink300, size: 10f);
            })
            .ItemRender((i, ctx) =>
            {
                LangChip(P, $"pl_lang_r{i}_tag", LocaleHelper.Tags[i], font);
                P.Box($"pl_lang_r{i}_nm").IsNotInteractable()
                    .Text(LocaleHelper.Names[i], font).FontSize(12.5f * TS)
                    .TextColor(ctx.IsSelected ? EditorTheme.Ink500 : EditorTheme.Ink300).Alignment(TextAlignment.MiddleLeft);
                if (ctx.IsSelected)
                    P.Box($"pl_lang_r{i}_ck").Width(16).IsNotInteractable()
                        .Icon(P, new FontIcon(font, EditorIcons.Check), EditorTheme.Accent, size: 11f);
            })
            .Show();
    }

    // Small pill showing a language's region tag (e.g. "US"), sized to its text.
    private static void LangChip(Paper P, string id, string text, Scribe.FontFile font)
    {
        P.Box(id).Width(UnitValue.Auto).Height(17).Rounded(M.SmallRounding).Margin(5, 5, UnitValue.StretchOne, UnitValue.StretchOne).Padding(6, 6, 0, 0)
            .BackgroundColor(Col(255, 255, 255, 0.06f)).BorderColor(EditorTheme.BorderSoft).BorderWidth(1)
            .IsNotInteractable()
            .Text(text, font).FontSize(9.5f * TS).TextColor(EditorTheme.Ink300).Alignment(TextAlignment.MiddleCenter);
    }

    private static void TabChip(Paper P, string label, int index)
    {
        bool on = _tab == index;
        float leftM = index == 0 ? 3 : 4;
        float rightM = index == 1 ? 3 : 0;
        P.Box("pl_tab" + index).Width(UnitValue.Auto).Height(28).Rounded(M.Rounding)
            .Margin(leftM, rightM, UnitValue.StretchOne, UnitValue.StretchOne).Padding(14, 14, 0, 0)
            .BackgroundColor(on ? EditorTheme.Accent : Color.Transparent)
            .Text(label, EditorTheme.FontMedium ?? EditorTheme.DefaultFont)
            .FontSize(13f * TS).TextColor(on ? EditorTheme.Ink700 : EditorTheme.Ink300).Alignment(TextAlignment.MiddleCenter)
            .OnClick(_ => _tab = index);
    }

    // ---- Recent tab -------------------------------------------------
    private static void RecentBody(Paper P, Scribe.FontFile font)
    {
        using (P.Column("pl_recent").Enter())
        {
            using (P.Row("pl_rbar").Height(58).Enter())
            {
                P.Box("pl_rtitle").Height(UnitValue.Auto).Margin(26, 0, UnitValue.StretchOne, UnitValue.StretchOne)
                    .Text(Loc.Get("launcher.recent_projects"), EditorTheme.FontSemiBold ?? font).FontSize(18f * TS).TextColor(EditorTheme.Ink500).Alignment(TextAlignment.MiddleLeft);

                using (P.Box("pl_search_wrap").Width(220).Height(34).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne).Enter())
                    Origami.TextField(P, "pl_search", _search, v => _search = v)
                        .Search(Loc.Get("launcher.search"))
                        .Height(34).Show();

                using (P.Row("pl_open").Width(UnitValue.Auto).Height(34).Rounded(M.Rounding).Margin(9, 26, UnitValue.StretchOne, UnitValue.StretchOne)
                    .BackgroundColor(EditorTheme.Glass).BorderColor(InputBd).BorderWidth(1)
                    .Hovered.BorderColor(EditorTheme.BorderStrong).End()
                    .OnClick(_ => OpenProjectDialog())
                    .Enter())
                {
                    IconBox(P, "pl_oico", EditorIcons.FolderOpen_I, 15, EditorTheme.Ink300, 1.4f, 12);
                    P.Box("pl_otxt").Width(UnitValue.Auto).Height(UnitValue.Auto).Margin(7, 13, UnitValue.StretchOne, UnitValue.StretchOne)
                        .Text(Loc.Get("launcher.open"), font).FontSize(13 * TS).TextColor(EditorTheme.Ink300).Alignment(TextAlignment.MiddleLeft);
                }
            }

            List<RecentProjectEntry> entries = RecentProjects.FavoritesFirst();
            bool searching = !string.IsNullOrWhiteSpace(_search);
            if (searching)
            {
                string q = _search.Trim();
                entries = entries.FindAll(e =>
                    e.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    e.Path.Contains(q, StringComparison.OrdinalIgnoreCase));
            }

            Origami.ScrollView(P, "pl_cards_sv", 938, 555 - 58).Padding(22, 22, 4, 22).ColSpacing(7)
                .SmoothScroll(false).Body(viewport =>
            {
                if (entries.Count == 0)
                {
                    P.Box("pl_empty").Height(120)
                        .Text(Loc.Get(searching ? "launcher.no_results" : "launcher.no_recent"), font).FontSize(14f * TS).TextColor(EditorTheme.Ink300).Alignment(TextAlignment.MiddleCenter);
                    return;
                }

                // Cards are 70 px plus the 7 px column gap. Render only the visible slice so a long
                // recent-projects list costs the same as a short one.
                const float stride = 77f;
                int first = Math.Max(0, (int)MathF.Floor(viewport.ScrollY / stride));
                int visible = (int)MathF.Ceiling(viewport.Height / stride) + 1;
                int last = Math.Min(entries.Count - 1, first + visible);

                if (first > 0)
                    P.Box("pl_cards_top").Height(first * stride).Width(1).IsNotInteractable();
                for (int i = first; i <= last; i++)
                    Card(P, font, entries[i], i);
                int bottom = entries.Count - 1 - last;
                if (bottom > 0)
                    P.Box("pl_cards_bottom").Height(bottom * stride).Width(1).IsNotInteractable();
            });
        }
    }

    private static void Card(Paper P, Scribe.FontFile font, RecentProjectEntry entry, int i)
    {
        bool exists = Directory.Exists(entry.Path);
        Scribe.FontFile mono = EditorTheme.FontMono ?? font;

        using (P.Row("pl_card" + i).Height(70).Rounded(M.ContainerRounding)
            .BackgroundColor(CardBg).BorderColor(EditorTheme.BorderSoft).BorderWidth(1)
            .Transition(GuiProp.BackgroundColor, 0.15f)
            .Transition(GuiProp.BorderColor, 0.15f)
            .Transition(GuiProp.TranslateX, 0.15f)
            .Transition(GuiProp.BoxShadow, 0.2f)
            .Hovered
                .BackgroundColor(EditorTheme.Hover)
                .BorderColor(EditorTheme.BorderStrong)
                .Translate(3, 0)
                .Glow(0, 10, 26, -6, Color.FromArgb(140, EditorTheme.Purple400))
                .End()
            .OnClick(entry, (e, _) => { if (Directory.Exists(e.Path)) TryOpenProject(e.Path); })
            .Enter())
        {
            RecentCardContextMenu(P, "pl_cardctx" + i, entry, exists);

            // Favorite toggle: outline star normally, filled yellow when favored. Drawn with the FA
            // weight directly (solid vs regular) because the fallback chain otherwise always resolves
            // the star to its outline. No background unless hovered; StopEventPropagation keeps the
            // click off the card's open-project handler.
            bool fav = entry.Favorite;
            Color amber = EditorTheme.Amber400;
            using (P.Box("pl_fav" + i).Width(44).Height(44).Rounded(M.Rounding).Margin(16, 0, UnitValue.StretchOne, UnitValue.StretchOne)
                .Hovered.BackgroundColor(Col(255, 255, 255, 0.08f)).End()
                .StopEventPropagation()
                .OnClick(entry, (e, _) => RecentProjects.SetFavorite(e.Path, !e.Favorite))
                .Enter())
                P.Draw((vg, r) => DrawIcon(vg, r, fav ? _starSolid : _starOutline, 22, fav ? amber : Color.White, 1.5f));

            using (P.Column("pl_ci" + i).Height(UnitValue.Auto).Margin(15, 0, UnitValue.StretchOne, UnitValue.StretchOne).Enter())
            {
                using (P.Row("pl_cn" + i).Height(UnitValue.Auto).Enter())
                {
                    P.Box("pl_cname" + i).Width(UnitValue.Auto).Height(UnitValue.Auto).Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne)
                        .Text(entry.Name, EditorTheme.FontSemiBold ?? font).FontSize(15 * TS).TextColor(exists ? EditorTheme.Ink500 : EditorTheme.Ink300).Alignment(TextAlignment.MiddleLeft);

                    // The version the project was saved with, amber when opening it migrates it (or a "missing" chip when the folder is gone).
                    if (exists)
                    {
                        string version = VersionOf(entry.Path);
                        bool current = EngineVersion.TryParse(version, out EngineVersion parsed) && parsed == Project.CurrentVersion;
                        P.Box("pl_cver" + i).Width(UnitValue.Auto).Height(UnitValue.Auto).Rounded(M.SmallRounding).Margin(9, 0, UnitValue.StretchOne, UnitValue.StretchOne).Padding(7, 7, 3, 3)
                            .BackgroundColor(current ? Raised : Color.FromArgb(90, EditorTheme.Amber400))
                            .Text(version.Length > 0 ? version : Loc.Get("launcher.version_unknown"), mono).FontSize(10 * TS)
                            .TextColor(current ? EditorTheme.Ink300 : EditorTheme.Amber400).Alignment(TextAlignment.MiddleCenter);
                    }
                    else
                        P.Box("pl_cmiss" + i).Width(UnitValue.Auto).Height(UnitValue.Auto).Rounded(M.SmallRounding).Margin(9, 0, UnitValue.StretchOne, UnitValue.StretchOne).Padding(7, 7, 3, 3)
                            .BackgroundColor(Color.FromArgb(128, EditorTheme.Red300))
                            .Text(Loc.Get("launcher.missing"), mono).FontSize(10 * TS).TextColor(EditorTheme.Red400).Alignment(TextAlignment.MiddleCenter);

                    P.Box("pl_cnsp" + i);
                }

                P.Box("pl_cp" + i).Height(UnitValue.Auto).Margin(0, 0, 5, 0)
                    .Text(entry.Path, mono).FontSize(11.5f * TS).TextColor(EditorTheme.InkDim).Alignment(TextAlignment.MiddleLeft);
            }

            P.Box("pl_ct" + i).Width(UnitValue.Auto).Height(UnitValue.Auto).Margin(8, 0, UnitValue.StretchOne, UnitValue.StretchOne)
                .Text(FormatTimeAgo(entry.LastOpened), font).FontSize(12 * TS).TextColor(EditorTheme.Ink300).Alignment(TextAlignment.MiddleRight);

            // Arrow, revealed on hover
            Color arrowCol = Color.FromArgb(P.IsParentHovered ? 255 : 0, EditorTheme.AccentText.R, EditorTheme.AccentText.G, EditorTheme.AccentText.B);
            using (P.Box("pl_carr" + i).Width(18).Height(18).Margin(10, 16, UnitValue.StretchOne, UnitValue.StretchOne).IsNotInteractable().Enter())
                P.Draw((vg, r) => DrawIcon(vg, r, EditorIcons.ArrowRight_I, 17, arrowCol, 1.6f));
        }
    }

    private static void RecentCardContextMenu(Paper P, string id, RecentProjectEntry entry, bool exists)
    {
        Origami.RightClickMenu(P, id, b =>
        {
            if (exists)
            {
                b.Item(Loc.Get("launcher.open"), () => TryOpenProject(entry.Path), icon: EditorIcons.FolderOpen);
                b.Item(Loc.Get("project.show_in_explorer"), () => EditorUtils.OpenFileSystemPath(entry.Path), icon: EditorIcons.FolderTree);
                b.Separator();
            }
            b.Item(Loc.Get("project.copy_path"), () => P.SetClipboard(entry.Path), icon: EditorIcons.Copy);
            b.Item(Loc.Get("launcher.remove_recent"), () => RecentProjects.Remove(entry.Path), icon: EditorIcons.Xmark);
            if (exists)
            {
                b.Separator();
                b.Item(Loc.Get("launcher.delete_project"), () => Origami.Confirm(
                    Loc.Get("launcher.delete_confirm_title"),
                    Loc.Get("launcher.delete_confirm_body", new { name = entry.Name }),
                    () =>
                    {
                        try
                        {
                            if (Directory.Exists(entry.Path))
                                Directory.Delete(entry.Path, true);
                            RecentProjects.Remove(entry.Path);
                        }
                        catch (Exception ex) { Runtime.Debug.LogError($"Failed to delete project: {ex.Message}"); }
                    }), icon: EditorIcons.Trash, danger: true);
            }
        });
    }

    // ---- News tab (UE5-style launcher dashboard) --------------------
    private static void NewsBody(Paper P, Scribe.FontFile font)
    {
        Origami.ScrollView(P, "pl_news_sv", 938, 555).Padding(22, 22, 12, 22).ColSpacing(8).SmoothScroll(false).Body(() =>
        {
            using (P.Column("pl_news").Width(UnitValue.Stretch()).Height(UnitValue.Auto).Gap(10).Enter())
            {
                using (P.Column("pl_news_hero").Width(UnitValue.Stretch()).Height(128).Padding(22, 22, 18, 18)
                    .BackgroundLinearGradient(0, 0, 1, 1, Col(92, 66, 150), Col(36, 28, 72))
                    .BorderColor(EditorTheme.BorderStrong).BorderWidth(1).Rounded(M.ContainerRounding).Enter())
                {
                    P.Box("pl_news_kicker").Height(UnitValue.Auto).Text(Loc.Get("launcher.news.kicker"), EditorTheme.FontSemiBold ?? font)
                        .FontSize(11 * TS).LetterSpacing(2f).TextColor(Col(220, 212, 255));
                    P.Box("pl_news_title").Height(UnitValue.Auto).Margin(0, 0, 0, 4)
                        .Text(Loc.Get("launcher.news.title"), EditorTheme.FontLogo ?? EditorTheme.FontDisplay ?? font)
                        .FontSize(24 * TS).TextColor(EditorTheme.Ink500);
                    P.Box("pl_news_sub").Height(UnitValue.Auto)
                        .Text(Loc.Get("launcher.news.subtitle"), font).FontSize(13 * TS).TextColor(Col(224, 218, 244));
                }

                P.Box("pl_news_head").Height(UnitValue.Auto).Margin(2, 0, 0, 0)
                    .Text(Loc.Get("launcher.news.heading"), EditorTheme.FontSemiBold ?? font)
                    .FontSize(17 * TS).TextColor(EditorTheme.Ink500);

                using (P.Row("pl_news_cards").Width(UnitValue.Stretch()).Height(210).Gap(10).Enter())
                {
                    NewsCard(P, font, "mp", EditorIcons.Users_I, Loc.Get("launcher.news.multiprogram_title"), Loc.Get("launcher.news.multiprogram_desc"));
                    NewsCard(P, font, "2d", EditorIcons.Image_I, Loc.Get("launcher.news.twod_title"), Loc.Get("launcher.news.twod_desc"));
                    NewsCard(P, font, "api", EditorIcons.Code_I, Loc.Get("launcher.news.api_title"), Loc.Get("launcher.news.api_desc"));
                }

                P.Box("pl_news_foot").Height(UnitValue.Auto).Margin(2, 4, 0, 0)
                    .Text(Loc.Get("launcher.news.footer"), font).FontSize(11.5f * TS).TextColor(EditorTheme.InkDim);
            }
        });
    }

    private static void NewsCard(Paper P, Scribe.FontFile font, string id, IOrigamiIcon icon, string title, string text)
    {
        using (P.Column("pl_news_card_" + id).Width(UnitValue.Stretch()).Height(UnitValue.Stretch())
            .Padding(17, 17, 15, 15).Gap(8)
            .BackgroundColor(Col(255, 255, 255, 0.025f)).BorderColor(EditorTheme.BorderSoft).BorderWidth(1)
            .Rounded(M.ContainerRounding).Enter())
        {
            using (P.Box("pl_news_ico_" + id).Width(36).Height(36).Rounded(M.Rounding)
                .BackgroundColor(Col(120, 96, 190, 0.35f)).Enter())
                P.Draw((vg, r) => DrawIcon(vg, r, icon, 19, EditorTheme.Ink500, 1.3f));
            P.Box("pl_news_t_" + id).Height(UnitValue.Auto).Text(title, EditorTheme.FontSemiBold ?? font)
                .FontSize(14 * TS).TextColor(EditorTheme.Ink500);
            P.Box("pl_news_d_" + id).Height(UnitValue.Auto).Text(text, font)
                .FontSize(11.8f * TS).TextColor(EditorTheme.Ink300).Wrap(Scribe.TextWrapMode.Wrap);
        }
    }

    // ---- Engine settings tab -----------------------------------------
    private static void EngineBody(Paper P, Scribe.FontFile font)
    {
        EditorSettings settings = EditorSettings.Instance;
        Origami.ScrollView(P, "pl_engine_sv", 938, 555).Padding(22, 22, 14, 22).SmoothScroll(false).Body(() =>
        {
            using (P.Column("pl_engine").Width(UnitValue.Stretch()).Height(UnitValue.Auto).Gap(10).Enter())
            {
                P.Box("pl_engine_head").Height(UnitValue.Auto)
                    .Text(Loc.Get("launcher.engine.title"), EditorTheme.FontSemiBold ?? font)
                    .FontSize(19 * TS).TextColor(EditorTheme.Ink500);

                EngineInfoCard(P, font, "version", Loc.Get("launcher.engine.version"), $"Infinity Engine {Project.CurrentVersion}");
                EngineInfoCard(P, font, "locale", Loc.Get("launcher.engine.language"), LocaleHelper.Names[LocaleHelper.GetIndex(settings.Locale)]);

                using (P.Row("pl_engine_row1").Width(UnitValue.Stretch()).Height(72).Gap(10).Enter())
                {
                    using (P.Column("pl_engine_lang").Width(UnitValue.Stretch()).Height(UnitValue.Stretch()).Padding(16, 16, 10, 10)
                        .BackgroundColor(Col(255, 255, 255, 0.025f)).BorderColor(EditorTheme.BorderSoft).BorderWidth(1)
                        .Rounded(M.ContainerRounding).Enter())
                    {
                        P.Box("pl_engine_lang_lbl").Height(UnitValue.Auto).Text(Loc.Get("launcher.engine.language"), font)
                            .FontSize(12 * TS).TextColor(EditorTheme.Ink300);
                        Origami.Dropdown(P, "pl_engine_lang_dd", LocaleHelper.GetIndex(settings.Locale), LocaleHelper.SetLocale, LocaleHelper.Names)
                            .Width(UnitValue.Stretch()).Height(34).Show();
                    }
                    EngineInfoCard(P, font, "autosave", Loc.Get("launcher.engine.autosave"), settings.AutoSave ? Loc.Get("launcher.engine.on") : Loc.Get("launcher.engine.off"));
                }

                P.Box("pl_engine_help").Height(UnitValue.Auto).Margin(2, 6, 0, 0)
                    .Text(Loc.Get("launcher.engine.help"), font).FontSize(11.8f * TS).TextColor(EditorTheme.InkDim).Wrap(Scribe.TextWrapMode.Wrap);
            }
        });
    }

    private static void EngineInfoCard(Paper P, Scribe.FontFile font, string id, string label, string value)
    {
        using (P.Column("pl_engine_card_" + id).Width(UnitValue.Stretch()).Height(72).Padding(16, 16, 10, 10)
            .BackgroundColor(Col(255, 255, 255, 0.025f)).BorderColor(EditorTheme.BorderSoft).BorderWidth(1)
            .Rounded(M.ContainerRounding).Enter())
        {
            P.Box("pl_engine_lbl_" + id).Height(UnitValue.Auto).Text(label, font)
                .FontSize(11.5f * TS).TextColor(EditorTheme.InkDim);
            P.Box("pl_engine_val_" + id).Height(UnitValue.Auto).Text(value, EditorTheme.FontSemiBold ?? font)
                .FontSize(15 * TS).TextColor(EditorTheme.Ink500);
        }
    }

    // ---- New Project tab --------------------------------------------
    private static void NewProjectBody(Paper P, Scribe.FontFile font)
    {
        using (P.Row("pl_new").Enter())
        {
            using (P.Column("pl_tplcol").Width(UnitValue.Stretch()).Padding(26, 26, 20, 20).Enter())
            {
                P.Box("pl_tplhead").Height(UnitValue.Auto).Margin(0, 0, 0, 16)
                    .Text(Loc.Get("launcher.choose_template"), EditorTheme.FontSemiBold ?? font).FontSize(18f * TS).TextColor(EditorTheme.Ink500).Alignment(TextAlignment.MiddleLeft);

                using (P.Row("pl_tplrow0").Width(UnitValue.Stretch()).Height(134).Enter())
                {
                    TemplateCard(P, font, 0, ProjectTemplate.Empty3D, "launcher.tpl_simple3d_name", "launcher.tpl_simple3d_desc", EditorIcons.Cube_I, 212.0);
                    TemplateCard(P, font, 1, ProjectTemplate.Empty2D, "launcher.tpl_simple2d_name", "launcher.tpl_simple2d_desc", EditorIcons.Image_I, 292.0);
                }
            }

            using (P.Column("pl_cfg").Width(308).Padding(24, 24, 24, 24)
                .BackgroundColor(Col(0, 0, 0, 0.18f)).BorderColor(EditorTheme.BorderSoft).BorderWidth(1).Enter())
            {
                P.Box("pl_cfghead").Height(UnitValue.Auto).Margin(0, 0, 0, 14)
                    .Text(Loc.Get("launcher.configure"), EditorTheme.FontSemiBold ?? font).FontSize(11f * TS).LetterSpacing(1f).TextColor(EditorTheme.InkDim).Alignment(TextAlignment.MiddleLeft);

                P.Box("pl_nmlbl").Height(UnitValue.Auto).Margin(0, 0, 0, 7)
                    .Text(Loc.Get("launcher.name"), font).FontSize(12f * TS).TextColor(EditorTheme.Ink300).Alignment(TextAlignment.MiddleLeft);
                Origami.TextField(P, "pl_nm", _newProjectName, v => _newProjectName = v)
                    .Height(38).Show();

                P.Box("pl_loclbl").Height(UnitValue.Auto).Margin(0, 0, 16, 7)
                    .Text(Loc.Get("launcher.path"), font).FontSize(12f * TS).TextColor(EditorTheme.Ink300).Alignment(TextAlignment.MiddleLeft);
                Origami.TextField(P, "pl_loc", _newProjectPath, v => _newProjectPath = v)
                    .Height(38).Mono()
                    .TrailingContent(() =>
                    {
                        using (P.Box("pl_locbtn").Width(24).Height(24).Margin(2, 6, UnitValue.StretchOne, UnitValue.StretchOne).Rounded(M.SmallRounding)
                            .Hovered.BackgroundColor(Col(255, 255, 255, 0.07f)).End()
                            .OnClick(_ => BrowseLocation())
                            .Enter())
                            P.Draw((vg, r) => DrawIcon(vg, r, EditorIcons.FolderOpen_I, 14, EditorTheme.Ink300, 1.4f));
                    })
                    .Show();

                P.Box("pl_cfgsp");

                using (P.Row("pl_cta").Height(44).Rounded(M.Rounding)
                    .BackgroundLinearGradient(0, 0, 1, 1, EditorTheme.Accent, EditorTheme.AccentBright)
                    .Glow(0, 8, 26, -4, Color.FromArgb(153, EditorTheme.Purple400))
                    .OnClick(_ => TryCreateProject())
                    .Enter())
                {
                    P.Box("pl_ctal");
                    IconBox(P, "pl_ctaico", EditorIcons.Bolt_I, 16, EditorTheme.Ink700, 1.4f, 0);
                    P.Box("pl_ctatxt").Width(UnitValue.Auto).Height(UnitValue.Auto).Margin(8, 0, UnitValue.StretchOne, UnitValue.StretchOne)
                        .Text(Loc.Get("launcher.create_project"), EditorTheme.FontSemiBold ?? font).FontSize(14f * TS).TextColor(EditorTheme.Ink700).Alignment(TextAlignment.MiddleLeft);
                    P.Box("pl_ctar");
                }
            }
        }
    }

    // The template cards. Selecting one only records the choice; TryCreateProject seeds the project from it.
    private static void TemplateCard(Paper P, Scribe.FontFile font, int col, ProjectTemplate template,
        string nameKey, string descKey, IOrigamiIcon icon, double hue)
    {
        bool selected = _template == template;
        (Color c1, Color c2) = GlyphColors(hue, true);
        using (P.Column("pl_tpl" + (int)template).Width(UnitValue.Stretch()).Height(UnitValue.Stretch())
            .Margin(col == 0 ? 0 : 6, col == 1 ? 0 : 6, 0, 0)
            .Rounded(M.ContainerRounding).Padding(17, 17, 17, 17)
            .BackgroundColor(selected ? EditorTheme.Selected : Col(255, 255, 255, 0.015f))
            .BorderColor(selected ? EditorTheme.Accent : EditorTheme.BorderSoft).BorderWidth(selected ? 1.5f : 1f)
            .OnClick(_ => _template = template)
            .Cursor(PaperCursor.Pointer)
            .Enter())
        {
            using (P.Box("pl_tplico" + (int)template).Width(46).Height(46).Rounded(M.Rounding).Margin(0, 0, 0, 11)
                .BackgroundLinearGradient(0, 0, 1, 1, c1, c2).Enter())
                P.Draw((vg, r) => DrawIcon(vg, r, icon, 24, EditorTheme.Ink700, 1.3f));

            P.Box("pl_tpln" + (int)template).Height(UnitValue.Auto).Margin(0, 0, 0, 4)
                .Text(Loc.Get(nameKey), EditorTheme.FontSemiBold ?? font).FontSize(15f * TS).TextColor(EditorTheme.Ink500).Alignment(TextAlignment.MiddleLeft);
            P.Box("pl_tpld" + (int)template).Height(UnitValue.Auto)
                .Text(Loc.Get(descKey), font).FontSize(12f * TS).TextColor(EditorTheme.Ink300).Alignment(TextAlignment.MiddleLeft);
        }
    }

    // ---- helpers ----------------------------------------------------
    private static void IconBox(Paper P, string id, IOrigamiIcon icon, float size, Color color, float sw, float leftM, float rightM = 0)
    {
        using (P.Box(id).Width(size).Height(size).Margin(leftM, rightM, UnitValue.StretchOne, UnitValue.StretchOne).Enter())
            P.Draw((vg, r) => icon.Draw(vg, r, color, sw));
    }

    private static void DrawIcon(Canvas vg, Rect box, IOrigamiIcon icon, float size, Color color, float sw)
    {
        float ox = (float)(box.Min.X + (box.Size.X - size) / 2);
        float oy = (float)(box.Min.Y + (box.Size.Y - size) / 2);
        icon.Draw(vg, new Rect(new Float2(ox, oy), new Float2(ox + size, oy + size)), color, sw);
    }


    private static double HueFor(string name)
    {
        int hash = 0;
        foreach (char ch in name) hash = hash * 31 + ch;
        return ((hash % 360) + 360) % 360;
    }

    private static (Color, Color) GlyphColors(double hue, bool mono)
    {
        if (mono) return (Col(58, 53, 80), Col(36, 31, 56));
        return (Hsl(hue, 0.70, 0.64), Hsl(hue + 28, 0.62, 0.50));
    }

    private static Color Hsl(double h, double s, double l)
    {
        h = ((h % 360) + 360) % 360 / 360.0;
        double r, g, b;
        if (s == 0) { r = g = b = l; }
        else
        {
            double q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            double p = 2 * l - q;
            r = Hue2(p, q, h + 1.0 / 3); g = Hue2(p, q, h); b = Hue2(p, q, h - 1.0 / 3);
        }
        return Col((int)Math.Round(r * 255), (int)Math.Round(g * 255), (int)Math.Round(b * 255));
    }
    private static double Hue2(double p, double q, double t)
    {
        if (t < 0) t += 1; if (t > 1) t -= 1;
        if (t < 1.0 / 6) return p + (q - p) * 6 * t;
        if (t < 1.0 / 2) return q;
        if (t < 2.0 / 3) return p + (q - p) * (2.0 / 3 - t) * 6;
        return p;
    }

    private static void OpenProjectDialog()
    {
        EditorApplication.OpenFileDialog(FileDialogMode.SelectFolder, path =>
        {
            if (path != null) TryOpenProject(path);
        });
    }

    private static void BrowseLocation()
    {
        EditorApplication.OpenFileDialog(FileDialogMode.SelectFolder, path =>
        {
            if (path != null) _newProjectPath = path;
        }, _newProjectPath);
    }

    /// <summary>
    /// Draws the cycling tip strip at the bottom of the screen. Called separately from
    /// <see cref="Draw"/> so it can render on top of the intro animation while the project loads.
    /// </summary>
    public static void DrawTipStrip(Paper paper, float dt, float globalAlpha = 1f)
    {
        if (globalAlpha <= 0f) return;

        Scribe.FontFile? font = EditorTheme.DefaultFont;
        if (font == null) return;

        float w = paper.ScreenRect.Size.X;
        float h = paper.ScreenRect.Size.Y;

        _tipTimer += dt;
        if (_tipTimer >= _tipDuration)
            AdvanceTip();

        float fadeIn = Math.Min(_tipTimer / _tipFadeTime, 1f);
        float fadeOut = Math.Min((_tipDuration - _tipTimer) / _tipFadeTime, 1f);
        float alpha = Math.Clamp(Math.Min(fadeIn, fadeOut), 0f, 1f) * Math.Clamp(globalAlpha, 0f, 1f);

        Color iconBase = EditorTheme.Purple500;
        Color textBase = EditorTheme.Ink400;
        Color labelBase = EditorTheme.Ink300;
        var iconColor = Color.FromArgb((int)(iconBase.A * alpha), iconBase.R, iconBase.G, iconBase.B);
        var textColor = Color.FromArgb((int)(textBase.A * alpha), textBase.R, textBase.G, textBase.B);
        var labelColor = Color.FromArgb((int)(labelBase.A * alpha), labelBase.R, labelBase.G, labelBase.B);

        const float stripHeight = 36f;
        float y = h - stripHeight - 16f;
        string tipText = Loc.Get(_tipKeys[_tipIndex]);

        using (paper.Row("pl_tip_strip")
            .PositionType(PositionType.SelfDirected)
            .Position(0, y)
            .Size(w, stripHeight)
            .JustifyContent(LayoutJustification.Center)

            .Gap(6)
            .OnClick(_ => AdvanceTip())
            .Enter())
        {
            paper.Box("pl_tip_icon")
                .Width(20)
                .Height(stripHeight)
                .Text(EditorIcons.Lightbulb, font)
                .TextColor(iconColor)
                .FontSize(EditorTheme.FontSize)
                .Alignment(TextAlignment.MiddleCenter);

            paper.Box("pl_tip_label")
                .Width(UnitValue.Auto)
                .Height(stripHeight)
                .Text(Loc.Get("launcher.tip_label"), font)
                .TextColor(labelColor)
                .FontSize(EditorTheme.FontSizeSmall)
                .Alignment(TextAlignment.MiddleLeft);

            paper.Box("pl_tip_text")
                .Width(UnitValue.Auto)
                .Height(stripHeight)
                .Text(tipText, font)
                .TextColor(textColor)
                .FontSize(EditorTheme.FontSizeSmall)
                .Alignment(TextAlignment.MiddleLeft);
        }
    }

    /// <summary> Opens a project, first asking to migrate it when it was saved with another version. </summary>
    public static void TryOpenProject(string path)
    {
        Project project;
        try
        {
            project = Project.Open(path);
        }
        catch (Exception ex)
        {
            Runtime.Debug.LogError(Loc.Get("launcher.open_failed", new { message = ex.Message }));
            Toasts.Warning(Loc.Get("launcher.open_failed_title"), ex.Message);
            return;
        }

        string version = project.Version.IsUnknown ? Loc.Get("launcher.version_unknown") : project.Version.ToString();
        if (project.IsFromNewerEngine)
        {
            Toasts.Warning(Loc.Get("launcher.newer_title"), Loc.Get("launcher.newer_body", new { name = project.Name, version, current = Project.CurrentVersion.ToString() }));
            return;
        }

        if (!project.NeedsMigration)
        {
            Activate(project);
            return;
        }

        Origami.Confirm(Loc.Get("launcher.migrate_title"),
            Loc.Get("launcher.migrate_body", new { name = project.Name, version, current = Project.CurrentVersion.ToString() }),
            () =>
            {
                try
                {
                    ProjectMigration.Migrate(project);
                }
                catch (Exception ex)
                {
                    Runtime.Debug.LogError($"Failed to migrate '{project.Name}': {ex}");
                    Toasts.Warning(Loc.Get("launcher.migrate_failed_title"), ex.Message);
                    return;
                }
                _versions.Remove(path);
                Activate(project);
            });
    }

    private static void Activate(Project project)
    {
        project.SetActive();
        Window.SetProjectIcon();
        Close();
    }

    private static void TryCreateProject()
    {
        if (string.IsNullOrWhiteSpace(_newProjectName))
        {
            Toasts.Show(Loc.Get("launcher.invalid_name"), Loc.Get("launcher.name_empty"), ToastType.Warning, 3f);
            return;
        }

        // The name becomes a folder, and Path.Combine hands back somewhere else entirely for one that is
        // rooted or carries a separator, so the project would be created outside the chosen directory.
        string name = EditorUtils.SafeFileName(_newProjectName, "New Project");

        string targetPath = Path.Combine(_newProjectPath, name);
        if (Directory.Exists(targetPath) && Directory.GetFileSystemEntries(targetPath).Length > 0)
        {
            Toasts.Show(Loc.Get("launcher.folder_exists"), Loc.Get("launcher.folder_exists_msg", new { name }), ToastType.Error, 5f);
            return;
        }

        try
        {
            var project = Project.Create(_newProjectPath, name);
            SeedTemplate(project);
            project.SetActive();
            Window.SetProjectIcon();
            Close();
        }
        catch (Exception ex)
        {
            Toasts.Show(Loc.Get("launcher.create_failed"), ex.Message, ToastType.Error, 5f);
        }
    }

    private static void SeedTemplate(Project project)
    {
        string scripts = Path.Combine(project.AssetsPath, "Scripts");
        Directory.CreateDirectory(scripts);

        if (_template == ProjectTemplate.Empty3D)
        {
            File.WriteAllText(Path.Combine(scripts, "SimpleRotator.cs"),
                "using Infinity.Runtime;\nusing Infinity.Vector;\n\npublic class SimpleRotator : Component\n{\n    public Float3 Axis = new Float3(0, 1, 0);\n    public float DegreesPerSecond = 90f;\n\n    public override void Update()\n    {\n        Transform.Rotate(Axis, DegreesPerSecond * Time.DeltaTime);\n    }\n}\n");
        }
        else
        {
            File.WriteAllText(Path.Combine(scripts, "SimpleSpriteAnimator.cs"),
                "using Infinity.Runtime;\nusing Infinity.Runtime.Resources;\n\npublic class SimpleSpriteAnimator : Component\n{\n    public Flipbook? Book;\n    public bool DoubleSided = true;\n\n    public override void Start()\n    {\n        SpriteAnimation animation = GameObject.GetComponent<SpriteAnimation>() ?? GameObject.AddComponent<SpriteAnimation>();\n        animation.DoubleSided = DoubleSided;\n        if (Book.IsValid()) animation.PlayFlipbook(Book!, true);\n    }\n}\n");
        }

        SeedStarterScene(project, _template == ProjectTemplate.Empty3D);
    }

    /// <summary>Writes a ready-to-play starter scene so the Simple templates open with a camera and light.</summary>
    private static void SeedStarterScene(Project project, bool threeD)
    {
        string scenes = Path.Combine(project.AssetsPath, "Scenes");
        Directory.CreateDirectory(scenes);

        var scene = new SceneInstance(SceneMode.Edit);
        try
        {
            GameObject cameraObject = scene.Create("Main Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            cameraObject.Transform.Position = threeD ? new Float3(0f, 2f, -6f) : new Float3(0f, 0f, -10f);
            cameraObject.Transform.LookAt(Float3.Zero);

            if (threeD)
            {
                GameObject lightObject = scene.Create("Directional Light");
                lightObject.AddComponent<DirectionalLight>();
                lightObject.Transform.LocalEulerAngles = new Float3(50f, -30f, 0f);
            }
            else
            {
                camera.ProjectionMode = Camera.ProjectionType.Orthographic;
                camera.OrthographicSize = 5f;
                camera.Background = CameraBackground.Color;
                camera.BackgroundColor = new Infinity.Vector.Color(0.08f, 0.09f, 0.12f, 1f);
            }

            File.WriteAllText(Path.Combine(scenes, "Main.scene"), scene.ActiveSection.CaptureTree().WriteToString());
        }
        finally
        {
            scene.Dispose();
        }
    }

    private static string FormatTimeAgo(DateTime utcTime)
    {
        TimeSpan span = DateTime.UtcNow - utcTime;
        if (span.TotalMinutes < 1) return Loc.Get("launcher.just_now");
        if (span.TotalHours < 1) return Loc.Get("launcher.minutes_ago", new { count = (int)span.TotalMinutes });
        if (span.TotalDays < 1) return Loc.Get("launcher.hours_ago", new { count = (int)span.TotalHours });
        if (span.TotalDays < 30) return Loc.Get("launcher.days_ago", new { count = (int)span.TotalDays });
        if (span.TotalDays < 365) return Loc.Get("launcher.months_ago", new { count = (int)(span.TotalDays / 30) });
        return Loc.Get("launcher.years_ago", new { count = (int)(span.TotalDays / 365) });
    }
}
