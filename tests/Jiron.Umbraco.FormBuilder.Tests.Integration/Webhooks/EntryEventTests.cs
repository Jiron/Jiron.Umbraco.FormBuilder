using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Jiron.Umbraco.FormBuilder.Core.Webhooks;
using Jiron.Umbraco.FormBuilder.Testing.Stub;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Actions;
using Jiron.Umbraco.FormBuilder.Tests.Integration.OptionLists;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Sync;
using Umbraco.Cms.Core.Webhooks;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Webhooks;

/// <summary>What the tests' handler heard.</summary>
internal sealed class HeardEntries
{
    public List<EntryNotification> Heard { get; } = [];

    public bool Fails { get; set; }

    public bool Hangs { get; set; }
}

/// <summary>A site's handler of the entry notifications, for the tests: it writes down what it heard.</summary>
internal sealed class HeardEntriesHandler(HeardEntries heard)
    : INotificationAsyncHandler<EntrySubmittedNotification>, INotificationAsyncHandler<EntryActionFailedNotification>, INotificationAsyncHandler<EntryApprovedNotification>, INotificationAsyncHandler<EntryRejectedNotification>
{
    public Task HandleAsync(EntryApprovedNotification notification, CancellationToken cancellationToken) => HearAsync(notification);

    public Task HandleAsync(EntryRejectedNotification notification, CancellationToken cancellationToken) => HearAsync(notification);

    public Task HandleAsync(EntrySubmittedNotification notification, CancellationToken cancellationToken) => HearAsync(notification);

    public Task HandleAsync(EntryActionFailedNotification notification, CancellationToken cancellationToken) => HearAsync(notification);

    private Task HearAsync(EntryNotification notification)
    {
        heard.Heard.Add(notification);
        if (heard.Hangs)
        {
            return Task.Delay(TimeSpan.FromMinutes(1));
        }

        return heard.Fails ? throw new InvalidOperationException("the site's handler failed") : Task.CompletedTask;
    }
}

