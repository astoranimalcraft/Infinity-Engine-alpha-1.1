// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Infinity.Editor.Theming;
using Infinity.PaperUI;
using Infinity.PaperUI.Events;
using Infinity.PaperUI.LayoutEngine;
using Infinity.Quill;
using Infinity.Rosetta;
using Infinity.Runtime;
using Infinity.Runtime.Animation;
using Infinity.Vector;
using Infinity.Vector.Geometry;

using Color = System.Drawing.Color;
using Color32 = Infinity.Vector.Color32;

namespace Infinity.Editor.GUI.Panels;

/// <summary>
/// The painted half of the timeline: the ruler, the grid, the key marks and the playhead on one strip, plus the
/// curve editor under it. The strip is a single canvas rather than a row of boxes so the ticks, the grid and the
/// playhead line up to the pixel at any zoom.
/// </summary>
public partial class AnimationTimelinePanel
{
    private ElementHandle _stripHandle;
    private ElementHandle _curveHandle;

    // Channels the user turned off in the curve view, as one bit each, and the range the drag is holding still.
    private int _curveHidden;
    private bool _curveFrozen;
    private float _curveFrozenMin = -1f;
    private float _curveFrozenMax = 1f;

    /// <summary>Seconds across the strip at the current zoom.</summary>
    private float VisibleSeconds => MathF.Max(0.01f, _stripWidth / _zoom);

    private float TimeToX(float time) => (time - _scroll) * _zoom;

    private float XToTime(float x) => _scroll + x / _zoom;

    private void ClampScroll()
    {
        float duration = _player.IsValid() ? _player!.Take.Duration : 1f;
        float visible = VisibleSeconds;
        _scroll = Math.Clamp(_scroll, -visible * 0.25f, MathF.Max(-visible * 0.25f, duration - visible * 0.25f));
    }

    // ── Input ───────────────────────────────────────────────────────────

    private void StripPress(ClickEvent e, Paper paper)
    {
        if (_player.IsNotValid()) return;
        paper.SetFocus(_stripHandle);

        float x = (float)e.RelativePosition.X;
        float y = (float)e.RelativePosition.Y;
        AnimationTake take = _player!.Take;
        bool additive = IsShiftDown(paper);

        if (y >= RulerHeight)
        {
            int row = (int)((y - RulerHeight) / RowHeight);
            if (row >= 0 && row < take.Tracks.Count)
            {
                AnimationTrack track = take.Tracks[row];
                _track = track;

                float hit = KeyAt(track, XToTime(x));
                if (hit >= 0f)
                {
                    var reference = new KeyRef(track, hit);
                    if (!_selection.Contains(reference))
                    {
                        if (!additive) _selection.Clear();
                        _selection.Add(reference);
                    }

                    _drag = DragMode.Keys;
                    _dragGrab = XToTime(x) - hit;
                    _dragStartTimes = new float[_selection.Count];
                    for (int i = 0; i < _selection.Count; i++) _dragStartTimes[i] = _selection[i].Time;
                    Seek(hit);
                    return;
                }
            }
        }

        if (!additive) _selection.Clear();
        _drag = DragMode.Scrub;
        Seek(XToTime(x));
    }

    private void StripDrag(DragEvent e)
    {
        if (_player.IsNotValid()) return;

        float x = (float)e.RelativePosition.X;
        if (_drag == DragMode.Scrub)
        {
            Seek(XToTime(x));
            return;
        }

        if (_drag != DragMode.Keys || _selection.Count == 0) return;

        AnimationTake take = _player!.Take;
        float pointer = XToTime(x) - _dragGrab;
        float delta = pointer - _dragStartTimes[0];

        for (int i = 0; i < _selection.Count; i++)
        {
            KeyRef key = _selection[i];
            float target = Math.Clamp(take.Snap(_dragStartTimes[i] + delta), 0f, take.Duration);
            if (MathF.Abs(target - key.Time) < 1e-5f) continue;

            key.Track.MoveKeys(key.Time, target);
            _selection[i] = new KeyRef(key.Track, target);
        }

        _player.Sample(_playhead);
    }

