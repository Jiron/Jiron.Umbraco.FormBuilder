using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;

namespace Jiron.Umbraco.FormBuilder.Core.Retention;

/// <summary>What the dry run counts of one form (ADR-0222).</summary>
/// <param name="Form">The form.</param>
/// <param name="Entries">The entries retention deletes within the span, those due now among them.</param>
/// <param name="Held">The entries whose time comes within the span that a legal hold keeps past it.</param>
/// <param name="Files">The uploaded files of the entries that go.</param>
internal sealed record RetentionDryRunForm(StoredForm Form, long Entries, long Held, long Files);

/// <summary>What the dry run counts (ADR-0222): the forms with entries to go or held, most to go first, and the totals.</summary>
/// <param name="Forms">The forms.</param>
/// <param name="Entries">The entries retention deletes within the span.</param>
/// <param name="Held">The entries a hold keeps past it.</param>
/// <param name="Files">The uploaded files of the entries that go.</param>
internal sealed record RetentionDryRunResult(IReadOnlyList<RetentionDryRunForm> Forms, long Entries, long Held, long Files);

/// <summary>A form that sets retention of its own, and what it sets (ADR-0222).</summary>
/// <param name="Form">The form.</param>
/// <param name="Rules">What its served version sets.</param>
internal sealed record OwnRetention(StoredForm Form, FormRetentionRules Rules);

/// <summary>
/// The retention dry run (ADR-0009, ADR-0222): what retention deletes within the next days, per form, by days per state
/// that may not be saved yet, counted from the rules directly rather than from the times stored, so it follows every
/// change at once and needs no walk of the entries. It deletes nothing. The forms in the trash count too, as retention
/// deletes their entries all the same.
/// </summary>
/// <param name="forms">The forms.</param>
/// <param name="entries">The counts.</param>
/// <param name="retention">The forms' own rules.</param>
/// <param name="time">The clock.</param>
internal sealed class RetentionDryRun(IFormRepository forms, IEntryRepository entries, EntryRetention retention, TimeProvider time)
{
    /// <summary>The spans the screen offers, in days (IA 9).</summary>
    public static readonly IReadOnlyList<int> Spans = [7, 30, 90];

    /// <summary>What retention deletes within <paramref name="days"/> by the site's days <paramref name="site"/>.</summary>
    /// <param name="site">The site's days per state, saved or not yet; each 1 to <see cref="SiteRetention.MaxDays"/>, or null for kept for good.</param>
    /// <param name="days">The span, one of <see cref="Spans"/>.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <exception cref="ArgumentOutOfRangeException">The span is none of <see cref="Spans"/>.</exception>
    public async Task<RetentionDryRunResult> CountAsync(SiteRetention site, int days, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(site);
        if (!Spans.Contains(days))
        {
            throw new ArgumentOutOfRangeException(nameof(days), days, "The span is 7, 30 or 90 days.");
        }

        var horizon = time.GetUtcNow().AddDays(days);
        var byTerms = new Dictionary<RetentionTerms, List<StoredForm>>();
        foreach (var form in await AllAsync(cancellationToken))
        {
            var terms = RetentionTerms.Of(site, await retention.RulesAsync(form, cancellationToken));
            if (!byTerms.TryGetValue(terms, out var group))
            {
                byTerms[terms] = group = [];
            }

            group.Add(form);
        }

        var counted = new List<RetentionDryRunForm>();
        foreach (var (terms, group) in byTerms)
        {
            var counts = await entries.CountDueAsync([.. group.Select(form => form.Id)], terms.CutoffsBy(horizon), cancellationToken);
            counted.AddRange(group.Where(form => counts.ContainsKey(form.Id)).Select(form => new RetentionDryRunForm(form, counts[form.Id].Entries, counts[form.Id].Held, counts[form.Id].Files)));
        }

        var ordered = counted
            .OrderByDescending(form => form.Entries)
            .ThenByDescending(form => form.Held)
            .ThenBy(form => form.Form.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return new RetentionDryRunResult(ordered, ordered.Sum(form => form.Entries), ordered.Sum(form => form.Held), ordered.Sum(form => form.Files));
    }

    /// <summary>The forms that set retention of their own, by name: those the Retention screen lists (IA 9).</summary>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public async Task<IReadOnlyList<OwnRetention>> OwnAsync(CancellationToken cancellationToken = default)
    {
        var own = new List<OwnRetention>();
        foreach (var form in await AllAsync(cancellationToken))
        {
            if (await retention.RulesAsync(form, cancellationToken) is { IsOwn: true } rules)
            {
                own.Add(new OwnRetention(form, rules));
            }
        }

        return [.. own.OrderBy(each => each.Form.Name, StringComparer.CurrentCultureIgnoreCase)];
    }

    private async Task<List<StoredForm>> AllAsync(CancellationToken cancellationToken)
    {
        var filter = new FormFilter { AllFolders = true, Trashed = null };
        var all = new List<StoredForm>();
        for (var skip = 0L; ;)
        {
            var page = await forms.ListAsync(filter, new PageRequest(skip, PageRequest.MaxTake), cancellationToken);
            all.AddRange(page.Items);
            skip += page.Items.Count;
            if (page.Items.Count == 0 || skip >= page.Total)
            {
                return all;
            }
        }
    }
}
