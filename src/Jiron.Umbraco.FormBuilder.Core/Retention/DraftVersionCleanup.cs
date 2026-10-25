using Jiron.Umbraco.FormBuilder.Core.Audit;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Microsoft.Extensions.Logging;

namespace Jiron.Umbraco.FormBuilder.Core.Retention;

/// <summary>
/// The draft-version cleanup, the first part of the retention job (ADR-0002, ADR-0045). It removes the draft saves that
/// <see cref="HistorySettings"/> lets go: past a form's newest ones and older than the days, never pinned, never the
/// form's draft, never one an entry points at. A read nominates them; each form's are removed in a unit of their own
/// with a <c>form.draftsPruned</c> audit entry, each by a delete that checks again what may have changed since.
/// </summary>
internal sealed partial class DraftVersionCleanup(
    IFormRepository forms,
    IAuditRepository audit,
    FormBuilderDatabase database,
    IFormBuilderSettingsService settings,
    TimeProvider timeProvider,
    ILogger<DraftVersionCleanup> logger)
    : IFormBuilderRetentionPart
{
    /// <summary>The audit action of removed draft saves; the details list their numbers.</summary>
    public const string PrunedAction = "form.draftsPruned";

    /// <summary>The most draft saves one read nominates.</summary>
    public const int Nominations = 200;

    /// <inheritdoc />
    public string Name => "draft saves";

    /// <inheritdoc />
    public async Task RunAsync(DateTimeOffset deadline, CancellationToken cancellationToken)
    {
        var history = (await settings.GetAsync<GeneralSettings>(cancellationToken: cancellationToken)).Value.History;
        var cutoff = timeProvider.GetUtcNow() - TimeSpan.FromDays(history.DraftDays);
        int removed = 0, changedForms = 0;
        while (timeProvider.GetUtcNow() < deadline)
        {
            var nominated = await forms.ListDraftsToPruneAsync(cutoff, history.DraftCount, Nominations, cancellationToken);
            var progress = false;
            foreach (var form in nominated.GroupBy(draft => draft.FormId))
            {
                var pruned = await PruneAsync(form.Key, form.Select(draft => draft.Number).ToList(), cancellationToken);
                removed += pruned;
                changedForms += pruned > 0 ? 1 : 0;
                progress |= pruned > 0;
                if (timeProvider.GetUtcNow() >= deadline)
                {
                    break;
                }
            }

            // Nothing nominated, or every nominee changed since the read: the next run nominates afresh.
            if (!progress)
            {
                break;
            }
        }

        if (removed > 0)
        {
            LogPruned(logger, removed, changedForms);
        }
    }

    // One form's nominees in one unit with its audit entry. Each delete is the unit's write first, so the read of the form
    // for the entry's key comes after.
    private Task<int> PruneAsync(int formId, List<int> numbers, CancellationToken cancellationToken) =>
        database.WriteAsync(
            async _ =>
            {
                var pruned = new List<long>();
                foreach (var number in numbers)
                {
                    if (await forms.PruneDraftAsync(formId, number, cancellationToken))
                    {
                        pruned.Add(number);
                    }
                }

                if (pruned.Count > 0 && await forms.GetByIdAsync(formId, cancellationToken) is { } form)
                {
                    var details = new AuditDetails().AddNumbers("versions", pruned);
                    await audit.AddAsync(new NewAuditEntry(PrunedAction, FormAudit.EntityType, form.Key, null, details), cancellationToken);
                }

                return pruned.Count;
            },
            cancellationToken);

    [LoggerMessage(Level = LogLevel.Information, Message = "Jiron Form Builder removed old draft saves from the History (draft saves: {Count}, forms: {Forms})")]
    private static partial void LogPruned(ILogger logger, int count, int forms);
}
