using System.Collections.Frozen;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

/// <summary>
/// The words a rendered form brings of its own (ADR-0093), in English and German so far; 14.2 adds the other five
/// languages. A culture without its own words takes its more general culture's, then English. A form's own labels and
/// messages win over these. A text with <c>{0}</c> takes a value there.
/// </summary>
internal sealed record RenderingWords
{
    // The letters outside ASCII the German words need.
    private const char CapitalAUmlaut = (char)0xC4;
    private const char OUmlaut = (char)0xF6;
    private const char UUmlaut = (char)0xFC;

    private static readonly FrozenDictionary<string, RenderingWords> Words = new Dictionary<string, RenderingWords>(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = new RenderingWords
        {
            Back = "Back",
            Next = "Next",
            Send = "Send",
            Optional = "(optional)",
            RequiredNote = "Questions marked {0} need an answer.",
            Asterisk = "with an asterisk",
            ErrorSummary = "These answers need a change",
            Step = "Step {0} of {1}",
            Progress = "Progress",
            Page = "Page {0}",
            ConfirmEmail = "Repeat your email address",
            Counter = "Up to {0} characters",
            Reference = "Your reference is {0}",
            OpensHeading = "This form opens on {0}",
            OpensText = "Come back then to fill it in.",
            ClosedHeading = "This form is closed",
            ClosedText = "It no longer takes answers.",
            FullHeading = "This form is full",
            FullText = "It has all the answers it can take.",
        },
        ["de"] = new RenderingWords
        {
            Back = "Zur" + UUmlaut + "ck",
            Next = "Weiter",
            Send = "Senden",
            Optional = "(optional)",
            RequiredNote = "Fragen mit {0} m" + UUmlaut + "ssen beantwortet werden.",
            Asterisk = "Sternchen",
            ErrorSummary = "Diese Antworten brauchen eine " + CapitalAUmlaut + "nderung",
            Step = "Schritt {0} von {1}",
            Progress = "Fortschritt",
            Page = "Seite {0}",
            ConfirmEmail = "E-Mail-Adresse wiederholen",
            Counter = "Bis zu {0} Zeichen",
            Reference = "Ihre Referenz lautet {0}",
            OpensHeading = "Dieses Formular " + OUmlaut + "ffnet am {0}",
            OpensText = "Kommen Sie dann wieder, um es auszuf" + UUmlaut + "llen.",
            ClosedHeading = "Dieses Formular ist geschlossen",
            ClosedText = "Es nimmt keine Antworten mehr an.",
            FullHeading = "Dieses Formular ist voll",
            FullText = "Es hat alle Antworten, die es annehmen kann.",
        },
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>The cultures with words of their own.</summary>
    public static IReadOnlyCollection<string> Cultures => Words.Keys;

    /// <summary>The button back to the previous page.</summary>
    public required string Back { get; init; }

    /// <summary>The button to the next page.</summary>
    public required string Next { get; init; }

    /// <summary>The button that sends the form.</summary>
    public required string Send { get; init; }

    /// <summary>The mark of a field that needs no answer.</summary>
    public required string Optional { get; init; }

    /// <summary>The sentence that says what the asterisk means; <c>{0}</c> is the asterisk.</summary>
    public required string RequiredNote { get; init; }

    /// <summary>What a screen reader says for the asterisk in <see cref="RequiredNote"/>.</summary>
    public required string Asterisk { get; init; }

    /// <summary>The error summary's heading.</summary>
    public required string ErrorSummary { get; init; }

    /// <summary>The progress in words; <c>{0}</c> is the current page's number, <c>{1}</c> the number of pages.</summary>
    public required string Step { get; init; }

    /// <summary>The name of the list of steps.</summary>
    public required string Progress { get; init; }

    /// <summary>The step of a page without a title; <c>{0}</c> is its number.</summary>
    public required string Page { get; init; }

    /// <summary>The label of the second input of an email field that asks for the address twice.</summary>
    public required string ConfirmEmail { get; init; }

    /// <summary>The character counter before the runtime keeps it current; <c>{0}</c> is the limit.</summary>
    public required string Counter { get; init; }

    /// <summary>The line with the entry's reference; <c>{0}</c> is the reference.</summary>
    public required string Reference { get; init; }

    /// <summary>The heading of the notice of a form that opens later; <c>{0}</c> is when, in the site's time zone.</summary>
    public required string OpensHeading { get; init; }

    /// <summary>The text of the notice of a form that opens later.</summary>
    public required string OpensText { get; init; }

    /// <summary>The heading of the notice of a closed form.</summary>
    public required string ClosedHeading { get; init; }

    /// <summary>The text of the notice of a closed form.</summary>
    public required string ClosedText { get; init; }

    /// <summary>The heading of the notice of a form that holds as many entries as it takes.</summary>
    public required string FullHeading { get; init; }

    /// <summary>The text of the notice of a form that holds as many entries as it takes.</summary>
    public required string FullText { get; init; }

    /// <summary>The words of a culture, of its more general culture, or English.</summary>
    /// <param name="culture">The culture, such as <c>de-CH</c>.</param>
    public static RenderingWords Of(string? culture)
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
