using System.Collections.Immutable;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes;

/// <summary>A field's answers as its type read them (ADR-0058): the values, or that the answers cannot be read.</summary>
/// <param name="Answers">What the visitor gave.</param>
/// <param name="Values">The values in the wire form; none when the answers cannot be read or the field takes none.</param>
/// <param name="IsUnreadable">Whether the type could not read the answers.</param>
internal sealed record FieldRead(FieldAnswers Answers, ImmutableArray<FieldValue> Values, bool IsUnreadable);

/// <summary>What the pipeline hands a visible field's checks besides its answers (ADR-0062).</summary>
/// <param name="IsRequired">Whether the field is required, by its own setting or by a rule.</param>
/// <param name="Today">The date in the site's time zone, which the pipeline takes once per submission.</param>
internal sealed record FieldCheckInput(bool IsRequired, DateOnly Today)
{
    /// <summary>For a field that stores files, the file each upload token stands for, in order; none for any other field.</summary>
    public ImmutableArray<FieldFile> Files { get; init => field = value.IsDefault ? [] : value; } = [];

    /// <summary>
    /// The values of the form's other fields by key, as the rules see them (a hidden field has none), which a
    /// <c>matchesField</c> check compares the field's values with, and whose parent's values filter a cascading field's
    /// options (ADR-0077).
    /// </summary>
    public IReadOnlyDictionary<Guid, ImmutableArray<string>> Values { get; init; } = ImmutableDictionary<Guid, ImmutableArray<string>>.Empty;

    /// <summary>
    /// The options the field's list or other source resolved for the submission, each with the parent answers it belongs
    /// to (ADR-0077); null for a field whose options are its own, which the check reads from the field, and for one whose
    /// source could not give its options, which then takes any answer.
    /// </summary>
    public IReadOnlyList<FormBuilderOption>? Options { get; init; }
}

/// <summary>A visible field's errors (ADR-0062).</summary>
/// <param name="Errors">The error kinds of its type's checks, in the order they are reported, each once.</param>
/// <param name="Checks">The indexes of the field's own checks that fail, in their order.</param>
internal sealed record FieldErrors(ImmutableArray<FieldMessageKinds> Errors, ImmutableArray<int> Checks)
{
    /// <summary>No error.</summary>
    public static FieldErrors None { get; } = new([], []);
}

/// <summary>
/// The three steps a visitor's answers to one field take through its type (ADR-0058), in the order the pipeline runs them
/// (6.1): the limits of every answer before the rules; each field read before the rules, so rules compare what was read;
/// and, for a field the rules leave visible, an unreadable answer, a missing required value or the type's checks. A
/// repeatable group's own check is of its number of rows (ADR-0071).
/// </summary>
internal static class FieldValues
{
    /// <summary>
    /// Whether the answers keep the longest answer the field takes: null when they do, or when the field takes no answer
    /// and the pipeline ignores them; otherwise <c>maxLength</c> for a text that is too long when the type declares that
    /// kind, and <c>invalid</c> for anything else over the limits.
    /// </summary>
    public static FieldMessageKinds? CheckLimits(IFormBuilderFieldType type, FormField field, FieldAnswers answers)
    {
        ArgumentNullException.ThrowIfNull(answers);
        var limits = LimitsOf(type, field);
        if (!limits.TakesAnswers || limits.Allows(answers))
        {
            return null;
        }

        return answers.LongestText > limits.LongestText && type.Declaration.Messages.HasFlag(FieldMessageKinds.MaxLength)
            ? FieldMessageKinds.MaxLength
            : FieldMessageKinds.Invalid;
    }

    /// <summary>
    /// Whether a signed-in member's value stands in for a field hidden from them (ADR-0124): it keeps the field's limits,
    /// the type reads a value from it, and it passes the type's checks and the field's own, so an entry never fails on a
    /// value the member can neither see nor change. The page shows the field to the member otherwise, and the posted
    /// answer counts.
    /// </summary>
    /// <param name="type">The field's type.</param>
    /// <param name="field">The field.</param>
    /// <param name="answers">The member's value.</param>
    /// <param name="today">Today in the form's time zone, for a date's checks.</param>
    public static bool StandsIn(IFormBuilderFieldType type, FormField field, FieldAnswers answers, DateOnly today) =>
        StandsIn(type, field, answers, new FieldCheckInput(IsRequired: false, today));

    /// <summary>
    /// Whether a value stands in for a visitor's answer, with what the checks know besides it: the other fields' values
    /// and the options a list or another source resolved, as a sample entry's answers are held (ADR-0187).
    /// </summary>
    /// <param name="type">The field's type.</param>
    /// <param name="field">The field.</param>
    /// <param name="answers">The value.</param>
    /// <param name="input">Today, the other fields' values and the resolved options; not required, and no files.</param>
    public static bool StandsIn(IFormBuilderFieldType type, FormField field, FieldAnswers answers, FieldCheckInput input)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(answers);
        ArgumentNullException.ThrowIfNull(input);
        if (type.Declaration.Storage == FieldStorageKind.File || CheckLimits(type, field, answers) is not null)
        {
            return false;
        }

        var read = Read(type, field, answers);
        if (read.IsUnreadable || read.Values.IsEmpty)
        {
            return false;
        }

