using System.Collections.Immutable;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes;

/// <summary>A field's answers as its type read them (ADR-0058): the values, or that the answers cannot be read.</summary>
/// <param name="Answers">What the visitor gave.</param>
/// <param name="Values">The values in the wire form; none when the answers cannot be read or the field takes none.</param>
/// <param name="IsUnreadable">Whether the type could not read the answers.</param>
internal sealed record FieldRead(FieldAnswers Answers, ImmutableArray<FieldValue> Values, bool IsUnreadable);

/// <summary>
/// The three steps a visitor's answers to one field take through its type (ADR-0058), in the order the pipeline runs them
/// (6.1): the limits of every answer before the rules; each field read before the rules, so rules compare what was read;
/// and, for a field the rules leave visible, an unreadable answer, a missing required value or the type's checks.
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
    /// The errors of a field the rules leave visible, in order: <c>invalid</c> when its answers could not be read,
    /// <c>required</c> when it has no value and is required, otherwise what the type's check reports; none when it is fine.
    /// </summary>
    /// <param name="type">The field's type.</param>
    /// <param name="field">The field.</param>
    /// <param name="read">The answers as <see cref="Read"/> read them.</param>
    /// <param name="required">Whether the field is required, by its own setting or by a rule.</param>
    /// <param name="today">The date in the site's time zone, which the pipeline takes once per submission.</param>
    public static ImmutableArray<FieldMessageKinds> Check(IFormBuilderFieldType type, FormField field, FieldRead read, bool required, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(read);
        if (read.IsUnreadable)
        {
            return [FieldMessageKinds.Invalid];
        }

        if (read.Values.IsEmpty)
        {
            return required ? [FieldMessageKinds.Required] : [];
        }

        var validation = new FieldValidation(field, read.Answers, read.Values, type.Declaration.Messages, required, today);
        type.Validate(validation);
        return validation.Errors;
    }

    private static FieldAnswerLimits LimitsOf(IFormBuilderFieldType type, FormField field)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(field);
        return type.AnswerLimits(field)
            ?? throw new InvalidOperationException($"The field type '{type.Declaration.Alias}' ({type.GetType().FullName}) answered no limits for its answers.");
    }
}
