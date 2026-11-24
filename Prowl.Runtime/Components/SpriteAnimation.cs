// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Linq;

using Infinity.Runtime.Resources;
using Infinity.Vector;

namespace Infinity.Runtime;

/// <summary>Which way the frames run.</summary>
public enum SpriteAnimationMode
{
    /// <summary>First to last, then back to the first.</summary>
    Forward,

    /// <summary>First to last, then back again, without repeating the ends.</summary>
    PingPong,

    /// <summary>Last to first.</summary>
    Reverse
}

/// <summary>
/// Plays a flipbook of 2D sprites into a <see cref="SpriteRenderer"/> on the same GameObject, from a sprite sheet
/// sliced into a grid or from a list of sprites. Point it at a sheet, set the columns and rows, and it animates;
/// drop it on a character with a <see cref="SpriteRenderer"/> and that character is a 2D-HD sprite.
/// </summary>
[AddComponentMenu("2D/Sprite Animation")]
[ComponentIcon("\uf008")] // Film
public class SpriteAnimation : Component
{
    [Header("Frames")]
    /// <summary>
    /// A flipbook asset to play. When set it supplies the frames, rate, loop and mode, so one asset can be shared by
    /// many objects and edited in the sprite editor's frame timeline.
    /// </summary>
    public Flipbook? Flipbook;

    /// <summary>Use the flipbook's rate, loop and mode instead of this component's own settings.</summary>
    public bool UseFlipbookSettings = true;

    /// <summary>
    /// The sheet the frames are cut from, left to right then top to bottom. Ignored while <see cref="Frames"/>
    /// holds sprites, which is how frames that live in separate images animate together.
    /// </summary>
    public Texture2D? Sheet;

    /// <summary>How many columns the sheet is cut into.</summary>
    public int Columns = 4;

    /// <summary>How many rows the sheet is cut into.</summary>
    public int Rows = 4;

    /// <summary>How many cells of the grid are frames, 0 for all of them. A sheet with a blank tail sets this.</summary>
    public int FrameCount;

    /// <summary>The cell the animation starts at, which is how one sheet holds several animations.</summary>
    public int FirstFrame;

    /// <summary>Sprites to play instead of slicing <see cref="Sheet"/>.</summary>
    public Sprite?[] Frames = [];

    [Header("Playback")]
    /// <summary>Frames a second.</summary>
    [Range(0.1f, 120f)] public float FramesPerSecond = 12f;

    /// <summary>Whether the animation starts again at the end, or holds the last frame.</summary>
    public bool Loop = true;

    /// <summary>Which way the frames run.</summary>
    public SpriteAnimationMode Mode = SpriteAnimationMode.Forward;

    /// <summary>Whether it starts playing when the GameObject comes alive.</summary>
    public bool PlayOnAwake = true;

    /// <summary>Where playback starts, from 0 at the first frame to 1 at the last.</summary>
    [Range(0f, 1f)] public float StartTime;

    [Header("Output")]
    /// <summary>The point of a frame that sits on the GameObject's position, 0.5,0.5 for its middle.</summary>
    public Float2 Pivot = new(0.5f, 0.5f);

    /// <summary>How many sprite pixels make a world unit, which sets the size the frames draw at.</summary>
    public float PixelsPerUnit = 100f;

    /// <summary>Adds a <see cref="SpriteRenderer"/> when there is none, so the animation shows on its own.</summary>
    public bool AutoCreateRenderer = true;

    /// <summary>Draw the same frame mirrored on the back of the object.</summary>
    public bool DoubleSided;

    /// <summary>Create the back-facing renderer automatically when <see cref="DoubleSided"/> is on.</summary>
    public bool AutoCreateBackFace = true;

    /// <summary>Raised after the frame changes, with its index.</summary>
    public event Action<int>? FrameChanged;

    /// <summary>Raised when a non-looping animation reaches its last frame.</summary>
    public event Action<SpriteAnimation>? Completed;

