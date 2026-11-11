using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Actions;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Extending;

/// <summary>What the tests' handlers of the entry notifications do and heard.</summary>
internal sealed class SiteHandlers
{
    public List<object> Heard { get; } = [];

    public Action<EntrySubmittingNotification>? Submitting { get; set; }

    public Func<EntryDeletingNotification, Task>? Deleting { get; set; }
}

/// <summary>A site's handler of the entry notifications, for the tests.</summary>
internal sealed class SiteHandler(SiteHandlers handlers)
    : INotificationAsyncHandler<EntrySubmittingNotification>, INotificationAsyncHandler<EntryStateChangedNotification>, INotificationAsyncHandler<EntryDeletingNotification>, INotificationAsyncHandler<EntryDeletedNotification>
{
    public Task HandleAsync(EntrySubmittingNotification notification, CancellationToken cancellationToken)
    {
        handlers.Heard.Add(notification);
        handlers.Submitting?.Invoke(notification);
        return Task.CompletedTask;
    }

    public Task HandleAsync(EntryStateChangedNotification notification, CancellationToken cancellationToken)
    {
        handlers.Heard.Add(notification);
        return Task.CompletedTask;
    }

    public async Task HandleAsync(EntryDeletingNotification notification, CancellationToken cancellationToken)
    {
        handlers.Heard.Add(notification);
        if (handlers.Deleting is { } deleting)
        {
            await deleting(notification);
        }
    }

    public Task HandleAsync(EntryDeletedNotification notification, CancellationToken cancellationToken)
    {
        handlers.Heard.Add(notification);
        return Task.CompletedTask;
    }
}