    private void StripScroll(ScrollEvent e, Paper paper)
    {
        float dx = e.DeltaX;
        float dy = e.Delta;

        if (MathF.Abs(dx) < 0.001f && IsShiftDown(paper))
        {
            dx = dy;
            dy = 0f;
        }

        if (MathF.Abs(dx) > 0.001f)
        {
            _scroll -= dx * 0.02f * VisibleSeconds;
            ClampScroll();
            return;
        }

        if (MathF.Abs(dy) < 0.001f) return;

        float anchor = (float)e.RelativePosition.X;
        float at = XToTime(anchor);
        _zoom = Math.Clamp(_zoom * (dy > 0f ? 1f / 1.15f : 1.15f), MinZoom, MaxZoom);
        _scroll = at - anchor / _zoom;
        ClampScroll();
    }

    private void CurvePress(ClickEvent e, Paper paper)
    {
        if (_track == null || _player.IsNotValid()) return;
        paper.SetFocus(_curveHandle);

        float x = (float)e.RelativePosition.X;
        float y = (float)e.RelativePosition.Y;
        float width = MathF.Max(1f, (float)e.ElementRect.Size.X);
        float height = MathF.Max(1f, (float)e.ElementRect.Size.Y);

        // The channel chips sit over the plot, so they take the click before any key does.
        for (int c = 0; c < Math.Min(4, _track.Channels); c++)
        {
            if (!CurveChipHit(c, x, y)) continue;
            _curveHidden ^= 1 << c;
            return;
        }

        // The value range is fitted from what is on screen. Fitting it again while a drag moves a key would move the
        // mapping under the pointer, so it stays where it was for the whole drag.
        _curveFrozen = true;
        _curveFrozenMin = _curveMin;
        _curveFrozenMax = _curveMax;

        float best = 12f;
        int channel = -1;
        int index = -1;

        for (int c = 0; c < _track.Channels; c++)
        {
            if (CurveChannelHidden(c)) continue;
            AnimationCurve curve = _track.CurveFor(c);
            for (int i = 0; i < curve.Count; i++)
            {
                (float px, float py) = CurveToPixel(curve[i].Time, curve[i].Value, width, height);
                float dx = px - x;
                float dy = py - y;
                float distance = MathF.Sqrt(dx * dx + dy * dy);
                if (distance >= best) continue;
                best = distance;
                channel = c;
                index = i;
            }
        }

        if (channel < 0)
        {
            // Empty space adds a key there, so a curve can be built without going back to the strip for every key.
            channel = FirstVisibleCurveChannel();
            if (channel < 0)
            {
                _curveFrozen = false;
                return;
            }

            float time = Math.Clamp(_player!.Take.Snap(XToTime(x)), 0f, _player.Take.Duration);
            index = _track.CurveFor(channel).AddKey(time, PixelToValue(y, height));
        }

        _curveChannel = channel;
        _curveKey = index;
        _tangentHandle = 0;

        // A click on one of the selected key's tangent handles drags that tangent instead of the key.
        if (TryTangentHandle(channel, index, width, height, x, y, out int handle))
        {
            _tangentHandle = handle;
            _curveFrozen = true;
        }

        _drag = DragMode.Curve;

        _playhead = Math.Clamp(_track.CurveFor(channel)[index].Time, 0f, _player!.Take.Duration);
        _player.Sample(_playhead);
    }

    private void CurveDrag(DragEvent e)
    {
        if (_drag != DragMode.Curve || _track == null || _curveChannel < 0) return;

        AnimationCurve curve = _track.CurveFor(_curveChannel);
        if (_curveKey < 0 || _curveKey >= curve.Count) return;

        float height = MathF.Max(1f, (float)e.ElementRect.Size.Y);
        float width = MathF.Max(1f, (float)e.ElementRect.Size.X);
        float time = Math.Clamp(_player!.Take.Snap(XToTime((float)e.RelativePosition.X)), 0f, _player.Take.Duration);

        Keyframe key = curve[_curveKey];
        if (_tangentHandle != 0)
        {
            float value = PixelToValue((float)e.RelativePosition.Y, height);
            key.Interpolation = CurveInterpolation.CubicSpline;
            if (_tangentHandle > 0)
            {
                float span = time - key.Time;
                if (MathF.Abs(span) > 1e-4f) key.OutTangent = (value - key.Value) / span;
            }
            else
            {
                float span = key.Time - time;
                if (MathF.Abs(span) > 1e-4f) key.InTangent = (key.Value - value) / span;
            }
            _curveKey = curve.MoveKey(_curveKey, key);
            _player.Sample(_playhead);
            return;
        }

        key.Time = time;
        key.Value = PixelToValue((float)e.RelativePosition.Y, height);
        _curveKey = curve.MoveKey(_curveKey, key);
        _playhead = time;

        _player.Sample(_playhead);
    }

