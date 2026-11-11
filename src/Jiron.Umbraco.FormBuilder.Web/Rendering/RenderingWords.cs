using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Reflection;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

/// <summary>
/// The words a rendered form brings of its own (ADR-0093), in English and German here and in French, Italian, Dutch,
/// Spanish and Danish in <c>lang/</c> (ADR-0230). A culture without its own words takes its more general culture's,
/// then English. A form's own labels and messages win over these. A text with <c>{0}</c> takes a value there.
/// </summary>
internal sealed partial record RenderingWords
{
    // The letters outside ASCII the German words need.
    private const char AUmlaut = (char)0xE4;
    private const char CapitalAUmlaut = (char)0xC4;
    private const char OUmlaut = (char)0xF6;
    private const char UUmlaut = (char)0xFC;
    private const char SharpS = (char)0xDF;

    // The words' properties in the order they are declared.
    private static readonly ImmutableArray<PropertyInfo> Properties = [.. typeof(RenderingWords).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(property => property.PropertyType == typeof(string)).OrderBy(property => property.MetadataToken)];

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
            Required = "required",
            Step = "Page {0} of {1}",
            Progress = "Progress",
            Page = "Page {0}",
            ConfirmEmail = "Repeat your email address",
            Counter = "Up to {0} characters",
            CounterLeft = "{0} characters left",
            CounterLeftOne = "{0} character left",
            CounterOver = "{0} characters too many",
            CounterOverOne = "{0} character too many",
            RankingMoved = "{0} moved to place {1}.",
            RowRemoved = "{0} removed.",
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
            AlreadySentHeading = "You've already sent this form",
            AlreadySentText = "Each member can send it once.",
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
            RepeaterRow = "Row {0}",
            AddRow = "Add a row",
            RemoveRow = "Remove",
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
            SignatureHand = "Sign with your finger or the mouse, or type your name instead.",
            SignatureDrawn = "Your signature, drawn",
            SignatureClear = "Clear",
            SignatureUndoClear = "Undo clear",
            SignatureTypeInstead = "Type your name instead",
            SignatureByHand = "Sign by hand instead",
            ThankYou = "Thank you. We have received your message.",
            Continue = "Continue",
            GoNow = "Go now",
            StayHere = "Stay here",
            CountdownOne = "Taking you to the next page in {0} second.",
            CountdownOther = "Taking you to the next page in {0} seconds.",
            Sending = "Sending your answers",
            ConnectionText = "Check your connection, then send again. Your answers are kept.",
            ServerErrorText = "Something went wrong on our side. Send again in a moment. Your answers are kept.",
            ChangedHeading = "This form was updated while you filled it in",
            ChangedText = "Your answers are kept.",
            ChangedConsent = "An agreement has new wording, so it is no longer ticked: read it again before you send.",
            ChangedConsents = "Some agreements have new wording, so they are no longer ticked: read them again before you send.",
            NotSentHeading = "Your answers weren't sent",
            NotSentText = "Check your answers, then send again. Your answers are kept.",
            TooLargeText = "The files were too large to send, so your answers could not be kept. Choose smaller files and fill in the form again.",
            WaitSecondsText = "This form was sent too often. Wait {0} seconds, then send it again. Your answers are kept.",
            WaitMinutesText = "This form was sent too often. Wait {0} minutes, then send it again. Your answers are kept.",
            ChooseFilesAgain = "Choose your files again before you send.",
            NoRoomText = "This form can't take files right now. Try again later. Your answers are kept.",
            KeepFile = "Keep {0} ({1})",
            ChooseFile = "Choose a file",
            ChooseFiles = "Choose files",
            TakePhoto = "Take a photo",
            DragFile = "Drag a file here, or",
            DragFiles = "Drag files here, or",
            YourFiles = "Your files",
            FileUploading = "{0}% uploaded",
            FileUploaded = "{0} is uploaded.",
            RemoveFile = "Remove {0}",
            StopUploading = "Stop uploading {0}",
            TryAgain = "Try again",
            TryAgainFor = " to upload {0}",
            UploadStopped = "The upload stopped before it finished.",
            UploadLater = "This form can't take files right now. Try again later. Your answers are kept.",
            Kilobytes = "{0} KB",
            Megabytes = "{0} MB",
            Honeypot = "Leave this field empty",
            Change = "Change",
            NotAnswered = "Not answered",
            HiddenAnswer = "Hidden for your privacy",
            YourAnswers = "Your answers",
            PrintAnswers = "Print your answers",
            FillInAgain = "Fill in the form again",
            CopyShown = "Your answers show only once, right after sending.",
        },
        ["de"] = new RenderingWords
        {
            Back = "Zur" + UUmlaut + "ck",
            Next = "Weiter",
            Send = "Senden",
            Optional = "(optional)",
            RequiredNote = "Fragen mit {0} m" + UUmlaut + "ssen beantwortet werden.",
            Asterisk = "Sternchen",
            Required = "erforderlich",
            Step = "Seite {0} von {1}",
            Progress = "Fortschritt",
            Page = "Seite {0}",
            ConfirmEmail = "E-Mail-Adresse wiederholen",
            Counter = "Bis zu {0} Zeichen",
            CounterLeft = "Noch {0} Zeichen",
            CounterLeftOne = "Noch {0} Zeichen",
            CounterOver = "{0} Zeichen zu viel",
            CounterOverOne = "{0} Zeichen zu viel",
            RankingMoved = "{0} auf Platz {1} verschoben.",
            RowRemoved = "{0} entfernt.",
            Reference = "Ihre Referenznummer lautet {0}",
            OpensHeading = "Dieses Formular " + OUmlaut + "ffnet am {0}",
            OpensText = "Kommen Sie dann wieder, um es auszuf" + UUmlaut + "llen.",
            ClosedHeading = "Dieses Formular ist geschlossen",
            ClosedText = "Es nimmt keine Antworten mehr an.",
            FullHeading = "Dieses Formular ist voll",
            FullText = "Die H" + OUmlaut + "chstzahl an Antworten ist erreicht.",
            SignInHeading = "Dieses Formular ist nur f" + UUmlaut + "r Mitglieder",
            SignInText = "Melden Sie sich an, um es auszuf" + UUmlaut + "llen.",
            GroupsHeading = "Dieses Formular ist f" + UUmlaut + "r Ihr Konto nicht freigegeben",
            GroupsText = "Es ist nur f" + UUmlaut + "r Mitglieder bestimmter Gruppen.",
            AlreadySentHeading = "Sie haben dieses Formular bereits gesendet",
            AlreadySentText = "Jedes Mitglied kann es einmal senden.",
            PendingHeading = "Dieses Formular wartet auf ein Update",
            PendingText = "Auf der Website ist hier nichts zu sehen, bis das Datenbank-Update von Jiron Form Builder ausgef" + UUmlaut + "hrt wurde. Diesen Hinweis sieht nur, wer im Backoffice angemeldet ist.",
            Other = "Sonstiges",
            OtherText = "Ihre eigene Antwort",
            Choose = "Bitte w" + AUmlaut + "hlen",
            ChooseCountry = "Land w" + AUmlaut + "hlen",
            SeveralHint = "Um mehr als eine Antwort zu w" + AUmlaut + "hlen, halten Sie beim Klicken Strg gedr" + UUmlaut + "ckt (am Mac Cmd).",
            NoChoices = "Hier gibt es noch nichts zur Auswahl.",
            ChoicesUnavailable = "Die Optionen konnten nicht geladen werden. Bitte versuchen Sie es sp" + AUmlaut + "ter noch einmal.",
            MoveUp = "{0} nach oben verschieben",
            MoveDown = "{0} nach unten verschieben",
            Row = "Zeile {0}",
            RepeaterRow = "Zeile {0}",
            AddRow = "Zeile hinzuf" + UUmlaut + "gen",
            RemoveRow = "Entfernen",
            NameTitle = "Titel",
            NameFirst = "Vorname",
            NameMiddle = "Zweiter Vorname",
            NameLast = "Nachname",
            Street = "Stra" + SharpS + "e und Hausnummer",
            Street2 = "Adresszusatz",
            Postcode = "Postleitzahl",
            City = "Ort",
            Region = "Bundesland, Kanton oder Region",
            Country = "Land",
            SignatureName = "Ihr vollst" + AUmlaut + "ndiger Name",
            SignatureTyped = "Geben Sie Ihren vollst" + AUmlaut + "ndigen Namen ein. Er gilt als Ihre Unterschrift.",
            SignatureHand = "Unterschreiben Sie mit dem Finger oder der Maus, oder geben Sie stattdessen Ihren Namen ein.",
            SignatureDrawn = "Ihre Unterschrift, gezeichnet",
            SignatureClear = "L" + OUmlaut + "schen",
            SignatureUndoClear = "L" + OUmlaut + "schen r" + UUmlaut + "ckg" + AUmlaut + "ngig machen",
            SignatureTypeInstead = "Stattdessen Namen eingeben",
            SignatureByHand = "Stattdessen von Hand unterschreiben",
            ThankYou = "Vielen Dank. Wir haben Ihre Nachricht erhalten.",
            Continue = "Weiter",
            GoNow = "Jetzt weiter",
            StayHere = "Hier bleiben",
            CountdownOne = "In {0} Sekunde geht es weiter zur n" + AUmlaut + "chsten Seite.",
            CountdownOther = "In {0} Sekunden geht es weiter zur n" + AUmlaut + "chsten Seite.",
            Sending = "Ihre Antworten werden gesendet",
            ConnectionText = "Pr" + UUmlaut + "fen Sie Ihre Internetverbindung und senden Sie erneut. Ihre Antworten bleiben erhalten.",
            ServerErrorText = "Bei uns ist etwas schiefgegangen. Senden Sie gleich noch einmal. Ihre Antworten bleiben erhalten.",
            ChangedHeading = "Dieses Formular wurde ge" + AUmlaut + "ndert, w" + AUmlaut + "hrend Sie es ausgef" + UUmlaut + "llt haben",
            ChangedText = "Ihre Antworten bleiben erhalten.",
            ChangedConsent = "Eine Einwilligung hat einen neuen Wortlaut und ist deshalb nicht mehr angehakt: Lesen Sie sie vor dem Senden noch einmal.",
            ChangedConsents = "Einige Einwilligungen haben einen neuen Wortlaut und sind deshalb nicht mehr angehakt: Lesen Sie sie vor dem Senden noch einmal.",
            NotSentHeading = "Ihre Antworten wurden nicht gesendet",
            NotSentText = "Pr" + UUmlaut + "fen Sie Ihre Antworten und senden Sie erneut. Ihre Antworten bleiben erhalten.",
            TooLargeText = "Die Dateien waren zu gro" + SharpS + ", um sie zu senden, deshalb konnten Ihre Antworten nicht erhalten bleiben. W" + AUmlaut + "hlen Sie kleinere Dateien und f" + UUmlaut + "llen Sie das Formular erneut aus.",
            WaitSecondsText = "Dieses Formular wurde zu oft gesendet. Warten Sie {0} Sekunden und senden Sie es dann erneut. Ihre Antworten bleiben erhalten.",
            WaitMinutesText = "Dieses Formular wurde zu oft gesendet. Warten Sie {0} Minuten und senden Sie es dann erneut. Ihre Antworten bleiben erhalten.",
            ChooseFilesAgain = "W" + AUmlaut + "hlen Sie Ihre Dateien vor dem Senden erneut aus.",
            NoRoomText = "Dieses Formular kann gerade keine Dateien annehmen. Versuchen Sie es sp" + AUmlaut + "ter noch einmal. Ihre Antworten bleiben erhalten.",
            KeepFile = "{0} ({1}) behalten",
            ChooseFile = "Datei ausw" + AUmlaut + "hlen",
            ChooseFiles = "Dateien ausw" + AUmlaut + "hlen",
            TakePhoto = "Foto aufnehmen",
            DragFile = "Datei hierher ziehen oder",
            DragFiles = "Dateien hierher ziehen oder",
            YourFiles = "Ihre Dateien",
            FileUploading = "{0}\u00a0% hochgeladen",
            FileUploaded = "{0} ist hochgeladen.",
            RemoveFile = "{0} entfernen",
            StopUploading = "Hochladen von {0} abbrechen",
            TryAgain = "Erneut versuchen",
            TryAgainFor = ", um {0} hochzuladen",
            UploadStopped = "Das Hochladen wurde unterbrochen.",
            UploadLater = "Dieses Formular kann gerade keine Dateien annehmen. Versuchen Sie es sp" + AUmlaut + "ter noch einmal. Ihre Antworten bleiben erhalten.",
            Kilobytes = "{0}\u00a0KB",
            Megabytes = "{0}\u00a0MB",
            Honeypot = "Lassen Sie dieses Feld leer",
            Change = CapitalAUmlaut + "ndern",
            NotAnswered = "Nicht beantwortet",
            HiddenAnswer = "Zum Schutz Ihrer Daten ausgeblendet",
            YourAnswers = "Ihre Antworten",
            PrintAnswers = "Ihre Antworten drucken",
            FillInAgain = "Formular erneut ausf" + UUmlaut + "llen",
            CopyShown = "Ihre Antworten werden nur einmal angezeigt, direkt nach dem Senden.",
        },
        ["fr"] = French,
        ["it"] = Italian,
        ["nl"] = Dutch,
        ["es"] = Spanish,
        ["da"] = Danish,
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    // Each word's property by its name in a key after the prefix (ADR-0230), read once.
    private static readonly ImmutableArray<(string Name, PropertyInfo Property)> Named =
        [.. Properties.Select(property => (JsonNamingPolicy.CamelCase.ConvertName(property.Name), property))];

    /// <summary>The cultures with words of their own.</summary>
    public static IReadOnlyCollection<string> Cultures => Words.Keys;

    /// <summary>
    /// These words with the ones a form or its site rewords in their place (ADR-0230), by their keys
    /// (<c>form.chooseFile</c>); these words themselves when they reword none.
    /// </summary>
    /// <param name="texts">The visitor texts a page shows, by key.</param>
    public RenderingWords With(IReadOnlyDictionary<string, string>? texts)
    {
        if (texts is not { Count: > 0 })
        {
            return this;
        }

        RenderingWords? copy = null;
        foreach (var (name, property) in Named)
        {
            if (texts.TryGetValue(VisitorTextKeys.FormPrefix + name, out var wording))
            {
                copy ??= this with { };
                property.SetValue(copy, wording);
            }
        }

        return copy ?? this;
    }

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

    /// <summary>
    /// What a screen reader hears for the asterisk of a required group whose controls cannot carry <c>required</c>: a
    /// group of boxes, a grid's row of boxes and a ranking (6.8c).
    /// </summary>
    public required string Required { get; init; }

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

    /// <summary>The heading in place of a form that takes one entry per member, to a member who has sent it (ADR-0231).</summary>
    public required string AlreadySentHeading { get; init; }

    /// <summary>The sentence under it.</summary>
    public required string AlreadySentText { get; init; }

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

    /// <summary>What the runtime says of a ranking's option moved, {0} its label and {1} its place.</summary>
    public required string RankingMoved { get; init; }

    /// <summary>What the runtime says of a row removed, {0} its legend.</summary>
    public required string RowRemoved { get; init; }

    /// <summary>A counter with characters left, {0} how many.</summary>
    public required string CounterLeft { get; init; }

    /// <summary>A counter with one character left, {0} the number.</summary>
    public required string CounterLeftOne { get; init; }

    /// <summary>A counter past its limit, {0} by how many.</summary>
    public required string CounterOver { get; init; }

    /// <summary>A counter one past its limit, {0} the number.</summary>
    public required string CounterOverOne { get; init; }

    /// <summary>The words of a ranking's button that moves an option down; <c>{0}</c> is the option's label.</summary>
    public required string MoveDown { get; init; }

    /// <summary>The statement of a grid's row without one; <c>{0}</c> is its number.</summary>
    public required string Row { get; init; }

    /// <summary>The legend of a repeatable group's row without a row label; <c>{0}</c> is its number from 1.</summary>
    public required string RepeaterRow { get; init; }

    /// <summary>The words of the button that adds a row to a repeatable group without an add label.</summary>
    public required string AddRow { get; init; }

    /// <summary>The words of the button that removes a row without a remove label; the row's legend follows, unseen.</summary>
    public required string RemoveRow { get; init; }

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

    /// <summary>The line above the pad.</summary>
    public required string SignatureHand { get; init; }

    /// <summary>What a screen reader calls the pad.</summary>
    public required string SignatureDrawn { get; init; }

    /// <summary>The button that clears the pad.</summary>
    public required string SignatureClear { get; init; }

    /// <summary>The same button until the next stroke.</summary>
    public required string SignatureUndoClear { get; init; }

    /// <summary>The way from the pad to the typed name.</summary>
    public required string SignatureTypeInstead { get; init; }

    /// <summary>The way from the typed name to the pad.</summary>
    public required string SignatureByHand { get; init; }

    /// <summary>The outcome of a form sent without an outcome of its own, or whose outcome could not be followed.</summary>
    public required string ThankYou { get; init; }

    /// <summary>The link after an outcome's message to the redirect that a page without JavaScript does not count down to.</summary>
    public required string Continue { get; init; }

    /// <summary>The link after an outcome's message to the redirect the page counts down to (ADR-0022).</summary>
    public required string GoNow { get; init; }

    /// <summary>The button beside it that stops the countdown (WCAG 2.2.1).</summary>
    public required string StayHere { get; init; }

    /// <summary>The line that says when the page moves on, with one second left: {0} is the second.</summary>
    public required string CountdownOne { get; init; }

    /// <summary>The line that says when the page moves on, with more seconds left: {0} is the seconds.</summary>
    public required string CountdownOther { get; init; }

    /// <summary>What the live region says while a send is on its way (ADR-0022).</summary>
    public required string Sending { get; init; }

    /// <summary>The notice's text when a send got no answer in time, or none at all.</summary>
    public required string ConnectionText { get; init; }

    /// <summary>The notice's text when the server failed on a send.</summary>
    public required string ServerErrorText { get; init; }

    /// <summary>The heading of the notice after a post that met a form published again meanwhile (ADR-0100).</summary>
    public required string ChangedHeading { get; init; }

    /// <summary>That notice's sentence that the answers are kept.</summary>
    public required string ChangedText { get; init; }

    /// <summary>That notice's sentence when one consent's wording changed, so it is no longer ticked.</summary>
    public required string ChangedConsent { get; init; }

    /// <summary>That notice's sentence when several consents' wordings changed.</summary>
    public required string ChangedConsents { get; init; }

    /// <summary>The heading of the notice after a post the form could not take, whose answers are kept.</summary>
    public required string NotSentHeading { get; init; }

    /// <summary>That notice's text.</summary>
    public required string NotSentText { get; init; }

    /// <summary>That notice's text after a post too large to read, whose answers are lost (ADR-0102).</summary>
    public required string TooLargeText { get; init; }

    /// <summary>The text of a post with files when the site can take no more of them (ADR-0148).</summary>
    public required string NoRoomText { get; init; }

    /// <summary>The button that chooses the one file a field takes, once the runtime runs.</summary>
    public required string ChooseFile { get; init; }

    /// <summary>The button that chooses several files.</summary>
    public required string ChooseFiles { get; init; }

    /// <summary>The button that takes a picture with the device's camera.</summary>
    public required string TakePhoto { get; init; }

    /// <summary>The line in a drop zone before the one file's button.</summary>
    public required string DragFile { get; init; }

    /// <summary>The line in a drop zone before the files' button.</summary>
    public required string DragFiles { get; init; }

    /// <summary>The name, for screen readers, of the list of a field's files.</summary>
    public required string YourFiles { get; init; }

    /// <summary>A file's state while it is on its way: {0} is how much of it, in percent.</summary>
    public required string FileUploading { get; init; }

    /// <summary>What the live region says once a file is there: {0} is its name.</summary>
    public required string FileUploaded { get; init; }

    /// <summary>The name, for screen readers, of the button that takes a file off the list: {0} is its name.</summary>
    public required string RemoveFile { get; init; }

    /// <summary>The name, for screen readers, of that button while the file is on its way: {0} is its name.</summary>
    public required string StopUploading { get; init; }

    /// <summary>The button that sends a file again whose upload stopped.</summary>
    public required string TryAgain { get; init; }

    /// <summary>What follows it, for screen readers: {0} is the file's name.</summary>
    public required string TryAgainFor { get; init; }

    /// <summary>A file's error when its upload stopped.</summary>
    public required string UploadStopped { get; init; }

    /// <summary>A file's error when the site cannot take a file now.</summary>
    public required string UploadLater { get; init; }

    /// <summary>That notice's text after a post over a rate limit, with the seconds to wait, always two or more (ADR-0137).</summary>
    public required string WaitSecondsText { get; init; }

    /// <summary>That notice's text after a post over a rate limit, with the minutes to wait, always two or more (ADR-0137).</summary>
    public required string WaitMinutesText { get; init; }

    /// <summary>That notice's sentence when a post over a rate limit held files, which it did not store.</summary>
    public required string ChooseFilesAgain { get; init; }

    /// <summary>The label of a kept file's box: its name and its size (ADR-0102).</summary>
    public required string KeepFile { get; init; }

    /// <summary>A size in kilobytes, the number first.</summary>
    public required string Kilobytes { get; init; }

    /// <summary>A size in megabytes, the number first.</summary>
    public required string Megabytes { get; init; }

    /// <summary>The honeypot's label, for a screen reader that reaches it all the same (ADR-0012).</summary>
    public required string Honeypot { get; init; }

    /// <summary>The button of an answer on the summary page that opens its page (ADR-0122).</summary>
    public required string Change { get; init; }

    /// <summary>What an unanswered question reads on the summary page.</summary>
    public required string NotAnswered { get; init; }

    /// <summary>What a screen reader hears for a sensitive answer that the summary page masks.</summary>
    public required string HiddenAnswer { get; init; }

    /// <summary>The heading of the copy of the answers when it is one untitled page (ADR-0123).</summary>
    public required string YourAnswers { get; init; }

    /// <summary>The button that prints the copy of the answers.</summary>
    public required string PrintAnswers { get; init; }

    /// <summary>The link below the copy of the answers to the form again.</summary>
    public required string FillInAgain { get; init; }

    /// <summary>The line in the copy's place on a later view of the page after sending.</summary>
    public required string CopyShown { get; init; }

    /// <summary>
    /// The words by their names in camel case, in the order they are declared, for the designer's canvas, which draws a
    /// form with them (ADR-0168).
    /// </summary>
    public IReadOnlyDictionary<string, string> ByName()
    {
        var words = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in Properties)
        {
            words[JsonNamingPolicy.CamelCase.ConvertName(property.Name)] = (string)property.GetValue(this)!;
        }

        return words;
    }

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
