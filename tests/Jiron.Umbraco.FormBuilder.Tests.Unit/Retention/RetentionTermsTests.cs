using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Retention;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Microsoft.Extensions.Options;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Retention;

/// <summary>
/// The retention rules (ROADMAP 13.1, ADR-0009, ADR-0222): the defaults ADR-0009 sets, an entry's time by its state, a
/// test entry's days and a form's days of storage, the dry run's moments, and the host's rule on keeping entries for good.
/// </summary>
[TestFixture]
public sealed class RetentionTermsTests
{
    private static readonly DateTimeOffset Submitted = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Changed = Submitted.AddDays(3);
    private static readonly RetentionTerms Site = RetentionTerms.Of(new SiteRetention(), FormRetentionRules.None);

    [Test]
    public void TheDefaults_AreThoseOfAdr0009()
    {
        var site = new SiteRetention();

        Assert.Multiple(() =>
        {
            Assert.That((site.Submitted, site.Approved, site.Rejected, site.Spam), Is.EqualTo((365, 365, 90, 30)));
            Assert.That(RetentionTerms.TestDays, Is.EqualTo(7));
            Assert.That(SiteRetention.MaxDays, Is.EqualTo(3_650), "ten years; longer is kept for good, which needs the host (ADR-0226)");
            Assert.That(DefinitionSaveChecks.MaxDays, Is.EqualTo(SiteRetention.MaxDays), "as many as a form may set");
            Assert.That(EntryHolds.MaxDays, Is.EqualTo(365));
            Assert.That(AuditLogPurge.KeptFor, Is.EqualTo(TimeSpan.FromDays(365)));
            Assert.That(DailyStatPurge.KeptFor, Is.EqualTo(TimeSpan.FromDays(3 * 365)));
            Assert.That(FileStoreSweep.OldAfter, Is.EqualTo(TimeSpan.FromDays(1)));
            Assert.That(new PrivacySettings().Retention, Is.EqualTo(site));
            Assert.That(RetentionStates.Kept, Is.EqualTo(new[] { EntryState.Submitted, EntryState.Approved, EntryState.Rejected, EntryState.Spam }));
        });
    }

    [TestCase("Submitted", 365)]
    [TestCase("Approved", 365)]
    [TestCase("Rejected", 90)]
    [TestCase("Spam", 30)]
    public void AnEntry_GoesItsStatesDays_AfterItsLastChangeOfState(string state, int days) =>
        Assert.That(Site.PurgeAfter(Enum.Parse<EntryState>(state), isTest: false, Changed, Submitted), Is.EqualTo(Changed.AddDays(days)));

    [TestCase("Draft")]
    [TestCase("Unverified")]
    public void ADraftOrAnEntryWaitingForItsAddress_HasNoTime_AsItsLinkEndsIt(string state) =>
        Assert.That(Site.PurgeAfter(Enum.Parse<EntryState>(state), isTest: false, Changed, Submitted), Is.Null);

    [Test]
    public void ATestEntry_GoesAfterSevenDays_WhateverTheRules()
    {
        var forGood = new RetentionTerms(null, null, null, null, null);

        Assert.Multiple(() =>
        {
            Assert.That(Site.PurgeAfter(EntryState.Approved, isTest: true, Changed, Submitted), Is.EqualTo(Changed.AddDays(7)));
            Assert.That(forGood.PurgeAfter(EntryState.Spam, isTest: true, Changed, Submitted), Is.EqualTo(Changed.AddDays(7)));
        });
    }

