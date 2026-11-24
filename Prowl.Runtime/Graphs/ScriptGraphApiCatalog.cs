// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

using Infinity.Vector;

namespace Infinity.Runtime.Graphs;

/// <summary>Where an API member is read from when a graph calls it.</summary>
public enum ScriptApiTarget : byte
{
    Static,
    Self,
    GameObject,
    Transform,
    Component,
}

/// <summary>One engine API member the Script Graph can call, with the pins it exposes.</summary>
public sealed class ScriptApiMember
{
    public required string Key { get; init; }
    public required string TypeId { get; init; }
    public required string DisplayName { get; init; }
    public required string Category { get; init; }
    public required string Description { get; init; }
    public required ScriptApiTarget Target { get; init; }
    public required string DeclaringType { get; init; }
    public required string MemberName { get; init; }
    public required string ReturnType { get; init; }
    public required bool ReturnsVoid { get; init; }
    public required bool IsProperty { get; init; }
    public required bool IsSetter { get; init; }
    public required bool IsField { get; init; }
    public required string[] ParameterTypes { get; init; }
    public required ScriptPinKind[] ParameterKinds { get; init; }
    public required ScriptGraphNodeDef Definition { get; init; }

    /// <summary>True when the node is an action with an exec input in front of its parameters.</summary>
    public bool HasExecInput => ReturnsVoid || IsSetter;

    public string Signature
    {
        get
        {
            if (IsProperty || IsField) return MemberName;
            return $"{MemberName}({string.Join(", ", ParameterTypes)})";
        }
    }
}

/// <summary>
/// Reflects the engine's public C# surface into graph nodes. Every static method, property and field of an engine
/// type, and every member of a component, becomes a node whose pins follow the member's own signature. The
/// catalogue is built once and shared by the editor picker and the code generator, so what a user searches for is
/// exactly what compiles.
/// </summary>
public static class ScriptGraphApiCatalog
{
    public const string Prefix = "script.api:";

    private static readonly Lazy<IReadOnlyList<ScriptApiMember>> s_members = new(Build);
    private static readonly Lazy<Dictionary<string, ScriptApiMember>> s_byKey = new(() =>
    {
        var map = new Dictionary<string, ScriptApiMember>(StringComparer.Ordinal);
        foreach (ScriptApiMember member in s_members.Value) map[member.Key] = member;
        return map;
    });

    public static IReadOnlyList<ScriptApiMember> Members => s_members.Value;

    public static ScriptApiMember? Find(string typeId)
    {
        if (string.IsNullOrEmpty(typeId) || !typeId.StartsWith(Prefix, StringComparison.Ordinal)) return null;
        return s_byKey.Value.GetValueOrDefault(typeId[Prefix.Length..]);
    }

    public static ScriptGraphNodeDef? Definition(string typeId) => Find(typeId)?.Definition;

    private static IReadOnlyList<ScriptApiMember> Build()
    {
        var result = new List<ScriptApiMember>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (assembly.IsDynamic) continue;
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray()!; }
            catch { continue; }

