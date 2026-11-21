// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics;

using Infinity.Editor.Core;
using Infinity.Editor.Theming;
using Infinity.OrigamiUI;
using Infinity.PaperUI;
using Infinity.PaperUI.LayoutEngine;
using Infinity.Rosetta;
using Infinity.Runtime;
using Infinity.Runtime.Animation;
using Infinity.Vector;

namespace Infinity.Editor.GUI.Panels;

/// <summary>
/// The animation timeline: plays, scrubs and keys an <see cref="AnimationTake"/> on the selected
/// <see cref="AnimationPlayer"/>. Tracks are rows, keys are marks on the row at their time, and the playhead is
/// shared by every row. Bones and plain transforms are the same thing here, because a track names a path under the
/// player's root, so a rigged character and a moving crate are authored with one tool.
/// </summary>
public class AnimationTimelinePanel : DockPanel
{
    [MenuItem("Window/Animation/Timeline", priority: 20)]
    static void Open() => EditorApplication.Instance?.OpenPanel<AnimationTimelinePanel>();

    public override string Title => "Animation";
    public override string Icon => EditorIcons.Film;

    private const float ToolbarHeight = 30f;
    private const float RowHeight = 22f;
    private const float TrackColumn = 200f;
    private const float KeySize = 9f;

    private AnimationPlayer? _player;
    private AnimationTrack? _track;
    private float _playhead;
    private bool _playing;

    private readonly Stopwatch _clock = new();
    private float _lastTick;

    public override void OnGUI(Paper paper, float width, float height)
    {
        Scribe.FontFile? font = EditorTheme.DefaultFont;
        if (font == null) return;

        ResolvePlayer();

        using (paper.Column("atl_root").Width(width).Height(height).Clip().Enter())
        {
            Toolbar(paper, font, width);

            if (_player.IsNotValid())
            {
                using (paper.Row("atl_empty").Height(RowHeight).Padding(8, 8, 0, 0).Gap(8).Enter())
                {
                    paper.Box("atl_empty_text").Width(UnitValue.Auto).Height(RowHeight)
                        .Text("Select an object to edit its take.", font)
                        .TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft)
                        .IsNotInteractable();
                    Origami.Button(paper, "atl_addplayer", "Add Animation Player", AddPlayerToSelection).Width(150).Small().Show();
                }
                return;
            }

            EditorGUI.Divider(paper, "atl_div");

            float bodyHeight = height - ToolbarHeight - 1;
            Origami.ScrollView(paper, "atl_scroll", width, bodyHeight).Body(() => Body(paper, font, width));
        }

        Tick();
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

