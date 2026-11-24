// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Infinity.Echo;
using Infinity.Runtime.Animation;
using Infinity.Vector;
using Infinity.Vector.Spatial;

namespace Infinity.Runtime;

/// <summary>
/// Plays an <see cref="AnimaTrack"/> on the hierarchy under a root transform.
/// </summary>
/// <remarks>
/// Assign an Anima Track asset created by the timeline. The track is the animation; this component is only the
/// source that plays it. Everything the timeline editor does is available here: <see cref="Play()"/>,
/// <see cref="Pause"/>, <see cref="Stop"/>, <see cref="Seek"/>, <see cref="Sample"/> and <see cref="Advance"/>,
/// plus <see cref="CurrentTime"/>, <see cref="NormalizedTime"/> and <see cref="IsPlaying"/> to drive UI. Sampling
/// writes local position, Euler rotation, local scale and sprite frames, so it composes with physics and scripts
/// that only move the root.
/// </remarks>
[AddComponentMenu("Animation/Animation Source")]
[ComponentDisplayName("Animation Source")]
[ComponentIcon("\uf008")]
public class AnimationPlayer : Component
{
    [SerializeField] private AnimaTrack? _animaTrack;

    // Kept for scenes authored before Anima Track existed. New content assigns _animaTrack instead.
    [SerializeField] private AnimationTake _take = new();

    /// <summary>Path from this object to the hierarchy the take drives. Empty drives this object.</summary>
    [SerializeField] private string _rootPath = string.Empty;

    [SerializeField] private bool _playOnEnable = true;
    [SerializeField] private float _speed = 1f;
    [SerializeField] private float _startTime;

    private float _currentTime;
    private bool _playing;
    private float _rangeStart = -1f;
    private float _rangeEnd = -1f;
    private AnimaTrack? _queued;
    private float _fadeDuration;
    private float _fadeElapsed;
    private Dictionary<Transform, TransformSnapshot>? _fadeFrom;

    private readonly record struct TransformSnapshot(Float3 Position, Float3 Euler, Float3 Scale);

    /// <summary>The Anima Track this source plays. The timeline creates and assigns it.</summary>
    public AnimaTrack? AnimaTrack
    {
        get => _animaTrack;
        set
        {
            _animaTrack = value;
            if (_animaTrack.IsValid() && _animaTrack!.Loop)
                Loop = true;
        }
    }

    /// <summary>True when an Anima Track asset is assigned.</summary>
    public bool HasAnimaTrack => _animaTrack.IsValid();

    /// <summary>The take this source samples. When an Anima Track is assigned, this is that asset's take.</summary>
    public AnimationTake Take => _animaTrack.IsValid() ? _animaTrack!.Take : _take;

    /// <summary>Length of the animation in seconds.</summary>
    public float Duration => Take.Duration;

    /// <summary>Playback position from 0 at the start to 1 at the end.</summary>
    public float NormalizedTime => Take.Duration > AnimationTake.MinimumDuration ? _currentTime / Take.Duration : 0f;

    /// <summary>Whether playback wraps at the end.</summary>
    public bool Loop
    {
        get => Take.Loop;
        set => Take.Loop = value;
    }

    /// <summary>True while playback is paused.</summary>
    public bool IsPaused => !_playing;

    public string RootPath
    {
        get => _rootPath;
        set => _rootPath = value ?? string.Empty;
    }

    /// <summary>Playback rate multiplier. Negative plays backwards.</summary>
    public float Speed
    {
        get => _speed;
        set => _speed = value;
    }

    /// <summary>Where playback starts, in seconds, when this source is enabled.</summary>
    public float StartTime
    {
        get => _startTime;
        set => _startTime = MathF.Max(0f, value);
    }

    /// <summary>Where playback is, in seconds, inside the take.</summary>
    public float CurrentTime => _currentTime;

    public bool IsPlaying => _playing;

    /// <summary>The track this source is playing, or null when it has none.</summary>
    public AnimaTrack? CurrentTrack => _animaTrack;