    [System.NonSerialized, NotHeld] private Sprite?[] _built = [];
    [System.NonSerialized, NotHeld] private SpriteRenderer? _renderer;
    [System.NonSerialized, NotHeld] private SpriteRenderer? _backRenderer;
    [System.NonSerialized, NotHeld] private GameObject? _backObject;
    [System.NonSerialized, NotHeld] private Flipbook? _builtBook;
    [System.NonSerialized, NotHeld] private Texture2D? _builtSheet;
    [System.NonSerialized, NotHeld] private Sprite?[] _builtFrom = [];
    [System.NonSerialized, NotHeld] private int _builtColumns, _builtRows, _builtCount, _builtFirst;
    [System.NonSerialized, NotHeld] private Float2 _builtPivot;
    [System.NonSerialized, NotHeld] private float _builtPpu;
    [System.NonSerialized, NotHeld] private float _time;
    [System.NonSerialized, NotHeld] private int _frame;
    [System.NonSerialized, NotHeld] private int _direction = 1;
    [System.NonSerialized, NotHeld] private bool _playing;
    [System.NonSerialized, NotHeld] private bool _started;
    [System.NonSerialized, NotHeld] private int _rangeStart = -1;
    [System.NonSerialized, NotHeld] private int _rangeEnd = -1;

    /// <summary>The frame on screen.</summary>
    public int CurrentFrame => _frame;

    /// <summary>How far playback has run, 0 at the first frame and 1 past the last.</summary>
    public float NormalizedTime
    {
        get
        {
            int count = FrameSprites.Length;
            return count <= 1 ? 0f : _frame / (float)(count - 1);
        }
    }

    /// <summary>Whether frames are advancing.</summary>
    public bool IsPlaying => _playing;

    /// <summary>How many frames the animation has.</summary>
    public int FrameCountTotal => FrameSprites.Length;

    /// <summary>The sprites being played, built from <see cref="Sheet"/> or taken from <see cref="Frames"/>.</summary>
    public Sprite?[] FrameSprites
    {
        get
        {
            Rebuild();
            return _built;
        }
    }

    /// <summary>The sprite currently on screen, or null when nothing is playing.</summary>
    public Sprite? CurrentSprite => _frame >= 0 && _frame < _built.Length ? _built[_frame] : null;

    /// <summary>Seconds one frame is shown at the current rate.</summary>
    public float FrameDuration => FramesPerSecond > 0.0001f ? 1f / FramesPerSecond : 0f;

    /// <summary>Plays a flipbook, optionally switching the component's settings to the book's.</summary>
    public void SetFlipbook(Flipbook? flipbook, bool useBookSettings = true)
    {
        Flipbook = flipbook;
        UseFlipbookSettings = useBookSettings;
        Rebuild();
        _frame = 0;
        _time = 0f;
        _direction = 1;
        Play();
    }

    /// <summary>Starts playing from the current frame.</summary>
    public void Play()
    {
        _playing = true;
        _started = true;
        Apply();
    }

    /// <summary>Plays from the first frame once and holds the last frame.</summary>
    public void PlayOnce()
    {
        Loop = false;
        PlayFrom(0);
    }

    /// <summary>Plays backwards from the current frame.</summary>
    public void PlayReverse()
    {
        Mode = SpriteAnimationMode.Reverse;
        _direction = -1;
        Play();
    }

    /// <summary>Plays forward then back.</summary>
    public void PlayPingPong()
    {
        Mode = SpriteAnimationMode.PingPong;
        _direction = 1;
        Play();
    }

    /// <summary>Sets the playback rate in frames per second.</summary>
    public void SetSpeed(float framesPerSecond) => FramesPerSecond = MathF.Max(0f, framesPerSecond);

    /// <summary>Sets the playback rate in frames per second.</summary>
    public void SetFrameRate(float framesPerSecond) => SetSpeed(framesPerSecond);

    /// <summary>Plays backwards from the current frame, as a readable alias of <see cref="PlayReverse"/>.</summary>
    public void PlayBackwards() => PlayReverse();

