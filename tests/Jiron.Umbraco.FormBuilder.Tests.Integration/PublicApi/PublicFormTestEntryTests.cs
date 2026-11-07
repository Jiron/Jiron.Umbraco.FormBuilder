using System.Net;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Jiron.Umbraco.FormBuilder.Core.Webhooks;
using Jiron.Umbraco.FormBuilder.Testing.Actions;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Webhooks;
using Jiron.Umbraco.FormBuilder.Web.PublicApi;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.PublicApi;

/// <summary>
/// The preview's test entries (ROADMAP 10.3f, ADR-0204): what the frame sends with a ticket is a test entry of the version
/// the ticket names. It sets off nothing (no action, no confirmation mail, no notification Umbraco's webhooks hear, no
/// count) and holds no place, unless its ticket runs actions; the ticket stands in for the puzzle and the spam score; its
/// uploads and its receipt take the ticket too, and a visitor's receipt never finds it. Run actions runs a test entry's
/// actions once, and finds no other entry. A field the draft stores otherwise than its published versions keeps no value
/// in a test entry, and a catalogue row another submission added a moment before stops none.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class PublicFormTestEntryTests : PublicApiTestBase
{
    private static readonly FormAction Always = new() { Key = Guid.Parse("71d00000-0000-4000-8000-000000000204"), Type = StandInEmailActionType.Alias, Name = "Always" };

    private static readonly FormField Email = new() { Key = Guid.Parse("30000000-0000-4000-8000-000000002041"), Type = "email", Alias = "email", Label = new LocalizedText("Email") };

    private PreviewTickets Tickets => GetRequiredService<PreviewTickets>();

    private IEntryRepository Entries => GetRequiredService<IEntryRepository>();

    private IActionRunRepository Runs => GetRequiredService<IActionRunRepository>();

    private IEntryEventRepository Events => GetRequiredService<IEntryEventRepository>();

    private IFormRepository FormRows => GetRequiredService<IFormRepository>();

    private IDailyStatRepository Stats => GetRequiredService<IDailyStatRepository>();

    private TestEntryActions TestEntries => GetRequiredService<TestEntryActions>();

    private HeardEntries Heard => GetRequiredService<HeardEntries>();

    [Test]
    public async Task ATicketsSubmission_IsATestEntry_ThatSetsOffNothing_AndCountsNowhere()
    {
        // An action, an entry limit of one and email verification would each show on a visitor's entry.
        var (key, version) = await DraftAsync(definition => definition with
        {
            Actions = [Always],
            Settings = definition.Settings with
            {
                Availability = definition.Settings.Availability with { MaxEntries = 1 },
                Submission = definition.Settings.Submission with { Verification = new EmailVerification { EmailField = Email.Key } },
            },
        });
        var ticket = Tickets.Issue(key, version).Value;

        var first = await SubmitAsync(key, ticket, """{"name":"Ada","email":"ada@example.com"}""");
        var second = await SubmitAsync(key, ticket, """{"name":"Grace","email":"grace@example.com"}""");
        var form = (await FormRows.GetAsync(key))!;
        var entry = (await Entries.GetBySubmissionAsync(form.Id, first))!;
        var found = await TestEntries.FindAsync(key, first);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        Assert.Multiple(async () =>
        {
            Assert.That((entry.IsTest, entry.State), Is.EqualTo((true, EntryState.Approved)), "a test entry, arrived as confirmed");
            Assert.That(entry.FormVersionId, Is.EqualTo((await FormRows.GetVersionAsync(form.Id, version))!.Info.Id), "the version the ticket names");
            Assert.That(await Entries.GetBySubmissionAsync(form.Id, second), Is.Not.Null, "the limit of one counts no test entry");
            Assert.That(await Runs.ListForEntryAsync(entry.Id), Is.Empty, "no action");
            Assert.That((await Events.ListForEntryAsync(entry.Id)).Select(item => item.Kind), Does.Not.Contain(EntryVerification.RequestedEvent), "no confirmation mail");
            Assert.That(Heard.Heard.OfType<EntrySubmittedNotification>(), Is.Empty, "nothing for Umbraco's webhooks");
            Assert.That(await Stats.ListAsync(form.Id, today.AddDays(-1), today.AddDays(1)), Is.Empty, "no count");
            Assert.That(found, Is.EqualTo(new TestEntry(entry.Key, entry.Number, ActionsRan: false, [Always.Key])).Using<TestEntry>(Same), "the action its rules chose, for Run actions");
        });
    }

    [Test]
    public async Task ATicketThatRunsActions_SetsOffWhatAVisitorsEntryDoes_ButCountsNowhere()
    {
        var (key, version) = await DraftAsync(definition => definition with { Actions = [Always] });
        var (verified, verifiedVersion) = await DraftAsync(definition => definition with
        {
            Actions = [Always],
            Settings = definition.Settings with { Submission = definition.Settings.Submission with { Verification = new EmailVerification { EmailField = Email.Key } } },
        });

        var sent = await SubmitAsync(key, Tickets.Issue(key, version, runsActions: true).Value, """{"name":"Ada"}""");
        var waits = await SubmitAsync(verified, Tickets.Issue(verified, verifiedVersion, runsActions: true).Value, """{"name":"Ada","email":"ada@example.com"}""");
        var form = (await FormRows.GetAsync(key))!;
        var entry = (await Entries.GetBySubmissionAsync(form.Id, sent))!;
        var unverified = (await Entries.GetBySubmissionAsync((await FormRows.GetAsync(verified))!.Id, waits))!;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        Assert.Multiple(async () =>
        {
            Assert.That(entry.IsTest, Is.True);
            Assert.That((await Runs.ListForEntryAsync(entry.Id)).Select(run => run.ActionKey), Is.EqualTo(new[] { Always.Key }));
            Assert.That(Heard.Heard.OfType<EntrySubmittedNotification>().Select(item => item.EntryKey), Is.EqualTo(new[] { entry.Key }));
            Assert.That(await Stats.ListAsync(form.Id, today.AddDays(-1), today.AddDays(1)), Is.Empty, "still no count");
            Assert.That(unverified.State, Is.EqualTo(EntryState.Unverified), "its address is asked to be confirmed, as a visitor's is");
            Assert.That((await Events.ListForEntryAsync(unverified.Id)).Select(item => item.Kind), Does.Contain(EntryVerification.RequestedEvent));
            Assert.That((await TestEntries.FindAsync(verified, waits)) is { ActionsRan: true, AwaitsConfirmation: true }, Is.True, "its actions wait for the confirmation");
            Assert.That((await TestEntries.FindAsync(key, sent))?.AwaitsConfirmation, Is.False);
        });
    }

    [Test]
    public async Task RunActions_RunsATestEntrysActionsOnce_AndFindsNoOtherEntry()
    {
        var (key, version) = await DraftAsync(definition => definition with { Actions = [Always] });
        var sent = await SubmitAsync(key, Tickets.Issue(key, version).Value, """{"name":"Ada"}""");
        var visitors = await PublishAsync(change: definition => definition with { Actions = [Always] });
        var visitorsEntry = await VisitorSubmitsAsync(visitors);

        var run = await TestEntries.RunAsync(key, sent);
        var again = await TestEntries.RunAsync(key, sent);
        var entry = (await Entries.GetBySubmissionAsync((await FormRows.GetAsync(key))!.Id, sent))!;

        Assert.Multiple(async () =>
        {
            Assert.That((run, again), Is.EqualTo((TestEntryRun.Queued, TestEntryRun.AlreadyRan)));
            Assert.That((await Runs.ListForEntryAsync(entry.Id)).Select(item => item.ActionKey), Is.EqualTo(new[] { Always.Key }), "once");
            Assert.That(await TestEntries.FindAsync(key, sent), Is.EqualTo(new TestEntry(entry.Key, entry.Number, ActionsRan: true, [])).Using<TestEntry>(Same));
            Assert.That(await TestEntries.FindAsync(visitors, visitorsEntry), Is.Null, "a visitor's entry is no test entry");
            Assert.That(await TestEntries.RunAsync(visitors, visitorsEntry), Is.EqualTo(TestEntryRun.NotFound));
            Assert.That(await TestEntries.FindAsync(visitors, sent), Is.Null, "another form's test entry");
        });
    }

    [Test]
    public async Task OnAFormThatStoresNoEntries_ATestEntryWhoseActionsWait_IsKeptForRunActions_AndOneWithNoneToWaitForGoes()
    {
        var (bare, bareVersion) = await DraftAsync(definition => definition with
        {
            Settings = definition.Settings with { Submission = definition.Settings.Submission with { Store = EntryStorage.Never } },
        });
        var gone = await SubmitAsync(bare, Tickets.Issue(bare, bareVersion).Value, """{"name":"Alan"}""");
        var (key, version) = await DraftAsync(definition => definition with
        {
            Actions = [Always],
            Settings = definition.Settings with { Submission = definition.Settings.Submission with { Store = EntryStorage.Never } },
        });

        var waits = await SubmitAsync(key, Tickets.Issue(key, version).Value, """{"name":"Ada"}""");
        var runs = await SubmitAsync(key, Tickets.Issue(key, version, runsActions: true).Value, """{"name":"Grace"}""");
        var form = (await FormRows.GetAsync(key))!;
        var kept = await Entries.GetBySubmissionAsync(form.Id, waits);
        var found = await TestEntries.FindAsync(key, waits);
        var running = (await Entries.GetBySubmissionAsync(form.Id, runs))!;

        Assert.Multiple(async () =>
        {
            Assert.That(kept?.IsTest, Is.True, "kept while its actions wait");
            Assert.That(found, Is.EqualTo(new TestEntry(kept!.Key, kept.Number, ActionsRan: false, [Always.Key])).Using<TestEntry>(Same));
            Assert.That((await Runs.ListForEntryAsync(running.Id)).Select(run => run.ActionKey), Is.EqualTo(new[] { Always.Key }), "kept until its actions ran, as a visitor's is");
            Assert.That(await TestEntries.RunAsync(key, waits), Is.EqualTo(TestEntryRun.Queued));
            Assert.That(await Entries.GetBySubmissionAsync((await FormRows.GetAsync(bare))!.Id, gone), Is.Null, "no action to wait for: let go at once");
        });
    }

    [Test]
    public async Task TheTicket_StandsInForThePuzzleAndTheSpamScore()
    {
        var settings = GetRequiredService<IFormBuilderSettingsService>();
        var current = await settings.GetAsync<ProtectionSettings>();
        await settings.SaveAsync(current.Value with { Level = ProtectionLevel.ProofOfWork, ProofBits = 8 }, current.Version, null);
        var (key, version) = await DraftAsync();
        var ticket = Tickets.Issue(key, version).Value;

        // Sent at once, with the honeypot filled, and no puzzle solved: a visitor's would be refused or go to Spam.
        var sent = await SubmitAsync(key, ticket, """{"name":"Ada"}""", members: new JsonObject { ["honeypot"] = "Buy now" });
        var entry = (await Entries.GetBySubmissionAsync((await FormRows.GetAsync(key))!.Id, sent))!;

        Assert.That((entry.IsTest, entry.State, entry.SpamScore), Is.EqualTo((true, EntryState.Approved, 0)));
    }

    [Test]
    public async Task UploadsAndTheReceipt_TakeTheTicket_AndAVisitorsReceiptFindsNoTestEntry()
    {
        var file = new FormField { Key = Guid.Parse("30000000-0000-4000-8000-000000002042"), Type = "file", Alias = "cv", Label = new LocalizedText("CV") };
        var (key, version) = await DraftAsync(definition => WithFields(definition, NameField with { Required = true }, file));
        var ticket = Tickets.Issue(key, version).Value;
        var challenge = await ChallengeFromFrameAsync(key, ticket);

        using var upload = await FromFrameAsync(HttpMethod.Post, $"{Root}/forms/{key}/uploads?fieldKey={file.Key}", ticket, request =>
        {
            var parts = new MultipartFormDataContent { { new StringContent(challenge), "challenge" } };
            parts.Add(new ByteArrayContent([.. "%PDF-1.7"u8, 0x0A, .. "1 0 obj"u8]) { Headers = { { "Content-Type", "application/pdf" } } }, "file", "sample.pdf");
            request.Content = parts;
        });
        Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.OK), await upload.Content.ReadAsStringAsync());
        var token = (await JsonAsync(upload))["token"]!.GetValue<string>();
        var sent = await SubmitAsync(key, ticket, $$"""{"name":"Ada","cv":["{{token}}"]}""", challenge);
        var receiptBody = $$"""{"submissionId":"{{sent}}","challenge":"{{challenge}}"}""";
        using var receipt = await FromFrameAsync(HttpMethod.Post, $"{Root}/forms/{key}/receipt", ticket, request => request.Content = Json(receiptBody));
        // Published, the form serves visitors: a visitor's receipt for the test entry's submission finds no entry.
        await SaveAndPublishAsync(key, definition => Headless(definition));
        var visitorsBody = $$"""{"submissionId":"{{sent}}","challenge":"{{await ChallengeAsync(key)}}"}""";
        using var visitors = await SendAsync(HttpMethod.Post, $"{Root}/forms/{key}/receipt", request => request.Content = Json(visitorsBody));
        var entry = (await Entries.GetBySubmissionAsync((await FormRows.GetAsync(key))!.Id, sent))!;

        Assert.Multiple(async () =>
        {
            Assert.That(upload.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(entry.IsTest, Is.True);
            Assert.That(receipt.StatusCode, Is.EqualTo(HttpStatusCode.OK), await receipt.Content.ReadAsStringAsync());
            Assert.That((await JsonAsync(receipt))["receipt"]?["submissionId"]?.GetValue<string>(), Is.EqualTo(sent.ToString()));
            Assert.That((visitors.StatusCode, (await JsonAsync(visitors))["code"]?.GetValue<string>()), Is.EqualTo((HttpStatusCode.NotFound, "not_found")), "a visitor's receipt finds no test entry");
        });
    }

    [Test]
    public async Task AFieldTheDraftStoresOtherwise_KeepsNoValueInATestEntry_AndARowAddedAMomentBefore_StopsNone()
    {
        var guests = new FormField { Key = Guid.Parse("30000000-0000-4000-8000-000000002043"), Type = "number", Alias = "guests", Label = new LocalizedText("Guests") };
        var note = new FormField { Key = Guid.Parse("30000000-0000-4000-8000-000000002044"), Type = "text", Alias = "note", Label = new LocalizedText("Note") };
        var key = await PublishAsync(fields: [NameField, guests]);
        var draft = (await Forms.GetDraftAsync(key))!;
        // The number becomes a text, which stores its values as text, and a new field joins it.
        await Forms.SaveDraftAsync(key, draft.Form.ConcurrencyVersion, WithFields(draft.Definition!, NameField, guests with { Type = "text" }, note), null, Editor);
        var version = (await Forms.GetDraftAsync(key))!.DraftVersion;
        var ticket = Tickets.Issue(key, version).Value;

        var sent = await SubmitAsync(key, ticket, """{"name":"Ada","guests":"a few","note":"first"}""");
        // A second one reads the catalogue as it was before the first added its row, and adds it again.
        GetRequiredService<StaleCatalogue>().StaleReads = 1;
        var again = await SubmitAsync(key, ticket, """{"name":"Grace","guests":"many","note":"second"}""");
        var form = (await FormRows.GetAsync(key))!;
        var rows = (await FormRows.ListFieldsAsync(form.Id)).ToDictionary(row => row.Id, row => row.Values.FieldKey);
        var entry = (await Entries.GetBySubmissionAsync(form.Id, sent))!;
        var values = await Entries.ListValuesAsync([entry.Id, (await Entries.GetBySubmissionAsync(form.Id, again))!.Id]);

        Assert.Multiple(() =>
        {
            Assert.That(entry.IsTest, Is.True);
            Assert.That(values.Where(value => value.EntryId == entry.Id).Select(value => rows[value.FieldId]), Is.EquivalentTo(new[] { NameField.Key, note.Key }), "no value for the field stored otherwise");
            Assert.That(values.Count(value => rows[value.FieldId] == note.Key), Is.EqualTo(2), "the new field's row, added once, holds both");
            Assert.That(rows.Values.Count(field => field == note.Key), Is.EqualTo(1));
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.Services.AddSingleton<HeardEntries>();
        builder.AddNotificationAsyncHandler<EntrySubmittedNotification, HeardEntriesHandler>();
        builder.Services.AddSingleton(provider => StaleCatalogue.Over(ActivatorUtilities.CreateInstance<FormRepository>(provider)));
        builder.Services.AddUnique<IFormRepository>(provider => (IFormRepository)provider.GetRequiredService<StaleCatalogue>());
    }

    private static bool Same(TestEntry? one, TestEntry? other) =>
        one is not null && other is not null && one.Key == other.Key && one.Number == other.Number && one.ActionsRan == other.ActionsRan && one.AwaitsConfirmation == other.AwaitsConfirmation && one.Actions.SequenceEqual(other.Actions);

    // A form never published whose draft holds the name and an email field on one page; its draft's version.
    private async Task<(Guid Key, int Version)> DraftAsync(Func<FormDefinition, FormDefinition>? change = null)
    {
        var created = await Forms.CreateAsync(new NewFormRequest("Tested", UserKey: Editor));
        var draft = (await Forms.GetDraftAsync(created.Key))!;
        var definition = (change ?? (d => d))(WithFields(draft.Definition!, NameField with { Required = true }, Email));
        await Forms.SaveDraftAsync(created.Key, draft.Form.ConcurrencyVersion, definition, null, Editor);
        return (created.Key, (await Forms.GetDraftAsync(created.Key))!.DraftVersion);
    }

    // The frame's submission with the ticket, and a challenge it asked for with it; its submission id.
    private async Task<Guid> SubmitAsync(Guid key, string ticket, string values, string? challenge = null, JsonObject? members = null)
    {
        var submissionId = Guid.NewGuid();
        var posted = new JsonObject
        {
            ["submissionId"] = submissionId.ToString(),
            ["revision"] = 1,
            ["values"] = JsonNode.Parse(values),
            ["challenge"] = challenge ?? await ChallengeFromFrameAsync(key, ticket),
        };
        foreach (var (name, value) in members ?? [])
        {
            posted[name] = value?.DeepClone();
        }

        using var answer = await FromFrameAsync(HttpMethod.Post, $"{Root}/forms/{key}/entries", ticket, request => request.Content = Json(posted.ToJsonString()));
        Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.OK), await answer.Content.ReadAsStringAsync());
        return submissionId;
    }

    // A visitor's submission to a published form; its submission id.
    private async Task<Guid> VisitorSubmitsAsync(Guid key)
    {
        var submissionId = Guid.NewGuid();
        var body = $$"""{"submissionId":"{{submissionId}}","revision":1,"values":{"name":"Ada"},"challenge":"{{await ChallengeAsync(key)}}"}""";
        using var answer = await SendAsync(HttpMethod.Post, $"{Root}/forms/{key}/entries", request => request.Content = Json(body));
        Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.OK), await answer.Content.ReadAsStringAsync());
        return submissionId;
    }

    private async Task<string> ChallengeFromFrameAsync(Guid key, string ticket)
    {
        using var answer = await FromFrameAsync(HttpMethod.Post, $"{Root}/forms/{key}/challenge", ticket, request => request.Content = Json("{}"));
        Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.OK), await answer.Content.ReadAsStringAsync());
        return (await JsonAsync(answer))["challenge"]!.GetValue<string>();
    }

    // A request as the preview frame sends it: from the origin null, with the ticket.
    private Task<HttpResponseMessage> FromFrameAsync(HttpMethod method, string path, string ticket, Action<HttpRequestMessage> prepare) =>
        SendAsync(method, path, request =>
        {
            request.Headers.TryAddWithoutValidation("Origin", "null");
            request.Headers.TryAddWithoutValidation("Authorization", $"{PreviewTickets.Scheme} {ticket}");
            prepare(request);
        });

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    // The site's form repository, every call passed on, except the catalogue reads a test names: they find it empty, as
    // a read made before another submission added its rows would. DispatchProxy derives the proxy, so it stays unsealed.
#pragma warning disable CA1852
    internal class StaleCatalogue : DispatchProxy
    {
        private IFormRepository _inner = null!;

        /// <summary>How many of the next catalogue reads find it empty.</summary>
        public int StaleReads { get; set; }

        /// <summary>A repository over <paramref name="inner"/>.</summary>
        public static StaleCatalogue Over(IFormRepository inner)
        {
            var proxy = (StaleCatalogue)(object)Create<IFormRepository, StaleCatalogue>();
            proxy._inner = inner;
            return proxy;
        }

        /// <inheritdoc />
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            if (targetMethod.Name == nameof(IFormRepository.ListFieldsAsync) && StaleReads > 0)
            {
                StaleReads--;
                return Task.FromResult<IReadOnlyList<CatalogueField>>([]);
            }

            try
            {
                return targetMethod.Invoke(_inner, args);
            }
            catch (TargetInvocationException exception) when (exception.InnerException is { } thrown)
            {
                ExceptionDispatchInfo.Throw(thrown);
                throw;
            }
        }
    }
#pragma warning restore CA1852
}
