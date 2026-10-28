using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Projection;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;

namespace Jiron.Umbraco.FormBuilder.Core.Forms;

/// <summary>
/// A published form as a visitor's page gets it in one culture (ADR-0084): each text of its projection in the culture
/// with the usual fallback (ADR-0002), a text that names a dictionary item (<c>#key</c>) as the item's text in the
/// culture through Umbraco's dictionary, every rich text through the one sanitiser (section 4.10), and a lone surrogate
/// a text may hold replaced, so the form always writes.
/// </summary>
internal sealed class VisitorForms(MergeTagDictionary dictionary, EditorHtml html, FieldTypeRegistry types)
{
    /// <summary>The visitor's form as UTF-8 JSON.</summary>
    /// <param name="projection">The form's public projection.</param>
    /// <param name="culture">The culture, an ISO code of one of the site's languages.</param>
    public async Task<byte[]> WriteAsync(PublicForm projection, string culture)
    {
        ArgumentNullException.ThrowIfNull(projection);
        ArgumentNullException.ThrowIfNull(culture);

        // The dictionary items the texts name in this culture, read at once.
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        VisitorForm.Map(
            projection,
            text =>
            {
                if (LocalizedString.TryGetDictionaryKey(text.Resolve(culture), out var key))
                {
                    keys.Add(key);
                }

                return string.Empty;
            },
            types);
        var items = keys.Count == 0 ? DictionaryTexts.None : await dictionary.TextsAsync(keys, culture);
        return VisitorForm.Write(VisitorForm.Map(projection, text => Show(text, culture, items), types));
    }

    private string Show(LocalizedString text, string culture, DictionaryTexts items)
    {
        var shown = WellFormed(items.Resolve(text, culture));
        return text is LocalizedHtml ? html.Sanitise(shown) : shown;
    }

    // A lone surrogate, which a dictionary item may hold, becomes U+FFFD, as UTF-8 writes it.
    private static string WellFormed(string text) =>
        StrictStringConverter.IsWellFormed(text) ? text : Encoding.UTF8.GetString(Encoding.UTF8.GetBytes(text));
}