    /// <summary>The same value as <see cref="NormalizedTime"/>, named for UI code that reads a progress bar.</summary>
    public float Progress => NormalizedTime;

    /// <summary>Seconds left before a non-looping take finishes.</summary>
    public float RemainingTime => Take.Loop ? 0f : MathF.Max(0f, Take.Duration - _currentTime);

    /// <summary>True when playback has stopped on the last frame.</summary>
    public bool IsFinished => !_playing && IsAtEnd;

    /// <summary>True when playback is sitting on the first frame.</summary>
    public bool IsAtStart => _currentTime <= AnimationTake.MinimumDuration;

    /// <summary>True when only part of the take plays.</summary>
    public bool HasRange => _rangeStart >= 0f && _rangeEnd > _rangeStart;

    /// <summary>The first second of the active range, or 0.</summary>
    public float RangeStart => HasRange ? _rangeStart : 0f;

    /// <summary>The last second of the active range, or the take's duration.</summary>
    public float RangeEnd => HasRange ? _rangeEnd : Take.Duration;

    /// <summary>Raised whenever the position or the play state changes, so a view can follow the player.</summary>
    public event Action<AnimationPlayer>? Changed;

    /// <summary>Raised when a non-looping take reaches its end, before a queued track starts.</summary>
    public event Action<AnimationPlayer>? Completed;

    /// <summary>The hierarchy the take drives: the path from this object, or this object when the path is empty.</summary>
    public Transform Root
    {
        get
        {
            if (string.IsNullOrEmpty(_rootPath)) return Transform;
            return Transform.Find(_rootPath) ?? Transform;
        }
    }

    public override void OnEnable()
    {
        _currentTime = Math.Clamp(_startTime, 0f, Take.Duration);
        if (_playOnEnable) Play();
        else Apply();
    }

    public override void Update()
    {
        if (!_playing) return;
        Advance(Time.DeltaTime);
    }

    /// <summary>Starts playing from the beginning, or from <paramref name="from"/> seconds.</summary>
    public void Play(float from = 0f)
    {
        float min = HasRange ? _rangeStart : 0f;
        float max = HasRange ? _rangeEnd : Take.Duration;
        _currentTime = Math.Clamp(from, min, max);
        _playing = true;
        Apply();
        Changed?.Invoke(this);
    }

    /// <summary>Assigns an Anima Track and starts it, the usual call from a script.</summary>
    public void Play(AnimaTrack track, float from = 0f)
    {
        AnimaTrack = track;
        Play(from);
    }

    /// <summary>Plays a track with an explicit loop setting.</summary>
    public void PlayTrack(AnimaTrack track, bool loop = true, float from = 0f)
    {
        AnimaTrack = track;
        Loop = loop;
        Play(from);
    }

    /// <summary>Blends the current pose into another track over <paramref name="duration"/> seconds.</summary>
    public void CrossFadeTo(AnimaTrack track, float duration = 0.25f, float from = 0f) => CrossFade(track, duration, from);

    /// <summary>Poses the hierarchy at a time without changing the play state.</summary>
    public void SampleAt(float time) => Sample(time);

    /// <summary>The authored track for a target path and channel, or null when the take does not key it.</summary>
    public AnimationTrack? GetTrack(string target, AnimationTrackKind kind) => Take.FindTrack(target, kind);

    /// <summary>Assigns an Anima Track without changing the play state.</summary>
    public void SetAnimaTrack(AnimaTrack? track)
    {
        AnimaTrack = track;
        _currentTime = Math.Clamp(_currentTime, 0f, Take.Duration);
        Apply();
        Changed?.Invoke(this);
    }

    /// <summary>Plays an Anima Track once and holds its last pose.</summary>
    public void PlayOnce(AnimaTrack track)
    {
        AnimaTrack = track;
        Loop = false;
        Play(0f);
    }

