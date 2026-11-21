// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;

using Infinity.Editor.Core;
using Infinity.Editor.GUI.SceneView;
using Infinity.Editor.Theming;
using Infinity.OrigamiUI;
using Infinity.PaperUI;
using Infinity.PaperUI.Events;
using Infinity.PaperUI.LayoutEngine;
using Infinity.Quill;
using Infinity.Rosetta;
using Infinity.Runtime;
using Infinity.Runtime.Animation;
using Infinity.Runtime.Resources;
using Infinity.Vector;
using Infinity.Vector.Geometry;

using Color = System.Drawing.Color;

namespace Infinity.Editor.GUI.Panels;

/// <summary>
/// The animation timeline: plays, scrubs and keys an <see cref="AnimationTake"/> on the selected
/// <see cref="AnimationPlayer"/>. Every object with a player keeps its own take, and the picker in the toolbar
/// switches between them. Rows are tracks, marks are keys, and the playhead runs the height of the strip in the
/// theme accent so the current moment reads at a glance.
/// </summary>
/// <remarks>
/// Input: drag the ruler or the strip to scrub, drag a key to retime it, wheel to zoom around the pointer,
/// shift+wheel to pan, right-click a key for its menu. With the strip focused: Space plays, K keys, Delete removes,
/// arrows step frames, +/- zoom, I inserts time, L loops, G toggles the curves, C cycles interpolation and T adds
/// tracks from the selection.
/// </remarks>
public partial class AnimationTimelinePanel : DockPanel
{
    [MenuItem("Window/Animation/Timeline", priority: 20)]
    static void Open() => EditorApplication.Instance?.OpenPanel<AnimationTimelinePanel>();

    public override string Title => "Animation";
    public override string Icon => EditorIcons.Film;

    private const float ToolbarHeight = 30f;
    private const float TrackColumn = 190f;
    private const float RowHeight = 22f;
    private const float RulerHeight = 26f;
    private const float CurveHeight = 148f;
    private const float KeyRadius = 4.5f;
    private const float MinZoom = 24f;
    private const float MaxZoom = 900f;

    private AnimationPlayer? _player;
    private AnimationTrack? _track;
    private float _playhead;
    private bool _playing;
    private bool _curveView;
    private float _zoom = 150f;
    private float _scroll;
    private float _stripWidth = 400f;

    private readonly Stopwatch _clock = new();
    private float _lastTick;

    private readonly List<KeyRef> _selection = [];
    private DragMode _drag = DragMode.None;
    private float _dragGrab;
    private float[] _dragStartTimes = [];
    private int _curveChannel = -1;
    private int _curveKey = -1;
    private float _curveMin = -1f;
    private float _curveMax = 1f;

    private readonly MenuTreeState _addTrackState = new();
    private bool _addTrackOpen;
    private ElementHandle _addTrackTrigger;

    private readonly List<AnimationPlayer> _players = [];

    /// <summary>A key the user picked: the track it lives on and the time it sits at.</summary>
    private readonly record struct KeyRef(AnimationTrack Track, float Time);

    private enum DragMode { None, Scrub, Keys, Curve }

    public override void OnGUI(Paper paper, float width, float height)
    {
        Scribe.FontFile? font = EditorTheme.DefaultFont;
        if (font == null) return;

        RefreshPlayers();
        ResolvePlayer();
        Tick();

        using (paper.Column("atl_root").Width(width).Height(height).Clip().Enter())
        {
            Toolbar(paper, font, width);

            if (_player.IsNotValid())
            {
                EmptyState(paper, font);
                return;
            }

            EditorGUI.Divider(paper, "atl_div");

            float bodyHeight = height - ToolbarHeight * 2f - 1f;
            Origami.ScrollView(paper, "atl_scroll", width, bodyHeight).Body(() => Body(paper, font, width));
        }
    }

    private void Tick()
    {
        if (!_playing || _player.IsNotValid()) return;

        float now = (float)_clock.Elapsed.TotalSeconds;
        float delta = now - _lastTick;
        _lastTick = now;
        if (delta <= 0f) return;

        _player!.Advance(delta);
        _playhead = _player.CurrentTime;
        if (!_player.IsPlaying) _playing = false;
    }

