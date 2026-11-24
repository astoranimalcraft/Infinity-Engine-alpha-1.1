// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Infinity.Editor.Build;
using Infinity.Editor.GUI;
using Infinity.Editor.Theming;
using Infinity.OrigamiUI;
using Infinity.PaperUI;
using Infinity.PaperUI.LayoutEngine;
using Infinity.Rosetta;
using Infinity.Scribe;

using Color = System.Drawing.Color;
using TextAlignment = Infinity.PaperUI.TextAlignment;

namespace Infinity.Editor.GUI.Popups;

/// <summary>
/// Covers the editor while a build runs, showing its progress and the only control that makes sense
/// during one.
/// </summary>
/// <remarks>
/// A build reads the asset database and the script assemblies from a background thread. Leaving the
/// editor usable during one invites importing or deleting an asset the build is halfway through
/// packaging, so the backdrop is the point rather than decoration. It is also why this modal cannot be
/// dismissed: closing it would lift the lock without stopping the build.
/// </remarks>
public sealed class BuildProgressModal : IModal
{
    private const float Width = 420f;

    private readonly BuildProgress _progress;
    private bool _notified;

    private BuildProgressModal(BuildProgress progress) => _progress = progress;

    /// <summary>
    /// Shows the lock for a running build. It takes itself down when the build finishes, and also appears for
    /// a build that already has, so its toast reports the failure instead of leaving a click with no visible result.
    /// </summary>
    public static void Show(BuildProgress progress)
    {
        // A build can fail before this frame reaches the modal stack (a missing scene, an unresolved
        // icon, a publish error). Showing the modal even for a finished build gives it one draw to
        // report that result instead of leaving the user with a window that appeared to do nothing.
        Modal.Push(new BuildProgressModal(progress));
    }

    public bool CloseOnBackdrop => false;
    public bool CloseOnEscape => false;

    /// <summary> Draws the build progress modal, showing the build status, a progress bar, and a cancel button. Removes itself from the modal stack when the build completes. </summary>
    public void Draw(Paper paper, int layer, int stackIndex)
    {
        // Owned here rather than by the Build window, so the lock is correct even when that window is
        // closed and nothing else is polling the build.
        if (_progress.IsComplete)
        {
            if (!_notified)
            {
                _notified = true;
                BuildResult? result = _progress.Result;
                if (result?.Cancelled == true)
                    Toasts.Info(Loc.Get("build.toast_cancelled"), Loc.Get("build.toast_cancelled_msg"));
                else if (result?.Success == true)
                    Toasts.Success(Loc.Get("build.toast_succeeded"), Loc.Get("build.toast_succeeded_msg"));
                else
                    Toasts.Error(Loc.Get("build.toast_failed"),
                        result?.Errors.Count > 0 ? result.Errors[0] : Loc.Get("build.toast_failed_msg"));
            }
            Modal.Remove(this);
            return;
        }

        OrigamiTheme theme = Origami.Current;
        FontFile? font = EditorTheme.DefaultFont;
        if (font == null) return;

        float screenW = (float)paper.ScreenRect.Size.X;
        float screenH = (float)paper.ScreenRect.Size.Y;

        ElementBuilder container = paper.Column("bpm_root")
            .PositionType(PositionType.SelfDirected)
            .Position((screenW - Width) / 2, screenH * 0.36f)
            .Width(Width).Height(UnitValue.Auto)
            .BackgroundColor(theme.Popover)
            .BorderColor(theme.BorderStrong).BorderWidth(1)
            .Rounded(theme.Metrics.ContainerRounding).Clip()
            .DropShadow(0, 24, 64, 0, EditorTheme.Shadow)
            .Layer(layer)
            .StopEventPropagation();

        using (container.Enter())
        {
            DrawHeader(paper, font);
            DrawBody(paper, font);
        }
    }

    private static void DrawHeader(Paper paper, FontFile font)
    {
        OrigamiTheme theme = Origami.Current;
        float headH = theme.Metrics.FontSize + 18f;

        using (paper.Row("bpm_head").Width(UnitValue.Stretch()).Height(headH)
            .BackgroundColor(theme.Glass).RoundedTop(theme.Metrics.ContainerRounding)
            .Padding(13, 13, 0, 0).Gap(8).Enter())
        {
            paper.Box("bpm_ico").Width(16).Height(headH).IsNotInteractable()
                .Text(EditorIcons.Hammer, font).TextColor(EditorTheme.Accent)
                .FontSize(theme.Metrics.FontSize).Alignment(TextAlignment.MiddleCenter);

            paper.Box("bpm_title").Width(UnitValue.Stretch()).Height(headH).IsNotInteractable()
                .Text(Loc.Get("build.building"), EditorTheme.FontSemiBold ?? font)
                .TextColor(EditorTheme.Ink500)
                .FontSize(theme.Metrics.FontSize).Alignment(TextAlignment.MiddleLeft);
        }

        paper.Box("bpm_hdiv").Width(UnitValue.Stretch()).Height(1)
            .BackgroundColor(theme.BorderSoft).IsNotInteractable();
    }

    private void DrawBody(Paper paper, FontFile font)
    {
        bool cancelling = _progress.IsCancelled;
        FontFile mono = EditorTheme.FontMono ?? font;

        using (paper.Column("bpm_body").Width(UnitValue.Stretch()).Height(UnitValue.Auto)
            .Padding(13, 13, 13, 13).Gap(10).Enter())
        {
            // The last line the build logged, which is the only part of it worth a glance mid build.
            paper.Box("bpm_state").Width(UnitValue.Stretch()).Height(16).IsNotInteractable()
                .Text(StatusLine(), mono).TextColor(EditorTheme.Ink400)
                .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft).TextTruncate();

            IReadOnlyList<string> log = _progress.Log;
            int first = Math.Max(0, log.Count - 5);
            for (int i = first; i < log.Count - 1; i++)
                paper.Box($"bpm_log_{i}").Width(UnitValue.Stretch()).Height(14).IsNotInteractable()
                    .Text(log[i], mono).TextColor(EditorTheme.Ink300)
                    .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft).TextTruncate();

            Origami.ProgressBar(paper, "bpm_bar", _progress.ProgressValue)
                .Thickness(8).ShowPercent("F0").Show();

            using (paper.Row("bpm_actions").Width(UnitValue.Stretch()).Height(UnitValue.Auto)
                .JustifyContent(LayoutJustification.End).Enter())
            {
                if (cancelling)
                {
                    paper.Box("bpm_cancelling").Width(UnitValue.Auto).Height(22).IsNotInteractable()
                        .Text(Loc.Get("build.cancelling"), font).TextColor(EditorTheme.Ink300)
                        .FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleRight);
                }
                else
                {
                    EditorGUI.CtaButton(paper, "bpm_cancel", $"{EditorIcons.Xmark}  {Loc.Get("build.cancel")}",
                        EditorTheme.Red500, _progress.Cancel);
                }
            }
        }
    }

    private string StatusLine()
    {
        string message = _progress.GetState()?.Message ?? "";
        int newline = message.IndexOf('\n');
        return (newline >= 0 ? message[..newline] : message).Trim();
    }
}
