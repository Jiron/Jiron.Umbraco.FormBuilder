namespace Jiron.Umbraco.FormBuilder.Core.Persistence;

/// <summary>
/// The text an enum is stored as: the camel case of its name (<c>pending</c>, <c>longText</c>), as the definitions,
/// the events and the audit actions write theirs (ADR-0033), from the one table of names (<see cref="EnumNames{T}"/>).
/// Every repository writes and reads its enums through this one converter. A read takes exactly that text; any other,
/// another letter case included, is a row that something else wrote, and is refused. A stored name that no enum holds
/// yet (a scan state, an option list kind) is a camelCase word too (<see cref="IsName"/>), so the rows written before
/// its enum comes read as the enum's names; a reader that meets a later version's name, as the option lists' does,
/// looks it up in <see cref="EnumNames{T}"/> itself instead of being refused (ADR-0081).
/// </summary>
internal static class StoredNames
{
    /// <summary>
    /// True for a stored name that no enum holds yet: a camelCase word of 1 to <paramref name="maxLength"/> ASCII
    /// letters, such as <c>pending</c> or <c>dataType</c>, as the names of stored enums are.
    /// </summary>
    public static bool IsName(string? text, int maxLength) =>
        text is { Length: > 0 } && text.Length <= maxLength && char.IsAsciiLetterLower(text[0]) && text.All(char.IsAsciiLetter);

    /// <summary>The stored text of <paramref name="value"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value has no name in its enum.</exception>
    public static string Of<TEnum>(TEnum value)
        where TEnum : struct, Enum =>
        EnumNames<TEnum>.ByValue.TryGetValue(value, out var name)
            ? name
            : throw new ArgumentOutOfRangeException(nameof(value), value, $"A {typeof(TEnum).Name} without a name.");

    /// <summary>
    /// <paramref name="value"/> for a message: its stored name, as rows and answers show it, or its number when it has
    /// none (a value that is refused).
    /// </summary>
    public static string Describe<TEnum>(TEnum value)
        where TEnum : struct, Enum =>
        EnumNames<TEnum>.ByValue.TryGetValue(value, out var name) ? name : value.ToString();

    /// <summary>The value that <paramref name="stored"/> names.</summary>
    /// <exception cref="InvalidOperationException">The text is not the stored name of any value of the enum.</exception>
    public static TEnum Parse<TEnum>(string stored)
        where TEnum : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(stored);
        return EnumNames<TEnum>.ByName.TryGetValue(stored, out var value)
            ? value
            : throw new InvalidOperationException($"\"{stored}\" is not the stored name of a {typeof(TEnum).Name}.");
    }
}
