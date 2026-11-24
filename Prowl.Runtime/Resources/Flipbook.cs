// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

namespace Infinity.Runtime.Resources;

/// <summary>How a flipbook walks its frames.</summary>
public enum FlipbookMode : byte
{
    Forward,
    PingPong,
    Reverse,
}

/// <summary>
/// A frame-by-frame 2D animation, the equivalent of an Unreal flipbook. Dropping one into the scene creates a
/// SpriteRenderer with a <see cref="SpriteAnimation"/> that plays it, optionally on both sides of the object.
/// </summary>
[CreateAssetMenu("2D/Flipbook", Extension = ".flipbook", Order = 620)]
public sealed class Flipbook : Asset
{
    /// <summary>Frames in play order. Each frame is a slice of a texture or a whole sprite.</summary>
    public List<Sprite> Frames = [];

    /// <summary>Playback rate in frames per second.</summary>
    public float FramesPerSecond = 12f;

    /// <summary>Whether playback wraps at the end.</summary>
    public bool Loop = true;

    /// <summary>Forward, ping-pong, or reverse.</summary>
    public FlipbookMode Mode = FlipbookMode.Forward;

    /// <summary>Show the same frame mirrored on the back of the object.</summary>
    public bool DoubleSided = true;

    /// <summary>Seconds a single frame is shown.</summary>
    public float FrameDuration => FramesPerSecond > 0.0001f ? 1f / FramesPerSecond : 0f;

    public int FrameCount => Frames.Count;

    public float Duration => MathF.Max(0f, FrameCount * FrameDuration);

    public Flipbook() : base("Flipbook") { }

    public Flipbook(string name) : base(name) { }

    public Sprite? GetFrame(int index)
    {
        if (Frames.Count == 0) return null;
        return Frames[Math.Clamp(index, 0, Frames.Count - 1)];
    }

    /// <summary>Maps elapsed seconds to a frame, respecting the mode and looping.</summary>
    public int FrameAt(float time)
    {
        int count = Frames.Count;
        if (count <= 1) return 0;

        float raw = time * FramesPerSecond;
        if (!Loop) return Math.Clamp((int)MathF.Floor(raw), 0, count - 1);

        switch (Mode)
        {
            case FlipbookMode.PingPong:
            {
                float span = count * 2f - 2f;
                float phase = PositiveModulo(raw, span);
                return phase <= count - 1 ? (int)MathF.Floor(phase) : count - 1 - (int)MathF.Floor(phase - (count - 1));
            }
            case FlipbookMode.Reverse:
            {
                int index = (int)MathF.Floor(PositiveModulo(-raw, count));
                return count - 1 - index;
            }
            default:
                return (int)MathF.Floor(PositiveModulo(raw, count));
        }
    }

    private static float PositiveModulo(float value, float modulus)
    {
        if (modulus <= 0f) return 0f;
        float result = value % modulus;
        return result < 0f ? result + modulus : result;
    }

    public void Add(Sprite sprite)
    {
        if (sprite.IsValid()) Frames.Add(sprite);
    }

    public void RemoveAt(int index)
    {
        if (index >= 0 && index < Frames.Count) Frames.RemoveAt(index);
    }

    public void Move(int from, int to)
    {
        if (from < 0 || from >= Frames.Count || to < 0 || to >= Frames.Count || from == to) return;
        Sprite sprite = Frames[from];
        Frames.RemoveAt(from);
        Frames.Insert(to, sprite);
    }

    public void Reverse() => Frames.Reverse();

    public void Duplicate(int index)
    {
        if (index < 0 || index >= Frames.Count) return;
        Frames.Insert(index + 1, Frames[index]);
    }

    public void Clear() => Frames.Clear();

    public void SortByName() => Frames.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Seconds at which a frame starts.</summary>
    public float TimeAtFrame(int index) => Math.Clamp(index, 0, Math.Max(0, FrameCount - 1)) * FrameDuration;

    /// <summary>The frame at a normalized position, 0 at the first frame and 1 at the last.</summary>
    public int FrameAtNormalized(float normalized)
        => FrameCount == 0 ? 0 : Math.Clamp((int)MathF.Round(Math.Clamp(normalized, 0f, 1f) * (FrameCount - 1)), 0, FrameCount - 1);

    /// <summary>The frame that would be shown after a number of seconds.</summary>
    public Sprite? GetFrameAtTime(float seconds) => GetFrame(FrameAt(seconds));

    /// <summary>The frame at a normalized position.</summary>
    public Sprite? GetFrameAtNormalized(float normalized) => GetFrame(FrameAtNormalized(normalized));

    /// <summary>Sets the playback rate, in frames per second.</summary>
    public void SetFramesPerSecond(float framesPerSecond) => FramesPerSecond = MathF.Max(0.01f, framesPerSecond);

    /// <summary>Sets whether playback wraps at the end.</summary>
    public void SetLoop(bool loop) => Loop = loop;

    /// <summary>Sets forward, ping-pong or reverse playback.</summary>
    public void SetMode(FlipbookMode mode) => Mode = mode;

    /// <summary>Appends every valid sprite in a sequence.</summary>
    public void AddRange(IEnumerable<Sprite> sprites)
    {
        foreach (Sprite sprite in sprites)
            if (sprite.IsValid()) Frames.Add(sprite);
    }

    /// <summary>Inserts a sprite at an index, clamped to the list.</summary>
    public void Insert(int index, Sprite sprite)
    {
        if (sprite.IsNotValid()) return;
        Frames.Insert(Math.Clamp(index, 0, Frames.Count), sprite);
    }

    /// <summary>Replaces a frame, keeping the list length.</summary>
    public void ReplaceAt(int index, Sprite sprite)
    {
        if (sprite.IsNotValid() || index < 0 || index >= Frames.Count) return;
        Frames[index] = sprite;
    }

    /// <summary>The index of a frame, or -1.</summary>
    public int IndexOf(Sprite sprite) => Frames.IndexOf(sprite);

    /// <summary>Builds a flipbook from a texture by slicing it into a grid.</summary>
    public static Flipbook FromSpriteSheet(Texture2D texture, int columns, int rows, int startFrame = 0, int frameCount = -1, string name = "Flipbook")
    {
        var flipbook = new Flipbook(name);
        if (texture.IsNotValid() || columns <= 0 || rows <= 0) return flipbook;

        int width = (int)texture.Width / columns;
        int height = (int)texture.Height / rows;
        if (width <= 0 || height <= 0) return flipbook;

        int total = columns * rows;
        int count = frameCount < 0 ? total - startFrame : Math.Min(frameCount, total - startFrame);
        for (int i = 0; i < count; i++)
        {
            int index = startFrame + i;
            int column = index % columns;
            int row = index / columns;
            // Sprite rects count up from the bottom of the texture, so row zero is the top row of the sheet.
            int y = (int)texture.Height - (row + 1) * height;
            flipbook.Frames.Add(new Sprite
            {
                Name = $"{name}_{index}",
                Texture = texture,
                Rect = new SpriteRect(column * width, y, width, height),
            });
        }
        return flipbook;
    }

    /// <summary>Builds a flipbook from whole sprites.</summary>
    public static Flipbook FromSprites(IEnumerable<Sprite> sprites, string name = "Flipbook")
    {
        var flipbook = new Flipbook(name);
        foreach (Sprite sprite in sprites)
            if (sprite.IsValid()) flipbook.Frames.Add(sprite);
        return flipbook;
    }
}
