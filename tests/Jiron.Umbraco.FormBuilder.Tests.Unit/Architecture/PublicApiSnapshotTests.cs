using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Web.Rendering;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Architecture;

/// <summary>
/// The public surface a site's code builds on (ROADMAP 15.1, section 4.9, ADR-0233): every public type and every public
/// or protected member of the packages' assemblies, with its nullability, as <c>docs/api/&lt;assembly&gt;.api.txt</c>
/// holds it. A change to it fails here until it is reviewed: run with <c>JFB_UPDATE_SNAPSHOTS=1</c>, which writes the
/// files and fails, and review the diff as part of the change, with its deprecation where it breaks (D21).
/// </summary>
public sealed class PublicApiSnapshotTests
{
    private static readonly NullabilityInfoContext Nullability = new();

    private static readonly Dictionary<Type, string> Keywords = new()
    {
        [typeof(void)] = "void",
        [typeof(object)] = "object",
        [typeof(string)] = "string",
        [typeof(bool)] = "bool",
        [typeof(byte)] = "byte",
        [typeof(char)] = "char",
        [typeof(short)] = "short",
        [typeof(int)] = "int",
        [typeof(long)] = "long",
        [typeof(float)] = "float",
        [typeof(double)] = "double",
        [typeof(decimal)] = "decimal",
    };

    private static IEnumerable<Assembly> Packages() =>
        [typeof(IFormBuilderEntryService).Assembly, typeof(IFormBuilderRenderer).Assembly];

    [TestCaseSource(nameof(Packages))]
    public async Task ThePublicSurface_IsTheReviewedOne(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var name = assembly.GetName().Name!;
        var path = RepositoryRoot.Path(typeof(PublicApiSnapshotTests).Assembly, "docs", "api", name + ".api.txt");
        var written = Describe(assembly);
        if (Environment.GetEnvironmentVariable("JFB_UPDATE_SNAPSHOTS") == "1")
        {
            await File.WriteAllTextAsync(path, written, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Assert.Fail($"JFB_UPDATE_SNAPSHOTS=1: wrote {path}. Review the diff, and run again without the variable.");
        }

        Assert.That(File.Exists(path), Is.True, $"the reviewed surface {path}; run with JFB_UPDATE_SNAPSHOTS=1");
        var reviewed = (await File.ReadAllTextAsync(path)).ReplaceLineEndings("\n");
        var first = FirstDifference(reviewed, written);
        Assert.That(first, Is.Null, $"the public surface of {name} changed at: {first}. Run with JFB_UPDATE_SNAPSHOTS=1 and review the diff.");
    }

    [Test]
    public void TheListing_NamesNullabilityRequiredMembersAndRecordsMembers()
    {
        var core = Describe(typeof(IFormBuilderEntryService).Assembly);

        Assert.Multiple(() =>
        {
            Assert.That(core, Does.Contain("public interface Jiron.Umbraco.FormBuilder.Core.Entries.IFormBuilderEntryService"));
            Assert.That(core, Does.Contain("  Task<FormBuilderEntry?> GetAsync(Guid entryKey, CancellationToken cancellationToken = default)"));
            Assert.That(core, Does.Contain("  required Guid FormKey { get; init; }"));
            Assert.That(core, Does.Contain("public sealed class Jiron.Umbraco.FormBuilder.Core.Entries.EntrySubmittingNotification : CancelableNotification"));
            Assert.That(core, Does.Not.Contain("FormSubmissions"), "an internal type is never listed");
            Assert.That(core, Does.Not.Contain("<Clone>$"), "nor a compiler's member");
        });
    }

    /// <summary>The listing of an assembly's public surface, one type after another in the order of their names.</summary>
    internal static string Describe(Assembly assembly)
    {
        var text = new StringBuilder();
        foreach (var type in assembly.GetExportedTypes().OrderBy(type => type.FullName, StringComparer.Ordinal))
        {
            text.Append(Header(type)).Append('\n');
            foreach (var line in type.IsEnum ? Members(type) : Members(type).Order(StringComparer.Ordinal))
            {
                text.Append("  ").Append(line).Append('\n');
            }

            text.Append('\n');
        }

        return text.ToString();
    }

    private static string? FirstDifference(string reviewed, string written)
    {
        var a = reviewed.Split('\n');
        var b = written.Split('\n');
        for (var index = 0; index < Math.Max(a.Length, b.Length); index++)
        {
            var left = index < a.Length ? a[index] : "(end)";
            var right = index < b.Length ? b[index] : "(end)";
            if (!string.Equals(left, right, StringComparison.Ordinal))
            {
                return string.Create(CultureInfo.InvariantCulture, $"line {index + 1}: reviewed \"{left}\", now \"{right}\"");
            }
        }

        return null;
    }

    private static string Header(Type type)
    {
        var kind = type switch
        {
            { IsEnum: true } => "enum",
            { IsInterface: true } => "interface",
            { IsValueType: true } when IsRecord(type) => (type.IsDefined(typeof(IsReadOnlyAttribute)) ? "readonly " : string.Empty) + "record struct",
            { IsValueType: true } => (type.IsDefined(typeof(IsReadOnlyAttribute)) ? "readonly " : string.Empty) + "struct",
            _ when type.IsSubclassOf(typeof(Delegate)) => "delegate",
            _ when IsRecord(type) => "record",
            _ => "class",
        };
        var modifiers = type switch
        {
            { IsAbstract: true, IsSealed: true } => "static ",
            { IsInterface: false, IsAbstract: true } => "abstract ",
            { IsValueType: false, IsSealed: true } when !type.IsSubclassOf(typeof(Delegate)) => "sealed ",
            _ => string.Empty,
        };
        var bases = new List<string>();
        if (type.BaseType is { } baseType && baseType != typeof(object) && baseType != typeof(ValueType) && baseType != typeof(Enum) && !type.IsSubclassOf(typeof(Delegate)))
        {
            bases.Add(Name(baseType));
        }

        var inherited = type.BaseType?.GetInterfaces() ?? [];
        bases.AddRange(type.GetInterfaces().Where(face => !inherited.Contains(face) && !IsRecordEquatable(type, face)).Select(face => Name(face)).Order(StringComparer.Ordinal));
        var obsolete = type.GetCustomAttribute<ObsoleteAttribute>() is { } old ? $"[Obsolete(\"{old.Message}\")] " : string.Empty;
        return $"{obsolete}public {modifiers}{kind} {FullName(type)}{(bases.Count == 0 ? string.Empty : " : " + string.Join(", ", bases))}";
    }

    private static IEnumerable<string> Members(Type type)
    {
        const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        if (type.IsEnum)
        {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static).OrderBy(field => Convert.ToInt64(field.GetRawConstantValue(), CultureInfo.InvariantCulture)).ThenBy(field => field.Name, StringComparer.Ordinal))
            {
                yield return string.Create(CultureInfo.InvariantCulture, $"{field.Name} = {Convert.ToInt64(field.GetRawConstantValue(), CultureInfo.InvariantCulture)}");
            }

            yield break;
        }

