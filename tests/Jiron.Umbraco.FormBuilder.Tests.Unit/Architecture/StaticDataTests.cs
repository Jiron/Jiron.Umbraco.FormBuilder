using System.Buffers;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Architecture;

/// <summary>
/// Core and Web keep no static state that can change (the quality bar; ROADMAP 4.8f, 4.8h): every static field is
/// read-only, and what it holds is allowed all the way down, rather than merely not on a list of what changes. Allowed
/// are values (primitives, strings, enums, instants, delegates, patterns, encodings), frozen and immutable collections
/// and the definition's lists of allowed items, serializer options and contexts that are read-only, and types of this
/// package whose every field is read-only and holds what is allowed.
/// </summary>
[TestFixture]
public sealed class StaticDataTests
{
    private static readonly Assembly[] Assemblies = [typeof(FormDefinition).Assembly, typeof(FormBuilderProblems).Assembly];

    [Test]
    public void EveryStaticField_IsReadOnly()
    {
        var writable = Fields(Assemblies).Where(field => !field.IsInitOnly).Select(Name).ToList();

        Assert.That(writable, Is.Empty, string.Join(Environment.NewLine, writable));
    }

    [Test]
    public void EveryStaticField_HoldsOnlyWhatCannotChange()
    {
        var changeable = Fields(Assemblies).Select(field => (Field: field, Why: Why(field))).Where(found => found.Why is not null).Select(found => $"{Name(found.Field)}: {found.Why}").ToList();

        Assert.That(changeable, Is.Empty, string.Join(Environment.NewLine, changeable));
    }

    [Test]
    public void TheCheck_FindsWhatCanChange_AndAllowsWhatCannot()
    {
        static string? Of(string name) => Why(typeof(Samples).GetField(name, BindingFlags.Static | BindingFlags.NonPublic)!);

        Assert.Multiple(() =>
        {
            Assert.That(Of(nameof(Samples.Concurrent)), Does.Contain("ConcurrentDictionary"), "a concurrent dictionary");
            Assert.That(Of(nameof(Samples.Options)), Does.Contain("not read-only"), "serializer options that can change");
            Assert.That(Of(nameof(Samples.Items)), Does.Contain("List"), "a list behind a read-only interface");
            Assert.That(Of(nameof(Samples.Numbers)), Does.Contain("array"), "an array");
            Assert.That(Of(nameof(Samples.Holder)), Does.Contain("Count"), "a writable field of an own type");
            Assert.That(Of(nameof(Samples.Deep)), Does.Contain("List"), "a list inside a frozen dictionary");
            Assert.That(Of(nameof(Samples.ReadOnlyOptions)), Is.Null, "read-only options");
            Assert.That(Of(nameof(Samples.Words)), Is.Null, "a frozen set");
            Assert.That(Of(nameof(Samples.Steps)), Is.Null, "an immutable array");
            Assert.That(Of(nameof(Samples.Fixed)), Is.Null, "an own record of read-only fields");
        });
    }