    [Test]
    public void AFormsDaysOfStorage_CapEveryState_WhenShorter_AndLeaveItsDays_WhenLonger()
    {
        var shorter = RetentionTerms.Of(new SiteRetention(), new FormRetentionRules(null, null, null, null, 10));
        var longer = RetentionTerms.Of(new SiteRetention(), new FormRetentionRules(null, null, null, null, 400));

        Assert.Multiple(() =>
        {
            Assert.That(shorter.PurgeAfter(EntryState.Submitted, false, Changed, Submitted), Is.EqualTo(Submitted.AddDays(10)), "from the submission, not the change");
            Assert.That(shorter.PurgeAfter(EntryState.Spam, false, Changed, Submitted), Is.EqualTo(Submitted.AddDays(10)));
            Assert.That(shorter.PurgeAfter(EntryState.Approved, true, Changed, Submitted), Is.EqualTo(Changed.AddDays(7)), "a test entry's days come first");
            Assert.That(longer.PurgeAfter(EntryState.Submitted, false, Changed, Submitted), Is.EqualTo(Changed.AddDays(365)));
            Assert.That(longer.PurgeAfter(EntryState.Spam, false, Changed, Submitted), Is.EqualTo(Changed.AddDays(30)));
            Assert.That(new RetentionTerms(null, null, null, null, 400).PurgeAfter(EntryState.Approved, false, Changed, Submitted), Is.EqualTo(Submitted.AddDays(400)), "a state kept for good still ends with the form's days");
            Assert.That(new RetentionTerms(null, null, null, null, null).PurgeAfter(EntryState.Approved, false, Changed, Submitted), Is.Null, "kept for good");
        });
    }

    [Test]
    public void AFormsOwnDays_TakeTheSitesPlace_StateByState()
    {
        var definition = new FormDefinition
        {
            Key = Guid.NewGuid(),
            Settings = new FormSettings
            {
                Privacy = new FormPrivacySettings { RetentionDays = new RetentionDaysByState { Spam = 7, Rejected = 14 } },
                Submission = new FormSubmissionSettings { Store = EntryStorage.Days, StoreDays = 60 },
            },
        };
        var terms = RetentionTerms.Of(new SiteRetention { Submitted = 200 }, FormRetentionRules.Of(definition));

        Assert.Multiple(() =>
        {
            Assert.That(terms, Is.EqualTo(new RetentionTerms(200, 365, 14, 7, 60)));
            Assert.That(FormRetentionRules.Of(definition).IsOwn, Is.True);
            Assert.That(FormRetentionRules.Of(new FormDefinition { Key = Guid.NewGuid() }).IsOwn, Is.False);
            Assert.That(FormRetentionRules.Of(new FormDefinition { Key = Guid.NewGuid(), Settings = new FormSettings { Submission = new FormSubmissionSettings { Store = EntryStorage.Always, StoreDays = 60 } } }).StoreDays, Is.Null, "days of storage only with Store for a number of days");
        });
    }

    [Test]
    public void TheFingerprint_ChangesWithEveryValue()
    {
        var all = new[]
        {
            Site,
            Site with { Submitted = 364 },
            Site with { Approved = null },
            Site with { Rejected = 91 },
            Site with { Spam = 7 },
            Site with { StoreDays = 30 },
        };

        Assert.That(all.Select(terms => terms.Fingerprint).Distinct().Count(), Is.EqualTo(all.Length));
    }

    [Test]
    public void TheDryRunsMoments_FindTheEntriesWhoseTimeComesByTheHorizon()
    {
        var horizon = Submitted.AddDays(40);
        var terms = new RetentionTerms(365, null, 90, 30, 50);
        var cutoffs = terms.CutoffsBy(horizon);
        var cases = new List<(EntryState State, bool Test, DateTimeOffset Changed, DateTimeOffset Submitted)>();
        foreach (var state in RetentionStates.Kept)
        {
            foreach (var test in new[] { false, true })
            {
                foreach (var back in new[] { 0, 6, 7, 8, 29, 30, 31, 45, 60, 400 })
                {
                    cases.Add((state, test, horizon.AddDays(-back), horizon.AddDays(-back - 5)));
                }
            }
        }

        Assert.Multiple(() =>
        {
            foreach (var (state, test, changed, submitted) in cases)
            {
                var due = terms.PurgeAfter(state, test, changed, submitted) is { } at && at <= horizon;
                var reached = (test && changed <= cutoffs.Test)
                    || (!test && Cutoff(cutoffs, state) is { } before && changed <= before)
                    || (cutoffs.Submission is { } stored && submitted <= stored);
                Assert.That(reached, Is.EqualTo(due), $"{state}, test {test}, changed {changed:O}");
            }

            Assert.That(cutoffs.Approved, Is.Null, "approved entries are kept for good");
            Assert.That(cutoffs.Horizon, Is.EqualTo(horizon));
        });
    }

