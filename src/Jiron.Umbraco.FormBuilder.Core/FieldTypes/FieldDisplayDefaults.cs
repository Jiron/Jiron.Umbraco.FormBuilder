using System.Collections.Frozen;
using System.Globalization;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes;

/// <summary>
/// How a type shows its values unless it says otherwise (ADR-0066), by what rules compare them as: texts as they are,
/// options by their labels, a ticked box as yes, files by their names, numbers and dates in the culture's formats with
/// the wire form behind, and a value with parts as its parts' texts. A type whose value stays on the server shows
/// nothing in the list of the answers.
/// </summary>
internal static class FieldDisplayDefaults
{
    /// <summary>The default display of a type's values.</summary>
    public static void Display(FieldTypeDeclaration declaration, FieldDisplay display)
    {
        if (!declaration.HoldsValue || (display.IsListing && declaration.ServerOnlyDefault))
        {
            return;
        }

        for (var i = 0; i < display.Values.Length; i++)
        {
            var value = display.Values[i].Text;
            switch (declaration.RuleKind)
            {
                case FieldKind.Choice or FieldKind.Choices:
                    display.Add(display.OptionLabel(value));
                    break;
                case FieldKind.Checkbox:
                    display.Add(value == "true" ? display.Yes : display.No);
                    break;
                case FieldKind.Files:
                    if (!display.Files.IsEmpty)
                    {
                        display.Add(display.Files[i].Name);
                    }

                    break;
                case FieldKind.Number:
                    display.AddNumber(display.Number(value), value);
                    break;
                case FieldKind.Date or FieldKind.Time or FieldKind.DateTime:
                    display.AddDate(display.Date(value), value);
                    break;
                default:
                    display.Add(declaration.Storage == FieldStorageKind.Json && FieldPartsJson.Read(value) is { } parts ? string.Join(", ", parts.Select(part => part.Value)) : value);
                    break;
            }
        }
    }

    /// <summary>
    /// A text between an editor's prefix and suffix, such as a currency and a unit, each trimmed as cleaning trims a
    /// value (the set both runtimes share) and apart by a space when it has one.
    /// </summary>
    public static string Around(string prefix, string text, string suffix) =>
        string.Join(' ', new[] { TextSemantics.Trim(prefix), text, TextSemantics.Trim(suffix) }.Where(piece => piece.Length > 0));
}

/// <summary>
/// The words a field's answers are shown with (ADR-0066), in English and German so far; 14.2 adds the other five
/// languages. A culture without its own words takes its more general culture's, then English.
/// </summary>
internal static class FieldDisplayWords
{
    private static readonly FrozenDictionary<string, (string Yes, string No)> Words = new Dictionary<string, (string Yes, string No)>(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = ("Yes", "No"),
        ["de"] = ("Ja", "Nein"),
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>The cultures with words of their own.</summary>
    public static IReadOnlyCollection<string> Cultures => Words.Keys;

    /// <summary>The word for yes in a culture.</summary>
    public static string Yes(string culture) => Of(culture).Yes;

    /// <summary>The word for no in a culture.</summary>
    public static string No(string culture) => Of(culture).No;

    private static (string Yes, string No) Of(string culture)
    {
        var candidate = culture ?? string.Empty;
        while (candidate.Length > 0)
        {
            if (Words.TryGetValue(candidate, out var words))
            {
                return words;
            }

            var dash = candidate.LastIndexOf('-');
            candidate = dash < 0 ? string.Empty : candidate[..dash];
        }

        return Words["en"];
    }
}

/// <summary>
/// Country names from .NET's culture data (ADR-0066): the name of a country in a language is the native name of the
/// region of that language's culture for the country (German and <c>FR</c>: <c>de-FR</c>, "Frankreich"), which ICU
/// gives for any pair, whether or not the pair is a culture of its own.
/// </summary>
internal static class CountryNames
{
    /// <summary>The name of a country by its ISO 3166 alpha-2 code in a culture's language; the code when .NET has none.</summary>
    /// <param name="code">The code, such as <c>CH</c>.</param>
    /// <param name="culture">The culture, such as <c>de-CH</c>; English for the invariant culture or one .NET does not know.</param>
    public static string Name(string code, string culture)
    {
        if (code.Length != 2 || !char.IsAsciiLetter(code[0]) || !char.IsAsciiLetter(code[1]))
        {
            return code;
        }

        var region = code.ToUpperInvariant();
        try
        {
            return new RegionInfo(Language(culture) + "-" + region) is { TwoLetterISORegionName: var found, NativeName: { Length: > 0 } name }
                && string.Equals(found, region, StringComparison.OrdinalIgnoreCase)
                ? name
                : code;
        }
        catch (ArgumentException)
        {
            return code;
        }
    }

    /// <summary>The language of a culture without its region (<c>sr-Latn</c> for <c>sr-Latn-RS</c>), English when there is none.</summary>
    private static string Language(string culture)
    {
        var known = MergeTags.MergeTagCultures.Culture(culture);
        while (!known.IsNeutralCulture && known.Name.Length > 0)
        {
            known = known.Parent;
        }

        return known.Name.Length > 0 ? known.Name : "en";
    }
}
