// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.IO;

using Infinity.Aperture;
using Infinity.Editor.Build;
using Infinity.Runtime.Resources;

using Silk.NET.Core;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>The application icon: reading a .png or .ico for the window, and writing the two files a build ships.</summary>
public sealed class AppIconTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("infinity-icon-test-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_directory, true); }
        catch (IOException) { }
    }

    private string WritePng(string name, int width, int height, byte red, byte green, byte blue, byte alpha)
    {
        var pixels = new byte[width * height * 4];
        for (int i = 0; i < pixels.Length; i += 4)
        {
            pixels[i] = red;
            pixels[i + 1] = green;
            pixels[i + 2] = blue;
            pixels[i + 3] = alpha;
        }

        string path = Path.Combine(_directory, name);
        using Image image = Image.FromPixels(pixels, width, height, PixelFormat.Rgba8);
        Assert.True(image.TrySave(path, new EncodeOptions { Format = ImageFormat.Png }, out ApertureError error), error.ToString());
        return path;
    }

    [Fact]
    public void LoadReadsAPngAsRawRgba()
    {
        string path = WritePng("icon.png", 32, 24, 10, 20, 30, 255);

        RawImage icon = Assert.Single(AppIcon.Load(path));

        Assert.Equal(32, icon.Width);
        Assert.Equal(24, icon.Height);
        Assert.Equal(10, icon.Pixels.Span[0]);
        Assert.Equal(20, icon.Pixels.Span[1]);
        Assert.Equal(30, icon.Pixels.Span[2]);
        Assert.Equal(255, icon.Pixels.Span[3]);
    }

    [Fact]
    public void LoadOfAMissingFileIsEmpty() => Assert.Empty(AppIcon.Load(Path.Combine(_directory, "missing.png")));

    [Fact]
    public void ExportWritesAPngAndAnIcoOfEverySize()
    {
        string source = WritePng("source.png", 128, 128, 200, 100, 50, 255);
        string output = Path.Combine(_directory, "Builds");

        Assert.True(AppIconExporter.TryExport(source, output, out string error), error);

        Assert.True(File.Exists(Path.Combine(output, "icon.png")));
        Assert.True(File.Exists(Path.Combine(output, "icon.ico")));

        RawImage[] sizes = AppIcon.Load(Path.Combine(output, "icon.ico"));
        Assert.Equal(7, sizes.Length);
        Assert.Contains(sizes, icon => icon.Width == 16);
        Assert.Contains(sizes, icon => icon.Width == 256);
    }

    [Fact]
    public void ExportKeepsTheColourOfTheSource()
    {
        string source = WritePng("colour.png", 64, 64, 12, 200, 90, 255);
        string output = Path.Combine(_directory, "Builds");

        Assert.True(AppIconExporter.TryExport(source, output, out string error), error);

        RawImage exported = Assert.Single(AppIcon.Load(Path.Combine(output, "icon.png")));
        Assert.Equal(12, exported.Pixels.Span[0]);
        Assert.Equal(200, exported.Pixels.Span[1]);
        Assert.Equal(90, exported.Pixels.Span[2]);
    }

    [Fact]
    public void ExportAcceptsAnIcoAsTheSource()
    {
        string png = WritePng("first.png", 96, 96, 255, 255, 255, 255);
        string firstOutput = Path.Combine(_directory, "First");
        Assert.True(AppIconExporter.TryExport(png, firstOutput, out _));

        string secondOutput = Path.Combine(_directory, "Second");
        Assert.True(AppIconExporter.TryExport(Path.Combine(firstOutput, "icon.ico"), secondOutput, out string error), error);

        RawImage icon = Assert.Single(AppIcon.Load(Path.Combine(secondOutput, "icon.png")));
        Assert.Equal(256, icon.Width);
        Assert.Equal(256, icon.Height);
    }

    [Fact]
    public void ExportReportsAMissingSource()
    {
        Assert.False(AppIconExporter.TryExport(Path.Combine(_directory, "nope.png"), Path.Combine(_directory, "Out"), out string error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void SizeOfReportsTheLargestImage()
    {
        string source = WritePng("size.png", 48, 40, 0, 0, 0, 255);

        Assert.Equal((48, 40), AppIconExporter.SizeOf(source));
        Assert.Null(AppIconExporter.SizeOf(Path.Combine(_directory, "missing.png")));
    }
}
