using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Testing.Settings;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Core.Sync;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Settings;

/// <summary>
/// The backoffice settings in a real site (ADR-0008, ADR-0043): effective values from defaults, the global row, a
/// site's row and the host's rules; saves that refuse locked and invalid values and stale versions; migrated old rows;
/// the cache with its loading token and refresher; the audit entry and <see cref="SettingsSavedNotification"/>.
/// Umbraco's test host runs without caches, so this fixture gives it a runtime cache, as a site has.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class SettingsServiceTests : RepositoryTestBase
{
    [Test]
    public async Task TheSitesThatSetASection_AreListed_AndASiteThatStopsLeavesTheList()
    {
        var none = await Service.SitesOfAsync<GeneralSettings>();
        var saved = await Service.SaveSiteAsync(SiteA, new GeneralSettings { TimeZone = "America/New_York" }, ["timeZone"], 0, null);
        var one = await Service.SitesOfAsync<GeneralSettings>();
        await Service.SaveSiteAsync(SiteA, new GeneralSettings(), [], saved.Version, null);
        var gone = await Service.SitesOfAsync<GeneralSettings>();

        Assert.Multiple(() =>
        {
            Assert.That(none, Is.Empty);
            Assert.That(one, Is.EqualTo(new[] { SiteA }));
            Assert.That(gone, Is.Empty);
        });
    }

    private static readonly Guid SiteA = Guid.Parse("4f6a2c1e-8d3b-4e5f-a7c9-1b2d3e4f5a01");
    private static readonly Guid SiteB = Guid.Parse("4f6a2c1e-8d3b-4e5f-a7c9-1b2d3e4f5a02");

    private IFormBuilderSettingsService Service => GetRequiredService<IFormBuilderSettingsService>();

    private SampleHostRule Rule => GetRequiredService<SampleHostRule>();

    private SavedNotifications Saved => GetRequiredService<SavedNotifications>();

    private HookedSettingsRepository Rows => GetRequiredService<HookedSettingsRepository>();

    private IAppPolicyCache Cache => GetRequiredService<AppCaches>().RuntimeCache;

    private RecordingServerMessenger Messenger => GetRequiredService<RecordingServerMessenger>();

    [Test]
    public async Task AFreshSite_HasTheDefaults_AtTheVersionOfTheMigrationsRow()
    {
        var general = await Service.GetAsync<GeneralSettings>();

        Assert.Multiple(() =>
        {
            Assert.That(general.Value, Is.EqualTo(new GeneralSettings()));
            Assert.That(general.Version, Is.EqualTo(1));
            Assert.That(general.Overrides, Is.Empty);
            Assert.That(general.Locks, Is.Empty);
        });
    }

    [Test]
    public async Task AGlobalSave_StoresTheSection_AndEveryReadAfterItHasIt()
    {
        var saved = await Service.SaveAsync(new GeneralSettings { TimeZone = "Europe/Berlin" }, 1, Editor);
        Cache.Clear();
        var read = await Service.GetAsync<GeneralSettings>();
        var row = await Settings.GetAsync(SettingsScopes.Global, GeneralSettings.Alias);

        Assert.Multiple(() =>
        {
            Assert.That(saved.Value.TimeZone, Is.EqualTo("Europe/Berlin"));
            Assert.That(saved.Version, Is.EqualTo(2));
            Assert.That(read, Is.EqualTo(saved).Using<SettingsSnapshot<GeneralSettings>>(SameSnapshot));
            Assert.That(row, Is.EqualTo(new StoredSettings(SettingsScopes.Global, "general", 1, 2, """{"requiredMarker":"optional","timeZone":"Europe/Berlin","newForms":{"notifyCreator":false},"history":{"draftDays":90,"draftCount":50}}""", Now, Editor)));
        });
    }

    [TestCase("Mars/Olympus_Mons")]
    [TestCase("W. Europe Standard Time")]
    [TestCase("+01:00")]
    public async Task AnUnknownTimeZone_CannotBeSaved_AndNothingChanges(string timeZone)
    {
        var refusal = Assert.ThrowsAsync<FormBuilderSettingsValidationException>(() => Service.SaveAsync(new GeneralSettings { TimeZone = timeZone }, 1, Editor));

        Assert.Multiple(async () =>
        {
            Assert.That(refusal!.Errors, Is.EqualTo(new[] { new SettingsError("general.timeZone", "timeZone", "Must be an IANA time zone this server knows, in its exact spelling, such as Europe/Berlin.") }));
            Assert.That((await Settings.GetAsync(SettingsScopes.Global, GeneralSettings.Alias))!.Version, Is.EqualTo(1));
            Assert.That(Count("jfbAuditEntry"), Is.Zero);
            Assert.That(Saved.Seen, Is.Empty);
        });

        Assert.That(() => Service.SaveSiteAsync(SiteA, new GeneralSettings { TimeZone = timeZone }, ["timeZone"], 0, Editor), Throws.InstanceOf<FormBuilderSettingsValidationException>(), "nor for a site");
    }

    [Test]
    public async Task ATimeZoneInAnotherLetterCase_CannotBeSaved_EvenOnceTheServerHasMetItsSpelling()
    {
        // After this save, the runtime's time zone cache, which ignores case, would find any spelling of the zone.
        var saved = await Service.SaveAsync(new GeneralSettings { TimeZone = "Europe/Berlin" }, 1, Editor);

        Assert.Multiple(async () =>
        {
            Assert.That(() => Service.SaveAsync(saved.Value with { TimeZone = "europe/berlin" }, saved.Version, Editor), Throws.InstanceOf<FormBuilderSettingsValidationException>());
            Assert.That((await Settings.GetAsync(SettingsScopes.Global, GeneralSettings.Alias))!.Json, Is.EqualTo("""{"requiredMarker":"optional","timeZone":"Europe/Berlin","newForms":{"notifyCreator":false},"history":{"draftDays":90,"draftCount":50}}"""));
        });
    }

    [Test]
    public async Task AStaleSave_IsRefused_ChangesNothing_AndTheNextReadHasTheCurrentValues()
    {
        _ = await Service.GetAsync<GeneralSettings>();
        await Settings.SaveAsync(new SettingsSave(SettingsScopes.Global, "general", 1, 1, """{"timeZone":"Asia/Tokyo"}""", null));

        Assert.That(() => Service.SaveAsync(new GeneralSettings { TimeZone = "Europe/Berlin" }, 1, Editor), Throws.InstanceOf<FormBuilderConcurrencyException>());
        Assert.Multiple(async () =>
        {
            Assert.That((await Service.GetAsync<GeneralSettings>()).Value.TimeZone, Is.EqualTo("Asia/Tokyo"), "this server forgot its stale copy");
            Assert.That(Count("jfbAuditEntry"), Is.Zero);
            Assert.That(Saved.Seen, Is.Empty);
        });
    }

    [Test]
    public async Task AnUpdate_ChangesTheStoredValues_NotThisServersOlderCopy_AndIsRefusedWhenStale()
    {
        _ = await Service.GetAsync<GeneralSettings>();
        await Settings.SaveAsync(new SettingsSave(SettingsScopes.Global, "general", 1, 1, """{"timeZone":"Asia/Tokyo"}""", null));

        // Saved by another server at version 2, which an editor read there; this server still holds version 1.
        var saved = await Service.UpdateAsync<GeneralSettings>(current => current with { History = new HistorySettings { DraftDays = 5, DraftCount = 7 } }, 2, Editor);

        Assert.Multiple(() =>
        {
            Assert.That(saved.Value.TimeZone, Is.EqualTo("Asia/Tokyo"), "the stored value stays, not the cached one");
            Assert.That(saved.Value.History?.DraftDays, Is.EqualTo(5));
            Assert.That(saved.Value.History?.DraftCount, Is.EqualTo(7));
            Assert.That(saved.Version, Is.EqualTo(3));
            Assert.That(
                () => Service.UpdateAsync<GeneralSettings>(current => current with { TimeZone = "Europe/Berlin" }, 2, Editor),
                Throws.InstanceOf<FormBuilderConcurrencyException>(),
                "an update of a version saved over since is refused");
        });
    }

    [Test]
    public async Task ASite_OverridesTheFieldsItNames_AndOtherSitesFollowTheGlobalValues()
    {
        await Service.SaveAsync(new GeneralSettings { TimeZone = "Europe/Berlin" }, 1, Editor);
        var before = await Service.GetAsync<GeneralSettings>(SiteA);

        var saved = await Service.SaveSiteAsync(SiteA, new GeneralSettings { TimeZone = "America/New_York" }, ["timeZone"], before.Version, Editor);

        Assert.Multiple(async () =>
        {
            Assert.That((before.Value.TimeZone, before.Version, before.Overrides), Is.EqualTo(("Europe/Berlin", 0, new HashSet<string>())).Using<(string, int, IReadOnlySet<string>)>(SameTuple));
            Assert.That((saved.Value.TimeZone, saved.Version), Is.EqualTo(("America/New_York", 1)));
            Assert.That(saved.Overrides, Is.EquivalentTo(new[] { "timeZone" }));
            Assert.That((await Service.GetAsync<GeneralSettings>(SiteA)).Value.TimeZone, Is.EqualTo("America/New_York"));
            Assert.That((await Service.GetAsync<GeneralSettings>(SiteB)).Value.TimeZone, Is.EqualTo("Europe/Berlin"));
            Assert.That((await Service.GetAsync<GeneralSettings>()).Value.TimeZone, Is.EqualTo("Europe/Berlin"));
            Assert.That((await Settings.GetAsync(SettingsScopes.ForSite(SiteA), "general"))!.Json, Is.EqualTo("""{"timeZone":"America/New_York"}"""));
        });
    }

    [Test]
    public async Task AGlobalSave_ReachesEverySiteThatFollowsIt_EvenOneThisServerHasCached()
    {
        await Service.SaveSiteAsync(SiteA, new GeneralSettings { TimeZone = "America/New_York" }, ["timeZone"], 0, Editor);
        _ = await Service.GetAsync<GeneralSettings>(SiteA);
        _ = await Service.GetAsync<GeneralSettings>(SiteB);

        await Service.SaveAsync(new GeneralSettings { TimeZone = "Asia/Tokyo" }, 1, Editor);

        Assert.Multiple(async () =>
        {
            Assert.That((await Service.GetAsync<GeneralSettings>(SiteA)).Value.TimeZone, Is.EqualTo("America/New_York"), "the site's own value");
            Assert.That((await Service.GetAsync<GeneralSettings>(SiteB)).Value.TimeZone, Is.EqualTo("Asia/Tokyo"), "the new global value");
        });
    }

    [Test]
    public async Task ASiteWithoutFields_FollowsTheGlobalValuesAgain_AndItsRowGoes()
    {
        var site = await Service.SaveSiteAsync(SiteA, new GeneralSettings { TimeZone = "America/New_York" }, ["timeZone"], 0, Editor);

        var back = await Service.SaveSiteAsync(SiteA, new GeneralSettings(), [], site.Version, Editor);

        Assert.Multiple(async () =>
        {
            Assert.That((back.Value.TimeZone, back.Version, back.Overrides.Count), Is.EqualTo(("UTC", 0, 0)));
            Assert.That(await Settings.GetAsync(SettingsScopes.ForSite(SiteA), "general"), Is.Null);
            Assert.That((await Service.GetAsync<GeneralSettings>(SiteA)).Version, Is.Zero);
            Assert.That(Saved.Seen.Select(n => (n.SiteKey, n.Version, string.Join(",", n.ChangedFields))), Is.EqualTo(new[] { ((Guid?)SiteA, 1, "timeZone"), (SiteA, 0, "timeZone") }));
            Assert.That(
                (await Audit.QueryAsync(new AuditQuery { Action = "settings.saved" }, PageRequest.First(10))).Items.Select(e => (e.EntityKey, e.Details)),
                Is.EquivalentTo(new[]
                {
                    ((Guid?)SiteA, """{"section":"general","version":1,"fields":["timeZone"],"fieldCount":1}"""),
                    (SiteA, """{"section":"general","version":0,"fields":["timeZone"],"fieldCount":1}"""),
                }),
                "the removal is audited too");
        });
    }

    [Test]
    public async Task ASiteWithoutARow_AndWithoutFields_ChangesNothing()
    {
        var snapshot = await Service.SaveSiteAsync(SiteA, new GeneralSettings(), [], 0, Editor);

        Assert.Multiple(() =>
        {
            Assert.That((snapshot.Value.TimeZone, snapshot.Version), Is.EqualTo(("UTC", 0)));
            Assert.That(Count("jfbAuditEntry"), Is.Zero);
            Assert.That(Saved.Seen, Is.Empty);
        });
    }

    [Test]
    public async Task ASitesStaleSave_IsRefused_ForARowAddedOrRemovedMeanwhile()
    {
        await Service.SaveSiteAsync(SiteA, new GeneralSettings { TimeZone = "America/New_York" }, ["timeZone"], 0, Editor);

        Assert.Multiple(() =>
        {
            Assert.That(() => Service.SaveSiteAsync(SiteA, new GeneralSettings { TimeZone = "Asia/Tokyo" }, ["timeZone"], 0, Editor), Throws.InstanceOf<FormBuilderConcurrencyException>(), "another save added the row");
            Assert.That(() => Service.SaveSiteAsync(SiteA, new GeneralSettings(), [], 0, Editor), Throws.InstanceOf<FormBuilderConcurrencyException>(), "inheriting again, unaware of the row");
            Assert.That(() => Service.SaveSiteAsync(SiteB, new GeneralSettings(), [], 1, Editor), Throws.InstanceOf<FormBuilderConcurrencyException>(), "removing a row that is gone");
        });
    }

    [Test]
    public async Task ASiteSave_RefusesAFieldEverySiteShares_AndOneTheSectionHasNot()
    {
        var refusal = Assert.ThrowsAsync<FormBuilderSettingsValidationException>(() => Service.SaveSiteAsync(SiteA, new SampleSettings(), ["keep", "count", "retired"], 0, Editor));

        Assert.Multiple(async () =>
        {
            Assert.That(refusal!.Errors.Select(e => (e.Field, e.Code)), Is.EqualTo(new[] { ("sample.keep", "scope"), ("sample.retired", "scope") }));
            Assert.That(await Settings.GetAsync(SettingsScopes.ForSite(SiteA), SampleSettings.Alias), Is.Null);
        });
    }

    [Test]
    public async Task ASiteSave_ChecksTheCombinedValues_ButOnlyTheFieldsItWrites()
    {
        await Settings.SaveAsync(new SettingsSave(SettingsScopes.Global, SampleSettings.Alias, 3, 0, """{"count":400}""", null));

        var saved = await Service.SaveSiteAsync(SiteA, new SampleSettings { Name = " Site " }, ["name"], 0, Editor);
        var refusal = Assert.ThrowsAsync<FormBuilderSettingsValidationException>(() => Service.SaveSiteAsync(SiteA, new SampleSettings { Name = "Site", Count = 101 }, ["name", "count"], saved.Version, Editor));

        Assert.Multiple(() =>
        {
            Assert.That((saved.Value.Name, saved.Value.Count), Is.EqualTo(("Site", 400)), "the global count is not the site's to fix, and the name is trimmed");
            Assert.That(refusal!.Errors.Select(e => e.Field), Is.EqualTo(new[] { "sample.count" }));
        });
    }

    [Test]
    public async Task ASectionEverySiteShares_GivesItsGlobalValuesToEverySite_AndTakesNoSiteSave()
    {
        await Settings.SaveAsync(new SettingsSave(SettingsScopes.Global, SampleSharedSettings.Alias, 1, 0, """{"enabled":false}""", null));

        var forSite = await Service.GetAsync<SampleSharedSettings>(SiteA);

        Assert.Multiple(() =>
        {
            Assert.That((forSite.Value.Enabled, forSite.Version), Is.EqualTo((false, 1)), "the global values, at the global row's version");
            Assert.That(() => Service.SaveSiteAsync(SiteA, new SampleSharedSettings(), [], 0, Editor), Throws.ArgumentException);
        });
    }

    [Test]
    public void AValue_IsCheckedAsItsValidatorNormalizedIt()
    {
        var refusal = Assert.ThrowsAsync<FormBuilderSettingsValidationException>(() => Service.SaveAsync(new SampleSettings { Name = "   " }, 0, Editor));

        Assert.That(refusal!.Errors.Select(e => (e.Field, e.Code)), Is.EqualTo(new[] { ("sample.name", "required") }));
    }

    [Test]
    public async Task ASiteSave_ChecksItsFields_AgainstTheValuesEverySiteShares()
    {
        await Service.SaveAsync(new SampleSettings { Keep = 1 }, 0, Editor);

        var refusal = Assert.ThrowsAsync<FormBuilderSettingsValidationException>(() => Service.SaveSiteAsync(SiteA, new SampleSettings { Tags = ["a", "b"] }, ["tags"], 0, Editor));

        Assert.That(refusal!.Errors.Select(e => (e.Field, e.Code)), Is.EqualTo(new[] { ("sample.tags", "maxCount") }));
    }

    [Test]
    public async Task ASiteRow_WithAnUnreadableValueOfAFieldEverySiteShares_StillGivesItsOwnFields()
    {
        await Settings.SaveAsync(new SettingsSave(SettingsScopes.ForSite(SiteA), SampleSettings.Alias, 3, 0, """{"keep":"many","count":3}""", null));

        var site = await Service.GetAsync<SampleSettings>(SiteA);

        Assert.That((site.Value.Count, site.Value.Keep, string.Join(",", site.Overrides)), Is.EqualTo((3, 50, "count")));
    }

    [Test]
    public async Task AnOldRow_IsMigratedWhenRead_AndWrittenInTheNewShapeAtItsNextSave()
    {
        await Settings.SaveAsync(new SettingsSave(SettingsScopes.Global, SampleSettings.Alias, 1, 0, """{"title":"Old","count":"7"}""", null));

        var read = await Service.GetAsync<SampleSettings>();
        var unchanged = await Settings.GetAsync(SettingsScopes.Global, SampleSettings.Alias);
        await Service.SaveAsync(read.Value, read.Version, Editor);
        var rewritten = await Settings.GetAsync(SettingsScopes.Global, SampleSettings.Alias);

        Assert.Multiple(() =>
        {
            Assert.That((read.Value.Name, read.Value.Count), Is.EqualTo(("Old", 7)));
            Assert.That((unchanged!.SchemaVersion, unchanged.Json), Is.EqualTo((1, """{"title":"Old","count":"7"}""")), "reading rewrites nothing");
            Assert.That((rewritten!.SchemaVersion, rewritten.Json), Is.EqualTo((3, """{"name":"Old","count":7,"mode":"quiet","limits":{"max":10},"tags":[],"keep":50}""")));
        });
    }

    [Test]
    public async Task AnOldSiteRow_IsMigratedToo()
    {
        await Settings.SaveAsync(new SettingsSave(SettingsScopes.ForSite(SiteA), SampleSettings.Alias, 1, 0, """{"title":"Site"}""", null));

        var read = await Service.GetAsync<SampleSettings>(SiteA);

        Assert.That((read.Value.Name, read.Overrides.Single()), Is.EqualTo(("Site", "name")));
    }

    [Test]
    public async Task ARowWithoutItsMigration_Throws_NamingTheSectionAndTheVersion()
    {
        await Settings.SaveAsync(new SettingsSave(SettingsScopes.Global, SampleSharedSettings.Alias, 1, 0, "{}", null));
        Write(db => db.Execute("UPDATE jfbSetting SET schemaVersion = 0 WHERE section = @0", SampleSharedSettings.Alias));

        Assert.That(
            () => Service.GetAsync<SampleSharedSettings>(),
            Throws.InvalidOperationException.With.Message.EqualTo("The settings section 'sampleshared' has no migration from schema version 0 to 1."));
    }

    [Test]
    public async Task ARowThatCannotBeRead_ReadsAsTheDefaults_KeepsItsVersion_AndTheNextSaveRepairsIt()
    {
        await Settings.SaveAsync(new SettingsSave(SettingsScopes.Global, SampleSettings.Alias, 3, 0, """{"count":"many","name":"Lost"}""", null));
        await Settings.SaveAsync(new SettingsSave(SettingsScopes.ForSite(SiteA), SampleSettings.Alias, 3, 0, """{"mode":2}""", null));

        var global = await Service.GetAsync<SampleSettings>();
        var site = await Service.GetAsync<SampleSettings>(SiteA);
        var repaired = await Service.SaveAsync(new SampleSettings { Name = "Repaired" }, global.Version, Editor);

        Assert.Multiple(() =>
        {
            Assert.That((global.Value.Name, global.Value.Count, global.Version), Is.EqualTo(("Jiron", 5, 1)));
            Assert.That((site.Value.Mode, site.Version, site.Overrides.Count), Is.EqualTo((SampleMode.Quiet, 1, 0)));
            Assert.That((repaired.Value.Name, repaired.Version), Is.EqualTo(("Repaired", 2)));
        });
    }

    [Test]
    public async Task AHostLock_OverridesTheBackofficeValue_WithoutRewritingTheRow_AndRefusesAnotherValue()
    {
        await Service.SaveAsync(new SampleSettings { Name = "Mine" }, 0, Editor);
        Rule.LockedName = "Host";
        Cache.Clear();

        var locked = await Service.GetAsync<SampleSettings>();

        Assert.Multiple(async () =>
        {
            Assert.That(locked.Value.Name, Is.EqualTo("Host"));
            Assert.That(locked.Locks["name"], Is.EqualTo(new SettingsLock(SettingsLockKind.Locked, SampleHostRule.NameKey)));
            Assert.That((await Settings.GetAsync(SettingsScopes.Global, SampleSettings.Alias))!.Json, Does.Contain("\"name\":\"Mine\""), "the row stays");
            Assert.That(
                () => Service.SaveAsync(locked.Value with { Name = "Other" }, locked.Version, Editor),
                Throws.InstanceOf<FormBuilderSettingLockedException>().With.Property(nameof(FormBuilderSettingLockedException.Field)).EqualTo("sample.name"));
            Assert.That((await Service.SaveAsync(locked.Value with { Count = 6 }, locked.Version, Editor)).Value.Count, Is.EqualTo(6), "the shown value saves");
        });
    }

    [Test]
    public async Task ARestrictionAndAClamp_ApplyWhereTheValueIsUsed_AndRefuseWhatTheyDoNotAllow()
    {
        await Service.SaveAsync(new SampleSettings { Mode = SampleMode.LoudAndClear, Limits = new SampleLimits { Max = 20 } }, 0, Editor);
        Rule.QuietOnly = true;
        Rule.MaxLimit = 8;
        Cache.Clear();

        var read = await Service.GetAsync<SampleSettings>();

        Assert.Multiple(() =>
        {
            Assert.That((read.Value.Mode, read.Value.Limits.Max), Is.EqualTo((SampleMode.Quiet, 8)));
            Assert.That(() => Service.SaveAsync(read.Value with { Mode = SampleMode.LoudAndClear }, read.Version, Editor), Throws.InstanceOf<FormBuilderSettingLockedException>());
            Assert.That(
                () => Service.SaveAsync(read.Value with { Limits = new SampleLimits { Max = 9 } }, read.Version, Editor),
                Throws.InstanceOf<FormBuilderSettingsValidationException>().With.Property(nameof(FormBuilderSettingsValidationException.Errors)).EqualTo(new[] { new SettingsError("sample.limits.max", "range", "Is above the limit that JironFormBuilder:Sample:MaxLimit sets.") }));
        });
    }

    [Test]
    public async Task ASave_IsAuditedWithWhatChanged_NeverAValue()
    {
        await Service.SaveAsync(new GeneralSettings { TimeZone = "Europe/Berlin" }, 1, Editor);
        await Service.SaveSiteAsync(SiteA, new GeneralSettings { TimeZone = "Asia/Tokyo" }, ["timeZone"], 0, Editor);
        await Service.SaveAsync(new SampleSettings { Name = "Two", Count = 6 }, 0, Editor);

        var entries = (await Audit.QueryAsync(new AuditQuery { Action = "settings.saved" }, PageRequest.First(10))).Items;

        Assert.Multiple(() =>
        {
            Assert.That(
                entries.Select(e => (e.EntityType, e.EntityKey, e.UserKey, e.Details)),
                Is.EquivalentTo(new[]
                {
                    ("settings", (Guid?)null, (Guid?)Editor, """{"section":"general","version":2,"fields":["timeZone"],"fieldCount":1}"""),
                    ("settings", SiteA, Editor, """{"section":"general","version":1,"fields":["timeZone"],"fieldCount":1}"""),
                    ("settings", null, Editor, """{"section":"sample","version":1,"fields":["count","name"],"fieldCount":2}"""),
                }));
            Assert.That(entries.Select(e => e.Details), Has.None.Contains("Europe").And.None.Contains("Asia"));
        });
    }

    [Test]
    public async Task ASave_TellsTheOtherServers_ItsScopeAndSection_AndANoOpTellsNobody()
    {
        await Service.SaveAsync(new GeneralSettings { TimeZone = "Europe/Berlin" }, 1, Editor);
        await Service.SaveSiteAsync(SiteA, new GeneralSettings { TimeZone = "Asia/Tokyo" }, ["timeZone"], 0, Editor);
        await Service.SaveSiteAsync(SiteB, new GeneralSettings(), [], 0, Editor);

        Assert.That(
            Messenger.Refreshes.Where(r => r.Refresher == FormBuilderSettingsCacheRefresher.UniqueId).Select(r => r.Payload),
            Is.EqualTo(new[] { new SettingsCacheChange(SettingsScopes.Global, "general").ToString(), new SettingsCacheChange(SettingsScopes.ForSite(SiteA), "general").ToString() }));
    }

    [Test]
    public async Task ASave_PublishesSettingsSaved_WithTheChangedFields_AfterItCommitted()
    {
        await Service.SaveAsync(new SampleSettings { Count = 9, Limits = new SampleLimits { Label = "L" } }, 0, Editor);

        var notification = Saved.Seen.Single();
        Assert.Multiple(async () =>
        {
            Assert.That((notification.Section, notification.SiteKey, notification.Version, notification.UserKey), Is.EqualTo(("sample", (Guid?)null, 1, (Guid?)Editor)));
            Assert.That(notification.ChangedFields, Is.EqualTo(new[] { "count", "limits.label" }));
            Assert.That(await Settings.GetAsync(SettingsScopes.Global, SampleSettings.Alias), Is.Not.Null);
        });
    }

    [Test]
    public async Task AFailingHandler_IsLogged_AndTheSaveStands()
    {
        Saved.Fail = true;

        var saved = await Service.SaveAsync(new GeneralSettings { TimeZone = "Europe/Berlin" }, 1, Editor);

        Assert.Multiple(async () =>
        {
            Assert.That(saved.Version, Is.EqualTo(2));
            Assert.That(Saved.Seen, Has.Count.EqualTo(1));
            Assert.That((await Service.GetAsync<GeneralSettings>()).Value.TimeZone, Is.EqualTo("Europe/Berlin"));
        });
    }

    [Test]
    public void ASave_InsideAnotherUnitOfWork_IsRefused_AndWritesNothing()
    {
        Assert.That(
            () => Database.WriteAsync(async _ => await Service.SaveAsync(new GeneralSettings { TimeZone = "Europe/Berlin" }, 1, Editor)),
            Throws.InvalidOperationException.With.Message.Contains("unit of work of its own"));
        Assert.That(Count("jfbSetting", "section = @0 AND version = 1", "general"), Is.EqualTo(1));
    }

    [Test]
    public void ASave_InsideAnUmbracoScope_IsRefused_AndWritesNothing()
    {
        using (ScopeProvider.CreateScope(autoComplete: true))
        {
            Assert.That(
                () => Service.SaveAsync(new GeneralSettings { TimeZone = "Europe/Berlin" }, 1, Editor),
                Throws.InvalidOperationException.With.Message.Contains("unit of work of its own"));
        }

        Assert.That(Count("jfbSetting", "section = @0 AND version = 1", "general"), Is.EqualTo(1));
    }

    [Test]
    public async Task ARead_IsCached_UntilTheRefresherClearsIt()
    {
        _ = await Service.GetAsync<GeneralSettings>();
        var gets = Rows.Gets;
        await Settings.SaveAsync(new SettingsSave(SettingsScopes.Global, "general", 1, 1, """{"timeZone":"Asia/Tokyo"}""", null));

        var cached = await Service.GetAsync<GeneralSettings>();
        var readsWhileCached = Rows.Gets - gets;
        var refresher = (FormBuilderSettingsCacheRefresher)GetRequiredService<CacheRefresherCollection>()[FormBuilderSettingsCacheRefresher.UniqueId]!;
        refresher.Refresh([new SettingsCacheChange(SettingsScopes.Global, "general")]);
        var refreshed = await Service.GetAsync<GeneralSettings>();

        Assert.Multiple(() =>
        {
            Assert.That((cached.Value.TimeZone, readsWhileCached), Is.EqualTo(("UTC", 0)), "no read while cached");
            Assert.That(refreshed.Value.TimeZone, Is.EqualTo("Asia/Tokyo"));
        });
    }

    [Test]
    public async Task ARefresherInstruction_FromAnotherServer_ClearsThisServersCopy()
    {
        _ = await Service.GetAsync<GeneralSettings>(SiteA);
        await Settings.SaveAsync(new SettingsSave(SettingsScopes.Global, "general", 1, 1, """{"timeZone":"Asia/Tokyo"}""", null));
        var refresher = GetRequiredService<CacheRefresherCollection>()[FormBuilderSettingsCacheRefresher.UniqueId]!;

        ((IJsonCacheRefresher)refresher).Refresh(GetRequiredService<IJsonSerializer>().Serialize(new[] { new SettingsCacheChange(SettingsScopes.Global, "general") }));

        Assert.That((await Service.GetAsync<GeneralSettings>(SiteA)).Value.TimeZone, Is.EqualTo("Asia/Tokyo"));
    }

    [Test]
    public async Task AReadThatRacedASave_IsNotCached()
    {
        Rows.AfterNextGet = async () =>
        {
            // Another server saves after this read took the row, and its instruction reaches this server.
            await Settings.SaveAsync(new SettingsSave(SettingsScopes.Global, "general", 1, 1, """{"timeZone":"Asia/Tokyo"}""", null));
            FormBuilderSettingsCacheRefresher.Forget(Cache, new SettingsCacheChange(SettingsScopes.Global, "general"));
        };

        var raced = await Service.GetAsync<GeneralSettings>();
        var next = await Service.GetAsync<GeneralSettings>();

        Assert.That((raced.Value.TimeZone, next.Value.TimeZone), Is.EqualTo(("UTC", "Asia/Tokyo")));
    }

    [Test]
    public void TheRefresher_IsRegistered_AndTheSectionsServicesToo()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Cache, Is.InstanceOf<ObjectCacheAppCache>(), "the tests of the cache use a real one");
            Assert.That(GetRequiredService<CacheRefresherCollection>()[FormBuilderSettingsCacheRefresher.UniqueId], Is.InstanceOf<FormBuilderSettingsCacheRefresher>());
            Assert.That(GetRequiredService<IEnumerable<ISettingsValidator<GeneralSettings>>>().Single(), Is.InstanceOf<GeneralSettingsValidator>());
            Assert.That(GetRequiredService<IFormBuilderSettingsService>(), Is.InstanceOf<FormBuilderSettingsService>());
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        base.CustomTestSetup(builder);
        builder.Services.AddUnique(new AppCaches(new ObjectCacheAppCache(), NoAppCache.Instance, new IsolatedCaches(_ => NoAppCache.Instance)));
        builder.Services.AddSingleton<ISettingsSectionMigration, SampleTitleToName>();
        builder.Services.AddSingleton<ISettingsSectionMigration, SampleCountToNumber>();
        builder.Services.AddSingleton<ISettingsValidator<SampleSettings>, SampleSettingsValidator>();
        builder.Services.AddSingleton<SampleHostRule>();
        builder.Services.AddSingleton<ISettingsHostRule<SampleSettings>>(provider => provider.GetRequiredService<SampleHostRule>());
        builder.Services.AddSingleton<SavedNotifications>();
        builder.AddNotificationAsyncHandler<SettingsSavedNotification, SavedNotificationHandler>();
        builder.Services.AddSingleton(provider => new HookedSettingsRepository(new SettingsRepository(provider.GetRequiredService<FormBuilderDatabase>())));
        builder.Services.AddUnique<ISettingsRepository>(provider => provider.GetRequiredService<HookedSettingsRepository>());
        builder.Services.AddSingleton<RecordingServerMessenger>();
        builder.Services.AddUnique<IServerMessenger>(provider => provider.GetRequiredService<RecordingServerMessenger>());
    }

    private static bool SameSnapshot(SettingsSnapshot<GeneralSettings> a, SettingsSnapshot<GeneralSettings> b) =>
        a.Value == b.Value && a.Version == b.Version && a.Overrides.SetEquals(b.Overrides) && a.Locks.Count == b.Locks.Count;

    private static bool SameTuple((string, int, IReadOnlySet<string>) a, (string, int, IReadOnlySet<string>) b) =>
        a.Item1 == b.Item1 && a.Item2 == b.Item2 && a.Item3.SetEquals(b.Item3);

}
