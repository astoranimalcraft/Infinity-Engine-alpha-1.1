// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.IO;
using System.Linq;
using System.Reflection;

using Infinity.OrigamiUI;
using Infinity.Quill;
using Infinity.Runtime.Resources;
using Infinity.Vector;

using Color = System.Drawing.Color;

namespace Infinity.Editor.GUI;

/// <summary>An icon drawn from a PNG embedded in the editor assembly, tinted like the SVG icons around it.</summary>
public sealed class EditorImageIcon : IOrigamiIcon
{
    private readonly string _resourceSuffix;
    private Texture2D? _texture;
    private bool _failed;

    public EditorImageIcon(string resourceSuffix) => _resourceSuffix = resourceSuffix;

    public void Draw(Canvas canvas, Rect rect, Color color, float strokeWidth = 1.5f)
    {
        Texture2D? texture = Load();
        if (ReferenceEquals(texture, null)) return;

        canvas.DrawImage(texture, rect.Min.X, rect.Min.Y, rect.Size.X, rect.Size.Y,
            Color32.FromArgb(color.A, color.R, color.G, color.B));
    }

    private Texture2D? Load()
    {
        if (!ReferenceEquals(_texture, null) || _failed) return _texture;

        try
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            string? name = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(_resourceSuffix, StringComparison.Ordinal));
            if (name == null) { _failed = true; return null; }

            using Stream? stream = assembly.GetManifestResourceStream(name);
            if (stream == null) { _failed = true; return null; }
            _texture = Texture2D.FromStream(stream);
            return _texture;
        }
        catch (Exception e)
        {
            _failed = true;
            Runtime.Debug.LogWarning($"Could not load the editor icon '{_resourceSuffix}': {e.Message}");
            return null;
        }
    }
}
