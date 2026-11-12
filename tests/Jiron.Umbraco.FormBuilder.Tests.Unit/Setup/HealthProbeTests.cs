using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Email;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags;
using Jiron.Umbraco.FormBuilder.Web.Setup;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.Mail;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.Navigation;
using Umbraco.Cms.Core.Sync;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Setup;

/// <summary>What each health probe finds (section 4.7, ADR-0165): one sentence per problem, in the user's language, and nothing when all is well.</summary>
public sealed class HealthProbeTests
{
    private static readonly HealthWords English = HealthWords.Of("en-GB");

    private static IEnumerable<string> Codes(IReadOnlyList<HealthFinding> found) => found.Select(finding => finding.Code);

    // The base URL (ADR-0067).
    private static readonly Guid Site = Guid.Parse("60000000-0000-4000-8000-000000000001");

    private static BaseUrlHealth BaseUrlProbe(TestUmbraco umbraco, string? setting = null, bool published = true, int? template = 1060)
    {
        Guid[] roots = [Site];
        umbraco.Navigation.TryGetRootKeys(out Arg.Any<IEnumerable<Guid>>()).Returns(call =>
        {
            call[0] = roots;
            return true;
        });
        var contents = Substitute.For<IContentService>();
        var content = Substitute.For<IContent>();
        content.Name.Returns("Lindenhall");
        content.Published.Returns(published);
        content.TemplateId.Returns(template);
        contents.GetById(Site).Returns(content);
        return new BaseUrlHealth(umbraco.BaseUrls(MergeTagTestSite.Settings("UTC", setting)), umbraco.Navigation, contents);
    }

    [Test]
    public async Task ASiteWithNoSetting_NoHttpsDomain_AndNoApplicationUrl_IsNamed_WithWhereToFixIt()
    {
        var found = await BaseUrlProbe(new TestUmbraco()).FindAsync(English);

        Assert.Multiple(() =>
        {
            Assert.That(Codes(found), Is.EqualTo(new[] { "siteNoBaseUrl" }));
            Assert.That(found[0].Message, Does.Contain("Lindenhall"));
            Assert.That(found[0].Link, Is.EqualTo(HealthLink.SettingsGeneral));
        });
    }

    [Test]
    public async Task ARootThatIsNoSite_IsLeftOut()
    {
        var data = await BaseUrlProbe(new TestUmbraco(), template: null).FindAsync(English);
        var unpublished = await BaseUrlProbe(new TestUmbraco(), published: false).FindAsync(English);

        Assert.Multiple(() =>
        {
            Assert.That(data, Is.Empty, "a folder of data has no address to give");
            Assert.That(unpublished, Is.Empty, "and an unpublished root no visitors yet");
        });
    }

    [Test]
    public async Task ASiteIsNamed_OnlyToWhoMayReadIt()
    {
        var found = await BaseUrlProbe(new TestUmbraco()).FindAsync(HealthWords.Of("en", namesSites: false));

        Assert.That(found.Single().Message, Does.StartWith("A site has no base URL").And.Not.Contain("Lindenhall"));
    }

    [Test]
    public async Task ASiteWithTheSetting_Passes() =>
        Assert.That(await BaseUrlProbe(new TestUmbraco(), "https://www.lindenhall.example").FindAsync(English), Is.Empty);

    [Test]
    public async Task ASiteWithAnHttpsDomain_Passes()
    {
        var umbraco = new TestUmbraco();
        umbraco.Domain(Site, 1050, "https://lindenhall.example");

        Assert.That(await BaseUrlProbe(umbraco).FindAsync(English), Is.Empty);
    }

    [Test]
    public async Task ASiteWithOnlyAnHttpDomain_IsStillNamed()
    {
        var umbraco = new TestUmbraco();
        umbraco.Domain(Site, 1050, "http://lindenhall.example");

        Assert.That(Codes(await BaseUrlProbe(umbraco).FindAsync(English)), Is.EqualTo(new[] { "siteNoBaseUrl" }));
    }

    [Test]
    public async Task ASiteWithTheApplicationUrl_Passes()
    {
        var umbraco = new TestUmbraco();
        umbraco.RoutingSettings.UmbracoApplicationUrl = "https://cms.lindenhall.example";

        Assert.That(await BaseUrlProbe(umbraco).FindAsync(English), Is.Empty);
    }

