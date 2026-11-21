// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Infinity.Echo;
using Infinity.Runtime.Animation;
using Infinity.Vector;

namespace Infinity.Runtime;

/// <summary>
/// Plays an <see cref="AnimationTake"/> on the hierarchy under a root transform.
/// </summary>
/// <remarks>
/// This is the runtime API for authored animation. Everything the timeline editor does is available here:
/// <see cref="Play()"/>, <see cref="Pause"/>, <see cref="Stop"/>, <see cref="Seek"/>, <see cref="Sample"/> and
/// <see cref="Advance"/>, plus <see cref="CurrentTime"/> and <see cref="IsPlaying"/> to drive UI. Sampling writes
/// local position, Euler rotation, local scale and sprite frames, so it composes with physics and scripts that only
/// move the root.
/// </remarks>
[AddComponentMenu("Animation/Animation Player")]
[ComponentIcon("\uf008")]
public class AnimationPlayer : Component
{
    [SerializeField] private AnimationTake _take = new();

    /// <summary>Path from this object to the hierarchy the take drives. Empty drives this object.</summary>
    [SerializeField] private string _rootPath = string.Empty;

    [SerializeField] private bool _playOnEnable = true;
    [SerializeField] private float _speed = 1f;
    [SerializeField] private float _startTime;

    private float _currentTime;
    private bool _playing;

    /// <summary>The take this player samples. Edit it through the timeline editor.</summary>
    public AnimationTake Take => _take;

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

    /// <summary>Where playback is, in seconds, inside the take.</summary>
    public float CurrentTime => _currentTime;

    public bool IsPlaying => _playing;

    /// <summary>Raised whenever the position or the play state changes, so a view can follow the player.</summary>
    public event Action<AnimationPlayer>? Changed;

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
        _currentTime = Math.Clamp(_startTime, 0f, _take.Duration);
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
        _currentTime = Math.Clamp(from, 0f, _take.Duration);
        _playing = true;
        Apply();
        Changed?.Invoke(this);
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
        _currentTime = 0f;
        Apply();
        Changed?.Invoke(this);
    }

    /// <summary>Jumps to <paramref name="time"/> without changing the play state.</summary>
    public void Seek(float time) => Sample(time);

    /// <summary>Poses the hierarchy at <paramref name="time"/> without playing. This is what scrubbing calls.</summary>
    public void Sample(float time)
    {
        _currentTime = Math.Clamp(time, 0f, _take.Duration);
        Apply();
        Changed?.Invoke(this);
    }

    /// <summary>Moves playback forward by <paramref name="delta"/> seconds, wrapping or clamping at the end.</summary>
    public void Advance(float delta)
    {
        if (_take.Duration <= AnimationTake.MinimumDuration) return;

        float next = _currentTime + delta * _speed;
        if (_take.Loop)
        {
            _currentTime = _take.Wrap(next);
        }
        else
        {
            _currentTime = Math.Clamp(next, 0f, _take.Duration);
            if (_currentTime >= _take.Duration && _speed > 0f) _playing = false;
            else if (_currentTime <= 0f && _speed < 0f) _playing = false;
        }

        Apply();
        Changed?.Invoke(this);
    }

    /// <summary>Poses the hierarchy at the current time.</summary>
    public void Apply() => _take.Apply(Root, _currentTime);
}
