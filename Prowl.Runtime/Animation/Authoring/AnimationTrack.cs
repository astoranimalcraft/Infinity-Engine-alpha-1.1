// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Reflection;

using Infinity.Echo;
using Infinity.Runtime.Resources;
using Infinity.Runtime.UI;
using Infinity.Vector;

namespace Infinity.Runtime.Animation;

/// <summary>What a track drives.</summary>
public enum AnimationTrackKind
{
    /// <summary>Local position of the target transform.</summary>
    TransformPosition,
    /// <summary>Local rotation of the target transform, in Euler degrees.</summary>
    TransformRotation,
    /// <summary>Local scale of the target transform.</summary>
    TransformScale,
    /// <summary>The sprite a renderer on the target shows.</summary>
    SpriteFrame,
    /// <summary>Tint of the target's renderer or UI graphic, alpha included, which is what fades things out.</summary>
    TintColor,
    /// <summary>A float or colour uniform on the material the target draws with.</summary>
    MaterialProperty,
    /// <summary>A texture map on that material, swapped at its keys.</summary>
    MaterialTexture,
    /// <summary>A numeric property of a component on the target, addressed by type name and member name.</summary>
    ComponentProperty,
}

/// <summary>A sprite shown from a given time on, until the next sprite key.</summary>
[Serializable]
public sealed class SpriteKey
{
    public float Time;
    public Sprite? Sprite;

    public SpriteKey() { }

    public SpriteKey(float time, Sprite? sprite)
    {
        Time = time;
        Sprite = sprite;
    }
}

/// <summary>A texture shown on a material from a given time on, until the next texture key.</summary>
[Serializable]
public sealed class TextureKey
{
    public float Time;
    public Texture2D? Texture;

    public TextureKey() { }

    public TextureKey(float time, Texture2D? texture)
    {
        Time = time;
        Texture = texture;
    }
}

/// <summary>
/// One animated property of one target: a transform under the take's root, or the root itself when the target path
/// is empty. Vector tracks carry one scalar curve per channel, so a track can key only the axis it animates and leave
/// the others where the object already is. Component and material tracks name the member they drive, so one track
/// type animates a canvas group's alpha, a UI element's size or a shader's colour.
/// </summary>
[Serializable]
public sealed class AnimationTrack
{
    /// <summary>Keys closer than this in time are the same key.</summary>
    public const float TimeEpsilon = 1e-4f;

    private static readonly Dictionary<string, MemberInfo?> s_members = [];

    [SerializeField] private string _target = string.Empty;
    [SerializeField] private AnimationTrackKind _kind;
    [SerializeField] private bool _enabled = true;
    [SerializeField] private AnimationCurve _x = new(1);
    [SerializeField] private AnimationCurve _y = new(1);
    [SerializeField] private AnimationCurve _z = new(1);
    [SerializeField] private AnimationCurve _w = new(1);
    [SerializeField] private List<SpriteKey> _sprites = [];
    [SerializeField] private List<TextureKey> _textures = [];

    [SerializeField] private string _component = string.Empty;
    [SerializeField] private string _property = string.Empty;
    [SerializeField] private int _channels = 1;

    public AnimationTrack() { }

    public AnimationTrack(string target, AnimationTrackKind kind)
    {
        _target = target ?? string.Empty;
        _kind = kind;
        _channels = DefaultChannels(kind);
    }

    /// <summary>Path from the take's root, as <see cref="Transform.Find"/> reads it. Empty is the root.</summary>
    public string Target { get => _target; set => _target = value ?? string.Empty; }

    public AnimationTrackKind Kind { get => _kind; set => _kind = value; }

    public bool Enabled { get => _enabled; set => _enabled = value; }

    /// <summary>Component type name a <see cref="AnimationTrackKind.ComponentProperty"/> track drives.</summary>
    public string Component { get => _component; set => _component = value ?? string.Empty; }

    /// <summary>Uniform name of a material track, or member name of a component track.</summary>
    public string Property { get => _property; set => _property = value ?? string.Empty; }

