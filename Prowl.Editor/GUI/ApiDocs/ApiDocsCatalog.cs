// This file is part of the Infinity Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

using Infinity.Runtime;

namespace Infinity.Editor.GUI.ApiDocs;

/// <summary>Top-level grouping used by the Códigos window's category list.</summary>
public enum ApiDocCategory
{
    All,
    Api,
    Function,
    Method,
    Variable,
    Property,
    Event,
    Type,
    Component,
    Enum,
    Constructor,
    Services,
    Dik,
}

/// <summary>One documented type or member shown in the Códigos window.</summary>
public sealed class ApiDocEntry
{
    public string Name = string.Empty;
    public string QualifiedName = string.Empty;
    public string OwnerFullName = string.Empty;
    public string Namespace = string.Empty;
    public string Assembly = string.Empty;
    public string Kind = string.Empty;
    public string Signature = string.Empty;
    public string Summary = string.Empty;
    public string SearchText = string.Empty;

    private string? _details;
    private string? _example;

    /// <summary>Long-form details, built the first time the entry is selected.</summary>
    public string Details
    {
        get => _details ??= DetailsFactory?.Invoke() ?? string.Empty;
        set => _details = value;
    }

    /// <summary>Copyable usage example, built the first time the entry is selected.</summary>
    public string Example
    {
        get => _example ??= ExampleFactory?.Invoke() ?? string.Empty;
        set => _example = value;
    }

    internal Func<string>? DetailsFactory;
    internal Func<string>? ExampleFactory;

    public ApiDocCategory Category;
    public bool IsPublic = true;
    public bool IsType;
    public Type? Type;
    public MemberInfo? Member;
}

/// <summary>
/// Builds the searchable documentation index from the engine's loaded public API. Summaries and examples come from
/// the XML documentation files emitted next to each assembly; anything without XML docs still gets a generated
/// signature, explanation and usage example so no public API is invisible in the window.
/// </summary>
public static class ApiDocsCatalog
{
    private const BindingFlags DeclaredMembers =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly ConcurrentDictionary<Assembly, XmlDocFile> s_xml = [];
    private static readonly object s_buildLock = new();

    // Anthology libraries keep their own assembly names, so they need an explicit opt-in; System and third-party
    // packages do not, which keeps the index focused on code that ships with the engine.
    private static readonly HashSet<string> s_engineLibraries = new(StringComparer.Ordinal)
    {
        "Aperture", "Clay", "Echo", "Motion", "Origami", "Paper", "Photonic", "Quill", "Quire",
        "Rosetta", "Scaffold", "Scribe", "Unwrapper", "Vector",
    };
    private static readonly string[] s_eagerLibraries = ["Prowl.Services", "Prowl.DIK"];
    private static List<ApiDocEntry> s_entries = [];
    private static bool s_includeNonPublic;
    private static bool s_built;
    private static volatile bool s_stale;
    private static int s_buildVersion;
    private static int s_building;
    private static int s_backgroundRunning;
    private static int s_assemblyCount;

    static ApiDocsCatalog()
    {
        // The engine references these libraries but does not touch them at startup, so the CLR may not have
        // loaded them when the window first asks for entries. Load them once, then let AssemblyLoad invalidate.
        AppDomain.CurrentDomain.AssemblyLoad += (_, args) =>
        {
            // Only a new engine assembly changes the index. Lazy framework/package loads happen while an
            // example is generated and must not throw the whole catalog away.
            if (Volatile.Read(ref s_building) != 0) return;
            if (s_built && IsEngineAssembly(args.LoadedAssembly)) Invalidate();
        };
        foreach (string name in s_eagerLibraries)
        {
            try { Assembly.Load(new AssemblyName(name)); } catch { }
        }
    }

    public static int AssemblyCount => s_assemblyCount;

    /// <summary>True once at least one complete index is available.</summary>
    public static bool IsReady => s_entries.Count > 0;

    /// <summary>True while a background rebuild is running.</summary>
    public static bool IsBuilding => Volatile.Read(ref s_building) != 0 || Volatile.Read(ref s_backgroundRunning) != 0;

    public static void Invalidate()
    {
        Interlocked.Increment(ref s_buildVersion);
        s_stale = true;
        s_built = false;
        s_assemblyCount = 0;
    }

    /// <summary>
    /// Returns the index. With <paramref name="backgroundBuild"/> the first call starts a worker and returns the
    /// previous index immediately, so opening Códigos never blocks the editor frame.
    /// </summary>
    public static IReadOnlyList<ApiDocEntry> GetEntries(bool includeNonPublic, bool backgroundBuild = false)
    {
        bool needsBuild = !s_built || s_stale || s_includeNonPublic != includeNonPublic;
        if (!needsBuild && s_entries.Count > 0)
            return s_entries;

        if (backgroundBuild)
        {
            StartBackgroundBuild(includeNonPublic);
            return s_entries;
        }

        Build(includeNonPublic);
        return s_entries;
    }