/// <summary>The entry events, in a site's handlers and through Umbraco's own webhooks (ROADMAP 8.4, section 4.4, ADR-0161).</summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class EntryEventTests : ActionTestBase
{
    private const string Spammy = """{"revision":1,"values":{"name":"Ada"},"honeypot":"Buy now"}""";

    private HeardEntries Heard => GetRequiredService<HeardEntries>();

    private static FormAction Failing(string name) => Action(name);

    private Task<Guid> FormAsync(params FormAction[] actions) => PublishAsync(name: Guid.NewGuid().ToString("N"), change: definition => definition with { Actions = actions });

    [Test]
    public void EveryEvent_IsInUmbracosPicker_WithItsName()
    {
        var events = GetRequiredService<WebhookEventCollection>();

        Assert.That(events.Select(each => each.Alias), Is.SupersetOf(new[] { "jfbEntrySubmitted", "jfbEntryApproved", "jfbEntryRejected", "jfbEntryDeleted", "jfbActionFailed" }));
        Assert.That(events.Single(each => each.Alias == "jfbEntrySubmitted").EventName, Is.EqualTo("Forms: entry submitted"));
    }

    [Test]
    public async Task ASubmission_ReachesAWebhookOnTheEvent_WithTheStablePayload_AndNoAnswer()
    {
        // A test host has no server role, as a load-balanced site's front-end has not the scheduling one: the request is queued all the same.
        Assert.That(GetRequiredService<IServerRoleAccessor>().CurrentServerRole, Is.Not.EqualTo(ServerRole.Single));

        await GetRequiredService<IWebhookService>().CreateAsync(new Webhook("https://hooks.jfb.test/entries", true, null, ["jfbEntrySubmitted"]));
        var form = await FormAsync();

        var entry = await SubmitAsync(form, """{"name":"Ada Lovelace"}""");

        var requests = (await GetRequiredService<IWebhookRequestService>().GetAllAsync()).ToList();
        var row = (await FormRows.GetAsync(form))!;
        Assert.Multiple(() =>
        {
            Assert.That(requests, Has.Count.EqualTo(1));
            Assert.That(requests[0].EventAlias, Is.EqualTo("jfbEntrySubmitted"));
            Assert.That(requests[0].RequestObject, Does.Contain(entry.Key.ToString("D")).And.Contain(row.Alias).And.Contain("approved"));
            Assert.That(requests[0].RequestObject, Does.Not.Contain("Ada"), "Umbraco sends it as it is, so no answer is in it");
        });
    }

    [TestCase("Approved", "jfbEntryApproved")]
    [TestCase("Rejected", "jfbEntryRejected")]
    public async Task AStateChange_ReachesAWebhookOnItsEvent(string state, string alias)
    {
        await GetRequiredService<IWebhookService>().CreateAsync(new Webhook("https://hooks.jfb.test/entries", true, null, [alias]));
        var form = await PublishAsync(
            name: Guid.NewGuid().ToString("N"),
            change: definition => definition with { Settings = definition.Settings with { Submission = definition.Settings.Submission with { Approval = ApprovalMode.Manual } } });
        var entry = await SubmitAsync(form, """{"name":"Ada Lovelace"}""");

        await GetRequiredService<EntryStateChanges>().ChangeAsync(form, [entry.Key], Enum.Parse<EntryState>(state), null);

        var request = (await GetRequiredService<IWebhookRequestService>().GetAllAsync()).Single();
        Assert.Multiple(() =>
        {
            Assert.That(request.EventAlias, Is.EqualTo(alias));
            Assert.That(request.RequestObject, Does.Contain(entry.Key.ToString("D")).And.Contain(state.ToLowerInvariant()));
            Assert.That(request.RequestObject, Does.Not.Contain("Ada"));
        });
    }

    [Test]
    public async Task AnActionThatFailsForGood_ReachesAWebhookOnItsEvent_WithItsTypeAndCode()
    {
        await GetRequiredService<IWebhookService>().CreateAsync(new Webhook("https://hooks.jfb.test/entries", true, null, ["jfbActionFailed"]));
        Scripted.Scripts["never"] = (_, _) => Task.FromResult(ActionResult.Failed("refused"));
        var entry = await SubmitAsync(await FormAsync(Failing("never")), """{"name":"Ada Lovelace"}""");

        await DrainAsync(entry.Id);

        var request = (await GetRequiredService<IWebhookRequestService>().GetAllAsync()).Single();
        Assert.Multiple(() =>
        {
            Assert.That(request.EventAlias, Is.EqualTo("jfbActionFailed"));
            Assert.That(request.RequestObject, Does.Contain("refused").And.Not.Contain("Ada"));
        });
    }

    [Test]
    public async Task WithTheSettingOff_NoWebhookFires_AndASitesHandlerStillHears()
    {
        await GetRequiredService<IWebhookService>().CreateAsync(new Webhook("https://hooks.jfb.test/entries", true, null, ["jfbEntrySubmitted"]));
        var settings = GetRequiredService<IFormBuilderSettingsService>();
        var current = await settings.GetAsync<IntegrationSettings>();
        await settings.SaveAsync(new IntegrationSettings { UmbracoWebhookEvents = false }, current.Version, Editor);
        var form = await FormAsync();

        await SubmitAsync(form);

        Assert.Multiple(async () =>
        {
            Assert.That(await GetRequiredService<IWebhookRequestService>().GetAllAsync(), Is.Empty);
            Assert.That(Heard.Heard.OfType<EntrySubmittedNotification>().Count(), Is.EqualTo(1));
        });
    }

    [TestCase("signingSecret")]
    [TestCase("slackWebhook")]
    [TestCase("teamsWebhook")]
    public async Task TheIntegrationsSettings_HoldOnlyReferences(string field)
    {
        var settings = GetRequiredService<IFormBuilderSettingsService>();
        var current = await settings.GetAsync<IntegrationSettings>();
        var section = field switch
        {
            "signingSecret" => current.Value with { SigningSecret = "JFB_FAKE-literal" },
            "slackWebhook" => current.Value with { SlackWebhook = "https://hooks.jfb.test/JFB_FAKE" },
            _ => current.Value with { TeamsWebhook = "https://hooks.jfb.test/JFB_FAKE" },
        };

        var refused = Assert.ThrowsAsync<FormBuilderSettingsValidationException>(() => settings.SaveAsync(section, current.Version, Editor));

        Assert.That(refused!.Errors.Single().Field, Is.EqualTo($"integrations.{field}"));
    }

    [Test]
    public async Task ASitesFailingHandler_NeverFailsTheVisitor()
    {
        Heard.Fails = true;
        var form = await FormAsync();

        var entry = await SubmitAsync(form);

        Assert.Multiple(() =>
        {
            Assert.That(entry.State, Is.EqualTo(EntryState.Approved));
            Assert.That(Heard.Heard, Is.Not.Empty);
        });
    }

    [Test]
    public async Task ASitesHangingHandler_HoldsUpTheVisitorForAMomentAtMost()
    {
        Heard.Hangs = true;
        var form = await FormAsync();
        var watch = System.Diagnostics.Stopwatch.StartNew();

        var entry = await SubmitAsync(form);

        Assert.Multiple(() =>
        {
            Assert.That(entry.State, Is.EqualTo(EntryState.Approved));
            Assert.That(watch.Elapsed, Is.LessThan(EntryEvents.Limit + TimeSpan.FromSeconds(10)));
        });
    }

    [Test]
    public async Task AnEntryInSpam_IsToldOfOnlyOnceItIsNoSpam()
    {
        var form = await FormAsync();
        using var content = new StringContent(Spammy, System.Text.Encoding.UTF8, "application/json");
        using var answer = await SendAsync(HttpMethod.Post, $"{Root}/forms/{form}/entries", request => request.Content = content);
        var row = (await FormRows.GetAsync(form))!;
        var entry = (await Entries.GetByIdAsync((await Entries.ListIdsAsync(row.Id, 10))[^1]))!;
        Assert.That(entry.State, Is.EqualTo(EntryState.Spam));
        var before = Heard.Heard.Count;

        await GetRequiredService<EntrySpamFolder>().MarkAsNotSpamAsync(entry.Key, null);

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.Zero);
            Assert.That(Heard.Heard.Single().EntryKey, Is.EqualTo(entry.Key));
            Assert.That(Heard.Heard.Single().FormAlias, Is.EqualTo(row.Alias));
        });
    }

    [Test]
    public async Task AnActionThatFailsForGood_IsToldOf_AndOneThatWillBeTriedAgainIsNot()
    {
        Scripted.Scripts["again"] = (_, _) => Task.FromResult(ActionResult.TryAgain("busy"));
        Scripted.Scripts["never"] = (_, _) => Task.FromResult(ActionResult.Failed("refused"));
        var form = await FormAsync(Failing("again"));
        var entry = await SubmitAsync(form);
        await Pass.RunAsync(CancellationToken.None);
        var afterRetry = Heard.Heard.OfType<EntryActionFailedNotification>().Count();

        var other = await FormAsync(Failing("never"));
        var failed = await SubmitAsync(other);
        await Pass.RunAsync(CancellationToken.None);

        var told = Heard.Heard.OfType<EntryActionFailedNotification>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(entry.Id, Is.Not.EqualTo(failed.Id));
            Assert.That(afterRetry, Is.Zero);
            Assert.That(told.EntryKey, Is.EqualTo(failed.Key));
            Assert.That(told.ActionType, Is.EqualTo(ScriptedActionType.Alias));
            Assert.That(told.Code, Is.EqualTo("refused"));
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        base.CustomTestSetup(builder);
        builder.Services.AddSingleton<HeardEntries>();
        builder.AddNotificationAsyncHandler<EntrySubmittedNotification, HeardEntriesHandler>();
        builder.AddNotificationAsyncHandler<EntryActionFailedNotification, HeardEntriesHandler>();
    }
}