    /// <summary>Switches to another Anima Track, blending the current pose into the new one.</summary>
    public void CrossFade(AnimaTrack track, float duration = 0.25f, float from = 0f)
    {
        if (duration <= 0f || !_playing)
        {
            Play(track, from);
            return;
        }

        CaptureFadeSource();
        _fadeDuration = duration;
        _fadeElapsed = 0f;
        AnimaTrack = track;
        _currentTime = Math.Clamp(from, 0f, Take.Duration);
        _playing = true;
        Apply();
        Changed?.Invoke(this);
    }

    /// <summary>Queues a track to start when the current one reaches its end.</summary>
    public void Queue(AnimaTrack track) => _queued = track;

    /// <summary>Stops and returns to the beginning, also clearing a queued track.</summary>
    public void StopAll()
    {
        _queued = null;
        _fadeFrom = null;
        Stop();
    }

    /// <summary>Sets the playback rate, in take seconds per real second.</summary>
    public void SetSpeed(float speed) => Speed = speed;

    /// <summary>Jumps to a normalized position, 0 at the start and 1 at the end.</summary>
    public void SeekNormalized(float normalized) => Seek(Math.Clamp(normalized, 0f, 1f) * Take.Duration);

    /// <summary>Starts playing at a normalized position, 0 at the start and 1 at the end.</summary>
    public void PlayNormalized(float normalized) => Play(Math.Clamp(normalized, 0f, 1f) * Take.Duration);

    /// <summary>Sets whether playback wraps, the usual call from a script or an animation event.</summary>
    public void SetLoop(bool loop) => Loop = loop;

    /// <summary>True when a non-looping take is sitting on its last frame.</summary>
    public bool IsAtEnd => !Take.Loop && Take.Duration > AnimationTake.MinimumDuration
        && _currentTime >= Take.Duration - AnimationTake.MinimumDuration;

    /// <summary>Poses the hierarchy at a normalized position without playing.</summary>
    public void SampleNormalized(float normalized) => Sample(Math.Clamp(normalized, 0f, 1f) * Take.Duration);

    /// <summary>Restarts from the current track's beginning.</summary>
    public void Restart() => Play(0f);

    /// <summary>Pauses when playing and resumes when paused.</summary>
    public void TogglePause()
    {
        if (_playing) Pause();
        else Resume();
    }

    /// <summary>Sets the playback position in seconds without changing the play state.</summary>
    public void SetTime(float seconds) => Seek(seconds);

    /// <summary>The playback position in seconds.</summary>
    public float GetTime() => _currentTime;

    /// <summary>The playback position from 0 at the start to 1 at the end.</summary>
    public float GetNormalizedTime() => NormalizedTime;

    /// <summary>Assigns a track without starting it, the usual call from a graph.</summary>
    public void SetAnimation(AnimaTrack? track) => SetAnimaTrack(track);

    /// <summary>Assigns a track and starts it, with loop and start time.</summary>
    public void PlayAnimation(AnimaTrack track, bool loop = true, float from = 0f) => PlayTrack(track, loop, from);

    /// <summary>Samples a position channel at a normalized time, for gameplay code that reads an animation.</summary>
    public Float3 SamplePosition(string target, float normalized, Float3 fallback)
        => Take.FindTrack(target, AnimationTrackKind.TransformPosition)?.EvaluateVector(normalized * Take.Duration, fallback) ?? fallback;

    /// <summary>Samples a rotation channel at a normalized time.</summary>
    public Float3 SampleRotation(string target, float normalized, Float3 fallback)
        => Take.FindTrack(target, AnimationTrackKind.TransformRotation)?.EvaluateVector(normalized * Take.Duration, fallback) ?? fallback;

    /// <summary>Samples a scale channel at a normalized time.</summary>
    public Float3 SampleScale(string target, float normalized, Float3 fallback)
        => Take.FindTrack(target, AnimationTrackKind.TransformScale)?.EvaluateVector(normalized * Take.Duration, fallback) ?? fallback;

    /// <summary>Samples a rotation channel as a quaternion at a normalized time.</summary>
    public Quaternion SampleRotationQuaternion(string target, float normalized, Quaternion fallback)
        => Take.FindTrack(target, AnimationTrackKind.TransformRotation) is { } track
            ? Quaternion.FromEuler(track.EvaluateVector(normalized * Take.Duration, fallback.EulerAngles))
            : fallback;