    /// <summary>How many curves a component or material track uses: 1 for a float, 2 to 4 for a vector or colour.</summary>
    public int Channels
    {
        get => _channels <= 0 ? DefaultChannels(_kind) : _channels;
        set => _channels = Math.Clamp(value, 1, 4);
    }

    public AnimationCurve X => _x;
    public AnimationCurve Y => _y;
    public AnimationCurve Z => _z;
    public AnimationCurve W => _w;
    public IReadOnlyList<SpriteKey> Sprites => _sprites;
    public IReadOnlyList<TextureKey> Textures => _textures;

    public bool IsSprite => _kind == AnimationTrackKind.SpriteFrame;
    public bool IsTexture => _kind == AnimationTrackKind.MaterialTexture;
    public bool IsKeyed => IsSprite || IsTexture;

    public string KindName => _kind switch
    {
        AnimationTrackKind.TransformPosition => "Position",
        AnimationTrackKind.TransformRotation => "Rotation",
        AnimationTrackKind.TransformScale => "Scale",
        AnimationTrackKind.SpriteFrame => "Sprite",
        AnimationTrackKind.TintColor => "Tint",
        AnimationTrackKind.MaterialProperty => "Material",
        AnimationTrackKind.MaterialTexture => "Material Map",
        _ => string.IsNullOrEmpty(_component) ? "Property" : $"{_component}.{_property}",
    };

    public string DisplayName
    {
        get
        {
            string label = KindName;
            if (_kind == AnimationTrackKind.MaterialProperty && !string.IsNullOrEmpty(_property))
                label = $"Material  {_property}";
            else if (_kind == AnimationTrackKind.MaterialTexture && !string.IsNullOrEmpty(_property))
                label = $"Map  {_property}";

            return string.IsNullOrEmpty(_target) ? label : $"{_target}  ({label})";
        }
    }

    /// <summary>The number of keys across the track, which is what the timeline draws.</summary>
    public int KeyCount
    {
        get
        {
            if (IsSprite) return _sprites.Count;
            if (IsTexture) return _textures.Count;

            int count = Math.Max(_x.Count, Math.Max(_y.Count, _z.Count));
            return Channels > 3 ? Math.Max(count, _w.Count) : count;
        }
    }

    public AnimationCurve CurveFor(int axis) => axis switch
    {
        0 => _x,
        1 => _y,
        2 => _z,
        _ => _w,
    };

    private static int DefaultChannels(AnimationTrackKind kind) => kind switch
    {
        AnimationTrackKind.TransformPosition or AnimationTrackKind.TransformRotation or AnimationTrackKind.TransformScale => 3,
        AnimationTrackKind.TintColor => 4,
        _ => 1,
    };

    /// <summary>The value an axis holds at <paramref name="time"/>, or <paramref name="fallback"/> when it has no keys.</summary>
    public static float EvaluateAxis(AnimationCurve curve, float time, float fallback)
        => curve.Count == 0 ? fallback : curve.Evaluate(time);

    /// <summary>
    /// The vector at <paramref name="time"/>. Each axis falls back to <paramref name="fallback"/> on its own, so a
    /// position track keyed only on Y keeps the object's X and Z.
    /// </summary>
    public Float3 EvaluateVector(float time, Float3 fallback) => new(
        EvaluateAxis(_x, time, fallback.X),
        EvaluateAxis(_y, time, fallback.Y),
        EvaluateAxis(_z, time, fallback.Z));

    /// <summary>The four channels at <paramref name="time"/>, each falling back on its own.</summary>
    public Float4 EvaluateVector4(float time, Float4 fallback) => new(
        EvaluateAxis(_x, time, fallback.X),
        EvaluateAxis(_y, time, fallback.Y),
        EvaluateAxis(_z, time, fallback.Z),
        EvaluateAxis(_w, time, fallback.W));

    /// <summary>Writes a key on one axis, overwriting the key already at that time instead of stacking a second.</summary>
    public void SetKey(int axis, float time, float value, CurveInterpolation interpolation = CurveInterpolation.Linear)
        => SetKey(CurveFor(axis), time, value, interpolation);

