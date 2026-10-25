using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;

namespace Jiron.Umbraco.FormBuilder.Core.Definitions;

/// <summary>
/// An editor-written text in one or more cultures (ADR-0002): culture names (<c>de</c>, <c>de-CH</c>,
/// <c>sr-Latn-RS</c>) map to texts, and the culture <c>""</c> holds the default text, which every text has. Culture
/// names are matched without regard to case. A definition writes no text as <c>null</c>, never as an empty object.
/// </summary>
public abstract partial class LocalizedString : IEquatable<LocalizedString>
{
    private readonly DefinitionList<KeyValuePair<string, string>> _texts;

    /// <summary>Creates a text from its cultures and texts.</summary>
    /// <exception cref="ArgumentException">
    /// The texts are empty, have no default (<c>""</c>), name a culture twice (in any letter case) or name something that
    /// is not a culture name.
    /// </exception>
    private protected LocalizedString(IEnumerable<KeyValuePair<string, string>> texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        var sorted = DefinitionList.Of(texts.OrderBy(text => text.Key, StringComparer.Ordinal));
        // The ordinal order puts a null culture first.
        if (sorted.Count > 0 && sorted[0].Key is null)
        {
            throw new ArgumentException("A culture name is null; the default text's culture is \"\".", nameof(texts));
        }

        if (sorted.Count == 0 || sorted[0].Key.Length != 0)
        {
            throw new ArgumentException("A localised text needs its default text, the culture \"\".", nameof(texts));
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (culture, text) in sorted.AsSpan())
        {
            if (culture.Length > 0 && !IsCultureName(culture))
            {
                throw new ArgumentException($"\"{culture}\" is not a culture name.", nameof(texts));
            }

            if (!seen.Add(culture))
            {
                throw new ArgumentException($"The culture \"{culture}\" appears twice.", nameof(texts));
            }

            ArgumentNullException.ThrowIfNull(text, nameof(texts));
        }

        _texts = sorted;
    }

    /// <summary>The default text, shown when no culture of the request has a text of its own.</summary>
    public string Default => _texts[0].Value;

    /// <summary>Every culture with its text, the default (<c>""</c>) first, then by culture name (ordinal).</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Texts => _texts;

    /// <summary>Whether <paramref name="name"/> has the form of a culture name: a language and optional subtags.</summary>
    public static bool IsCultureName(string name) => name is not null && CultureName().IsMatch(name);

    /// <summary>
    /// Whether <paramref name="text"/> is exactly <c>#</c> and a dictionary key (ASCII letters, digits, <c>.</c>,
    /// <c>_</c> and <c>-</c>), which the website resolves through Umbraco's dictionary.
    /// </summary>
    public static bool TryGetDictionaryKey(string text, [NotNullWhen(true)] out string? key)
    {
        key = text is not null && DictionaryReference().IsMatch(text) ? text[1..] : null;
        return key is not null;
    }

    /// <summary>The text of exactly this culture (any letter case), without falling back.</summary>
    public bool TryGetText(string culture, [NotNullWhen(true)] out string? text)
    {
        ArgumentNullException.ThrowIfNull(culture);
        foreach (var entry in _texts.AsSpan())
        {
            if (string.Equals(entry.Key, culture, StringComparison.OrdinalIgnoreCase))
            {
                text = entry.Value;
                return true;
            }
        }

        text = null;
        return false;
    }

    /// <summary>
    /// The text a request in <paramref name="culture"/> sees: that culture's, then its more general cultures' (subtags
    /// removed from the end, so <c>de-CH</c> falls back to <c>de</c>), then the default.
    /// </summary>
    public string Resolve(string? culture)
    {
        var candidate = culture ?? string.Empty;
        while (candidate.Length > 0)
        {
            if (TryGetText(candidate, out var text))
            {
                return text;
            }

            var dash = candidate.LastIndexOf('-');
            candidate = dash < 0 ? string.Empty : candidate[..dash];
        }

        return Default;
    }

    /// <inheritdoc />
    public bool Equals(LocalizedString? other) =>
        other is not null && other.GetType() == GetType() && _texts.Equals(other._texts);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as LocalizedString);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = default(HashCode);
        foreach (var (culture, text) in _texts.AsSpan())
        {
            hash.Add(culture, StringComparer.Ordinal);
            hash.Add(text, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }

    /// <inheritdoc />
    public override string ToString() => Default;

    [GeneratedRegex(@"^[A-Za-z]{2,8}(-[A-Za-z0-9]{1,8})*\z", RegexOptions.CultureInvariant)]
    private static partial Regex CultureName();

    [GeneratedRegex(@"^#[A-Za-z0-9._\-]+\z", RegexOptions.CultureInvariant)]
    private static partial Regex DictionaryReference();
}

/// <summary>
/// Plain text in one or more cultures: labels, titles, placeholders, help texts and messages. Texts that render on one
/// line take at most 500 characters and no line break, other plain texts at most 2,000 (ADR-0002).
/// </summary>
[JsonConverter(typeof(LocalizedStringConverter<LocalizedText>))]
public sealed class LocalizedText : LocalizedString
{
    /// <summary>Creates a text from its cultures and texts; the culture <c>""</c> is the default and required.</summary>
    public LocalizedText(IEnumerable<KeyValuePair<string, string>> texts)
        : base(texts)
    {
    }

    /// <summary>Creates a text that has only its default text.</summary>
    public LocalizedText(string text)
        : base([new KeyValuePair<string, string>(string.Empty, text)])
    {
    }
}

/// <summary>
/// Rich text in one or more cultures, stored as the editor wrote it and sanitised on every output (section 4.10), at
/// most 50,000 characters per culture (ADR-0002).
/// </summary>
[JsonConverter(typeof(LocalizedStringConverter<LocalizedHtml>))]
public sealed class LocalizedHtml : LocalizedString
{
    /// <summary>Creates a text from its cultures and texts; the culture <c>""</c> is the default and required.</summary>
    public LocalizedHtml(IEnumerable<KeyValuePair<string, string>> texts)
        : base(texts)
    {
    }

    /// <summary>Creates a text that has only its default text.</summary>
    public LocalizedHtml(string html)
        : base([new KeyValuePair<string, string>(string.Empty, html)])
    {
    }
}
