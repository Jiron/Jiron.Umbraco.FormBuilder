using System.Collections.Frozen;

namespace Jiron.Umbraco.FormBuilder.Logic;

/// <summary>A message that counts: the words for one and for any other number.</summary>
/// <param name="One">The words for 1.</param>
/// <param name="Other">The words for any other number.</param>
internal sealed record PluralWords(string One, string Other)
{
    /// <summary>The words for a count: English and German say one only for 1; 14.2 adds the rules of five more languages.</summary>
    public string For(long count) => count == 1 ? One : Other;
}

/// <summary>
/// The package's default messages for a visitor's errors (ADR-0099), in English and German so far; 14.2 adds the other
/// five languages. Each is a whole sentence with named values in braces, never joined from pieces, as
/// <c>docs/ux/copy.md</c> lists them, and <c>tests/vectors/messages.json</c> holds both runtimes to them. A culture
/// without its own words takes its more general culture's, then English.
/// </summary>
internal sealed record VisitorMessageWords
{
    // The letters outside ASCII the German words need.
    private const char AUmlaut = (char)0xE4;
    private const char OUmlaut = (char)0xF6;
    private const char UUmlaut = (char)0xFC;
    private const char SharpS = (char)0xDF;

    private static readonly FrozenDictionary<string, VisitorMessageWords> Words = new Dictionary<string, VisitorMessageWords>(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = new VisitorMessageWords
        {
            Or = "or",
            Kilobytes = "{size} KB",
            Megabytes = "{size} MB",
            Answer = "Enter an answer",
            Email = "Enter an email address",
            Phone = "Enter a phone number",
            Url = "Enter a web address",
            Number = "Enter a number",
            Date = "Enter a date",
            Time = "Enter a time",
            DateTime = "Enter a date and a time",
            Rating = "Choose a rating",
            Scale = "Choose a point on the scale",
            Value = "Choose a value",
            Choose = "Choose an answer",
            ChooseSeveral = "Choose at least one answer",
            GridRows = "Choose an answer in each row",
            GridRow = "Choose an answer in this row",
            Order = "Put the options in order",
            Tick = "Tick the box",
            YesNo = "Choose {yes} or {no}",
            Agree = "Tick the box to agree",
            Country = "Choose a country",
            Name = "Enter the name in full",
            Address = "Enter the address in full",
            File = "Choose a file",
            Sign = "Add your signature, or type your name instead",
            AnswerThis = "Answer this question",
            EmailFormat = "Enter a complete email address, for example name@example.com",
            EmailDomain = "Use an email address at another domain",
            PhoneFormat = "Enter a phone number with digits, and a + for the country code",
            UrlFormat = "Enter a complete web address, for example https://example.com",
            NumberFormat = "Enter a number, for example {example}",
            DateFormat = "Enter a valid date",
            TimeFormat = "Enter a valid time",
            DateTimeFormat = "Enter a valid date and time",
            RatingRange = "Choose a rating from 1 to {count}",
            ScaleRange = "Choose a point from {from} to {to}",
            ValueRange = "Choose a value from {min} to {max}",
            Options = "Choose one of the options given",
            CountryList = "Choose a country from the list",
            NameCheck = "Check the name",
            AddressCheck = "Check the address",
            SignAgain = "Sign again, or type your name instead",
            CheckThis = "Check this answer",
            MinLength = new("Enter at least {min} character", "Enter at least {min} characters"),
            MaxLength = new("Enter at most {max} character", "Enter at most {max} characters"),
            MinWords = new("Enter at least {min} word", "Enter at least {min} words"),
            MaxWords = new("Enter at most {max} word", "Enter at most {max} words"),
            NumberBetween = "Enter a number from {min} to {max}",
            NumberFrom = "Enter a number of {min} or more",
            NumberUpTo = "Enter a number of {max} or less",
            TimeBetween = "Enter a time from {min} to {max}",
            TimeFrom = "Enter a time of {min} or later",
            TimeUpTo = "Enter a time of {max} or earlier",
            NumberStep = "Enter a number in steps of {step}",
            NumberStepFrom = "Enter a number in steps of {step}, starting at {min}",
            WholeNumber = "Enter a whole number",
            Places = new("Enter a number with at most {places} decimal place", "Enter a number with at most {places} decimal places"),
            ValueStep = "Choose a value in steps of {step}",
            TimeStep = new("Choose a time in steps of {minutes} minute", "Choose a time in steps of {minutes} minutes"),
            TimeStepFrom = new("Choose a time in steps of {minutes} minute from {min}", "Choose a time in steps of {minutes} minutes from {min}"),
            DateBetween = "Choose a date from {min} to {max}",
            DateFrom = "Choose a date on or after {min}",
            DateUpTo = "Choose a date on or before {max}",
            WeekdayBetween = "Choose a date from {min} to {max}, {monday} to {friday}",
            WeekdayFrom = "Choose a date on or after {min}, {monday} to {friday}",
            WeekdayUpTo = "Choose a date on or before {max}, {monday} to {friday}",
            Weekday = "Choose a day from {monday} to {friday}",
            DateUnavailable = "This date is not available. Choose another one.",
            SelectionsExactly = new("Choose {count} answer", "Choose {count} answers"),
            SelectionsBetween = "Choose from {min} to {max} answers",
            SelectionsAtLeast = new("Choose at least {min} answer", "Choose at least {min} answers"),
            SelectionsAtMost = new("Choose at most {max} answer", "Choose at most {max} answers"),
            FileFormat = "Choose a file in the format {formats}",
            FileTooLarge = "This file is too large. Choose a smaller one.",
            FileLargerThan = "This file is larger than {size}. Choose a smaller one.",
            FilesLargerThan = "One of the files is larger than {size}. Choose a smaller one.",
            FilesAtMost = new("Choose only {max} file", "Choose at most {max} files"),
            OptionFull = "{option} is now full. Choose another option.",
            ChoiceFull = "One of your choices is now full. Choose another option.",
            Confirmation = "Enter the same email address twice",
            Digits = "Enter a number with fewer digits",
            RowsAtLeast = new("Add at least {min} row", "Add at least {min} rows"),
            RowsAtMost = new("Remove rows to keep at most {max}", "Remove rows to keep at most {max}"),
            Pattern = "Enter the answer in the format asked for",
            SameAs = "Enter the same as for {field}",
            Taken = "This answer has already been given. Enter another one.",
            Empty = "Answer at least one question before you send the form",
            SummaryHeading = new("This answer needs a change", "These answers need a change"),
            SummaryEntry = "{label}: {message}",
            SummaryRowEntry = "{label} ({row}): {message}",
        },
        ["de"] = new VisitorMessageWords
        {
            Or = "oder",
            Kilobytes = "{size} KB",
            Megabytes = "{size} MB",
            Answer = "Geben Sie eine Antwort ein",
            Email = "Geben Sie eine E-Mail-Adresse ein",
            Phone = "Geben Sie eine Telefonnummer ein",
            Url = "Geben Sie eine Webadresse ein",
            Number = "Geben Sie eine Zahl ein",
            Date = "Geben Sie ein Datum ein",
            Time = "Geben Sie eine Uhrzeit ein",
            DateTime = "Geben Sie ein Datum und eine Uhrzeit ein",
            Rating = "W" + AUmlaut + "hlen Sie eine Bewertung",
            Scale = "W" + AUmlaut + "hlen Sie einen Wert auf der Skala",
            Value = "W" + AUmlaut + "hlen Sie einen Wert",
            Choose = "W" + AUmlaut + "hlen Sie eine Antwort",
            ChooseSeveral = "W" + AUmlaut + "hlen Sie mindestens eine Antwort",
            GridRows = "W" + AUmlaut + "hlen Sie in jeder Zeile eine Antwort",
            GridRow = "W" + AUmlaut + "hlen Sie in dieser Zeile eine Antwort",
            Order = "Bringen Sie die Optionen in eine Reihenfolge",
            Tick = "Kreuzen Sie das K" + AUmlaut + "stchen an",
            YesNo = "W" + AUmlaut + "hlen Sie {yes} oder {no}",
            Agree = "Kreuzen Sie das K" + AUmlaut + "stchen an, um zuzustimmen",
            Country = "W" + AUmlaut + "hlen Sie ein Land",
            Name = "Geben Sie den vollst" + AUmlaut + "ndigen Namen ein",
            Address = "Geben Sie die vollst" + AUmlaut + "ndige Adresse ein",
            File = "W" + AUmlaut + "hlen Sie eine Datei",
            Sign = "Unterschreiben Sie, oder geben Sie stattdessen Ihren Namen ein",
            AnswerThis = "Beantworten Sie diese Frage",
            EmailFormat = "Geben Sie eine vollst" + AUmlaut + "ndige E-Mail-Adresse ein, zum Beispiel name@example.com",
            EmailDomain = "Verwenden Sie eine E-Mail-Adresse bei einer anderen Domain",
            PhoneFormat = "Geben Sie eine Telefonnummer mit Ziffern ein, mit einem + f" + UUmlaut + "r die Landesvorwahl",
            UrlFormat = "Geben Sie eine vollst" + AUmlaut + "ndige Webadresse ein, zum Beispiel https://example.com",
            NumberFormat = "Geben Sie eine Zahl ein, zum Beispiel {example}",
            DateFormat = "Geben Sie ein g" + UUmlaut + "ltiges Datum ein",
            TimeFormat = "Geben Sie eine g" + UUmlaut + "ltige Uhrzeit ein",
            DateTimeFormat = "Geben Sie ein g" + UUmlaut + "ltiges Datum und eine g" + UUmlaut + "ltige Uhrzeit ein",
            RatingRange = "W" + AUmlaut + "hlen Sie eine Bewertung von 1 bis {count}",
            ScaleRange = "W" + AUmlaut + "hlen Sie einen Wert von {from} bis {to}",
            ValueRange = "W" + AUmlaut + "hlen Sie einen Wert von {min} bis {max}",
            Options = "W" + AUmlaut + "hlen Sie eine der angebotenen Optionen",
            CountryList = "W" + AUmlaut + "hlen Sie ein Land aus der Liste",
            NameCheck = "Pr" + UUmlaut + "fen Sie den Namen",
            AddressCheck = "Pr" + UUmlaut + "fen Sie die Adresse",
            SignAgain = "Unterschreiben Sie erneut, oder geben Sie stattdessen Ihren Namen ein",
            CheckThis = "Pr" + UUmlaut + "fen Sie diese Antwort",
            MinLength = new("Geben Sie mindestens {min} Zeichen ein", "Geben Sie mindestens {min} Zeichen ein"),
            MaxLength = new("Geben Sie h" + OUmlaut + "chstens {max} Zeichen ein", "Geben Sie h" + OUmlaut + "chstens {max} Zeichen ein"),
            MinWords = new("Geben Sie mindestens {min} Wort ein", "Geben Sie mindestens {min} W" + OUmlaut + "rter ein"),
            MaxWords = new("Geben Sie h" + OUmlaut + "chstens {max} Wort ein", "Geben Sie h" + OUmlaut + "chstens {max} W" + OUmlaut + "rter ein"),
            NumberBetween = "Geben Sie eine Zahl von {min} bis {max} ein",
            NumberFrom = "Geben Sie eine Zahl ab {min} ein",
            NumberUpTo = "Geben Sie eine Zahl bis {max} ein",
            TimeBetween = "Geben Sie eine Uhrzeit von {min} bis {max} ein",
            TimeFrom = "Geben Sie eine Uhrzeit ab {min} ein",
            TimeUpTo = "Geben Sie eine Uhrzeit bis {max} ein",
            NumberStep = "Geben Sie eine Zahl in Schritten von {step} ein",
            NumberStepFrom = "Geben Sie eine Zahl in Schritten von {step} ab {min} ein",
            WholeNumber = "Geben Sie eine ganze Zahl ein",
            Places = new("Geben Sie eine Zahl mit h" + OUmlaut + "chstens {places} Nachkommastelle ein", "Geben Sie eine Zahl mit h" + OUmlaut + "chstens {places} Nachkommastellen ein"),
            ValueStep = "W" + AUmlaut + "hlen Sie einen Wert in Schritten von {step}",
            TimeStep = new("W" + AUmlaut + "hlen Sie eine Uhrzeit im Abstand von {minutes} Minute", "W" + AUmlaut + "hlen Sie eine Uhrzeit im Abstand von {minutes} Minuten"),
            TimeStepFrom = new("W" + AUmlaut + "hlen Sie eine Uhrzeit im Abstand von {minutes} Minute ab {min}", "W" + AUmlaut + "hlen Sie eine Uhrzeit im Abstand von {minutes} Minuten ab {min}"),
            DateBetween = "W" + AUmlaut + "hlen Sie ein Datum vom {min} bis zum {max}",
            DateFrom = "W" + AUmlaut + "hlen Sie ein Datum ab dem {min}",
            DateUpTo = "W" + AUmlaut + "hlen Sie ein Datum bis zum {max}",
            WeekdayBetween = "W" + AUmlaut + "hlen Sie ein Datum vom {min} bis zum {max}, {monday} bis {friday}",
            WeekdayFrom = "W" + AUmlaut + "hlen Sie ein Datum ab dem {min}, {monday} bis {friday}",
            WeekdayUpTo = "W" + AUmlaut + "hlen Sie ein Datum bis zum {max}, {monday} bis {friday}",
            Weekday = "W" + AUmlaut + "hlen Sie einen Tag von {monday} bis {friday}",
            DateUnavailable = "Dieses Datum ist nicht verf" + UUmlaut + "gbar. W" + AUmlaut + "hlen Sie ein anderes.",
            SelectionsExactly = new("W" + AUmlaut + "hlen Sie {count} Antwort", "W" + AUmlaut + "hlen Sie {count} Antworten"),
            SelectionsBetween = "W" + AUmlaut + "hlen Sie {min} bis {max} Antworten",
            SelectionsAtLeast = new("W" + AUmlaut + "hlen Sie mindestens {min} Antwort", "W" + AUmlaut + "hlen Sie mindestens {min} Antworten"),
            SelectionsAtMost = new("W" + AUmlaut + "hlen Sie h" + OUmlaut + "chstens {max} Antwort", "W" + AUmlaut + "hlen Sie h" + OUmlaut + "chstens {max} Antworten"),
            FileFormat = "W" + AUmlaut + "hlen Sie eine Datei im Format {formats}",
            FileTooLarge = "Diese Datei ist zu gro" + SharpS + ". W" + AUmlaut + "hlen Sie eine kleinere.",
            FileLargerThan = "Diese Datei ist gr" + OUmlaut + SharpS + "er als {size}. W" + AUmlaut + "hlen Sie eine kleinere.",
            FilesLargerThan = "Eine der Dateien ist gr" + OUmlaut + SharpS + "er als {size}. W" + AUmlaut + "hlen Sie eine kleinere.",
            FilesAtMost = new("W" + AUmlaut + "hlen Sie nur {max} Datei", "W" + AUmlaut + "hlen Sie h" + OUmlaut + "chstens {max} Dateien"),
            OptionFull = "{option} ist jetzt ausgebucht. W" + AUmlaut + "hlen Sie eine andere Option.",
            ChoiceFull = "Eine Ihrer gew" + AUmlaut + "hlten Optionen ist jetzt ausgebucht. W" + AUmlaut + "hlen Sie eine andere Option.",
            Confirmation = "Geben Sie zweimal dieselbe E-Mail-Adresse ein",
            Digits = "Geben Sie eine Zahl mit weniger Ziffern ein",
            RowsAtLeast = new("F" + UUmlaut + "gen Sie mindestens {min} Zeile hinzu", "F" + UUmlaut + "gen Sie mindestens {min} Zeilen hinzu"),
            RowsAtMost = new("Entfernen Sie Zeilen, sodass h" + OUmlaut + "chstens {max} bleibt", "Entfernen Sie Zeilen, sodass h" + OUmlaut + "chstens {max} bleiben"),
            Pattern = "Geben Sie die Antwort im verlangten Format ein",
            SameAs = "Geben Sie dasselbe ein wie bei {field}",
            Taken = "Diese Antwort wurde bereits gegeben. Geben Sie eine andere ein.",
            Empty = "Beantworten Sie mindestens eine Frage, bevor Sie das Formular senden",
            SummaryHeading = new("Diese Antwort muss noch ge" + AUmlaut + "ndert werden", "Diese Antworten m" + UUmlaut + "ssen noch ge" + AUmlaut + "ndert werden"),
            SummaryEntry = "{label}: {message}",
            SummaryRowEntry = "{label} ({row}): {message}",
        },
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>The languages with words of their own.</summary>
    public static IReadOnlyCollection<string> Languages => Words.Keys;

    /// <summary>The word that joins the last of a list of choices: "PDF, JPG or PNG".</summary>
    public required string Or { get; init; }

    /// <summary>A size below a megabyte.</summary>
    public required string Kilobytes { get; init; }

    /// <summary>A size from a megabyte.</summary>
    public required string Megabytes { get; init; }

    // required, by what the control asks for.

    /// <summary><c>required</c> of a text.</summary>
    public required string Answer { get; init; }

    /// <summary><c>required</c> of an email address.</summary>
    public required string Email { get; init; }

    /// <summary><c>required</c> of a phone number.</summary>
    public required string Phone { get; init; }

    /// <summary><c>required</c> of a web address.</summary>
    public required string Url { get; init; }

    /// <summary><c>required</c> of a number.</summary>
    public required string Number { get; init; }

    /// <summary><c>required</c> of a date.</summary>
    public required string Date { get; init; }

    /// <summary><c>required</c> of a time.</summary>
    public required string Time { get; init; }

    /// <summary><c>required</c> of a date and time.</summary>
    public required string DateTime { get; init; }

    /// <summary><c>required</c> of a rating.</summary>
    public required string Rating { get; init; }

    /// <summary><c>required</c> of a scale.</summary>
    public required string Scale { get; init; }

    /// <summary><c>required</c> of a slider.</summary>
    public required string Value { get; init; }

    /// <summary><c>required</c> of one choice.</summary>
    public required string Choose { get; init; }

    /// <summary><c>required</c> of several choices.</summary>
    public required string ChooseSeveral { get; init; }

    /// <summary><c>required</c> of a grid.</summary>
    public required string GridRows { get; init; }

    /// <summary><c>required</c> of a grid's row.</summary>
    public required string GridRow { get; init; }

    /// <summary><c>required</c> of a ranking.</summary>
    public required string Order { get; init; }

    /// <summary><c>required</c> of a single checkbox.</summary>
    public required string Tick { get; init; }

    /// <summary><c>required</c> of yes or no, with the field's words for each.</summary>
    public required string YesNo { get; init; }

    /// <summary><c>required</c> of a consent.</summary>
    public required string Agree { get; init; }

    /// <summary><c>required</c> of a country.</summary>
    public required string Country { get; init; }

    /// <summary><c>required</c> of a name.</summary>
    public required string Name { get; init; }

    /// <summary><c>required</c> of an address.</summary>
    public required string Address { get; init; }

    /// <summary><c>required</c> of a file.</summary>
    public required string File { get; init; }

    /// <summary><c>required</c> of a signature.</summary>
    public required string Sign { get; init; }

    /// <summary><c>required</c> of a site's own type.</summary>
    public required string AnswerThis { get; init; }

    // invalid, by type.

    /// <summary><c>invalid</c> of an email address that is not one.</summary>
    public required string EmailFormat { get; init; }

    /// <summary><c>invalid</c> of a well-formed email address at a domain the field does not take.</summary>
    public required string EmailDomain { get; init; }

    /// <summary><c>invalid</c> of a phone number.</summary>
    public required string PhoneFormat { get; init; }

    /// <summary><c>invalid</c> of a web address.</summary>
    public required string UrlFormat { get; init; }

    /// <summary><c>invalid</c> of a number, with an example in the culture's format.</summary>
    public required string NumberFormat { get; init; }

    /// <summary><c>invalid</c> of a date.</summary>
    public required string DateFormat { get; init; }

    /// <summary><c>invalid</c> of a time.</summary>
    public required string TimeFormat { get; init; }

    /// <summary><c>invalid</c> of a date and time.</summary>
    public required string DateTimeFormat { get; init; }

    /// <summary><c>invalid</c> of a rating.</summary>
    public required string RatingRange { get; init; }

    /// <summary><c>invalid</c> of a scale.</summary>
    public required string ScaleRange { get; init; }

    /// <summary><c>invalid</c>, <c>min</c> and <c>max</c> of a slider.</summary>
    public required string ValueRange { get; init; }

    /// <summary><c>invalid</c> of a choice among options.</summary>
    public required string Options { get; init; }

    /// <summary><c>invalid</c> of a country.</summary>
    public required string CountryList { get; init; }

    /// <summary><c>invalid</c> of a name.</summary>
    public required string NameCheck { get; init; }

    /// <summary><c>invalid</c> of an address.</summary>
    public required string AddressCheck { get; init; }

    /// <summary><c>invalid</c> of a signature.</summary>
    public required string SignAgain { get; init; }

    /// <summary>Any error of a site's own type but <c>required</c>, and a validation rule without a message.</summary>
    public required string CheckThis { get; init; }

    // Lengths, limits and steps.

    /// <summary><c>minLength</c>.</summary>
    public required PluralWords MinLength { get; init; }

    /// <summary><c>maxLength</c>.</summary>
    public required PluralWords MaxLength { get; init; }

    /// <summary><c>minWords</c>.</summary>
    public required PluralWords MinWords { get; init; }

    /// <summary><c>maxWords</c>.</summary>
    public required PluralWords MaxWords { get; init; }

    /// <summary><c>min</c> or <c>max</c> of a number with both limits.</summary>
    public required string NumberBetween { get; init; }

    /// <summary><c>min</c> of a number without a most.</summary>
    public required string NumberFrom { get; init; }

    /// <summary><c>max</c> of a number without a least.</summary>
    public required string NumberUpTo { get; init; }

    /// <summary><c>min</c> or <c>max</c> of a time with both limits.</summary>
    public required string TimeBetween { get; init; }

    /// <summary><c>min</c> of a time without a latest.</summary>
    public required string TimeFrom { get; init; }

    /// <summary><c>max</c> of a time without an earliest.</summary>
    public required string TimeUpTo { get; init; }

    /// <summary><c>step</c> of a number with a step and no least.</summary>
    public required string NumberStep { get; init; }

    /// <summary><c>step</c> of a number with a step counted from its least.</summary>
    public required string NumberStepFrom { get; init; }

    /// <summary><c>step</c> of a number without a step or decimals.</summary>
    public required string WholeNumber { get; init; }

    /// <summary><c>step</c> of a number without a step, with the places its decimals allow.</summary>
    public required PluralWords Places { get; init; }

    /// <summary><c>step</c> of a slider.</summary>
    public required string ValueStep { get; init; }

    /// <summary><c>step</c> of a time counted from midnight, and of a date and time.</summary>
    public required PluralWords TimeStep { get; init; }

    /// <summary><c>step</c> of a time counted from its earliest.</summary>
    public required PluralWords TimeStepFrom { get; init; }

    // dateWindow.

    /// <summary>A window with both dates.</summary>
    public required string DateBetween { get; init; }

    /// <summary>A window with an earliest date only.</summary>
    public required string DateFrom { get; init; }

    /// <summary>A window with a latest date only.</summary>
    public required string DateUpTo { get; init; }

    /// <summary>A window of weekdays with both dates.</summary>
    public required string WeekdayBetween { get; init; }

    /// <summary>A window of weekdays with an earliest date only.</summary>
    public required string WeekdayFrom { get; init; }

    /// <summary>A window of weekdays with a latest date only.</summary>
    public required string WeekdayUpTo { get; init; }

    /// <summary>Weekdays without dates.</summary>
    public required string Weekday { get; init; }

    /// <summary>A date the window takes that the field excludes, or a window that takes none.</summary>
    public required string DateUnavailable { get; init; }

    // selections.

    /// <summary>As few as most.</summary>
    public required PluralWords SelectionsExactly { get; init; }

    /// <summary>Fewest and most.</summary>
    public required string SelectionsBetween { get; init; }

    /// <summary>Fewest only.</summary>
    public required PluralWords SelectionsAtLeast { get; init; }

    /// <summary>Most only.</summary>
    public required PluralWords SelectionsAtMost { get; init; }

    // Files.

    /// <summary><c>fileType</c>.</summary>
    public required string FileFormat { get; init; }

    /// <summary><c>fileSize</c> without a limit of the field's own.</summary>
    public required string FileTooLarge { get; init; }

    /// <summary><c>fileSize</c> of a field that takes one file.</summary>
    public required string FileLargerThan { get; init; }

    /// <summary><c>fileSize</c> of a field that takes several.</summary>
    public required string FilesLargerThan { get; init; }

    /// <summary><c>fileCount</c>.</summary>
    public required PluralWords FilesAtMost { get; init; }

    // The others.

    /// <summary><c>full</c> of one option.</summary>
    public required string OptionFull { get; init; }

    /// <summary><c>full</c> of several options.</summary>
    public required string ChoiceFull { get; init; }

    /// <summary><c>confirmation</c>.</summary>
    public required string Confirmation { get; init; }

    /// <summary><c>digits</c>.</summary>
    public required string Digits { get; init; }

    /// <summary><c>rowCount</c> of too few rows.</summary>
    public required PluralWords RowsAtLeast { get; init; }

    /// <summary><c>rowCount</c> of too many rows.</summary>
    public required PluralWords RowsAtMost { get; init; }

    /// <summary>A <c>pattern</c> check without its own message.</summary>
    public required string Pattern { get; init; }

    /// <summary>A <c>matchesField</c> check without its own message, with the other field's label.</summary>
    public required string SameAs { get; init; }

    /// <summary>A <c>uniquePerForm</c> check without its own message.</summary>
    public required string Taken { get; init; }

    /// <summary>A submission without an answer.</summary>
    public required string Empty { get; init; }

    // The error summary.

    /// <summary>Its heading for one error and for several.</summary>
    public required PluralWords SummaryHeading { get; init; }

    /// <summary>A link to a default message, with the field's label.</summary>
    public required string SummaryEntry { get; init; }

    /// <summary>A link to a default message in a row, with the field's label and the row's legend.</summary>
    public required string SummaryRowEntry { get; init; }

    /// <summary>The words of a culture: its own, its more general culture's, or English.</summary>
    /// <param name="culture">A culture name, such as <c>de-CH</c>.</param>
    public static VisitorMessageWords Of(string? culture)
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
