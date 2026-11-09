using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Microsoft.Extensions.Logging;

namespace Jiron.Umbraco.FormBuilder.Core.Retention;

/// <summary>
/// The part of the retention job that deletes entries whose time has come (ADR-0009, ADR-0222), in batches, soonest first,
/// each form's in units of its own through <see cref="EntryDeletions"/>, which audits them, drops their files and mail, and
/// tells the site of each. Before it deletes, it gives each entry its time again by the rules as they hold now: one whose
/// rules got longer since gets its later time and stays. A legal hold keeps an entry until it ends.
/// </summary>
/// <param name="entries">The entries.</param>
/// <param name="forms">The forms.</param>
/// <param name="retention">The rules.</param>
/// <param name="deletions">The deletes.</param>
/// <param name="time">The clock.</param>
/// <param name="logger">Where the counts go, never anything of an entry.</param>
internal sealed partial class EntryPurge(
    IEntryRepository entries,
    IFormRepository forms,
    EntryRetention retention,
    EntryDeletions deletions,
    TimeProvider time,
    ILogger<EntryPurge> logger) : IFormBuilderRetentionPart
{
    /// <summary>How many due entries one read nominates.</summary>
    public const int Batch = 200;

    /// <inheritdoc />
    public string Name => "entries whose retention time has come";

    /// <inheritdoc />
    public async Task RunAsync(DateTimeOffset deadline, CancellationToken cancellationToken)
    {
        var site = await retention.SiteAsync(cancellationToken);
        var terms = new Dictionary<int, RetentionTerms>();
        var deleted = 0;
        while (time.GetUtcNow() < deadline)
        {
            var now = time.GetUtcNow();
            var due = await entries.ListDueAsync(now, Batch, cancellationToken);
            if (due.Count == 0)
            {
                break;
            }

            var progress = false;
            foreach (var group in due.GroupBy(row => row.FormId))
            {
                if (await forms.GetByIdAsync(group.Key, cancellationToken) is not { } form)
                {
                    continue;
                }

                if (!terms.TryGetValue(form.Id, out var rules))
                {
                    terms[form.Id] = rules = await retention.TermsAsync(form, site, cancellationToken);
                }

                var later = new List<EntryPurgeTime>();
                var gone = new List<long>();
                foreach (var row in group)
                {
                    var after = rules.PurgeAfter(row.State, row.IsTest, row.StateChangedUtc, row.SubmittedUtc);
                    if (after is { } at && at <= now)
                    {
                        gone.Add(row.Id);
                    }
                    else
                    {
                        later.Add(new EntryPurgeTime(row.Id, after));
                    }
                }

                if (later.Count > 0)
                {
                    progress |= await entries.SetPurgeAfterAsync(later, cancellationToken) > 0;
                }

                foreach (var chunk in gone.Chunk(EntryDeletions.MaxEntries))
                {
                    var done = await deletions.PurgeAsync(form, chunk, now, cancellationToken);
                    deleted += done.Entries;
                    progress |= done.Entries > 0;
                }

                if (time.GetUtcNow() >= deadline)
                {
                    break;
                }
            }

            // Nothing that would go nor a time that moved: what is left waits for the next run.
            if (!progress)
            {
                break;
            }
        }

        if (deleted > 0)
        {
            LogPurged(logger, deleted);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Jiron Form Builder's retention deleted {Count} entries whose time had come")]
    private static partial void LogPurged(ILogger logger, int count);
}

/// <summary>
/// The part of the retention job that gives a form's entries their times again once its rules changed (ADR-0222): a save of
/// the site's days, a publish that changes a form's own, or entries stored before retention came. Each run compares every
/// form's terms with those its entries were given their times by, kept in <c>jfbKeyValue</c>, and walks the entries of a
/// form whose terms differ in batches by id, from where the last run stopped, until it has seen them all.
/// </summary>
/// <param name="forms">The forms, those in the trash too.</param>
/// <param name="entries">The entries.</param>
/// <param name="keyValues">Where each form's terms and how far its walk got are kept.</param>
/// <param name="retention">The rules.</param>
/// <param name="time">The clock.</param>
internal sealed class RetentionRecompute(
    IFormRepository forms,
    IEntryRepository entries,
    IKeyValueRepository keyValues,
    EntryRetention retention,
    TimeProvider time) : IFormBuilderRetentionPart
{
    /// <summary>The prefix of the keys that hold each form's terms, the form's key after it.</summary>
    public const string KeyPrefix = "jfb.retention.terms.";

    /// <summary>How many entries one batch reads.</summary>
    public const int Batch = 500;

    // What separates the terms from how far the walk got, while it is under way.
    private const char Walking = '|';

    /// <inheritdoc />
    public string Name => "the retention times of entries whose rules changed";

    /// <summary>The key of a form's terms.</summary>
    public static string KeyOf(Guid formKey) => KeyPrefix + formKey.ToString("N");

    /// <inheritdoc />
    /// <remarks>A run that saw every form also forgets the terms of the forms deleted for good since.</remarks>
    public async Task RunAsync(DateTimeOffset deadline, CancellationToken cancellationToken)
    {
        var site = await retention.SiteAsync(cancellationToken);
        var filter = new FormFilter { AllFolders = true, Trashed = null };
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var skip = 0L; ;)
        {
            var page = await forms.ListAsync(filter, new PageRequest(skip, PageRequest.MaxTake), cancellationToken);
            foreach (var form in page.Items)
            {
                if (time.GetUtcNow() >= deadline)
                {
                    return;
                }

                seen.Add(KeyOf(form.Key));
                var terms = await retention.TermsAsync(form, site, cancellationToken);
                if (!await WalkAsync(form, terms, deadline, cancellationToken))
                {
                    return;
                }
            }

            skip += page.Items.Count;
            if (page.Items.Count == 0 || skip >= page.Total)
            {
                break;
            }
        }

        foreach (var (key, _) in await keyValues.ListAsync(KeyPrefix, cancellationToken))
        {
            if (!seen.Contains(key))
            {
                await keyValues.DeleteAsync(key, cancellationToken);
            }
        }
    }

    // Gives the form's entries their times by its terms, unless they have them; false once the deadline stopped the walk.
    private async Task<bool> WalkAsync(StoredForm form, RetentionTerms terms, DateTimeOffset deadline, CancellationToken cancellationToken)
    {
        var key = KeyOf(form.Key);
        var fingerprint = terms.Fingerprint;
        var stored = await keyValues.GetAsync(key, cancellationToken);
        if (stored == fingerprint)
        {
            return true;
        }

        // A walk under way by the same terms goes on where it stopped; one by other terms starts again.
        var afterId = 0L;
        if (stored is not null && stored.Split(Walking) is [var walked, var at] && walked == fingerprint && long.TryParse(at, out var resumed))
        {
            afterId = resumed;
        }

        while (true)
        {
            var rows = await entries.ListForRetentionAsync(form.Id, afterId, Batch, cancellationToken);
            var changed = rows
                .Select(row => (Row: row, Time: new EntryPurgeTime(row.Id, terms.PurgeAfter(row.State, row.IsTest, row.StateChangedUtc, row.SubmittedUtc))))
                .Where(each => each.Row.PurgeAfterUtc != each.Time.PurgeAfterUtc)
                .Select(each => each.Time)
                .ToList();
            if (changed.Count > 0)
            {
                await entries.SetPurgeAfterAsync(changed, cancellationToken);
            }

            if (rows.Count < Batch)
            {
                await SetAsync(key, stored, fingerprint, cancellationToken);
                return true;
            }

            afterId = rows[^1].Id;
            var progress = $"{fingerprint}{Walking}{afterId}";
            await SetAsync(key, stored, progress, cancellationToken);
            stored = progress;
            if (time.GetUtcNow() >= deadline)
            {
                return false;
            }
        }
    }

    // Replaces what the walk read; one server runs the job at a time, so a value written meanwhile only comes from a run
    // that went on elsewhere, which then stands, and the next run looks again.
    private async Task SetAsync(string key, string? stored, string value, CancellationToken cancellationToken)
    {
        if (stored is not null && await keyValues.TryReplaceAsync(key, stored, value, cancellationToken))
        {
            return;
        }

        await keyValues.TryAddAsync(key, value, cancellationToken);
    }
}

/// <summary>The part of the retention job that deletes audit entries after a year (ADR-0009), a thousand at a time.</summary>
/// <param name="audit">The audit log.</param>
/// <param name="time">The clock.</param>
internal sealed class AuditLogPurge(IAuditRepository audit, TimeProvider time) : IFormBuilderRetentionPart
{
    /// <summary>How long an audit entry stays (ADR-0009).</summary>
    public static readonly TimeSpan KeptFor = TimeSpan.FromDays(365);

    /// <summary>How many one statement deletes.</summary>
    public const int Batch = 1000;

    /// <inheritdoc />
    public string Name => "audit entries older than a year";

    /// <inheritdoc />
    public async Task RunAsync(DateTimeOffset deadline, CancellationToken cancellationToken)
    {
        while (time.GetUtcNow() < deadline && await audit.PurgeAsync(KeptFor, Batch, cancellationToken) > 0)
        {
        }
    }
}

/// <summary>The part of the retention job that deletes daily statistics after three years (ADR-0009), a form's day at a time.</summary>
/// <param name="stats">The daily statistics.</param>
/// <param name="time">The clock.</param>
internal sealed class DailyStatPurge(IDailyStatRepository stats, TimeProvider time) : IFormBuilderRetentionPart
{
    /// <summary>How long a day's statistics stay (ADR-0009): three years.</summary>
    public static readonly TimeSpan KeptFor = TimeSpan.FromDays(3 * 365);

    /// <inheritdoc />
    public string Name => "daily statistics older than three years";

    /// <inheritdoc />
    public async Task RunAsync(DateTimeOffset deadline, CancellationToken cancellationToken)
    {
        while (time.GetUtcNow() < deadline && await stats.PurgeAsync(KeptFor, cancellationToken) > 0)
        {
        }
    }
}
