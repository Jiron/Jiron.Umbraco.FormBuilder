using System.Buffers;
using System.Collections;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Jiron.Umbraco.FormBuilder.Testing.Architecture;

/// <summary>
/// The rule that a package keeps no static state that can change (the quality bar; ROADMAP 4.8f, 4.8h): every static
/// field is read-only, and what it holds is allowed all the way down, rather than merely not on a list of what changes.
/// Allowed are values (primitives, strings, enums, instants, delegates, patterns, encodings, reflection's members),
/// frozen and immutable collections and the definition's lists of allowed items, serializer options and contexts that
/// are read-only, and types of Jiron Form Builder whose every field is read-only and holds what is allowed. A lazy value
/// not made yet is judged by what its type can hold (4.8i). Core and Web are held to it in Tests.Unit, the AI add-on in
/// Tests.AI (ADR-0018).
/// </summary>
public static class StaticDataRule
{
    /// <summary>The static fields of the assemblies that can be set, by name: every one must be read-only.</summary>
    public static IReadOnlyList<string> Writable(IEnumerable<Assembly> assemblies) =>
        Fields(assemblies).Where(field => !field.IsInitOnly).Select(Name).ToList();

    /// <summary>The static fields of the assemblies whose value can change, by name, each with why.</summary>
    public static IReadOnlyList<string> Changeable(IEnumerable<Assembly> assemblies) =>
        Fields(assemblies).Select(field => (Field: field, Why: Why(field))).Where(found => found.Why is not null).Select(found => $"{Name(found.Field)}: {found.Why}").ToList();

    /// <summary>Why a static field's value can change, or null when it cannot.</summary>
    public static string? Why(FieldInfo field)
    {
        ArgumentNullException.ThrowIfNull(field);
        if (field.DeclaringType!.ContainsGenericParameters || field.FieldType.ContainsGenericParameters)
        {
            return Why(field.FieldType, [], item: false);
        }

        return Why(field.GetValue(null), new HashSet<object>(ReferenceEqualityComparer.Instance));
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

    // Why a value of the type can change, or null, judged by the type alone: what a container's items, an own type's
    // fields and a lazy value can hold. A type parameter is what each instantiation fills it with, which the open type
    // cannot tell: allowed as an enum, or as the items of a container that cannot change, and refused otherwise.
    private static string? Why(Type type, HashSet<Type> seen, bool item)
    {
        if (type.IsGenericParameter)
        {
            return item || type.GetGenericParameterConstraints().Contains(typeof(Enum)) ? null : $"its type parameter {type.Name} can change";
        }

        if (IsValue(type) || !seen.Add(type))
        {
            return null;
        }

        if (type.IsArray)
        {
            return $"an array of {type.GetElementType()!.Name} can change";
        }

        if (Closed(type, typeof(Lazy<>)) is { } lazy)
        {
            return Why(lazy.GetGenericArguments()[0], seen, item: false) is { } why ? $"its lazy value: {why}" : null;
        }

        if (IsContainer(type))
        {
            foreach (var argument in type.GetGenericArguments())
            {
                if (Why(argument, seen, item: true) is { } why)
                {
                    return $"an item of its {Short(type)}: {why}";
                }
            }

            return null;
        }

        if (type.Namespace?.StartsWith("Jiron.Umbraco.FormBuilder", StringComparison.Ordinal) != true || !(type.IsSealed || type.IsValueType))
        {
            return $"{Short(type)} is not known to stay unchanged";
        }

        for (var current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            foreach (var field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (!type.IsValueType && !field.IsInitOnly)
                {
                    return $"{Short(type)}.{field.Name} can be set";
                }

                if (Why(field.FieldType, seen, item: false) is { } why)
                {
                    return $"{Short(type)}.{field.Name}: {why}";
                }
            }
        }

        return null;
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

        // A lazy value is set once: what it holds once it is made is checked, and before that what its type can hold,
        // without making it.
        if (Closed(type, typeof(Lazy<>)) is { } lazy)
        {
            return Read(value, "IsValueCreated") is true ? Why(Read(value, "Value"), seen) : Why(lazy, [], item: false);
        }

        if (IsContainer(type))
        {
            if (Closed(type, typeof(ImmutableArray<>)) is not null && Read(value, "IsDefault") is true)
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

    // Values that cannot change, reflection's members among them, and flow-scoped state, which no other request sees.
    private static bool IsValue(Type type) =>
        type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal) || type == typeof(Guid) ||
        type == typeof(TimeSpan) || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(JsonEncodedText) ||
        typeof(Delegate).IsAssignableFrom(type) || typeof(Regex).IsAssignableFrom(type) || typeof(MemberInfo).IsAssignableFrom(type) ||
        typeof(CompositeFormat) == type || Closed(type, typeof(SearchValues<>)) is not null || Closed(type, typeof(AsyncLocal<>)) is not null;

    // Collections whose items cannot be added, removed or replaced, the immutable collections' builders aside: what
    // they hold is checked item by item.
    private static bool IsContainer(Type type) =>
        type.Namespace == "System.Collections.Frozen" ||
        (type.Namespace == "System.Collections.Immutable" && !type.Name.StartsWith("Builder", StringComparison.Ordinal)) ||
        type.Name.StartsWith("<>z__ReadOnly", StringComparison.Ordinal) ||
        (type.IsGenericType && type.GetGenericTypeDefinition().FullName == "Jiron.Umbraco.FormBuilder.Core.Definitions.DefinitionList`1");

    // The closed form of the generic definition that the type is or derives from, or null.
    private static Type? Closed(Type type, Type definition)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            if (current.IsGenericType && current.GetGenericTypeDefinition() == definition)
            {
                return current;
            }
        }

        return null;
    }

    private static object? Read(object value, string property) => value.GetType().GetProperty(property)!.GetValue(value);

    private static string Short(Type type) => type.IsGenericType ? type.Name[..type.Name.IndexOf('`', StringComparison.Ordinal)] : type.Name;

    private static string Name(FieldInfo field) => $"{field.DeclaringType!.FullName}.{field.Name}: {field.FieldType.Name}";
}
