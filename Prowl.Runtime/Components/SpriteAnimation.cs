// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime;

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

    /// <summary>Raised after the frame changes, with its index.</summary>
    public event Action<int>? FrameChanged;

    [System.NonSerialized, NotHeld] private Sprite?[] _built = [];
    [System.NonSerialized, NotHeld] private SpriteRenderer? _renderer;
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

    /// <summary>Starts playing from the current frame.</summary>
    public void Play()
    {
        _playing = true;
        _started = true;
        Apply();
    }

    /// <summary>Starts playing from a frame, clamped into range.</summary>
    public void PlayFrom(int frame)
    {
        int count = FrameSprites.Length;
        if (count == 0) return;

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

    public override void OnEnable()
    {
        _renderer = GameObject.GetComponent<SpriteRenderer>();
        if (_renderer.IsNotValid() && AutoCreateRenderer)
            _renderer = GameObject.AddComponent<SpriteRenderer>();

        _started = false;
        _time = 0f;
        _direction = 1;
        Rebuild();
        int last = Math.Max(0, _built.Length - 1);
        _frame = Math.Clamp((int)MathF.Round(StartTime * last), 0, last);
        _playing = PlayOnAwake && _built.Length > 0;
        Apply();
    }

    public override void Update()
    {
        Rebuild();

        int count = _built.Length;
        if (count == 0) return;
        if (!_started)
        {
            _started = true;
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
        int next = _frame + _direction;
        if (next >= 0 && next < count)
        {
            _frame = next;
            return;
        }

        switch (Mode)
        {
            case SpriteAnimationMode.PingPong:
                _direction = -_direction;
                _frame = Math.Clamp(_frame + _direction, 0, count - 1);
                break;
            case SpriteAnimationMode.Reverse:
                if (Loop) _frame = count - 1;
                else _playing = false;
                break;
            default:
                if (Loop) _frame = 0;
                else _playing = false;
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
        FrameChanged?.Invoke(_frame);
    }

    // Slices the sheet again only when something the cut depends on changed, so a running animation builds its
    // frames once rather than once a frame.
    private void Rebuild()
    {
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
