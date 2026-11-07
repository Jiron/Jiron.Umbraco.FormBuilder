using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Queries;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Core.Entries;

/// <summary>What emptying Spam deletes, or would delete.</summary>
/// <param name="Entries">The entries.</param>
/// <param name="Files">Their uploaded files.</param>
/// <param name="More">Whether Spam holds more than one call takes, which a second Empty spam deletes.</param>
internal sealed record EntrySpamEmptying(int Entries, int Files, bool More);

/// <summary>
/// Empty spam (IA 6.2, ADR-0209): the entries in Spam of the forms a user may delete entries of, as the Spam list shows
/// them, deleted for good in batches through <see cref="EntryDeletions"/>, which audits and tells the site of each.
/// The caller passes the user's visibility with <c>deleteEntries</c>.
/// </summary>
/// <param name="queries">The entries query.</param>
/// <param name="forms">The forms.</param>
/// <param name="deletions">The deletes.</param>
internal sealed class EntrySpamBin(IEntryQueryRepository queries, IFormRepository forms, EntryDeletions deletions)
{
    /// <summary>The most entries one call deletes; more wait for the next.</summary>
    public const int MostPerCall = 10_000;

    /// <summary>What emptying Spam would delete, for the dialog that asks first.</summary>
    /// <param name="visibility">The forms the user may delete entries of.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public Task<EntrySpamEmptying> PlanAsync(FormVisibility visibility, CancellationToken cancellationToken = default) =>
        WalkAsync(visibility, null, cancellationToken);

    /// <summary>Deletes the entries in Spam for good, at most <see cref="MostPerCall"/>.</summary>
    /// <param name="visibility">The forms the user may delete entries of.</param>
    /// <param name="userKey">The user who empties Spam.</param>
    /// <param name="cancellationToken">Cancels what has not started.</param>
    public Task<EntrySpamEmptying> EmptyAsync(FormVisibility visibility, Guid userKey, CancellationToken cancellationToken = default) =>
        WalkAsync(visibility, userKey, cancellationToken);

    // A plan pages on through Spam; a delete starts at the top again each time, as the page before is gone.
    private async Task<EntrySpamEmptying> WalkAsync(FormVisibility visibility, Guid? userKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(visibility);
        var query = EntryQuery.Across(new EntryFormScope(visibility, [])) with { States = [EntryState.Spam] };
        var (entries, files) = (0, 0);
        EntryCursor? after = null;
        while (entries < MostPerCall)
        {
            var page = await queries.ListAsync(query, after, EntryDeletions.MaxEntries, count: false, cancellationToken);
            var taken = 0;
            foreach (var group in page.Entries.GroupBy(entry => entry.FormId))
            {
                if (await forms.GetByIdAsync(group.Key, cancellationToken) is not { } form)
                {
                    continue;
                }

                var keys = group.Select(entry => entry.Key).ToList();
                var done = userKey is { } user
                    ? await deletions.DeleteAsync(form.Key, keys, user, cancellationToken)
                    : await deletions.PlanAsync(form.Key, keys, cancellationToken);
                (entries, files, taken) = (entries + done.Entries, files + done.Files, taken + done.Entries);
            }

            // The end of Spam, or a page none of whose entries could go (held back meanwhile): nothing more to take.
            if (page.Next is null || taken == 0)
            {
                return new EntrySpamEmptying(entries, files, More: false);
            }

            after = userKey is null ? page.Next : null;
        }

        return new EntrySpamEmptying(entries, files, More: true);
    }
}