    public static void SetKey(AnimationCurve curve, float time, float value, CurveInterpolation interpolation = CurveInterpolation.Linear)
    {
        for (int i = 0; i < curve.Count; i++)
        {
            if (MathF.Abs(curve[i].Time - time) > TimeEpsilon) continue;
            Keyframe key = curve[i];
            key.Value = value;
            key.Interpolation = interpolation;
            curve[i] = key;
            return;
        }

        curve.AddKey(new Keyframe(time, value) { Interpolation = interpolation });
    }

    /// <summary>Writes a key on every channel at once, which is what keying a colour or a vector means.</summary>
    public void SetVectorKey(float time, Float4 value, CurveInterpolation interpolation = CurveInterpolation.Linear)
    {
        int channels = Channels;
        SetKey(0, time, value.X, interpolation);
        if (channels > 1) SetKey(1, time, value.Y, interpolation);
        if (channels > 2) SetKey(2, time, value.Z, interpolation);
        if (channels > 3) SetKey(3, time, value.W, interpolation);
    }

    /// <summary>Every distinct time any axis is keyed at, sorted, which is what the track row draws.</summary>
    public List<float> KeyTimes()
    {
        var times = new List<float>();
        if (IsSprite)
        {
            foreach (SpriteKey key in _sprites) times.Add(key.Time);
        }
        else if (IsTexture)
        {
            foreach (TextureKey key in _textures) times.Add(key.Time);
        }
        else
        {
            AddTimes(times, _x);
            AddTimes(times, _y);
            AddTimes(times, _z);
            if (Channels > 3) AddTimes(times, _w);
        }

        times.Sort();
        for (int i = times.Count - 1; i > 0; i--)
            if (times[i] - times[i - 1] <= TimeEpsilon) times.RemoveAt(i);
        return times;
    }

    private static void AddTimes(List<float> times, AnimationCurve curve)
    {
        for (int i = 0; i < curve.Count; i++) times.Add(curve[i].Time);
    }

    /// <summary>Removes every axis key at <paramref name="time"/>.</summary>
    public bool RemoveKeysAt(float time)
    {
        bool removed = false;
        for (int i = 0; i < _sprites.Count; i++)
        {
            if (MathF.Abs(_sprites[i].Time - time) > TimeEpsilon) continue;
            _sprites.RemoveAt(i--);
            removed = true;
        }

        for (int i = 0; i < _textures.Count; i++)
        {
            if (MathF.Abs(_textures[i].Time - time) > TimeEpsilon) continue;
            _textures.RemoveAt(i--);
            removed = true;
        }

        removed |= RemoveAt(_x, time);
        removed |= RemoveAt(_y, time);
        removed |= RemoveAt(_z, time);
        if (Channels > 3) removed |= RemoveAt(_w, time);
        return removed;
    }