    private void ResolvePlayer()
    {
        foreach (GameObject go in Selection.GetSelected<GameObject>())
        {
            AnimationPlayer? player = go.GetComponent<AnimationPlayer>();
            if (player.IsNotValid() || ReferenceEquals(player, _player)) return;
            Bind(player);
            return;
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

    private void Bind(AnimationPlayer player)
    {
        _player = player;
        _track = player.Take.Tracks.Count > 0 ? player.Take.Tracks[0] : null;
        _playhead = 0f;
        _playing = false;
    }

    private void Toolbar(Paper paper, Scribe.FontFile font, float width)
    {
        AnimationTake? take = _player.IsValid() ? _player!.Take : null;
        using (paper.Row("atl_bar").Height(ToolbarHeight).Padding(8, 8, 0, 0).Gap(6).Enter())
        {
            Origami.Button(paper, "atl_play", _playing ? EditorIcons.Pause : EditorIcons.Play, TogglePlay).Width(30).Small().Show();
            Origami.Button(paper, "atl_stop", EditorIcons.Stop, Stop).Width(30).Small().Show();
            Origami.Button(paper, "atl_prev", EditorIcons.ChevronLeft, () => StepFrame(-1)).Width(26).Small().Show();
            Origami.Button(paper, "atl_next", EditorIcons.ChevronRight, () => StepFrame(1)).Width(26).Small().Show();

            paper.Box("atl_clock").Width(110).Height(RowHeight).Padding(4, 4, 0, 0)
                .Text($"{_playhead:0.00} / {take?.Duration ?? 0f:0.00}s", EditorTheme.FontSemiBold ?? font)
                .TextColor(EditorTheme.Ink500).FontSize(EditorTheme.FontSize).Alignment(TextAlignment.MiddleLeft)
                .IsNotInteractable();

            Origami.Button(paper, "atl_key", "Key", AddKeyAtPlayhead).Width(44).Small().Show();
            Origami.Button(paper, "atl_delkey", EditorIcons.Trash, DeleteKeyAtPlayhead).Width(30).Small().Show();

            Origami.Button(paper, "atl_addtracks", "Tracks From Selection", AddTracksFromSelection).Width(150).Small().Show();

            if (take != null)
            {
                Origami.Button(paper, "atl_shorter", "-0.5s", () => Resize(take, -0.5f)).Width(46).Small().Show();
                Origami.Button(paper, "atl_longer", "+0.5s", () => Resize(take, 0.5f)).Width(46).Small().Show();

                Origami.Button(paper, "atl_loop", take.Loop ? "Loop: On" : "Loop: Off", () => take.Loop = !take.Loop).Width(80).Small().Show();
            }
        }
    }

    private void Body(Paper paper, Scribe.FontFile font, float width)
    {
        AnimationTake take = _player!.Take;
        float stripWidth = MathF.Max(80f, width - TrackColumn - 24f);

        using (paper.Column("atl_body").Height(UnitValue.Auto).Enter())
        {
            Ruler(paper, font, take, stripWidth);

            foreach (AnimationTrack track in take.Tracks)
                TrackRow(paper, font, take, track, stripWidth);

            if (take.Tracks.Count == 0)
            {
                paper.Box("atl_no_tracks").Height(RowHeight).Padding(8, 8, 0, 0)
                    .Text("No tracks. Select a bone or object and press Tracks From Selection.", font)
                    .TextColor(EditorTheme.Ink300).FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft)
                    .IsNotInteractable();
            }

            EditorGUI.Divider(paper, "atl_body_div");

            paper.Box("atl_hint").Height(RowHeight).Padding(8, 8, 0, 0)
                .Text("API: player.Play(), Pause(), Stop(), Seek(t), Sample(t), Advance(dt), CurrentTime, IsPlaying", font)
                .TextColor(EditorTheme.Ink300).FontSize(EditorTheme.FontSizeSmall).Alignment(TextAlignment.MiddleLeft)
                .IsNotInteractable();
        }
    }

    private void Ruler(Paper paper, Scribe.FontFile font, AnimationTake take, float stripWidth)
    {
        float step = MathF.Max(1f, MathF.Round(take.Duration / 10f));

        using (paper.Row("atl_ruler").Height(RowHeight).Gap(0).Enter())
        {
            paper.Box("atl_ruler_label").Width(TrackColumn).Height(RowHeight).Padding(8, 8, 0, 0)
                .Text("Time", font).TextColor(EditorTheme.Ink400).FontSize(EditorTheme.FontSizeSmall)
                .Alignment(TextAlignment.MiddleLeft).IsNotInteractable();

            using (paper.Row("atl_ruler_cells").Width(stripWidth).Height(RowHeight).Gap(0).Enter())
            {
                for (float t = 0f; t < take.Duration; t += step)
                {
                    float cellTime = t;
                    float cellWidth = stripWidth * (step / take.Duration);
                    bool here = _playhead >= cellTime && _playhead < cellTime + step;

                    var cell = paper.Box($"atl_ruler_{cellTime:0.0}").Width(cellWidth).Height(RowHeight).Padding(4, 0, 0, 0)
                        .Text($"{cellTime:0.#}s", font).FontSize(EditorTheme.FontSizeSmall)
                        .TextColor(here ? EditorTheme.Ink500 : EditorTheme.Ink300)
                        .Alignment(TextAlignment.MiddleLeft)
                        .BackgroundColor(here ? EditorTheme.Glass : System.Drawing.Color.Transparent);

                    cell.OnClick(0, (_, _) => Seek(cellTime)).Cursor(PaperCursor.Pointer);
                }
            }
        }
    }

    private void TrackRow(Paper paper, Scribe.FontFile font, AnimationTake take, AnimationTrack track, float stripWidth)
    {
        bool selected = ReferenceEquals(track, _track);

        using (paper.Row($"atl_tr_{track.GetHashCode()}").Height(RowHeight).Gap(0).Enter())
        {
            var name = paper.Box($"atl_tr_name_{track.GetHashCode()}").Width(TrackColumn).Height(RowHeight).Padding(8, 8, 0, 0)
                .Text(track.DisplayName, font)
                .FontSize(EditorTheme.FontSizeSmall)
                .TextColor(selected ? EditorTheme.Ink500 : EditorTheme.Ink400)
                .Alignment(TextAlignment.MiddleLeft)
                .BackgroundColor(selected ? EditorTheme.Glass : System.Drawing.Color.Transparent);

            name.OnClick(0, (_, _) => Select(track)).Cursor(PaperCursor.Pointer);

            using (paper.Row($"atl_tr_strip_{track.GetHashCode()}").Width(stripWidth).Height(RowHeight).Gap(0).Enter())
                Keys(paper, track, stripWidth);
        }
    }

    /// <summary>
    /// Lays a track's keys out with a spacer before each one, so a key sits at its time without absolute
    /// positioning. Keys are clickable to seek and right-clickable to delete.
    /// </summary>
    private void Keys(Paper paper, AnimationTrack track, float stripWidth)
    {
        AnimationTake take = _player!.Take;
        List<float> times = track.KeyTimes();
        float previous = 0f;

        foreach (float time in times)
        {
            float gap = MathF.Max(0f, (time - previous) / take.Duration * stripWidth - KeySize);
            previous = time;

            paper.Box($"atl_gap_{track.GetHashCode()}_{time:0.000}").Width(gap).Height(RowHeight)
                .BackgroundColor(System.Drawing.Color.Transparent).IsNotInteractable();

            float keyTime = time;
            var key = paper.Box($"atl_key_{track.GetHashCode()}_{time:0.000}").Width(KeySize).Height(KeySize)
                .Rounded(2)
                .BackgroundColor(track.Enabled ? EditorTheme.Accent : EditorTheme.Ink300)
                .Margin(0, 0, UnitValue.StretchOne, UnitValue.StretchOne);

            key.OnClick(0, (_, _) => Seek(keyTime)).Cursor(PaperCursor.Pointer);
            key.OnClick(1, (_, _) => DeleteKey(track, keyTime)).Cursor(PaperCursor.Pointer);
        }
    }

    private void Select(AnimationTrack track)
    {
        _track = track;
        _playhead = Math.Clamp(_playhead, 0f, _player!.Take.Duration);
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

    private void AddKeyAtPlayhead()
    {
        if (_player.IsNotValid() || _track == null) return;

        AnimationTake take = _player!.Take;
        Transform? target = AnimationTake.Resolve(_player.Root, _track.Target);
        if (target == null) return;

        float time = take.Snap(_playhead);

        switch (_track.Kind)
        {
            case AnimationTrackKind.TransformPosition:
            {
                Float3 value = target.LocalPosition;
                _track.SetKey(0, time, value.X);
                _track.SetKey(1, time, value.Y);
                _track.SetKey(2, time, value.Z);
                break;
            }
            case AnimationTrackKind.TransformRotation:
            {
                Float3 value = target.LocalEulerAngles;
                _track.SetKey(0, time, value.X);
                _track.SetKey(1, time, value.Y);
                _track.SetKey(2, time, value.Z);
                break;
            }
            case AnimationTrackKind.TransformScale:
            {
                Float3 value = target.LocalScale;
                _track.SetKey(0, time, value.X);
                _track.SetKey(1, time, value.Y);
                _track.SetKey(2, time, value.Z);
                break;
            }
            case AnimationTrackKind.SpriteFrame:
            {
                SpriteRenderer? renderer = target.GameObject.GetComponent<SpriteRenderer>();
                if (renderer.IsNotValid()) return;
                _track.AddSpriteKey(time, renderer.Sprite);
                break;
            }
        }
    }

    private void DeleteKeyAtPlayhead()
    {
        if (_player.IsNotValid() || _track == null) return;
        DeleteKey(_track, _player!.Take.Snap(_playhead));
    }

    private static void DeleteKey(AnimationTrack track, float time) => track.RemoveKeysAt(time);
}
