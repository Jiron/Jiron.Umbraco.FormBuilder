using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes;

/// <summary>
/// How a field type takes and reads answers unless it says otherwise (ADR-0058), from its declaration: what its rule
/// kind needs, one value unless it takes several, and nothing at all for a type that holds no value of its own.
/// </summary>
internal static class FieldAnswerDefaults
{
    /// <summary>The longest number a type takes as posted: room for any number a stored one can hold, with leading zeros or an exponent.</summary>
    public const int NumberLength = 64;

    /// <summary>A date: <c>YYYY-MM-DD</c>.</summary>
    public const int DateLength = 10;

    /// <summary>A time: <c>HH:mm:ss</c>.</summary>
    public const int TimeLength = 8;

    /// <summary>A date with a time: <c>YYYY-MM-DDTHH:mm:ss</c>.</summary>
    public const int DateTimeLength = 19;

    /// <summary>A ticked checkbox: <c>true</c>.</summary>
    public const int CheckboxLength = 4;

    /// <summary>An upload token (ADR-0006), with room to spare.</summary>
    public const int TokenLength = 1024;

    /// <summary>The value a ticked checkbox posts.</summary>
    public const string Ticked = "true";

    /// <summary>The limits a type has by default.</summary>
    public static FieldAnswerLimits Limits(FieldTypeDeclaration declaration, FormField field)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentNullException.ThrowIfNull(field);
        if (!declaration.HoldsValue || declaration.Storage == FieldStorageKind.None)
        {
            return FieldAnswerLimits.None;
        }

        var most = TakesSeveral(declaration, field) ? FieldAnswerLimits.DefaultManyTexts : 1;
        return declaration.RuleKind switch
        {
            FieldKind.Choice or FieldKind.Choices => new FieldAnswerLimits(DefinitionSaveChecks.OptionValueLength, most),
            FieldKind.Checkbox => new FieldAnswerLimits(CheckboxLength, most),
            FieldKind.Files => new FieldAnswerLimits(TokenLength, most),
            FieldKind.Number => new FieldAnswerLimits(NumberLength, most),
            FieldKind.Date => new FieldAnswerLimits(DateLength, most),
            FieldKind.Time => new FieldAnswerLimits(TimeLength, most),
            FieldKind.DateTime => new FieldAnswerLimits(DateTimeLength, most),
            _ => new FieldAnswerLimits(FieldAnswerLimits.DefaultTextLength, most),
        };
    }

    /// <summary>Reads the field's own texts as the type's rule kind needs; parts are left to the types that have them.</summary>
    public static void Read(FieldTypeDeclaration declaration, FieldReading reading)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        ArgumentNullException.ThrowIfNull(reading);
        if (!declaration.HoldsValue || declaration.Storage == FieldStorageKind.None)
        {
            return;
        }

        var texts = reading.Answers.Values;
        if ((texts.Length > 1 && !TakesSeveral(declaration, reading.Field)) || texts.Distinct(StringComparer.Ordinal).Count() != texts.Length)
        {
            reading.Invalid();
            return;
        }

        foreach (var text in texts)
        {
            if (Read(declaration.RuleKind, text) is { } value)
            {
                reading.Add(value);
            }
            else if (declaration.RuleKind != FieldKind.Checkbox)
            {
                reading.Invalid();
                return;
            }
        }
    }

    /// <summary>
    /// Whether a type takes several values: choices and files, and a type with a <c>multiple</c> setting when the field
    /// turns it on.
    /// </summary>
    public static bool TakesSeveral(FieldTypeDeclaration declaration, FormField field) =>
        declaration.RuleKind is FieldKind.Choices or FieldKind.Files
        || (declaration.MultipleSetting && field.Settings is { } settings
            && settings.Value.TryGetProperty("multiple", out var multiple) && multiple.ValueKind == JsonValueKind.True);

    // A text in the wire form of the rule kind; null when it is not one (a checkbox's anything but "true" is unticked).
    private static string? Read(FieldKind? kind, string text) => kind switch
    {
        FieldKind.Checkbox => text == Ticked ? Ticked : null,
        FieldKind.Number => PostedNumbers.ToWire(text),
        FieldKind.Date => CalendarValues.ParseDate(text) is null ? null : text,
        FieldKind.Time => CalendarValues.ParseTime(text) is null ? null : text,
        FieldKind.DateTime => CalendarValues.ParseDateTime(text) is null ? null : text,
        _ => text,
    };
}
