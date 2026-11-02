using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Retention;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Infrastructure.Persistence;

namespace Jiron.Umbraco.FormBuilder.Core.Actions;

/// <summary>
/// What goes once an entry's actions are through (section 4.4, ADR-0158): the fields kept only until the actions have run,
/// with the entry event that records it, and a whole entry of a form that stores none
/// (<c>settings.submission.store: never</c>), its files too. Both wait until every run of the entry is final, its mail has
/// left or failed, as a mail still reads the files it attaches, and no trigger can still fire for it. The check runs in the unit that makes the last run final, which holds the entry's row first, so
/// two last runs on two servers cannot both miss it and a crash cannot leave the values behind.
/// </summary>
internal sealed partial class ActionSettlement(
    FormBuilderDatabase database,
    IEntryRepository entries,
    IActionRunRepository runs,
    IEntryEventRepository events,
    IEmailRepository emails,
    IFormRepository forms,
    IKeyValueRepository keyValues,
    FormDocuments documents,
    EntryAnswerStore store,
    IFormBuilderFileStore files,
    TimeProvider time,
    ILogger<ActionSettlement> logger)
{
    /// <summary>The kind of the event that records the removal of the kept values.</summary>
    public const string ValuesRemovedEvent = "valuesRemoved";

    // What marks a used challenge of an entry that is gone, so a form that stores nothing keeps a challenge to one entry.
    private const string UsedChallengePrefix = "jfb.usedchallenge.";

    /// <summary>
    /// Whether an action of the form may still run for an entry that has none queued (section 4.4): any while it waits for
    /// its address to be confirmed or sits in Spam, an approved or rejected one while it waits for approval, and an approved
    /// one when it is approved at once. None once it was approved or rejected.
    /// </summary>
    public static bool TriggerCanFire(FormDefinition definition, EntryState state)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var enabled = definition.Actions.Where(action => action.Enabled).ToList();
        return state switch
        {
            EntryState.Unverified or EntryState.Spam => enabled.Count > 0,
            EntryState.Approved or EntryState.Rejected => false,
            _ => definition.Settings.Submission.Approval == ApprovalMode.Manual
                ? enabled.Any(action => action.Trigger is ActionTrigger.Approved or ActionTrigger.Rejected)
                : enabled.Any(action => action.Trigger == ActionTrigger.Approved),
        };
    }

    /// <summary>Whether the field keeps its value only until the actions have run.</summary>
    public static bool KeptUntilActions(FormDefinition definition, Guid field)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return FieldsOf(definition).FirstOrDefault(item => item.Key == field)?.Keep == FieldKeep.UntilActionsRun;
    }

    /// <summary>Whether a challenge made an entry that is gone since, on a form that stored none then.</summary>
    /// <param name="challengeId">The challenge's id as an entry stores it.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task<bool> ChallengeUsedAsync(string challengeId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(challengeId);
        return await keyValues.GetAsync(UsedChallengePrefix + challengeId, cancellationToken) is not null;
    }

    /// <summary>Forgets the used challenges no longer in their time; how many.</summary>
    /// <param name="cancellationToken">Cancels the delete.</param>
    public Task<int> ForgetUsedChallengesAsync(CancellationToken cancellationToken) =>
        keyValues.DeleteOlderAsync(UsedChallengePrefix, (time.GetUtcNow() - FormChallenges.Lifetime - FormChallenges.ReceiptGrace).UtcDateTime, cancellationToken);

    /// <summary>
    /// Records an outcome that may make a run final, and settles the entry in the same unit when its form has something to
    /// settle: <paramref name="record"/>'s answer, whether the token still held the run.
    /// </summary>
    /// <param name="run">The run.</param>
    /// <param name="record">The repository's statement that records the outcome.</param>
    public async Task<bool> FinishAsync(StoredActionRun run, Func<Task<bool>> record)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(record);
        var definition = await DefinitionOfAsync(run.FormVersionId);
        if (definition is null || !HasSomethingToSettle(definition) || await entries.GetByIdAsync(run.EntryId, CancellationToken.None) is not { } entry)
        {
            return await record();
        }

        IReadOnlyList<StoredEntryFile> gone = [];
        var recorded = await database.WriteAsync(
            async db =>
            {
                gone = [];
                await HoldAsync(db, entry.FormId, run.EntryId);
                if (!await record())
                {
                    return false;
                }

                gone = await SettleInUnitAsync(run.EntryId, definition, CancellationToken.None);
                return true;
            });
        await DeleteBytesAsync(gone);
        return recorded;
    }

    /// <summary>
    /// Settles an entry whose state changed or that has no runs, in a unit of its own; the bytes of its files go after the
    /// commit.
    /// </summary>
    /// <param name="entryId">The entry.</param>
    /// <param name="definition">The version it was submitted with.</param>
    /// <param name="cancellationToken">Cancels the unit of work.</param>
    public async Task SettleAsync(long entryId, FormDefinition definition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!HasSomethingToSettle(definition) || await entries.GetByIdAsync(entryId, cancellationToken) is not { } entry)
        {
            return;
        }

        await DeleteBytesAsync(await database.WriteAsync(
            async db =>
            {
                await HoldAsync(db, entry.FormId, entryId);
                return await SettleInUnitAsync(entryId, definition, cancellationToken);
            },
            cancellationToken));
    }

    /// <summary>
    /// Settles the entry of a mail that has left or failed, which may have been the last thing it waited for. It never decides what
    /// came of the mail, so a failure is logged.
    /// </summary>
    /// <param name="entryId">The entry.</param>
    public async Task AfterMailAsync(long entryId)
    {
        try
        {
            if (await entries.GetByIdAsync(entryId, CancellationToken.None) is { } entry && await DefinitionOfAsync(entry.FormVersionId) is { } definition)
            {
                await SettleAsync(entryId, definition, CancellationToken.None);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogSettleAfterMailFailed(logger, entryId, exception);
        }
    }

    /// <summary>
    /// Settles an entry inside the caller's unit, which has written first and holds the entry's row: what goes, when
    /// nothing can run for it any more. The files it returns are the caller's to delete after the commit
    /// (<see cref="DeleteBytesAsync"/>).
    /// </summary>
    /// <param name="entryId">The entry.</param>
    /// <param name="definition">The version it was submitted with.</param>
    /// <param name="cancellationToken">Cancels the unit of work.</param>
    public async Task<IReadOnlyList<StoredEntryFile>> SettleInUnitAsync(long entryId, FormDefinition definition, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (await entries.GetByIdAsync(entryId, cancellationToken) is not { } entry
            || entry.State == EntryState.Draft
            || await events.HasMoveDueAsync(entryId, cancellationToken)
            || (await emails.ListForEntryAsync(entryId, cancellationToken)).Any(mail => mail.State is EmailState.Queued or EmailState.Sending or EmailState.Uncertain)
            || (await runs.ListForEntryAsync(entryId, cancellationToken)).Any(run => run.State is ActionRunState.Pending or ActionRunState.Running or ActionRunState.Uncertain)
            || TriggerCanFire(definition, entry.State))
        {
            return [];
        }

        if (definition.Settings.Submission.Store == EntryStorage.Never)
        {
            // An entry a person may still confirm or take out of Spam stays until then, or until retention takes it.
            return entry.State is EntryState.Unverified or EntryState.Spam ? [] : await RemoveEntryAsync(entry, cancellationToken);
        }

        return await RemoveKeptValuesAsync(entry, definition, cancellationToken);
    }

    /// <summary>Deletes the bytes of files whose rows went in a unit that has committed; a failure is logged, as nothing can roll back.</summary>
    /// <param name="gone">The files.</param>
    public async Task DeleteBytesAsync(IReadOnlyList<StoredEntryFile> gone)
    {
        ArgumentNullException.ThrowIfNull(gone);
        foreach (var file in gone)
        {
            try
            {
                await files.DeleteAsync(file.StoredName, CancellationToken.None);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                LogBytesFailed(logger, file.Key, exception);
            }
        }
    }

    // The form first, as every unit that touches what belongs to it does (ADR-0036), then the entry.
    private static async Task HoldAsync(IUmbracoDatabase db, int formId, long entryId)
    {
        var names = new SqlNames(db);
        await RowHolds.HoldFormAsync(db, names, formId);
        await RowHolds.HoldEntryAsync(db, names, entryId);
    }

    private static bool HasSomethingToSettle(FormDefinition definition) =>
        definition.Settings.Submission.Store == EntryStorage.Never || FieldsOf(definition).Any(field => field.Keep == FieldKeep.UntilActionsRun);

    private static IEnumerable<FormField> FieldsOf(FormDefinition definition) =>
        definition.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields).SelectMany(field => field.Fields.Prepend(field));

    private async Task<FormDefinition?> DefinitionOfAsync(int versionId)
    {
        try
        {
            return await forms.GetVersionByIdAsync(versionId, CancellationToken.None) is { } version ? documents.Readable(version) : null;
        }
        catch (FormBuilderValidationException)
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<StoredEntryFile>> RemoveEntryAsync(StoredEntry entry, CancellationToken cancellationToken)
    {
        var stored = await entries.ListFilesAsync([entry.Id], cancellationToken);
        await emails.DeleteUnsentAsync([entry.Id], cancellationToken);
        if (entry.ChallengeId is { } challenge)
        {
            await keyValues.SetAsync(UsedChallengePrefix + challenge, "1", cancellationToken);
        }

        await entries.DeleteAsync([entry.Id], cancellationToken);
        return stored;
    }

    private async Task<IReadOnlyList<StoredEntryFile>> RemoveKeptValuesAsync(StoredEntry entry, FormDefinition definition, CancellationToken cancellationToken)
    {
        var kept = FieldsOf(definition).Where(field => field.Keep == FieldKeep.UntilActionsRun).Select(field => field.Key).ToHashSet();
        if (kept.Count == 0)
        {
            return [];
        }

        var catalogue = await forms.ListFieldsAsync(entry.FormId, cancellationToken);
        var rows = catalogue.Where(row => kept.Contains(row.Values.FieldKey) || (row.Values.ParentFieldKey is { } parent && kept.Contains(parent))).Select(row => row.Id).ToHashSet();
        var values = await entries.ListValuesAsync([entry.Id], cancellationToken);
        if (!values.Any(value => rows.Contains(value.FieldId)))
        {
            return [];
        }

        var searchText = store.SearchTextOf(definition, catalogue, values.Where(value => !rows.Contains(value.FieldId)));
        await entries.ReplaceValuesAsync(entry.Id, rows, [], searchText, cancellationToken);
        var gone = (await entries.ListFilesAsync(entry.Id, cancellationToken)).Where(file => kept.Contains(file.FieldKey)).ToList();
        foreach (var file in gone)
        {
            await entries.DeleteFileAsync(file.Id, entry.Id, cancellationToken);
        }

        await events.AddAsync(new NewEntryEvent(entry.Id, ValuesRemovedEvent, Details: JsonSerializer.Serialize(new { fields = kept.Select(key => key.ToString("D")).Order(StringComparer.Ordinal) })), cancellationToken);
        return gone;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Entry {Entry} could not be settled after its mail; the retention job looks again")]
    private static partial void LogSettleAfterMailFailed(ILogger logger, long entry, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The bytes of file {File} could not be deleted after its entry let it go")]
    private static partial void LogBytesFailed(ILogger logger, Guid file, Exception exception);
}

/// <summary>
/// Finds the entries of forms that store none and that nothing holds any more (ADR-0158): one a request or a server left behind
/// between its commit and letting it go, as nothing else would look at an entry without runs again. Each form's oldest few, within
/// the job's time; an entry that may still go on stays.
/// </summary>
internal sealed class NeverStoredEntryCleanup(IFormRepository forms, IEntryRepository entries, FormDocuments documents, ActionSettlement settlement, TimeProvider time) : IFormBuilderRetentionPart
{
    /// <summary>How many entries of a form one run looks at.</summary>
    public const int PerForm = 50;

    /// <inheritdoc />
    public string Name => "entries of forms that store none";

    /// <inheritdoc />
    public async Task RunAsync(DateTimeOffset deadline, CancellationToken cancellationToken)
    {
        for (var skip = 0L; ;)
        {
            var page = await forms.ListAsync(FormFilter.Live, new PageRequest(skip, PageRequest.MaxTake), cancellationToken);
            foreach (var form in page.Items)
            {
                if (time.GetUtcNow() >= deadline)
                {
                    return;
                }

                if (form.PublishedVersionId is not { } published
                    || await forms.GetVersionByIdAsync(published, cancellationToken) is not { } version
                    || documents.Readable(version) is not { Settings.Submission.Store: EntryStorage.Never } definition)
                {
                    continue;
                }

                foreach (var id in await entries.ListIdsAsync(form.Id, PerForm, cancellationToken))
                {
                    await settlement.SettleAsync(id, definition, cancellationToken);
                }
            }

            skip += page.Items.Count;
            if (page.Items.Count == 0 || skip >= page.Total)
            {
                return;
            }
        }
    }
}