    private static void StartBackgroundBuild(bool includeNonPublic)
    {
        if (Interlocked.CompareExchange(ref s_backgroundRunning, 1, 0) != 0) return;

        _ = Task.Run(() =>
        {
            try { Build(includeNonPublic); }
            catch (Exception e) { Debug.LogError($"API docs build failed: {e.Message}"); }
            finally { Volatile.Write(ref s_backgroundRunning, 0); }
        });
    }

    private static bool IsEngineAssembly(Assembly assembly)
    {
        if (assembly.IsDynamic) return false;

        string name = assembly.GetName().Name ?? string.Empty;
        if (name.Contains(".Test", StringComparison.OrdinalIgnoreCase)) return false;
        if (name.Contains("Analyzer", StringComparison.OrdinalIgnoreCase)) return false;
        if (name.Contains("SourceGenerator", StringComparison.OrdinalIgnoreCase)) return false;
        return name.StartsWith("Prowl.", StringComparison.Ordinal) || s_engineLibraries.Contains(name);
    }

    private static void Build(bool includeNonPublic)
    {
        Interlocked.Increment(ref s_building);
        try
        {
            lock (s_buildLock)
            {
                int version = Volatile.Read(ref s_buildVersion);
                List<Assembly> assemblies = [.. AppDomain.CurrentDomain.GetAssemblies()
                    .Where(IsEngineAssembly)
                    .OrderBy(a => a.GetName().Name, StringComparer.OrdinalIgnoreCase)];

                var xml = new ConcurrentDictionary<Assembly, XmlDocFile>();
                var perAssembly = new ConcurrentBag<List<ApiDocEntry>>();
                int workers = Math.Clamp(Environment.ProcessorCount, 1, 4);

                Parallel.ForEach(assemblies, new ParallelOptions { MaxDegreeOfParallelism = workers }, assembly =>
                {
                    XmlDocFile docs = XmlDocFile.Load(assembly);
                    xml[assembly] = docs;
                    var entries = new List<ApiDocEntry>();
                    foreach (Type type in RuntimeUtils.GetLoadableTypes(assembly))
                    {
                        try { AddType(assembly, type, docs, includeNonPublic, entries); }
                        catch { /* A single malformed type must not hide every other API. */ }
                    }
                    perAssembly.Add(entries);
                });

                if (version != Volatile.Read(ref s_buildVersion)) return;

                s_entries = [.. perAssembly
                    .SelectMany(list => list)
                    .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(entry => entry.QualifiedName, StringComparer.OrdinalIgnoreCase)];
                s_xml.Clear();
                foreach ((Assembly assembly, XmlDocFile docs) in xml)
                    s_xml[assembly] = docs;
                s_includeNonPublic = includeNonPublic;
                s_assemblyCount = assemblies.Count;
                s_stale = false;
                s_built = true;
            }
        }
        finally
        {
            Interlocked.Decrement(ref s_building);
        }
    }