        if (type.IsSubclassOf(typeof(Delegate)))
        {
            var invoke = type.GetMethod("Invoke")!;
            yield return $"{Name(invoke.ReturnParameter)} Invoke({Parameters(invoke)})";
            yield break;
        }

        foreach (var constructor in type.GetConstructors(Declared).Where(Visible))
        {
            yield return $"{Access(constructor)}ctor({Parameters(constructor)})";
        }

        foreach (var field in type.GetFields(Declared).Where(field => (field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly) && !field.IsSpecialName && !field.Name.Contains('<', StringComparison.Ordinal)))
        {
            var access = field.IsPublic ? string.Empty : "protected ";
            yield return field.IsLiteral
                ? $"{access}const {Name(field.FieldType)} {field.Name} = {Constant(field.GetRawConstantValue())}"
                : $"{access}{(field.IsStatic ? "static " : string.Empty)}{(field.IsInitOnly ? "readonly " : string.Empty)}{Name(field.FieldType, Nullability.Create(field))} {field.Name}";
        }

        foreach (var property in type.GetProperties(Declared))
        {
            var getter = property.GetMethod is { } get && Visible(get) ? get : null;
            var setter = property.SetMethod is { } set && Visible(set) ? set : null;
            if (getter is null && setter is null)
            {
                continue;
            }

            var accessor = getter ?? setter!;
            var init = setter?.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit)) == true;
            var parts = new List<string>();
            if (getter is not null)
            {
                parts.Add(Access(getter, accessor) + "get;");
            }

            if (setter is not null)
            {
                parts.Add(Access(setter, accessor) + (init ? "init;" : "set;"));
            }

            var required = property.IsDefined(typeof(RequiredMemberAttribute)) ? "required " : string.Empty;
            var indexer = property.GetIndexParameters() is { Length: > 0 } index ? $"this[{string.Join(", ", index.Select(Parameter))}]" : property.Name;
            yield return $"{Access(accessor)}{Modifiers(accessor, type)}{required}{Name(property.PropertyType, Nullability.Create(property))} {indexer} {{ {string.Join(" ", parts)} }}";
        }

        foreach (var member in type.GetEvents(Declared).Where(member => member.AddMethod is { } add && Visible(add)))
        {
            yield return $"{Access(member.AddMethod!)}event {Name(member.EventHandlerType!)} {member.Name}";
        }

        foreach (var method in type.GetMethods(Declared).Where(method => Visible(method) && !method.IsSpecialName || method.Name.StartsWith("op_", StringComparison.Ordinal) && method.IsPublic))
        {
            if (method.Name.Contains('<', StringComparison.Ordinal))
            {
                continue;
            }

            var generic = method.IsGenericMethodDefinition ? "<" + string.Join(", ", method.GetGenericArguments().Select(argument => argument.Name)) + ">" : string.Empty;
            yield return $"{Access(method)}{Modifiers(method, type)}{Name(method.ReturnParameter)} {method.Name}{generic}({Parameters(method)})";
        }
    }

    private static bool Visible(MethodBase method) =>
        (method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly) && (method.IsPublic || !method.DeclaringType!.IsSealed);

    private static string Access(MethodBase method, MethodBase? of = null) =>
        of is not null && (method.IsPublic == of.IsPublic) ? string.Empty : method.IsPublic ? string.Empty : "protected ";

    private static string Modifiers(MethodInfo method, Type type) =>
        type.IsInterface
            ? method.IsStatic ? "static " : string.Empty
            : method.IsStatic ? "static "
            : method.IsAbstract ? "abstract "
            : method.IsVirtual && !method.IsFinal ? method.GetBaseDefinition() != method ? "override " : "virtual "
            : method.IsVirtual && method.GetBaseDefinition() != method ? "override "
            : string.Empty;

    private static string Parameters(MethodBase method) => string.Join(", ", method.GetParameters().Select(Parameter));

    private static string Parameter(ParameterInfo parameter)
    {
        var modifier = parameter.IsDefined(typeof(ParamArrayAttribute)) ? "params "
            : parameter.IsOut ? "out "
            : parameter.ParameterType.IsByRef ? parameter.IsIn ? "in " : "ref "
            : string.Empty;
        var value = parameter.HasDefaultValue ? " = " + (parameter.DefaultValue is null && parameter.ParameterType.IsValueType && Nullable.GetUnderlyingType(parameter.ParameterType) is null ? "default" : Constant(parameter.DefaultValue)) : string.Empty;
        return $"{modifier}{Name(parameter)} {parameter.Name}{value}";
    }

    private static string Constant(object? value) => value switch
    {
        null => "default",
        string text => "\"" + text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"",
        bool flag => flag ? "true" : "false",
        char one => "'" + one + "'",
        IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "default",
    };

    private static string Name(ParameterInfo parameter)
    {
        var type = parameter.ParameterType.IsByRef ? parameter.ParameterType.GetElementType()! : parameter.ParameterType;
        return Name(type, Nullability.Create(parameter));
    }

    private static string Name(Type type, NullabilityInfo? nullability = null)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return Name(underlying, nullability?.GenericTypeArguments is [var inner] ? inner : null) + "?";
        }

        var nullable = nullability?.ReadState == NullabilityState.Nullable && !type.IsValueType ? "?" : string.Empty;
        if (type.IsArray)
        {
            return Name(type.GetElementType()!, nullability?.ElementType) + "[" + new string(',', type.GetArrayRank() - 1) + "]" + nullable;
        }

        if (type.IsGenericParameter)
        {
            return type.Name + nullable;
        }

        if (Keywords.TryGetValue(type, out var keyword))
        {
            return keyword + nullable;
        }

        var name = type.IsNested ? Name(type.DeclaringType!) + "." + Plain(type) : Plain(type);
        if (type.IsGenericType)
        {
            var arguments = type.GetGenericArguments();
            var own = type.IsNested ? arguments.Skip(type.DeclaringType!.GetGenericArguments().Length).ToArray() : arguments;
            if (own.Length > 0)
            {
                var infos = nullability?.GenericTypeArguments ?? [];
                var offset = arguments.Length - own.Length;
                name += "<" + string.Join(", ", own.Select((argument, index) => Name(argument, offset + index < infos.Length ? infos[offset + index] : null))) + ">";
            }
        }

        return name + nullable;
    }

    private static string Plain(Type type) => type.Name.Split('`')[0];

    private static string FullName(Type type) =>
        (type.IsNested ? FullName(type.DeclaringType!) + "." : type.Namespace + ".") + Plain(type)
        + (type.IsGenericTypeDefinition ? "<" + string.Join(", ", type.GetGenericArguments().Skip(type.IsNested ? type.DeclaringType!.GetGenericArguments().Length : 0).Select(argument => argument.Name)) + ">" : string.Empty);

    private static bool IsRecord(Type type) => type.GetMethod("<Clone>$") is not null || type.GetMethod("PrintMembers", BindingFlags.NonPublic | BindingFlags.Instance) is { } print && print.IsDefined(typeof(CompilerGeneratedAttribute));

    private static bool IsRecordEquatable(Type type, Type face) => IsRecord(type) && face.IsGenericType && face.GetGenericTypeDefinition() == typeof(IEquatable<>) && face.GetGenericArguments()[0] == type;
}
