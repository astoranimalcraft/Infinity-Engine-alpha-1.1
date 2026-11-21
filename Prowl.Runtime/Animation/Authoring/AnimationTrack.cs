// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Infinity.Echo;
using Infinity.Runtime.Resources;
using Infinity.Vector;

namespace Infinity.Runtime.Animation;

/// <summary>What a track drives.</summary>
public enum AnimationTrackKind
{
    /// <summary>Local position of the target transform.</summary>
    TransformPosition,
    /// <summary>Local rotation of the target transform, in Euler degrees.</summary>
    TransformRotation,
    /// <summary>Local scale of the target transform.</summary>
    TransformScale,
    /// <summary>The sprite a renderer on the target shows.</summary>
    SpriteFrame,
}

/// <summary>A sprite shown from a given time on, until the next sprite key.</summary>
[Serializable]
public sealed class SpriteKey
{
    public float Time;
    public Sprite? Sprite;

    public SpriteKey() { }

    public SpriteKey(float time, Sprite? sprite)
    {
        Time = time;
        Sprite = sprite;
    }
}

/// <summary>
/// One animated property of one target: a transform under the take's root, or the root itself when the target path
/// is empty. Vector tracks carry one scalar curve per axis, so a track can key only the axis it animates and leave
/// the others where the object already is.
/// </summary>
[Serializable]
public sealed class AnimationTrack
{
    /// <summary>Keys closer than this in time are the same key.</summary>
    public const float TimeEpsilon = 1e-4f;

    [SerializeField] private string _target = string.Empty;
    [SerializeField] private AnimationTrackKind _kind;
    [SerializeField] private bool _enabled = true;
    [SerializeField] private AnimationCurve _x = new(1);
    [SerializeField] private AnimationCurve _y = new(1);
    [SerializeField] private AnimationCurve _z = new(1);
    [SerializeField] private List<SpriteKey> _sprites = [];

    public AnimationTrack() { }

    public AnimationTrack(string target, AnimationTrackKind kind)
    {
        _target = target ?? string.Empty;
        _kind = kind;
    }

    /// <summary>Path from the take's root, as <see cref="Transform.Find"/> reads it. Empty is the root.</summary>
    public string Target { get => _target; set => _target = value ?? string.Empty; }

    public AnimationTrackKind Kind { get => _kind; set => _kind = value; }

    public bool Enabled { get => _enabled; set => _enabled = value; }

    public AnimationCurve X => _x;
    public AnimationCurve Y => _y;
    public AnimationCurve Z => _z;
    public IReadOnlyList<SpriteKey> Sprites => _sprites;

    public bool IsSprite => _kind == AnimationTrackKind.SpriteFrame;

    public string KindName => _kind switch
    {
        AnimationTrackKind.TransformPosition => "Position",
        AnimationTrackKind.TransformRotation => "Rotation",
        AnimationTrackKind.TransformScale => "Scale",
        _ => "Sprite",
    };

    public string DisplayName => string.IsNullOrEmpty(_target) ? KindName : $"{_target}  ({KindName})";

    /// <summary>The number of keys across the track, which is what the timeline draws.</summary>
    public int KeyCount
    {
        get
        {
            if (IsSprite) return _sprites.Count;
            return Math.Max(_x.Count, Math.Max(_y.Count, _z.Count));
        }
    }

    public AnimationCurve CurveFor(int axis) => axis switch
    {
        0 => _x,
        1 => _y,
        _ => _z,
    };

    /// <summary>The value an axis holds at <paramref name="time"/>, or <paramref name="fallback"/> when it has no keys.</summary>
    public static float EvaluateAxis(AnimationCurve curve, float time, float fallback)
        => curve.Count == 0 ? fallback : curve.Evaluate(time);

