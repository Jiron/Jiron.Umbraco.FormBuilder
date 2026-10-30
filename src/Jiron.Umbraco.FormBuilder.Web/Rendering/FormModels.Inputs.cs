using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Projection;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Core.Signatures;
using Jiron.Umbraco.FormBuilder.Logic;
using Jiron.Umbraco.FormBuilder.Web.Rendering.Models;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

// The controls of the types with parts, dates and outputs (ADR-0097): the browser's own date, time, range and file
// inputs with what the server checks, a name's and an address's parts in a fieldset, the signature's typed name, a
// calculation's result as the pipeline works it out, the captcha's place, and what a site's own type's view gets.
internal sealed partial class FormModels
{
    // The input type of a date and time.
    private const string DateTimeInput = "datetime-local";

    // The minutes of a day, which a date and time's step must divide for the browser's steps to be the server's.
    private const int MinutesPerDay = 24 * 60;

    // A name's parts in its type's order, with their autocomplete tokens.
    private static readonly ImmutableArray<(NamePart Part, string Token)> NameTokens =
    [
        (NamePart.Title, "honorific-prefix"),
        (NamePart.First, "given-name"),
        (NamePart.Middle, "additional-name"),
        (NamePart.Last, "family-name"),
    ];

    // An address's parts in its type's order, with their autocomplete tokens.
    private static readonly ImmutableArray<(AddressPart Part, string Token)> AddressTokens =
    [
        (AddressPart.Street, "address-line1"),
        (AddressPart.Street2, "address-line2"),
        (AddressPart.Postcode, "postal-code"),
        (AddressPart.City, "address-level2"),
        (AddressPart.Region, "address-level1"),
        (AddressPart.Country, "country"),
    ];

    // The control of a type with parts, a date or an output; null for any other type. The types' own settings come from
    // the published version, their texts as the form endpoint shows them.
    private static (object? Model, IReadOnlyList<string> DescribedBy)? InputControl(Build build, PublicField field, IFormBuilderFieldType type, ShownSettings settings, string id, FieldAnswers answers)
    {
        var given = build.Definition(field.Key).Settings;
        switch (type)
        {
            case DateFieldType date:
                {
                    var typed = date.ReadSettings(given);
                    return (Dated(build, "date", typed.Min, typed.Max, null), []);
                }

            case DateTimeFieldType dateTime:
                {
                    var typed = dateTime.ReadSettings(given);
                    return (Dated(build, DateTimeInput, typed.Min, typed.Max, typed.StepMinutes is { } minutes && MinutesPerDay % minutes == 0 ? Seconds(minutes) : null), []);
                }

            case TimeFieldType time:
                {
                    var typed = time.ReadSettings(given);
                    return (new DateControlModel { InputType = "time", Min = typed.Min, Max = typed.Max, Step = typed.StepMinutes is { } minutes ? Seconds(minutes) : null }, []);
                }

            case SliderFieldType slider:
                return (Slider(build, slider.ReadSettings(given), settings, answers), []);

            case CalculationFieldType calculation:
                return (Calculation(build, field, calculation.ReadSettings(given), settings), []);

            case NameFieldType name:
                {
                    var typed = name.ReadSettings(given);
                    var asked = typed.Parts.Distinct().Order().Select(part => Spec(build, NameTokens.Single(token => token.Part == part).Token, part, wide: false)).ToList();
                    return (Parts(build, field, id, answers, asked, [.. typed.RequiredParts.Select(BuiltInChecks.Name)], [.. asked.Select(spec => spec.Name)]), []);
                }

            case AddressFieldType address:
                {
                    var typed = address.ReadSettings(given);
                    var parts = AddressFieldType.Always.Concat(typed.Parts).Distinct().Order().ToList();
                    var asked = parts.Select(part => Spec(build, AddressTokens.Single(token => token.Part == part).Token, part, Wide(part, parts))).ToList();
                    return (Parts(build, field, id, answers, asked, [.. typed.RequiredParts.Select(BuiltInChecks.Name)], [.. AddressFieldType.Always.Select(BuiltInChecks.Name)]), []);
                }

            case FileFieldType file:
                {
                    var typed = file.ReadSettings(given);
                    return (new FileControlModel
                    {
                        Accept = string.Join(',', FileFieldType.Extensions(typed).Select(extension => "." + extension)),
                        Multiple = typed.MaxFiles > 1,
                        DropZone = typed.DropZone,
                        Kept = [.. build.Files(field.Key).Select((file, at) => new KeptFileModel
                        {
                            Id = id + "-kept-" + (at + 1).ToString(CultureInfo.InvariantCulture),
                            Token = file.Token,
                            Label = string.Format(build.Culture, build.Words.KeepFile, file.Name, SizeOf(file.Size, build.Words, build.Culture)),
                        })],
                    }, []);
                }

            case SignatureFieldType:
                return (new SignatureControlModel
                {
                    Label = build.Words.SignatureName,
                    Name = build.Name(field) + "." + SignatureFieldType.TypedPart,
                    Value = answers.Part(SignatureFieldType.TypedPart) is [var written, ..] ? written : string.Empty,
                    MaxLength = TypedSignature.LongestName,
                    Help = build.Words.SignatureTyped,
                    HelpId = id + "-typed-help",
                }, []);

            case CaptchaFieldType:
                // The place renders only for a form that names its captcha (FieldAsync).
                return (new CaptchaControlModel(build.Render.Shown.Form.Settings.CaptchaProvider!), []);

            default:
                return null;
        }
    }