    [Test]
    public void KeepingEntriesForGood_NeedsTheHost_AndOtherwiseReadsAsTheDefaults()
    {
        var stored = new PrivacySettings { Retention = new SiteRetention { Submitted = SiteRetention.ForGood, Spam = SiteRetention.ForGood, Rejected = 10 } };
        var refused = new SettingsLocks();
        var allowing = new FormBuilderOptions();
        allowing.Privacy.AllowRetentionOff = true;
        var allowed = new SettingsLocks();

        var read = new RetentionOffRule(Options.Create(new FormBuilderOptions())).Apply(stored, refused);
        var kept = new RetentionOffRule(Options.Create(allowing)).Apply(stored, allowed);

        Assert.Multiple(() =>
        {
            Assert.That(read.Retention, Is.EqualTo(new SiteRetention { Rejected = 10 }), "for good reads as the state's default; a number stays");
            Assert.That(refused.All.Keys, Is.EquivalentTo(new[] { "retention.submitted", "retention.approved", "retention.rejected", "retention.spam" }));
            Assert.That(refused.All.Values.Distinct(), Is.EqualTo(new[] { new SettingsLock(SettingsLockKind.Restricted, "JironFormBuilder:Privacy:AllowRetentionOff") }));
            Assert.That(kept, Is.SameAs(stored));
            Assert.That(allowed.All, Is.Empty);
        });
    }

    [Test]
    public void TheValidator_HoldsEachStateToItsRange_AndTakesKeptForGood_AsZero()
    {
        var validator = new PrivacySettingsValidator();
        var errors = new SettingsErrors("privacy");

        validator.Validate(new PrivacySettings { Retention = new SiteRetention { Submitted = -1, Approved = SiteRetention.ForGood, Rejected = 3_651, Spam = 3_650 } }, errors);

        Assert.That(errors.Errors.Select(error => error.Field), Is.EquivalentTo(new[] { "privacy.retention.submitted", "privacy.retention.rejected" }));
    }

    [TestCase("0123456789abcdef0123456789abcdef", "01", "Stored")]
    [TestCase("0123456789abcdef0123456789abcdef.partial", "01", "Partial")]
    [TestCase("0123456789abcdef0123456789abcdef.deleted-0123456789abcdef", "01", "Deleted")]
    [TestCase("0123456789abcdef0123456789abcdef", "02", null)]
    [TestCase("0123456789abcdef0123456789abcdef.deleted-0123", "01", null)]
    [TestCase("0123456789ABCDEF0123456789abcdef", "01", null)]
    [TestCase("0123456789abcdef0123456789abcdef.txt", "01", null)]
    [TestCase("readme.md", "re", null)]
    public void TheStoresNames_AreKnown_AndAnyOtherIsLeftAlone(string name, string folder, string? kind) =>
        Assert.That(PhysicalFileStore.KindOf(name, folder)?.ToString(), Is.EqualTo(kind));

    private static DateTimeOffset? Cutoff(RetentionCutoffs cutoffs, EntryState state) => state switch
    {
        EntryState.Submitted => cutoffs.Submitted,
        EntryState.Approved => cutoffs.Approved,
        EntryState.Rejected => cutoffs.Rejected,
        _ => cutoffs.Spam,
    };
}
