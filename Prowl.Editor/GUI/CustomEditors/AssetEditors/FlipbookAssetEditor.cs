// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Infinity.Editor.Core;
using Infinity.Editor.GUI;
using Infinity.Editor.GUI.Panels;
using Infinity.Editor.Inspector;
using Infinity.Editor.Theming;
using Infinity.OrigamiUI;
using Infinity.PaperUI;
using Infinity.PaperUI.LayoutEngine;
using Infinity.Quill;
using Infinity.Runtime;
using Infinity.Runtime.Resources;
using Infinity.Vector;
using Infinity.Vector.Spatial;

using Color = System.Drawing.Color;

namespace Infinity.Editor.Inspector;

/// <summary>
/// The UE5-style flipbook editor: a preview, frame timeline, playback controls, sheet slicing and the
/// two-sided option that the scene spawner reads when a flipbook is dropped into the world.
/// </summary>
[CustomAssetEditor(typeof(Flipbook))]
public class FlipbookAssetEditor : AssetImporterEditor
{
    private bool _playing;
    private float _time;
    private int _selected;
    private Rect _rulerRect;
    private Sprite? _frameToAdd;
    private Texture2D? _sheet;
    private int _columns = 4;
    private int _rows = 4;

    public override void OnGUI(Paper paper, string id, AssetEntry entry, EngineObject? asset)
    {
        if (asset is not Flipbook flipbook) return;

        DrawPreview(paper, id, flipbook);
        DrawPlayback(paper, id, flipbook);
        DrawTimeline(paper, id, flipbook);
        DrawFrames(paper, id, flipbook);
        DrawSheet(paper, id, flipbook);
        DrawSaveRevertBar(paper, id, flipbook);
    }

