// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Infinity.Editor.Core;
using Infinity.Editor.GUI.ApiDocs;
using Infinity.Editor.Theming;
using Infinity.Editor.Utils;
using Infinity.OrigamiUI;
using Infinity.PaperUI;
using Infinity.PaperUI.LayoutEngine;
using Infinity.Rosetta;
using Infinity.Runtime;
using Infinity.Scribe;

using static Infinity.Editor.GUI.EditorGUI;

using Color = System.Drawing.Color;
using TextAlignment = Infinity.PaperUI.TextAlignment;

namespace Infinity.Editor.GUI.Panels;

/// <summary>
/// The Códigos window: a searchable index of every public type and member in the engine, grouped into APIs,
/// functions, methods, variables, properties, events, components and enums. Selecting an entry shows its
/// signature, explanation and a copyable usage example.
/// </summary>
public class ApiDocsPanel : DockPanel
{
    [MenuItem("Window/General/Códigos", priority: 2)]
    private static void Open() => EditorApplication.Instance?.OpenPanel<ApiDocsPanel>();

    public override string Title => Loc.Get("panel.api_docs");
    public override string Icon => EditorIcons.Code;

    private const float HeaderH = 52f;
    private const float CategoryW = 190f;
    private const float ListW = 340f;
    private const float RowH = 44f;
    private const int MaxResults = 500;

    private string _search = string.Empty;
    private ApiDocCategory _category = ApiDocCategory.All;
    private bool _includeNonPublic;
    private bool _dirty = true;
    private DateTime _refreshAt;
    private List<ApiDocEntry> _filtered = [];
    private IReadOnlyList<ApiDocEntry> _all = [];
    private ApiDocEntry? _selected;
    private int _typeCount;
    private int _servicesCount;
    private int _dikCount;
    private int[] _categoryCounts = [];
    private readonly Dictionary<ApiDocEntry, List<ApiDocEntry>> _memberCache = [];
    private readonly Dictionary<string, List<ApiDocEntry>> _membersByOwner = new(StringComparer.Ordinal);
    private IReadOnlyList<ApiDocEntry> _indexedAll = [];

    public override void OnGUI(Paper paper, float width, float height)
    {
        FontFile? font = EditorTheme.DefaultFont;
        if (font == null) return;

        IReadOnlyList<ApiDocEntry> all = ApiDocsCatalog.GetEntries(_includeNonPublic, backgroundBuild: true);
        if (!ReferenceEquals(all, _all))
        {
            _all = all;
            _dirty = true;
        }
        if (_dirty && DateTime.UtcNow >= _refreshAt)
            Refresh();

        DrawHeader(paper, font, width);
        if (!ApiDocsCatalog.IsReady)
        {
            DrawLoading(paper, font, width, height - HeaderH);
            return;
        }
        DrawBody(paper, font, width, height - HeaderH);
    }

    private void Refresh()
    {
        IEnumerable<ApiDocEntry> query = _all;

        if (_category != ApiDocCategory.All)
            query = query.Where(entry => MatchesCategory(entry, _category));

        string search = _search.Trim();
        if (search.Length > 0)
        {
            string[] terms = search.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            query = query.Where(entry => terms.All(term =>
                entry.SearchText.Contains(term, StringComparison.OrdinalIgnoreCase)));
        }

        _filtered = query.Take(MaxResults).ToList();

        if (_selected == null || !_filtered.Contains(_selected))
            _selected = _filtered.FirstOrDefault();

        if (!ReferenceEquals(_indexedAll, _all))
        {
            _indexedAll = _all;
            _memberCache.Clear();
            _membersByOwner.Clear();
            foreach (ApiDocEntry entry in _all)
            {
                if (entry.IsType || entry.OwnerFullName.Length == 0) continue;
                if (!_membersByOwner.TryGetValue(entry.OwnerFullName, out List<ApiDocEntry>? members))
                    _membersByOwner[entry.OwnerFullName] = members = [];
                members.Add(entry);
            }
            foreach (List<ApiDocEntry> members in _membersByOwner.Values)
                members.Sort(static (a, b) =>
                {
                    int byCategory = a.Category.CompareTo(b.Category);
                    return byCategory != 0 ? byCategory : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
                });
            RebuildCounts();
        }

        _dirty = false;
    }