    private void RefreshPlayers()
    {
        _players.Clear();
        if (EditorSceneManager.EditWorldOrNull.IsNotValid()) return;

        foreach (GameObject go in EditorSceneManager.EditWorld.Objects)
        {
            AnimationPlayer? player = go.GetComponent<AnimationPlayer>();
            if (player.IsValid()) _players.Add(player);
        }
    }

    private void ResolvePlayer()
    {
        if (_player.IsValid()) return;

        foreach (GameObject go in Selection.GetSelected<GameObject>())
        {
            AnimationPlayer? player = go.GetComponent<AnimationPlayer>();
            if (player.IsNotValid()) continue;
            Bind(player);
            return;
        }

        if (_players.Count > 0) Bind(_players[0]);
    }

    private void Bind(AnimationPlayer player)
    {
        _player = player;
        _track = player.Take.Tracks.Count > 0 ? player.Take.Tracks[0] : null;
        _playhead = 0f;
        _playing = false;
        _selection.Clear();
    }

    private void EmptyState(Paper paper, Scribe.FontFile font)
    {
        using (paper.Column("atl_empty").Height(UnitValue.Auto).Padding(16, 16, 16, 0).Gap(8).Enter())
        {
            paper.Box("atl_empty_title").Width(UnitValue.Auto).Height(RowHeight)
                .Text("No animated object selected", EditorTheme.FontSemiBold ?? font)
                .TextColor(EditorTheme.Ink500).FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft)
                .IsNotInteractable();

            paper.Box("atl_empty_text").Width(UnitValue.Auto).Height(RowHeight)
                .Text("Pick an object with an Animation Player, or add one to the selection.", font)
                .TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft)
                .IsNotInteractable();

