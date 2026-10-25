using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text.Json;

namespace Jiron.Umbraco.FormBuilder.Core;

/// <summary>
/// The camelCase names of an enum's values (<c>pending</c>, <c>longText</c>), built once per enum: the one table behind
/// the words of the definition model (ADR-0002) and the names stored in the database (ADR-0033), which are the same
/// names. A value that its enum names twice gets one name.
/// </summary>
/// <typeparam name="T">The enum.</typeparam>
internal static class EnumNames<T>
    where T : struct, Enum
{
    /// <summary>Each value's name.</summary>
    public static readonly FrozenDictionary<T, string> ByValue =
        Enum.GetValues<T>().Distinct().ToFrozenDictionary(value => value, value => JsonNamingPolicy.CamelCase.ConvertName(value.ToString()));

    /// <summary>The value of each name, in exactly its letter case.</summary>
    public static readonly FrozenDictionary<string, T> ByName = ByValue.ToFrozenDictionary(pair => pair.Value, pair => pair.Key, StringComparer.Ordinal);

    /// <summary>Every name once, in the enum's order.</summary>
    public static readonly ImmutableArray<string> All = [.. Enum.GetValues<T>().Distinct().Select(value => ByValue[value])];
}