    private void RebuildCounts()
    {
        var counts = new int[Enum.GetValues<ApiDocCategory>().Length];
        int types = 0, services = 0, dik = 0;
        foreach (ApiDocEntry entry in _all)
        {
            counts[(int)entry.Category]++;
            if (entry.IsType) types++;
            if (entry.Namespace.StartsWith("Infinity.Services", StringComparison.Ordinal)) services++;
            else if (entry.Namespace.StartsWith("Infinity.DIK", StringComparison.Ordinal)) dik++;
        }
        _categoryCounts = counts;
        _typeCount = types;
        _servicesCount = services;
        _dikCount = dik;
    }

    private List<ApiDocEntry> MembersOf(ApiDocEntry entry)
    {
        if (_memberCache.TryGetValue(entry, out List<ApiDocEntry>? cached)) return cached;

        List<ApiDocEntry> members = _membersByOwner.TryGetValue(entry.QualifiedName, out List<ApiDocEntry>? found)
            ? [.. found.Take(80)]
            : [];
        _memberCache[entry] = members;
        return members;
    }

    private void DrawLoading(Paper paper, FontFile font, float width, float height)
    {
        paper.Box("api_loading").Width(width).Height(Math.Max(80, height)).Padding(24, 24, 0, 0)
            .Text(Loc.Get("apidocs.loading"), font).TextColor(EditorTheme.Ink300)
            .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleCenter).IsNotInteractable();
    }

    private void DrawHeader(Paper paper, FontFile font, float width)
    {
        bool compact = width < 900;
        using (paper.Row("api_header").Width(width).Height(HeaderH).Padding(12, 12, 0, 0).Gap(10).Enter())
        {
            paper.Box("api_title").Width(UnitValue.Auto).Height(HeaderH)
                .Text(Loc.Get("apidocs.title"), EditorTheme.FontBold ?? font)
                .TextColor(EditorTheme.Ink500).FontSize(20).Alignment(TextAlignment.MiddleLeft)
                .IsNotInteractable();

            if (!compact)
            {
                int types = _typeCount;
                int members = _all.Count - types;
                paper.Box("api_stats").Width(UnitValue.Auto).Height(HeaderH)
                    .Text(Loc.Get("apidocs.stats", new { types, members, assemblies = ApiDocsCatalog.AssemblyCount }), font)
                    .TextColor(EditorTheme.Ink300).FontSize(EditorTheme.FontSizeSmall)
                    .Alignment(TextAlignment.MiddleLeft).IsNotInteractable();

                paper.Box("api_spacer").Width(UnitValue.Stretch()).Height(1).IsNotInteractable();
            }

            Origami.SearchField(paper, "api_search", _search, value =>
            {
                _search = value;
                _dirty = true;
                _refreshAt = DateTime.UtcNow.AddMilliseconds(140);
            }, Loc.Get("apidocs.search")).Width(compact ? UnitValue.Stretch() : 320).Height(30).Show();

            var privateButton = Origami.Button(paper, "api_private", Loc.Get("apidocs.include_non_public"), () =>
            {
                _includeNonPublic = !_includeNonPublic;
                _dirty = true;
            }).Small().Tooltip(Loc.Get("apidocs.include_non_public_tip"));
            if (_includeNonPublic) privateButton.Primary(); else privateButton.Ghost();
            privateButton.Show();
        }
    }

    private void DrawBody(Paper paper, FontFile font, float width, float height)
    {
        height = Math.Max(80, height);
        float categoryW = width < 760 ? 150 : CategoryW;
        float listW = width < 760 ? 210 : width < 1000 ? 280 : ListW;
        float listHeight = Math.Max(80, height - 26);
        float detailW = Math.Max(200, width - categoryW - listW - 2);

        using (paper.Row("api_body").Width(width).Height(height).Enter())
        {
            using (paper.Column("api_categories").Width(categoryW).Height(height).Padding(8, 8, 0, 0).Gap(3).Enter())
            {
                foreach (ApiDocCategory category in Enum.GetValues<ApiDocCategory>())
                {
                    int count = CountFor(category);

                    var button = Origami.Button(paper, $"api_cat_{(int)category}",
                        $"{CategoryLabel(category)}  {count}", () =>
                        {
                            _category = category;
                            _dirty = true;
                            _refreshAt = DateTime.UtcNow;
                        }).Small().FullWidth();
                    if (_category == category) button.Primary(); else button.Ghost();
                    button.Show();
                }
            }

            paper.Box("api_cat_div").Width(1).Height(height).BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();

            using (paper.Column("api_list").Width(listW).Height(height).Enter())
            {
                paper.Box("api_list_header").Height(26).Padding(8, 8, 0, 0)
                    .Text(_filtered.Count >= MaxResults
                        ? Loc.Get("apidocs.results_limit", new { count = MaxResults })
                        : Loc.Get("apidocs.results", new { count = _filtered.Count }), font)
                    .TextColor(EditorTheme.Ink300).FontSize(EditorTheme.FontSizeSmall)
                    .Alignment(TextAlignment.MiddleLeft).IsNotInteractable();

                Origami.ScrollView(paper, "api_list_scroll", listW, listHeight).SmoothScroll(false).Body(viewport =>
                {
                    if (_filtered.Count == 0) return;

                    int first = Math.Max(0, (int)MathF.Floor(viewport.ScrollY / RowH));
                    int visible = (int)MathF.Ceiling(viewport.Height / RowH) + 1;
                    int last = Math.Min(_filtered.Count - 1, first + visible);

                    using (paper.Column("api_list_rows").Width(UnitValue.Stretch()).Height(UnitValue.Auto).Enter())
                    {
                        if (first > 0)
                            paper.Box("api_list_top_pad").Width(1).Height(first * RowH).IsNotInteractable();

                        for (int i = first; i <= last; i++)
                            DrawListRow(paper, font, _filtered[i], i, listW);

                        int bottom = _filtered.Count - 1 - last;
                        if (bottom > 0)
                            paper.Box("api_list_bottom_pad").Width(1).Height(bottom * RowH).IsNotInteractable();
                    }
                });
            }

            paper.Box("api_list_div").Width(1).Height(height).BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();

            using (paper.Column("api_detail").Width(detailW).Height(height).Padding(12, 12, 0, 0).Enter())
            {
                Origami.ScrollView(paper, "api_detail_scroll", detailW, height).Body(() =>
                {
                    using (paper.Column("api_detail_body").Width(UnitValue.Stretch()).Height(UnitValue.Auto).Gap(8).Enter())
                        DrawDetail(paper, font, detailW);
                });
            }
        }
    }

    private void DrawListRow(Paper paper, FontFile font, ApiDocEntry entry, int index, float width)
    {
        bool selected = ReferenceEquals(_selected, entry);
        var row = paper.Row($"api_row_{index}").Width(width).Height(RowH).Padding(8, 8, 0, 0).Gap(6)
            .BackgroundColor(selected ? EditorTheme.Glass : Color.Transparent)
            .Hovered.BackgroundColor(EditorTheme.Hover).End()
            .OnClick(0, (_, _) => _selected = entry)
            .Cursor(PaperCursor.Pointer);

        using (row.Enter())
        {
            using (paper.Column($"api_row_text_{index}").Width(UnitValue.Stretch()).Height(RowH).Enter())
            {
                paper.Box($"api_row_name_{index}").Height(21).Width(UnitValue.Stretch())
                    .Text(entry.Name, font).TextColor(selected ? EditorTheme.Ink500 : EditorTheme.Ink400)
                    .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft).IsNotInteractable();
                paper.Box($"api_row_owner_{index}").Height(17).Width(UnitValue.Stretch())
                    .Text(entry.IsType ? entry.Namespace : entry.OwnerFullName, font)
                    .TextColor(EditorTheme.Ink300).FontSize(EditorTheme.FontSizeSmall - 1)
                    .Alignment(TextAlignment.MiddleLeft).IsNotInteractable();
            }

            paper.Box($"api_row_kind_{index}").Width(UnitValue.Auto).Height(18).Padding(5, 5, 0, 0)
                .Rounded(4).BackgroundColor(EditorTheme.WithAlpha(EditorTheme.Neutral400, 150))
                .Text(entry.Kind, font).TextColor(EditorTheme.Ink300)
                .FontSize(EditorTheme.FontSizeSmall - 1).Alignment(TextAlignment.MiddleCenter)
                .IsNotInteractable();
        }
    }

    private void DrawDetail(Paper paper, FontFile font, float width)
    {
        if (_selected == null)
        {
            paper.Box("api_empty").Width(UnitValue.Stretch()).Height(UnitValue.Auto)
                .Text(Loc.Get("apidocs.select"), font).TextColor(EditorTheme.Ink300)
                .FontSize(EditorTheme.FontSize).Wrap(TextWrapMode.Wrap).IsNotInteractable();
            return;
        }

        ApiDocEntry entry = _selected;

        paper.Box("api_d_name").Width(UnitValue.Stretch()).Height(UnitValue.Auto)
            .Text(entry.Name, EditorTheme.FontBold ?? font).TextColor(EditorTheme.Ink500)
            .FontSize(22).Wrap(TextWrapMode.Wrap).IsNotInteractable();

        using (paper.Row("api_d_badges").Width(UnitValue.Stretch()).Height(24).Gap(6).Enter())
        {
            Badge(paper, "api_d_kind", entry.Kind, font);
            Badge(paper, "api_d_cat", CategoryLabel(entry.Category), font);
            if (!entry.IsPublic) Badge(paper, "api_d_private", Loc.Get("apidocs.private"), font);
        }

        paper.Box("api_d_qualified").Width(UnitValue.Stretch()).Height(UnitValue.Auto)
            .Text(entry.QualifiedName, EditorTheme.FontMono ?? font).TextColor(EditorTheme.Ink300)
            .FontSize(EditorTheme.FontSizeSmall).Wrap(TextWrapMode.Wrap).IsNotInteractable();

        Section(paper, font, "api_d_sig_label", Loc.Get("apidocs.signature"));
        CodeBlock(paper, font, "api_d_sig", ClampForUi(entry.Signature, 2400));

        Section(paper, font, "api_d_desc_label", Loc.Get("apidocs.description"));
        paper.Box("api_d_desc").Width(UnitValue.Stretch()).Height(UnitValue.Auto)
            .Text(ClampForUi(entry.Summary, 6000), font).TextColor(EditorTheme.Ink400)
            .FontSize(EditorTheme.FontSize).Wrap(TextWrapMode.Wrap).IsNotInteractable();

        Section(paper, font, "api_d_details_label", Loc.Get("apidocs.how"));
        paper.Box("api_d_details").Width(UnitValue.Stretch()).Height(UnitValue.Auto)
            .Text(ClampForUi(entry.Details, 9000), font).TextColor(EditorTheme.Ink400)
            .FontSize(EditorTheme.FontSizeSmall).Wrap(TextWrapMode.Wrap).IsNotInteractable();

        DrawMedia(paper, font, entry);

        using (paper.Row("api_d_example_header").Width(UnitValue.Stretch()).Height(26).Gap(8).Enter())
        {
            paper.Box("api_d_example_label").Width(UnitValue.Stretch()).Height(26)
                .Text(Loc.Get("apidocs.example"), EditorTheme.FontSemiBold ?? font)
                .TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSizeSmall)
                .Alignment(TextAlignment.MiddleLeft).IsNotInteractable();

            Origami.Button(paper, "api_d_copy", Loc.Get("apidocs.copy"), () =>
            {
                Input.Clipboard = entry.Example;
                Toasts.Success(Loc.Get("apidocs.copied"), entry.Name);
            }).Small().Show();
        }

        CodeBlock(paper, font, "api_d_example", ClampForUi(entry.Example, 16000));

        if (entry.IsType)
        {
            List<ApiDocEntry> members = MembersOf(entry);

            if (members.Count > 0)
            {
                Section(paper, font, "api_d_members_label", Loc.Get("apidocs.members"));
                foreach (ApiDocEntry member in members)
                {
                    var row = paper.Row($"api_d_member_{member.Name}_{member.Category}").Width(UnitValue.Stretch())
                        .Height(24).Padding(6, 6, 0, 0).Gap(6)
                        .Hovered.BackgroundColor(EditorTheme.Hover).End()
                        .OnClick(0, (_, _) => _selected = member)
                        .Cursor(PaperCursor.Pointer);
                    using (row.Enter())
                    {
                        paper.Box($"api_d_member_name_{member.Name}_{member.Category}").Width(UnitValue.Stretch()).Height(24)
                            .Text(member.Name, font).TextColor(EditorTheme.Ink400)
                            .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft).IsNotInteractable();
                        paper.Box($"api_d_member_kind_{member.Name}_{member.Category}").Width(UnitValue.Auto).Height(24)
                            .Text(member.Kind, font).TextColor(EditorTheme.Ink300)
                            .FontSize(EditorTheme.FontSizeSmall - 1).Alignment(TextAlignment.MiddleRight).IsNotInteractable();
                    }
                }
            }
        }
        else if (entry.OwnerFullName.Length > 0)
        {
            Section(paper, font, "api_d_owner_label", Loc.Get("apidocs.member_of"));
            paper.Box("api_d_owner").Width(UnitValue.Stretch()).Height(UnitValue.Auto)
                .Text(entry.OwnerFullName, EditorTheme.FontMono ?? font).TextColor(EditorTheme.Ink300)
                .FontSize(EditorTheme.FontSizeSmall).Wrap(TextWrapMode.Wrap).IsNotInteractable();
        }
    }

    private static void DrawMedia(Paper paper, FontFile font, ApiDocEntry entry)
    {
        Section(paper, font, "api_d_media_label", Loc.Get("apidocs.media_label"));
        paper.Box("api_d_media_hint").Width(UnitValue.Stretch()).Height(UnitValue.Auto)
            .Text(Loc.Get("apidocs.media_hint", new { name = entry.Name }), font)
            .TextColor(EditorTheme.Ink300).FontSize(EditorTheme.FontSizeSmall).Wrap(TextWrapMode.Wrap).IsNotInteractable();

        // The print and the GIF are drawn in place, so selecting an entry never launches another window.
        ApiDocsMedia.Draw(paper, entry);
    }

    /// <summary>Bounds the text a single detail view lays out, so an unusually large XML example cannot stall the UI.</summary>
    private static string ClampForUi(string text, int max)
        => string.IsNullOrEmpty(text) || text.Length <= max ? text : text[..max] + "\n\n...";

    private static void Badge(Paper paper, string id, string text, FontFile font)
    {
        paper.Box(id).Width(UnitValue.Auto).Height(22).Padding(8, 8, 0, 0).Rounded(5)
            .BackgroundColor(EditorTheme.WithAlpha(EditorTheme.Neutral400, 150))
            .Text(text, font).TextColor(EditorTheme.Ink300).FontSize(EditorTheme.FontSizeSmall)
            .Alignment(TextAlignment.MiddleCenter).IsNotInteractable();
    }

    private static void Section(Paper paper, FontFile font, string id, string text)
    {
        paper.Box(id).Width(UnitValue.Stretch()).Height(24).Padding(0, 0, 8, 0)
            .Text(text, EditorTheme.FontSemiBold ?? font).TextColor(EditorTheme.Ink400)
            .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft).IsNotInteractable();
    }

    private static void CodeBlock(Paper paper, FontFile font, string id, string code)
    {
        using (paper.Column($"{id}_box").Width(UnitValue.Stretch()).Height(UnitValue.Auto)
            .Padding(10, 10, 8, 8).Rounded(6)
            .BackgroundColor(EditorTheme.WithAlpha(EditorTheme.Neutral100, 180))
            .BorderColor(EditorTheme.BorderSoft).BorderWidth(1).Enter())
        {
            paper.Box(id).Width(UnitValue.Stretch()).Height(UnitValue.Auto)
                .Text(code, EditorTheme.FontMono ?? font).TextColor(EditorTheme.Ink400)
                .FontSize(12).Wrap(TextWrapMode.Wrap).IsNotInteractable();
        }
    }

    private int CountFor(ApiDocCategory category) => category switch
    {
        ApiDocCategory.All => _all.Count,
        ApiDocCategory.Type => _typeCount,
        ApiDocCategory.Services => _servicesCount,
        ApiDocCategory.Dik => _dikCount,
        _ when (int)category < _categoryCounts.Length => _categoryCounts[(int)category],
        _ => 0,
    };

    private static bool MatchesCategory(ApiDocEntry entry, ApiDocCategory category) => category switch
    {
        ApiDocCategory.All => true,
        ApiDocCategory.Type => entry.IsType,
        ApiDocCategory.Services => entry.Namespace.StartsWith("Infinity.Services", StringComparison.Ordinal),
        ApiDocCategory.Dik => entry.Namespace.StartsWith("Infinity.DIK", StringComparison.Ordinal),
        // API is the category for plain engine types. Keep it explicit: the generic fallback used to make
        // a category with no member entries look empty even though its type rows were there.
        ApiDocCategory.Api => entry.Category == ApiDocCategory.Api,
        _ => entry.Category == category,
    };

    private static string CategoryLabel(ApiDocCategory category) => category switch
    {
        ApiDocCategory.All => Loc.Get("apidocs.cat.all"),
        ApiDocCategory.Api => Loc.Get("apidocs.cat.api"),
        ApiDocCategory.Function => Loc.Get("apidocs.cat.function"),
        ApiDocCategory.Method => Loc.Get("apidocs.cat.method"),
        ApiDocCategory.Variable => Loc.Get("apidocs.cat.variable"),
        ApiDocCategory.Property => Loc.Get("apidocs.cat.property"),
        ApiDocCategory.Event => Loc.Get("apidocs.cat.event"),
        ApiDocCategory.Type => Loc.Get("apidocs.cat.type"),
        ApiDocCategory.Component => Loc.Get("apidocs.cat.component"),
        ApiDocCategory.Enum => Loc.Get("apidocs.cat.enum"),
        ApiDocCategory.Constructor => Loc.Get("apidocs.cat.constructor"),
        ApiDocCategory.Services => Loc.Get("apidocs.cat.services"),
        ApiDocCategory.Dik => Loc.Get("apidocs.cat.dik"),
        _ => category.ToString(),
    };
}