    private static void AddType(Assembly assembly, Type type, XmlDocFile docs, bool includeNonPublic,
        List<ApiDocEntry> entries)
    {
        if (type.FullName == null || type.Name.StartsWith("<", StringComparison.Ordinal)) return;
        if (!includeNonPublic && !IsVisible(type)) return;
        if (type.IsGenericTypeDefinition && type.Name.Contains("<>", StringComparison.Ordinal)) return;

        bool isPublic = IsVisible(type);
        ApiDocCategory category = type.IsEnum
            ? ApiDocCategory.Enum
            : typeof(Component).IsAssignableFrom(type) ? ApiDocCategory.Component : ApiDocCategory.Api;

        XmlDocEntry? typeDoc = docs.Find(type);
        string summary = typeDoc?.Summary ?? string.Empty;
        if (summary.Length == 0) summary = FallbackSummary(type, category);

        var entry = new ApiDocEntry
        {
            Name = DisplayName(type),
            QualifiedName = type.FullName,
            OwnerFullName = type.DeclaringType?.FullName ?? string.Empty,
            Namespace = type.Namespace ?? "(global)",
            Assembly = assembly.GetName().Name ?? string.Empty,
            Kind = KindOf(type),
            Signature = SignatureOf(type),
            Summary = summary,
            Category = category,
            IsPublic = isPublic,
            IsType = true,
            Type = type,
        };
        entry.DetailsFactory = () => DetailsOf(entry, typeDoc);
        entry.ExampleFactory = () => !string.IsNullOrWhiteSpace(typeDoc?.Example)
            ? typeDoc!.Example
            : ApiDocsExamples.For(entry) ?? GeneratedExample(entry);
        entry.SearchText = SearchTextOf(entry);
        entries.Add(entry);

        foreach (ConstructorInfo constructor in type.GetConstructors(DeclaredMembers))
        {
            if (!includeNonPublic && !constructor.IsPublic) continue;
            AddMember(assembly, type, constructor, ApiDocCategory.Constructor, "Construtor", docs, includeNonPublic, entries);
        }

        foreach (MethodInfo method in type.GetMethods(DeclaredMembers))
        {
            if (method.IsSpecialName) continue;
            if (!includeNonPublic && !method.IsPublic) continue;
            ApiDocCategory categoryOfMember = method.IsStatic ? ApiDocCategory.Function : ApiDocCategory.Method;
            string kind = method.IsStatic ? "Função" : "Método";
            AddMember(assembly, type, method, categoryOfMember, kind, docs, includeNonPublic, entries);
        }

        foreach (PropertyInfo property in type.GetProperties(DeclaredMembers))
        {
            bool visible = (property.GetMethod?.IsPublic ?? false) || (property.SetMethod?.IsPublic ?? false);
            if (!includeNonPublic && !visible) continue;
            AddMember(assembly, type, property, ApiDocCategory.Property, "Propriedade", docs, includeNonPublic, entries);
        }

        foreach (FieldInfo field in type.GetFields(DeclaredMembers))
        {
            if (field.Name.Contains("k__BackingField", StringComparison.Ordinal)) continue;
            if (!includeNonPublic && !field.IsPublic) continue;
            AddMember(assembly, type, field, ApiDocCategory.Variable, "Variável", docs, includeNonPublic, entries);
        }

        foreach (EventInfo @event in type.GetEvents(DeclaredMembers))
        {
            bool visible = @event.AddMethod?.IsPublic ?? false;
            if (!includeNonPublic && !visible) continue;
            AddMember(assembly, type, @event, ApiDocCategory.Event, "Evento", docs, includeNonPublic, entries);
        }
    }

    private static void AddMember(Assembly assembly, Type owner, MemberInfo member, ApiDocCategory category,
        string kind, XmlDocFile docs, bool includeNonPublic, List<ApiDocEntry> entries)
    {
        bool isPublic = member switch
        {
            MethodBase method => method.IsPublic,
            FieldInfo field => field.IsPublic,
            PropertyInfo property => property.GetMethod?.IsPublic == true || property.SetMethod?.IsPublic == true,
            EventInfo @event => @event.AddMethod?.IsPublic == true,
            _ => true,
        };
        if (!includeNonPublic && !isPublic) return;

        XmlDocEntry? doc = docs.Find(member);
        string summary = doc?.Summary ?? string.Empty;
        if (summary.Length == 0) summary = FallbackSummary(member, owner, category);

        var entry = new ApiDocEntry
        {
            Name = member.Name,
            QualifiedName = owner.FullName + "." + member.Name,
            OwnerFullName = owner.FullName ?? string.Empty,
            Namespace = owner.Namespace ?? "(global)",
            Assembly = assembly.GetName().Name ?? string.Empty,
            Kind = kind,
            Signature = SignatureOf(member),
            Summary = summary,
            Category = category,
            IsPublic = isPublic,
            IsType = false,
            Member = member,
        };
        entry.DetailsFactory = () => DetailsOf(entry, doc);
        entry.ExampleFactory = () => !string.IsNullOrWhiteSpace(doc?.Example)
            ? doc!.Example
            : ApiDocsExamples.For(entry) ?? GeneratedExample(entry);
        entry.SearchText = SearchTextOf(entry);
        entries.Add(entry);
    }

    private static string SearchTextOf(ApiDocEntry entry)
        => string.Join(" ", entry.Name, entry.QualifiedName, entry.Namespace, entry.Assembly, entry.Kind,
            entry.Signature, entry.Summary, entry.Category.ToString()).ToLowerInvariant();

    private static string DisplayName(Type type)
    {
        string name = type.Name;
        int tick = name.IndexOf('`');
        return tick >= 0 ? name[..tick] : name;
    }

    private static bool IsVisible(Type type)
    {
        if (type.IsNested)
        {
            bool nestedVisible = type.IsNestedPublic || type.IsNestedFamily || type.IsNestedFamORAssem;
            return nestedVisible && type.DeclaringType != null && IsVisible(type.DeclaringType);
        }
        return type.IsPublic;
    }