    // A date's input, or a date and time's, with its window for the site's today: a limit that is a date or one relative
    // to today (ADR-0062), as the server checks it; one that falls outside the calendar sets none. A date and time's
    // window is that of its date, from the first minute of its first day to the last minute of its last.
    private static DateControlModel Dated(Build build, string inputType, JsonElement? min, JsonElement? max, string? step)
    {
        var withTime = inputType == DateTimeInput;
        string? Limit(JsonElement? limit, string time) =>
            BuiltInValidation.TryBound(limit, build.Today.DayNumber, out var day) && day is { } found ? CalendarValues.FormatDate(found) + time : null;
        return new DateControlModel
        {
            InputType = inputType,
            Min = Limit(min, withTime ? "T00:00" : string.Empty),
            Max = Limit(max, withTime ? "T23:59" : string.Empty),
            Step = step,
        };
    }

    // A step of minutes in seconds, as a time input takes it.
    private static string Seconds(int minutes) => (minutes * 60).ToString(CultureInfo.InvariantCulture);

    // A slider at its answer, or before one at its start (SliderStart), with the value in the culture's format and the
    // field's unit for screen readers, and beside the slider for sight when the field shows it; a posted value that is no
    // number shows as it is.
    private static SliderControlModel Slider(Build build, SliderFieldSettings typed, ShownSettings settings, FieldAnswers answers)
    {
        var value = answers.Values is [var given, ..] ? given : SliderStart(typed);
        var text = FieldDisplayDefaults.Around(string.Empty, CultureFormatting.FormatNumber(value, null, build.Formats) ?? value, settings.Text("unit") ?? string.Empty);
        return new SliderControlModel
        {
            Min = typed.Min,
            Max = typed.Max,
            Step = typed.Step,
            Value = value,
            ValueText = text == value ? null : text,
            Shown = typed.ShowValue ? text : null,
        };
    }

    // Where a slider stands before an answer: the middle of its range on its step, the higher of two steps as near, which
    // is where a browser puts a range input without a value. Publishing holds its settings to numbers, the lowest below
    // the highest and the step above zero and within the range (the type's settings check), so the start is in it.
    private static string SliderStart(SliderFieldSettings settings)
    {
        var low = ExactDecimal.Parse(settings.Min)!.Value;
        var step = ExactDecimal.Parse(settings.Step)!.Value;
        var half = ExactDecimal.Divide(ExactDecimal.Subtract(ExactDecimal.Parse(settings.Max)!.Value, low), ExactDecimal.Integer(2));
        return ExactDecimal.Add(low, ExactDecimal.Multiply(ExactDecimal.Round(ExactDecimal.Divide(half, step), 0), step)).ToString();
    }

    /// <summary>
    /// A file's size as the page shows it: 1,024 based and rounded to one place, shown without it when it is whole; in
    /// kilobytes until the rounded size reaches a megabyte, and a file of a few bytes as a tenth of a kilobyte, not as
    /// none.
    /// </summary>
    /// <param name="bytes">The file's size.</param>
    /// <param name="words">The page's words.</param>
    /// <param name="culture">The page's culture, which writes the number.</param>
    internal static string SizeOf(long bytes, RenderingWords words, CultureInfo culture)
    {
        const decimal Kilobyte = 1024m;
        const decimal Megabyte = 1024m * 1024m;
        var kilobytes = Math.Max(Tenths(Kilobyte), bytes > 0 ? 1m : 0m);
        var (tenths, template) = kilobytes < 10240m ? (kilobytes, words.Kilobytes) : (Tenths(Megabyte), words.Megabytes);
        return string.Format(culture, template, (tenths / 10m).ToString(tenths % 10m == 0 ? "0" : "0.0", culture));

        decimal Tenths(decimal unit) => decimal.Floor(((bytes * 10m) + (unit / 2m)) / unit);
    }

    // A calculation's result as the pipeline works it out for the same answers (the rules' value, rounded to its places,
    // ADR-0063), shown as its answer shows (ADR-0066): in the culture's format with the field's places, between its prefix
    // and its suffix as the culture shows them; empty without a value.
    private static CalculationControlModel Calculation(Build build, PublicField field, CalculationFieldSettings typed, ShownSettings settings)
    {
        if (build.Calculations.GetValueOrDefault(Id(field.Key)) is not { } value)
        {
            return new CalculationControlModel { Text = string.Empty };
        }

        // The rules' value is a number in the wire form, which the culture writes.
        var number = CultureFormatting.FormatNumber(value, Math.Clamp(typed.Format?.Decimals ?? CalculationFormat.DefaultDecimals, 0, 15), build.Formats)!;
        var format = settings.Child("format");
        return new CalculationControlModel { Text = FieldDisplayDefaults.Around(format?.Text("prefix") ?? string.Empty, number, format?.Text("suffix") ?? string.Empty) };
    }