    /// <summary>
    /// Plays only the frames between <paramref name="firstFrame"/> and <paramref name="lastFrame"/>, which is how a
    /// single sheet holds several clips. The range stays until <see cref="ClearRange"/> or a new play call resets it.
    /// </summary>
    public void PlayRange(int firstFrame, int lastFrame, bool loop = true)
    {
        int count = FrameSprites.Length;
        if (count == 0) return;

        int start = Math.Clamp(Math.Min(firstFrame, lastFrame), 0, count - 1);
        int end = Math.Clamp(Math.Max(firstFrame, lastFrame), 0, count - 1);
        Loop = loop;
        PlayFrom(start);
        _direction = Mode == SpriteAnimationMode.Reverse ? -1 : 1;
        _rangeStart = start;
        _rangeEnd = end;
    }

    /// <summary>Returns playback to the whole frame list.</summary>
    public void ClearRange()
    {
        _rangeStart = -1;
        _rangeEnd = -1;
    }

    /// <summary>Moves one frame forward without starting playback.</summary>
    public void NextFrame() => SetFrame(_frame + 1);

    /// <summary>Moves one frame back without starting playback.</summary>
    public void PreviousFrame() => SetFrame(_frame - 1);

    /// <summary>Maps a normalized time to a frame and shows it.</summary>
    public void SetNormalizedTime(float normalized)
    {
        int count = FrameSprites.Length;
        if (count == 0) return;
        SetFrame((int)MathF.Round(Math.Clamp(normalized, 0f, 1f) * (count - 1)));
    }

    /// <summary>Assigns a flipbook and starts it from a frame, the usual call from a script.</summary>
    public void PlayFlipbook(Flipbook flipbook, bool useBookSettings = true, int fromFrame = 0)
    {
        SetFlipbook(flipbook, useBookSettings);
        PlayFrom(fromFrame);
    }

    /// <summary>Maps a normalized time to a frame and shows it, without starting playback.</summary>
    public void SetFrameNormalized(float normalized)
    {
        int count = FrameSprites.Length;
        if (count == 0) return;
        SetFrame((int)MathF.Round(Math.Clamp(normalized, 0f, 1f) * (count - 1)));
    }

    /// <summary>Starts playing from a frame, clamped into range.</summary>
    public void PlayFrom(int frame)
    {
        int count = FrameSprites.Length;
        if (count == 0) return;

        ClearRange();
        _frame = Math.Clamp(frame, 0, count - 1);
        _time = 0f;
        _direction = 1;
        Play();
    }

    /// <summary>Stops advancing and holds the frame on screen.</summary>
    public void Pause() => _playing = false;

    /// <summary>Stops and returns to the first frame.</summary>
    public void Stop()
    {
        _playing = false;
        _frame = 0;
        _time = 0f;
        _direction = 1;
        Apply();
    }

    /// <summary>Moves to a frame without starting playback.</summary>
    public void SetFrame(int frame)
    {
        int count = FrameSprites.Length;
        if (count == 0) return;

        _frame = Math.Clamp(frame, 0, count - 1);
        Apply();
    }

    /// <summary>The sprite at a frame, or null when the index is out of range.</summary>
    public Sprite? GetFrame(int frame)
    {
        Sprite?[] frames = FrameSprites;
        return frame >= 0 && frame < frames.Length ? frames[frame] : null;
    }

    /// <summary>Shows a frame and starts playing from it.</summary>
    public void PlayFrame(int frame) => PlayFrom(frame);

    /// <summary>Sets whether playback wraps at the end.</summary>
    public void SetLoop(bool loop) => Loop = loop;

    /// <summary>Sets the playback direction mode.</summary>
    public void SetMode(SpriteAnimationMode mode)
    {
        Mode = mode;
        _direction = mode == SpriteAnimationMode.Reverse ? -1 : 1;
    }

    /// <summary>Turns the mirrored back face on or off and creates or removes it immediately.</summary>
    public void SetDoubleSided(bool doubleSided)
    {
        DoubleSided = doubleSided;
        EnsureBackFace();
        Apply();
    }

    /// <summary>Sets the pivot of the frame that sits on the object's position.</summary>
    public void SetPivot(Float2 pivot)
    {
        Pivot = pivot;
        Rebuild();
        Apply();
    }

