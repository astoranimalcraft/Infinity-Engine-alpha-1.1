// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;

using Infinity.Aperture;
using Infinity.Editor.Core;
using Infinity.Editor.GUI;
using Infinity.Editor.Theming;
using Infinity.Editor.Utils;
using Infinity.OrigamiUI;
using Infinity.PaperUI;
using Infinity.PaperUI.LayoutEngine;
using Infinity.Rosetta;
using Infinity.Runtime;
using Infinity.Quill;
using Infinity.Runtime.Resources;
using Infinity.Vector;
using Infinity.Vector.Geometry;

namespace Infinity.Editor.GUI.ApiDocs;

/// <summary>
/// The picture and the short animation that explain an API entry. Every entry shares its category's media,
/// so a type, a method and a property all get a visual without shipping thousands of near-identical files.
/// </summary>
public static class ApiDocsMedia
{
    private sealed class Media
    {
        public Texture2D? Still;
        public readonly List<Texture2D> Frames = [];
        public readonly List<double> Delays = [];
        public double Duration;
        public long StartedAt;
    }

    private static readonly Dictionary<string, Media> s_cache = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, bool> s_failed = new(StringComparer.Ordinal);

    /// <summary>The media key for an entry, one of the category names in lower case.</summary>
    public static string KeyFor(ApiDocEntry entry) => entry.Category switch
    {
        ApiDocCategory.Api => "api",
        ApiDocCategory.Function => "function",
        ApiDocCategory.Method => "method",
        ApiDocCategory.Variable => "variable",
        ApiDocCategory.Property => "property",
        ApiDocCategory.Event => "event",
        ApiDocCategory.Type => "type",
        ApiDocCategory.Component => "component",
        ApiDocCategory.Enum => "enum",
        ApiDocCategory.Constructor => "constructor",
        ApiDocCategory.Services => "services",
        ApiDocCategory.Dik => "dik",
        _ => "all",
    };

    /// <summary>Draws the static print and the animated GIF inline, without opening another application.</summary>
    public static void Draw(Paper paper, ApiDocEntry entry)
    {
        string key = KeyFor(entry);
        Media? media = Get(key);
        if (media == null)
        {
            paper.Box("api_d_media_missing").Width(UnitValue.Stretch()).Height(30)
                .Text(Loc.Get("apidocs.media_missing"), EditorTheme.DefaultFont!)
                .TextColor(EditorTheme.Ink300).FontSize(EditorTheme.FontSizeSmall).IsNotInteractable();
            return;
        }

        using (paper.Row("api_d_media_preview_row").Width(UnitValue.Stretch()).Height(172).Gap(8).Enter())
        {
            DrawPanel(paper, "api_d_media_png", key, Loc.Get("apidocs.media_print"), media.Still, animated: false);
            DrawPanel(paper, "api_d_media_gif", key, Loc.Get("apidocs.media_gif"), media.Frames.Count > 0 ? media.Frames[0] : null, animated: true);
        }
    }

    private static void DrawPanel(Paper paper, string id, string mediaKey, string label, Texture2D? preview, bool animated)
    {
        using (paper.Column(id + "_col").Width(UnitValue.Stretch()).Height(UnitValue.Stretch())
            .Padding(8, 8, 6, 6).Gap(4)
            .BackgroundColor(EditorTheme.WithAlpha(EditorTheme.Neutral100, 180))
            .BorderColor(EditorTheme.BorderSoft).BorderWidth(1).Rounded(6).Enter())
        {
            paper.Box(id + "_label").Width(UnitValue.Stretch()).Height(20)
                .Text(label, EditorTheme.DefaultFont!).TextColor(EditorTheme.Ink300)
                .FontSize(EditorTheme.FontSizeSmall - 1).Alignment(TextAlignment.MiddleLeft).IsNotInteractable();

            using (paper.Box(id + "_frame").Width(UnitValue.Stretch()).Height(UnitValue.Stretch())
                .Rounded(4).Clip().Enter())
            {
                if (animated)
                    paper.Draw((canvas, rect) => DrawAnimated(canvas, rect, mediaKey));
                else if (preview.IsValid())
                    paper.Draw((canvas, rect) => DrawFitted(canvas, rect, preview!));
            }
        }
    }