    private static string KindOf(Type type)
    {
        if (type.IsEnum) return "Enum";
        if (type.IsInterface) return "Interface";
        if (typeof(Delegate).IsAssignableFrom(type)) return "Delegado";
        if (type.IsValueType) return "Struct";
        return "Classe";
    }

    private static string SignatureOf(Type type)
    {
        var sb = new StringBuilder();
        if (type.IsAbstract && type.IsSealed) sb.Append("static ");
        else if (type.IsAbstract && !type.IsInterface) sb.Append("abstract ");
        else if (type.IsSealed && !type.IsValueType) sb.Append("sealed ");

        sb.Append(KindOf(type).ToLowerInvariant()).Append(' ').Append(DisplayName(type));

        if (type.IsGenericTypeDefinition)
        {
            Type[] args = type.GetGenericArguments();
            sb.Append('<').Append(string.Join(", ", args.Select(a => a.Name))).Append('>');
        }

        var bases = new List<string>();
        if (type.BaseType != null && type.BaseType != typeof(object) && !type.IsEnum && !type.IsValueType)
            bases.Add(FriendlyTypeName(type.BaseType));
        try
        {
            foreach (Type iface in type.GetInterfaces())
                bases.Add(FriendlyTypeName(iface));
        }
        catch { }
        if (bases.Count > 0) sb.Append(" : ").Append(string.Join(", ", bases));
        return sb.ToString();
    }

    private static string SignatureOf(MemberInfo member)
    {
        switch (member)
        {
            case ConstructorInfo constructor:
            {
                string parameters = string.Join(", ", constructor.GetParameters()
                    .Select(p => $"{FriendlyTypeName(p.ParameterType)} {p.Name}"));
                return $"{Visibility(constructor)} {DisplayName(constructor.DeclaringType!)} ({parameters})";
            }
            case MethodInfo method:
            {
                string generic = method.IsGenericMethodDefinition
                    ? "<" + string.Join(", ", method.GetGenericArguments().Select(a => a.Name)) + ">"
                    : string.Empty;
                string parameters = string.Join(", ", method.GetParameters()
                    .Select(p => $"{FriendlyTypeName(p.ParameterType)} {p.Name}"));
                return $"{Modifiers(method)} {FriendlyTypeName(method.ReturnType)} {method.Name}{generic}({parameters})";
            }
            case PropertyInfo property:
            {
                MethodInfo? accessor = property.GetMethod ?? property.SetMethod;
                string accessors = (property.GetMethod != null ? " get;" : string.Empty)
                                 + (property.SetMethod != null ? " set;" : string.Empty);
                return $"{Visibility(accessor)} {Modifiers(accessor, includeVisibility: false)}"
                     + $"{FriendlyTypeName(property.PropertyType)} {property.Name} {{{accessors} }}";
            }
            case FieldInfo field:
            {
                string mods = field.IsLiteral ? "const " : field.IsStatic ? "static " : string.Empty;
                if (field.IsInitOnly) mods += "readonly ";
                return $"{Visibility(field)} {mods}{FriendlyTypeName(field.FieldType)} {field.Name}";
            }
            case EventInfo @event:
                return $"{Visibility(@event.AddMethod)} event {FriendlyTypeName(@event.EventHandlerType!)} {@event.Name}";
            default:
                return member.Name;
        }
    }

    private static string Modifiers(MethodBase method, bool includeVisibility = true)
    {
        var sb = new StringBuilder();
        if (includeVisibility) sb.Append(Visibility(method)).Append(' ');
        if (method.IsStatic) sb.Append("static ");
        if (method.IsAbstract) sb.Append("abstract ");
        else if (method.IsVirtual && !method.IsFinal) sb.Append("virtual ");
        return sb.ToString().TrimEnd();
    }

    private static string Visibility(MethodBase? method)
        => method == null ? "public"
         : method.IsPublic ? "public"
         : method.IsFamily ? "protected"
         : method.IsFamilyOrAssembly ? "protected internal"
         : method.IsFamilyAndAssembly ? "private protected"
         : method.IsAssembly ? "internal"
         : "private";

    private static string Visibility(FieldInfo field)
        => field.IsPublic ? "public"
         : field.IsFamily ? "protected"
         : field.IsFamilyOrAssembly ? "protected internal"
         : field.IsFamilyAndAssembly ? "private protected"
         : field.IsAssembly ? "internal"
         : "private";

