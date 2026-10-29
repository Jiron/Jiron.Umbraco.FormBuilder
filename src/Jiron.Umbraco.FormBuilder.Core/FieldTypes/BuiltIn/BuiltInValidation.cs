using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;

/// <summary>
/// What the checks of the built-in field types share (ADR-0062), the same as <c>packages/logic/src/checks.ts</c>; the
/// vectors of <c>tests/vectors/validation.json</c> hold both runtimes to one behaviour.
/// </summary>
internal static class BuiltInValidation
{
    /// <summary>Fails <c>minLength</c> or <c>maxLength</c> for a text whose UTF-16 code units fall outside the limits.</summary>
    public static void Length(FieldValidation validation, string text, int min, int max)
    {
        if (text.Length < min)
        {
            validation.Fail(FieldMessageKinds.MinLength);
        }
        else if (text.Length > max)
        {
            validation.Fail(FieldMessageKinds.MaxLength);
        }
    }

    /// <summary>Fails <c>minWords</c> or <c>maxWords</c> for a text whose words fall outside the limits.</summary>
    public static void Words(FieldValidation validation, string text, int? min, int? max)
    {
        var words = TextSemantics.CountWords(text);
        if (words < min)
        {
            validation.Fail(FieldMessageKinds.MinWords);
        }
        else if (words > max)
        {
            validation.Fail(FieldMessageKinds.MaxWords);
        }
    }

    /// <summary>
    /// Fails <c>min</c> or <c>max</c> for a number outside its limits, and <c>step</c> for one off its step, which counts
    /// from the minimum, or from 0 without one. A limit or a step that is no number, or a step of 0, limits nothing:
    /// publishing refuses them (ADR-0056), and a draft's preview must not fail on them.
    /// </summary>
    public static void Range(FieldValidation validation, string number, string? min, string? max, string step)
    {
        if (min is not null && ExactDecimal.CompareText(number, min) < 0)
        {
            validation.Fail(FieldMessageKinds.Min);
        }
        else if (max is not null && ExactDecimal.CompareText(number, max) > 0)
        {
            validation.Fail(FieldMessageKinds.Max);
        }

        if (ExactDecimal.Parse(number) is { } value && ExactDecimal.Parse(min ?? "0") is { } from && ExactDecimal.Parse(step) is { } by && !by.N.IsZero
            && !ExactDecimal.IsMultiple(ExactDecimal.Subtract(value, from), by))
        {
            validation.Fail(FieldMessageKinds.Step);
        }
    }

    /// <summary>The step of a number field that has none: 10 to the power of minus its decimals, so 1 without decimals.</summary>
    public static string StepOfPlaces(int decimals) => decimals == 0 ? "1" : "0." + new string('0', decimals - 1) + "1";

    /// <summary>Whether a number in the wire form is a whole number from <paramref name="low"/> to <paramref name="high"/>.</summary>
    public static bool IsWholeNumber(string number, int low, int high) =>
        ExactDecimal.Parse(number) is { S: 0 } value && value.N >= low && value.N <= high;

    /// <summary>
    /// Fails <c>dateWindow</c> for a date, as a day number, before the earliest date or after the latest, on a Saturday or
    /// a Sunday when the field takes weekdays only, or on an excluded date. A limit relative to today counts from the
    /// site's today; one that falls outside the calendar lets no date through.
    /// </summary>
    public static void DateWindow(FieldValidation validation, int day, JsonElement? min, JsonElement? max, bool weekdaysOnly, IReadOnlyList<string> excluded)
    {
        var today = validation.Today.DayNumber;
        var fits = TryBound(min, today, out var low) && TryBound(max, today, out var high)
            && (low is null || day >= low) && (high is null || day <= high)
            && !(weekdaysOnly && DateOnly.FromDayNumber(day).DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            && !excluded.Any(date => CalendarValues.ParseDate(date) == day);
        if (!fits)
        {
            validation.Fail(FieldMessageKinds.DateWindow);
        }
    }

    /// <summary>Whether a time, in seconds since midnight, keeps a step of minutes that counts from a start; any time without a step.</summary>
    public static bool OnMinuteStep(int seconds, int from, int? stepMinutes) =>
        stepMinutes is not { } minutes || (seconds - from) % (minutes * 60) == 0;

    /// <summary>
    /// Fails <c>invalid</c> unless each of the answers' own values is one of the options the field offers
    /// (<see cref="FieldValidation.Options"/>, ADR-0077); the "Other" text, which is a part, is the visitor's own.
    /// </summary>
    public static void AmongOptions(FieldValidation validation)
    {
        if (validation.Options is { } options && !validation.Answers.Values.All(options.Contains))
        {
            validation.Fail(FieldMessageKinds.Invalid);
        }
    }

    /// <summary>Fails <c>selections</c> for a number of values below the fewest or above the most.</summary>
    public static void Selections(FieldValidation validation, int? min, int? max)
    {
        if (validation.Values.Length < min || validation.Values.Length > max)
        {
            validation.Fail(FieldMessageKinds.Selections);
        }
    }

    /// <summary>
    /// Fails <c>required</c> when a part the value needs has no text (ADR-0056): the listed parts the field asks for, or,
    /// when it lists none, the given parts once the field is required.
    /// </summary>
    /// <param name="validation">The field, its answers and where errors go.</param>
    /// <param name="asked">The names of the parts the field asks for.</param>
    /// <param name="listed">The names of the parts its settings require.</param>
    /// <param name="unlisted">The names of the parts a required field needs when its settings list none.</param>
    public static void RequiredParts(FieldValidation validation, IEnumerable<string> asked, IReadOnlyCollection<string> listed, IEnumerable<string> unlisted)
    {
        var needed = listed.Count > 0 ? listed.Intersect(asked, StringComparer.Ordinal) : validation.IsRequired ? unlisted : [];
        if (needed.Any(part => validation.Answers.Part(part).IsEmpty))
        {
            validation.Fail(FieldMessageKinds.Required);
        }
    }

    /// <summary>A text with its ASCII capitals made small and every other character left as it is.</summary>
    public static string AsciiLower(string text) => string.Create(text.Length, text, static (span, source) =>
    {
        for (var i = 0; i < source.Length; i++)
        {
            span[i] = source[i] is >= 'A' and <= 'Z' ? (char)(source[i] + 32) : source[i];
        }
    });

    /// <summary>
    /// The day number of a date's limit, a date or a date relative to today (ADR-0062): null for none, and false for one
    /// that falls outside the calendar or is no limit at all (publishing refuses those, a draft may hold them). The page
    /// gives the browser the same limits (ADR-0097).
    /// </summary>
    /// <param name="limit">The limit as the settings hold it, or null for none.</param>
    /// <param name="today">The site's today as a day number.</param>
    /// <param name="day">The limit's day number, or null.</param>
    public static bool TryBound(JsonElement? limit, int today, out int? day)
    {
        day = null;
        if (limit is not { } bound)
        {
            return true;
        }

        if (bound.ValueKind == JsonValueKind.String)
        {
            day = CalendarValues.ParseDate(bound.GetString()!);
        }
        else if (bound.ValueKind == JsonValueKind.Object && bound.TryGetProperty("today", out var offset) && offset.ValueKind == JsonValueKind.String
            && CalendarValues.ParseRelative(offset.GetString()!) is { } steps)
        {
            day = CalendarValues.ApplyRelative(today, steps);
        }

        return day is not null;
    }
}
