// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Infinity.Vector;

namespace Infinity.Runtime.Graphs;

/// <summary>Type-safe helpers the generated script graph code calls, so a wire can carry a number or a vector.</summary>
public static class ScriptGraphOps
{
    public static float ToFloat(float value) => value;
    public static float ToFloat(Float3 value) => value.X;
    public static float ToFloat(Float2 value) => value.X;
    public static float ToFloat(bool value) => value ? 1f : 0f;
    public static float ToFloat(int value) => value;
    public static float ToFloat(string value) => float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float result) ? result : 0f;
    public static float ToFloat<TEnum>(TEnum value) where TEnum : struct, Enum
        => Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture);

    public static Float2 ToVec2(Float2 value) => value;
    public static Float2 ToVec2(Float3 value) => new(value.X, value.Y);
    public static Float2 ToVec2(float value) => new(value, value);
    public static Float2 ToVec2(int value) => new(value, value);
    public static Float2 ToVec2(bool value) => new(value ? 1f : 0f);
    public static Float2 ToVec2(string value) => new(ToFloat(value), ToFloat(value));

    public static Float3 ToVec3(float value) => new(value, value, value);
    public static Float3 ToVec3(int value) => new(value, value, value);
    public static Float3 ToVec3(bool value) => new(value ? 1f : 0f);
    public static Float3 ToVec3(Float3 value) => value;
    public static Float3 ToVec3(Float2 value) => new(value.X, value.Y, 0f);
    public static Float3 ToVec3(Color value) => new(value.R, value.G, value.B);
    public static Float3 ToVec3(string value) => new(ToFloat(value), ToFloat(value), ToFloat(value));

    public static Color ToColor(Color value) => value;
    public static Color ToColor(Float3 value) => new(value.X, value.Y, value.Z, 1f);
    public static Color ToColor(Float2 value) => new(value.X, value.Y, 0f, 1f);
    public static Color ToColor(float value) => new(value, value, value, 1f);
    public static Color ToColor(int value) => new(value, value, value, 1f);
    public static Color ToColor(bool value) => new(value ? 1f : 0f, value ? 1f : 0f, value ? 1f : 0f, 1f);
    public static Color ToColor(string value) => new(ToFloat(value), ToFloat(value), ToFloat(value), 1f);

    public static Quaternion ToQuat(Quaternion value) => value;
    public static Quaternion ToQuat(Float3 euler) => Quaternion.FromEuler(euler);
    public static Quaternion ToQuat(Float2 euler) => Quaternion.FromEuler(euler.X, euler.Y, 0f);
    public static Quaternion ToQuat(float value) => Quaternion.FromEuler(value, value, value);
    public static Quaternion ToQuat(int value) => Quaternion.FromEuler(value, value, value);

    public static bool ToBool(bool value) => value;
    public static bool ToBool(float value) => value > 0.0001f;
    public static bool ToBool(int value) => value != 0;
    public static bool ToBool(Float3 value) => value.X > 0.0001f || value.Y > 0.0001f || value.Z > 0.0001f;
    public static bool ToBool(Float2 value) => value.X > 0.0001f || value.Y > 0.0001f;
    public static bool ToBool(Color value) => value.R > 0.0001f || value.G > 0.0001f || value.B > 0.0001f || value.A > 0.0001f;
    public static bool ToBool(string value) => !string.IsNullOrEmpty(value);
    public static bool ToBool<TEnum>(TEnum value) where TEnum : struct, Enum
        => Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture) != 0;

    public static string ToText(string value) => value;
    public static string ToText(float value) => value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
    public static string ToText(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public static string ToText(bool value) => value ? "true" : "false";
    public static string ToText(Float3 value) => $"({value.X:0.###}, {value.Y:0.###}, {value.Z:0.###})";
    public static string ToText(Float2 value) => $"({value.X:0.###}, {value.Y:0.###})";
    public static string ToText(Color value) => $"({value.R:0.###}, {value.G:0.###}, {value.B:0.###}, {value.A:0.###})";
    public static string ToText(Quaternion value)
    {
        Float3 euler = Quaternion.ToEuler(value);
        return $"({euler.X:0.###}, {euler.Y:0.###}, {euler.Z:0.###})";
    }
    public static string ToText<TEnum>(TEnum value) where TEnum : struct, Enum => value.ToString();

    public static float Add(float a, float b) => a + b;
    public static Float3 Add(Float3 a, Float3 b) => a + b;
    public static float Subtract(float a, float b) => a - b;
    public static Float3 Subtract(Float3 a, Float3 b) => a - b;
    public static float Multiply(float a, float b) => a * b;
    public static Float3 Multiply(Float3 a, float b) => a * b;
    public static Float3 Multiply(float a, Float3 b) => b * a;
    public static Float3 Multiply(Float3 a, Float3 b) => new(a.X * b.X, a.Y * b.Y, a.Z * b.Z);
    public static float Divide(float a, float b) => b == 0f ? 0f : a / b;
    public static Float3 Divide(Float3 a, float b) => b == 0f ? Float3.Zero : a / b;
    public static Float3 Divide(Float3 a, Float3 b) => new(
        b.X == 0f ? 0f : a.X / b.X,
        b.Y == 0f ? 0f : a.Y / b.Y,
        b.Z == 0f ? 0f : a.Z / b.Z);
    public static float Random(float min, float max) => min + (float)System.Random.Shared.NextDouble() * (max - min);

    public static bool Compare(float a, float b, string op) => op switch
    {
        "Less" or "<" => a < b,
        "Less Or Equal" or "<=" => a <= b,
        "Equal" or "==" => MathF.Abs(a - b) < 0.0001f,
        "Not Equal" or "!=" => MathF.Abs(a - b) >= 0.0001f,
        "Greater Or Equal" or ">=" => a >= b,
        _ => a > b,
    };

    public static bool Logic(bool a, bool b, string op) => op switch
    {
        "Or" => a || b,
        "Xor" => a ^ b,
        "Not" => !a,
        _ => a && b,
    };

    public static Float3 Lerp(Float3 a, Float3 b, float t) => new(
        a.X + (b.X - a.X) * t,
        a.Y + (b.Y - a.Y) * t,
        a.Z + (b.Z - a.Z) * t);

    public static float Clamp(float value, float min, float max) => Math.Clamp(value, min, max);

    public static float VectorSplit(Float3 vector, string axis) => axis switch
    {
        "Y" => vector.Y,
        "Z" => vector.Z,
        _ => vector.X,
    };

    public static Float3 VectorJoin(float x, float y, float z) => new(x, y, z);

    public static Float2 MousePosition() => new(Input.MousePosition.X, Input.MousePosition.Y);

    /// <summary>Runs an action on a component of this object, when the object has one.</summary>
    public static void CallComponent<T>(GameObject gameObject, Action<T> call) where T : Component
    {
        T? component = gameObject.GetComponent<T>();
        if (component.IsValid()) call(component);
    }

    /// <summary>Reads a member from a component of this object, with a fallback when it has none.</summary>
    public static TValue GetComponentValue<T, TValue>(GameObject gameObject, Func<T, TValue> read, TValue fallback = default!) where T : Component
    {
        T? component = gameObject.GetComponent<T>();
        return component.IsValid() ? read(component) : fallback;
    }

    /// <summary>Writes a member on a component of this object, when the object has one.</summary>
    public static void SetComponentValue<T, TValue>(GameObject gameObject, Action<T, TValue> write, TValue value) where T : Component
    {
        T? component = gameObject.GetComponent<T>();
        if (component.IsValid()) write(component, value);
    }
}