    private static string FriendlyTypeName(Type type)
    {
        Type? nullable = Nullable.GetUnderlyingType(type);
        if (nullable != null) return FriendlyTypeName(nullable) + "?";

        if (type.IsByRef) return "ref " + FriendlyTypeName(type.GetElementType()!);
        if (type.IsArray) return FriendlyTypeName(type.GetElementType()!) + "[]";
        if (type.IsGenericParameter) return type.Name;

        if (type == typeof(void)) return "void";
        if (type == typeof(string)) return "string";
        if (type == typeof(bool)) return "bool";
        if (type == typeof(byte)) return "byte";
        if (type == typeof(sbyte)) return "sbyte";
        if (type == typeof(short)) return "short";
        if (type == typeof(ushort)) return "ushort";
        if (type == typeof(int)) return "int";
        if (type == typeof(uint)) return "uint";
        if (type == typeof(long)) return "long";
        if (type == typeof(ulong)) return "ulong";
        if (type == typeof(float)) return "float";
        if (type == typeof(double)) return "double";
        if (type == typeof(decimal)) return "decimal";
        if (type == typeof(char)) return "char";
        if (type == typeof(object)) return "object";

        if (type.IsGenericType)
        {
            string name = DisplayName(type.GetGenericTypeDefinition());
            return name + "<" + string.Join(", ", type.GetGenericArguments().Select(FriendlyTypeName)) + ">";
        }

        if (type.IsNested && type.DeclaringType != null)
            return DisplayName(type.DeclaringType) + "." + DisplayName(type);

        return DisplayName(type);
    }

    private static string FallbackSummary(Type type, ApiDocCategory category)
    {
        if (category == ApiDocCategory.Component)
            return $"Componente da engine. Adicione ao GameObject com AddComponent<{DisplayName(type)}>() e configure pelo Inspector ou por código.";
        if (type.IsEnum)
            return $"Enumeração {DisplayName(type)}. Use os valores declarados em campos, condições e serialização.";
        return $"{KindOf(type)} {DisplayName(type)} exposto pela API pública da engine.";
    }

    private static string FallbackSummary(MemberInfo member, Type owner, ApiDocCategory category)
        => category switch
        {
            ApiDocCategory.Function => $"Função estática de {DisplayName(owner)}. Chame direto em {DisplayName(owner)}.{member.Name}(...).",
            ApiDocCategory.Method => $"Método de instância de {DisplayName(owner)}. Obtenha uma instância e chame {member.Name}(...).",
            ApiDocCategory.Constructor => $"Cria uma nova instância de {DisplayName(owner)}.",
            ApiDocCategory.Property => $"Propriedade {member.Name} de {DisplayName(owner)}.",
            ApiDocCategory.Variable => $"Campo {member.Name} de {DisplayName(owner)}.",
            ApiDocCategory.Event => $"Evento {member.Name} de {DisplayName(owner)}. Inscreva com += e remova com -=.",
            _ => $"Membro {member.Name} de {DisplayName(owner)}.",
        };

    private static string DetailsOf(ApiDocEntry entry, XmlDocEntry? doc)
    {
        var lines = new List<string>
        {
            $"Namespace: {entry.Namespace}",
            $"Assembly: {entry.Assembly}",
        };

        if (entry.Type is { } type)
        {
            if (type.BaseType != null && type.BaseType != typeof(object))
                lines.Add($"Base: {FriendlyTypeName(type.BaseType)}");
            try
            {
                Type[] interfaces = type.GetInterfaces();
                if (interfaces.Length > 0)
                    lines.Add($"Interfaces: {string.Join(", ", interfaces.Select(FriendlyTypeName))}");
            }
            catch { }
        }

        if (entry.Member is MethodInfo method)
        {
            lines.Add($"Retorno: {FriendlyTypeName(method.ReturnType)}");
            AddParameters(lines, method.GetParameters(), doc);
        }
        else if (entry.Member is ConstructorInfo constructor)
        {
            AddParameters(lines, constructor.GetParameters(), doc);
        }
        else if (entry.Member is PropertyInfo property)
        {
            lines.Add($"Tipo: {FriendlyTypeName(property.PropertyType)}");
            if (property.CanRead) lines.Add("Leitura: sim");
            if (property.CanWrite) lines.Add("Escrita: sim");
        }
        else if (entry.Member is FieldInfo field)
        {
            lines.Add($"Tipo: {FriendlyTypeName(field.FieldType)}");
            if (field.IsLiteral) lines.Add("Constante: sim");
            if (field.IsStatic) lines.Add("Estático: sim");
        }
        else if (entry.Member is EventInfo @event)
        {
            lines.Add($"Handler: {FriendlyTypeName(@event.EventHandlerType!)}");
        }

        if (!string.IsNullOrWhiteSpace(doc?.Returns))
            lines.Add($"Retorno documentado: {doc!.Returns}");

        if (!string.IsNullOrWhiteSpace(doc?.Remarks))
        {
            lines.Add(string.Empty);
            lines.Add(doc!.Remarks);
        }

        lines.Add(string.Empty);
        lines.Add("Como funciona:");
        lines.Add(HowItWorks(entry));
        return string.Join("\n", lines);
    }

