// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;

using Infinity.Echo;
using Infinity.Runtime.Resources;
using Infinity.Vector;
using Infinity.Vector.Spatial;

namespace Infinity.Runtime.Animation;

/// <summary>
/// A timeline animation saved as an asset. An <see cref="AnimationPlayer"/> plays one, and an animation graph can
/// sample one through the Anima Track node. Keeping the authored curves in an asset (rather than inside a component)
/// is what lets the same animation be reused, blended and driven from code.
/// </summary>
[CreateAssetMenu("Animation/Anima Track", Extension = ".animatrack", Order = 1140)]
public sealed class AnimaTrack : Asset
{
    /// <summary>The authored tracks. The timeline edits this in place.</summary>
    public AnimationTake Take = new();

    public AnimaTrack() : base("Anima Track") { }

    public AnimaTrack(string name) : base(name) { }

    /// <summary>Length in seconds.</summary>
    public float Duration
    {
        get => Take.Duration;
        set => Take.Duration = value;
    }

    /// <summary>The rate the timeline snaps keys to.</summary>
    public float FrameRate
    {
        get => Take.FrameRate;
        set => Take.FrameRate = value;
    }

    /// <summary>Whether playback wraps at the end.</summary>
    public bool Loop
    {
        get => Take.Loop;
        set => Take.Loop = value;
    }

    public IReadOnlyList<AnimationTrack> Tracks => Take.Tracks;

    public int FrameCount => Take.FrameCount;

    public float FrameToTime(int frame) => Take.FrameToTime(frame);

    public int TimeToFrame(float time) => Take.TimeToFrame(time);

    public float Snap(float time) => Take.Snap(time);

    public AnimationTrack GetOrAddTrack(string target, AnimationTrackKind kind) => Take.GetOrAddTrack(target, kind);

    public AnimationTrack GetOrAddTrack(string target, AnimationTrackKind kind, string component, string property, int channels)
        => Take.GetOrAddTrack(target, kind, component, property, channels);

    public AnimationTrack? FindTrack(string target, AnimationTrackKind kind) => Take.FindTrack(target, kind);

    /// <summary>True when the take keys the given target and channel.</summary>
    public bool HasTrack(string target, AnimationTrackKind kind) => FindTrack(target, kind) != null;

    /// <summary>Samples a position channel, falling back to <paramref name="fallback"/> for unkeyed channels.</summary>
    public Float3 SamplePosition(string target, float time, Float3 fallback)
        => EvaluateVector(target, AnimationTrackKind.TransformPosition, time, fallback);

    /// <summary>Samples a rotation channel as Euler angles.</summary>
    public Float3 SampleRotation(string target, float time, Float3 fallback)
        => EvaluateVector(target, AnimationTrackKind.TransformRotation, time, fallback);

    /// <summary>Samples a scale channel, falling back to <paramref name="fallback"/> for unkeyed channels.</summary>
    public Float3 SampleScale(string target, float time, Float3 fallback)
        => EvaluateVector(target, AnimationTrackKind.TransformScale, time, fallback);

    /// <summary>Samples a rotation channel as a quaternion, which is what a 3D pose or skeleton consumes.</summary>
    public Quaternion SampleRotationQuaternion(string target, float time, Quaternion fallback)
    {
        AnimationTrack? track = FindTrack(target, AnimationTrackKind.TransformRotation);
        return track == null ? fallback : Quaternion.FromEuler(track.EvaluateVector(time, fallback.EulerAngles));
    }

    /// <summary>
    /// Samples position, rotation and scale together. Channels the take does not key keep the matching value from
    /// <paramref name="fallback"/>, so a partial take composes over a bind pose or the current transform.
    /// </summary>
    public Transform3D SampleTransform(string target, float time, Transform3D fallback)
        => new(
            SamplePosition(target, time, fallback.position),
            SampleRotationQuaternion(target, time, fallback.rotation),
            SampleScale(target, time, fallback.scale));

    public bool RemoveTrack(AnimationTrack track) => Take.RemoveTrack(track);

    public List<float> AllKeyTimes() => Take.AllKeyTimes();

    /// <summary>Poses a hierarchy at <paramref name="time"/>.</summary>
    public void Apply(Transform root, float time) => Take.Apply(root, time);

    /// <summary>Poses a hierarchy at a normalized time, 0 at the start and 1 at the end.</summary>
    public void ApplyNormalized(Transform root, float normalized)
        => Take.Apply(root, Math.Clamp(normalized, 0f, 1f) * Take.Duration);

    /// <summary>Samples one track into a vector, with the fallback used for channels the track does not key.</summary>
    public Float3 EvaluateVector(string target, AnimationTrackKind kind, float time, Float3 fallback)
    {
        AnimationTrack? track = FindTrack(target, kind);
        return track == null ? fallback : track.EvaluateVector(time, fallback);
    }

    /// <summary>Moves every key at or after <paramref name="at"/> by <paramref name="amount"/>.</summary>
    public void InsertTime(float at, float amount) => Take.InsertTime(at, amount);

    /// <summary>Sets the length in seconds.</summary>
    public void SetDuration(float seconds) => Duration = seconds;

