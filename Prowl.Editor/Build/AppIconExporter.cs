// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;

using Infinity.Aperture;
using Infinity.Runtime;
using Infinity.Runtime.Resources;

namespace Infinity.Editor.Build;

/// <summary>
/// Turns the project's application icon into the two files a build ships: <c>icon.png</c>, which the runtime window
/// reads from next to the executable, and <c>icon.ico</c>, which a Windows build needs for the file icon the desktop
/// and the taskbar show for the executable itself.
/// </summary>
public static class AppIconExporter
{
    /// <summary>The extensions the project settings accept for an application icon.</summary>
    public static readonly string[] Extensions = ["*.png", "*.ico"];

    // Sizes a Windows icon carries. Explorer picks 16 for the small list, 32 for the desktop, 48 for tiles and 256 for
    // the extra large view; the rest cover the taskbar and the Alt+Tab switcher at common DPI scales.
    private static readonly int[] IcoSizes = [16, 24, 32, 48, 64, 128, 256];

    /// <summary>
    /// Writes <c>icon.png</c> and <c>icon.ico</c> into <paramref name="outputDirectory"/> from
    /// <paramref name="sourcePath"/>. Returns false and sets <paramref name="error"/> when the file cannot be read.
    /// </summary>
    public static bool TryExport(string sourcePath, string outputDirectory, out string error)
    {
        error = "";
        if (!File.Exists(sourcePath))
        {
            error = "The icon file was not found.";
            return false;
        }

        Image image;
        try
        {
            image = Image.Load(sourcePath, new DecodeOptions { TargetPixelFormat = PixelFormat.Rgba8, MaxFrames = 16 });
        }
        catch (Exception e)
        {
            error = e.Message;
            return false;
        }

        using (image)
        {
            ImageFrame? largest = null;
            foreach (ImageFrame frame in image.Frames)
                if (frame.Width > 0 && frame.Height > 0 && (largest == null || frame.Width * frame.Height > largest.Width * largest.Height))
                    largest = frame;

            if (largest == null)
            {
                error = "The file holds no image.";
                return false;
            }

            byte[] rgba = CopyPixels(largest);
            Directory.CreateDirectory(outputDirectory);

            if (!WritePng(Path.Combine(outputDirectory, "icon.png"), rgba, largest.Width, largest.Height))
            {
                error = "The PNG could not be written.";
                return false;
            }

            WriteIco(Path.Combine(outputDirectory, "icon.ico"), rgba, largest.Width, largest.Height);
            return true;
        }
    }

    /// <summary>Shows <paramref name="sourcePath"/> as the editor's own window icon, which is the live preview.</summary>
    public static void ApplyToEditor(string sourcePath) => Window.SetWindowIcon(AppIcon.Load(sourcePath));

    /// <summary>The size of the icon's largest image, or null when the file cannot be read.</summary>
    public static (int Width, int Height)? SizeOf(string sourcePath)
    {
        try
        {
            if (!File.Exists(sourcePath)) return null;
            using Image image = Image.Load(sourcePath, new DecodeOptions { MaxFrames = 16 });
            (int Width, int Height)? best = null;
            foreach (ImageFrame frame in image.Frames)
                if (best == null || frame.Width * frame.Height > best.Value.Width * best.Value.Height)
                    best = (frame.Width, frame.Height);
            return best;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static byte[] CopyPixels(ImageFrame frame)
    {
        int stride = frame.Width * 4;
        var pixels = new byte[stride * frame.Height];
        for (int y = 0; y < frame.Height; y++)
            frame.GetRow(y).CopyTo(pixels.AsSpan(y * stride, stride));
        return pixels;
    }

    private static bool WritePng(string path, byte[] rgba, int width, int height)
    {
        using Image image = Image.FromPixels(rgba, width, height, PixelFormat.Rgba8);
        return image.TrySave(path, new EncodeOptions { Format = ImageFormat.Png }, out _);
    }

    // An ICO is a directory of entries, and Windows accepts PNG data in each entry, so the sizes are downscaled and
    // re-encoded rather than written as BMPs.
    private static void WriteIco(string path, byte[] rgba, int width, int height)
    {
        List<(int Size, byte[] Png)> entries = [];
        foreach (int size in IcoSizes)
        {
            byte[] scaled = size == width && size == height ? rgba : Resize(rgba, width, height, size, size);

            using Image image = Image.FromPixels(scaled, size, size, PixelFormat.Rgba8);
            using var buffer = new MemoryStream();
            if (!image.TrySave(buffer, new EncodeOptions { Format = ImageFormat.Png }, out _)) continue;
            entries.Add((size, buffer.ToArray()));
        }

        if (entries.Count == 0) return;

        using FileStream file = File.Create(path);
        using var writer = new BinaryWriter(file);

        writer.Write((ushort)0);              // reserved
        writer.Write((ushort)1);              // 1 = icon
        writer.Write((ushort)entries.Count);

        int offset = 6 + entries.Count * 16;
        foreach ((int size, byte[] png) in entries)
        {
            writer.Write((byte)(size >= 256 ? 0 : size));   // 0 means 256
            writer.Write((byte)(size >= 256 ? 0 : size));
            writer.Write((byte)0);            // palette entries
            writer.Write((byte)0);            // reserved
            writer.Write((ushort)1);          // colour planes
            writer.Write((ushort)32);         // bits per pixel
            writer.Write(png.Length);
            writer.Write(offset);
            offset += png.Length;
        }

        foreach ((_, byte[] png) in entries) writer.Write(png);
    }

    /// <summary>Bilinear resample, which is all an icon needs and costs nothing to keep here.</summary>
    private static byte[] Resize(byte[] source, int sourceWidth, int sourceHeight, int width, int height)
    {
        var result = new byte[width * height * 4];

        for (int y = 0; y < height; y++)
        {
            float sy = (y + 0.5f) * sourceHeight / height - 0.5f;
            int y0 = Math.Clamp((int)MathF.Floor(sy), 0, sourceHeight - 1);
            int y1 = Math.Min(y0 + 1, sourceHeight - 1);
            float fy = Math.Clamp(sy - MathF.Floor(sy), 0f, 1f);

            for (int x = 0; x < width; x++)
            {
                float sx = (x + 0.5f) * sourceWidth / width - 0.5f;
                int x0 = Math.Clamp((int)MathF.Floor(sx), 0, sourceWidth - 1);
                int x1 = Math.Min(x0 + 1, sourceWidth - 1);
                float fx = Math.Clamp(sx - MathF.Floor(sx), 0f, 1f);

                for (int channel = 0; channel < 4; channel++)
                {
                    float top = Lerp(source[(y0 * sourceWidth + x0) * 4 + channel], source[(y0 * sourceWidth + x1) * 4 + channel], fx);
                    float bottom = Lerp(source[(y1 * sourceWidth + x0) * 4 + channel], source[(y1 * sourceWidth + x1) * 4 + channel], fx);
                    result[(y * width + x) * 4 + channel] = (byte)Math.Clamp(MathF.Round(Lerp(top, bottom, fy)), 0f, 255f);
                }
            }
        }

        return result;
    }

    private static float Lerp(float from, float to, float t) => from + (to - from) * t;
}