    private static void AddParameters(List<string> lines, ParameterInfo[] parameters, XmlDocEntry? doc)
    {
        if (parameters.Length == 0) return;
        lines.Add("Parâmetros:");
        foreach (ParameterInfo parameter in parameters)
        {
            string description = doc != null && doc.Parameters.TryGetValue(parameter.Name ?? string.Empty, out string? text)
                ? " - " + text
                : string.Empty;
            lines.Add($"  {FriendlyTypeName(parameter.ParameterType)} {parameter.Name}{description}");
        }
    }

    private static string HowItWorks(ApiDocEntry entry)
    {
        if (entry.IsType)
        {
            if (entry.Category == ApiDocCategory.Component)
                return "Componentes vivem em um GameObject. Use GameObject.AddComponent<T>() para criar, "
                     + "GetComponent<T>() para obter e Destroy(component) para remover. O ciclo de vida chama "
                     + "Awake, OnEnable, Start, Update, OnDisable e OnDestroy.";
            if (entry.Category == ApiDocCategory.Enum)
                return "Valores de enumeração são constantes nomeadas. Use-os em campos, comparações e switches; "
                     + "o Inspector mostra um dropdown para campos desse tipo.";
            return "Este tipo faz parte da API pública da engine. Crie uma instância com new ou use seus membros "
                 + "estáticos; os tipos de engine normalmente são acessados por componentes e assets.";
        }

        return entry.Category switch
        {
            ApiDocCategory.Function => "É estático: não precisa de instância. Chame direto pelo nome do tipo.",
            ApiDocCategory.Method => "É de instância: obtenha o objeto primeiro e chame o método nele. "
                                   + "Em componentes, use a instância atual (this) ou GetComponent<T>().",
            ApiDocCategory.Constructor => "Use new para criar o objeto e passe os argumentos mostrados na assinatura.",
            ApiDocCategory.Property => "Leia ou atribua a propriedade como um campo. Propriedades podem validar, "
                                     + "converter ou disparar eventos ao serem alteradas.",
            ApiDocCategory.Variable => "Acesse o campo pela instância (ou pelo tipo, quando estático). "
                                     + "Campos públicos aparecem no Inspector quando o objeto é serializado.",
            ApiDocCategory.Event => "Inscreva um handler com += e remova com -=. O evento é disparado pela engine "
                                  + "quando a condição correspondente acontece.",
            _ => "Use o membro conforme a assinatura mostrada acima.",
        };
    }

    private static string GeneratedExample(ApiDocEntry entry)
    {
        string typeName = entry.IsType ? entry.QualifiedName : entry.OwnerFullName;
        string usingLine = UsingFor(entry.Namespace);

        if (entry.IsType)
        {
            if (entry.Category == ApiDocCategory.Component)
                return $"{usingLine}var go = new GameObject(\"Object\");\nvar component = go.AddComponent<{typeName}>();";

            if (entry.Category == ApiDocCategory.Enum)
                return $"{usingLine}var value = {typeName}.{FirstEnumName(entry.Type)};\n// Compare with == or use it in a switch.";

            if (entry.Type is { } type)
            {
                if (type.IsInterface || type.IsAbstract)
                    return $"{usingLine}// {typeName} is abstract. Create or receive a concrete implementation.\n{typeName} value = /* implementation */;";
                if (type.IsValueType)
                    return $"{usingLine}var value = new {typeName}();\n// Fill the fields or use the constructor shown in the signature.";
                if (type.IsGenericTypeDefinition)
                    return $"{usingLine}var value = new {typeName}<object>();";
            }

            return $"{usingLine}var value = new {typeName}();";
        }

        string instance = "component";
        if (entry.Member is MethodInfo method)
        {
            string target = method.IsStatic ? typeName : instance;
            string args = string.Join(", ", method.GetParameters().Select(p => Placeholder(p.ParameterType, p.Name)));
            string call = $"{target}.{method.Name}({args})";
            string body = method.ReturnType == typeof(void)
                ? $"{usingLine}{call};"
                : $"{usingLine}var result = {call};";
            if (!method.IsStatic)
                body = $"// component is a {typeName} obtained with GetComponent<{typeName}>().\n" + body;
            return body;
        }

        if (entry.Member is ConstructorInfo constructor)
        {
            string args = string.Join(", ", constructor.GetParameters().Select(p => Placeholder(p.ParameterType, p.Name)));
            return $"{usingLine}var value = new {typeName}({args});";
        }

        if (entry.Member is PropertyInfo property)
        {
            bool isStatic = property.GetMethod?.IsStatic == true || property.SetMethod?.IsStatic == true;
            string target = isStatic ? typeName : instance;
            string body = $"{usingLine}var value = {target}.{property.Name};";
            if (property.CanWrite) body += $"\n{target}.{property.Name} = value;";
            if (!isStatic) body = $"// component is a {typeName} obtained with GetComponent<{typeName}>().\n" + body;
            return body;
        }

        if (entry.Member is FieldInfo field)
        {
            string target = field.IsStatic ? typeName : instance;
            string body = $"{usingLine}var value = {target}.{field.Name};";
            if (!field.IsStatic && !field.IsInitOnly) body += $"\n{target}.{field.Name} = value;";
            if (!field.IsStatic) body = $"// component is a {typeName} obtained with GetComponent<{typeName}>().\n" + body;
            return body;
        }

        if (entry.Member is EventInfo @event)
            return $"{usingLine}// component is a {typeName} obtained with GetComponent<{typeName}>().\n"
                 + $"component.{@event.Name} += OnEvent;\n"
                 + "// ...\n"
                 + $"component.{@event.Name} -= OnEvent;\n"
                 + "// OnEvent must match the event handler signature shown above.";

        return $"// {typeName}.{entry.Name}";
    }

