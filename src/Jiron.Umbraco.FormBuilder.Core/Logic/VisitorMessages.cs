using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;

namespace Jiron.Umbraco.FormBuilder.Logic;

/// <summary>An option of a field as its messages name it: its value and its label in the page's culture.</summary>
/// <param name="Value">The option's value.</param>
/// <param name="Label">Its label; an empty one shows the value.</param>
internal sealed record MessageOption(string Value, string Label);

/// <summary>A field as its messages read it: as the form endpoint shows it (ADR-0099).</summary>
internal sealed record MessageField
{
    /// <summary>The field's type alias.</summary>
    public required string Type { get; init; }

    /// <summary>Whether the type is the built-in one of its alias; a site's own type, even one that replaces a built-in alias, gets the generic words.</summary>
    public bool BuiltIn { get; init; } = true;

    /// <summary>The field's public settings with their effective values, every text in the page's culture; null for none.</summary>
    public JsonElement? Settings { get; init; }

    /// <summary>The options the field offers, with their labels in the page's culture.</summary>
    public IReadOnlyList<MessageOption> Options { get; init; } = [];
}

/// <summary>An error a visitor sees a message for (ADR-0099): what the pipeline reports, and what the message reads.</summary>
internal sealed record VisitorError
{
    /// <summary>
    /// The field's kind of error (<c>required</c>, <c>maxLength</c>, ...), <c>check</c> for one of its own checks,
    /// <c>rule</c> for a validation rule, or <c>empty</c> for a submission without an answer.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>The field; null for <c>empty</c>.</summary>
    public MessageField? Field { get; init; }

    /// <summary>For <c>check</c>, the check's kind: <c>pattern</c>, <c>matchesField</c> or <c>uniquePerForm</c>.</summary>
    public string? Check { get; init; }

    /// <summary>For a <c>matchesField</c> check, the label of the field it names; null or empty for none.</summary>
    public string? Other { get; init; }

    /// <summary>Whether the error is at one of a grid's rows rather than at the grid.</summary>
    public bool GridRow { get; init; }

    /// <summary>The field's answers as posted, which only <c>dateWindow</c>, an email's <c>invalid</c> and <c>full</c> read.</summary>
    public IReadOnlyList<string> Answers { get; init; } = [];

    /// <summary>For <c>rowCount</c>, the rows the visitor gave; null when unknown.</summary>
    public int? Rows { get; init; }
}

/// <summary>
/// The package's default message for a visitor's error (ADR-0099), the same as the runtime's in
/// <c>packages/logic</c>: a sentence of <see cref="VisitorMessageWords"/> in the language of the page's culture, with
/// the values the field's settings give, printed with the culture's formats (ADR-0064). The vectors of
/// <c>tests/vectors/messages.json</c> hold both runtimes to one text. A field's, a check's or a rule's own message
/// comes first; this is what shows without one.
/// </summary>
internal static class VisitorMessages
{
    private const long Kilobyte = 1_024;
    private const long Megabyte = Kilobyte * Kilobyte;

    // The marks that already close a label, so its summary link takes no colon after it (ADR-0106): a question mark, an
    // exclamation mark, a full stop and a colon, the ellipsis, the Greek question mark, the Arabic question mark, the
    // ideographic full stop, and the full-width forms of the four.
    private static readonly SearchValues<char> ClosingMarks = SearchValues.Create(
        ['?', '!', '.', ':', (char)0x2026, (char)0x037E, (char)0x061F, (char)0x3002, (char)0xFF01, (char)0xFF0E, (char)0xFF1A, (char)0xFF1F]);

    /// <summary>The default message for an error.</summary>
    /// <param name="error">The error.</param>
    /// <param name="culture">The page's culture, whose language picks the words.</param>
    /// <param name="formats">The culture's formats, which print the values.</param>
    /// <param name="today">The site's today as a day number, from which a date window relative to today counts.</param>
    /// <param name="texts">The visitor texts the form or the site rewords, by key (ADR-0230); null for none.</param>
    public static string Default(VisitorError error, string culture, CultureFormats formats, int today, IReadOnlyDictionary<string, string>? texts = null)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(formats);
        var words = Words(culture, texts);
        switch (error.Code)
        {
            case "empty":
                return words.Empty;
            case "rule":
                return words.CheckThis;
            case "check":
                return error.Check switch
                {
                    "pattern" => words.Pattern,
                    "matchesField" when error.Other is { Length: > 0 } other => Fill(words.SameAs, ("field", other)),
                    "uniquePerForm" => words.Taken,
                    _ => words.CheckThis,
                };
        }