            Origami.Button(paper, "atl_addplayer", "Add Animation Player", AddPlayerToSelection)
                .Width(170).Small().Tooltip("Adds an Animation Player to the selected object.").Show();
        }
    }

    private void AddPlayerToSelection()
    {
        foreach (GameObject go in Selection.GetSelected<GameObject>())
        {
            AnimationPlayer? player = go.GetComponent<AnimationPlayer>();
            if (player.IsNotValid()) player = go.AddComponent<AnimationPlayer>();
            Bind(player);
            return;
        }
    }

    // ── Toolbar ─────────────────────────────────────────────────────────

    private void Toolbar(Paper paper, Scribe.FontFile font, float width)
    {
        AnimationTake? take = _player.IsValid() ? _player!.Take : null;

        using (paper.Row("atl_bar").Height(ToolbarHeight).Padding(8, 8, 0, 0).Gap(6).Enter())
        {
            Origami.IconButton(paper, "atl_play", _playing ? EditorIcons.Pause : EditorIcons.Play, TogglePlay)
                .Width(30).Small().IconOnly().Tooltip("Play or pause (Space)").Show();
            Origami.IconButton(paper, "atl_stop", EditorIcons.Stop, Stop)
                .Width(30).Small().IconOnly().Tooltip("Stop and rewind").Show();
            Origami.IconButton(paper, "atl_prev", EditorIcons.ChevronLeft, () => StepFrame(-1))
                .Width(26).Small().IconOnly().Tooltip("Previous frame (Left)").Show();
            Origami.IconButton(paper, "atl_next", EditorIcons.ChevronRight, () => StepFrame(1))
                .Width(26).Small().IconOnly().Tooltip("Next frame (Right)").Show();

            Clock(paper, font, take);

            if (take != null)
            {
                Origami.Button(paper, "atl_loop", take.Loop ? "Loop" : "Once", () => take.Loop = !take.Loop)
                    .Width(58).Small().Tooltip("Whether playback wraps at the end (L)").Show();
            }

            Origami.IconButton(paper, "atl_curves", EditorIcons.ChartLine, () => _curveView = !_curveView)
                .Width(30).Small().IconOnly().Tooltip("Show the curves of the selected track (G)").Show();

            paper.Box("atl_bar_gap").Width(UnitValue.Stretch()).Height(RowHeight).IsNotInteractable();

            ObjectPicker(paper, font);
        }

        using (paper.Row("atl_bar2").Height(ToolbarHeight).Padding(8, 8, 0, 0).Gap(6).Enter())
        {
            Origami.Button(paper, "atl_key", "Key", AddKeyAtPlayhead)
                .Width(46).Small().Tooltip("Key the selected track at the playhead (K)").Show();
            Origami.IconButton(paper, "atl_delkey", EditorIcons.Trash, DeleteSelectedKeys)
                .Width(30).Small().IconOnly().Tooltip("Delete the selected keys (Delete)").Show();
            AddTrackButton(paper, font);
            Origami.Button(paper, "atl_addtracks", "All Tracks", AddTracksFromSelection)
                .Width(86).Small().Tooltip("Add position, rotation and scale tracks for the selection (T)").Show();

            paper.Box("atl_bar2_sep").Width(1).Height(18).BackgroundColor(EditorTheme.BorderSoft).IsNotInteractable();

            Origami.Button(paper, "atl_insert", "+ Time", InsertTimeAtPlayhead)
                .Width(72).Small().Tooltip("Open half a second at the playhead, moving the keys after it (I)").Show();

            if (take != null)
            {
                Origami.IconButton(paper, "atl_shorter", EditorIcons.Minus, () => Resize(take, -0.5f))
                    .Width(26).Small().IconOnly().Tooltip("Shorter take").Show();
                Origami.IconButton(paper, "atl_longer", EditorIcons.Plus, () => Resize(take, 0.5f))
                    .Width(26).Small().IconOnly().Tooltip("Longer take").Show();
            }

            paper.Box("atl_bar2_gap").Width(UnitValue.Stretch()).Height(RowHeight).IsNotInteractable();

            Origami.IconButton(paper, "atl_zoom_out", EditorIcons.MagnifyingGlassMinus, () => ZoomBy(1f / 1.3f))
                .Width(28).Small().IconOnly().Tooltip("Zoom out (-)").Show();
            Origami.Button(paper, "atl_zoom_fit", "Fit", FrameAll)
                .Width(40).Small().Tooltip("Fit the take in the view (F)").Show();
            Origami.IconButton(paper, "atl_zoom_in", EditorIcons.MagnifyingGlassPlus, () => ZoomBy(1.3f))
                .Width(28).Small().IconOnly().Tooltip("Zoom in (+)").Show();
        }
    }

    private void Clock(Paper paper, Scribe.FontFile font, AnimationTake? take)
    {
        float duration = take?.Duration ?? 0f;
        int frame = take == null ? 0 : take.TimeToFrame(_playhead);
        int last = take == null ? 0 : take.TimeToFrame(duration);

        using (paper.Row("atl_clock").Width(178).Height(RowHeight).Gap(6).AlignItems(LayoutAlignment.Center).Enter())
        {
            paper.Box("atl_clock_time").Width(96).Height(RowHeight)
                .Text($"{_playhead:0.00} / {duration:0.00}s", EditorTheme.FontSemiBold ?? font)
                .TextColor(EditorTheme.Ink500).FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft)
                .IsNotInteractable();

            paper.Box("atl_clock_frame").Width(64).Height(18).Padding(6, 0, 0, 0).Rounded(4)
                .Text($"f{frame} / {last}", font)
                .TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft)
                .BackgroundColor(EditorTheme.Glass).IsNotInteractable();
        }
    }

    private void ObjectPicker(Paper paper, Scribe.FontFile font)
    {
        if (_players.Count == 0)
        {
            Origami.Button(paper, "atl_addplayer2", "Add Player", AddPlayerToSelection).Width(96).Small().Show();
            return;
        }

        var items = new List<AnimationPlayer>(_players);
        AnimationPlayer current = _player.IsValid() ? _player! : items[0];
        if (!items.Contains(current)) items.Insert(0, current);

        Origami.Dropdown(paper, "atl_objects", current, Bind, items)
            .Width(180)
            .Display(p => p.IsValid() ? $"{EditorIcons.Film}  {p.GameObject.Name}" : "None")
            .Secondary(p => p.IsValid() ? $"{p.Take.Tracks.Count} tracks  {p.Take.Duration:0.#}s" : "")
            .Show();
    }

    // ── Body ────────────────────────────────────────────────────────────

    private void Body(Paper paper, Scribe.FontFile font, float width)
    {
        AnimationTake take = _player!.Take;
        float stripWidth = MathF.Max(140f, width - TrackColumn - 22f);
        float gridHeight = RulerHeight + take.Tracks.Count * RowHeight;
        _stripWidth = stripWidth;

        using (paper.Column("atl_body").Height(UnitValue.Auto).Enter())
        {
            using (paper.Row("atl_grid").Height(gridHeight).Gap(0).Enter())
            {
                using (paper.Column("atl_names").Width(TrackColumn).Height(gridHeight).Gap(0).Enter())
                {
                    paper.Box("atl_names_head").Width(TrackColumn).Height(RulerHeight).Padding(10, 6, 0, 0)
                        .Text("TRACKS", font).FontSize(9).TextColor(EditorTheme.Ink300)
                        .Alignment(TextAlignment.MiddleLeft).IsNotInteractable();

                    foreach (AnimationTrack track in take.Tracks)
                        TrackRow(paper, font, take, track);
                }

                Strip(paper, font, stripWidth, gridHeight);
            }

            if (_curveView) CurveView(paper, font, stripWidth);

            EditorGUI.Divider(paper, "atl_body_div");

            paper.Box("atl_hint").Height(RowHeight).Padding(10, 8, 0, 0)
                .Text("Space play  ·  K key  ·  Del remove  ·  ←/→ frame  ·  wheel zoom  ·  shift+wheel pan  ·  right-click a key", font)
                .TextColor(EditorTheme.Ink300).FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft)
                .IsNotInteractable();
        }
    }

    private void TrackRow(Paper paper, Scribe.FontFile font, AnimationTake take, AnimationTrack track)
    {
        bool selected = ReferenceEquals(track, _track);
        int hash = track.GetHashCode();

        using (paper.Row($"atl_tr_{hash}").Height(RowHeight).Gap(4).AlignItems(LayoutAlignment.Center).Enter())
        {
            var name = paper.Box($"atl_tr_name_{hash}").Width(UnitValue.Stretch()).Height(RowHeight).Padding(10, 0, 0, 0)
                .Text(track.DisplayName, font)
                .FontSize(EditorTheme.FontSizeSmall)
                .TextColor(selected ? EditorTheme.Ink500 : EditorTheme.Ink400)
                .Alignment(TextAlignment.MiddleLeft)
                .BackgroundColor(selected ? EditorTheme.Glass : Color.Transparent)
                .Tooltip($"{track.KeyCount} keys");

            name.OnPress(_ => Select(track)).Cursor(PaperCursor.Pointer);

            Origami.IconButton(paper, $"atl_tr_eye_{hash}", track.Enabled ? EditorIcons.Eye : EditorIcons.EyeSlash,
                    () => track.Enabled = !track.Enabled)
                .Width(22).Small().IconOnly().Tooltip(track.Enabled ? "Mute this track" : "Unmute this track").Show();

            Origami.IconButton(paper, $"atl_tr_del_{hash}", EditorIcons.Trash, () => RemoveTrack(take, track))
                .Width(22).Small().IconOnly().Tooltip("Delete this track").Show();
        }
    }

    private void Strip(Paper paper, Scribe.FontFile font, float stripWidth, float height)
    {
        ElementBuilder strip = paper.Box("atl_strip").Width(stripWidth).Height(height).Clip()
            .BackgroundColor(EditorTheme.Neutral300)
            .BorderColor(EditorTheme.BorderSoft).BorderWidth(1)
            .Rounded(Origami.Current.Metrics.SmallRounding)
            .Cursor(PaperCursor.Pointer)
            .OnPostLayout((handle, _) => _stripHandle = handle)
            .OnPress(e => StripPress(e, paper))
            .OnDragging(e => StripDrag(e))
            .OnDragEnd(_ => _drag = DragMode.None)
            .OnScroll(e => StripScroll(e, paper))
            .OnKeyPressed(e => Shortcut(e.Key))
            .Tooltip("Drag to scrub, drag a key to retime it. Wheel zooms, shift+wheel pans.");

        using (strip.Enter())
            paper.Draw((canvas, rect) => PaintStrip(canvas, rect, font));
    }

    private void CurveView(Paper paper, Scribe.FontFile font, float stripWidth)
    {
        using (paper.Row("atl_curve_row").Height(CurveHeight + 6).Gap(0).Enter())
        {
            paper.Box("atl_curve_head").Width(TrackColumn).Height(CurveHeight).Padding(10, 6, 8, 0)
                .Text(_track == null ? "CURVES" : $"CURVES  ·  {_track.KindName}", font)
                .FontSize(EditorTheme.FontSizeSmall).TextColor(EditorTheme.Ink300)
                .Alignment(TextAlignment.Left).IsNotInteractable();

            ElementBuilder box = paper.Box("atl_curve").Width(stripWidth).Height(CurveHeight).Clip()
                .BackgroundColor(EditorTheme.Neutral300)
                .BorderColor(EditorTheme.BorderSoft).BorderWidth(1)
                .Rounded(Origami.Current.Metrics.SmallRounding)
                .Cursor(PaperCursor.Pointer)
                .OnPostLayout((handle, _) => _curveHandle = handle)
                .OnPress(e => CurvePress(e, paper))
                .OnDragging(e => CurveDrag(e))
                .OnDragEnd(_ => CurveDragEnd())
                .OnScroll(e => StripScroll(e, paper))
                .OnKeyPressed(e => Shortcut(e.Key))
                .Tooltip("Drag a key to change its time and value.");

            using (box.Enter())
                paper.Draw((canvas, rect) => PaintCurves(canvas, rect, font));
        }
    }

    // ── Actions ─────────────────────────────────────────────────────────

    private void Select(AnimationTrack track)
    {
        if (!ReferenceEquals(_track, track)) _curveHidden = 0;
        _track = track;
        _curveChannel = -1;
        _curveKey = -1;
        if (_player.IsValid()) _playhead = Math.Clamp(_playhead, 0f, _player!.Take.Duration);
    }

    private void Seek(float time)
    {
        if (_player.IsNotValid()) return;
        _playhead = Math.Clamp(time, 0f, _player!.Take.Duration);
        _player.Sample(_playhead);
    }

    private void StepFrame(int frames)
    {
        if (_player.IsNotValid()) return;
        AnimationTake take = _player!.Take;
        int frame = take.TimeToFrame(_playhead) + frames;
        Seek(Math.Clamp(take.FrameToTime(frame), 0f, take.Duration));
    }

    private void TogglePlay()
    {
        if (_player.IsNotValid()) return;

        if (_playing)
        {
            _playing = false;
            _player!.Pause();
            return;
        }

        if (_playhead >= _player!.Take.Duration) _playhead = 0f;
        _player.Play(_playhead);
        _playing = true;
        _clock.Restart();
        _lastTick = 0f;
    }

    private void Stop()
    {
        _playing = false;
        _playhead = 0f;
        if (_player.IsValid()) _player!.Stop();
    }

    private void Resize(AnimationTake take, float delta)
    {
        take.Duration = MathF.Max(0.5f, take.Duration + delta);
        _playhead = Math.Clamp(_playhead, 0f, take.Duration);
    }

    private void InsertTimeAtPlayhead()
    {
        if (_player.IsNotValid()) return;
        _player!.Take.InsertTime(_player.Take.Snap(_playhead), 0.5f);
    }

    private void ZoomBy(float factor)
    {
        float center = _scroll + VisibleSeconds * 0.5f;
        _zoom = Math.Clamp(_zoom * factor, MinZoom, MaxZoom);
        _scroll = center - VisibleSeconds * 0.5f;
        ClampScroll();
    }

    private void FrameAll()
    {
        if (_player.IsNotValid()) return;
        float duration = MathF.Max(0.1f, _player!.Take.Duration);
        _zoom = Math.Clamp(_stripWidth / (duration * 1.08f), MinZoom, MaxZoom);
        _scroll = -duration * 0.02f;
        ClampScroll();
    }

    private void AddTracksFromSelection()
    {
        if (_player.IsNotValid()) return;

        AnimationTake take = _player!.Take;
        Transform root = _player.Root;
        int added = 0;

        foreach (GameObject go in Selection.GetSelected<GameObject>())
        {
            string path = Transform.GetRelativePath(go.Transform, root);
            take.GetOrAddTrack(path, AnimationTrackKind.TransformPosition);
            take.GetOrAddTrack(path, AnimationTrackKind.TransformRotation);
            take.GetOrAddTrack(path, AnimationTrackKind.TransformScale);
            added += 3;

            if (go.GetComponent<SpriteRenderer>().IsValid())
            {
                take.GetOrAddTrack(path, AnimationTrackKind.SpriteFrame);
                added++;
            }
        }

        if (_track == null && take.Tracks.Count > 0) _track = take.Tracks[0];
        if (added == 0) Runtime.Debug.LogWarning("[Animation] Select a bone or object in the hierarchy to add tracks for it.");
    }

    private void RemoveTrack(AnimationTake take, AnimationTrack track)
    {
        take.RemoveTrack(track);
        _selection.RemoveAll(k => ReferenceEquals(k.Track, track));
        if (ReferenceEquals(track, _track)) _track = take.Tracks.Count > 0 ? take.Tracks[0] : null;
    }

    private void AddKeyAtPlayhead()
    {
        if (_player.IsNotValid() || _track == null) return;

        AnimationTake take = _player!.Take;
        Transform? target = AnimationTake.Resolve(_player.Root, _track.Target);
        if (target == null) return;

        float time = take.Snap(_playhead);
        AnimationTrack track = _track;

        switch (track.Kind)
        {
            case AnimationTrackKind.TransformPosition:
            {
                Float3 value = target.LocalPosition;
                track.SetKey(0, time, value.X);
                track.SetKey(1, time, value.Y);
                track.SetKey(2, time, value.Z);
                break;
            }
            case AnimationTrackKind.TransformRotation:
            {
                Float3 value = target.LocalEulerAngles;
                track.SetKey(0, time, value.X);
                track.SetKey(1, time, value.Y);
                track.SetKey(2, time, value.Z);
                break;
            }
            case AnimationTrackKind.TransformScale:
            {
                Float3 value = target.LocalScale;
                track.SetKey(0, time, value.X);
                track.SetKey(1, time, value.Y);
                track.SetKey(2, time, value.Z);
                break;
            }
            case AnimationTrackKind.SpriteFrame:
            {
                SpriteRenderer? renderer = target.GameObject.GetComponent<SpriteRenderer>();
                if (renderer.IsNotValid()) return;
                track.AddSpriteKey(time, renderer.Sprite);
                break;
            }
            case AnimationTrackKind.TintColor:
            {
                if (!AnimationTrack.TryGetTint(target.GameObject, out Infinity.Vector.Color tint)) return;
                track.SetVectorKey(time, new Float4(tint.R, tint.G, tint.B, tint.A));
                break;
            }
            case AnimationTrackKind.MaterialProperty:
            {
                Material? material = AnimationTrack.GetMaterial(target.GameObject);
                if (material.IsNotValid()) return;

                if (track.Channels >= 4)
                {
                    Color c = material.GetColor(track.Property);
                    track.SetVectorKey(time, new Float4(c.R, c.G, c.B, c.A));
                }
                else
                {
                    Float4 current = material.GetVector(track.Property);
                    track.SetKey(0, time, current.X);
                    if (track.Channels > 1) track.SetKey(1, time, current.Y);
                    if (track.Channels > 2) track.SetKey(2, time, current.Z);
                }
                break;
            }
            case AnimationTrackKind.MaterialTexture:
            {
                Material? material = AnimationTrack.GetMaterial(target.GameObject);
                if (material.IsNotValid()) return;
                track.AddTextureKey(time, material.GetTexture(track.Property));
                break;
            }
            case AnimationTrackKind.ComponentProperty:
            {
                Component? component = AnimationTrack.FindComponent(target.GameObject, track.Component);
                if (component.IsNotValid()) return;
                if (!AnimationTrack.TryGetMemberValue(component, track.Property, out Float4 value)) return;
                track.SetVectorKey(time, value);
                break;
            }
        }

        _selection.Clear();
        _selection.Add(new KeyRef(track, time));
    }

    private void DeleteSelectedKeys()
    {
        if (_player.IsNotValid()) return;

        if (_selection.Count == 0)
        {
            if (_track != null) _track.RemoveKeysAt(_player!.Take.Snap(_playhead));
            return;
        }

        foreach (KeyRef key in _selection) key.Track.RemoveKeysAt(key.Time);
        _selection.Clear();
    }

    private void Shortcut(PaperKey key)
    {
        switch (key)
        {
            case PaperKey.Space:
                TogglePlay();
                break;
            case PaperKey.K:
                AddKeyAtPlayhead();
                break;
            case PaperKey.Delete:
            case PaperKey.Backspace:
                if (_curveView && _curveChannel >= 0 && _curveKey >= 0) DeleteCurveKey();
                else DeleteSelectedKeys();
                break;
            case PaperKey.Left:
                StepFrame(-1);
                break;
            case PaperKey.Right:
                StepFrame(1);
                break;
            case PaperKey.Home:
                Seek(0f);
                break;
            case PaperKey.End:
                if (_player.IsValid()) Seek(_player!.Take.Duration);
                break;
            case PaperKey.Minus:
                ZoomBy(1f / 1.3f);
                break;
            case PaperKey.Equals:
                ZoomBy(1.3f);
                break;
            case PaperKey.I:
                InsertTimeAtPlayhead();
                break;
            case PaperKey.L:
                if (_player.IsValid()) _player!.Take.Loop = !_player.Take.Loop;
                break;
            case PaperKey.G:
                _curveView = !_curveView;
                break;
            case PaperKey.T:
                AddTracksFromSelection();
                break;
            case PaperKey.F:
                FrameAll();
                break;
            case PaperKey.C:
                CycleInterpolation();
                break;
        }
    }

    private void CycleInterpolation()
    {
        foreach (KeyRef key in _selection)
        {
            CurveInterpolation next = NextInterpolation(key.Track, key.Time);
            key.Track.SetInterpolationAt(key.Time, next);
        }
    }

    private static CurveInterpolation NextInterpolation(AnimationTrack track, float time)
    {
        AnimationCurve curve = track.CurveFor(0);
        for (int i = 0; i < curve.Count; i++)
        {
            if (MathF.Abs(curve[i].Time - time) > AnimationTrack.TimeEpsilon) continue;
            return curve[i].Interpolation switch
            {
                CurveInterpolation.Linear => CurveInterpolation.Step,
                CurveInterpolation.Step => CurveInterpolation.CubicSpline,
                _ => CurveInterpolation.Linear,
            };
        }

        return CurveInterpolation.Linear;
    }

    // ── Add track menu ──────────────────────────────────────────────────

    private sealed record TrackRequest(AnimationTrackKind Kind, string Component, string Property, int Channels);

    private void AddTrackButton(Paper paper, Scribe.FontFile font)
    {
        ElementBuilder trigger = paper.Box("atl_addtrack_btn")
            .Width(96).Height(26)
            .Rounded(EditorTheme.Roundness)
            .BackgroundColor(EditorTheme.Ink100)
            .BorderColor(EditorTheme.BorderSoft).BorderWidth(1)
            .PaddingLeft(10).PaddingRight(10)
            .OnClick(0, (_, _) => _addTrackOpen = !_addTrackOpen)
            .Cursor(PaperCursor.Pointer)
            .Tooltip("Animate a transform, tint, material, map or UI property");

        using (trigger.Enter())
        {
            _addTrackTrigger = paper.CurrentParent;

            paper.Box("atl_addtrack_lbl").Width(UnitValue.Stretch()).Height(26)
                .Text("Add Track", font).TextColor(EditorTheme.Ink500)
                .FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleCenter)
                .IsNotInteractable();

            if (!_addTrackOpen) return;

            if (paper.IsKeyPressed(PaperKey.Escape))
            {
                _addTrackOpen = false;
                return;
            }

            MenuTreePopup.Backdrop(paper, "atl_addtrack", () => _addTrackOpen = false);
            MenuTreePopup.Popover(paper, "atl_addtrack", _addTrackTrigger, BuildTrackEntries(), _addTrackState,
                entry =>
                {
                    _addTrackOpen = false;
                    if (entry.Tag is TrackRequest request) AddTrack(request);
                },
                "Search a property...", "Nothing to animate on this selection.");
        }
    }

    private List<MenuTreeEntry> BuildTrackEntries()
    {
        var entries = new List<MenuTreeEntry>();

        foreach (GameObject go in Selection.GetSelected<GameObject>())
        {
            string path = Transform.GetRelativePath(go.Transform, _player.IsValid() ? _player!.Root : go.Transform);
            string prefix = string.IsNullOrEmpty(path) ? go.Name : path;

            void Add(string group, string label, TrackRequest request) =>
                entries.Add(MenuTreeEntry.FromPath($"{prefix}/{group}/{label}", "", request));

            Add("Transform", "Position", new TrackRequest(AnimationTrackKind.TransformPosition, "", "", 3));
            Add("Transform", "Rotation", new TrackRequest(AnimationTrackKind.TransformRotation, "", "", 3));
            Add("Transform", "Scale", new TrackRequest(AnimationTrackKind.TransformScale, "", "", 3));

            if (go.GetComponent<SpriteRenderer>().IsValid())
                Add("Look", "Sprite Frame", new TrackRequest(AnimationTrackKind.SpriteFrame, "", "", 1));

            if (AnimationTrack.TryGetTint(go, out _))
                Add("Look", "Tint / Opacity", new TrackRequest(AnimationTrackKind.TintColor, "", "", 4));

            Material? material = AnimationTrack.GetMaterial(go);
            if (material.IsValid())
            {
                Shader? shader = material.Shader;
                if (shader.IsValid())
                {
                    foreach (Infinity.Graphite.Rendering.MaterialProperty property in shader.Properties)
                    {
                        string? name = Infinity.Graphite.PropertyID.ToString(property.Name);
                        if (string.IsNullOrEmpty(name)) name = property.DisplayName;
                        if (string.IsNullOrEmpty(name)) continue;

                        switch (property.Type)
                        {
                            case Infinity.Graphite.Rendering.MaterialPropertyType.Float:
                                Add("Material", name, new TrackRequest(AnimationTrackKind.MaterialProperty, "", name, 1));
                                break;
                            case Infinity.Graphite.Rendering.MaterialPropertyType.Float2:
                                Add("Material", name, new TrackRequest(AnimationTrackKind.MaterialProperty, "", name, 2));
                                break;
                            case Infinity.Graphite.Rendering.MaterialPropertyType.Float3:
                                Add("Material", name, new TrackRequest(AnimationTrackKind.MaterialProperty, "", name, 3));
                                break;
                            case Infinity.Graphite.Rendering.MaterialPropertyType.Color:
                            case Infinity.Graphite.Rendering.MaterialPropertyType.Float4:
                                Add("Material", name, new TrackRequest(AnimationTrackKind.MaterialProperty, "", name, 4));
                                break;
                            case Infinity.Graphite.Rendering.MaterialPropertyType.Texture2D:
                                Add("Maps", name, new TrackRequest(AnimationTrackKind.MaterialTexture, "", name, 1));
                                break;
                        }
                    }
                }
            }

            foreach (Component component in go.GetComponents())
            {
                Type type = component.GetType();
                if (type == typeof(Transform) || type == typeof(RectTransform)) continue;

                foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!property.CanWrite || property.GetIndexParameters().Length > 0) continue;
                    int channels = AnimationTrack.ChannelsOfMember(type, property.Name);
                    if (channels == 0) continue;
                    Add(type.Name, property.Name, new TrackRequest(AnimationTrackKind.ComponentProperty, type.Name, property.Name, channels));
                }

                foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    int channels = AnimationTrack.ChannelsOfMember(type, field.Name);
                    if (channels == 0) continue;
                    Add(type.Name, field.Name, new TrackRequest(AnimationTrackKind.ComponentProperty, type.Name, field.Name, channels));
                }
            }
        }

        entries.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));
        return entries;
    }

    private void AddTrack(TrackRequest request)
    {
        if (_player.IsNotValid()) return;

        AnimationTake take = _player!.Take;
        foreach (GameObject go in Selection.GetSelected<GameObject>())
        {
            string path = Transform.GetRelativePath(go.Transform, _player.Root);
            AnimationTrack track = take.GetOrAddTrack(path, request.Kind, request.Component, request.Property, request.Channels);
            _track = track;
            if (track.KeyCount == 0) AddKeyAtPlayhead();
            return;
        }
    }
}