    private static string UsingFor(string ns)
        => string.IsNullOrWhiteSpace(ns) || ns == "(global)" || ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal)
            ? string.Empty
            : $"using {ns};\n";

    private static string Placeholder(Type type, string? name)
    {
        string label = string.IsNullOrWhiteSpace(name) ? "value" : name;
        Type? nullable = Nullable.GetUnderlyingType(type);
        if (nullable != null) return Placeholder(nullable, name);

        if (type == typeof(string)) return $"\"{label}\"";
        if (type == typeof(bool)) return "true";
        if (type == typeof(char)) return "'a'";
        if (type == typeof(float)) return "0f";
        if (type == typeof(double)) return "0d";
        if (type == typeof(decimal)) return "0m";
        if (type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort)
            || type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong))
            return "0";
        if (type == typeof(void)) return string.Empty;
        if (type.IsEnum) return $"{FriendlyTypeName(type)}.{FirstEnumName(type)}";
        if (type == typeof(Infinity.Vector.Float2)) return "new Float2(0, 0)";
        if (type == typeof(Infinity.Vector.Float3)) return "new Float3(0, 0, 0)";
        if (type == typeof(Infinity.Vector.Color)) return "Color.White";
        if (type == typeof(GameObject)) return "new GameObject(\"Object\")";
        if (type == typeof(Component)) return "component";
        if (type.IsArray) return $"new {FriendlyTypeName(type.GetElementType()!)}(0)";
        if (type.IsValueType) return $"default({FriendlyTypeName(type)})";
        return "null";
    }

    private static string FirstEnumName(Type? type)
    {
        if (type == null) return "Value";
        string? name = Enum.GetNames(type).FirstOrDefault();
        return name ?? "Value";
    }

    private sealed record XmlDocEntry(string Summary, string Remarks, string Example, string Returns,
        IReadOnlyDictionary<string, string> Parameters);

    /// <summary>Reads the XML documentation that ships beside an assembly, when that project generates one.</summary>
    private sealed class XmlDocFile
    {
        private readonly Dictionary<string, XmlDocEntry> _byKey = new(StringComparer.Ordinal);
        private readonly Dictionary<string, XmlDocEntry> _byName = new(StringComparer.Ordinal);

        public static XmlDocFile Load(Assembly assembly)
        {
            var file = new XmlDocFile();
            try
            {
                string path = assembly.Location;
                if (string.IsNullOrEmpty(path)) return file;
                path = Path.ChangeExtension(path, ".xml");
                if (!File.Exists(path)) return file;

                XDocument document = XDocument.Load(path);
                foreach (XElement member in document.Descendants("member"))
                {
                    string? key = member.Attribute("name")?.Value;
                    if (string.IsNullOrWhiteSpace(key)) continue;

                    var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (XElement parameter in member.Elements("param"))
                    {
                        string? name = parameter.Attribute("name")?.Value;
                        if (!string.IsNullOrWhiteSpace(name)) parameters[name!] = Normalize(parameter);
                    }

                    var entry = new XmlDocEntry(
                        Normalize(member.Element("summary")),
                        Normalize(member.Element("remarks")),
                        NormalizeCode(member.Element("example")),
                        Normalize(member.Element("returns")),
                        parameters);
                    file._byKey[key!] = entry;
                    file._byName[NameKey(key!)] = entry;
                }
            }
            catch { /* Documentation is optional; reflection still builds the entry. */ }
            return file;
        }

        public XmlDocEntry? Find(MemberInfo member)
        {
            string key = KeyOf(member);
            if (_byKey.TryGetValue(key, out XmlDocEntry? exact)) return exact;

            string nameKey = NameKey(key);
            return _byName.TryGetValue(nameKey, out XmlDocEntry? named) ? named : null;
        }

        private static string NameKey(string key)
        {
            int bracket = key.IndexOf('(');
            return bracket >= 0 ? key[..bracket] : key;
        }

        private static string KeyOf(MemberInfo member)
        {
            string owner = member.DeclaringType?.FullName ?? string.Empty;
            switch (member)
            {
                case Type type:
                    return "T:" + type.FullName;
                case ConstructorInfo constructor:
                    return "M:" + owner + ".#ctor(" + string.Join(",", constructor.GetParameters().Select(p => XmlTypeName(p.ParameterType))) + ")";
                case MethodInfo method:
                {
                    string name = method.Name;
                    if (method.IsGenericMethod) name += "``" + method.GetGenericArguments().Length;
                    return "M:" + owner + "." + name + "(" + string.Join(",", method.GetParameters().Select(p => XmlTypeName(p.ParameterType))) + ")";
                }
                case PropertyInfo property:
                    return "P:" + owner + "." + property.Name;
                case FieldInfo field:
                    return "F:" + owner + "." + field.Name;
                case EventInfo @event:
                    return "E:" + owner + "." + @event.Name;
                default:
                    return string.Empty;
            }
        }

        private static string XmlTypeName(Type type)
        {
            if (type.IsByRef) return XmlTypeName(type.GetElementType()!) + "@";
            if (type.IsArray) return XmlTypeName(type.GetElementType()!) + "[]";
            if (type.IsGenericType)
            {
                string name = type.GetGenericTypeDefinition().FullName ?? type.Name;
                int tick = name.IndexOf('`');
                if (tick >= 0) name = name[..tick];
                return name + "{" + string.Join(",", type.GetGenericArguments().Select(XmlTypeName)) + "}";
            }
            return type.FullName ?? type.Name;
        }

        private static string Normalize(XElement? element)
        {
            if (element == null) return string.Empty;
            var sb = new StringBuilder();
            Append(element, sb);
            return Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
        }

        private static string NormalizeCode(XElement? element)
        {
            if (element == null) return string.Empty;
            var sb = new StringBuilder();
            Append(element, sb);
            string[] lines = sb.ToString().Replace("\r", string.Empty).Split('\n')
                .Select(line => line.TrimEnd())
                .ToArray();
            int first = 0, last = lines.Length - 1;
            while (first <= last && string.IsNullOrWhiteSpace(lines[first])) first++;
            while (last >= first && string.IsNullOrWhiteSpace(lines[last])) last--;
            if (last < first) return string.Empty;
            return string.Join("\n", lines[first..(last + 1)]);
        }

        private static void Append(XElement element, StringBuilder sb)
        {
            foreach (XNode node in element.Nodes())
            {
                if (node is XText text)
                {
                    sb.Append(text.Value);
                    continue;
                }
                if (node is not XElement child) continue;

                switch (child.Name.LocalName)
                {
                    case "see":
                    case "seealso":
                        sb.Append(ShortCref(child.Attribute("cref")?.Value));
                        break;
                    case "paramref":
                        sb.Append(child.Attribute("name")?.Value ?? string.Empty);
                        break;
                    case "c":
                        sb.Append(child.Value);
                        break;
                    case "code":
                        sb.Append('\n').Append(child.Value).Append('\n');
                        break;
                    case "para":
                        sb.Append('\n');
                        Append(child, sb);
                        sb.Append('\n');
                        break;
                    default:
                        Append(child, sb);
                        break;
                }
            }
        }

        private static string ShortCref(string? cref)
        {
            if (string.IsNullOrWhiteSpace(cref)) return string.Empty;
            int colon = cref.IndexOf(':');
            string name = colon >= 0 ? cref[(colon + 1)..] : cref;
            int bracket = name.IndexOf('(');
            if (bracket >= 0) name = name[..bracket];
            int lastDot = name.LastIndexOf('.');
            return lastDot >= 0 ? name[(lastDot + 1)..] : name;
        }
    }
}
