// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace Infinity.DIK;

/// <summary>An icon decoded to RGBA pixels, top row first.</summary>
public sealed record WindowIcon(nint Handle, int Width, int Height, byte[] RgbaPixels);

/// <summary>Icon extraction and PNG writing for <see cref="WindowTools"/>.</summary>
public static partial class WindowTools
{
    /// <summary>Decodes the window icon to RGBA. Returns null when the window has no icon or Windows rejects it.</summary>
    public static WindowIcon? GetIcon(nint hwnd)
    {
        if (!IsSupported) return null;
        nint icon = GetIconHandle(hwnd);
        if (icon == IntPtr.Zero) return null;

        nint colorBitmap = IntPtr.Zero;
        nint maskBitmap = IntPtr.Zero;
        nint dc = IntPtr.Zero;
        try
        {
            if (!GetIconInfo(icon, out IconInfo info)) return null;
            colorBitmap = info.ColorBitmap;
            maskBitmap = info.MaskBitmap;
            if (colorBitmap == IntPtr.Zero) return null;
            if (GetObjectW(colorBitmap, Marshal.SizeOf<Bitmap>(), out Bitmap bitmap) == 0) return null;

            int width = bitmap.Width;
            int height = bitmap.Height;
            if (width <= 0 || height <= 0 || width > 4096 || height > 4096) return null;

            var header = new BitmapInfoHeader
            {
                Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                Width = width,
                Height = -height,
                Planes = 1,
                BitCount = 32,
                Compression = 0,
                SizeImage = (uint)(width * height * 4),
            };
            var bitmapInfo = new BitmapInfo { Header = header };
            byte[] bgra = new byte[width * height * 4];
            dc = GetDC(IntPtr.Zero);
            if (GetDIBits(dc, colorBitmap, 0, (uint)height, bgra, ref bitmapInfo, 0) == 0) return null;

            byte[] rgba = new byte[bgra.Length];
            bool hasAlpha = false;
            for (int i = 0; i < bgra.Length; i += 4)
            {
                if (bgra[i + 3] != 0) hasAlpha = true;
                rgba[i] = bgra[i + 2];
                rgba[i + 1] = bgra[i + 1];
                rgba[i + 2] = bgra[i];
                rgba[i + 3] = bgra[i + 3];
            }
            if (!hasAlpha)
                for (int i = 3; i < rgba.Length; i += 4) rgba[i] = 255;

            return new WindowIcon(icon, width, height, rgba);
        }
        catch { return null; }
        finally
        {
            if (dc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, dc);
            if (colorBitmap != IntPtr.Zero) DeleteObject(colorBitmap);
            if (maskBitmap != IntPtr.Zero) DeleteObject(maskBitmap);
        }
    }

    /// <summary>Decodes the window icon and writes it as a PNG.</summary>
    public static bool TrySaveIconPng(nint hwnd, string path)
    {
        WindowIcon? icon = GetIcon(hwnd);
        if (icon == null) return false;
        try
        {
            string? folder = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            File.WriteAllBytes(path, EncodePng(icon.Width, icon.Height, icon.RgbaPixels));
            return true;
        }
        catch { return false; }
    }

    private static byte[] EncodePng(int width, int height, byte[] rgba)
    {
        using var output = new MemoryStream();
        output.Write([137, 80, 78, 71, 13, 10, 26, 10]);

        byte[] header = new byte[13];
        WriteBigEndian(header, 0, (uint)width);
        WriteBigEndian(header, 4, (uint)height);
        header[8] = 8;
        header[9] = 6;
        WriteChunk(output, "IHDR", header);

        int stride = width * 4;
        byte[] raw = new byte[(stride + 1) * height];
        for (int y = 0; y < height; y++)
        {
            int destination = y * (stride + 1);
            raw[destination] = 0;
            Buffer.BlockCopy(rgba, y * stride, raw, destination + 1, stride);
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            zlib.Write(raw);
        WriteChunk(output, "IDAT", compressed.ToArray());
        WriteChunk(output, "IEND", []);
        return output.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        byte[] typeBytes = Encoding.ASCII.GetBytes(type);
        byte[] length = new byte[4];
        WriteBigEndian(length, 0, (uint)data.Length);
        stream.Write(length);
        stream.Write(typeBytes);
        stream.Write(data);

        uint crc = Crc32(typeBytes, data);
        byte[] crcBytes = new byte[4];
        WriteBigEndian(crcBytes, 0, crc);
        stream.Write(crcBytes);
    }

    private static void WriteBigEndian(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static uint Crc32(byte[] first, byte[] second)
    {
        uint crc = 0xFFFFFFFF;
        crc = UpdateCrc(crc, first);
        crc = UpdateCrc(crc, second);
        return crc ^ 0xFFFFFFFF;
    }

    private static uint UpdateCrc(uint crc, byte[] data)
    {
        foreach (byte value in data)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
        }
        return crc;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IconInfo
    {
        public int IsIcon;
        public int HotspotX;
        public int HotspotY;
        public nint MaskBitmap;
        public nint ColorBitmap;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Bitmap
    {
        public int Type;
        public int Width;
        public int Height;
        public int WidthBytes;
        public ushort Planes;
        public ushort BitsPixel;
        public nint Bits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ColorsUsed;
        public uint ColorsImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Color;
    }

    [DllImport("user32.dll")]
    private static extern bool GetIconInfo(nint icon, out IconInfo info);

    [DllImport("gdi32.dll")]
    private static extern int GetObjectW(nint handle, int size, out Bitmap bitmap);

    [DllImport("user32.dll")]
    private static extern nint GetDC(nint hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint hwnd, nint dc);

    [DllImport("gdi32.dll")]
    private static extern int GetDIBits(nint dc, nint bitmap, uint start, uint lines, byte[] bits,
        ref BitmapInfo info, uint usage);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint handle);
}
