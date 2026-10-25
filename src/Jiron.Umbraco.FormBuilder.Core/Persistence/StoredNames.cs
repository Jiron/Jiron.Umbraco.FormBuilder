using System.Collections.Frozen;
using System.Text.Json;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence;

/// <summary>
/// The text an enum is stored as: the camel case of its name (<c>pending</c>, <c>longText</c>), as the definitions,
/// the events and the audit actions write theirs (ADR-0033). Every repository writes and reads its enums through this
/// one converter. A read takes exactly that text; any other, another letter case included, is a row that something
/// else wrote, and is refused.
/// </summary>
internal static class StoredNames
{
    /// <summary>The stored text of <paramref name="value"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value has no name in its enum.</exception>
    public static string Of<TEnum>(TEnum value)
        where TEnum : struct, Enum =>
        Names<TEnum>.ByValue.TryGetValue(value, out var name)
            ? name
            : throw new ArgumentOutOfRangeException(nameof(value), value, $"A {typeof(TEnum).Name} without a name.");

    /// <summary>The value that <paramref name="stored"/> names.</summary>
    /// <exception cref="InvalidOperationException">The text is not the stored name of any value of the enum.</exception>
    public static TEnum Parse<TEnum>(string stored)
        where TEnum : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(stored);
        return Names<TEnum>.ByName.TryGetValue(stored, out var value)
            ? value
            : throw new InvalidOperationException($"\"{stored}\" is not the stored name of a {typeof(TEnum).Name}.");
    }

    // Built once per enum from its names, and never changed.
    private static class Names<TEnum>
        where TEnum : struct, Enum
    {
        public static readonly FrozenDictionary<TEnum, string> ByValue =
            Enum.GetValues<TEnum>().Distinct().ToFrozenDictionary(value => value, value => JsonNamingPolicy.CamelCase.ConvertName(value.ToString()));

        public static readonly FrozenDictionary<string, TEnum> ByName =
            ByValue.ToFrozenDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);
    }
}