    // A part as the page offers it: its name, label, autocomplete token and width.
    private static PartSpec Spec<TPart>(Build build, string token, TPart part, bool wide)
        where TPart : struct, Enum
    {
        var name = BuiltInChecks.Name(part);
        return new PartSpec(name, PartLabel(build.Words, name), token, wide);
    }

    // The streets take the full width, and so does the country unless the region shares its line.
    private static bool Wide(AddressPart part, List<AddressPart> asked) =>
        part is AddressPart.Street or AddressPart.Street2 || (part == AddressPart.Country && !asked.Contains(AddressPart.Region));

    private static string PartLabel(RenderingWords words, string part) => part switch
    {
        "title" => words.NameTitle,
        "first" => words.NameFirst,
        "middle" => words.NameMiddle,
        "last" => words.NameLast,
        "street" => words.Street,
        "street2" => words.Street2,
        "postcode" => words.Postcode,
        "city" => words.City,
        "region" => words.Region,
        _ => words.Country,
    };

    // A name's or an address's parts the field asks for, in its type's order, the first carrying the field's id. The parts
    // it needs (those its settings list, or with none listed the unlisted ones once it is required, as its type checks
    // them, ADR-0062) are required while the field is, and marked in the form's style when it needs some but not all.
    // With an error, the parts it needs that are empty carry it, or every part when none is. On a form with rules, a part
    // carries the mark it shows while the field is required, and says when its own requirement follows the field's (ADR-0111).
    private static PartsControlModel Parts(Build build, PublicField field, string id, FieldAnswers answers, List<PartSpec> asked, string[] listed, string[] unlisted)
    {
        var required = build.Required.Contains(Id(field.Key));
        var needed = (listed.Length > 0 ? listed : required ? unlisted : []).Where(part => asked.Any(spec => spec.Name == part)).ToHashSet(StringComparer.Ordinal);
        var neededIfRequired = (listed.Length > 0 ? listed : unlisted).Where(part => asked.Any(spec => spec.Name == part)).ToHashSet(StringComparer.Ordinal);
        var someIfRequired = neededIfRequired.Count < asked.Count;
        var empty = needed.Where(part => answers.Part(part).IsEmpty).ToHashSet(StringComparer.Ordinal);
        var failed = build.Error(field.Key) is not null;
        return new PartsControlModel
        {
            Parts = [.. asked.Select((spec, at) =>
            {
                var isRequired = required && needed.Contains(spec.Name);
                var markIfRequired = !someIfRequired ? FieldMark.None : build.Marker switch
                {
                    RequiredMarker.Required when neededIfRequired.Contains(spec.Name) => FieldMark.Required,
                    RequiredMarker.Optional when !neededIfRequired.Contains(spec.Name) => FieldMark.Optional,
                    _ => FieldMark.None,
                };
                var mark = required ? markIfRequired : FieldMark.None;
                var carried = build.Hooks ? markIfRequired : mark;
                var value = answers.Part(spec.Name) is [var text, ..] ? text : string.Empty;
                return new PartModel
                {
                    Part = spec.Name,
                    Label = spec.Label,
                    Id = at == 0 ? id : id + "-" + spec.Name,
                    Name = build.Name(field) + "." + spec.Name,
                    Value = value,
                    Autocomplete = spec.Token,
                    IsWide = spec.Wide,
                    IsRequired = isRequired,
                    FollowsRequired = build.Hooks && neededIfRequired.Contains(spec.Name),
                    IsInvalid = failed && (empty.Count == 0 || empty.Contains(spec.Name)),
                    Mark = carried,
                    MarkText = MarkText(build, carried),
                    IsMarkShown = mark != FieldMark.None,
                    MarkShownWhile = build.Hooks && carried != FieldMark.None ? MarkWhile.Required : null,
                    Countries = spec.Name == "country" ? Countries(build, BuiltInChecks.CountryCodes, [], value.Length > 0 ? [value] : []) : null,
                };
            })],
        };
    }

    // What a site's own type's view gets: its public settings, a text with tags as rendered for the page's answers, and
    // its answer's parts.
    private static CustomControlModel Custom(ShownSettings settings, FieldAnswers answers) => new()
    {
        Settings = settings.Json(),
        Parts = answers.Parts.ToDictionary(part => part.Key, part => (IReadOnlyList<string>)part.Value, StringComparer.Ordinal),
    };

    // A part of a name or an address: its name, label, autocomplete token and whether it takes the full width.
    private readonly record struct PartSpec(string Name, string Label, string Token, bool Wide);
}