        var errors = Check(type, field, read, input);
        return errors.Errors.IsEmpty && errors.Checks.IsEmpty;
    }

    /// <summary>The answers read by the field's type; no values for a field that takes no answer.</summary>
    /// <exception cref="InvalidOperationException">The type read a value its storage kind cannot hold (<see cref="EntryValueColumns.Holds"/>).</exception>
    public static FieldRead Read(IFormBuilderFieldType type, FormField field, FieldAnswers answers)
    {
        ArgumentNullException.ThrowIfNull(answers);
        if (!LimitsOf(type, field).TakesAnswers)
        {
            return new FieldRead(answers, [], IsUnreadable: false);
        }

        var reading = new FieldReading(field, answers);
        type.Read(reading);
        var storage = type.Declaration.Storage;
        if (reading.Values.Any(value => !EntryValueColumns.Holds(storage, value)))
        {
            // The message names the type and leaves out the visitor's answer, which has no place in a log.
            throw new InvalidOperationException(
                $"The field type '{type.Declaration.Alias}' ({type.GetType().FullName}) read a value its storage kind cannot hold.");
        }

        return new FieldRead(answers, reading.Values, reading.IsInvalid);
    }

    /// <summary>
    /// The errors of a field the rules leave visible (ADR-0062): <c>invalid</c> when its answers could not be read;
    /// <c>required</c> when it has no value and is required, or has a row marked required (ADR-0069); <c>digits</c>, or
    /// <c>invalid</c> for a type without that kind, alone when it stores numbers and a value has more digits than a stored
    /// number keeps exactly (ADR-0003); otherwise what the type's check reports, followed by the field's own checks that
    /// fail; none when it is fine.
    /// </summary>
    /// <param name="type">The field's type.</param>
    /// <param name="field">The field.</param>
    /// <param name="read">The answers as <see cref="Read"/> read them.</param>
    /// <param name="input">What the pipeline knows besides the answers: requiredness, today, files and the other fields' values.</param>
    /// <exception cref="ArgumentException">
    /// A field that stores files has not one file for each value, or a field that stores none has files.
    /// </exception>
    public static FieldErrors Check(IFormBuilderFieldType type, FormField field, FieldRead read, FieldCheckInput input)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(input);
        if (read.IsUnreadable)
        {
            return new([FieldMessageKinds.Invalid], []);
        }

        if (read.Values.IsEmpty)
        {
            return input.IsRequired || field.Rows.Any(row => row.Required) ? new([FieldMessageKinds.Required], []) : FieldErrors.None;
        }

        var storesFiles = type.Declaration.Storage == FieldStorageKind.File;
        if (input.Files.Length != (storesFiles ? read.Values.Length : 0))
        {
            throw new ArgumentException(
                storesFiles ? "A field that stores files has one file for each upload token." : "Only a field that stores files has files.", nameof(input));
        }

        if (type.Declaration.Storage == FieldStorageKind.Number && read.Values.Any(value => ExactDecimal.Parse(value.Text) is { } number && !ExactDecimal.Fits(number)))
        {
            return new([type.Declaration.Messages.HasFlag(FieldMessageKinds.Digits) ? FieldMessageKinds.Digits : FieldMessageKinds.Invalid], []);
        }

        var validation = new FieldValidation(field, read.Answers, read.Values, type.Declaration.Messages, input);
        type.Validate(validation);
        return new(validation.Errors, OwnChecks(type, field, [.. read.Values.Select(value => value.Text)], input.Values));
    }

    /// <summary>
    /// The errors of a visible field's rows (ADR-0071): what its type's check of the number of rows a submission gives
    /// it reports, in order, each once; none when they are fine. The child fields' checks run row by row, apart.
    /// </summary>
    /// <param name="type">The field's type, which has child fields.</param>
    /// <param name="field">The field.</param>
    /// <param name="rows">The number of rows.</param>
    /// <exception cref="ArgumentException">The type has no child fields.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The number of rows is negative.</exception>
    public static ImmutableArray<FieldMessageKinds> CheckRows(IFormBuilderFieldType type, FormField field, int rows)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentOutOfRangeException.ThrowIfNegative(rows);
        if (!type.Declaration.Features.HasFlag(FieldFeatures.ChildFields))
        {
            throw new ArgumentException($"The field type '{type.Declaration.Alias}' has no child fields, so its fields have no rows.", nameof(type));
        }

        var validation = new FieldRowsValidation(field, rows, type.Declaration.Messages);
        type.ValidateRows(validation);
        return validation.Errors;
    }

    // The indexes of the field's own checks that fail, of those that need no other entry (ADR-0062): every value matches
    // the whole pattern, and the values match the other field's. A pattern outside the safe subset, which publishing
    // refuses, checks nothing, so a draft's preview does not fail on it; uniquePerForm runs with the submission, which
    // holds its answers against the other entries' (ADR-0091).
    private static ImmutableArray<int> OwnChecks(IFormBuilderFieldType type, FormField field, ImmutableArray<string> values, IReadOnlyDictionary<Guid, ImmutableArray<string>> others)
    {
        var failing = ImmutableArray.CreateBuilder<int>();
        for (var i = 0; i < field.Validation.Count; i++)
        {
            var holds = field.Validation[i] switch
            {
                PatternCheck pattern => Compiled(pattern.Pattern) is not { } compiled || values.All(compiled.IsMatch),
                MatchesFieldCheck matches => Rules.SameValues(type.Declaration.RuleKind, values, others.TryGetValue(matches.Field, out var other) ? other : []),
                _ => true,
            };
            if (!holds)
            {
                failing.Add(i);
            }
        }

        return failing.ToImmutable();
    }

    private static SafePattern? Compiled(string pattern)
    {
        try
        {
            return SafePattern.Compile(pattern);
        }
        catch (LogicException)
        {
            return null;
        }
    }

    private static FieldAnswerLimits LimitsOf(IFormBuilderFieldType type, FormField field)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(field);
        return type.AnswerLimits(field)
            ?? throw new InvalidOperationException($"The field type '{type.Declaration.Alias}' ({type.GetType().FullName}) answered no limits for its answers.");
    }
}