            foreach (Type type in types)
            {
                if (!IsEngineType(type)) continue;
                AddMethods(result, seen, type);
                AddProperties(result, seen, type);
                AddFields(result, seen, type);
            }
        }

        result.Sort((a, b) =>
        {
            int category = string.Compare(a.Category, b.Category, StringComparison.OrdinalIgnoreCase);
            return category != 0 ? category : string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase);
        });
        return result;
    }

    private static readonly HashSet<string> s_standardApiTypes = new(StringComparer.Ordinal)
    {
        "System.Math", "System.MathF", "System.String", "System.Convert",
        "System.Guid", "System.TimeSpan", "System.DateTime", "System.Environment",
        "System.IO.Path",
    };

    private static bool IsEngineType(Type type)
    {
        if (!type.IsVisible || type.IsGenericTypeDefinition || type.IsByRefLike) return false;
        if (type == typeof(ScriptGraphApiCatalog)) return false;
        string? ns = type.Namespace;
        if (string.IsNullOrEmpty(ns)) return false;
        if (!ns.StartsWith("Infinity.", StringComparison.Ordinal) && !ns.StartsWith("Prowl.", StringComparison.Ordinal))
            return type.FullName != null && s_standardApiTypes.Contains(type.FullName);
        if (ns.StartsWith("Infinity.Editor", StringComparison.Ordinal)) return false;
        if (type.Name.StartsWith("<", StringComparison.Ordinal)) return false;
        return true;
    }

    private static ScriptApiTarget? TargetOf(Type type)
    {
        if (type == typeof(GameObject)) return ScriptApiTarget.GameObject;
        if (type == typeof(Transform)) return ScriptApiTarget.Transform;
        if (type == typeof(ScriptGraphBehaviour)) return ScriptApiTarget.Self;
        if (type.IsSubclassOf(typeof(Component))) return ScriptApiTarget.Component;
        return null;
    }

    private static void AddMethods(List<ScriptApiMember> result, HashSet<string> seen, Type type)
    {
        ScriptApiTarget? instanceTarget = TargetOf(type);
        foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            if (method.IsSpecialName || method.IsGenericMethodDefinition || method.ContainsGenericParameters) continue;
            if (method.DeclaringType == typeof(object)) continue;
            if (method.GetCustomAttribute<ObsoleteAttribute>() is { IsError: true }) continue;

            ScriptApiTarget target;
            if (method.IsStatic) target = ScriptApiTarget.Static;
            else if (instanceTarget is { } found) target = found;
            else continue;

            if (method.ReturnType.IsByRef || method.ReturnType.IsPointer || method.ReturnType.IsByRefLike) continue;
            bool returnsVoid = method.ReturnType == typeof(void);
            ScriptPinKind? returnKind = returnsVoid ? null : KindOf(method.ReturnType);
            if (!returnsVoid && returnKind == null) continue;

            ParameterInfo[] parameters = method.GetParameters();
            var kinds = new ScriptPinKind[parameters.Length];
            var typeNames = new string[parameters.Length];
            bool usable = true;
            for (int i = 0; i < parameters.Length; i++)
            {
                ParameterInfo parameter = parameters[i];
                if (parameter.ParameterType.IsByRef || parameter.IsOut || parameter.ParameterType.IsPointer ||
                    parameter.ParameterType.IsByRefLike || parameter.GetCustomAttribute<ParamArrayAttribute>() != null)
                {
                    usable = false;
                    break;
                }
                ScriptPinKind? kind = KindOf(parameter.ParameterType);
                if (kind == null) { usable = false; break; }
                kinds[i] = kind.Value;
                typeNames[i] = CSharpName(parameter.ParameterType);
            }
            if (!usable) continue;

            string signature = string.Join(",", typeNames);
            string key = $"{target}:{type.FullName}:{method.Name}:{signature}";
            if (!seen.Add(key)) continue;

            var inputs = new List<ScriptGraphPin>();
            if (returnsVoid) inputs.Add(new ScriptGraphPin("In", "In", ScriptPinKind.Exec));
            for (int i = 0; i < parameters.Length; i++)
                inputs.Add(new ScriptGraphPin($"arg{i}", Label(parameters[i].Name, i), kinds[i]));

            var outputs = new List<ScriptGraphPin>();
            if (returnsVoid) outputs.Add(new ScriptGraphPin("Then", "Then", ScriptPinKind.Exec));
            else outputs.Add(new ScriptGraphPin("Out", "Out", returnKind!.Value));

            string display = target == ScriptApiTarget.Static ? $"{type.Name}.{method.Name}" : method.Name;
            string category = CategoryOf(type, target);
            var def = new ScriptGraphNodeDef(Prefix + key, display, category,
                Describe(type, method, parameters, returnsVoid), [.. inputs], [.. outputs]);
            result.Add(new ScriptApiMember
            {
                Key = key,
                TypeId = Prefix + key,
                DisplayName = display,
                Category = category,
                Description = def.Description,
                Target = target,
                DeclaringType = CSharpName(type),
                MemberName = method.Name,
                ReturnType = returnsVoid ? "void" : CSharpName(method.ReturnType),
                ReturnsVoid = returnsVoid,
                IsProperty = false,
                IsSetter = false,
                IsField = false,
                ParameterTypes = typeNames,
                ParameterKinds = kinds,
                Definition = def,
            });
        }
    }

    private static void AddProperties(List<ScriptApiMember> result, HashSet<string> seen, Type type)
    {
        ScriptApiTarget? instanceTarget = TargetOf(type);
        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            if (property.GetIndexParameters().Length > 0 || property.GetMethod == null) continue;
            if (property.GetCustomAttribute<ObsoleteAttribute>() is { IsError: true }) continue;
            ScriptPinKind? kind = KindOf(property.PropertyType);
            if (kind == null) continue;

            ScriptApiTarget target;
            if (property.GetMethod.IsStatic) target = ScriptApiTarget.Static;
            else if (instanceTarget is { } found) target = found;
            else continue;

            string key = $"{target}:{type.FullName}:{property.Name}:get";
            if (seen.Add(key))
            {
                string display = target == ScriptApiTarget.Static ? $"{type.Name}.{property.Name}" : property.Name;
                var def = new ScriptGraphNodeDef(Prefix + key, display, CategoryOf(type, target),
                    $"Reads {type.Name}.{property.Name} ({CSharpName(property.PropertyType)}).",
                    [], [new ScriptGraphPin("Out", "Out", kind.Value)]);
                result.Add(new ScriptApiMember
                {
                    Key = key,
                    TypeId = Prefix + key,
                    DisplayName = display,
                    Category = def.Category,
                    Description = def.Description,
                    Target = target,
                    DeclaringType = CSharpName(type),
                    MemberName = property.Name,
                    ReturnType = CSharpName(property.PropertyType),
                    ReturnsVoid = false,
                    IsProperty = true,
                    IsSetter = false,
                    IsField = false,
                    ParameterTypes = [],
                    ParameterKinds = [],
                    Definition = def,
                });
            }

            if (property.SetMethod is not { IsPublic: true }) continue;
            string setKey = $"{target}:{type.FullName}:{property.Name}:set";
            if (!seen.Add(setKey)) continue;

            string setDisplay = target == ScriptApiTarget.Static ? $"{type.Name}.{property.Name} =" : property.Name + " =";
            var setDef = new ScriptGraphNodeDef(Prefix + setKey, setDisplay, CategoryOf(type, target),
                $"Writes {type.Name}.{property.Name} ({CSharpName(property.PropertyType)}).",
                [new ScriptGraphPin("In", "In", ScriptPinKind.Exec), new ScriptGraphPin("Value", "Value", kind.Value)],
                [new ScriptGraphPin("Then", "Then", ScriptPinKind.Exec)]);
            result.Add(new ScriptApiMember
            {
                Key = setKey,
                TypeId = Prefix + setKey,
                DisplayName = setDisplay,
                Category = setDef.Category,
                Description = setDef.Description,
                Target = target,
                DeclaringType = CSharpName(type),
                MemberName = property.Name,
                ReturnType = "void",
                ReturnsVoid = true,
                IsProperty = true,
                IsSetter = true,
                IsField = false,
                ParameterTypes = [CSharpName(property.PropertyType)],
                ParameterKinds = [kind.Value],
                Definition = setDef,
            });
        }
    }

    private static void AddFields(List<ScriptApiMember> result, HashSet<string> seen, Type type)
    {
        ScriptApiTarget? instanceTarget = TargetOf(type);
        foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            if (field.IsLiteral || field.IsInitOnly) continue;
            if (field.GetCustomAttribute<ObsoleteAttribute>() is { IsError: true }) continue;
            ScriptPinKind? kind = KindOf(field.FieldType);
            if (kind == null) continue;

            ScriptApiTarget target;
            if (field.IsStatic) target = ScriptApiTarget.Static;
            else if (instanceTarget is { } found) target = found;
            else continue;

            string key = $"{target}:{type.FullName}:{field.Name}:field";
            if (seen.Add(key))
            {
                string display = target == ScriptApiTarget.Static ? $"{type.Name}.{field.Name}" : field.Name;
                var def = new ScriptGraphNodeDef(Prefix + key, display, CategoryOf(type, target),
                    $"Reads {type.Name}.{field.Name} ({CSharpName(field.FieldType)}).",
                    [], [new ScriptGraphPin("Out", "Out", kind.Value)]);
                result.Add(new ScriptApiMember
                {
                    Key = key, TypeId = Prefix + key, DisplayName = display, Category = def.Category,
                    Description = def.Description, Target = target, DeclaringType = CSharpName(type),
                    MemberName = field.Name, ReturnType = CSharpName(field.FieldType), ReturnsVoid = false,
                    IsProperty = false, IsSetter = false, IsField = true, ParameterTypes = [],
                    ParameterKinds = [], Definition = def,
                });
            }

            if (field.IsInitOnly) continue;
            string setKey = $"{target}:{type.FullName}:{field.Name}:fieldset";
            if (!seen.Add(setKey)) continue;
            string setDisplay = target == ScriptApiTarget.Static ? $"{type.Name}.{field.Name} =" : field.Name + " =";
            var setDef = new ScriptGraphNodeDef(Prefix + setKey, setDisplay, CategoryOf(type, target),
                $"Writes {type.Name}.{field.Name} ({CSharpName(field.FieldType)}).",
                [new ScriptGraphPin("In", "In", ScriptPinKind.Exec), new ScriptGraphPin("Value", "Value", kind.Value)],
                [new ScriptGraphPin("Then", "Then", ScriptPinKind.Exec)]);
            result.Add(new ScriptApiMember
            {
                Key = setKey, TypeId = Prefix + setKey, DisplayName = setDisplay, Category = setDef.Category,
                Description = setDef.Description, Target = target, DeclaringType = CSharpName(type),
                MemberName = field.Name, ReturnType = "void", ReturnsVoid = true, IsProperty = false,
                IsSetter = true, IsField = true, ParameterTypes = [CSharpName(field.FieldType)],
                ParameterKinds = [kind.Value], Definition = setDef,
            });
        }
    }

    private static string CategoryOf(Type type, ScriptApiTarget target)
    {
        if (target == ScriptApiTarget.Static)
        {
            string ns = type.Namespace ?? "";
            string area = ns.StartsWith("Infinity.", StringComparison.Ordinal) ? ns["Infinity.".Length..] : ns;
            int dot = area.IndexOf('.');
            if (dot > 0) area = area[..dot];
            return $"API/{Title(area)}/{type.Name}";
        }
        return $"API/{Title(type.Name)}";
    }

    private static string Title(string text)
    {
        if (text.Length == 0) return "General";
        var builder = new StringBuilder(text.Length + 2);
        builder.Append(char.ToUpperInvariant(text[0]));
        for (int i = 1; i < text.Length; i++)
        {
            if (char.IsUpper(text[i]) && !char.IsUpper(text[i - 1])) builder.Append(' ');
            builder.Append(text[i]);
        }
        return builder.ToString();
    }

    private static string Describe(Type type, MethodInfo method, ParameterInfo[] parameters, bool returnsVoid)
    {
        string args = string.Join(", ", parameters.Select((p, i) => $"{CSharpName(p.ParameterType)} {p.Name ?? "arg" + i}"));
        string result = returnsVoid ? "void" : CSharpName(method.ReturnType);
        return $"{type.Name}.{method.Name}({args}) -> {result}";
    }

    private static string Label(string? name, int index)
    {
        if (string.IsNullOrWhiteSpace(name)) return "Arg " + (index + 1);
        var builder = new StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (i == 0) builder.Append(char.ToUpperInvariant(c));
            else if (char.IsUpper(c)) builder.Append(' ').Append(c);
            else builder.Append(c);
        }
        return builder.ToString();
    }

    /// <summary>The pin kind a CLR type can cross a wire as, or null when it cannot.</summary>
    public static ScriptPinKind? KindOf(Type type)
    {
        if (type.IsEnum) return ScriptPinKind.String;
        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal)) return ScriptPinKind.Float;
        if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte) ||
            type == typeof(uint) || type == typeof(ulong) || type == typeof(ushort) || type == typeof(sbyte))
            return ScriptPinKind.Int;
        if (type == typeof(bool)) return ScriptPinKind.Bool;
        if (type == typeof(string)) return ScriptPinKind.String;
        if (type == typeof(Float2)) return ScriptPinKind.Vector2;
        if (type == typeof(Float3)) return ScriptPinKind.Vector3;
        if (type == typeof(Color)) return ScriptPinKind.Color;
        if (type == typeof(Quaternion)) return ScriptPinKind.Quaternion;
        if (type == typeof(GameObject)) return ScriptPinKind.GameObject;
        return null;
    }

    /// <summary>A fully qualified C# name, so generated code never depends on a using directive.</summary>
    public static string CSharpName(Type type)
    {
        if (type.IsEnum) return "global::" + (type.FullName ?? type.Name).Replace('+', '.');
        if (type == typeof(void)) return "void";
        if (type == typeof(float)) return "float";
        if (type == typeof(double)) return "double";
        if (type == typeof(decimal)) return "decimal";
        if (type == typeof(int)) return "int";
        if (type == typeof(long)) return "long";
        if (type == typeof(short)) return "short";
        if (type == typeof(byte)) return "byte";
        if (type == typeof(uint)) return "uint";
        if (type == typeof(ulong)) return "ulong";
        if (type == typeof(ushort)) return "ushort";
        if (type == typeof(sbyte)) return "sbyte";
        if (type == typeof(bool)) return "bool";
        if (type == typeof(string)) return "string";
        return "global::" + (type.FullName ?? type.Name).Replace('+', '.');
    }
}
