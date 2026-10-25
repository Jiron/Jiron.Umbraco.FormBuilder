using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;

namespace Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;

/// <summary>
/// The words of one enum of the definition model (ADR-0002): each value's camelCase name, read and written by the
/// converter and checked by the save checks, so a value the enum does not define is refused once, in one wording.
/// </summary>
/// <typeparam name="T">The enum.</typeparam>
internal static class DefinitionWords<T>
    where T : struct, Enum
{
    private static readonly FrozenDictionary<T, string> ByValue = Enum.GetValues<T>().ToFrozenDictionary(value => value, value => JsonNamingPolicy.CamelCase.ConvertName(value.ToString()));
    private static readonly FrozenDictionary<string, T> ByWord = ByValue.ToFrozenDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);

    /// <summary>Every word, in the enum's order.</summary>
    public static ImmutableArray<string> All { get; } = [.. Enum.GetValues<T>().Select(value => ByValue[value])];

    /// <summary>The value's word, or null when the enum does not define the value.</summary>
    public static string? Of(T value) => ByValue.GetValueOrDefault(value);

    /// <summary>The value of exactly this word, in its letter case.</summary>
    public static bool TryRead(string word, out T value) => ByWord.TryGetValue(word, out value);

    /// <summary>Why a value the enum does not define cannot be written.</summary>
    public static string NotAWord(T value) =>
        string.Create(CultureInfo.InvariantCulture, $"{typeof(T).Name} has no value {value:D}; its words are {string.Join(", ", All)}.");
}
