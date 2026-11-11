using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;

namespace Jiron.Umbraco.FormBuilder.Logic;

/// <summary>
/// A site's and a form's own wording of the package's visitor texts (ADR-0230): each a map from a text's key
/// (<see cref="VisitorTextKeys"/>) to its wording per culture, a culture's own, its language's or, as <c>""</c>, every
/// language's. A page takes the form's wording of a text, then the site's, then the package's.
/// </summary>
internal static class VisitorWording
{
    /// <summary>No texts reworded.</summary>
    public static IReadOnlyDictionary<string, string> None { get; } = FrozenDictionary<string, string>.Empty;

    // Each property of the message table by its name after the prefix, read once.
    private static readonly ImmutableArray<(string Name, PropertyInfo Property)> MessageProperties =
        [.. typeof(VisitorMessageWords).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => (JsonNamingPolicy.CamelCase.ConvertName(property.Name), property))];

    /// <summary>The wording a map gives a culture: the culture's own, its language's, then every language's; null for none.</summary>
    /// <param name="cultures">The wording per culture.</param>
    /// <param name="culture">The page's culture, such as <c>fr-CH</c>.</param>
    public static string? Resolve(IReadOnlyDictionary<string, string> cultures, string culture)
    {
        ArgumentNullException.ThrowIfNull(cultures);
        var candidate = culture ?? string.Empty;
        while (true)
        {
            foreach (var (name, wording) in cultures)
            {
                if (string.Equals(name, candidate, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(wording))
                {
                    return wording;
                }
            }

            if (candidate.Length == 0)
            {
                return null;
            }

            var dash = candidate.LastIndexOf('-');
            candidate = dash < 0 ? string.Empty : candidate[..dash];
        }
    }

    /// <summary>
    /// The texts a page reworded in its culture: each text's wording from the form, else from the site. A key the package
    /// has no text of is left out, and so is a text neither words in the culture.
    /// </summary>
    /// <param name="form">The form's own wording, or null.</param>
    /// <param name="site">The site's, or null.</param>
    /// <param name="culture">The page's culture.</param>
    public static IReadOnlyDictionary<string, string> ForCulture(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? form,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? site,
        string culture)
    {
        if (form is not { Count: > 0 } && site is not { Count: > 0 })
        {
            return None;
        }

        var texts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var owner in new[] { form, site })
        {
            foreach (var (key, cultures) in owner ?? FrozenDictionary<string, IReadOnlyDictionary<string, string>>.Empty)
            {
                if (!texts.ContainsKey(key) && cultures is not null && VisitorTextKeys.Find(key) is not null && Resolve(cultures, culture) is { } wording)
                {
                    texts[key] = wording;
                }
            }
        }

        return texts;
    }

    /// <summary>
    /// The texts a page shows: those whose wording, as it shows (a dictionary item's text in its place), the text may
    /// take (<see cref="VisitorTextKeys.Problem"/>); a wording that broke since it was saved leaves the package's text.
    /// </summary>
    /// <param name="texts">The reworded texts, as they show.</param>
    public static IReadOnlyDictionary<string, string> Shown(IReadOnlyDictionary<string, string> texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        if (texts.Count == 0)
        {
            return None;
        }

        var shown = texts.Where(pair => VisitorTextKeys.Find(pair.Key) is { } key && VisitorTextKeys.Problem(key, pair.Value) is null).ToDictionary(StringComparer.Ordinal);
        return shown.Count == 0 ? None : shown;
    }

    /// <summary>The reworded messages, by their names after <see cref="VisitorTextKeys.MessagePrefix"/>, as the browser takes them.</summary>
    /// <param name="texts">The texts a page shows.</param>
    public static IReadOnlyDictionary<string, string> Messages(IReadOnlyDictionary<string, string> texts)
    {
        ArgumentNullException.ThrowIfNull(texts);
        return texts
            .Where(pair => pair.Key.StartsWith(VisitorTextKeys.MessagePrefix, StringComparison.Ordinal))
            .ToDictionary(pair => pair.Key[VisitorTextKeys.MessagePrefix.Length..], pair => pair.Value, StringComparer.Ordinal);
    }

    /// <summary>A message table with the messages a page rewords in place of its own; the table itself when it rewords none.</summary>
    /// <param name="words">The table of the page's culture.</param>
    /// <param name="texts">The texts a page shows.</param>
    public static VisitorMessageWords Apply(VisitorMessageWords words, IReadOnlyDictionary<string, string> texts)
    {
        ArgumentNullException.ThrowIfNull(words);
        var messages = Messages(texts ?? None);
        if (messages.Count == 0)
        {
            return words;
        }

        var copy = words with { };
        foreach (var (name, property) in MessageProperties)
        {
            switch (property.GetValue(copy))
            {
                case string when messages.TryGetValue(name, out var wording):
                    property.SetValue(copy, wording);
                    break;
                case PluralWords plural when messages.ContainsKey(name + ".one") || messages.ContainsKey(name + ".other"):
                    property.SetValue(copy, new PluralWords(messages.GetValueOrDefault(name + ".one", plural.One), messages.GetValueOrDefault(name + ".other", plural.Other)));
                    break;
                case IReadOnlyDictionary<string, string> names when names.Keys.Any(entry => messages.ContainsKey(name + "." + entry)):
                    property.SetValue(copy, names.ToDictionary(pair => pair.Key, pair => messages.GetValueOrDefault(name + "." + pair.Key, pair.Value), StringComparer.Ordinal));
                    break;
            }
        }

        return copy;
    }

    /// <summary>The word for yes a page shows: its own, else the package's in the culture.</summary>
    /// <param name="culture">The page's culture.</param>
    /// <param name="texts">The texts a page shows.</param>
    public static string Yes(string culture, IReadOnlyDictionary<string, string>? texts) =>
        texts is not null && texts.TryGetValue(VisitorTextKeys.AnswerPrefix + "yes", out var yes) ? yes : FieldDisplayWords.Yes(culture);

    /// <summary>The word for no a page shows: its own, else the package's in the culture.</summary>
    /// <param name="culture">The page's culture.</param>
    /// <param name="texts">The texts a page shows.</param>
    public static string No(string culture, IReadOnlyDictionary<string, string>? texts) =>
        texts is not null && texts.TryGetValue(VisitorTextKeys.AnswerPrefix + "no", out var no) ? no : FieldDisplayWords.No(culture);
}