    private void CurveDragEnd()
    {
        _drag = DragMode.None;
        _tangentHandle = 0;
        _curveFrozen = false;
    }

    /// <summary>The tangent handle under a point: -1 for the in handle, 1 for the out handle, 0 for neither.</summary>
    private bool TryTangentHandle(int channel, int index, float width, float height, float x, float y, out int handle)
    {
        handle = 0;
        if (_track == null || channel < 0 || index < 0) return false;

        AnimationCurve curve = _track.CurveFor(channel);
        if (index >= curve.Count || curve[index].Interpolation != CurveInterpolation.CubicSpline) return false;

        Keyframe key = curve[index];
        const float hit = 8f;
        for (int side = -1; side <= 1; side += 2)
        {
            (float hx, float hy) = TangentToPixel(key, side < 0, width, height);
            if (MathF.Abs(x - hx) <= hit && MathF.Abs(y - hy) <= hit)
            {
                handle = side;
                return true;
            }
        }
        return false;
    }

    private (float X, float Y) TangentToPixel(Keyframe key, bool inHandle, float width, float height)
    {
        (float kx, float ky) = CurveToPixel(key.Time, key.Value, width, height);
        float pixelsPerValue = MathF.Max(1f, height - 16f) / MathF.Max(1e-4f, CurveRangeMax - CurveRangeMin);
        float dx = inHandle ? -TangentHandleLength : TangentHandleLength;
        float seconds = dx / MathF.Max(1f, width) * VisibleSeconds;
        float slope = inHandle ? key.InTangent : key.OutTangent;
        return (kx + dx, ky - slope * seconds * pixelsPerValue);
    }

    /// <summary>Removes the key selected in the curve view, if any, and keeps the selection on a neighbour.</summary>
    private void DeleteCurveKey()
    {
        if (_track == null || _curveChannel < 0 || _curveKey < 0) return;

        AnimationCurve curve = _track.CurveFor(_curveChannel);
        if (_curveKey >= curve.Count) return;

        curve.RemoveKey(_curveKey);
        _curveKey = curve.Count == 0 ? -1 : Math.Min(_curveKey, curve.Count - 1);
        if (_player.IsValid()) _player!.Sample(_playhead);
    }

    private bool CurveChannelHidden(int channel) => (_curveHidden & (1 << channel)) != 0;

    private int FirstVisibleCurveChannel()
    {
        for (int c = 0; c < _track!.Channels; c++)
            if (!CurveChannelHidden(c)) return c;
        return -1;
    }

    /// <summary>Where a channel's toggle chip sits, relative to the curve view's top-left corner.</summary>
    private static (float X, float Y, float W, float H) CurveChipRect(int channel)
        => (8f + channel * 24f, 4f, 20f, 13f);

    private static bool CurveChipHit(int channel, float x, float y)
    {
        (float cx, float cy, float cw, float ch) = CurveChipRect(channel);
        return x >= cx && x <= cx + cw && y >= cy && y <= cy + ch;
    }

    private static bool IsShiftDown(Paper paper)
        => paper.IsKeyDown(PaperKey.LeftShift) || paper.IsKeyDown(PaperKey.RightShift);

    /// <summary>The time of the key nearest <paramref name="time"/> on the track, or -1 when none is close enough.</summary>
    private float KeyAt(AnimationTrack track, float time)
    {
        float tolerance = 7f / _zoom;
        float best = -1f;
        float bestDistance = float.MaxValue;

        foreach (float key in track.KeyTimes())
        {
            float distance = MathF.Abs(key - time);
            if (distance > tolerance || distance >= bestDistance) continue;
            best = key;
            bestDistance = distance;
        }

        return best;
    }

