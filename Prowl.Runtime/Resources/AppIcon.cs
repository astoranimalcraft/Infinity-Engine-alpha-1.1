// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;

using Infinity.Aperture;

using Silk.NET.Core;

namespace Infinity.Runtime.Resources;

/// <summary>
/// The icon a window shows in its title bar, its taskbar button and the Alt+Tab list. A project picks a .png or a
/// .ico in Project Settings, every build drops a copy next to the executable as icon.png, and
/// <see cref="Window"/> reads it there when the entry assembly carries no embedded icon.
/// </summary>
public static class AppIcon
{
    /// <summary>The file names searched next to the executable, in the order they are tried.</summary>
    public static readonly string[] FileNames =
        ["icon.png", "icon.ico", "app-icon.png", "app-icon.ico", "Content/icon.png"];

    /// <summary>The largest edge an icon may have, so a poster-sized image is never handed to the platform.</summary>
    public const int MaxEdge = 1024;

    /// <summary>Decodes <paramref name="path"/>, or returns an empty list when it is missing or not an image.</summary>
    public static RawImage[] Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return [];
            using FileStream stream = File.OpenRead(path);
            return Load(stream);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ApertureException or ArgumentException)
        {
            Debug.LogWarning($"Could not read the application icon '{path}': {e.Message}");
            return [];
        }
    }

    /// <summary>
    /// Decodes <paramref name="stream"/> into the images a window takes as its icon: one per icon entry of a .ico,
    /// one for a .png.
    /// </summary>
    public static RawImage[] Load(Stream stream)
    {
        // A .ico holds one image per size and the window picks the closest, so every entry is read rather than only
        // the largest.
        var options = new DecodeOptions { TargetPixelFormat = PixelFormat.Rgba8, MaxFrames = 16, DecodeAllFrames = true };
        using Image image = Image.Load(stream, options);

        List<RawImage> icons = [];
        foreach (ImageFrame frame in image.Frames)
        {
            if (frame.Width <= 0 || frame.Height <= 0) continue;
            if (frame.Width > MaxEdge || frame.Height > MaxEdge) continue;

            int stride = frame.Width * 4;
            var pixels = new byte[stride * frame.Height];
            for (int y = 0; y < frame.Height; y++)
                frame.GetRow(y).CopyTo(pixels.AsSpan(y * stride, stride));
            icons.Add(new RawImage(frame.Width, frame.Height, pixels));
        }
        return [.. icons];
    }

    /// <summary>The first icon found next to the executable, or an empty list when the build shipped none.</summary>
    public static RawImage[] FindNextToExecutable()
    {
        string directory = AppContext.BaseDirectory;
        foreach (string name in FileNames)
        {
            string path = Path.Combine(directory, name);
            if (!File.Exists(path)) continue;

            RawImage[] icons = Load(path);
            if (icons.Length > 0) return icons;
        }
        return [];
    }
}