        if (error.Field is not { } field)
        {
            return words.CheckThis;
        }

        if (!field.BuiltIn)
        {
            return error.Code == "required" ? words.AnswerThis : words.CheckThis;
        }

        var settings = new Settings(field.Settings);
        var said = new Saying(words, formats);
        return error.Code switch
        {
            "required" => Required(error, field, settings, words, culture, texts),
            "invalid" => Invalid(error, field, settings, said),
            "minLength" when settings.Long("minLength") is { } min => Fill(words.MinLength.For(min), ("min", said.Count(min))),
            "maxLength" when settings.Long("maxLength") is { } max => Fill(words.MaxLength.For(max), ("max", said.Count(max))),
            "minWords" when settings.Long("minWords") is { } min => Fill(words.MinWords.For(min), ("min", said.Count(min))),
            "maxWords" when settings.Long("maxWords") is { } max => Fill(words.MaxWords.For(max), ("max", said.Count(max))),
            "min" or "max" => Range(field, settings, said),
            "step" => Step(field, settings, said),
            "dateWindow" when field.Type is "date" or "datetime" => DateWindow(error, settings, said, today),
            "selections" => Selections(settings, said),
            "fileType" => Formats(settings, said),
            "fileSize" => Size(settings, said),
            "fileCount" when settings.Long("maxFiles") is { } max => Fill(words.FilesAtMost.For(max), ("max", said.Count(max))),
            "full" => Full(error, field, words),
            "confirmation" => words.Confirmation,
            "digits" => words.Digits,
            "rowCount" => Rows(error, settings, said),
            _ => words.CheckThis,
        };
    }

    /// <summary>The error summary's heading for a number of errors.</summary>
    /// <param name="errors">How many errors the summary lists.</param>
    /// <param name="culture">The page's culture.</param>
    /// <param name="texts">The visitor texts the form or the site rewords, by key (ADR-0230); null for none.</param>
    public static string SummaryHeading(int errors, string culture, IReadOnlyDictionary<string, string>? texts = null) => Words(culture, texts).SummaryHeading.For(errors);

    /// <summary>The words of a culture, the messages the form or the site rewords in their place (ADR-0230).</summary>
    /// <param name="culture">The page's culture.</param>
    /// <param name="texts">The visitor texts reworded, by key; null for none.</param>
    public static VisitorMessageWords Words(string culture, IReadOnlyDictionary<string, string>? texts) =>
        texts is { Count: > 0 } ? VisitorWording.Apply(VisitorMessageWords.Of(culture), texts) : VisitorMessageWords.Of(culture);

    /// <summary>
    /// The error summary's link to a default message: the field's label first, and in a row the row's legend, so each
    /// link says which question it is (WCAG 2.4.4); the message alone without a label. A label that already ends in a
    /// question mark, an exclamation mark, a full stop or a colon takes no colon after it ("Where would you like to sit?
    /// Choose an answer").
    /// </summary>
    /// <param name="message">The default message.</param>
    /// <param name="label">The field's label as the page shows it, or null when it shows none.</param>
    /// <param name="row">The legend of the row the field is in, or null outside a row.</param>
    /// <param name="culture">The page's culture.</param>
    /// <param name="texts">The visitor texts the form or the site rewords, by key (ADR-0230); null for none.</param>
    public static string SummaryEntry(string message, string? label, string? row, string culture, IReadOnlyDictionary<string, string>? texts = null)
    {
        if (IsBlank(label))
        {
            return message;
        }

        var words = Words(culture, texts);
        return IsBlank(row)
            ? Fill(EndsWithMark(label) ? words.SummaryMarkedEntry : words.SummaryEntry, ("label", label), ("message", message))
            : Fill(words.SummaryRowEntry, ("label", label), ("row", row), ("message", message));
    }

    // Whether a label or a row's legend shows nothing: every code unit one that cleaning trims (ADR-0005), the same set in
    // both runtimes.
    private static bool IsBlank([NotNullWhen(false)] string? text) => text is null || text.All(TextSemantics.IsTrimmed);

    /// <summary>Whether the label's last character that cleaning keeps (ADR-0005) closes it as a question mark, an exclamation mark, a full stop or a colon does.</summary>
    /// <param name="label">The label as the page shows it.</param>
    public static bool EndsWithMark(string label)
    {
        ArgumentNullException.ThrowIfNull(label);
        for (var at = label.Length - 1; at >= 0; at--)
        {
            if (!TextSemantics.IsTrimmed(label[at]))
            {
                return ClosingMarks.Contains(label[at]);
            }
        }

        return false;
    }

    /// <summary>A template with each <c>{name}</c> it holds replaced by its value, in one pass, so a value is never read as a template.</summary>
    public static string Fill(string template, params (string Name, string Value)[] values)
    {
        ArgumentNullException.ThrowIfNull(template);
        var text = new StringBuilder(template.Length + 16);
        var at = 0;
        while (at < template.Length)
        {
            var open = template.IndexOf('{', at);
            var close = open < 0 ? -1 : template.IndexOf('}', open + 1);
            if (close < 0)
            {
                text.Append(template, at, template.Length - at);
                break;
            }

            var name = template[(open + 1)..close];
            var found = Array.FindIndex(values, value => value.Name == name);
            text.Append(template, at, open - at);
            text.Append(found < 0 ? template[open..(close + 1)] : values[found].Value);
            at = close + 1;
        }

        return text.ToString();
    }

    // required, by what the control asks for.
    private static string Required(VisitorError error, MessageField field, Settings settings, VisitorMessageWords words, string culture, IReadOnlyDictionary<string, string>? texts) => field.Type switch
    {
        "text" or "textarea" => words.Answer,
        "email" => words.Email,
        "phone" => words.Phone,
        "url" => words.Url,
        "number" => words.Number,
        "date" => words.Date,
        "time" => words.Time,
        "datetime" => words.DateTime,
        "rating" => words.Rating,
        "scale" => words.Scale,
        "slider" => words.Value,
        "dropdown" => settings.Bool("multiple") ? words.ChooseSeveral : words.Choose,
        "radio" => words.Choose,
        "checkboxes" => words.ChooseSeveral,
        "grid" => error.GridRow ? words.GridRow : words.GridRows,
        "ranking" => words.Order,
        "checkbox" => words.Tick,
        "yesno" => Fill(
            words.YesNo,
            ("yes", settings.Text("yesLabel") is { Length: > 0 } yes ? yes : VisitorWording.Yes(culture, texts)),
            ("no", settings.Text("noLabel") is { Length: > 0 } no ? no : VisitorWording.No(culture, texts))),
        "consent" => words.Agree,
        "country" => words.Country,
        "name" => words.Name,
        "address" => words.Address,
        "file" => words.File,
        "signature" => words.Sign,
        _ => words.AnswerThis,
    };

    // invalid, by type, with an example where one helps; an email address that is well formed but refused can only
    // be at a domain the field does not take, which stays on the server (ADR-0062).
    private static string Invalid(VisitorError error, MessageField field, Settings settings, Saying said)
    {
        var words = said.Words;
        return field.Type switch
        {
            "email" => error.Answers is [var address, ..] && BuiltInForms.IsEmailAddress(TextSemantics.Clean(address)) ? words.EmailDomain : words.EmailFormat,
            "phone" => words.PhoneFormat,
            "url" => words.UrlFormat,
            "number" => Fill(words.NumberFormat, ("example", said.Number(settings.Long("decimals") is > 0 ? "12.5" : "12"))),
            "date" => words.DateFormat,
            "time" => words.TimeFormat,
            "datetime" => words.DateTimeFormat,
            "rating" => Fill(words.RatingRange, ("count", said.Count(settings.Long("count") ?? 5))),
            "scale" => Fill(words.ScaleRange, ("from", said.Count(settings.Long("from") ?? 0)), ("to", said.Count(settings.Long("to") ?? 10))),
            "slider" => SliderRange(settings, said),
            "dropdown" or "radio" or "checkboxes" or "grid" or "ranking" => words.Options,
            "country" => words.CountryList,
            "name" => words.NameCheck,
            "address" => words.AddressCheck,
            "signature" => words.SignAgain,
            _ => words.CheckThis,
        };
    }

    // min and max: the field's whole range, whichever limit the answer passed.
    private static string Range(MessageField field, Settings settings, Saying said)
    {
        var words = said.Words;
        var (min, max) = (settings.Text("min"), settings.Text("max"));
        switch (field.Type)
        {
            case "number":
                return (min, max) switch
                {
                    ({ } low, { } high) => Fill(words.NumberBetween, ("min", said.Number(low)), ("max", said.Number(high))),
                    ({ } low, null) => Fill(words.NumberFrom, ("min", said.Number(low))),
                    (null, { } high) => Fill(words.NumberUpTo, ("max", said.Number(high))),
                    _ => words.CheckThis,
                };
            case "slider":
                return SliderRange(settings, said);
            case "time":
                return (min, max) switch
                {
                    ({ } low, { } high) => Fill(words.TimeBetween, ("min", said.Time(low)), ("max", said.Time(high))),
                    ({ } low, null) => Fill(words.TimeFrom, ("min", said.Time(low))),
                    (null, { } high) => Fill(words.TimeUpTo, ("max", said.Time(high))),
                    _ => words.CheckThis,
                };
            default:
                return words.CheckThis;
        }
    }

    private static string SliderRange(Settings settings, Saying said) =>
        settings.Text("min") is { } low && settings.Text("max") is { } high
            ? Fill(said.Words.ValueRange, ("min", said.Number(low)), ("max", said.Number(high)))
            : said.Words.CheckThis;

    // step: a number's own step (from its least when it has one), else the places its decimals allow; a slider's step;
    // a time's minutes, from its earliest, and a date and time's, from midnight.
    private static string Step(MessageField field, Settings settings, Saying said)
    {
        var words = said.Words;
        switch (field.Type)
        {
            case "number":
                if (settings.Text("step") is { } step)
                {
                    return settings.Text("min") is { } min
                        ? Fill(words.NumberStepFrom, ("step", said.Number(step)), ("min", said.Number(min)))
                        : Fill(words.NumberStep, ("step", said.Number(step)));
                }

                return settings.Long("decimals") is { } places and > 0
                    ? Fill(words.Places.For(places), ("places", said.Count(places)))
                    : words.WholeNumber;
            case "slider":
                return settings.Text("step") is { } sliderStep ? Fill(words.ValueStep, ("step", said.Number(sliderStep))) : words.CheckThis;
            case "time" or "datetime" when settings.Long("stepMinutes") is { } minutes:
                return field.Type == "time" && settings.Text("min") is { } earliest
                    ? Fill(words.TimeStepFrom.For(minutes), ("minutes", said.Count(minutes)), ("min", said.Time(earliest)))
                    : Fill(words.TimeStep.For(minutes), ("minutes", said.Count(minutes)));
            default:
                return words.CheckThis;
        }
    }

    // dateWindow: the window a date must keep, its dates relative to today counted from the site's today; an answer the
    // window would take is one the field excludes.
    private static string DateWindow(VisitorError error, Settings settings, Saying said, int today)
    {
        var words = said.Words;
        if (!BuiltInValidation.TryBound(settings.Json("min"), today, out var low) || !BuiltInValidation.TryBound(settings.Json("max"), today, out var high))
        {
            return words.DateUnavailable;
        }

        var weekdays = settings.Bool("weekdaysOnly");
        if (error.Answers is [var answer, ..] && CalendarValues.ParseDate(answer.Length >= 10 ? answer[..10] : answer) is { } day
            && (low is null || day >= low) && (high is null || day <= high)
            && !(weekdays && DateOnly.FromDayNumber(day).DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday))
        {
            return words.DateUnavailable;
        }

        var (monday, friday) = (said.Formats.Days[1], said.Formats.Days[5]);
        return (low, high, weekdays) switch
        {
            ({ } from, { } to, false) => Fill(words.DateBetween, ("min", said.Date(from)), ("max", said.Date(to))),
            ({ } from, null, false) => Fill(words.DateFrom, ("min", said.Date(from))),
            (null, { } to, false) => Fill(words.DateUpTo, ("max", said.Date(to))),
            ({ } from, { } to, true) => Fill(words.WeekdayBetween, ("min", said.Date(from)), ("max", said.Date(to)), ("monday", monday), ("friday", friday)),
            ({ } from, null, true) => Fill(words.WeekdayFrom, ("min", said.Date(from)), ("monday", monday), ("friday", friday)),
            (null, { } to, true) => Fill(words.WeekdayUpTo, ("max", said.Date(to)), ("monday", monday), ("friday", friday)),
            (null, null, true) => Fill(words.Weekday, ("monday", monday), ("friday", friday)),
            _ => words.DateUnavailable,
        };
    }

    private static string Selections(Settings settings, Saying said)
    {
        var words = said.Words;
        return (settings.Long("minSelections"), settings.Long("maxSelections")) switch
        {
            ({ } min, { } max) when min == max => Fill(words.SelectionsExactly.For(min), ("count", said.Count(min))),
            ({ } min, { } max) => Fill(words.SelectionsBetween, ("min", said.Count(min)), ("max", said.Count(max))),
            ({ } min, null) => Fill(words.SelectionsAtLeast.For(min), ("min", said.Count(min))),
            (null, { } max) => Fill(words.SelectionsAtMost.For(max), ("max", said.Count(max))),
            _ => words.CheckThis,
        };
    }

    // fileType: the formats the field takes, its groups' extensions first and then its own, in capitals, each once.
    private static string Formats(Settings settings, Saying said)
    {
        var formats = new List<string>();
        foreach (var group in settings.Texts("types"))
        {
            if (Enum.TryParse<FileTypeGroup>(group, ignoreCase: true, out var known) && Enum.IsDefined(known))
            {
                formats.AddRange(FileFieldType.ExtensionsOf(known));
            }
        }

        formats.AddRange(settings.Texts("extensions").Select(extension => extension.TrimStart('.')));
        var shown = formats.Select(format => format.ToUpperInvariant()).Where(format => format.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        return shown.Count == 0 ? said.Words.CheckThis : Fill(said.Words.FileFormat, ("formats", said.Choices(shown)));
    }

    // fileSize: the field's limit in KB below a megabyte and in MB from one, 1,024 based, with one place at most; a
    // field without a limit of its own is held to the host's, which the page does not know.
    private static string Size(Settings settings, Saying said)
    {
        var words = said.Words;
        if (settings.Long("maxBytes") is not { } bytes || bytes <= 0)
        {
            return words.FileTooLarge;
        }

        var (unit, template) = bytes < Megabyte ? (Kilobyte, words.Kilobytes) : (Megabyte, words.Megabytes);
        var tenths = ((bytes * 10m) + (unit / 2m)) / unit;
        var rounded = decimal.Floor(tenths);
        var text = (rounded / 10m).ToString(rounded % 10m == 0 ? "0" : "0.0", CultureInfo.InvariantCulture);
        var size = Fill(template, ("size", said.Number(text)));
        return Fill(settings.Long("maxFiles") is > 1 ? words.FilesLargerThan : words.FileLargerThan, ("size", size));
    }

    // full: the option an answer of one option holds, by its label; several, or none known, in general.
    private static string Full(VisitorError error, MessageField field, VisitorMessageWords words)
    {
        if (error.Answers is not [var answer])
        {
            return words.ChoiceFull;
        }

        var option = field.Options.FirstOrDefault(each => each.Value == answer);
        var label = option is { Label.Length: > 0 } ? option.Label : answer;
        return Fill(words.OptionFull, ("option", label));
    }

    // rowCount: too few rows asks for the least, too many for the most; without the count, the least when there is one.
    private static string Rows(VisitorError error, Settings settings, Saying said)
    {
        var words = said.Words;
        var min = settings.Long("minRows") ?? 0;
        var max = settings.Long("maxRows");
        var tooMany = error.Rows is { } rows ? max is { } most && rows > most : min == 0;
        return tooMany && max is { } limit
            ? Fill(words.RowsAtMost.For(limit), ("max", said.Count(limit)))
            : Fill(words.RowsAtLeast.For(min), ("min", said.Count(min)));
    }

    // How a message prints its values: counts and numbers in the culture's format as written, dates in its medium
    // pattern, times in its time pattern, and choices joined with its "or".
    private readonly record struct Saying(VisitorMessageWords Words, CultureFormats Formats)
    {
        public string Count(long count) => Number(count.ToString(CultureInfo.InvariantCulture));

        public string Number(string text) => CultureFormatting.FormatNumber(text, null, Formats) ?? text;

        public string Date(int day) => CultureFormatting.FormatCalendar(CalendarValues.FormatDate(day), "medium", Formats)!;

        public string Time(string text) => CultureFormatting.FormatCalendar(text, "time", Formats) ?? text;

        public string Choices(List<string> choices) =>
            choices.Count == 1 ? choices[0] : string.Join(", ", choices.Take(choices.Count - 1)) + " " + Words.Or + " " + choices[^1];
    }

    // The settings by name, as the form endpoint shows them.
    private readonly struct Settings(JsonElement? settings)
    {
        public string? Text(string name) => Find(name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

        public long? Long(string name) => Find(name) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt64(out var number) ? number : null;

        public bool Bool(string name) => Find(name) is { ValueKind: JsonValueKind.True };

        public JsonElement? Json(string name) => Find(name) is { ValueKind: not JsonValueKind.Null } value ? value : null;

        public IReadOnlyList<string> Texts(string name) =>
            Find(name) is { ValueKind: JsonValueKind.Array } value
                ? [.. value.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!)]
                : [];

        private JsonElement? Find(string name) =>
            settings is { ValueKind: JsonValueKind.Object } given && given.TryGetProperty(name, out var value) ? value : null;
    }
}