    // ── Painting ────────────────────────────────────────────────────────

    private void PaintStrip(Canvas canvas, Rect rect, Scribe.FontFile font)
    {
        if (_player.IsNotValid()) return;

        float left = (float)rect.Min.X;
        float top = (float)rect.Min.Y;
        float width = MathF.Max(1f, (float)rect.Size.X);
        float height = MathF.Max(1f, (float)rect.Size.Y);
        AnimationTake take = _player!.Take;
        float rowsTop = top + RulerHeight;

        canvas.SaveState();

        for (int i = 0; i < take.Tracks.Count; i++)
        {
            float y = rowsTop + i * RowHeight;
            if (i % 2 == 1) canvas.RectFilled(left, y, width, RowHeight, EditorTheme.ToColor32(EditorTheme.Ink700, 0.05f));
            if (ReferenceEquals(take.Tracks[i], _track)) canvas.RectFilled(left, y, width, RowHeight, EditorTheme.ToColor32(EditorTheme.Accent, 0.10f));
            canvas.RectFilled(left, y + RowHeight - 1f, width, 1f, EditorTheme.ToColor32(EditorTheme.BorderSoft, 0.55f));
        }

        ShadeOutside(canvas, left, top, width, height, take);
        DrawGrid(canvas, left, rowsTop, width, height - RulerHeight, font);

        for (int i = 0; i < take.Tracks.Count; i++)
        {
            AnimationTrack track = take.Tracks[i];
            float center = rowsTop + i * RowHeight + RowHeight * 0.5f;

            foreach (float time in track.KeyTimes())
            {
                float x = left + TimeToX(time);
                if (x < left - 8f || x > left + width + 8f) continue;

                bool selected = false;
                foreach (KeyRef key in _selection)
                {
                    if (!ReferenceEquals(key.Track, track) || MathF.Abs(key.Time - time) > AnimationTrack.TimeEpsilon) continue;
                    selected = true;
                    break;
                }

                Color32 fill = selected
                    ? EditorTheme.ToColor32(Color.White, 1f)
                    : EditorTheme.ToColor32(track.Enabled ? EditorTheme.Blue400 : EditorTheme.Ink300, 1f);

                Diamond(canvas, x, center, selected ? KeyRadius + 1.5f : KeyRadius, fill);
                if (selected) Diamond(canvas, x, center, KeyRadius - 1f, EditorTheme.ToColor32(EditorTheme.Accent, 1f));
            }
        }

        DrawRuler(canvas, left, top, width, font, take);
        DrawPlayhead(canvas, left, top, width, height, font);

        if (take.Tracks.Count == 0)
        {
            canvas.DrawText("No tracks yet. Press Add Track, or All Tracks with objects selected.",
                left + 12f, rowsTop + 6f, EditorTheme.ToColor32(EditorTheme.Ink300, 1f), 10f, font);
        }

        canvas.RestoreState();
    }

    private void ShadeOutside(Canvas canvas, float left, float top, float width, float height, AnimationTake take)
    {
        float start = left + TimeToX(0f);
        float end = left + TimeToX(take.Duration);
        Color32 shade = EditorTheme.ToColor32(EditorTheme.Neutral200, 0.55f);

        if (start > left) canvas.RectFilled(left, top, MathF.Min(start - left, width), height, shade);
        if (end < left + width) canvas.RectFilled(MathF.Max(end, left), top, left + width - MathF.Max(end, left), height, shade);
    }

    private void DrawGrid(Canvas canvas, float left, float top, float width, float height, Scribe.FontFile font)
    {
        float step = MajorStep();
        float first = MathF.Floor(_scroll / step) * step;
        Color32 line = EditorTheme.ToColor32(EditorTheme.Ink400, 0.10f);

        for (float t = first; t <= _scroll + VisibleSeconds + step; t += step)
        {
            float x = left + TimeToX(t);
            if (x < left || x > left + width) continue;
            canvas.RectFilled(x, top, 1f, height, line);
        }
    }

