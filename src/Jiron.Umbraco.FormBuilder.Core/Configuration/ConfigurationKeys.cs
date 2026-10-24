using System.Collections;
using System.Reflection;
using Microsoft.Extensions.Configuration;

namespace Jiron.Umbraco.FormBuilder.Core.Configuration;

/// <summary>
/// Finds the keys of a configuration section that its options class does not have (ADR-0042). The configuration
/// binder leaves such a key out without a word, so a misspelt lock would leave the default in place.
/// </summary>
internal static class ConfigurationKeys
{
    /// <summary>
    /// Adds a failure for every key under <paramref name="section"/> that <paramref name="type"/> does not have, for a
    /// value where a section belongs, and for a section where a value or a list belongs. Names are matched ignoring case,
    /// as configuration matches them; a dictionary takes any name.
    /// </summary>
    public static void Check(IConfigurationSection section, Type type, ICollection<string> failures)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(failures);
        Walk(section, type, failures);
    }

    private static void Walk(IConfigurationSection section, Type type, ICollection<string> failures)
    {
        var children = section.GetChildren().ToList();
        var hasValue = !string.IsNullOrEmpty(section.Value);
        if (IsValue(type))
        {
            if (children.Count > 0)
            {
                failures.Add($"{section.Path} is a single value, not a section.");
            }

            return;
        }

        if (ItemType(type) is { } item)
        {
            if (hasValue)
            {
                failures.Add($"{section.Path} is a list: give its items as {section.Path}:0, {section.Path}:1 and so on.");
            }

            foreach (var child in children)
            {
                Walk(child, item, failures);
            }

            return;
        }

        if (hasValue)
        {
            failures.Add($"{section.Path} is a section of settings, not a single value.");
        }

        var properties = Properties(type);
        foreach (var child in children)
        {
            var property = properties.FirstOrDefault(p => string.Equals(p.Name, child.Key, StringComparison.OrdinalIgnoreCase));
            if (property is null)
            {
                failures.Add($"{child.Path} is not a setting of Jiron Form Builder; the settings in {section.Path} are {List(properties.Select(p => p.Name))}.");
            }
            else
            {
                Walk(child, property.PropertyType, failures);
            }
        }
    }

    // What the binder binds from one string: text, numbers, switches, enums and the like.
    private static bool IsValue(Type type)
    {
        var plain = Nullable.GetUnderlyingType(type) ?? type;
        return plain.IsPrimitive || plain.IsEnum || plain == typeof(string) || plain == typeof(decimal) || plain == typeof(TimeSpan)
            || plain == typeof(DateTime) || plain == typeof(DateTimeOffset) || plain == typeof(Guid) || plain == typeof(Uri);
    }

    // The type of a list's items or a dictionary's values, whose keys are indexes and names, or null for a class. An
    // array is a list of its elements.
    private static Type? ItemType(Type type)
    {
        if (!typeof(IEnumerable).IsAssignableFrom(type))
        {
            return null;
        }

        Type[] interfaces = type.IsInterface ? [type, .. type.GetInterfaces()] : type.GetInterfaces();
        var dictionary = interfaces.FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDictionary<,>));
        if (dictionary is not null)
        {
            return dictionary.GetGenericArguments()[1];
        }

        return interfaces.FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>))?.GetGenericArguments()[0];
    }

    private static List<PropertyInfo> Properties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.GetMethod is { IsPublic: true } && p.GetIndexParameters().Length == 0).ToList();

    private static string List(IEnumerable<string> names) => names.ToArray() switch
    {
        [] => "none",
        [var one] => one,
        [.. var first, var last] => $"{string.Join(", ", first)} and {last}",
    };
}
