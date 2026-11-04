using System.Collections.Frozen;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;

namespace Jiron.Umbraco.FormBuilder.Core.MergeTags;

/// <summary>
/// Umbraco's dictionary as merge tags and the answers read it (ADR-0067): an item's text in a culture, falling back
/// through the languages Umbraco names for it. The languages tried are the culture's own, then its more general ones
/// (<c>de-CH</c>, then <c>de</c>), each followed by the language Umbraco falls back to from it, and last the default
/// language; an empty translation counts as none.
/// </summary>
/// <param name="items">The dictionary items.</param>
/// <param name="languages">The languages, with their fallbacks.</param>
internal sealed class MergeTagDictionary(IDictionaryItemService items, ILanguageService languages)
{
    /// <summary>An item's text in a culture; the empty text when no language tried has one, and null when there is no such item.</summary>
    /// <param name="key">The item's key, dots included.</param>
    /// <param name="culture">The template's culture; the default language's text for the invariant culture.</param>
    public async Task<string?> TextAsync(string key, string culture)
    {
        if (await items.GetAsync(key) is not { } item)
        {
            return null;
        }

        return Pick(item, await LanguagesAsync(culture));
    }

    /// <summary>The texts of several items in a culture, by key in any letter case; an item without a text in any language tried is left out.</summary>
    /// <param name="keys">The items' keys.</param>
    /// <param name="culture">The template's culture.</param>
    public async Task<DictionaryTexts> TextsAsync(IReadOnlyCollection<string> keys, string culture)
    {
        if (keys.Count == 0)
        {
            return DictionaryTexts.None;
        }

        var found = await items.GetManyAsync([.. keys]);
        var tried = await LanguagesAsync(culture);
        var texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in found)
        {
            if (Pick(item, tried) is { Length: > 0 } text)
            {
                texts.TryAdd(item.ItemKey, text);
            }
        }

        return new DictionaryTexts(texts.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>The ISO codes of the languages a culture's texts come from, in the order they are tried.</summary>
    /// <param name="culture">The template's culture.</param>
    public async Task<IReadOnlyList<string>> LanguagesAsync(string culture)
    {
        var all = (await languages.GetAllAsync()).ToList();
        var byCode = new Dictionary<string, ILanguage>(StringComparer.OrdinalIgnoreCase);
        foreach (var language in all)
        {
            byCode.TryAdd(language.IsoCode, language);
        }

        var tried = new List<string>();
        for (var candidate = culture; candidate.Length > 0; candidate = Parent(candidate))
        {
            // A language's fallbacks may lead in a circle, which the list of those tried ends.
            for (var code = candidate; code is not null && byCode.TryGetValue(code, out var language) && !Contains(tried, language.IsoCode); code = language.FallbackIsoCode)
            {
                tried.Add(language.IsoCode);
            }
        }

        if (all.FirstOrDefault(language => language.IsDefault) is { } fallback && !Contains(tried, fallback.IsoCode))
        {
            tried.Add(fallback.IsoCode);
        }

        return tried;
    }

    /// <summary>The text of the first language tried that has one; the empty text when none has.</summary>
    private static string Pick(IDictionaryItem item, IReadOnlyList<string> tried)
    {
        foreach (var code in tried)
        {
            var translation = item.Translations.FirstOrDefault(found => string.Equals(found.LanguageIsoCode, code, StringComparison.OrdinalIgnoreCase));
            if (translation?.Value is { Length: > 0 } text)
            {
                return text;
            }
        }

        return string.Empty;
    }

    private static string Parent(string culture)
    {
        var dash = culture.LastIndexOf('-');
        return dash < 0 ? string.Empty : culture[..dash];
    }

    private static bool Contains(List<string> codes, string code) => codes.Exists(found => string.Equals(found, code, StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// The dictionary items a form's texts name (<c>#key</c>, ADR-0002), each with its text in one culture: a text that names
/// one shows the item's text, and one that names an item without a text shows as it is written (ADR-0067).
/// </summary>
internal sealed class DictionaryTexts
{
    private readonly FrozenDictionary<string, string> _texts;

    /// <summary>Creates the texts, by key in any letter case.</summary>
    public DictionaryTexts(FrozenDictionary<string, string> texts)
    {
        _texts = texts;
    }

    /// <summary>No items: every text shows as it is written.</summary>
    public static DictionaryTexts None { get; } = new(FrozenDictionary<string, string>.Empty);

    /// <summary>Whether the item with this key, in any letter case, has a text, so a text that names it shows that text.</summary>
    /// <param name="key">The item's key, dots included.</param>
    public bool Has(string key) => _texts.ContainsKey(key);

    /// <summary>An editor's text in a culture, as <see cref="LocalizedString.Resolve"/> finds it, with the item's text for a text that names one.</summary>
    /// <param name="text">The text, or null for none.</param>
    /// <param name="culture">The culture.</param>
    public string Resolve(LocalizedString? text, string culture)
    {
        var resolved = text?.Resolve(culture) ?? string.Empty;
        return LocalizedString.TryGetDictionaryKey(resolved, out var key) && _texts.TryGetValue(key, out var found) ? found : resolved;
    }
}