    private void DrawRuler(Canvas canvas, float left, float top, float width, Scribe.FontFile font, AnimationTake take)
    {
        canvas.RectFilled(left, top, width, RulerHeight, EditorTheme.ToColor32(EditorTheme.Neutral200, 0.9f));
        canvas.RectFilled(left, top + RulerHeight - 1f, width, 1f, EditorTheme.ToColor32(EditorTheme.BorderSoft, 1f));

        float major = MajorStep();
        float minor = major / 5f;
        float first = MathF.Floor(_scroll / minor) * minor;

        for (float t = first; t <= _scroll + VisibleSeconds + minor; t += minor)
        {
            float x = left + TimeToX(t);
            if (x < left - 1f || x > left + width + 1f) continue;

            bool isMajor = MathF.Abs(t / major - MathF.Round(t / major)) < 1e-3f;
            float tick = isMajor ? 9f : 5f;
            Color32 ink = EditorTheme.ToColor32(isMajor ? EditorTheme.Ink500 : EditorTheme.Ink300, isMajor ? 0.95f : 0.5f);

            canvas.RectFilled(x, top + RulerHeight - 1f - tick, 1f, tick, ink);
            if (isMajor && t > -1e-3f)
                canvas.DrawText($"{t:0.##}s", x + 3f, top + 3f, EditorTheme.ToColor32(EditorTheme.Ink400, 1f), 9f, font);
        }

        float end = left + TimeToX(take.Duration);
        if (end >= left && end <= left + width)
            canvas.RectFilled(end - 1f, top, 2f, RulerHeight, EditorTheme.ToColor32(EditorTheme.Amber400, 0.8f));
    }

    private void DrawPlayhead(Canvas canvas, float left, float top, float width, float height, Scribe.FontFile font)
    {
        float x = left + TimeToX(_playhead);
        if (x < left - 1f || x > left + width + 1f) return;

        Color32 accent = EditorTheme.ToColor32(EditorTheme.Accent, 1f);
        canvas.RectFilled(x - 0.75f, top, 1.5f, height, EditorTheme.ToColor32(EditorTheme.Accent, 0.95f));
        canvas.RoundedRectFilled(x - 5f, top, 10f, 11f, 3f, 3f, 0f, 0f, accent);

        string label = $"{_playhead:0.00}s";
        float labelWidth = canvas.MeasureText(label, 9f, font).X + 10f;
        float labelX = x + 8f;
        if (labelX + labelWidth > left + width) labelX = x - 8f - labelWidth;

        canvas.RoundedRectFilled(labelX, top + 13f, labelWidth, 14f, 3f, 3f, 3f, 3f, EditorTheme.ToColor32(EditorTheme.Neutral200, 0.92f));
        canvas.DrawText(label, labelX + 5f, top + 16f, EditorTheme.ToColor32(EditorTheme.Ink500, 1f), 9f, font);
    }

    /// <summary>Seconds between labelled ticks: the smallest nice step that keeps labels apart at this zoom.</summary>
    private float MajorStep()
    {
        float[] steps = [0.02f, 0.05f, 0.1f, 0.25f, 0.5f, 1f, 2f, 5f, 10f, 30f, 60f, 120f, 300f];
        foreach (float step in steps)
            if (step * _zoom >= 64f) return step;
        return steps[^1];
    }

    private static void Diamond(Canvas canvas, float x, float y, float radius, Color32 fill)
    {
        canvas.SetFillColor(fill);
        canvas.BeginPath();
        canvas.MoveTo(x, y - radius);
        canvas.LineTo(x + radius, y);
        canvas.LineTo(x, y + radius);
        canvas.LineTo(x - radius, y);
        canvas.ClosePath();
        canvas.Fill();
    }

    // ── Curves ──────────────────────────────────────────────────────────

    private (float X, float Y) CurveToPixel(float time, float value, float width, float height)
    {
        float range = MathF.Max(1e-4f, CurveRangeMax - CurveRangeMin);
        float x = TimeToX(time);
        float y = (height - 8f) - (value - CurveRangeMin) / range * (height - 16f) + 4f;
        return (x, y);
    }

    private float PixelToValue(float y, float height)
    {
        float range = MathF.Max(1e-4f, CurveRangeMax - CurveRangeMin);
        return CurveRangeMin + (height - 8f - y) / MathF.Max(1f, height - 16f) * range;
    }

