// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Infinity.Echo;
using Infinity.Runtime.Resources;
using Infinity.Vector;

namespace Infinity.Runtime.Animation;

/// <summary>
/// An authored animation: tracks of keyframes over a length, played by <see cref="AnimationPlayer"/>. A take is
/// plain data, so the timeline editor edits it in place and the player only samples it.
/// </summary>
[Serializable]
public sealed class AnimationTake
{
    /// <summary>Below this a take cannot be sampled, so a new one starts with a second.</summary>
    public const float MinimumDuration = 1f / 1000f;

    [SerializeField] private float _duration = 1f;
    [SerializeField] private float _frameRate = 30f;
    [SerializeField] private bool _loop = true;
    [SerializeField] private List<AnimationTrack> _tracks = [];

    /// <summary>Length in seconds.</summary>
    public float Duration
    {
        get => _duration;
        set => _duration = MathF.Max(MinimumDuration, value);
    }

    /// <summary>The rate the timeline snaps keys to, in frames per second.</summary>
    public float FrameRate
    {
        get => _frameRate;
        set => _frameRate = Math.Clamp(value, 1f, 240f);
    }

    /// <summary>Whether playback wraps at the end instead of holding the last frame.</summary>
    public bool Loop { get => _loop; set => _loop = value; }

    public IReadOnlyList<AnimationTrack> Tracks => _tracks;

    /// <summary>The frame count the ruler draws.</summary>
    public int FrameCount => Math.Max(1, (int)MathF.Round(_duration * _frameRate) + 1);

    public float FrameToTime(int frame) => frame / _frameRate;

    public int TimeToFrame(float time) => (int)MathF.Round(time * _frameRate);

    /// <summary>Snaps a time to the frame grid, which is what the timeline's key placement uses.</summary>
    public float Snap(float time) => MathF.Round(time * _frameRate) / _frameRate;

    public AnimationTrack GetOrAddTrack(string target, AnimationTrackKind kind)
    {
        AnimationTrack? existing = FindTrack(target, kind);
        if (existing != null) return existing;

        var track = new AnimationTrack(target, kind);
        _tracks.Add(track);
        return track;
    }

    /// <summary>
    /// Finds or adds a track for a named member: the uniform of a material track or the property of a component
    /// track. Two tracks of the same kind on the same target only match when they drive the same member.
    /// </summary>
    public AnimationTrack GetOrAddTrack(string target, AnimationTrackKind kind, string component, string property, int channels)
    {
        foreach (AnimationTrack track in _tracks)
        {
            if (track.Kind != kind) continue;
            if (!string.Equals(track.Target, target ?? string.Empty, StringComparison.Ordinal)) continue;
            if (!string.Equals(track.Component, component ?? string.Empty, StringComparison.Ordinal)) continue;
            if (!string.Equals(track.Property, property ?? string.Empty, StringComparison.Ordinal)) continue;
            return track;
        }

        var added = new AnimationTrack(target, kind)
        {
            Component = component,
            Property = property,
            Channels = channels,
        };
        _tracks.Add(added);
        return added;
    }

    /// <summary>
    /// Shifts every key at or after <paramref name="at"/> by <paramref name="amount"/> and grows the take to hold
    /// them, which is how the timeline opens a gap without retyping every key after it.
    /// </summary>
    public void InsertTime(float at, float amount)
    {
        if (amount <= 0f) return;
        foreach (AnimationTrack track in _tracks) track.ShiftKeys(at, amount);

        float last = 0f;
        foreach (AnimationTrack track in _tracks)
        {
            List<float> times = track.KeyTimes();
            if (times.Count > 0) last = MathF.Max(last, times[^1]);
        }

        // Opening a gap past the end moves nothing, so the take keeps its length instead of stretching.
        Duration = MathF.Max(at <= _duration ? _duration + amount : _duration, last);
    }

    /// <summary>Every key time across every track, sorted, which is what the timeline's summary band draws.</summary>
    public List<float> AllKeyTimes()
    {
        var times = new List<float>();
        foreach (AnimationTrack track in _tracks) times.AddRange(track.KeyTimes());
        times.Sort();

        for (int i = times.Count - 1; i > 0; i--)
            if (times[i] - times[i - 1] <= AnimationTrack.TimeEpsilon) times.RemoveAt(i);
        return times;
    }

    public AnimationTrack? FindTrack(string target, AnimationTrackKind kind)
    {
        foreach (AnimationTrack track in _tracks)
            if (track.Kind == kind && string.Equals(track.Target, target ?? string.Empty, StringComparison.Ordinal))
                return track;
        return null;
    }

    public bool RemoveTrack(AnimationTrack track) => _tracks.Remove(track);

    /// <summary>Where a time lands once playback reaches the end, honouring <see cref="Loop"/>.</summary>
    public float Wrap(float time)
    {
        if (_duration <= MinimumDuration) return 0f;
        if (!_loop) return Math.Clamp(time, 0f, _duration);

        float wrapped = time % _duration;
        return wrapped < 0f ? wrapped + _duration : wrapped;
    }

    /// <summary>
    /// Samples every enabled track at <paramref name="time"/> and writes it onto the hierarchy under
    /// <paramref name="root"/>. A target path that does not resolve is skipped, so a take outlives a renamed bone
    /// without throwing.
    /// </summary>
    public void Apply(Transform root, float time)
    {
        if (root == null) return;
        foreach (AnimationTrack track in _tracks)
        {
            if (!track.Enabled) continue;
            ApplyTrack(track, root, time);
        }
    }

    public void ApplyTrack(AnimationTrack track, Transform root, float time)
    {
        Transform? target = Resolve(root, track.Target);
        if (target == null) return;

        switch (track.Kind)
        {
            case AnimationTrackKind.TransformPosition:
                target.LocalPosition = track.EvaluateVector(time, target.LocalPosition);
                break;
            case AnimationTrackKind.TransformRotation:
                target.LocalEulerAngles = track.EvaluateVector(time, target.LocalEulerAngles);
                break;
            case AnimationTrackKind.TransformScale:
                target.LocalScale = track.EvaluateVector(time, target.LocalScale);
                break;
            case AnimationTrackKind.SpriteFrame:
            {
                Sprite? sprite = track.EvaluateSprite(time);
                if (sprite.IsNotValid()) break;
                SpriteRenderer? renderer = target.GameObject.GetComponent<SpriteRenderer>();
                if (renderer.IsValid()) renderer.Sprite = sprite;
                break;
            }
            case AnimationTrackKind.TintColor:
                track.ApplyTint(target, time);
                break;
            case AnimationTrackKind.MaterialProperty:
                track.ApplyMaterialProperty(target, time);
                break;
            case AnimationTrackKind.MaterialTexture:
                track.ApplyMaterialTexture(target, time);
                break;
            case AnimationTrackKind.ComponentProperty:
                track.ApplyComponentProperty(target, time);
                break;
        }
    }

    /// <summary>The transform a target path names, or null when it no longer exists.</summary>
    public static Transform? Resolve(Transform root, string target)
    {
        if (root == null) return null;
        if (string.IsNullOrEmpty(target)) return root;
        return root.Find(target);
    }
}