/// <summary>The same webhook outside Development (ADR-0076, ADR-0161): the site's own transport refuses loopback, and a host the configuration does not name is refused before anything goes out.</summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class WebhookRefusalTests : ActionTestBase
{
    private StubServer _stub = null!;

    [OneTimeSetUp]
    public async Task StartStubAsync() => _stub = await StubServer.StartAsync(port: 0);

    [OneTimeTearDown]
    public async Task StopStubAsync() => await _stub.DisposeAsync();

    [SetUp]
    public void ForgetTheCalls() => _stub.Reset();

    private async Task<StoredActionRun> RunAsync(string settings)
    {
        var form = await PublishAsync(name: Guid.NewGuid().ToString("N"), change: definition => definition with
        {
            Actions = [new FormAction { Key = Guid.NewGuid(), Type = WebhookActionType.Alias, Name = "Hook", Settings = SettingsObject.Parse(settings) }],
        });
        var entry = await SubmitAsync(form);
        await Pass.RunAsync(CancellationToken.None);
        return (await Runs.ListForEntryAsync(entry.Id)).Single();
    }

    [Test]
    public async Task TheSitesTransport_RefusesLoopback_AndTheStubIsNeverCalled()
    {
        var run = await RunAsync(string.Create(CultureInfo.InvariantCulture, $$"""{"url":"http://127.0.0.1:{{_stub.BaseAddress.Port}}/hook"}"""));

        Assert.Multiple(() =>
        {
            Assert.That(GetRequiredService<IHostEnvironment>().IsDevelopment(), Is.False);
            Assert.That(run.State, Is.EqualTo(ActionRunState.Failed));
            Assert.That(run.LastError?.Code, Is.EqualTo(WebhookActionType.NotAllowed));
            Assert.That(_stub.Calls, Is.Empty);
        });
    }

    [TestCase("Elsewhere", "a host the configuration does not name")]
    [TestCase("Scheme", "a scheme it does not allow")]
    [TestCase("WithCredentials", "an address with a user and password")]
    [TestCase("OtherPort", "another port of an allowed host")]
    [TestCase("Metadata", "the address of a cloud's metadata service")]
    public async Task AnAddressFromASecret_IsHeldToTheSameRules(string secret, string what)
    {
        var run = await RunAsync($$"""{"url":"$JironFormBuilder:Secrets:{{secret}}"}""");

        Assert.Multiple(() =>
        {
            Assert.That(run.State, Is.EqualTo(ActionRunState.Failed), what);
            Assert.That(run.LastError?.Code, Is.EqualTo(WebhookActionType.NotAllowed), what);
            Assert.That(_stub.Calls, Is.Empty);
        });
    }

    /// <inheritdoc />
    protected override void ConfigureHost(IDictionary<string, string> configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        base.ConfigureHost(configuration);
        var port = _stub.BaseAddress.Port;
        UrlOptionListHost.Configure(configuration, port);
        configuration["JironFormBuilder:Webhooks:AllowedHosts:1"] = "169.254.169.254";
        configuration["JironFormBuilder:Secrets:Elsewhere"] = "http://elsewhere.jfb.test/hook";
        configuration["JironFormBuilder:Secrets:Scheme"] = string.Create(CultureInfo.InvariantCulture, $"ftp://127.0.0.1:{port}/hook");
        configuration["JironFormBuilder:Secrets:WithCredentials"] = string.Create(CultureInfo.InvariantCulture, $"http://{"JFB_FAKE"}{(char)58}{"JFB_FAKE"}@127.0.0.1:{port}/hook");
        configuration["JironFormBuilder:Secrets:OtherPort"] = string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{port + 1}/hook");
        configuration["JironFormBuilder:Secrets:Metadata"] = "http://169.254.169.254/latest/meta-data/";
    }
}
