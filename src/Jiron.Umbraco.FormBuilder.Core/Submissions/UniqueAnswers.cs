using System.Collections.Immutable;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Core.Submissions;

/// <summary>
/// Holds a submission's answers to fields with a <c>uniquePerForm</c> check against the answers other entries hold
/// (ADR-0091): while its answers are read, and inside its unit of work, once its entry is stored, under the form's row.
/// An answer another entry holds fails each of its field's unique checks at its field and row. A page's validation never
/// asks, so it cannot tell whether an answer is taken (ADR-0012).
/// </summary>
internal sealed class UniqueAnswers(IEntryRepository entries)
{
    /// <summary>The answers another entry holds, read without a unit of work.</summary>
    /// <param name="formId">The form.</param>
    /// <param name="answers">The submission's answers that must be unique, in the order of the form and its rows.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task<ImmutableArray<SubmissionError>> CheckAsync(int formId, ImmutableArray<SubmissionUnique> answers, CancellationToken cancellationToken = default) =>
        answers.IsEmpty ? [] : Taken(answers, await entries.GetTakenAnswersAsync(formId, Values(answers), cancellationToken));

    /// <summary>
    /// The answers another entry than the submission's holds, inside the unit of work that stored it: the unit holds the
    /// form's row, so no other entry takes or gives back an answer until it ends.
    /// </summary>
    /// <param name="entryId">The submission's entry.</param>
    /// <param name="answers">The entry's answers that must be unique, in the order of the form and its rows.</param>
    /// <param name="cancellationToken">Cancels the unit of work.</param>
    public async Task<ImmutableArray<SubmissionError>> FindAsync(long entryId, ImmutableArray<SubmissionUnique> answers, CancellationToken cancellationToken = default) =>
        answers.IsEmpty ? [] : Taken(answers, await entries.FindTakenAnswersAsync(entryId, Values(answers), cancellationToken));

    private static List<AnswerValue> Values(ImmutableArray<SubmissionUnique> answers) => [.. answers.SelectMany(answer => answer.Values).Distinct()];

    private static ImmutableArray<SubmissionError> Taken(ImmutableArray<SubmissionUnique> answers, IReadOnlySet<AnswerValue> taken) =>
    [
        .. answers
            .Where(answer => answer.Values.Any(taken.Contains))
            .SelectMany(answer => answer.Checks.Select(check => new SubmissionError(answer.Field, answer.Row, FieldMessageKinds.None, Check: check))),
    ];
}
