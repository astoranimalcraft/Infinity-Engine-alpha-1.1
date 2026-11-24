// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Infinity.Editor.Core;
using Infinity.Editor.Theming;
using Infinity.OrigamiUI;
using Infinity.PaperUI;
using Infinity.PaperUI.LayoutEngine;
using Infinity.Quill;
using Infinity.Runtime;
using Infinity.Scribe;
using Infinity.Vector;

using Color = System.Drawing.Color;
using TextAlignment = Infinity.PaperUI.TextAlignment;

namespace Infinity.Editor.MultiProgram;

/// <summary>
/// The MultiProgram window: host/join controls, the developer list with roles and colors, the host's
/// ban list, and the host-only change history.
/// </summary>
public sealed class MultiProgramPanel : DockPanel
{
    private const int TabConnection = 0;
    private const int TabDevelopers = 1;
    private const int TabBans = 2;
    private const int TabHistory = 3;

    private int _tab = TabConnection;
    private string _name = string.Empty;
    private string _color = string.Empty;
    private string _host = string.Empty;
    private string _port = string.Empty;

    [MenuItem("Window/MultiProgram/MultiProgram", priority: 4)]
    private static void OpenPanel()
        => EditorApplication.Instance?.OpenPanel<MultiProgramPanel>();

    [MenuItem("Window/MultiProgram/Programadores", priority: 5)]
    private static void OpenDevelopers() => OpenAt(TabDevelopers);

    [MenuItem("Window/MultiProgram/Banidos", priority: 6)]
    private static void OpenBans() => OpenAt(TabBans);

    private static void OpenAt(int tab)
    {
        MultiProgramPanel? panel = EditorApplication.Instance?.OpenPanel<MultiProgramPanel>();
        if (panel != null) panel._tab = tab;
    }

    [MenuItem("Window/MultiProgram/Historico", priority: 7)]
    private static void OpenHistory() => OpenAt(TabHistory);

    public override string Title => "MultiProgram";
    public override string Icon => EditorIcons.Users;

    public MultiProgramPanel()
    {
        _name = MultiProgramSession.LocalName;
        _color = MultiProgramSession.LocalColor;
        _host = MultiProgramSession.LastHost;
        _port = MultiProgramSession.LastPort.ToString();
    }

    public override void OnGUI(Paper paper, float width, float height)
    {
        FontFile? font = EditorTheme.DefaultFont;
        if (font == null) return;
        MultiProgramSession.Tick();

        float padding = 16f;
        float contentW = Math.Max(300f, width - padding * 2f);

        paper.Box("mp_bg").Width(width).Height(height).IsNotInteractable();
        DrawHeader(paper, font, width, padding);
        DrawTabs(paper, font, width, padding);

        float bodyH = Math.Max(120f, height - 112f);
        using (paper.Column("mp_body").Width(width).Height(bodyH).Padding(padding, padding, 0, 0).Enter())
        {
            Origami.ScrollView(paper, "mp_scroll", contentW, bodyH).Body(() =>
            {
                using (paper.Column("mp_scroll_body").Width(UnitValue.Stretch()).Height(UnitValue.Auto).Gap(10).Enter())
                {
                    switch (_tab)
                    {
                        case TabDevelopers: DrawDevelopers(paper, font); break;
                        case TabBans: DrawBans(paper, font); break;
                        case TabHistory: DrawHistory(paper, font); break;
                        default: DrawConnection(paper, font); break;
                    }
                }
            });
        }
    }

    private void DrawHeader(Paper paper, FontFile font, float width, float padding)
    {
        using (paper.Row("mp_header").Width(width).Height(54).Padding(padding, padding, 0, 0).Gap(10).Enter())
        {
            paper.Box("mp_icon").Width(28).Height(54).Icon(paper, EditorIcons.Users_I, EditorTheme.Purple400, 22f).IsNotInteractable();
            paper.Box("mp_title").Width(UnitValue.Auto).Height(54).Text("MultiProgram", EditorTheme.FontBold ?? font)
                .TextColor(EditorTheme.Ink500).FontSize(22).Alignment(TextAlignment.MiddleLeft).IsNotInteractable();

            paper.Box("mp_status").Width(UnitValue.Auto).Height(22).Padding(9, 9, 0, 0).Rounded(5)
                .BackgroundColor(EditorTheme.WithAlpha(EditorTheme.Neutral400, 150))
                .Text(MultiProgramSession.IsActive
                    ? $"{EditorIcons.Circle}  {(MultiProgramSession.IsHosting ? "Hospedando" : "Conectado")}  •  {MultiProgramSession.Users.Count} dev(s)"
                    : "Desconectado", font)
                .TextColor(MultiProgramSession.IsActive ? EditorTheme.Green400 : EditorTheme.Ink300)
                .FontSize(EditorTheme.FontSizeSmall - 1).Alignment(TextAlignment.MiddleCenter).IsNotInteractable();

            paper.Box("mp_header_spacer").Width(UnitValue.Stretch()).Height(1).IsNotInteractable();
        }
    }