    // Every static field the assemblies declare themselves, without constants and without what the compiler generates
    // for lambdas.
    private static IEnumerable<FieldInfo> Fields(IEnumerable<Assembly> assemblies) =>
        from assembly in assemblies
        from type in assembly.GetTypes()
        where !type.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false) && !type.Name.StartsWith('<')
        from field in type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
        where !field.IsLiteral && !IsTemplate(field)
        select field;

    // The source generator's private template of a context's options: copied into the context, which makes its copy
    // read-only, and never handed out.
    private static bool IsTemplate(FieldInfo field) =>
        field.Name == "s_defaultOptions" && field.IsPrivate && typeof(JsonSerializerContext).IsAssignableFrom(field.DeclaringType);

    // Why the field's value can change, or null. A field of an open generic type cannot be read, so its declared type has
    // to do.
    private static string? Why(FieldInfo field)
    {
        if (field.DeclaringType!.ContainsGenericParameters || field.FieldType.ContainsGenericParameters)
        {
            return IsContainer(field.FieldType) || IsValue(field.FieldType) ? null : $"its type {field.FieldType.Name} can change";
        }

        return Why(field.GetValue(null), new HashSet<object>(ReferenceEqualityComparer.Instance));
    }

    private static string? Why(object? value, HashSet<object> seen)
    {
        if (value is null || IsValue(value.GetType()) || (!value.GetType().IsValueType && !seen.Add(value)))
        {
            return null;
        }

        var type = value.GetType();
        switch (value)
        {
            case Array array:
                return array.Length == 0 ? null : $"an array of {type.GetElementType()!.Name} can change";
            case JsonSerializerOptions options:
                return options.IsReadOnly ? null : "serializer options that are not read-only";
            case JsonSerializerContext context:
                return context.Options.IsReadOnly ? null : "a serializer context whose options are not read-only";
            case Encoding encoding:
                return encoding.IsReadOnly ? null : "an encoding that is not read-only";
        }

        // A lazy value is set once; what it holds once it is made is checked.
        if (IsGeneric(type, typeof(Lazy<>)))
        {
            return Read(value, "IsValueCreated") is true ? Why(Read(value, "Value"), seen) : null;
        }

        if (IsContainer(type))
        {
            if (IsGeneric(type, typeof(ImmutableArray<>)) && Read(value, "IsDefault") is true)
            {
                return null;
            }

            foreach (var item in (IEnumerable)value)
            {
                if (Why(item, seen) is { } why)
                {
                    return $"an item of its {Short(type)}: {why}";
                }
            }

            return null;
        }

        if (!type.IsValueType && type.Namespace?.StartsWith("Jiron.Umbraco.FormBuilder", StringComparison.Ordinal) != true)
        {
            return $"{Short(type)} is not known to stay unchanged";
        }

        // A struct is copied out of its read-only field, so only what its fields hold counts; an own class keeps every
        // field read-only, its base classes' included.
        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            foreach (var field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (!type.IsValueType && !field.IsInitOnly)
                {
                    return $"{Short(type)}.{field.Name} can be set";
                }

                if (Why(field.GetValue(value), seen) is { } why)
                {
                    return $"{Short(type)}.{field.Name}: {why}";
                }
            }
        }

        return null;
    }

    // Values that cannot change, and flow-scoped state, which no other request sees.
    private static bool IsValue(Type type) =>
        type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type == typeof(Guid) ||
        type == typeof(TimeSpan) || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(JsonEncodedText) ||
        typeof(Delegate).IsAssignableFrom(type) || typeof(Regex).IsAssignableFrom(type) || typeof(Type).IsAssignableFrom(type) ||
        typeof(CompositeFormat) == type || IsGeneric(type, typeof(SearchValues<>)) || IsGeneric(type, typeof(AsyncLocal<>));

    // Collections whose items cannot be added, removed or replaced, the immutable collections' builders aside: what
    // they hold is checked item by item.
    private static bool IsContainer(Type type) =>
        type.Namespace == "System.Collections.Frozen" ||
        (type.Namespace == "System.Collections.Immutable" && !type.Name.StartsWith("Builder", StringComparison.Ordinal)) ||
        type.Name.StartsWith("<>z__ReadOnly", StringComparison.Ordinal) ||
        (type.IsGenericType && type.GetGenericTypeDefinition().FullName == "Jiron.Umbraco.FormBuilder.Core.Definitions.DefinitionList`1");

    // Whether the type is, or derives from, a closed form of the generic definition.
    private static bool IsGeneric(Type type, Type definition)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == definition)
            {
                return true;
            }
        }

        return false;
    }

    private static object? Read(object value, string property) => value.GetType().GetProperty(property)!.GetValue(value);

    private static string Short(Type type) => type.IsGenericType ? type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)] : type.Name;

    private static string Name(FieldInfo field) => $"{field.DeclaringType!.FullName}.{field.Name}: {field.FieldType.Name}";

    // What the check refuses and allows, as static fields of this assembly's own type.
    private static class Samples
    {
        internal static readonly ConcurrentDictionary<string, int> Concurrent = new();
        internal static readonly JsonSerializerOptions Options = new();
        internal static readonly IReadOnlyList<string> Items = new List<string> { "a" };
        internal static readonly int[] Numbers = [1, 2];
        internal static readonly Counter Holder = new();
        internal static readonly FrozenDictionary<string, IReadOnlyList<string>> Deep = new Dictionary<string, IReadOnlyList<string>> { ["a"] = new List<string>() }.ToFrozenDictionary();
        internal static readonly JsonSerializerOptions ReadOnlyOptions = ReadOnly(new JsonSerializerOptions());
        internal static readonly FrozenSet<string> Words = new[] { "a" }.ToFrozenSet();
        internal static readonly ImmutableArray<string> Steps = ["a"];
        internal static readonly Point Fixed = new(1, 2);

        private static JsonSerializerOptions ReadOnly(JsonSerializerOptions options)
        {
            options.MakeReadOnly(populateMissingResolver: true);
            return options;
        }
    }

    private sealed class Counter
    {
        public int Count = 1;
    }

    private sealed record Point(int X, int Y);
}
