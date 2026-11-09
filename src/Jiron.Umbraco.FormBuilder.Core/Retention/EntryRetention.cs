using System.Collections.Concurrent;
using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;

namespace Jiron.Umbraco.FormBuilder.Core.Retention;

/// <summary>
/// What a form's definition sets of retention (ADR-0222): its own days per state, null where it takes the site's, and the
/// days it keeps any entry for, when it keeps them for a number of days.
/// </summary>
/// <param name="Submitted">The form's days of a submitted entry, or null.</param>
/// <param name="Approved">The form's days of an approved entry, or null.</param>
/// <param name="Rejected">The form's days of a rejected entry, or null.</param>
/// <param name="Spam">The form's days of an entry in Spam, or null.</param>
/// <param name="StoreDays">The days the form keeps any entry from its submission, or null.</param>
internal sealed record FormRetentionRules(int? Submitted, int? Approved, int? Rejected, int? Spam, int? StoreDays)
{
    /// <summary>A form that sets nothing: the site's days, and no days of storage.</summary>
    public static FormRetentionRules None { get; } = new(null, null, null, null, null);

    /// <summary>Whether the form sets anything of its own.</summary>
    public bool IsOwn => this != None;

    /// <summary>What a definition sets.</summary>
    /// <param name="definition">The definition.</param>
    public static FormRetentionRules Of(FormDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var own = definition.Settings.Privacy.RetentionDays;
        var submission = definition.Settings.Submission;
        return new FormRetentionRules(own.Submitted, own.Approved, own.Rejected, own.Spam, submission.Store == EntryStorage.Days ? submission.StoreDays : null);
    }
}

/// <summary>
/// How long a form's entries are kept (ADR-0009, ADR-0222): the days of each state the entries wait in, the form's own or
/// else the site's, null for kept for good; and, for a form that keeps its entries for a number of days
/// (<c>settings.submission.store: days</c>), that number, counted from the submission, which caps every state.
/// </summary>
/// <param name="Submitted">The days of a submitted entry, one that waits for approval among them.</param>
/// <param name="Approved">The days of an approved entry.</param>
/// <param name="Rejected">The days of a rejected entry.</param>
/// <param name="Spam">The days of an entry in Spam.</param>
/// <param name="StoreDays">The days the form keeps any entry from its submission, or null.</param>
internal sealed record RetentionTerms(int? Submitted, int? Approved, int? Rejected, int? Spam, int? StoreDays)
{
    /// <summary>The days a test entry, one sent from the preview, stays after its last change of state, whatever the rules (ADR-0009).</summary>
    public const int TestDays = 7;

    /// <summary>The terms of a form: its own days where it has them, the site's for the others, and its days of storage.</summary>
    /// <param name="site">The site's days per state.</param>
    /// <param name="own">What the form's served version (<see cref="FormDocuments.ServedOfAsync"/>) sets.</param>
    public static RetentionTerms Of(SiteRetention site, FormRetentionRules own)
    {
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(own);
        return new RetentionTerms(
            own.Submitted ?? DaysOf(site.Submitted),
            own.Approved ?? DaysOf(site.Approved),
            own.Rejected ?? DaysOf(site.Rejected),
            own.Spam ?? DaysOf(site.Spam),
            own.StoreDays);
    }

    // The site's days of a state, null for kept for good.
    private static int? DaysOf(int days) => days == SiteRetention.ForGood ? null : days;

    /// <summary>
    /// What the terms are written as, to tell when a form's entries were last given their times by other terms
    /// (<see cref="RetentionRecompute"/>).
    /// </summary>
    public string Fingerprint => string.Create(CultureInfo.InvariantCulture, $"v1:{Submitted}:{Approved}:{Rejected}:{Spam}:{StoreDays}");

    /// <summary>The days of a state; null for kept for good, and for a state retention leaves to its own time.</summary>
    public int? DaysIn(EntryState state) => state switch
    {
        EntryState.Submitted => Submitted,
        EntryState.Approved => Approved,
        EntryState.Rejected => Rejected,
        EntryState.Spam => Spam,
        _ => null,
    };

    /// <summary>
    /// When retention deletes an entry: the days of its state after its last change of state (a test entry's
    /// <see cref="TestDays"/>), or the form's days of storage after its submission, whichever comes first; null when
    /// neither applies, for an entry kept for good, a draft and one that waits for its address.
    /// </summary>
    /// <param name="state">The entry's state.</param>
    /// <param name="isTest">Whether it came from the preview.</param>
    /// <param name="stateChangedUtc">When its state last changed.</param>
    /// <param name="submittedUtc">When it was submitted.</param>
    public DateTimeOffset? PurgeAfter(EntryState state, bool isTest, DateTimeOffset stateChangedUtc, DateTimeOffset submittedUtc)
    {
        if (!RetentionStates.Kept.Contains(state))
        {
            return null;
        }

        var byState = (isTest ? TestDays : DaysIn(state)) is { } days ? stateChangedUtc.AddDays(days) : (DateTimeOffset?)null;
        var byStore = StoreDays is { } stored ? submittedUtc.AddDays(stored) : (DateTimeOffset?)null;
        return (byState, byStore) switch
        {
            ({ } a, { } b) => a <= b ? a : b,
            ({ } a, null) => a,
            (null, { } b) => b,
            _ => null,
        };
    }