    private static bool RemoveAt(AnimationCurve curve, float time)
    {
        for (int i = 0; i < curve.Count; i++)
        {
            if (MathF.Abs(curve[i].Time - time) > TimeEpsilon) continue;
            curve.RemoveKey(i);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Moves every key at or after <paramref name="from"/> by <paramref name="delta"/> seconds, which is how the
    /// timeline inserts time under a playhead without touching what comes before it.
    /// </summary>
    public void ShiftKeys(float from, float delta)
    {
        if (delta == 0f) return;

        foreach (SpriteKey key in _sprites)
            if (key.Time >= from - TimeEpsilon) key.Time += delta;

        foreach (TextureKey key in _textures)
            if (key.Time >= from - TimeEpsilon) key.Time += delta;

        ShiftCurve(_x, from, delta);
        ShiftCurve(_y, from, delta);
        ShiftCurve(_z, from, delta);
        if (Channels > 3) ShiftCurve(_w, from, delta);
    }

    private static void ShiftCurve(AnimationCurve curve, float from, float delta)
    {
        for (int i = 0; i < curve.Count; i++)
        {
            Keyframe key = curve[i];
            if (key.Time < from - TimeEpsilon) continue;
            key.Time += delta;
            curve[i] = key;
        }
    }

    /// <summary>Moves every key at <paramref name="from"/> to <paramref name="to"/>, which is how the timeline retimes a key.</summary>
    public void MoveKeys(float from, float to)
    {
        if (MathF.Abs(to - from) <= TimeEpsilon) return;

        foreach (SpriteKey key in _sprites)
            if (MathF.Abs(key.Time - from) <= TimeEpsilon) key.Time = to;

        foreach (TextureKey key in _textures)
            if (MathF.Abs(key.Time - from) <= TimeEpsilon) key.Time = to;

        MoveCurve(_x, from, to);
        MoveCurve(_y, from, to);
        MoveCurve(_z, from, to);
        if (Channels > 3) MoveCurve(_w, from, to);

        _sprites.Sort((a, b) => a.Time.CompareTo(b.Time));
        _textures.Sort((a, b) => a.Time.CompareTo(b.Time));
    }

    private static void MoveCurve(AnimationCurve curve, float from, float to)
    {
        for (int i = 0; i < curve.Count; i++)
        {
            if (MathF.Abs(curve[i].Time - from) > TimeEpsilon) continue;
            Keyframe key = curve[i];
            key.Time = to;
            curve[i] = key;
            return;
        }
    }

    /// <summary>Sets the interpolation of every key at <paramref name="time"/>, on every channel.</summary>
    public void SetInterpolationAt(float time, CurveInterpolation interpolation)
    {
        SetInterpolation(_x, time, interpolation);
        SetInterpolation(_y, time, interpolation);
        SetInterpolation(_z, time, interpolation);
        if (Channels > 3) SetInterpolation(_w, time, interpolation);
    }

    private static void SetInterpolation(AnimationCurve curve, float time, CurveInterpolation interpolation)
    {
        for (int i = 0; i < curve.Count; i++)
        {
            if (MathF.Abs(curve[i].Time - time) > TimeEpsilon) continue;
            Keyframe key = curve[i];
            key.Interpolation = interpolation;
            curve[i] = key;
            if (interpolation == CurveInterpolation.CubicSpline) curve.SmoothTangents(CurveTangentMode.Auto);
            return;
        }
    }

    /// <summary>The sprite in force at <paramref name="time"/>, or null before the first key.</summary>
    public Sprite? EvaluateSprite(float time)
    {
        Sprite? found = null;
        float best = float.NegativeInfinity;
        foreach (SpriteKey key in _sprites)
        {
            if (key.Time > time + TimeEpsilon || key.Time < best) continue;
            best = key.Time;
            found = key.Sprite;
        }
        return found;
    }

    /// <summary>The texture in force at <paramref name="time"/>, or null before the first key.</summary>
    public Texture2D? EvaluateTexture(float time)
    {
        Texture2D? found = null;
        float best = float.NegativeInfinity;
        foreach (TextureKey key in _textures)
        {
            if (key.Time > time + TimeEpsilon || key.Time < best) continue;
            best = key.Time;
            found = key.Texture;
        }
        return found;
    }

    public SpriteKey AddSpriteKey(float time, Sprite? sprite)
    {
        foreach (SpriteKey existing in _sprites)
        {
            if (MathF.Abs(existing.Time - time) > TimeEpsilon) continue;
            existing.Sprite = sprite;
            return existing;
        }

        var key = new SpriteKey(time, sprite);
        int index = _sprites.FindIndex(k => k.Time > time);
        if (index < 0) _sprites.Add(key);
        else _sprites.Insert(index, key);
        return key;
    }

    public bool RemoveSpriteKey(SpriteKey key) => _sprites.Remove(key);

    public TextureKey AddTextureKey(float time, Texture2D? texture)
    {
        foreach (TextureKey existing in _textures)
        {
            if (MathF.Abs(existing.Time - time) > TimeEpsilon) continue;
            existing.Texture = texture;
            return existing;
        }

        var key = new TextureKey(time, texture);
        int index = _textures.FindIndex(k => k.Time > time);
        if (index < 0) _textures.Add(key);
        else _textures.Insert(index, key);
        return key;
    }

    public bool RemoveTextureKey(TextureKey key) => _textures.Remove(key);

    /// <summary>Drops every key: the curves, the sprites and the textures.</summary>
    public void ClearKeys()
    {
        _x.Clear();
        _y.Clear();
        _z.Clear();
        _w.Clear();
        _sprites.Clear();
        _textures.Clear();
    }

    // ── Applying ────────────────────────────────────────────────────────

    /// <summary>Writes the tint colour of the first <see cref="IColorTint"/> on the target.</summary>
    public void ApplyTint(Transform target, float time)
    {
        IColorTint? tint = FindTint(target.GameObject);
        if (tint == null) return;

        Color current = tint.Tint;
        Float4 value = EvaluateVector4(time, new Float4(current.R, current.G, current.B, current.A));
        tint.Tint = new Color(value.X, value.Y, value.Z, value.W);
    }

    /// <summary>Writes a float or colour uniform on the material the target draws with.</summary>
    public void ApplyMaterialProperty(Transform target, float time)
    {
        Material? material = FindMaterial(target.GameObject);
        if (material.IsNotValid() || string.IsNullOrEmpty(_property)) return;

        switch (Channels)
        {
            case 1:
                material.SetFloat(_property, EvaluateAxis(_x, time, material.GetFloat(_property)));
                break;
            case 2:
            {
                Float4 current = material.GetVector(_property);
                material.SetVector(_property, new Float2(
                    EvaluateAxis(_x, time, current.X),
                    EvaluateAxis(_y, time, current.Y)));
                break;
            }
            case 3:
            {
                Float4 current = material.GetVector(_property);
                material.SetVector(_property, new Float3(
                    EvaluateAxis(_x, time, current.X),
                    EvaluateAxis(_y, time, current.Y),
                    EvaluateAxis(_z, time, current.Z)));
                break;
            }
            default:
            {
                Color current = material.GetColor(_property);
                Float4 value = EvaluateVector4(time, new Float4(current.R, current.G, current.B, current.A));
                material.SetColor(_property, new Color(value.X, value.Y, value.Z, value.W));
                break;
            }
        }
    }

    /// <summary>Swaps the texture a material map holds.</summary>
    public void ApplyMaterialTexture(Transform target, float time)
    {
        Material? material = FindMaterial(target.GameObject);
        if (material.IsNotValid() || string.IsNullOrEmpty(_property)) return;
        material.SetTexture(_property, EvaluateTexture(time));
    }

    /// <summary>Writes a numeric member of a component on the target.</summary>
    public void ApplyComponentProperty(Transform target, float time)
    {
        Component? component = FindComponent(target.GameObject, _component);
        if (component.IsNotValid() || string.IsNullOrEmpty(_property)) return;

        MemberInfo? member = FindMember(component.GetType(), _property);
        if (member == null || !TryRead(component, member, out Float4 current)) return;

        Float4 value = EvaluateVector4(time, current);
        TryWrite(component, member, value);
    }

    /// <summary>The colour a target is tinted with, for the timeline to read when keying.</summary>
    public static bool TryGetTint(GameObject? go, out Color color)
    {
        color = Color.White;
        if (go.IsNotValid()) return false;

        IColorTint? tint = FindTint(go);
        if (tint == null) return false;

        color = tint.Tint;
        return true;
    }

    /// <summary>The material a target draws with, for the timeline to enumerate its uniforms.</summary>
    public static Material? GetMaterial(GameObject? go) => go.IsNotValid() ? null : FindMaterial(go);

    private static IColorTint? FindTint(GameObject go)
    {
        foreach (Component component in go.GetComponents())
            if (component is IColorTint tint) return tint;
        return null;
    }

    private static Material? FindMaterial(GameObject go)
    {
        foreach (Component component in go.GetComponents())
        {
            if (component is IMaterialRenderer renderer)
            {
                Material? material = renderer.GetMaterial(0);
                if (material.IsValid()) return material;
            }

            if (component is Graphic graphic && graphic.Material.IsValid()) return graphic.Material;
        }

        return null;
    }

    /// <summary>The component on a target whose type name is <paramref name="typeName"/>.</summary>
    public static Component? FindComponent(GameObject go, string typeName)
    {
        if (string.IsNullOrEmpty(typeName)) return null;

        foreach (Component component in go.GetComponents())
        {
            Type type = component.GetType();
            if (type.Name == typeName || type.FullName == typeName) return component;
        }

        return null;
    }

    private static MemberInfo? FindMember(Type type, string name)
    {
        string key = type.FullName + "." + name;
        if (s_members.TryGetValue(key, out MemberInfo? cached)) return cached;

        MemberInfo? found = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
        found ??= type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
        s_members[key] = found;
        return found;
    }

    private static bool TryRead(object source, MemberInfo member, out Float4 value)
    {
        value = default;
        object? raw;
        try
        {
            raw = member is PropertyInfo property ? property.GetValue(source) : ((FieldInfo)member).GetValue(source);
        }
        catch (Exception)
        {
            return false;
        }

        switch (raw)
        {
            case float f:
                value = new Float4(f, 0f, 0f, 0f);
                return true;
            case int i:
                value = new Float4(i, 0f, 0f, 0f);
                return true;
            case bool b:
                value = new Float4(b ? 1f : 0f, 0f, 0f, 0f);
                return true;
            case Color c:
                value = new Float4(c.R, c.G, c.B, c.A);
                return true;
            case Float2 v2:
                value = new Float4(v2.X, v2.Y, 0f, 0f);
                return true;
            case Float3 v3:
                value = new Float4(v3.X, v3.Y, v3.Z, 0f);
                return true;
            case Float4 v4:
                value = v4;
                return true;
            default:
                return false;
        }
    }

    private static bool TryWrite(object target, MemberInfo member, Float4 value)
    {
        Type type = member is PropertyInfo property ? property.PropertyType : ((FieldInfo)member).FieldType;
        object? converted;
        try
        {
            if (type == typeof(float)) converted = value.X;
            else if (type == typeof(int)) converted = (int)MathF.Round(value.X);
            else if (type == typeof(bool)) converted = value.X >= 0.5f;
            else if (type == typeof(Color)) converted = new Color(value.X, value.Y, value.Z, value.W);
            else if (type == typeof(Float2)) converted = new Float2(value.X, value.Y);
            else if (type == typeof(Float3)) converted = new Float3(value.X, value.Y, value.Z);
            else if (type == typeof(Float4)) converted = value;
            else return false;

            if (member is PropertyInfo p)
            {
                if (!p.CanWrite) return false;
                p.SetValue(target, converted);
            }
            else
            {
                ((FieldInfo)member).SetValue(target, converted);
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>How many curves a member of a component uses, or 0 when it cannot be animated.</summary>
    public static int ChannelsOfMember(Type type, string name)
    {
        MemberInfo? member = FindMember(type, name);
        if (member == null) return 0;

        Type memberType = member is PropertyInfo property ? property.PropertyType : ((FieldInfo)member).FieldType;
        if (memberType == typeof(float) || memberType == typeof(int) || memberType == typeof(bool)) return 1;
        if (memberType == typeof(Float2)) return 2;
        if (memberType == typeof(Float3)) return 3;
        if (memberType == typeof(Float4) || memberType == typeof(Color)) return 4;
        return 0;
    }

    /// <summary>Reads a member of a component as four channels, for keying it from the timeline.</summary>
    public static bool TryGetMemberValue(Component component, string name, out Float4 value)
    {
        value = default;
        if (component.IsNotValid() || string.IsNullOrEmpty(name)) return false;

        MemberInfo? member = FindMember(component.GetType(), name);
        if (member == null) return false;
        return TryRead(component, member, out value);
    }

    /// <summary>Writes a member of a component from four channels, for dragging a curve key.</summary>
    public static bool TrySetMemberValue(Component component, string name, Float4 value)
    {
        if (component.IsNotValid() || string.IsNullOrEmpty(name)) return false;

        MemberInfo? member = FindMember(component.GetType(), name);
        if (member == null) return false;
        return TryWrite(component, member, value);
    }
}
