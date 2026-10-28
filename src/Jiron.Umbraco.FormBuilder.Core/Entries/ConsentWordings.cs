using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Projection;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Core.Entries;

/// <summary>
/// A consent's wording as the visitor was shown it (ADR-0009, ADR-0070): its culture, its text, and the hash that names
/// both (<see cref="ConsentTextHash"/>), which the form posts and the entry stores.
/// </summary>
internal sealed record ConsentWording
{
    /// <summary>A wording.</summary>
    /// <param name="culture">The culture it was shown in, or "" for the invariant culture.</param>
    /// <param name="text">The text, as shown.</param>
    /// <exception cref="ArgumentException">The culture or the text cannot be hashed (<see cref="ConsentTextHash.For"/>).</exception>
    public ConsentWording(string culture, string text)
    {
        Hash = ConsentTextHash.For(culture, text);
        Culture = culture;
        Text = text;
    }

    /// <summary>The culture it was shown in, or "" for the invariant culture.</summary>
    public string Culture { get; }

    /// <summary>The text, as shown.</summary>
    public string Text { get; }

    /// <summary>The SHA-256 of the culture and the text, in lowercase hex.</summary>
    public string Hash { get; }
}

/// <summary>
/// The wording a consent field shows (ADR-0070, ADR-0085): its text in the culture with the usual fallback, a dictionary
/// item resolved through Umbraco's dictionary, its merge tags rendered where consent texts render them, so the wording
/// is the same for every visitor and holds no personal data (ADR-0014), and the result through the one sanitiser, with a
/// lone surrogate replaced, so the text the visitor reads, its hash and the text the entry stores are one. The form
/// endpoint shows it, the entry stores it, and 6.2 renders it.
/// </summary>
internal sealed class ConsentWordings(IFormBuilderMergeTagService mergeTags, MergeTagDictionary dictionary, EditorHtml html, FieldTypeRegistry types)
{
    /// <summary>
    /// The wording the field shows in the scope's culture; null for a field that is no consent, or whose text renders
    /// as nothing.
    /// </summary>
    /// <param name="field">The field, of the version the scope's form is.</param>
    /// <param name="scope">What the wording is rendered for: the form, the culture, the site and the page.</param>
    /// <param name="cancellationToken">Cancels the merge-tag sources' work.</param>
    public async Task<ConsentWording?> ShownAsync(FormField field, MergeTagScope scope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(scope);
        if (types.FindType(field.Type) is not ConsentFieldType consent || consent.ReadSettings(field.Settings).Text is not { } text)
        {
            return null;
        }

        var written = text.Resolve(scope.Culture);
        if (LocalizedString.TryGetDictionaryKey(written, out var key) && await dictionary.TextAsync(key, scope.Culture) is { Length: > 0 } item)
        {
            written = item;
        }

        var rendering = await mergeTags.RenderAsync(written, MergeTagPlace.ConsentText, MergeTagContext.Html, scope, cancellationToken);
        var shown = StrictStringConverter.WellFormed(html.Sanitise(rendering.Text));
        return shown.Length == 0 ? null : new ConsentWording(scope.Culture, shown);
    }
}