    /// <summary>Sets how many sprite pixels make a world unit.</summary>
    public void SetPixelsPerUnit(float pixelsPerUnit)
    {
        PixelsPerUnit = MathF.Max(0.0001f, pixelsPerUnit);
        Rebuild();
        Apply();
    }

    /// <summary>Restarts from the first frame.</summary>
    public void Restart() => PlayFrom(0);

    /// <summary>Pauses when playing and resumes when paused.</summary>
    public void TogglePause()
    {
        if (_playing) Pause();
        else Play();
    }

    /// <summary>The playback position from 0 at the first frame to 1 at the last.</summary>
    public float GetNormalizedTime() => NormalizedTime;

    /// <summary>The sprite on screen.</summary>
    public Sprite? GetCurrentSprite() => CurrentSprite;

    /// <summary>How many frames the animation has.</summary>
    public int GetFrameCount() => FrameCountTotal;

    /// <summary>Sets whether playback starts when the object comes alive.</summary>
    public void SetPlayOnAwake(bool playOnAwake) => PlayOnAwake = playOnAwake;

    /// <summary>Plays a flipbook from a frame, the usual one call from a graph.</summary>
    public void Play(Flipbook flipbook, int fromFrame = 0)
    {
        SetFlipbook(flipbook);
        PlayFrom(fromFrame);
    }

    /// <summary>Shows a frame of a flipbook without changing the play state.</summary>
    public void SetFlipbookFrame(Flipbook flipbook, int frame)
    {
        SetFlipbook(flipbook);
        Pause();
        SetFrame(frame);
    }

    /// <summary>Sets the frames played, as a first and last index into the current frame list.</summary>
    public void SetFrameRange(int firstFrame, int lastFrame) => PlayRange(firstFrame, lastFrame, Loop);

    /// <summary>Returns playback to the whole frame list.</summary>
    public void ClearFrameRange() => ClearRange();

    /// <summary>Moves to a time in seconds without starting playback.</summary>
    public void SetTime(float seconds) => SetFrame(TimeToFrame(seconds));

    /// <summary>Seconds a frame is on screen at the current rate.</summary>
    public float FrameToTime(int frame) => frame * FrameDuration;

    /// <summary>The frame that would be shown after <paramref name="time"/> seconds of playback.</summary>
    public int TimeToFrame(float time)
    {
        int count = FrameSprites.Length;
        if (count <= 1 || FrameDuration <= 0f) return 0;
        return Math.Clamp((int)MathF.Floor(MathF.Max(0f, time) / FrameDuration), 0, count - 1);
    }

    /// <summary>True when a non-looping animation is holding its last frame.</summary>
    public bool IsAtEnd
    {
        get
        {
            int count = FrameSprites.Length;
            if (count == 0 || Loop) return false;
            return Mode == SpriteAnimationMode.Reverse ? _frame <= 0 : _frame >= count - 1;
        }
    }

    public override void OnEnable()
    {
        _renderer = GameObject.GetComponent<SpriteRenderer>();
        if (_renderer.IsNotValid() && AutoCreateRenderer)
            _renderer = GameObject.AddComponent<SpriteRenderer>();

        EnsureBackFace();
        _started = false;
        _time = 0f;
        _direction = 1;
        Rebuild();
        int last = Math.Max(0, _built.Length - 1);
        _frame = Math.Clamp((int)MathF.Round(StartTime * last), 0, last);
        _playing = PlayOnAwake && _built.Length > 0;
        Apply();
    }

    /// <summary>Creates or removes the mirrored back-facing renderer to match <see cref="DoubleSided"/>.</summary>
    public void EnsureBackFace()
    {
        if (!DoubleSided || !AutoCreateBackFace)
        {
            if (_backObject.IsValid())
                _backObject!.Enabled = false;
            return;
        }

        if (_backObject.IsNotValid())
        {
            _backObject = new GameObject($"{GameObject.Name} (Back)");
            _backObject.Transform.SetParent(Transform, false);
            _backObject.Transform.LocalPosition = Float3.Zero;
            _backObject.Transform.LocalEulerAngles = new Float3(0f, 180f, 0f);
            _backRenderer = _backObject.AddComponent<SpriteRenderer>();
        }

        _backObject!.Enabled = true;
        if (_backRenderer.IsNotValid())
            _backRenderer = _backObject.GetComponent<SpriteRenderer>();
    }