    // Mail (ADR-0007).
    private static EmailHealth EmailProbe(bool canSend, string? address, MailServerReach? reach = null)
    {
        var sender = Substitute.For<IEmailSender>();
        sender.CanSendRequiredEmail().Returns(canSend);
        var settings = Substitute.For<IFormBuilderSettingsService>();
        settings.GetAsync<EmailSettings>(null, Arg.Any<CancellationToken>()).Returns(Task.FromResult(
            new SettingsSnapshot<EmailSettings>(new EmailSettings { SenderAddress = address }, 1, new HashSet<string>(), new Dictionary<string, SettingsLock>())));
        var global = Substitute.For<IOptionsMonitor<GlobalSettings>>();
        global.CurrentValue.Returns(new GlobalSettings());
        return new EmailHealth(sender, settings, global, Substitute.For<IDocumentNavigationQueryService>(), reach ?? new MailServerReach(new TestTimeProvider()));
    }

    [Test]
    public async Task Mail_SaysEachOfWhatItLacks_AndThatTheChecklistCoversIt()
    {
        var both = await EmailProbe(false, null).FindAsync(English);
        var noFrom = await EmailProbe(true, null).FindAsync(English);
        var noServer = await EmailProbe(false, "forms@jfb.test").FindAsync(English);

        Assert.Multiple(async () =>
        {
            Assert.That(Codes(both), Is.EqualTo(new[] { "emailNoServer", "emailNoFrom" }));
            Assert.That(both.All(finding => finding.CoversEmail), Is.True);
            Assert.That(Codes(noFrom), Is.EqualTo(new[] { "emailNoFrom" }));
            Assert.That(noFrom[0].Link, Is.EqualTo(HealthLink.SettingsEmail));
            Assert.That(Codes(noServer), Is.EqualTo(new[] { "emailNoServer" }));
            Assert.That(await EmailProbe(true, "forms@jfb.test").FindAsync(English), Is.Empty);
        });
    }

    [Test]
    public async Task Mail_SaysWhenThisServerFoundNoMailServer_TillAMailLeaves_AndLeavesTheChecklistAlone()
    {
        // An outage of a mail server that is set up (ADR-0237).
        var reach = new MailServerReach(new TestTimeProvider());
        reach.Refusal();

        var found = await EmailProbe(true, "forms@jfb.test", reach).FindAsync(English);
        var german = await EmailProbe(true, "forms@jfb.test", reach).FindAsync(HealthWords.Of("de"));
        var notSetUp = await EmailProbe(false, "forms@jfb.test", reach).FindAsync(English);
        reach.Reached();
        var back = await EmailProbe(true, "forms@jfb.test", reach).FindAsync(English);

        Assert.Multiple(() =>
        {
            Assert.That(Codes(found), Is.EqualTo(new[] { "emailUnreached" }));
            Assert.That(found[0].Message, Does.Contain("couldn't reach the mail server").And.Contain("without using up their attempts"));
            Assert.That(found[0].CoversEmail, Is.False, "the setup checklist is about setting up");
            Assert.That(german[0].Message, Does.Contain("Mailserver zuletzt nicht erreicht"));
            Assert.That(Codes(notSetUp), Is.EqualTo(new[] { "emailNoServer" }), "said once: no server set up is the finding");
            Assert.That(back, Is.Empty);
        });
    }

    // How many servers (ADR-0041).
    private static SiteServers Servers(ServerRole role, int active = 1)
    {
        var roles = Substitute.For<IServerRoleAccessor>();
        roles.CurrentServerRole.Returns(role);
        var registrations = Substitute.For<IServerRegistrationService>();
        registrations.GetActiveServers(true).Returns(Enumerable.Range(0, active).Select(_ => Substitute.For<IServerRegistration>()).ToArray());
        return new SiteServers(roles, registrations, NullLogger<SiteServers>.Instance);
    }

    [TestCase(ServerRole.Single, 3, false, TestName = "A site set to one server runs on one")]
    [TestCase(ServerRole.Subscriber, 1, true, TestName = "A subscriber has a publisher")]
    [TestCase(ServerRole.SchedulingPublisher, 1, false, TestName = "A publisher alone runs on one")]
    [TestCase(ServerRole.SchedulingPublisher, 2, true, TestName = "A publisher with another active server runs on two")]
    public void Servers_AreCountedByTheRoleAndTheRegistrations(ServerRole role, int active, bool more) =>
        Assert.That(Servers(role, active).MoreThanOne, Is.EqualTo(more));

    [Test]
    public void Servers_ThatCannotBeRead_CountAsOne()
    {
        var roles = Substitute.For<IServerRoleAccessor>();
        roles.CurrentServerRole.Returns(ServerRole.SchedulingPublisher);
        var registrations = Substitute.For<IServerRegistrationService>();
        registrations.GetActiveServers(true).Returns(_ => throw new InvalidOperationException("no table"));

        Assert.That(new SiteServers(roles, registrations, NullLogger<SiteServers>.Instance).MoreThanOne, Is.False);
    }

