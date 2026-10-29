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
    private const char AUmlaut = (char)0xE4;
    private const char CapitalAUmlaut = (char)0xC4;
    private const char OUmlaut = (char)0xF6;
    private const char UUmlaut = (char)0xFC;
    private const char SharpS = (char)0xDF;

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
            SignInHeading = "This form is for members",
            SignInText = "Sign in to fill it in.",
            GroupsHeading = "This form is not open to your account",
            GroupsText = "It is for members of certain groups.",
            PendingHeading = "This form waits for an update",
            PendingText = "Visitors see nothing here until Jiron Form Builder's database update has run. Only signed-in backoffice users see this note.",
            Other = "Other",
            OtherText = "Your own answer",
            Choose = "Choose one",
            ChooseCountry = "Choose a country",
            SeveralHint = "To choose more than one, hold Ctrl (Cmd on a Mac) while you click.",
            NoChoices = "There is nothing to choose from yet.",
            ChoicesUnavailable = "The choices could not be loaded. Please try again later.",
            MoveUp = "Move {0} up",
            MoveDown = "Move {0} down",
            Row = "Row {0}",
            NameTitle = "Title",
            NameFirst = "First name",
            NameMiddle = "Middle name",
            NameLast = "Last name",
            Street = "Street and number",
            Street2 = "Address line 2",
            Postcode = "Postcode",
            City = "Town",
            Region = "State or region",
            Country = "Country",
            SignatureName = "Your full name",
            SignatureTyped = "Type your full name. It counts as your signature.",
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
            SignInHeading = "Dieses Formular ist f" + UUmlaut + "r Mitglieder",
            SignInText = "Melden Sie sich an, um es auszuf" + UUmlaut + "llen.",
            GroupsHeading = "Dieses Formular steht Ihrem Konto nicht offen",
            GroupsText = "Es ist nur f" + UUmlaut + "r Mitglieder bestimmter Gruppen.",
            PendingHeading = "Dieses Formular wartet auf ein Update",
            PendingText = "Besucher sehen hier nichts, bis das Datenbank-Update von Jiron Form Builder gelaufen ist. Diesen Hinweis sehen nur angemeldete Backoffice-Benutzer.",
            Other = "Sonstiges",
            OtherText = "Ihre eigene Antwort",
            Choose = "Bitte w" + AUmlaut + "hlen",
            ChooseCountry = "Land w" + AUmlaut + "hlen",
            SeveralHint = "Um mehr als eine Antwort zu w" + AUmlaut + "hlen, halten Sie beim Klicken Strg gedr" + UUmlaut + "ckt (am Mac Cmd).",
            NoChoices = "Hier gibt es noch nichts zur Auswahl.",
            ChoicesUnavailable = "Die Auswahl konnte nicht geladen werden. Bitte versuchen Sie es sp" + AUmlaut + "ter noch einmal.",
            MoveUp = "{0} nach oben verschieben",
            MoveDown = "{0} nach unten verschieben",
            Row = "Zeile {0}",
            NameTitle = "Titel",
            NameFirst = "Vorname",
            NameMiddle = "Zweiter Vorname",
            NameLast = "Nachname",
            Street = "Stra" + SharpS + "e und Hausnummer",
            Street2 = "Adresszusatz",
            Postcode = "Postleitzahl",
            City = "Ort",
            Region = "Bundesland oder Region",
            Country = "Land",
            SignatureName = "Ihr vollst" + AUmlaut + "ndiger Name",
            SignatureTyped = "Geben Sie Ihren vollst" + AUmlaut + "ndigen Namen ein. Er gilt als Ihre Unterschrift.",
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

    /// <summary>The heading of the notice of a members-only form to a visitor who is not signed in.</summary>
    public required string SignInHeading { get; init; }

    /// <summary>The text of the notice of a members-only form to a visitor who is not signed in.</summary>
    public required string SignInText { get; init; }

    /// <summary>The heading of the notice of a form for member groups to a member of none of them.</summary>
    public required string GroupsHeading { get; init; }

    /// <summary>The text of the notice of a form for member groups to a member of none of them.</summary>
    public required string GroupsText { get; init; }

    /// <summary>The heading of the note a signed-in backoffice user sees in a form's place while the migrations are pending.</summary>
    public required string PendingHeading { get; init; }

    /// <summary>The text of the note a signed-in backoffice user sees in a form's place while the migrations are pending.</summary>
    public required string PendingText { get; init; }

    /// <summary>The label of the "Other" choice of a field that does not label it.</summary>
    public required string Other { get; init; }

    /// <summary>The label, for screen readers, of the input that takes the text of the "Other" choice.</summary>
    public required string OtherText { get; init; }

    /// <summary>The first line of a dropdown that does not say its own.</summary>
    public required string Choose { get; init; }

    /// <summary>The first line of a country's dropdown.</summary>
    public required string ChooseCountry { get; init; }

    /// <summary>The line below a list box that says how to choose several options.</summary>
    public required string SeveralHint { get; init; }

    /// <summary>What a field with options shows while it has none to offer, such as a list below a parent not answered yet.</summary>
    public required string NoChoices { get; init; }

    /// <summary>What a field with options shows when their source could not give them.</summary>
    public required string ChoicesUnavailable { get; init; }

    /// <summary>The words of a ranking's button that moves an option up; <c>{0}</c> is the option's label.</summary>
    public required string MoveUp { get; init; }

    /// <summary>The words of a ranking's button that moves an option down; <c>{0}</c> is the option's label.</summary>
    public required string MoveDown { get; init; }

    /// <summary>The statement of a grid's row without one; <c>{0}</c> is its number.</summary>
    public required string Row { get; init; }

    /// <summary>The label of a name's title, such as Dr.</summary>
    public required string NameTitle { get; init; }

    /// <summary>The label of a name's first name.</summary>
    public required string NameFirst { get; init; }

    /// <summary>The label of a name's middle name.</summary>
    public required string NameMiddle { get; init; }

    /// <summary>The label of a name's last name.</summary>
    public required string NameLast { get; init; }

    /// <summary>The label of an address's street and number.</summary>
    public required string Street { get; init; }

    /// <summary>The label of an address's second street line.</summary>
    public required string Street2 { get; init; }

    /// <summary>The label of an address's postcode.</summary>
    public required string Postcode { get; init; }

    /// <summary>The label of an address's town or city.</summary>
    public required string City { get; init; }

    /// <summary>The label of an address's region, state or canton.</summary>
    public required string Region { get; init; }

    /// <summary>The label of an address's country.</summary>
    public required string Country { get; init; }

    /// <summary>The label, for screen readers, of the name a visitor types instead of signing by hand.</summary>
    public required string SignatureName { get; init; }

    /// <summary>The line above that name, which says typing it counts as signing.</summary>
    public required string SignatureTyped { get; init; }

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