    /// <summary>
    /// The moments a dry run counts with to find the entries these terms delete by <paramref name="horizon"/>: the same
    /// times as <see cref="PurgeAfter"/>, written as the latest change of state or submission that reaches them.
    /// </summary>
    /// <param name="horizon">The moment counted up to.</param>
    public RetentionCutoffs CutoffsBy(DateTimeOffset horizon) => new(
        Submitted is { } submitted ? horizon.AddDays(-submitted) : null,
        Approved is { } approved ? horizon.AddDays(-approved) : null,
        Rejected is { } rejected ? horizon.AddDays(-rejected) : null,
        Spam is { } spam ? horizon.AddDays(-spam) : null,
        horizon.AddDays(-TestDays),
        StoreDays is { } stored ? horizon.AddDays(-stored) : null,
        horizon);
}

/// <summary>
/// The retention rules as they hold now (ADR-0222): the terms of a form, read from the site's privacy settings and the
/// form's served version, and the time an entry gets from them, which every new entry and every change of state stores.
/// </summary>
/// <remarks>
/// A version never changes once stored, so what each one sets of retention is kept by its id: a bulk move of hundreds of
/// entries reads the form's definition once. The cache holds no more than <see cref="MostVersionsKept"/> versions.
/// </remarks>
/// <param name="settings">The site's privacy settings.</param>
/// <param name="forms">The forms.</param>
/// <param name="documents">The forms' served versions.</param>
/// <param name="entries">The entries.</param>
internal sealed class EntryRetention(
    IFormBuilderSettingsService settings,
    IFormRepository forms,
    FormDocuments documents,
    IEntryRepository entries)
{
    /// <summary>The most versions whose rules the cache keeps; it starts again once it holds more.</summary>
    public const int MostVersionsKept = 2_000;

    private readonly ConcurrentDictionary<int, FormRetentionRules> _byVersion = new();

    /// <summary>The site's days per state, with the host's rules applied.</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task<SiteRetention> SiteAsync(CancellationToken cancellationToken = default) =>
        (await settings.GetAsync<PrivacySettings>(cancellationToken: cancellationToken)).Value.Retention ?? new SiteRetention();

    /// <summary>A form's terms; the site's alone for a form that is gone or whose served version cannot be read.</summary>
    /// <param name="formId">The form.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public async Task<RetentionTerms> TermsAsync(int formId, CancellationToken cancellationToken = default)
    {
        var site = await SiteAsync(cancellationToken);
        return await forms.GetByIdAsync(formId, cancellationToken) is { } form
            ? RetentionTerms.Of(site, await RulesAsync(form, cancellationToken))
            : RetentionTerms.Of(site, FormRetentionRules.None);
    }

    /// <summary>A form's terms by the site's days as saved.</summary>
    /// <param name="form">The form.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public async Task<RetentionTerms> TermsAsync(StoredForm form, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(form);
        return RetentionTerms.Of(await SiteAsync(cancellationToken), await RulesAsync(form, cancellationToken));
    }

    /// <summary>A form's terms with the site's days given, saved or not yet.</summary>
    /// <param name="form">The form.</param>
    /// <param name="site">The site's days.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    public async Task<RetentionTerms> TermsAsync(StoredForm form, SiteRetention site, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(form);
        ArgumentNullException.ThrowIfNull(site);
        return RetentionTerms.Of(site, await RulesAsync(form, cancellationToken));
    }

    /// <summary>What a form's served version sets of retention; nothing when it has none or it cannot be read.</summary>
    /// <param name="form">The form.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    public async Task<FormRetentionRules> RulesAsync(StoredForm form, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(form);
        if (form.PublishedVersionId is { } published && _byVersion.TryGetValue(published, out var known))
        {
            return known;
        }

        if (await documents.ServedOfAsync(form, cancellationToken) is not { } served)
        {
            return FormRetentionRules.None;
        }

        if (_byVersion.TryGetValue(served.Info.Id, out var kept))
        {
            return kept;
        }

        FormRetentionRules rules;
        try
        {
            rules = FormRetentionRules.Of(documents.Readable(served));
        }
        catch (FormBuilderValidationException)
        {
            rules = FormRetentionRules.None;
        }

        if (_byVersion.Count >= MostVersionsKept)
        {
            _byVersion.Clear();
        }

        _byVersion[served.Info.Id] = rules;
        return rules;
    }

    /// <summary>
    /// Stores the time retention deletes an entry, after its state changed, in the caller's unit when there is one; the
    /// entry as it is then. The terms are read before the unit begins (ADR-0007): a read of the rules inside a write could
    /// wait on a load of the settings that waits on that write.
    /// </summary>
    /// <param name="entry">The entry as the change left it.</param>
    /// <param name="terms">The terms of the entry's form, read before the unit.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    public async Task<StoredEntry> StampAsync(StoredEntry entry, RetentionTerms terms, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(terms);
        var after = terms.PurgeAfter(entry.State, entry.IsTest, entry.StateChangedUtc, entry.SubmittedUtc);
        if (after == entry.PurgeAfterUtc)
        {
            return entry;
        }

        await entries.SetPurgeAfterAsync([new EntryPurgeTime(entry.Id, after)], cancellationToken);
        return entry with { PurgeAfterUtc = after };
    }
}