    /// <summary>
    /// The vector at <paramref name="time"/>. Each axis falls back to <paramref name="fallback"/> on its own, so a
    /// position track keyed only on Y keeps the object's X and Z.
    /// </summary>
    public Float3 EvaluateVector(float time, Float3 fallback) => new(
        EvaluateAxis(_x, time, fallback.X),
        EvaluateAxis(_y, time, fallback.Y),
        EvaluateAxis(_z, time, fallback.Z));

    /// <summary>Writes a key on one axis, overwriting the key already at that time instead of stacking a second.</summary>
    public void SetKey(int axis, float time, float value, CurveInterpolation interpolation = CurveInterpolation.Linear)
        => SetKey(CurveFor(axis), time, value, interpolation);

    public static void SetKey(AnimationCurve curve, float time, float value, CurveInterpolation interpolation = CurveInterpolation.Linear)
    {
        for (int i = 0; i < curve.Count; i++)
        {
            if (MathF.Abs(curve[i].Time - time) > TimeEpsilon) continue;
            Keyframe key = curve[i];
            key.Value = value;
            key.Interpolation = interpolation;
            curve[i] = key;
            return;
        }

        curve.AddKey(new Keyframe(time, value) { Interpolation = interpolation });
    }

    /// <summary>Every distinct time any axis is keyed at, sorted, which is what the track row draws.</summary>
    public List<float> KeyTimes()
    {
        var times = new List<float>();
        if (IsSprite)
        {
            foreach (SpriteKey key in _sprites) times.Add(key.Time);
        }
        else
        {
            AddTimes(times, _x);
            AddTimes(times, _y);
            AddTimes(times, _z);
        }

        times.Sort();
        for (int i = times.Count - 1; i > 0; i--)
            if (times[i] - times[i - 1] <= TimeEpsilon) times.RemoveAt(i);
        return times;
    }

    private static void AddTimes(List<float> times, AnimationCurve curve)
    {
        for (int i = 0; i < curve.Count; i++) times.Add(curve[i].Time);
    }

    /// <summary>Removes every axis key at <paramref name="time"/>.</summary>
    public bool RemoveKeysAt(float time)
    {
        bool removed = false;
        for (int i = 0; i < _sprites.Count; i++)
        {
            if (MathF.Abs(_sprites[i].Time - time) > TimeEpsilon) continue;
            _sprites.RemoveAt(i--);
            removed = true;
        }

        removed |= RemoveAt(_x, time);
        removed |= RemoveAt(_y, time);
        removed |= RemoveAt(_z, time);
        return removed;
    }

    private static bool RemoveAt(AnimationCurve curve, float time)
    {
        for (int i = 0; i < curve.Count; i++)
        {
            if (MathF.Abs(curve[i].Time - time) > TimeEpsilon) continue;
            curve.RemoveKey(i);
            return true;
        }
        return false;
    }

    /// <summary>The sprite in force at <paramref name="time"/>, or null before the first key.</summary>
    public Sprite? EvaluateSprite(float time)
    {
        Sprite? found = null;
        float best = float.NegativeInfinity;
        foreach (SpriteKey key in _sprites)
        {
            if (key.Time > time + TimeEpsilon || key.Time < best) continue;
            best = key.Time;
            found = key.Sprite;
        }
        return found;
    }

    public SpriteKey AddSpriteKey(float time, Sprite? sprite)
    {
        foreach (SpriteKey existing in _sprites)
        {
            if (MathF.Abs(existing.Time - time) > TimeEpsilon) continue;
            existing.Sprite = sprite;
            return existing;
        }

        var key = new SpriteKey(time, sprite);
        int index = _sprites.FindIndex(k => k.Time > time);
        if (index < 0) _sprites.Add(key);
        else _sprites.Insert(index, key);
        return key;
    }

    public bool RemoveSpriteKey(SpriteKey key) => _sprites.Remove(key);

    /// <summary>Drops every key, both the curves and the sprites.</summary>
    public void ClearKeys()
    {
        _x.Clear();
        _y.Clear();
        _z.Clear();
        _sprites.Clear();
    }
}
