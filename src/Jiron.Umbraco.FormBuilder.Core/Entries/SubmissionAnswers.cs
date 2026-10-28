using System.Collections.Immutable;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Core.Entries;

/// <summary>
/// One error of a submission (ADR-0072), shown at its field: an error kind of the field's type, one of the field's own
/// checks, or a form-level validation rule's message. A repeatable group's child has it in its row.
/// </summary>
/// <param name="Field">The key of the field, the child field or, for a validation rule's message, the grid row it shows at.</param>
/// <param name="Row">The row of a repeatable group's child, as posted, from 0; null for any other field.</param>
/// <param name="Kind">The error kind of the field's type; <see cref="FieldMessageKinds.None"/> for a check or a rule.</param>
/// <param name="Check">The index of the field's own check that fails, or null.</param>
/// <param name="Rule">The key of the form-level validation rule whose message shows, or null.</param>
internal sealed record SubmissionError(Guid Field, int? Row, FieldMessageKinds Kind, int? Check = null, Guid? Rule = null);

/// <summary>
/// An option with places that an answer chose (section 4.4, ADR-0090): one of the field's own options, or of those its
/// list or source resolved for the submission, whose number of places is set. Each row and each chosen option is one.
/// </summary>
/// <param name="Field">The key of the field; a repeatable group's child has its own.</param>
/// <param name="Row">The row of a repeatable group's child, as posted, from 0; null for any other field.</param>
/// <param name="Value">The option's value, as the answer chose it.</param>
/// <param name="Places">The option's number of places: 0 or more.</param>
internal sealed record SubmissionPlace(Guid Field, int? Row, string Value, int Places);

/// <summary>
/// An answer that must be unique in its form (<c>uniquePerForm</c>, ADR-0091): a field's or a row's answer that stands,
/// with its values as entries are compared by them and the indexes of the field's unique checks, which fail when another
/// entry holds one of the values.
/// </summary>
/// <param name="Field">The key of the field; a repeatable group's child has its own.</param>
/// <param name="Row">The row of a repeatable group's child, as posted, from 0; null for any other field.</param>
/// <param name="Checks">The indexes of the field's <c>uniquePerForm</c> checks in its validation list.</param>
/// <param name="Values">The answer's values, as entries are compared by them.</param>
internal sealed record SubmissionUnique(Guid Field, int? Row, ImmutableArray<int> Checks, ImmutableArray<AnswerValue> Values);

/// <summary>
/// A submission's answers over its whole form (ADR-0072), as <see cref="SubmissionReader"/> reads them: what the entry
/// stores, the errors by field and row, what the rules decide, and the answers as merge tags show them.
/// </summary>
/// <param name="Answers">
/// What the entry stores (ADR-0070), in the form's order: each shown field's values as its type read them, a
/// calculation's result as the server works it out and a hidden field's value as the server resolves it; a repeatable
/// group's children by their row, the rows that hold no value left out.
/// </param>
/// <param name="Errors">The errors of the shown fields, in the form's order and then by row; none when the submission is fine.</param>
/// <param name="State">What the rules decide over the answers: hidden and required fields, the actions that run, the outcome.</param>
/// <param name="ForMergeTags">The stored answers as merge tags show them (ADR-0066), with the fields the rules hid.</param>
internal sealed record SubmissionAnswers(
    ImmutableArray<EntryFieldAnswer> Answers,
    ImmutableArray<SubmissionError> Errors,
    FormState State,
    MergeTagAnswers ForMergeTags)
{
    /// <summary>Whether the submission has no error, so the entry may be stored.</summary>
    public bool IsValid => Errors.IsEmpty;

    /// <summary>
    /// The options with places that the answers that stand chose (ADR-0090), in the order of the form and its rows: what
    /// the pipeline holds against the places left.
    /// </summary>
    public ImmutableArray<SubmissionPlace> Places { get; init; } = [];

    /// <summary>
    /// The answers that stand of the fields with a <c>uniquePerForm</c> check (ADR-0091), in the order of the form and its
    /// rows: what the pipeline holds against the other entries' answers.
    /// </summary>
    public ImmutableArray<SubmissionUnique> Unique { get; init; } = [];
}