    /// <summary>Sets the timeline frame rate.</summary>
    public void SetFrameRate(float framesPerSecond) => FrameRate = framesPerSecond;

    /// <summary>Sets whether playback wraps at the end.</summary>
    public void SetLoop(bool loop) => Loop = loop;

    /// <summary>The sprite a sprite-frame track shows at a time, or null when the track is missing.</summary>
    public Sprite? SampleSprite(string target, float time)
        => Take.FindTrack(target, AnimationTrackKind.SpriteFrame)?.EvaluateSprite(time);

    /// <summary>The sprite a sprite-frame track shows at a normalized time.</summary>
    public Sprite? SampleSpriteNormalized(string target, float normalized)
        => SampleSprite(target, Math.Clamp(normalized, 0f, 1f) * Take.Duration);

    /// <summary>True when the track keys a transform channel for the target.</summary>
    public bool HasTransformTrack(string target)
        => HasTrack(target, AnimationTrackKind.TransformPosition) ||
           HasTrack(target, AnimationTrackKind.TransformRotation) ||
           HasTrack(target, AnimationTrackKind.TransformScale);

    /// <summary>Removes every track, keeping the length and playback settings.</summary>
    public void ClearTracks()
    {
        foreach (AnimationTrack track in Take.Tracks.ToArray()) Take.RemoveTrack(track);
    }

    /// <summary>Reverses every track in place, mirroring the key times around the take's duration.</summary>
    public void Reverse()
    {
        float duration = Take.Duration;
        foreach (AnimationTrack track in Take.Tracks)
            ReverseTrack(track, duration);
    }

    private static void ReverseTrack(AnimationTrack track, float duration)
    {
        if (track.IsSprite || track.IsTexture)
        {
            // Sprite and texture keys are already ordered; moving them through a mirror keeps that order.
            List<float> times = track.KeyTimes();
            for (int i = 0; i < times.Count; i++)
                track.MoveKeys(times[i], duration - times[i]);
            return;
        }

        for (int axis = 0; axis < Math.Max(1, track.Channels); axis++)
        {
            AnimationCurve curve = track.CurveFor(axis);
            for (int i = 0; i < curve.Count; i++)
            {
                Keyframe key = curve[i];
                key.Time = MathF.Max(0f, duration - key.Time);
                curve[i] = key;
            }
        }
    }

    /// <summary>Copies another track's keys onto the end of this one, offset by this take's duration.</summary>
    public void Append(AnimaTrack other)
    {
        if (other.IsNotValid()) return;

        float offset = Take.Duration;
        foreach (AnimationTrack source in other.Take.Tracks)
        {
            AnimationTrack target = Take.GetOrAddTrack(source.Target, source.Kind, source.Component, source.Property, source.Channels);
            for (int axis = 0; axis < Math.Max(1, source.Channels); axis++)
            {
                AnimationCurve curve = source.CurveFor(axis);
                for (int i = 0; i < curve.Count; i++)
                    AnimationTrack.SetKey(target.CurveFor(axis), curve[i].Time + offset, curve[i].Value, curve[i].Interpolation);
            }

            foreach (SpriteKey key in source.Sprites)
                target.AddSpriteKey(key.Time + offset, key.Sprite);
            foreach (TextureKey key in source.Textures)
                target.AddTextureKey(key.Time + offset, key.Texture);
        }

        Take.Duration = offset + other.Take.Duration;
    }

    /// <summary>Removes keys before <paramref name="time"/> and shifts the rest to start at zero.</summary>
    public void TrimStart(float time)
    {
        if (time <= 0f) return;

        foreach (AnimationTrack track in Take.Tracks)
        {
            track.ShiftKeys(0f, -time);
            List<float> times = track.KeyTimes();
            for (int i = times.Count - 1; i >= 0; i--)
                if (times[i] < -AnimationTrack.TimeEpsilon) track.RemoveKeysAt(times[i]);
        }

        Take.Duration = MathF.Max(AnimationTake.MinimumDuration, Take.Duration - time);
    }

    /// <summary>Captures the current pose of a hierarchy into a new track set, which is how a timeline is seeded.</summary>
    public static AnimaTrack Capture(Transform root, string name = "Anima Track")
    {
        var track = new AnimaTrack(name);
        if (root == null) return track;

        void Capture(Transform current)
        {
            string path = Transform.GetRelativePath(current, root);
            track.Take.GetOrAddTrack(path, AnimationTrackKind.TransformPosition)
                .SetVectorKey(0f, new Float4(current.LocalPosition.X, current.LocalPosition.Y, current.LocalPosition.Z, 0f));
            track.Take.GetOrAddTrack(path, AnimationTrackKind.TransformRotation)
                .SetVectorKey(0f, new Float4(current.LocalEulerAngles.X, current.LocalEulerAngles.Y, current.LocalEulerAngles.Z, 0f));
            track.Take.GetOrAddTrack(path, AnimationTrackKind.TransformScale)
                .SetVectorKey(0f, new Float4(current.LocalScale.X, current.LocalScale.Y, current.LocalScale.Z, 0f));

            foreach (Transform child in current.GetChildren()) Capture(child);
        }

        Capture(root);
        return track;
    }
}