    /// <summary>The value range in use: the one held still for the drag, or the one fitted to what is on screen.</summary>
    private float CurveRangeMin => _curveFrozen ? _curveFrozenMin : _curveMin;

    private float CurveRangeMax => _curveFrozen ? _curveFrozenMax : _curveMax;

    private void PaintCurves(Canvas canvas, Rect rect, Scribe.FontFile font)
    {
        float left = (float)rect.Min.X;
        float top = (float)rect.Min.Y;
        float width = MathF.Max(1f, (float)rect.Size.X);
        float height = MathF.Max(1f, (float)rect.Size.Y);

        canvas.SaveState();
        canvas.IntersectScissor(left, top, width, height);

        if (_track == null || _track.IsKeyed)
        {
            string message = _track == null ? "Select a track to edit its curves." : "This track keys sprites or maps, so it has no curves.";
            canvas.DrawText(message, left + 12f, top + height * 0.5f - 6f, EditorTheme.ToColor32(EditorTheme.Ink300, 1f), 10f, font);
            canvas.RestoreState();
            return;
        }

        if (!_curveFrozen) FitCurveRange();
        DrawCurveGrid(canvas, left, top, width, height);

        for (int c = 0; c < _track.Channels; c++)
        {
            if (CurveChannelHidden(c)) continue;

            AnimationCurve curve = _track.CurveFor(c);
            if (curve.Count == 0) continue;

            Color32 ink = EditorTheme.ToColor32(ChannelColor(c), 1f);
            canvas.SetStrokeColor(ink);
            canvas.SetStrokeWidth(1.6f);
            canvas.BeginPath();

            const int steps = 96;
            for (int i = 0; i <= steps; i++)
            {
                float time = _scroll + VisibleSeconds * i / steps;
                (float px, float py) = CurveToPixel(time, curve.Evaluate(time), width, height);
                if (i == 0) canvas.MoveTo(left + px, top + py);
                else canvas.LineTo(left + px, top + py);
            }

            canvas.Stroke();

            for (int i = 0; i < curve.Count; i++)
            {
                float time = curve[i].Time;
                if (time < _scroll - 0.001f || time > _scroll + VisibleSeconds + 0.001f) continue;

                (float px, float py) = CurveToPixel(time, curve[i].Value, width, height);
                bool selected = c == _curveChannel && i == _curveKey;
                if (selected)
                {
                    canvas.CircleFilled(left + px, top + py, 7f, EditorTheme.ToColor32(EditorTheme.Neutral700, 0.9f));
                    canvas.CircleFilled(left + px, top + py, 4.5f, EditorTheme.ToColor32(Color.White, 1f));
                    if (curve[i].Interpolation == CurveInterpolation.CubicSpline)
                        DrawTangentHandles(canvas, left, top, width, height, curve[i], ink);
                }
                else
                {
                    canvas.CircleFilled(left + px, top + py, 3.5f, ink);
                }
            }
        }

        float playheadX = left + TimeToX(_playhead);
        if (playheadX >= left && playheadX <= left + width)
            canvas.RectFilled(playheadX - 0.75f, top, 1.5f, height, EditorTheme.ToColor32(EditorTheme.Accent, 0.8f));

        DrawCurveChips(canvas, left, top, font);
        DrawCurveReadout(canvas, left, top, width, font);

        canvas.RestoreState();
    }

    private void DrawTangentHandles(Canvas canvas, float left, float top, float width, float height,
        Keyframe key, Color32 ink)
    {
        (float kx, float ky) = CurveToPixel(key.Time, key.Value, width, height);
        for (int side = -1; side <= 1; side += 2)
        {
            (float hx, float hy) = TangentToPixel(key, side < 0, width, height);
            canvas.SetStrokeColor(EditorTheme.ToColor32(EditorTheme.Ink300, 0.9f));
            canvas.SetStrokeWidth(1.1f);
            canvas.BeginPath();
            canvas.MoveTo(left + kx, top + ky);
            canvas.LineTo(left + hx, top + hy);
            canvas.Stroke();
            canvas.CircleFilled(left + hx, top + hy, 3.4f, ink);
        }
    }

