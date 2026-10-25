using System.Collections.Immutable;
using System.Globalization;

namespace Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;

/// <summary>
/// The words of one enum of the definition model (ADR-0002): each value's camelCase name, read and written by the
/// converter and checked by the save checks, so a value the enum does not define is refused once, in one wording. The
/// words are the enum's names (<see cref="EnumNames{T}"/>), as the database stores them.
/// </summary>
/// <typeparam name="T">The enum.</typeparam>
internal static class DefinitionWords<T>
    where T : struct, Enum
{
    /// <summary>Every word once, in the enum's order.</summary>
    public static ImmutableArray<string> All => EnumNames<T>.All;

    /// <summary>The value's word, or null when the enum does not define the value.</summary>
    public static string? Of(T value) => EnumNames<T>.ByValue.GetValueOrDefault(value);

    /// <summary>The value of exactly this word, in its letter case.</summary>
    public static bool TryRead(string word, out T value) => EnumNames<T>.ByName.TryGetValue(word, out value);

    /// <summary>Why a value the enum does not define cannot be written.</summary>
    public static string NotAWord(T value) =>
        string.Create(CultureInfo.InvariantCulture, $"{typeof(T).Name} has no value {value:D}; its words are {string.Join(", ", All)}.");
}