/// <summary>
/// The entry notifications and services of section 4.9 a site's code uses (ROADMAP 15.1, ADR-0233): a handler that refuses
/// an entry in its own words, the state changes and deletes it hears of, and the entries it reads, creates and moves.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class ExtensionPointTests : ActionTestBase
{
    private static readonly FormField Topic = new()
    {
        Key = Guid.Parse("30000000-0000-4000-8000-000000001501"),
        Type = "dropdown",
        Alias = "topic",
        Label = new LocalizedText([new(string.Empty, "Topic"), new("de", "Thema")]),
        Options = new FieldOptions { Items = [new FieldOption { Value = "sales", Label = new LocalizedText([new(string.Empty, "Sales"), new("de", "Vertrieb")]) }] },
    };

    private static readonly FormField Secret = new()
    {
        Key = Guid.Parse("30000000-0000-4000-8000-000000001502"),
        Type = "text",
        Alias = "secret",
        Label = new LocalizedText([new(string.Empty, "Secret")]),
        Sensitive = true,
    };

    private SiteHandlers Handlers => GetRequiredService<SiteHandlers>();

    private IFormBuilderEntryService Service => GetRequiredService<IFormBuilderEntryService>();

    private Task<Guid> FormAsync(ApprovalMode approval = ApprovalMode.Automatic, params FormField[] fields) => PublishAsync(
        name: Guid.NewGuid().ToString("N"),
        change: definition => definition with { Settings = definition.Settings with { Submission = definition.Settings.Submission with { Approval = approval } } },
        fields: fields.Length == 0 ? [NameField] : fields);

    private async Task<(HttpStatusCode Status, JsonNode Body)> PostAsync(Guid form, string values)
    {
        using var content = new StringContent($$"""{"revision":1,"values":{{values}}}""", Encoding.UTF8, "application/json");
        using var answer = await SendAsync(HttpMethod.Post, $"{Root}/forms/{form}/entries", request => request.Content = content);
        return (answer.StatusCode, await JsonAsync(answer));
    }

    private async Task<int> CountAsync(Guid form) => (await Entries.ListIdsAsync((await FormRows.GetAsync(form))!.Id, 1000)).Count;

    [Test]
    public async Task AHandlersFieldError_RefusesTheEntry_InItsOwnWords()
    {
        Handlers.Submitting = notification =>
        {
            if (notification.AnswerOf("name")?.Values.FirstOrDefault()?.Text == "Mallory")
            {
                notification.AddError("name", "We know this name already.");
            }
        };
        var form = await FormAsync();

        var (status, body) = await PostAsync(form, """{"name":"Mallory"}""");

        var error = body["errors"]!.AsArray().Single()!;
        Assert.Multiple(async () =>
        {
            Assert.That(status, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
            Assert.That(body["code"]!.GetValue<string>(), Is.EqualTo("validation_failed"));
            Assert.That(error["code"]!.GetValue<string>(), Is.EqualTo("custom"));
            Assert.That(error["path"]!.GetValue<string>(), Is.EqualTo("name"));
            Assert.That(error["text"]!.GetValue<string>(), Is.EqualTo("We know this name already."));
            Assert.That(await CountAsync(form), Is.Zero, "nothing is stored");
        });
    }

    [Test]
    public async Task AFormError_AndACancelWithItsMessage_ShowAtTheForm_AndABareCancelWithoutWords()
    {
        var form = await FormAsync();

        Handlers.Submitting = notification => notification.AddFormError("The shop is closed today.");
        var (_, added) = await PostAsync(form, """{"name":"Ada"}""");
        Handlers.Submitting = notification => notification.CancelOperation(new EventMessage("Shop", "Come back tomorrow.", EventMessageType.Error));
        var (_, cancelled) = await PostAsync(form, """{"name":"Ada"}""");
        Handlers.Submitting = notification => notification.Cancel = true;
        var (status, bare) = await PostAsync(form, """{"name":"Ada"}""");

        Assert.Multiple(async () =>
        {
            Assert.That(added["errors"]!.AsArray().Single()!.ToJsonString(), Does.Contain("\"path\":\"\"").And.Contain("The shop is closed today."));
            Assert.That(cancelled["errors"]!.AsArray().Single()!["text"]!.GetValue<string>(), Is.EqualTo("Come back tomorrow."));
            Assert.That(status, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
            Assert.That(bare["errors"]!.AsArray().Single()!["code"]!.GetValue<string>(), Is.EqualTo("custom"));
            Assert.That(bare["errors"]!.AsArray().Single()!["text"], Is.Null, "no words: the page shows a check's own");
            Assert.That(await CountAsync(form), Is.Zero);
        });
    }

    [Test]
    public async Task AFailingHandler_HasNoWord_AndTheEntryIsStored()
    {
        Handlers.Submitting = _ => throw new InvalidOperationException("the site's handler failed");
        var form = await FormAsync();

        var (status, _) = await PostAsync(form, """{"name":"Ada"}""");

        Assert.Multiple(async () =>
        {
            Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(await CountAsync(form), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task TheNotification_TellsTheAnswersCultureAndWhereTheEntryCameFrom_AndAPageCheckRaisesNone()
    {
        var form = await FormAsync();
        var page = (await Forms.GetDraftAsync(form))!.Definition!.Pages[0].Key;
        using (var check = new StringContent($$$"""{"revision":1,"pageKey":"{{{page}}}","values":{"name":"Ada"}}""", Encoding.UTF8, "application/json"))
        using (var answer = await SendAsync(HttpMethod.Post, $"{Root}/forms/{form}/validate", request => request.Content = check))
        {
            Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), await answer.Content.ReadAsStringAsync());
        }

        var checkedPage = Handlers.Heard.OfType<EntrySubmittingNotification>().Count();
        await PostAsync(form, """{"name":"Ada"}""");
        await Service.CreateAsync(new FormBuilderNewEntry { Form = form, Answers = new Dictionary<string, FormBuilderNewAnswer> { ["name"] = new("Grace") } });

        var heard = Handlers.Heard.OfType<EntrySubmittingNotification>().ToList();
        Assert.Multiple(() =>
        {
            Assert.That(checkedPage, Is.Zero, "a page's check stores nothing");
            Assert.That(heard, Has.Count.EqualTo(2));
            Assert.That(heard[0].FormKey, Is.EqualTo(form));
            Assert.That(heard[0].AnswerOf("name")!.Values.Single().Text, Is.EqualTo("Ada"));
            Assert.That(heard[0].AnswerOf("nothing"), Is.Null);
            Assert.That(heard[0].Culture, Is.Not.Empty);
            Assert.That(heard[0].IsFromCode, Is.False);
            Assert.That(heard[0].IsTest, Is.False);
            Assert.That(heard[1].IsFromCode, Is.True);
            Assert.Throws<ArgumentException>(() => heard[0].AddError("nothing", "No such field."));
            Assert.Throws<ArgumentException>(() => heard[0].AddError("name", " "));
            Assert.Throws<ArgumentException>(() => heard[0].AddError("name", "A row for a field in no group.", row: 0));
        });
    }

    [Test]
    public async Task AStateChange_IsToldWithWhereItCameFrom_AtOnceForCode_AndForAUserOnceItStands()
    {
        var form = await FormAsync(ApprovalMode.Manual);
        var first = await SubmitAsync(form);
        var second = await SubmitAsync(form);

        var moved = await Service.ChangeStateAsync(first.Key, FormBuilderEntryState.Approved);
        var again = await Service.ChangeStateAsync(first.Key, FormBuilderEntryState.Approved);
        await GetRequiredService<EntryStateChanges>().ChangeAsync(form, [second.Key], EntryState.Rejected, Editor);
        await Pass.RunAsync(CancellationToken.None);
        var heardInside = Handlers.Heard.OfType<EntryStateChangedNotification>().Count(change => change.EntryKey == second.Key);
        Clock.Advance(EntrySpamFolder.UndoWindow);
        await Pass.RunAsync(CancellationToken.None);

        var changes = Handlers.Heard.OfType<EntryStateChangedNotification>().ToList();
        Assert.Multiple(() =>
        {
            Assert.That(moved, Is.True);
            Assert.That(again, Is.False, "it is approved already");
            Assert.That(changes[0].EntryKey, Is.EqualTo(first.Key));
            Assert.That((changes[0].From, changes[0].State), Is.EqualTo(("submitted", "approved")));
            Assert.That(heardInside, Is.Zero, "a user's move waits for its undo window");
            Assert.That(changes.Single(change => change.EntryKey == second.Key).State, Is.EqualTo("rejected"));
            Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Service.ChangeStateAsync(first.Key, FormBuilderEntryState.Unverified));
        });
    }

    [Test]
    public async Task AnEntryAboutToBeDeleted_IsToldBefore_WhileItCanStillBeRead()
    {
        var form = await FormAsync();
        var entry = await SubmitAsync(form);
        FormBuilderEntry? readWhileDeleting = null;
        Handlers.Deleting = async notification => readWhileDeleting = await Service.GetAsync(notification.EntryKey);

        await GetRequiredService<EntryDeletions>().DeleteAsync(form, [entry.Key], Editor);

        var told = Handlers.Heard.Where(item => item is EntryDeletingNotification or EntryDeletedNotification).ToList();
        Assert.Multiple(async () =>
        {
            Assert.That(told.Select(item => item.GetType().Name), Is.EqualTo(new[] { nameof(EntryDeletingNotification), nameof(EntryDeletedNotification) }));
            Assert.That(readWhileDeleting?.Answers.Single().Values, Is.EqualTo(new[] { "Ada" }));
            Assert.That(await Service.GetAsync(entry.Key), Is.Null);
        });
    }

    [Test]
    public async Task CodeCreatesAnEntry_ThroughThePipeline_AndARetryAnswersTheSameEntry()
    {
        var form = await FormAsync(ApprovalMode.Automatic, NameField, Topic, Secret);
        var submission = Guid.NewGuid();
        var request = new FormBuilderNewEntry
        {
            Form = form,
            SubmissionId = submission,
            Answers = new Dictionary<string, FormBuilderNewAnswer> { ["name"] = new("Ada"), ["topic"] = new("sales"), ["secret"] = new("s3cret") },
        };

        var created = await Service.CreateAsync(request);
        var retried = await Service.CreateAsync(request);
        var refused = await Service.CreateAsync(new FormBuilderNewEntry { Form = form, Answers = new Dictionary<string, FormBuilderNewAnswer> { ["topic"] = new("nothing") } });
        var missing = await Service.CreateAsync(new FormBuilderNewEntry { Form = Guid.NewGuid() });

        var entry = created.Entry!;
        Assert.Multiple(() =>
        {
            Assert.That(entry.State, Is.EqualTo(FormBuilderEntryState.Approved));
            Assert.That(entry.Number, Is.EqualTo(1));
            Assert.That(entry.Reference, Does.EndWith("00001"));
            Assert.That(entry.Answers.Select(answer => (answer.Field, answer.Values.Single(), answer.IsSensitive)), Is.EqualTo(new[] { ("name", "Ada", false), ("topic", "sales", false), ("secret", "s3cret", true) }));
            Assert.That(retried.Replayed, Is.True);
            Assert.That(retried.Entry!.Key, Is.EqualTo(entry.Key));
            Assert.That(refused.Entry, Is.Null);
            Assert.That(refused.Refusal, Is.EqualTo("validation_failed"));
            Assert.That(refused.Problems.Select(problem => problem.Path), Does.Contain("topic"));
            Assert.That(missing.Refusal, Is.EqualTo("form_not_found"));
        });
    }

    [Test]
    public async Task AFormForMembers_TakesAnEntryFromCode_OnlyForAMember()
    {
        var form = await PublishAsync(
            name: Guid.NewGuid().ToString("N"),
            change: definition => definition with { Settings = definition.Settings with { Availability = definition.Settings.Availability with { Access = MemberAccess.Members } } });

        var anonymous = await Service.CreateAsync(new FormBuilderNewEntry { Form = form, Answers = new Dictionary<string, FormBuilderNewAnswer> { ["name"] = new("Ada") } });
        var member = Guid.NewGuid();
        var forMember = await Service.CreateAsync(new FormBuilderNewEntry { Form = form, Member = member, Answers = new Dictionary<string, FormBuilderNewAnswer> { ["name"] = new("Ada") } });

        Assert.Multiple(() =>
        {
            Assert.That(anonymous.Refusal, Is.EqualTo("member_required"));
            Assert.That(forMember.Entry?.Member, Is.EqualTo(member));
        });
    }

    [Test]
    public async Task AFormsEntries_ListNewestFirst_APageAtATime_ByState()
    {
        var form = await FormAsync(ApprovalMode.Manual);
        var made = new List<StoredEntry>();
        for (var index = 0; index < 3; index++)
        {
            Clock.Advance(TimeSpan.FromMinutes(1));
            made.Add(await SubmitAsync(form, $$"""{"name":"Person {{index}}"}"""));
        }

        await Service.ChangeStateAsync(made[0].Key, FormBuilderEntryState.Approved);
        var first = (await Service.ListAsync(form, new FormBuilderEntryQuery { Take = 2 }))!;
        var second = (await Service.ListAsync(form, new FormBuilderEntryQuery { Take = 2, After = first.Next }))!;
        var approved = (await Service.ListAsync(form, new FormBuilderEntryQuery { States = [FormBuilderEntryState.Approved] }))!;

        Assert.Multiple(async () =>
        {
            Assert.That(first.Entries.Select(entry => entry.Number), Is.EqualTo(new[] { 3, 2 }));
            Assert.That(second.Entries.Select(entry => entry.Number), Is.EqualTo(new[] { 1 }));
            Assert.That(second.Next, Is.Null);
            Assert.That(approved.Entries.Single().Key, Is.EqualTo(made[0].Key));
            Assert.That(await Service.ListAsync(Guid.NewGuid(), new FormBuilderEntryQuery()), Is.Null);
            Assert.ThrowsAsync<ArgumentException>(() => Service.ListAsync(form, new FormBuilderEntryQuery { After = "not a cursor" }));
            Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Service.ListAsync(form, new FormBuilderEntryQuery { Take = 0 }));
        });
    }

    [Test]
    public async Task APage_ReadsApprovedEntriesOnly_WithoutSensitiveAnswers_InTheCultureAsked()
    {
        var form = await FormAsync(ApprovalMode.Manual, NameField, Topic, Secret);
        var approved = await SubmitAsync(form, """{"name":"Ada","topic":"sales","secret":"s3cret"}""");
        await SubmitAsync(form, """{"name":"Waiting","topic":"sales"}""");
        await Service.ChangeStateAsync(approved.Key, FormBuilderEntryState.Approved);
        var reader = GetRequiredService<IFormBuilderEntryReader>();
        await GetRequiredService<ILanguageService>().CreateAsync(new Language("de-DE", "German"), Constants.Security.SuperUserKey);
        var alias = (await FormRows.GetAsync(form))!.Alias;

        var read = (await reader.ReadApprovedAsync(alias.ToUpperInvariant(), new FormBuilderEntryReadRequest { Culture = "de-de" }))!;
        var chosen = (await reader.ReadApprovedAsync(form.ToString(), new FormBuilderEntryReadRequest { Fields = ["topic", "secret"] }))!;

        var entry = read.Entries.Single();
        Assert.Multiple(async () =>
        {
            Assert.That(entry.Number, Is.EqualTo(approved.Number));
            Assert.That(entry.Answers.Select(answer => answer.Field), Is.EqualTo(new[] { "name", "topic" }), "never the sensitive one");
            Assert.That(entry.Answers[1].Label, Is.EqualTo("Thema"));
            Assert.That(entry.Answers[1].Texts, Is.EqualTo(new[] { "Vertrieb" }));
            Assert.That(chosen.Entries.Single().Answers.Select(answer => answer.Field), Is.EqualTo(new[] { "topic" }));
            Assert.That(await reader.ReadApprovedAsync("no-such-form", new FormBuilderEntryReadRequest()), Is.Null);
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        base.CustomTestSetup(builder);
        builder.Services.AddSingleton<SiteHandlers>();
        builder.AddNotificationAsyncHandler<EntrySubmittingNotification, SiteHandler>();
        builder.AddNotificationAsyncHandler<EntryStateChangedNotification, SiteHandler>();
        builder.AddNotificationAsyncHandler<EntryDeletingNotification, SiteHandler>();
        builder.AddNotificationAsyncHandler<EntryDeletedNotification, SiteHandler>();
    }
}
