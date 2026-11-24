// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Infinity.Runtime.Resources;
using Infinity.Vector;

namespace Infinity.Runtime;

/// <summary>The silhouette a <see cref="Primitive2D"/> builds. Every shape lives in the XY plane.</summary>
public enum Primitive2DShape
{
    Rectangle,
    Square,
    Circle,
    Ellipse,
    Triangle,
    Capsule,
    Hexagon,
}

/// <summary>
/// A flat, colored 2D primitive rendered by a <see cref="SpriteRenderer"/>. It writes its own
/// triangle fan into a <see cref="Infinity.Runtime.Resources.Sprite"/> that samples a one pixel
/// white texture, so circles and capsules are real silhouettes rather than stretched quads.
/// </summary>
[ExecuteAlways]
[AddComponentMenu("2D/Primitive 2D")]
[ComponentIcon("")]
public sealed class Primitive2D : Component
{
    public Primitive2DShape Shape = Primitive2DShape.Rectangle;
    public Float2 Size = new(1f, 1f);
    public Color Tint = Color.White;
    public int Segments = 32;
    public int SortingOrder;

    private SpriteRenderer? _renderer;
    private Sprite? _sprite;
    private Primitive2DShape _builtShape;
    private Float2 _builtSize;
    private int _builtSegments;

    private static Texture2D? s_white;

    private static Texture2D White
    {
        get
        {
            if (s_white.IsValid()) return s_white!;
            s_white = new Texture2D(1, 1);
            s_white.SetData<Color32>(new Color32[] { new Color32(255, 255, 255, 255) });
            s_white.Name = "Primitive2D White";
            return s_white;
        }
    }

    public override void OnEnable() => EnsureRenderer();

    public override void Start() => EnsureRenderer();

    public override void Update()
    {
        EnsureRenderer();
        Apply();
    }

    /// <summary>Forces the silhouette to be rebuilt on the next frame.</summary>
    public void Rebuild()
    {
        _builtShape = (Primitive2DShape)(-1);
        _builtSegments = -1;
        Apply();
    }

    private void EnsureRenderer()
    {
        if (_renderer.IsNotValid())
        {
            _renderer = GetComponent<SpriteRenderer>();
            if (_renderer.IsNotValid()) _renderer = GameObject.AddComponent<SpriteRenderer>();
        }
        if (_renderer.IsNotValid()) return;
        _renderer.Billboard = true;
        _renderer.Color = Tint;
        _renderer.SortingOrder = SortingOrder;
    }

    private void Apply()
    {
        if (_renderer.IsNotValid()) return;

        if (_sprite.IsNotValid() || _builtShape != Shape || _builtSize != Size || _builtSegments != Segments)
            Build();

        if (_sprite.IsValid() && _renderer.Sprite != _sprite)
            _renderer.Sprite = _sprite;

        _renderer.Color = Tint;
        _renderer.SortingOrder = SortingOrder;
    }

    private void Build()
    {
        Float2 size = Shape switch
        {
            Primitive2DShape.Square => new Float2(MathF.Min(Size.X, Size.Y), MathF.Min(Size.X, Size.Y)),
            _ => new Float2(MathF.Max(0.0001f, Size.X), MathF.Max(0.0001f, Size.Y)),
        };

        List<Float2> outline = BuildOutline(size);
        var verts = new Float2[outline.Count + 1];
        var uvs = new Float2[verts.Length];
        var indices = new ushort[outline.Count * 3];

        verts[0] = Float2.Zero;
        uvs[0] = new Float2(0.5f, 0.5f);
        for (int i = 0; i < outline.Count; i++)
        {
            verts[i + 1] = outline[i];
            uvs[i + 1] = new Float2(0.5f, 0.5f);
        }

        for (int i = 0; i < outline.Count; i++)
        {
            indices[i * 3] = 0;
            indices[i * 3 + 1] = (ushort)(i + 1);
            indices[i * 3 + 2] = (ushort)((i + 1) % outline.Count + 1);
        }

        if (_sprite.IsNotValid()) _sprite = new Sprite { Name = "Primitive2D" };
        _sprite.Texture = White;
        _sprite.Rect = new SpriteRect(0, 0, 1, 1);
        _sprite.PixelsPerUnit = 1f;
        _sprite.Pivot = new Float2(0.5f, 0.5f);
        _sprite.Vertices = verts;
        _sprite.UV = uvs;
        _sprite.Indices = indices;

        _builtShape = Shape;
        _builtSize = Size;
        _builtSegments = Segments;
    }

    private List<Float2> BuildOutline(Float2 size)
    {
        float w = size.X, h = size.Y;
        switch (Shape)
        {
            case Primitive2DShape.Triangle:
                return [new Float2(0f, h * 0.5f), new Float2(w * 0.5f, -h * 0.5f), new Float2(-w * 0.5f, -h * 0.5f)];

            case Primitive2DShape.Circle:
            case Primitive2DShape.Ellipse:
            case Primitive2DShape.Hexagon:
            {
                int count = Shape == Primitive2DShape.Hexagon ? 6 : Math.Max(8, Segments);
                var points = new List<Float2>(count);
                for (int i = 0; i < count; i++)
                {
                    float angle = MathF.Tau * i / count + MathF.PI / 2f;
                    points.Add(new Float2(MathF.Cos(angle) * w * 0.5f, MathF.Sin(angle) * h * 0.5f));
                }
                return points;
            }

            case Primitive2DShape.Capsule:
                return RoundedRect(w, h, MathF.Min(w, h) * 0.5f);

            default:
                return [new Float2(-w * 0.5f, -h * 0.5f), new Float2(w * 0.5f, -h * 0.5f),
                        new Float2(w * 0.5f, h * 0.5f), new Float2(-w * 0.5f, h * 0.5f)];
        }
    }

    private List<Float2> RoundedRect(float w, float h, float radius)
    {
        radius = MathF.Min(radius, MathF.Min(w, h) * 0.5f);
        int steps = Math.Max(4, Segments / 4);
        var points = new List<Float2>(steps * 4);

        void Arc(float cx, float cy, float from, float to)
        {
            for (int i = 0; i <= steps; i++)
            {
                float a = from + (to - from) * i / steps;
                points.Add(new Float2(cx + MathF.Cos(a) * radius, cy + MathF.Sin(a) * radius));
            }
        }

        float hw = w * 0.5f, hh = h * 0.5f;
        Arc(hw - radius, hh - radius, 0f, MathF.PI * 0.5f);
        Arc(-hw + radius, hh - radius, MathF.PI * 0.5f, MathF.PI);
        Arc(-hw + radius, -hh + radius, MathF.PI, MathF.PI * 1.5f);
        Arc(hw - radius, -hh + radius, MathF.PI * 1.5f, MathF.PI * 2f);
        return points;
    }
}