    public override void Update()
    {
        Rebuild();

        int count = _built.Length;
        if (count == 0) return;
        if (!_started)
        {
            _started = true;
            if (PlayOnAwake) _playing = true;
            Apply();
        }
        if (!_playing || count == 1 || FramesPerSecond <= 0f) return;

        _time += Time.DeltaTime * FramesPerSecond;
        while (_time >= 1f)
        {
            _time -= 1f;
            Advance(count);
        }
        Apply();
    }

    private void Advance(int count)
    {
        int start = _rangeStart >= 0 ? Math.Clamp(_rangeStart, 0, count - 1) : 0;
        int end = _rangeEnd >= 0 ? Math.Clamp(_rangeEnd, 0, count - 1) : count - 1;
        if (end < start) (start, end) = (end, start);

        int next = _frame + _direction;
        if (next >= start && next <= end)
        {
            _frame = next;
            return;
        }

        switch (Mode)
        {
            case SpriteAnimationMode.PingPong:
                _direction = -_direction;
                _frame = Math.Clamp(_frame + _direction, start, end);
                break;
            case SpriteAnimationMode.Reverse:
                if (Loop) _frame = end;
                else { _playing = false; Completed?.Invoke(this); }
                break;
            default:
                if (Loop) _frame = start;
                else { _playing = false; Completed?.Invoke(this); }
                break;
        }
    }

    private void Apply()
    {
        if (_renderer.IsNotValid())
            _renderer = GameObject.GetComponent<SpriteRenderer>();
        if (_renderer.IsNotValid())
            return;
        if (_frame < 0 || _frame >= _built.Length)
            return;

        Sprite? sprite = _built[_frame];
        if (!ReferenceEquals(_renderer.Sprite, sprite))
            _renderer.Sprite = sprite;

        EnsureBackFace();
        if (_backRenderer.IsValid() && !ReferenceEquals(_backRenderer.Sprite, sprite))
            _backRenderer.Sprite = sprite;

        FrameChanged?.Invoke(_frame);
    }

    /// <summary>Total length of the frame list in seconds at the current rate.</summary>
    public float TotalDuration => FrameCountTotal * FrameDuration;

    /// <summary>Seconds elapsed since the first frame of the current range.</summary>
    public float CurrentTime => _frame * FrameDuration;

    /// <summary>True while a non-looping animation sits on its first frame.</summary>
    public bool IsAtStart => _frame <= 0;

    /// <summary>The first frame of the active range, or 0 when the whole list plays.</summary>
    public int RangeStart => _rangeStart >= 0 ? _rangeStart : 0;

    /// <summary>The last frame of the active range, or the final frame when the whole list plays.</summary>
    public int RangeEnd => _rangeEnd >= 0 ? _rangeEnd : Math.Max(0, FrameCountTotal - 1);

    /// <summary>The sprite that would be shown after a number of seconds of playback.</summary>
    public Sprite? GetSpriteAtTime(float seconds) => GetFrame(TimeToFrame(seconds));

    /// <summary>The sprite at a normalized position, 0 at the first frame and 1 at the last.</summary>
    public Sprite? GetSpriteAtNormalized(float normalized)
    {
        int count = FrameCountTotal;
        if (count == 0) return null;
        return GetFrame((int)MathF.Round(Math.Clamp(normalized, 0f, 1f) * (count - 1)));
    }

    /// <summary>Points the component at a sheet grid without touching the playback state.</summary>
    public void SetSheet(Texture2D? sheet, int columns, int rows, int frameCount = 0, int firstFrame = 0)
    {
        Sheet = sheet;
        Columns = Math.Max(1, columns);
        Rows = Math.Max(1, rows);
        FrameCount = Math.Max(0, frameCount);
        FirstFrame = Math.Max(0, firstFrame);
        Frames = [];
        Rebuild();
        _frame = Math.Clamp(_frame, 0, Math.Max(0, _built.Length - 1));
        Apply();
    }