    /// <summary>Samples position, rotation and scale at a normalized time, for 3D gameplay code.</summary>
    public Transform3D SampleTransform(string target, float normalized, Transform3D fallback)
        => new(
            SamplePosition(target, normalized, fallback.position),
            SampleRotationQuaternion(target, normalized, fallback.rotation),
            SampleScale(target, normalized, fallback.scale));

    /// <summary>Samples a position channel at a time in seconds.</summary>
    public Float3 SamplePositionAtTime(string target, float seconds, Float3 fallback)
        => Take.FindTrack(target, AnimationTrackKind.TransformPosition)?.EvaluateVector(seconds, fallback) ?? fallback;

    /// <summary>Samples a rotation channel at a time in seconds.</summary>
    public Float3 SampleRotationAtTime(string target, float seconds, Float3 fallback)
        => Take.FindTrack(target, AnimationTrackKind.TransformRotation)?.EvaluateVector(seconds, fallback) ?? fallback;

    /// <summary>Samples a scale channel at a time in seconds.</summary>
    public Float3 SampleScaleAtTime(string target, float seconds, Float3 fallback)
        => Take.FindTrack(target, AnimationTrackKind.TransformScale)?.EvaluateVector(seconds, fallback) ?? fallback;

    /// <summary>Samples a rotation channel as a quaternion at a time in seconds.</summary>
    public Quaternion SampleRotationQuaternionAtTime(string target, float seconds, Quaternion fallback)
        => Take.FindTrack(target, AnimationTrackKind.TransformRotation) is { } track
            ? Quaternion.FromEuler(track.EvaluateVector(seconds, fallback.EulerAngles))
            : fallback;

    /// <summary>Samples position, rotation and scale at a time in seconds.</summary>
    public Transform3D SampleTransformAtTime(string target, float seconds, Transform3D fallback)
        => new(
            SamplePositionAtTime(target, seconds, fallback.position),
            SampleRotationQuaternionAtTime(target, seconds, fallback.rotation),
            SampleScaleAtTime(target, seconds, fallback.scale));

    /// <summary>The sprite a sprite-frame track shows at a normalized time, for 2D animation.</summary>
    public Resources.Sprite? SampleSprite(string target, float normalized)
        => Take.FindTrack(target, AnimationTrackKind.SpriteFrame)?.EvaluateSprite(normalized * Take.Duration);

    /// <summary>Plays only the seconds between <paramref name="start"/> and <paramref name="end"/>.</summary>
    public void PlayRange(float start, float end, bool loop = true, float from = -1f)
    {
        float duration = Take.Duration;
        float first = Math.Clamp(MathF.Min(start, end), 0f, duration);
        float last = Math.Clamp(MathF.Max(start, end), 0f, duration);
        if (last - first <= AnimationTake.MinimumDuration) return;

        _rangeStart = first;
        _rangeEnd = last;
        Loop = loop;
        Play(from >= 0f ? from : first);
        _currentTime = Math.Clamp(_currentTime, first, last);
        Apply();
    }

    /// <summary>Returns playback to the whole take.</summary>
    public void ClearRange()
    {
        _rangeStart = -1f;
        _rangeEnd = -1f;
    }

    /// <summary>Plays the take backwards from its end, or from a given second.</summary>
    public void PlayBackwards(float from = -1f)
    {
        Speed = -MathF.Abs(Speed <= 0f ? 1f : Speed);
        Play(from >= 0f ? from : Take.Duration);
    }

    /// <summary>Plays the take forwards from its start, restoring a negative speed.</summary>
    public void PlayForwards(float from = 0f)
    {
        Speed = MathF.Abs(Speed);
        Play(from);
    }

    /// <summary>Starts the assigned track from its first key.</summary>
    public void PlayFromStart() => Play(0f);