    // Uploads (ADR-0041).
    private static UploadsHealth UploadsProbe(string root, IFormBuilderFileStore store, ServerRole role)
    {
        var options = Options.Create(new FormBuilderOptions { Files = { Root = root } });
        var folders = Substitute.For<IHostFolders>();
        var content = Path.Combine(Path.GetTempPath(), "jfb-site");
        folders.ContentRoot.Returns(content);
        folders.WebRoot.Returns(Path.Combine(content, "wwwroot"));
        return new UploadsHealth(options, store, folders, Servers(role));
    }

    private static PhysicalFileStore Physical() => SetupTestForms.PhysicalStore();

    [Test]
    public async Task Uploads_WhereTheWebsiteServesThem_AreSaid()
    {
        var found = await UploadsProbe("~/wwwroot/uploads", Physical(), ServerRole.Single).FindAsync(English);

        Assert.That(Codes(found), Is.EqualTo(new[] { "uploadsReachable" }));
    }

    [Test]
    public async Task Uploads_InTheDefaultFolder_AreSaidOnlyOnMoreThanOneServer()
    {
        var one = await UploadsProbe(FormBuilderFilesOptions.DefaultRoot, Physical(), ServerRole.Single).FindAsync(English);
        var two = await UploadsProbe(FormBuilderFilesOptions.DefaultRoot, Physical(), ServerRole.Subscriber).FindAsync(English);
        var chosen = await UploadsProbe("~/shared/uploads", Physical(), ServerRole.Subscriber).FindAsync(English);
        var own = await UploadsProbe(FormBuilderFilesOptions.DefaultRoot, Substitute.For<IFormBuilderFileStore>(), ServerRole.Subscriber).FindAsync(English);

        Assert.Multiple(() =>
        {
            Assert.That(one, Is.Empty);
            Assert.That(Codes(two), Is.EqualTo(new[] { "uploadsOnEachServer" }));
            Assert.That(chosen, Is.Empty, "a folder the host chose is its choice");
            Assert.That(own, Is.Empty, "and so is a store of its own");
        });
    }

    // The key ring.
    private sealed class FixedProfile(bool exists, bool shared, bool container) : KeyRingProfile
    {
        public override bool Exists => exists;

        public override bool SharedByHost => shared;

        public override bool InContainer => container;
    }

    private static KeyRingHealth KeyRing(IXmlRepository? repository, bool profile, ServerRole role, bool shared = false, bool container = false) =>
        new(Options.Create(new KeyManagementOptions { XmlRepository = repository }), Servers(role), new FixedProfile(profile, shared, container));

    [Test]
    public async Task TheKeyRing_IsSaid_WhenItLivesOnlyInMemory_OrOnEachServersDisk()
    {
        var memory = await KeyRing(null, false, ServerRole.Single).FindAsync(English);
        var disks = await KeyRing(null, true, ServerRole.Subscriber).FindAsync(English);
        var one = await KeyRing(null, true, ServerRole.Single).FindAsync(English);
        var store = await KeyRing(Substitute.For<IXmlRepository>(), false, ServerRole.Subscriber).FindAsync(English);
        var azure = await KeyRing(null, true, ServerRole.Subscriber, shared: true).FindAsync(English);
        var container = await KeyRing(null, true, ServerRole.Single, container: true).FindAsync(English);
        var containerWithStore = await KeyRing(Substitute.For<IXmlRepository>(), true, ServerRole.Single, container: true).FindAsync(English);

        Assert.Multiple(() =>
        {
            Assert.That(Codes(memory), Is.EqualTo(new[] { "keyRingInMemory" }));
            Assert.That(Codes(disks), Is.EqualTo(new[] { "keyRingOnEachServer" }));
            Assert.That(one, Is.Empty, "one server keeps it in its profile, across restarts");
            Assert.That(store, Is.Empty, "a store the site chose is its choice");
            Assert.That(azure, Is.Empty, "Azure App Service shares the folder between a site's instances");
            Assert.That(Codes(container), Is.EqualTo(new[] { "keyRingInContainer" }), "a container's disk goes with it");
            Assert.That(containerWithStore, Is.Empty);
        });
    }

    [Test]
    public void TheProfile_LooksForAFolderThatExists() => Assert.That(new KeyRingProfile().Exists, Is.True, "the account that runs the tests has one");