    private void DrawTabs(Paper paper, FontFile font, float width, float padding)
    {
        using (paper.Row("mp_tabs").Width(width).Height(42).Padding(padding, padding, 0, 0).Gap(6).Enter())
        {
            Tab(paper, "mp_tab_conn", "Sessão", TabConnection, font);
            Tab(paper, "mp_tab_devs", $"Programadores ({MultiProgramSession.Users.Count})", TabDevelopers, font);
            Tab(paper, "mp_tab_bans", $"Banidos ({MultiProgramSession.Bans.Count})", TabBans, font);
            Tab(paper, "mp_tab_hist", $"Histórico ({MultiProgramSession.History.Count})", TabHistory, font);
        }
    }

    private void Tab(Paper paper, string id, string label, int index, FontFile font)
    {
        var button = Origami.Button(paper, id, label, () => _tab = index).Small();
        if (_tab == index) button.Primary(); else button.Ghost();
        button.Show();
    }

    private void DrawConnection(Paper paper, FontFile font)
    {
        Section(paper, font, "mp_identity_title", "Identidade do desenvolvedor");
        using (paper.Row("mp_identity").Width(UnitValue.Stretch()).Height(34).Gap(8).Enter())
        {
            paper.Box("mp_name_lbl").Width(64).Height(34).Text("Nome", font).TextColor(EditorTheme.Ink300)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft).IsNotInteractable();
            Origami.TextField(paper, "mp_name_field", _name, v => _name = v).Width(220).Show();

            paper.Box("mp_color_lbl").Width(52).Height(34).Text("Cor", font).TextColor(EditorTheme.Ink300)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft).IsNotInteractable();
            paper.Box("mp_color_dot").Width(14).Height(14).Rounded(7)
                .BackgroundColor(ToColor(_color)).IsNotInteractable();
            Origami.TextField(paper, "mp_color_field", _color, v => _color = v).Width(100).Show();
            Origami.Button(paper, "mp_color_apply", "Aplicar", () =>
            {
                MultiProgramSession.LocalName = _name;
                MultiProgramSession.LocalColor = _color;
                _color = MultiProgramSession.LocalColor;
                Toasts.Success("Identidade atualizada", "Seu nome e cor aparecem para a equipe.");
            }).Small().Show();
        }

        paper.Box("mp_identity_hint").Width(UnitValue.Stretch()).Height(UnitValue.Auto)
            .Text("O nome e a cor identificam você no projeto, na hierarquia e na lista de programadores.", font)
            .TextColor(EditorTheme.Ink300).FontSize(EditorTheme.FontSizeSmall).Wrap(Scribe.TextWrapMode.Wrap).IsNotInteractable();

        Section(paper, font, "mp_session_title", "Sessão");

        using (paper.Row("mp_session_row").Width(UnitValue.Stretch()).Height(36).Gap(8).Enter())
        {
            paper.Box("mp_ip_lbl").Width(64).Height(36).Text("Seu IP", font).TextColor(EditorTheme.Ink300)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft).IsNotInteractable();
            paper.Box("mp_ip_value").Width(170).Height(36).Text(MultiProgramSession.LocalAddress, EditorTheme.FontMono ?? font)
                .TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft).IsNotInteractable();
            Origami.Button(paper, "mp_ip_copy", $"{EditorIcons.Copy}  Copiar", () =>
            {
                Input.Clipboard = MultiProgramSession.LocalAddress;
                Toasts.Success("IP copiado", "Envie este endereço para os outros desenvolvedores.");
            }).Small().Show();

            paper.Box("mp_port_lbl").Width(44).Height(36).Text("Porta", font).TextColor(EditorTheme.Ink300)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft).IsNotInteractable();
            Origami.TextField(paper, "mp_port_field", _port, v => _port = v).Width(90).Show();

            Origami.Button(paper, "mp_host", $"{EditorIcons.TowerBroadcast}  Hospedar", () =>
            {
                if (!int.TryParse(_port, out int port)) { Toasts.Warning("Porta inválida", "Use um número entre 1024 e 65535."); return; }
                MultiProgramSession.Host(port);
            }).Small().Show();
        }

        using (paper.Row("mp_join_row").Width(UnitValue.Stretch()).Height(36).Gap(8).Enter())
        {
            paper.Box("mp_join_lbl").Width(64).Height(36).Text("Conectar", font).TextColor(EditorTheme.Ink300)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft).IsNotInteractable();
            Origami.TextField(paper, "mp_host_field", _host, v => _host = v).Width(240).Show();

            Origami.Button(paper, "mp_join", $"{EditorIcons.RightToBracket}  Entrar", () =>
            {
                if (string.IsNullOrWhiteSpace(_host)) { Toasts.Warning("Endereço vazio", "Digite o IP ou nome do host."); return; }
                if (!int.TryParse(_port, out int port)) { Toasts.Warning("Porta inválida", "Use um número entre 1024 e 65535."); return; }
                MultiProgramSession.Join(_host, port);
            }).Small().Show();

            Origami.Button(paper, "mp_leave", "Sair", () => MultiProgramSession.Disconnect()).Small().Ghost().Show();
        }

        paper.Box("mp_status_line").Width(UnitValue.Stretch()).Height(UnitValue.Auto)
            .Text(MultiProgramSession.StatusMessage, font).TextColor(EditorTheme.Ink300)
            .FontSize(EditorTheme.FontSizeSmall).Wrap(Scribe.TextWrapMode.Wrap).IsNotInteractable();

        Section(paper, font, "mp_camera_title", "Câmera do jogo");
        using (paper.Row("mp_camera_row").Width(UnitValue.Stretch()).Height(34).Gap(8).Enter())
        {
            Origami.Button(paper, "mp_camera_go", $"{EditorIcons.Camera}  Ir para a câmera do jogo", MultiProgramSession.FocusGameCamera)
                .Small().Show();
            paper.Box("mp_camera_hint").Width(UnitValue.Stretch()).Height(34)
                .Text("Usa a mesma pose da câmera principal; cada dev continua podendo personalizar sua própria cor.", font)
                .TextColor(EditorTheme.Ink300).FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft).IsNotInteractable();
        }

        Section(paper, font, "mp_people_title", "Quem está no projeto");
        if (MultiProgramSession.Users.Count == 0)
        {
            paper.Box("mp_people_empty").Width(UnitValue.Stretch()).Height(30)
                .Text("Nenhum desenvolvedor conectado. Hospede uma sessão ou entre em uma.", font)
                .TextColor(EditorTheme.Ink300).FontSize(EditorTheme.FontSizeSmall).IsNotInteractable();
            return;
        }

        foreach (MultiProgramUser user in MultiProgramSession.Users)
            UserRow(paper, font, user, compact: true);
    }

    private void DrawDevelopers(Paper paper, FontFile font)
    {
        if (MultiProgramSession.Users.Count == 0)
        {
            Empty(paper, font, "Nenhum desenvolvedor conectado.");
            return;
        }

        bool owner = MultiProgramSession.IsHosting;
        foreach (MultiProgramUser user in MultiProgramSession.Users)
        {
            using (paper.Column($"mp_dev_{user.Id}").Width(UnitValue.Stretch()).Height(UnitValue.Auto)
                .Padding(10, 10, 8, 8).Gap(4).Rounded(6)
                .BackgroundColor(EditorTheme.WithAlpha(EditorTheme.Neutral100, 160))
                .BorderColor(ToColor(user.Color)).BorderWidth(1).Enter())
            {
                using (paper.Row($"mp_dev_head_{user.Id}").Width(UnitValue.Stretch()).Height(28).Gap(8).Enter())
                {
                    paper.Box($"mp_dev_dot_{user.Id}").Width(12).Height(12).Rounded(6)
                        .BackgroundColor(ToColor(user.Color)).IsNotInteractable();
                    paper.Box($"mp_dev_name_{user.Id}").Width(UnitValue.Auto).Height(28)
                        .Text(user.Name + (user.Id == MultiProgramSession.LocalId ? "  (você)" : string.Empty), EditorTheme.FontSemiBold ?? font)
                        .TextColor(EditorTheme.Ink500).FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft).IsNotInteractable();
                    paper.Box($"mp_dev_pc_{user.Id}").Width(UnitValue.Auto).Height(28)
                        .Text(user.PcName, EditorTheme.FontMono ?? font).TextColor(EditorTheme.Ink300)
                        .FontSize(EditorTheme.FontSizeSmall - 1).Alignment(TextAlignment.MiddleLeft).IsNotInteractable();
                    paper.Box($"mp_dev_role_{user.Id}").Width(UnitValue.Auto).Height(22).Padding(7, 7, 0, 0).Rounded(4)
                        .BackgroundColor(EditorTheme.WithAlpha(EditorTheme.Neutral400, 160))
                        .Text(user.Role.ToString(), font).TextColor(EditorTheme.Ink300)
                        .FontSize(EditorTheme.FontSizeSmall - 1).Alignment(TextAlignment.MiddleCenter).IsNotInteractable();
                    paper.Box($"mp_dev_spacer_{user.Id}").Width(UnitValue.Stretch()).Height(1).IsNotInteractable();

                    if (owner && user.Role != MultiProgramRole.Owner)
                    {
                        Origami.Button(paper, $"mp_dev_editor_{user.Id}", "Editor", () => MultiProgramSession.SetRole(user.Id, MultiProgramRole.Editor))
                            .Small().Ghost().Show();
                        Origami.Button(paper, $"mp_dev_viewer_{user.Id}", "Viewer", () => MultiProgramSession.SetRole(user.Id, MultiProgramRole.Viewer))
                            .Small().Ghost().Show();
                        Origami.Button(paper, $"mp_dev_kick_{user.Id}", "Expulsar", () => MultiProgramSession.Kick(user.Id, ban: false))
                            .Small().Ghost().Show();
                        Origami.Button(paper, $"mp_dev_ban_{user.Id}", "Banir", () => MultiProgramSession.Kick(user.Id, ban: true, "Banido pelo dono da sessão."))
                            .Small().Show();
                    }
                }

                string scene = string.IsNullOrWhiteSpace(user.Scene) ? "nenhuma cena" : user.Scene;
                string file = string.IsNullOrWhiteSpace(user.File) ? "nenhum arquivo" : user.File;
                string obj = string.IsNullOrWhiteSpace(user.ObjectName) ? "nada selecionado" : user.ObjectName;
                paper.Box($"mp_dev_where_{user.Id}").Width(UnitValue.Stretch()).Height(UnitValue.Auto)
                    .Text($"Cena: {scene}   •   Arquivo: {file}   •   Seleção: {obj}", font)
                    .TextColor(EditorTheme.Ink300).FontSize(EditorTheme.FontSizeSmall)
                    .Wrap(Scribe.TextWrapMode.Wrap).IsNotInteractable();
            }
        }
    }

    private void DrawBans(Paper paper, FontFile font)
    {
        if (!MultiProgramSession.IsHosting)
        {
            Empty(paper, font, "Somente o dono da sessão pode ver e gerenciar a lista de banidos.");
            return;
        }

        IReadOnlyList<MultiProgramBan> bans = MultiProgramSession.Bans;
        if (bans.Count == 0)
        {
            Empty(paper, font, "Nenhum desenvolvedor banido.");
            return;
        }

        foreach (MultiProgramBan ban in bans)
        {
            using (paper.Row($"mp_ban_{ban.Id}").Width(UnitValue.Stretch()).Height(44).Padding(10, 10, 0, 0).Gap(8)
                .Rounded(6).BackgroundColor(EditorTheme.WithAlpha(EditorTheme.Neutral100, 160))
                .BorderColor(EditorTheme.BorderSoft).BorderWidth(1).Enter())
            {
                paper.Box($"mp_ban_name_{ban.Id}").Width(180).Height(44).Text(ban.Name, EditorTheme.FontSemiBold ?? font)
                    .TextColor(EditorTheme.Ink500).FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft).IsNotInteractable();
                paper.Box($"mp_ban_pc_{ban.Id}").Width(160).Height(44).Text(ban.PcName, EditorTheme.FontMono ?? font)
                    .TextColor(EditorTheme.Ink300).FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft).IsNotInteractable();
                paper.Box($"mp_ban_reason_{ban.Id}").Width(UnitValue.Stretch()).Height(44).Text(ban.Reason, font)
                    .TextColor(EditorTheme.Ink300).FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft).IsNotInteractable();
                paper.Box($"mp_ban_date_{ban.Id}").Width(120).Height(44).Text(ban.AtUtc.ToLocalTime().ToString("dd/MM HH:mm"), font)
                    .TextColor(EditorTheme.Ink300).FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleRight).IsNotInteractable();
                Origami.Button(paper, $"mp_unban_{ban.Id}", "Desbanir", () => MultiProgramSession.Unban(ban.Id)).Small().Show();
            }
        }
    }

    private void DrawHistory(Paper paper, FontFile font)
    {
        if (!MultiProgramSession.IsHosting)
        {
            Empty(paper, font, "O histórico é exclusivo do dono da sessão.");
            return;
        }

        using (paper.Row("mp_hist_actions").Width(UnitValue.Stretch()).Height(32).Gap(8).Enter())
        {
            paper.Box("mp_hist_hint").Width(UnitValue.Stretch()).Height(32)
                .Text("Tudo que os desenvolvedores fizeram nesta sessão, do mais recente para o mais antigo.", font)
                .TextColor(EditorTheme.Ink300).FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft).IsNotInteractable();
            Origami.Button(paper, "mp_hist_clear", "Limpar histórico", MultiProgramSession.ClearHistory).Small().Ghost().Show();
        }

        IReadOnlyList<MultiProgramHistoryEntry> history = MultiProgramSession.History;
        if (history.Count == 0)
        {
            Empty(paper, font, "Nenhuma alteração registrada ainda.");
            return;
        }

        foreach (MultiProgramHistoryEntry entry in history)
        {
            using (paper.Row($"mp_hist_{entry.Id}").Width(UnitValue.Stretch()).Height(42).Padding(8, 8, 0, 0).Gap(8)
                .BackgroundColor(EditorTheme.WithAlpha(EditorTheme.Neutral100, 120)).Rounded(4).Enter())
            {
                paper.Box($"mp_hist_time_{entry.Id}").Width(78).Height(42).Text(entry.AtUtc.ToLocalTime().ToString("HH:mm:ss"), EditorTheme.FontMono ?? font)
                    .TextColor(EditorTheme.Ink300).FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft).IsNotInteractable();
                paper.Box($"mp_hist_user_{entry.Id}").Width(110).Height(42).Text(entry.User, EditorTheme.FontSemiBold ?? font)
                    .TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft).IsNotInteractable();
                paper.Box($"mp_hist_action_{entry.Id}").Width(100).Height(42).Text(entry.Action, font)
                    .TextColor(EditorTheme.Purple400).FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft).IsNotInteractable();
                paper.Box($"mp_hist_target_{entry.Id}").Width(UnitValue.Stretch()).Height(42)
                    .Text(string.IsNullOrEmpty(entry.Detail) ? entry.Target : $"{entry.Target}  —  {entry.Detail}", font)
                    .TextColor(EditorTheme.Ink300).FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft).IsNotInteractable();
            }
        }
    }

    private static void UserRow(Paper paper, FontFile font, MultiProgramUser user, bool compact)
    {
        using (paper.Row($"mp_user_{user.Id}").Width(UnitValue.Stretch()).Height(compact ? 26 : 34).Gap(8).Enter())
        {
            paper.Box($"mp_user_dot_{user.Id}").Width(10).Height(10).Rounded(5)
                .BackgroundColor(ToColor(user.Color)).IsNotInteractable();
            paper.Box($"mp_user_name_{user.Id}").Width(UnitValue.Auto).Height(compact ? 26 : 34)
                .Text(user.Name + (user.Role == MultiProgramRole.Owner ? "  •  dono" : string.Empty), font)
                .TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft).IsNotInteractable();
            paper.Box($"mp_user_pc_{user.Id}").Width(UnitValue.Auto).Height(compact ? 26 : 34)
                .Text(user.PcName, EditorTheme.FontMono ?? font).TextColor(EditorTheme.Ink300)
                .FontSize(EditorTheme.FontSizeSmall - 1).Alignment(TextAlignment.MiddleLeft).IsNotInteractable();
            paper.Box($"mp_user_where_{user.Id}").Width(UnitValue.Stretch()).Height(compact ? 26 : 34)
                .Text(string.IsNullOrEmpty(user.ObjectName) ? user.File : $"{user.File} → {user.ObjectName}", font)
                .TextColor(EditorTheme.Ink300).FontSize(EditorTheme.FontSizeSmall - 1)
                .Alignment(TextAlignment.MiddleRight).IsNotInteractable();
        }
    }

    private static void Empty(Paper paper, FontFile font, string text)
    {
        paper.Box("mp_empty").Width(UnitValue.Stretch()).Height(60)
            .Text(text, font).TextColor(EditorTheme.Ink300)
            .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleCenter).IsNotInteractable();
    }

    private static void Section(Paper paper, FontFile font, string id, string text)
    {
        paper.Box(id).Width(UnitValue.Stretch()).Height(28).Padding(0, 0, 6, 0)
            .Text(text, EditorTheme.FontSemiBold ?? font).TextColor(EditorTheme.Ink400)
            .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft).IsNotInteractable();
    }

    private static Color ToColor(string hex)
    {
        Color color = MultiProgramSession.ParseColor(hex);
        return Color.FromArgb(255, color.R, color.G, color.B);
    }
}
