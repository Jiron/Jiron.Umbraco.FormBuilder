using System.Globalization;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Audit;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Core.Retention;

/// <summary>Why a legal hold was refused (ADR-0222).</summary>
internal enum EntryHoldRefusal
{
    /// <summary>Held, or released.</summary>
    None,

    /// <summary>The entry does not exist.</summary>
    NotFound,

    /// <summary>The entry is a draft or waits for its address, which their own links end, and no hold keeps.</summary>
    NotKept,

    /// <summary>The end is not after now, or more than <see cref="EntryHolds.MaxDays"/> days ahead.</summary>
    End,

    /// <summary>The reason is empty, longer than the column holds, or not one line.</summary>
    Reason,
}

/// <summary>
/// Legal holds (ADR-0009, ADR-0222): an entry kept past its retention, and past any delete, until an end at most
/// <see cref="MaxDays"/> days ahead, with a reason and the user who set it. A hold, and its release, is a unit of its own
/// with the entry's timeline event and an audit entry, neither of which holds the reason, which only the entry keeps. A
/// new hold replaces the one before.
/// </summary>
/// <param name="database">The units of work.</param>
/// <param name="entries">The entries.</param>
/// <param name="forms">The forms, which the audit entries name.</param>
/// <param name="events">The entries' timelines.</param>
/// <param name="audit">The audit log.</param>
/// <param name="time">The clock.</param>
internal sealed class EntryHolds(
    FormBuilderDatabase database,
    IEntryRepository entries,
    IFormRepository forms,
    IEntryEventRepository events,
    IAuditRepository audit,
    TimeProvider time)
{
    /// <summary>The furthest ahead one hold ends (ADR-0009); a longer one is set again before it ends.</summary>
    public const int MaxDays = 365;

    /// <summary>The kind of the timeline event of a hold.</summary>
    public const string HeldEvent = "held";

    /// <summary>The kind of the timeline event of a release.</summary>
    public const string ReleasedEvent = "released";

    /// <summary>The audit action of a hold; its details name the entry and the end.</summary>
    public const string HeldAction = "entry.held";

    /// <summary>The audit action of a release; its details name the entry.</summary>
    public const string ReleasedAction = "entry.released";

    /// <summary>Keeps an entry until <paramref name="until"/>, for a reason.</summary>
    /// <param name="entryKey">The entry.</param>
    /// <param name="until">The end: after now, and at most <see cref="MaxDays"/> days ahead.</param>
    /// <param name="reason">Why, one line of at most <see cref="EntryRepository.MaxHoldReasonLength"/> characters; trimmed.</param>
    /// <param name="userKey">The user who sets it.</param>
    /// <param name="cancellationToken">Cancels the unit of work.</param>
    public async Task<EntryHoldRefusal> HoldAsync(Guid entryKey, DateTimeOffset until, string reason, Guid userKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reason);
        var now = time.GetUtcNow();
        var said = reason.Trim();
        if (until <= now || until > now.AddDays(MaxDays))
        {
            return EntryHoldRefusal.End;
        }

        if (said.Length is 0 or > EntryRepository.MaxHoldReasonLength || said.Any(char.IsControl))
        {
            return EntryHoldRefusal.Reason;
        }

        var end = until.ToUniversalTime();
        return await ChangeAsync(entryKey, end, said, userKey, cancellationToken);
    }

    /// <summary>Ends an entry's hold now; nothing changes for an entry without one.</summary>
    /// <param name="entryKey">The entry.</param>
    /// <param name="userKey">The user who releases it.</param>
    /// <param name="cancellationToken">Cancels the unit of work.</param>
    public Task<EntryHoldRefusal> ReleaseAsync(Guid entryKey, Guid userKey, CancellationToken cancellationToken = default) =>
        ChangeAsync(entryKey, null, null, userKey, cancellationToken);

    private async Task<EntryHoldRefusal> ChangeAsync(Guid entryKey, DateTimeOffset? until, string? reason, Guid userKey, CancellationToken cancellationToken)
    {
        if (await entries.GetAsync(entryKey, cancellationToken) is not { } entry || await forms.GetByIdAsync(entry.FormId, cancellationToken) is not { } form)
        {
            return EntryHoldRefusal.NotFound;
        }

        if (!RetentionStates.Kept.Contains(entry.State))
        {
            return EntryHoldRefusal.NotKept;
        }

        if (until is null && entry.HoldUntilUtc is null)
        {
            return EntryHoldRefusal.None;
        }

        var details = until is { } end
            ? JsonSerializer.Serialize(new { until = end.ToString("O", CultureInfo.InvariantCulture) })
            : "{}";
        var found = await database.WriteAsync(
            async db =>
            {
                await RowHolds.HoldFormAsync(db, new SqlNames(db), form.Id);
                if (!await entries.SetHoldAsync(entry.Id, until, reason, cancellationToken))
                {
                    return false;
                }

                await events.AddAsync(new NewEntryEvent(entry.Id, until is null ? ReleasedEvent : HeldEvent, userKey, Details: details), cancellationToken);
                var audited = new AuditDetails().Add("entry", entry.Key);
                if (until is { } ends)
                {
                    audited = audited.Add("until", ends);
                }

                await audit.AddAsync(FormAudit.Entry(until is null ? ReleasedAction : HeldAction, form, userKey, audited), cancellationToken);
                return true;
            },
            cancellationToken);
        return found ? EntryHoldRefusal.None : EntryHoldRefusal.NotFound;
    }
}