    // The jobs.
    [Test]
    public async Task TheJobs_AreSaid_OnlyAfterAQuarterOfAnHourWithoutARun()
    {
        var time = new TestTimeProvider();
        var values = new InMemoryKeyValues();
        var heartbeat = new JobHeartbeat(values, time);
        var probe = new JobsHealth(heartbeat, time);

        var started = await probe.FindAsync(English);
        time.Advance(JobsHealth.Silence + TimeSpan.FromSeconds(1));
        var neverRan = await probe.FindAsync(English);
        await heartbeat.BeatAsync();
        var ran = await probe.FindAsync(English);
        time.Advance(JobsHealth.Silence);
        var atTheLimit = await probe.FindAsync(English);
        time.Advance(TimeSpan.FromSeconds(1));
        var silent = await probe.FindAsync(English);

        Assert.Multiple(() =>
        {
            Assert.That(started, Is.Empty, "a server that just started has not missed anything");
            Assert.That(Codes(neverRan), Is.EqualTo(new[] { "jobsStopped" }));
            Assert.That(ran, Is.Empty);
            Assert.That(atTheLimit, Is.Empty);
            Assert.That(Codes(silent), Is.EqualTo(new[] { "jobsStopped" }));
        });
    }

    [Test]
    public async Task ASiteThatWasAway_HasMissedNothing_UntilARunHasHadTheTimeToCome()
    {
        var time = new TestTimeProvider();
        var values = new InMemoryKeyValues();
        await new JobHeartbeat(values, time).BeatAsync();

        // Idle for an hour; the request that wakes it comes to a new process, before any job.
        time.Advance(TimeSpan.FromHours(1));
        var probe = new JobsHealth(new JobHeartbeat(values, time), time);
        var woken = await probe.FindAsync(English);
        time.Advance(JobsHealth.Silence + TimeSpan.FromSeconds(1));
        var silent = await probe.FindAsync(English);

        Assert.Multiple(() =>
        {
            Assert.That(woken, Is.Empty);
            Assert.That(Codes(silent), Is.EqualTo(new[] { "jobsStopped" }));
        });
    }

    [Test]
    public async Task TheHeartbeat_WritesAtMostEveryFiveMinutes_AndAnUnreadableRowIsNoRun()
    {
        var time = new TestTimeProvider();
        var values = Substitute.For<IKeyValueRepository>();
        var heartbeat = new JobHeartbeat(values, time);

        await heartbeat.BeatAsync();
        time.Advance(JobHeartbeat.BeatEvery - TimeSpan.FromSeconds(1));
        await heartbeat.BeatAsync();
        await values.Received(1).SetAsync(JobHeartbeat.Key, Arg.Any<string?>(), Arg.Any<CancellationToken>());
        time.Advance(TimeSpan.FromSeconds(1));
        await heartbeat.BeatAsync();
        await values.Received(2).SetAsync(JobHeartbeat.Key, Arg.Any<string?>(), Arg.Any<CancellationToken>());

        values.GetAsync(JobHeartbeat.Key, Arg.Any<CancellationToken>()).Returns("not a time");
        Assert.That(await heartbeat.LastRunAsync(), Is.EqualTo(heartbeat.Started));
    }