    /// <summary>The per-channel toggles, drawn over the plot so a busy track can be read one channel at a time.</summary>
    private void DrawCurveChips(Canvas canvas, float left, float top, Scribe.FontFile font)
    {
        for (int c = 0; c < Math.Min(4, _track!.Channels); c++)
        {
            (float cx, float cy, float cw, float ch) = CurveChipRect(c);
            bool hidden = CurveChannelHidden(c);
            canvas.RoundedRectFilled(left + cx, top + cy, cw, ch, 3f,
                EditorTheme.ToColor32(EditorTheme.Neutral600, hidden ? 0.35f : 0.85f));
            canvas.DrawText(CurveChannelLabel(c), left + cx + 6.5f, top + cy + 2f,
                EditorTheme.ToColor32(ChannelColor(c), hidden ? 0.3f : 1f), 9f, font);
        }
    }

    /// <summary>The time and value of the selected key, so a drag can be read to the number instead of by eye.</summary>
    private void DrawCurveReadout(Canvas canvas, float left, float top, float width, Scribe.FontFile font)
    {
        if (_track == null || _curveChannel < 0 || _curveKey < 0) return;

        AnimationCurve curve = _track.CurveFor(_curveChannel);
        if (_curveKey >= curve.Count) return;

        Keyframe key = curve[_curveKey];
        string text = $"{CurveChannelLabel(_curveChannel)}  {key.Time:0.00}s  {key.Value:0.###}";
        float boxWidth = 14f + text.Length * 5.2f;
        float x = left + width - boxWidth - 6f;
        canvas.RoundedRectFilled(x, top + 4f, boxWidth, 13f, 3f, EditorTheme.ToColor32(EditorTheme.Neutral700, 0.85f));
        canvas.DrawText(text, x + 5f, top + 6f, EditorTheme.ToColor32(EditorTheme.Ink200, 1f), 9f, font);
    }

    private void FitCurveRange()
    {
        float min = float.MaxValue;
        float max = float.MinValue;
        float end = _scroll + VisibleSeconds;

        for (int c = 0; c < _track!.Channels; c++)
        {
            AnimationCurve curve = _track.CurveFor(c);
            for (int i = 0; i < curve.Count; i++)
            {
                float time = curve[i].Time;
                if (time < _scroll - 0.001f || time > end + 0.001f) continue;
                min = MathF.Min(min, curve[i].Value);
                max = MathF.Max(max, curve[i].Value);
            }
        }

        if (min > max)
        {
            _curveMin = -1f;
            _curveMax = 1f;
            return;
        }

        // Centred with room above and below, and never narrower than half a unit: a range that hugs the keys makes the
        // curve jump every frame as one of them moves.
        float span = MathF.Max(max - min, 0.5f);
        float centre = (min + max) * 0.5f;
        _curveMin = centre - span * 0.65f;
        _curveMax = centre + span * 0.65f;
    }

    private void DrawCurveGrid(Canvas canvas, float left, float top, float width, float height)
    {
        for (int i = 0; i <= 4; i++)
        {
            float y = top + height * i / 4f;
            canvas.RectFilled(left, y, width, 1f, EditorTheme.ToColor32(EditorTheme.Ink400, 0.08f));
        }

        if (CurveRangeMin < 0f && CurveRangeMax > 0f)
        {
            float range = CurveRangeMax - CurveRangeMin;
            float y = top + (height - 8f) - (0f - CurveRangeMin) / range * (height - 16f) + 4f;
            canvas.RectFilled(left, y, width, 1f, EditorTheme.ToColor32(EditorTheme.Ink400, 0.25f));
        }
    }

    private static Color ChannelColor(int channel) => channel switch
    {
        0 => EditorTheme.Red400,
        1 => EditorTheme.Green400,
        2 => EditorTheme.Blue400,
        _ => EditorTheme.Amber400,
    };

    private static string ChannelName(int channel) => channel switch
    {
        0 => "X",
        1 => "Y",
        2 => "Z",
        _ => "W",
    };

    /// <summary>How a channel is labelled: colours read as channels, everything else as an axis.</summary>
    private string CurveChannelLabel(int channel) => _track?.Kind == AnimationTrackKind.TintColor
        ? channel switch { 0 => "R", 1 => "G", 2 => "B", _ => "A" }
        : ChannelName(channel);
}
