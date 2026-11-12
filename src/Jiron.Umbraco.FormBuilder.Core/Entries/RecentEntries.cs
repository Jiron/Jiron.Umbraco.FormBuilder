using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Microsoft.Extensions.Logging;

namespace Jiron.Umbraco.FormBuilder.Core.Entries;

/// <summary>One of the newest entries, as the Overview lists it.</summary>
/// <param name="Key">The entry's key.</param>
/// <param name="FormKey">The form's key.</param>
/// <param name="FormName">The form's name.</param>
/// <param name="Title">The entry's title in its own culture, without sensitive answers; null when it has none, and its number names it.</param>
/// <param name="Number">The entry's number within the form.</param>
/// <param name="Submitted">When it was received.</param>
/// <param name="Unseen">Whether nobody has opened it yet.</param>
internal sealed record RecentEntry(Guid Key, Guid FormKey, string FormName, string? Title, int Number, DateTimeOffset Submitted, bool Unseen);

/// <summary>
/// The newest entries a user may view (ADR-0165), named as the lists name them (section 4.3.4), for the Overview. It holds no
/// answers but what the form's entry title shows, which leaves sensitive fields out.
/// </summary>
/// <param name="access">The access evaluation.</param>
/// <param name="entries">The entries.</param>
/// <param name="forms">The forms.</param>
/// <param name="scopes">Reads an entry's scope.</param>
/// <param name="titles">Writes an entry's title.</param>
/// <param name="documents">Reads a stored version.</param>
/// <param name="logger">The log.</param>
internal sealed partial class RecentEntries(
    IFormBuilderAccessService access,
    IEntryRepository entries,
    IFormRepository forms,
    SubmissionEntries scopes,
    EntryTitles titles,
    FormDocuments documents,
    ILogger<RecentEntries> logger)
{
    /// <summary>How many entries the Overview lists.</summary>
    public const int Count = 10;

    /// <summary>The newest entries of the forms the user may view entries of, newest first.</summary>
    /// <param name="user">Who asks.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    public async Task<IReadOnlyList<RecentEntry>> ListAsync(AccessUser user, CancellationToken cancellationToken = default)
    {
        var visibility = await access.GetFormVisibilityAsync(user, FormBuilderPermissions.ViewEntries, cancellationToken);
        if (visibility.IsNothing)
        {
            return [];
        }

        var recent = new List<RecentEntry>(Count);
        var names = new Dictionary<int, StoredForm?>();
        foreach (var entry in await entries.ListRecentAsync(visibility, Count, cancellationToken))
        {
            if (!names.TryGetValue(entry.FormId, out var form))
            {
                form = names[entry.FormId] = await forms.GetByIdAsync(entry.FormId, cancellationToken);
            }

            if (form is not null)
            {
                recent.Add(new RecentEntry(entry.Key, form.Key, form.Name, await TitleAsync(entry, cancellationToken), entry.Number, entry.SubmittedUtc, entry.FirstSeenUtc is null));
            }
        }

        return recent;
    }

    // A form without a title costs one read of its version; the entry's answers, page and timeline are read only for one that has it.
    // An entry that can't be read is still an entry that arrived, so it goes on being named by its number.
    private async Task<string?> TitleAsync(StoredEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            if (await forms.GetVersionByIdAsync(entry.FormVersionId, cancellationToken) is not { } version
                || documents.Readable(version).Settings.Submission.EntryTitle is not { } title
                || string.IsNullOrWhiteSpace(title)
                || await scopes.ScopeAsync(entry, cancellationToken) is not { } scope)
            {
                return null;
            }

            return await titles.RenderAsync(scope, entry.Key, cancellationToken);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException && exception is not OperationCanceledException)
        {
            LogUnreadable(logger, LoggedException.Of(exception), entry.Key);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The entry {Entry} could not be read for its title.")]
    private static partial void LogUnreadable(ILogger logger, LoggedException exception, Guid entry);
}