    private static void DrawAnimated(Canvas canvas, Rect rect, string key)
    {
        Media? media = Get(key);
        if (media == null || media.Frames.Count == 0) return;

        double elapsed = (Environment.TickCount64 - media.StartedAt) / 1000.0;
        if (media.Duration > 0.0001) elapsed %= media.Duration;

        int index = 0;
        double acc = 0;
        for (int i = 0; i < media.Frames.Count; i++)
        {
            acc += media.Delays[i];
            if (elapsed <= acc)
            {
                index = i;
                break;
            }
            index = i;
        }

        DrawFitted(canvas, rect, media.Frames[index]);
    }

    private static void DrawFitted(Canvas canvas, Rect rect, Texture2D texture)
    {
        if (texture.Width == 0 || texture.Height == 0) return;
        float availW = (float)rect.Size.X, availH = (float)rect.Size.Y;
        float scale = MathF.Min(availW / texture.Width, availH / texture.Height);
        float w = texture.Width * scale, h = texture.Height * scale;
        float x = (float)rect.Min.X + (availW - w) * 0.5f;
        float y = (float)rect.Min.Y + (availH - h) * 0.5f;
        canvas.DrawImage(texture, x, y, w, h);
    }

    private static Media? Get(string key)
    {
        if (s_cache.TryGetValue(key, out Media? cached)) return cached;
        if (s_failed.ContainsKey(key)) return null;

        try
        {
            var media = new Media();
            using (Stream? still = EditorApplication.GetEmbeddedResource("ApiDocs/" + key + ".png"))
                media.Still = still == null ? null : Decode(still, allFrames: false, media);

            using (Stream? gif = EditorApplication.GetEmbeddedResource("ApiDocs/" + key + ".gif"))
                Decode(gif, allFrames: true, media);

            media.StartedAt = Environment.TickCount64;
            s_cache[key] = media;
            if (media.Still.IsNotValid() && media.Frames.Count == 0)
            {
                s_cache.Remove(key);
                s_failed[key] = true;
                return null;
            }
            return media;
        }
        catch (Exception e)
        {
            s_failed[key] = true;
            Runtime.Debug.LogWarning($"API media '{key}' could not be decoded: {e.Message}");
            return null;
        }
    }

    private static Texture2D? Decode(Stream? stream, bool allFrames, Media media)
    {
        if (stream == null) return null;
        using Image image = Image.Load(stream, new DecodeOptions
        {
            TargetPixelFormat = PixelFormat.Rgba8,
            DecodeAllFrames = allFrames,
            MaxFrames = allFrames ? 128 : 1,
        });

        Texture2D? first = null;
        for (int i = 0; i < image.Frames.Count; i++)
        {
            var frame = image.Frames[i];
            var texture = new Texture2D((uint)image.Width, (uint)image.Height);
            texture.SetData(frame.Pixels.ToArray(), 0, 0, (uint)image.Width, (uint)image.Height);
            if (first.IsNotValid()) first = texture;
            if (allFrames)
            {
                media.Frames.Add(texture);
                double delay = frame.Delay.TotalSeconds;
                if (delay <= 0.001) delay = 0.1;
                media.Delays.Add(delay);
                media.Duration += delay;
            }
        }
        return first;
    }

    /// <summary>Extracts the entry's PNG or GIF and opens it in the system viewer. Kept for context menus.</summary>
    public static void Open(ApiDocEntry entry, bool animated)
    {
        string key = KeyFor(entry);
        string extension = animated ? ".gif" : ".png";
        string resource = "ApiDocs/" + key + extension;
        try
        {
            using Stream? stream = EditorApplication.GetEmbeddedResource(resource);
            if (stream == null)
            {
                Toasts.Warning("Preview missing", $"The {key}{extension} preview is not in this build.");
                return;
            }

            string folder = Path.Combine(Path.GetTempPath(), "InfinityEngine", "ApiDocs");
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, key + extension);
            using (FileStream file = File.Create(path))
                stream.CopyTo(file);

            ExternalTools.OpenWithSystem(path);
        }
        catch (Exception e)
        {
            Toasts.Error("Could not open preview", e.Message);
        }
    }
}