    private void DrawPreview(Paper paper, string id, Flipbook flipbook)
    {
        float dt = (float)Runtime.Time.UnscaledDeltaTime;
        if (_playing && flipbook.FrameCount > 0)
        {
            _time += dt;
            if (!flipbook.Loop && _time >= flipbook.Duration) { _time = flipbook.Duration; _playing = false; }
        }

        int index = _playing ? flipbook.FrameAt(_time) : Math.Clamp(_selected, 0, Math.Max(0, flipbook.FrameCount - 1));
        Sprite? frame = flipbook.GetFrame(index);

        paper.Box($"{id}_fb_preview")
            .Height(260).Margin(12, 12, 12, 8).Rounded(6).Clip()
            .BackgroundColor(EditorTheme.Neutral300)
            .BorderColor(EditorTheme.BorderSoft).BorderWidth(1)
            .OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) =>
                DrawFrame(canvas, (float)r.Min.X, (float)r.Min.Y, (float)r.Size.X, (float)r.Size.Y, frame, true)));

        if (flipbook.FrameCount > 0)
            Origami.Label(paper, $"{id}_fb_count", $"Frame {index + 1} / {flipbook.FrameCount}   {flipbook.FramesPerSecond:0.#} fps   {flipbook.Duration:0.##} s").Show();
        else
            Origami.Label(paper, $"{id}_fb_empty", "No frames yet. Add a sprite, or slice a sheet below.").Show();
    }

    private void DrawPlayback(Paper paper, string id, Flipbook flipbook)
    {
        using (paper.Row($"{id}_fb_controls").Width(UnitValue.Stretch()).Height(32).Padding(12, 0).Gap(6).Enter())
        {
            Origami.Button(paper, $"{id}_fb_play", _playing ? $"{EditorIcons.Pause}  Pause" : $"{EditorIcons.Play}  Play",
                () => { _playing = !_playing; if (_playing) _time = 0f; }).Show();
            Origami.Button(paper, $"{id}_fb_prev", EditorIcons.ChevronLeft, () => Step(flipbook, -1)).Show();
            Origami.Button(paper, $"{id}_fb_next", EditorIcons.ChevronRight, () => Step(flipbook, 1)).Show();

            paper.Box($"{id}_fb_sp").Width(UnitValue.Stretch());

            Origami.Checkbox(paper, $"{id}_fb_loop", flipbook.Loop, v => Mutate(flipbook, () => flipbook.Loop = v)).Show();
            Origami.Label(paper, $"{id}_fb_loop_l", "Loop").Show();

            Origami.Button(paper, $"{id}_fb_spawn", $"{EditorIcons.ArrowDown}  Spawn in Scene", () =>
            {
                var payload = new AssetDragPayload(flipbook.AssetID, flipbook.Name, string.Empty, typeof(Flipbook));
                HierarchyPanel.SpawnAssetInScene(payload, null, Float3.Zero);
            }).Show();
        }

        EditorGUI.Note(paper, $"{id}_fb_timing",
            $"{flipbook.FrameDuration:0.###} s per frame  |  {flipbook.Duration:0.##} s total  |  {flipbook.FrameCount} frames");

        EditorGUI.Row(paper, $"{id}_fb_fps", "Frames Per Second", () =>
            Origami.NumericField<float>(paper, $"{id}_fb_fps_v", flipbook.FramesPerSecond,
                v => Mutate(flipbook, () => flipbook.FramesPerSecond = MathF.Max(0.01f, v))).Show());
        EditorGUI.Row(paper, $"{id}_fb_mode", "Mode", () =>
            Origami.EnumDropdown(paper, $"{id}_fb_mode_v", flipbook.Mode, v => Mutate(flipbook, () => flipbook.Mode = v)).Show());
        EditorGUI.Row(paper, $"{id}_fb_double", "Two Sided", () =>
            Origami.Checkbox(paper, $"{id}_fb_double_v", flipbook.DoubleSided, v => Mutate(flipbook, () => flipbook.DoubleSided = v)).Show());
    }

    private void DrawTimeline(Paper paper, string id, Flipbook flipbook)
    {
        EditorGUI.SectionHeader(paper, $"{id}_fb_timeline", "Timeline");

        if (flipbook.FrameCount > 0)
        {
            using (paper.Box($"{id}_fb_ruler").Width(UnitValue.Stretch()).Height(20).Padding(12, 0)
                .OnPostLayout((_, rect) => _rulerRect = rect)
                .OnClick(_ => ScrubRuler(paper, flipbook))
                .OnDragging(_ => ScrubRuler(paper, flipbook))
                .Enter())
            {
                paper.Draw((canvas, rect) => DrawRuler(canvas, rect, flipbook));
            }
        }

        using (paper.Row($"{id}_fb_frames").Width(UnitValue.Stretch()).Height(86).Padding(12, 4).Gap(4).Enter())
        {
            if (flipbook.FrameCount == 0)
            {
                Origami.Label(paper, $"{id}_fb_no_frames", "Empty timeline").Show();
                return;
            }

            for (int i = 0; i < flipbook.FrameCount; i++)
            {
                int index = i;
                bool selected = !_playing && index == _selected;
                paper.Box($"{id}_fb_frame_{i}")
                    .Width(74).Height(74)
                    .Rounded(4).Clip()
                    .BackgroundColor(selected ? EditorTheme.Purple700 : EditorTheme.Neutral300)
                    .BorderColor(selected ? EditorTheme.Accent : EditorTheme.BorderSoft).BorderWidth(selected ? 2 : 1)
                    .OnClick(_ => { _selected = index; _playing = false; _time = index * flipbook.FrameDuration; })
                    .OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) =>
                        DrawFrame(canvas, (float)r.Min.X, (float)r.Min.Y, (float)r.Size.X, (float)r.Size.Y, flipbook.GetFrame(index), false)));
            }
        }
    }

    private void ScrubRuler(Paper paper, Flipbook flipbook)
    {
        if (flipbook.FrameCount == 0 || _rulerRect.Size.X <= 0) return;
        float t = (float)((paper.PointerPos.X - _rulerRect.Min.X) / _rulerRect.Size.X);
        int frame = Math.Clamp((int)MathF.Floor(t * flipbook.FrameCount), 0, flipbook.FrameCount - 1);
        _selected = frame;
        _playing = false;
        _time = frame * flipbook.FrameDuration;
    }

    private void DrawRuler(Canvas canvas, Rect rect, Flipbook flipbook)
    {
        int count = flipbook.FrameCount;
        if (count == 0) return;

        float x = (float)rect.Min.X;
        float w = (float)rect.Size.X;
        float y = (float)rect.Min.Y;
        float h = (float)rect.Size.Y;
        float step = w / count;

        canvas.RectFilled(x, y + h - 6f, w, 1f, EditorTheme.ToColor32(EditorTheme.BorderSoft));
        for (int i = 0; i < count; i++)
        {
            float tickX = x + i * step;
            bool major = i == 0 || (i + 1) % 5 == 0 || i == count - 1;
            canvas.RectFilled(tickX, y + h - (major ? 8f : 4f), 1f, major ? 8f : 4f,
                EditorTheme.ToColor32(EditorTheme.Ink300));
            if (major)
                canvas.DrawText(i.ToString(), tickX + 2f, y + 1f,
                    EditorTheme.ToColor32(EditorTheme.Ink300), 8f, EditorTheme.DefaultFont!, 0);
        }

        float playhead = x + Math.Clamp((float)(_time / Math.Max(1e-6, flipbook.Duration)), 0f, 1f) * w;
        canvas.RectFilled(playhead - 1f, y + 2f, 2f, h - 4f, EditorTheme.ToColor32(EditorTheme.Accent));
    }

    private void DrawFrames(Paper paper, string id, Flipbook flipbook)
    {
        EditorGUI.SectionHeader(paper, $"{id}_fb_add", "Frames");

        EditorGUI.Row(paper, $"{id}_fb_pick", "Sprite", () =>
            AnimationNodeFields.AssetField<Sprite>(paper, $"{id}_fb_pick_v", "", _frameToAdd, v => _frameToAdd = v));

        using (paper.Row($"{id}_fb_buttons").Width(UnitValue.Stretch()).Height(30).Padding(12, 0).Gap(6).Enter())
        {
            Origami.Button(paper, $"{id}_fb_addbtn", $"{EditorIcons.Plus}  Add Frame", () =>
            {
                if (_frameToAdd.IsNotValid()) return;
                Mutate(flipbook, () => { flipbook.Add(_frameToAdd!); _selected = flipbook.FrameCount - 1; });
            }).Show();
            Origami.Button(paper, $"{id}_fb_remove", $"{EditorIcons.Trash}  Remove", () =>
                Mutate(flipbook, () => { flipbook.RemoveAt(_selected); _selected = Math.Clamp(_selected, 0, flipbook.FrameCount - 1); }))
                .Disabled(flipbook.FrameCount == 0).Show();
            Origami.Button(paper, $"{id}_fb_left", EditorIcons.ArrowLeft, () =>
                Mutate(flipbook, () => { flipbook.Move(_selected, _selected - 1); _selected = Math.Max(0, _selected - 1); })).Show();
            Origami.Button(paper, $"{id}_fb_right", EditorIcons.ArrowRight, () =>
                Mutate(flipbook, () => { flipbook.Move(_selected, _selected + 1); _selected = Math.Min(flipbook.FrameCount - 1, _selected + 1); })).Show();
            Origami.Button(paper, $"{id}_fb_duplicate", $"{EditorIcons.Clone}  Duplicate", () =>
                Mutate(flipbook, () => { flipbook.Duplicate(_selected); _selected = Math.Min(flipbook.FrameCount - 1, _selected + 1); }))
                .Disabled(flipbook.FrameCount == 0).Show();
            Origami.Button(paper, $"{id}_fb_reverse", $"{EditorIcons.ArrowsRotate}  Reverse", () => Mutate(flipbook, flipbook.Reverse)).Show();
            Origami.Button(paper, $"{id}_fb_sort", $"{EditorIcons.ArrowDownAZ}  Sort", () => Mutate(flipbook, flipbook.SortByName)).Show();
            Origami.Button(paper, $"{id}_fb_clear", $"{EditorIcons.Trash}  Clear", () => Mutate(flipbook, () => { flipbook.Clear(); _selected = 0; }))
                .Disabled(flipbook.FrameCount == 0).Show();
        }
    }

    private void DrawSheet(Paper paper, string id, Flipbook flipbook)
    {
        EditorGUI.SectionHeader(paper, $"{id}_fb_sheet_hdr", "Slice Sprite Sheet");

        EditorGUI.Row(paper, $"{id}_fb_sheet", "Texture", () =>
            AnimationNodeFields.AssetField<Texture2D>(paper, $"{id}_fb_sheet_v", "", _sheet, v => _sheet = v));
        EditorGUI.Row(paper, $"{id}_fb_cols", "Columns", () =>
            Origami.NumericField<int>(paper, $"{id}_fb_cols_v", _columns, v => _columns = Math.Max(1, v)).Show());
        EditorGUI.Row(paper, $"{id}_fb_rows", "Rows", () =>
            Origami.NumericField<int>(paper, $"{id}_fb_rows_v", _rows, v => _rows = Math.Max(1, v)).Show());

        Origami.Button(paper, $"{id}_fb_slice", $"{EditorIcons.BorderAll}  Build Frames From Sheet", () =>
        {
            if (_sheet.IsNotValid()) return;
            Flipbook sliced = Flipbook.FromSpriteSheet(_sheet!, _columns, _rows, name: flipbook.Name);
            Mutate(flipbook, () =>
            {
                flipbook.Frames.Clear();
                flipbook.Frames.AddRange(sliced.Frames);
                _selected = 0;
            });
        }).FullWidth().Show();
    }

    private void Step(Flipbook flipbook, int direction)
    {
        if (flipbook.FrameCount == 0) return;
        _playing = false;
        _selected = Math.Clamp(_selected + direction, 0, flipbook.FrameCount - 1);
        _time = _selected * flipbook.FrameDuration;
    }

    private static void Mutate(Flipbook flipbook, Action change)
    {
        change();
        EditorUtility.SetDirty(flipbook);
    }

    private static void DrawFrame(Canvas canvas, float x, float y, float w, float h, Sprite? sprite, bool checker)
    {
        if (checker)
        {
            const float cell = 12f;
            Vector.Color ca = Color32.FromArgb(255, 44, 40, 54);
            Vector.Color cb = Color32.FromArgb(255, 34, 30, 44);
            for (int cy = 0; cy < (int)MathF.Ceiling(h / cell); cy++)
                for (int cx = 0; cx < (int)MathF.Ceiling(w / cell); cx++)
                {
                    float px = x + cx * cell, py = y + cy * cell;
                    canvas.RectFilled(px, py, MathF.Min(cell, x + w - px), MathF.Min(cell, y + h - py),
                        ((cx + cy) & 1) == 0 ? ca : cb);
                }
        }

        if (sprite.IsNotValid()) return;
        Texture2D? texture = sprite!.Texture;
        if (texture.IsNotValid() || texture!.Width == 0 || sprite.Rect.Width <= 0 || sprite.Rect.Height <= 0) return;

        float pad = checker ? 14f : 3f;
        float maxW = w - pad * 2f, maxH = h - pad * 2f;
        float aspect = sprite.Rect.Width / MathF.Max(1f, sprite.Rect.Height);
        float drawW = maxW, drawH = drawW / aspect;
        if (drawH > maxH) { drawH = maxH; drawW = drawH * aspect; }
        float drawX = x + (w - drawW) / 2f;
        float drawY = y + (h - drawH) / 2f;

        float u0 = sprite.Rect.X / (float)texture.Width, u1 = sprite.Rect.MaxX / (float)texture.Width;
        float v0 = sprite.Rect.Y / (float)texture.Height, v1 = sprite.Rect.MaxY / (float)texture.Height;
        float sx = drawW / MathF.Max(1e-6f, u1 - u0);
        float sy = -drawH / MathF.Max(1e-6f, v1 - v0);

        canvas.SetBrushTexture(texture);
        canvas.SetBrushTextureTransform(
            Transform2D.CreateTranslation(drawX - u0 * sx, drawY + drawH - v0 * sy) *
            Transform2D.CreateScale(sx, sy));
        canvas.RectFilled(drawX, drawY, drawW, drawH, Color32.FromArgb(255, 255, 255, 255));
        canvas.ClearBrushTexture();
    }
}