    /// <summary>Moves the playhead by a number of seconds, clamped to the take.</summary>
    public void AddTime(float seconds) => Seek(_currentTime + seconds);

    /// <summary>Moves the playhead to a normalized time, 0 at the start and 1 at the end.</summary>
    public void SetNormalizedTime(float normalized) => SeekNormalized(normalized);

    private void CaptureFadeSource()
    {
        _fadeFrom = [];
        CollectFadeTransforms(Root, _fadeFrom);
    }

    private static void CollectFadeTransforms(Transform root, Dictionary<Transform, TransformSnapshot> into)
    {
        into[root] = new TransformSnapshot(root.LocalPosition, root.LocalEulerAngles, root.LocalScale);
        foreach (Transform child in root.GetChildren()) CollectFadeTransforms(child, into);
    }

    private void ApplyFade()
    {
        if (_fadeFrom == null || _fadeDuration <= 0f) return;

        _fadeElapsed += Time.DeltaTime;
        float t = Math.Clamp(_fadeElapsed / _fadeDuration, 0f, 1f);
        float smooth = t * t * (3f - 2f * t);

        foreach ((Transform transform, TransformSnapshot from) in _fadeFrom)
        {
            if (transform == null) continue;
            transform.LocalPosition = Maths.Lerp(from.Position, transform.LocalPosition, smooth);
            transform.LocalEulerAngles = Maths.Lerp(from.Euler, transform.LocalEulerAngles, smooth);
            transform.LocalScale = Maths.Lerp(from.Scale, transform.LocalScale, smooth);
        }

        if (t >= 1f) _fadeFrom = null;
    }

    public void Pause()
    {
        if (!_playing) return;
        _playing = false;
        Changed?.Invoke(this);
    }

    public void Resume()
    {
        if (_playing) return;
        _playing = true;
        Changed?.Invoke(this);
    }

    /// <summary>Stops and returns to the first frame, which it also applies.</summary>
    public void Stop()
    {
        _playing = false;
        _currentTime = HasRange ? _rangeStart : 0f;
        Apply();
        Changed?.Invoke(this);
    }

    /// <summary>Jumps to <paramref name="time"/> without changing the play state.</summary>
    public void Seek(float time) => Sample(time);

    /// <summary>Poses the hierarchy at <paramref name="time"/> without playing. This is what scrubbing calls.</summary>
    public void Sample(float time)
    {
        _currentTime = Math.Clamp(time, 0f, Take.Duration);
        Apply();
        Changed?.Invoke(this);
    }

    /// <summary>Moves playback forward by <paramref name="delta"/> seconds, wrapping or clamping at the end.</summary>
    public void Advance(float delta)
    {
        if (Take.Duration <= AnimationTake.MinimumDuration) return;

        float min = HasRange ? _rangeStart : 0f;
        float max = HasRange ? _rangeEnd : Take.Duration;
        float next = _currentTime + delta * _speed;
        if (Take.Loop)
        {
            float span = max - min;
            if (span <= AnimationTake.MinimumDuration)
            {
                _currentTime = min;
            }
            else
            {
                float wrapped = (next - min) % span;
                if (wrapped < 0f) wrapped += span;
                _currentTime = min + wrapped;
            }
        }
        else
        {
            _currentTime = Math.Clamp(next, min, max);
            if (_currentTime >= max && _speed > 0f)
            {
                _playing = false;
                Completed?.Invoke(this);
                PlayQueued();
            }
            else if (_currentTime <= min && _speed < 0f)
            {
                _playing = false;
                Completed?.Invoke(this);
                PlayQueued();
            }
        }

        Apply();
        Changed?.Invoke(this);
    }

    private void PlayQueued()
    {
        if (_queued.IsNotValid()) return;
        AnimaTrack next = _queued!;
        _queued = null;
        ClearRange();
        AnimaTrack = next;
        _currentTime = 0f;
        _playing = true;
    }

    /// <summary>Poses the hierarchy at the current time.</summary>
    public void Apply()
    {
        Take.Apply(Root, _currentTime);
        ApplyFade();
    }
}