    /// <summary>Moves a number of frames from the current one, clamped to the frame list.</summary>
    public void StepFrames(int delta) => SetFrame(_frame + delta);

    /// <summary>Starts playing at a normalized position, 0 at the first frame and 1 at the last.</summary>
    public void PlayFromNormalized(float normalized)
    {
        int count = FrameCountTotal;
        if (count == 0) return;
        PlayFrom((int)MathF.Round(Math.Clamp(normalized, 0f, 1f) * (count - 1)));
    }

    /// <summary>Plays a frame range once and holds its last frame.</summary>
    public void PlayRangeOnce(int firstFrame, int lastFrame) => PlayRange(firstFrame, lastFrame, loop: false);

    // Slices the sheet again only when something the cut depends on changed, so a running animation builds its
    // frames once rather than once a frame.
    private void Rebuild()
    {
        Flipbook? book = Flipbook;
        if (book.IsValid() && book.FrameCount > 0)
        {
            if (UseFlipbookSettings)
            {
                FramesPerSecond = book.FramesPerSecond;
                Loop = book.Loop;
                Mode = (SpriteAnimationMode)book.Mode;
                DoubleSided = book.DoubleSided;
            }

            Sprite?[] bookFrames = book.Frames.ToArray();
            if (!ReferenceEquals(_builtBook, book) || !SameFrames(bookFrames))
            {
                _built = bookFrames;
                _builtFrom = bookFrames;
                _builtBook = book;
            }
            return;
        }

        _builtBook = null;
        Sprite?[] frames = Frames ?? [];
        if (frames.Length > 0)
        {
            if (!SameFrames(frames))
            {
                _built = frames;
                _builtFrom = frames;
            }
            return;
        }

        Texture2D? sheet = Sheet;
        if (!sheet.IsValid() || sheet.Width == 0 || sheet.Height == 0)
        {
            _built = [];
            return;
        }

        int columns = Math.Max(1, Columns);
        int rows = Math.Max(1, Rows);
        int total = columns * rows;
        int count = FrameCount > 0 ? Math.Min(FrameCount, total) : total;
        int first = Math.Clamp(FirstFrame, 0, Math.Max(0, total - 1));

        bool same = _built.Length == Math.Max(0, Math.Min(count - first, total - first))
            && ReferenceEquals(_builtSheet, sheet)
            && _builtColumns == columns && _builtRows == rows
            && _builtCount == count && _builtFirst == first
            && _builtPivot.X == Pivot.X && _builtPivot.Y == Pivot.Y
            && _builtPpu == PixelsPerUnit;
        if (same)
            return;

        int cellWidth = (int)(sheet.Width / (uint)columns);
        int cellHeight = (int)(sheet.Height / (uint)rows);
        if (cellWidth <= 0 || cellHeight <= 0)
        {
            _built = [];
            return;
        }

        int last = Math.Min(count, total);
        Sprite?[] built = new Sprite?[Math.Max(0, last - first)];
        for (int i = first; i < last; i++)
        {
            int column = i % columns;
            int row = i / columns;
            // Rect Y counts up from the bottom of the texture, so the top row of the sheet is the last one.
            int x = column * cellWidth;
            int y = (int)sheet.Height - (row + 1) * cellHeight;
            built[i - first] = Sprite.Create(sheet, new SpriteRect(x, y, cellWidth, cellHeight), Pivot, PixelsPerUnit, $"{sheet.Name} [{i}]");
        }

        _built = built;
        _builtSheet = sheet;
        _builtColumns = columns;
        _builtRows = rows;
        _builtCount = count;
        _builtFirst = first;
        _builtPivot = Pivot;
        _builtPpu = PixelsPerUnit;
        _builtFrom = [];
    }

    private bool SameFrames(Sprite?[] frames)
    {
        if (_builtFrom.Length != frames.Length || !ReferenceEquals(_built, _builtFrom))
            return false;

        for (int i = 0; i < frames.Length; i++)
            if (!ReferenceEquals(_builtFrom[i], frames[i]))
                return false;

        return true;
    }
}