    // Deliveries (section 4.4).
    private static DeliveriesHealth Deliveries(int mail, int runs, bool paused, out IEmailRepository emails, out TestTimeProvider time)
    {
        time = new TestTimeProvider();
        emails = Substitute.For<IEmailRepository>();
        emails.CountFailedSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>()).Returns(mail);
        var actionRuns = Substitute.For<IActionRunRepository>();
        actionRuns.CountFailedSinceAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(runs);
        var options = Substitute.For<IOptionsMonitor<FormBuilderOptions>>();
        options.CurrentValue.Returns(new FormBuilderOptions { Actions = { Paused = paused } });
        return new DeliveriesHealth(emails, actionRuns, new OutboxWake(options), time);
    }

    [Test]
    public async Task Deliveries_SayWhatFailedInTheLastDay_AndThatSendingIsPaused()
    {
        var found = await Deliveries(2, 1, true, out var emails, out var time).FindAsync(English);
        var none = await Deliveries(0, 0, false, out _, out _).FindAsync(English);

        Assert.Multiple(async () =>
        {
            Assert.That(Codes(found), Is.EqualTo(new[] { "sendingPaused", "emailsFailing", "actionsFailing" }));
            Assert.That(found[1].Message, Is.EqualTo("2 emails couldn't be sent in the last 24 hours."));
            Assert.That(found[2].Message, Is.EqualTo("1 action failed in the last 24 hours."));
            Assert.That(found[1].Link, Is.EqualTo(HealthLink.Deliveries));
            Assert.That(none, Is.Empty);
            await emails.Received(1).CountFailedSinceAsync(time.GetUtcNow() - TimeSpan.FromHours(24), EmailOutbox.Keep, Arg.Any<CancellationToken>());
        });
    }

    // The migrations (ADR-0033).
    private sealed class FixedState(bool pending) : IFormBuilderMigrationState
    {
        public ValueTask<bool> IsPendingAsync(CancellationToken cancellationToken = default) => new(pending);

        public void Invalidate()
        {
        }
    }

    [TestCase(true, new[] { "migrationsPending" })]
    [TestCase(false, new string[0])]
    public async Task TheMigrations_AreSaid_WhilePending(bool pending, string[] codes) =>
        Assert.That(Codes(await new MigrationsHealth(new FixedState(pending)).FindAsync(English)), Is.EqualTo(codes));

    // All together.
    private sealed class Throws : HealthProbe
    {
        public override Task<IReadOnlyList<HealthFinding>> FindAsync(HealthWords words, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("no table");

        public override string Passed(HealthWords words) => string.Empty;
    }

    private sealed class Finds(string code, string? message = null) : HealthProbe
    {
        public int Asked { get; private set; }

        public override Task<IReadOnlyList<HealthFinding>> FindAsync(HealthWords words, CancellationToken cancellationToken = default)
        {
            Asked++;
            return Task.FromResult<IReadOnlyList<HealthFinding>>([new HealthFinding(code, message ?? words.JobsPassed)]);
        }

        public override string Passed(HealthWords words) => string.Empty;
    }

    [Test]
    public async Task AProbeThatFails_SaysSo_AndLeavesTheOthersStanding()
    {
        var found = await new FormBuilderHealth([new Finds("first", "One."), new Throws(), new Finds("last", "Two.")], new TestTimeProvider(), NullLogger<FormBuilderHealth>.Instance).FindAsync("en", true);

        Assert.That(Codes(found), Is.EqualTo(new[] { "first", "checkFailed", "last" }));
    }

    [TestCase("de-CH", "Die Hintergrundaufgaben laufen.")]
    [TestCase("de", "Die Hintergrundaufgaben laufen.")]
    [TestCase("en-US", "The background jobs are running.")]
    [TestCase("fr", "The background jobs are running.")]
    [TestCase(null, "The background jobs are running.")]
    public async Task TheWords_ComeInTheUsersLanguage_AndEnglishOtherwise(string? culture, string expected)
    {
        var found = await new FormBuilderHealth([new Finds("x")], new TestTimeProvider(), NullLogger<FormBuilderHealth>.Instance).FindAsync(culture, true);

        Assert.That(found[0].Message, Is.EqualTo(expected));
    }

    [Test]
    public async Task AnAnswer_StandsForAMinute_PerLanguageAndAudience()
    {
        var probe = new Finds("x");
        var time = new TestTimeProvider();
        var health = new FormBuilderHealth([probe], time, NullLogger<FormBuilderHealth>.Instance);

        await health.FindAsync("en", true);
        await health.FindAsync("en-GB", true);
        var once = probe.Asked;
        await health.FindAsync("de", true);
        await health.FindAsync("en", false);
        var perAudience = probe.Asked;
        time.Advance(FormBuilderHealth.Fresh);
        await health.FindAsync("en", true);

        Assert.Multiple(() =>
        {
            Assert.That(once, Is.EqualTo(1));
            Assert.That(perAudience, Is.EqualTo(3));
            Assert.That(probe.Asked, Is.EqualTo(4));
        });
    }

    [Test]
    public async Task TheSameSentence_IsSaidOnce_AsTwoSitesWithoutANameWouldSayIt()
    {
        var found = await new FormBuilderHealth([new Finds("a", "Same."), new Finds("b", "Same.")], new TestTimeProvider(), NullLogger<FormBuilderHealth>.Instance).FindAsync("en", false);

        Assert.That(Codes(found), Is.EqualTo(new[] { "a" }));
    }

    [Test]
    public void ThePlurals_ReadInBothLanguages()
    {
        var german = HealthWords.Of("de");

        Assert.Multiple(() =>
        {
            Assert.That(English.EmailsFailing(1), Is.EqualTo("1 email couldn't be sent in the last 24 hours."));
            Assert.That(german.EmailsFailing(1), Does.StartWith("1 E-Mail konnte"));
            Assert.That(german.EmailsFailing(3), Does.StartWith("3 E-Mails konnten"));
            Assert.That(German(german), Is.Not.EqualTo(English.ActionsFailing(3)));
        });

        static string German(HealthWords words) => words.ActionsFailing(3);
    }
}
