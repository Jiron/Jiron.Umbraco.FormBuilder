using System.Collections;
using System.Reflection;
using Jiron.Umbraco.FormBuilder.Core.Definitions;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Definitions;

/// <summary>
/// Compares two definition models property by property, through every list and nested record, and names each
/// difference by its path. Records compare by value too (ADR-0034), but their equality cannot say where two differ.
/// </summary>
internal static class DefinitionComparer
{
    public static IReadOnlyList<string> Differences(object? expected, object? actual)
    {
        var differences = new List<string>();
        Compare(expected, actual, "$", differences);
        return differences;
    }

    private static void Compare(object? expected, object? actual, string path, List<string> differences)
    {
        if (expected is null || actual is null)
        {
            if (expected is not null || actual is not null)
            {
                differences.Add($"{path}: {Describe(expected)} != {Describe(actual)}");
            }

            return;
        }

        if (expected.GetType() != actual.GetType())
        {
            differences.Add($"{path}: {expected.GetType().Name} != {actual.GetType().Name}");
            return;
        }

        var type = expected.GetType();
        if (IsValue(type))
        {
            if (!Equals(expected, actual))
            {
                differences.Add($"{path}: {Describe(expected)} != {Describe(actual)}");
            }

            return;
        }

        if (expected is IList expectedList && actual is IList actualList)
        {
            if (expectedList.Count != actualList.Count)
            {
                differences.Add($"{path}: {expectedList.Count} items != {actualList.Count} items");
                return;
            }

            for (var i = 0; i < expectedList.Count; i++)
            {
                Compare(expectedList[i], actualList[i], $"{path}[{i}]", differences);
            }

            return;
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0 || property.Name == "EqualityContract")
            {
                continue;
            }

            Compare(property.GetValue(expected), property.GetValue(actual), $"{path}.{property.Name}", differences);
        }
    }

    // Types that carry their own value equality: primitives, and the model's texts, defaults and settings.
    private static bool IsValue(Type type) =>
        type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(Guid) || type == typeof(DateTime)
        || typeof(LocalizedString).IsAssignableFrom(type) || type == typeof(FieldDefaultValue) || type == typeof(SettingsObject);

    private static string Describe(object? value) => value switch
    {
        null => "null",
        LocalizedString text => string.Join(", ", text.Texts.Select(entry => $"{entry.Key}={entry.Value}")),
        DateTime instant => $"{instant:O} ({instant.Kind})",
        _ => value.ToString() ?? string.Empty,
    };
}
