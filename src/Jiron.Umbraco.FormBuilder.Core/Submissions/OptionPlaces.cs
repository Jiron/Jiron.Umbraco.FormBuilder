using System.Collections.Immutable;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Core.Submissions;

/// <summary>
/// Holds a submission's choices against the places left on their options (section 4.4, ADR-0090): while its answers are
/// read, by the counts of the other entries, and inside its unit of work, once its own values are counted, by counts the
/// unit holds. A choice past an option's places fails with <c>full</c> at its field and row; the first choices in the
/// order of the form and its rows get the places left.
/// </summary>
internal sealed class OptionPlaces(IEntryRepository entries)
{
    /// <summary>The choices past the places the other entries leave, read without a unit of work.</summary>
    /// <param name="formId">The form.</param>
    /// <param name="places">The submission's choices of options with places, in the order of the form and its rows.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task<ImmutableArray<SubmissionError>> CheckAsync(int formId, ImmutableArray<SubmissionPlace> places, CancellationToken cancellationToken = default) =>
        places.IsEmpty ? [] : Full(places, await entries.GetPlacesTakenAsync(formId, Options(places), cancellationToken), counted: false);

    /// <summary>
    /// The choices past the places, inside the unit of work that stored the submission's entry, whose values the counts
    /// hold already; the unit locked the form's row, so the counts hold until it ends.
    /// </summary>
    /// <param name="formId">The form.</param>
    /// <param name="places">The entry's choices of options with places, in the order of the form and its rows.</param>
    /// <param name="cancellationToken">Cancels the unit of work.</param>
    public async Task<ImmutableArray<SubmissionError>> CountAsync(int formId, ImmutableArray<SubmissionPlace> places, CancellationToken cancellationToken = default) =>
        places.IsEmpty ? [] : Full(places, await entries.CountPlacesAsync(formId, Options(places), cancellationToken), counted: true);

    private static List<OptionPlace> Options(ImmutableArray<SubmissionPlace> places) =>
        [.. places.Select(place => new OptionPlace(place.Field, place.Value)).Distinct()];

    // Each option's choices in their order, the first ones on the places the others leave; each field and row fails once.
    private static ImmutableArray<SubmissionError> Full(ImmutableArray<SubmissionPlace> places, IReadOnlyDictionary<OptionPlace, int> counts, bool counted)
    {
        var chosen = places.GroupBy(place => new OptionPlace(place.Field, place.Value)).ToDictionary(group => group.Key, group => group.ToList());
        var failing = new HashSet<SubmissionPlace>();
        foreach (var (option, choices) in chosen)
        {
            var others = counts.GetValueOrDefault(option) - (counted ? choices.Count : 0);
            var limit = choices.Min(choice => choice.Places);
            failing.UnionWith(choices.Skip(Math.Max(0, limit - others)));
        }

        return [.. places.Where(failing.Contains).Select(place => (place.Field, place.Row)).Distinct().Select(at => new SubmissionError(at.Field, at.Row, FieldMessageKinds.Full))];
    }
}

/// <summary>
/// Thrown inside a submission's unit of work when its answers fail against the other entries (ADR-0090): the unit rolls
/// back, entry and number with it, and the pipeline answers the errors.
/// </summary>
internal sealed class SubmissionInvalidException : Exception
{
    /// <summary>Creates the exception for these errors.</summary>
    /// <param name="errors">The errors, in the order of the form and its rows.</param>
    public SubmissionInvalidException(ImmutableArray<SubmissionError> errors)
        : base("The submission's answers fail against the form's other entries.")
    {
        Errors = errors;
    }

    /// <summary>The errors.</summary>
    public ImmutableArray<SubmissionError> Errors { get; }
}
