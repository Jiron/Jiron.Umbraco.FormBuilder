using System.Collections.Concurrent;
using System.Net;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.AI;
using Jiron.Umbraco.FormBuilder.AI.Chat;
using Jiron.Umbraco.FormBuilder.AI.Drafting;
using Jiron.Umbraco.FormBuilder.AI.Marks;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.AI.Drafting;

/// <summary>
/// Draft a form with AI (ROADMAP 15.3, ADR-0018) through the management API and Umbraco.AI's chat on the fake provider:
/// the spike's proof that a description becomes a form definition the save checks accept, created as a draft that is
/// never published and is marked for review; and every way it refuses.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class DraftApiTests : AITestBase
{
    private const string Description = "Summer party sign-up\nWhich day suits you? (Friday / Saturday)\nAnything we should know about your food?";

    [Test]
    public async Task ADescription_BecomesADraftForm_ThroughUmbracoAIsChat_NeverPublished_AndMarkedForReview()
    {
        var answer = await CallAsync("designer", HttpMethod.Post, "ai/draft", new { text = Description });

        Assert.That(answer.Status, Is.EqualTo(HttpStatusCode.Created), answer.ToString());
        var key = Guid.Parse(answer.Body!["key"]!.GetValue<string>());
        var draft = await GetRequiredService<IFormBuilderFormService>().GetDraftAsync(key);
        var fields = draft!.Definition!.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields).ToList();
        var versions = await GetRequiredService<IFormBuilderFormVersionService>().ListVersionsAsync(key, includeDrafts: true, new PageRequest(0, 10));
        Assert.Multiple(() =>
        {
            Assert.That(answer.Created, Is.EqualTo(key.ToString()));
            Assert.That(draft.Form.Name, Is.EqualTo("Summer party sign-up"));
            Assert.That(draft.Form.IsPublished, Is.False, "never published");
            Assert.That(fields.Select(field => (field.Type, field.Label?.Default)), Is.SupersetOf(new[]
            {
                ("text", (string?)"Name"),
                ("email", "Email address"),
                ("radio", "Which day suits you?"),
                ("textarea", "Anything we should know about your food?"),
            }));
            Assert.That(fields.Single(field => field.Type == "radio").Options!.Items.Select(option => option.Label?.Default), Is.EqualTo(new[] { "Friday", "Saturday" }));
            Assert.That(fields.Single(field => field.Type == "email").Required, Is.True);
            Assert.That(fields.Select(field => field.Alias).OfType<string>(), Is.Unique);
            Assert.That(versions!.Items.Single(entry => entry.Version.Number == draft.DraftVersion).Version.Comment, Is.EqualTo(FormBuilderAI.DraftedComment));
            Assert.That(answer.Body!["draftVersion"]!.GetValue<int>(), Is.EqualTo(draft.DraftVersion));
        });
        Assert.That(await GetRequiredService<DraftMarks>().GetAsync(key, CancellationToken.None), Is.EqualTo(draft.DraftVersion), "the mark the designer reads");
        Assert.That(Journal.Entries, Has.Some.EqualTo($"answered {AIPrompts.DraftSchemaName}"), "Umbraco.AI's chat reached the fake model with the draft's schema");
        Assert.That(Journal.Entries, Has.Some.Contains("Which day suits you?"), "the editor's text reached it");
    }

    [Test]
    public async Task ADraft_IsReadThroughTheSaveChecks_AndTheNewFormDefaultsApply()
    {
        var script = """
            draft:{"name":"Contact","pages":[{"title":null,"groups":[{"title":null,"fields":[
            {"type":"text","label":"Name","help":null,"placeholder":null,"required":true,"options":[]},
            {"type":"text","label":"Name","help":null,"placeholder":null,"required":false,"options":[]},
            {"type":"date","label":"Date","help":null,"placeholder":null,"required":false,"options":[]},
            {"type":"radio","label":"One option only","help":null,"placeholder":null,"required":false,"options":["Only"]},
            {"type":"signature","label":"Sign here","help":null,"placeholder":null,"required":true,"options":[]},
            {"type":"paragraph","label":"Intro","help":"Tell us <b>anything</b>.","placeholder":null,"required":false,"options":[]}
            ]}]}]}
            """;
        var answer = await CallAsync("designer", HttpMethod.Post, "ai/draft", new { text = script });

        Assert.That(answer.Status, Is.EqualTo(HttpStatusCode.Created), answer.ToString());
        var draft = await GetRequiredService<IFormBuilderFormService>().GetDraftAsync(Guid.Parse(answer.Body!["key"]!.GetValue<string>()));
        var fields = draft!.Definition!.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(fields.Select(field => field.Alias), Is.EqualTo(new[] { "name", "name2", "date2", "oneOptionOnly", null }), "free aliases, never a reserved one");
            Assert.That(fields[3].Type, Is.EqualTo("text"), "a choice of one option stays a question");
            Assert.That(fields.Any(field => field.Type == "signature"), Is.False, "a type a draft may not use is left out");
            Assert.That(answer.Body!["left"]!.GetValue<int>(), Is.EqualTo(1));
            Assert.That(fields[4].Settings!.Value.GetRawText(), Does.Contain("&lt;b&gt;anything&lt;/b&gt;"), "a paragraph holds the AI's text, never its markup");
        });
    }

    [TestCase("say:Sorry, I cannot help with that.", "ai_reply_unusable")]
    [TestCase("say:{\"name\":\"Empty\",\"pages\":[]}", "ai_reply_unusable")]
    [TestCase("error:provider", "ai_provider_failed")]
    [TestCase("error:timeout", "ai_provider_failed")]
    public async Task AReplyThatIsNoForm_OrAProvidersFailure_CreatesNothing(string text, string code)
    {
        var before = await FormCountAsync();

        var answer = await CallAsync("designer", HttpMethod.Post, "ai/draft", new { text });

        Assert.Multiple(async () =>
        {
            Assert.That(answer.Code, Is.EqualTo(code), answer.ToString());
            Assert.That((int)answer.Status, Is.EqualTo(code == "ai_provider_failed" ? 502 : 422));
            Assert.That(answer.Text, Does.Not.Contain("JFB_FAKE"), "the provider's own message never reaches the answer");
            Assert.That(await FormCountAsync(), Is.EqualTo(before), "no form");
        });
    }

    [Test]
    public async Task OnlyAUserWhoMayDesignWhereTheFormLands_MayDraftOne()
    {
        var folder = await FolderAsync("Restricted");
        await RestrictAsync(folder, "hr", Core.Access.FormBuilderPermissions.ViewEntries);

        Assert.Multiple(async () =>
        {
            Assert.That((await CallAsync(null, HttpMethod.Post, "ai/draft", new { text = Description })).Status, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That((await CallAsync("viewer", HttpMethod.Post, "ai/draft", new { text = Description })).Outcome, Is.EqualTo("403 forbidden"));
            Assert.That((await CallAsync("outsider", HttpMethod.Post, "ai/draft", new { text = Description })).Status, Is.EqualTo(HttpStatusCode.Forbidden));
            Assert.That((await CallAsync("designer", HttpMethod.Post, "ai/draft", new { text = Description, folderKey = folder })).Status, Is.EqualTo(HttpStatusCode.NotFound), "a restricted folder the designer is not granted");
            Assert.That((await CallAsync("designer", HttpMethod.Post, "ai/draft", new { text = new string('x', 20_001) })).Status, Is.EqualTo(HttpStatusCode.BadRequest));
            Assert.That(Journal.Entries, Has.None.StartsWith("request:"), "nothing reached the AI");
        });
    }

    [Test]
    public async Task ADraftTheSaveChecksRefuse_IsRefusedAsNoForm()
    {
        // The reply's reader cuts every text to what a save takes, so only a draft made past it reaches this guard.
        var label = new string('x', 600);
        var draft = new DraftReply("Too long", [new DraftPage(null, [new DraftGroup(null, [new DraftField("text", label, null, null, false, [])])])], 0);

        Assert.That(
            async () => await GetRequiredService<DraftDefinitions>().BuildAsync(draft, CancellationToken.None),
            Throws.TypeOf<AIUnavailableException>().With.Property(nameof(AIUnavailableException.Code)).EqualTo("ai_draft_invalid"));
    }

    [Test]
    public async Task ADraftedForm_IsAudited_WithWhatWasLeftOut_AndItsNotificationsSayItWasDrafted()
    {
        DraftedHeard.Clear();

        var answer = await CallAsync("designer", HttpMethod.Post, "ai/draft", new { text = Description });

        var key = Guid.Parse(answer.Body!["key"]!.GetValue<string>());
        var entry = (await GetRequiredService<IAuditRepository>().QueryAsync(new AuditQuery { Action = FormBuilderAI.DraftedAction }, new PageRequest(0, 10))).Items.Single();
        Assert.Multiple(() =>
        {
            Assert.That((entry.EntityType, entry.EntityKey), Is.EqualTo(("form", (Guid?)key)));
            Assert.That(JsonNode.Parse(entry.Details)!["left"]!.GetValue<int>(), Is.EqualTo(answer.Body!["left"]!.GetValue<int>()), "what the AI drafted that the form could not hold");
            Assert.That(DraftedHeard.Heard, Is.EqualTo(new[] { "saving new Drafted", "saved new Drafted" }));
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.AddNotificationAsyncHandler<FormSavingNotification, DraftedHeard>();
        builder.AddNotificationAsyncHandler<FormSavedNotification, DraftedHeard>();
    }

    [Test]
    public async Task ADeletedForm_TakesItsMarkWithIt()
    {
        var answer = await CallAsync("designer", HttpMethod.Post, "ai/draft", new { text = Description });
        var key = Guid.Parse(answer.Body!["key"]!.GetValue<string>());

        await TrashFormAsync(key);
        var forms = GetRequiredService<IFormBuilderFormService>();
        await forms.DeleteAsync(key, (await forms.GetAsync(key))!.ConcurrencyVersion, null);

        Assert.That(await GetRequiredService<DraftMarks>().GetAsync(key, CancellationToken.None), Is.Null);
    }

    [Test]
    public async Task FormsGoneWithTheirFolder_OrWithTheTrash_TakeTheirMarksWithThem()
    {
        var folders = GetRequiredService<IFormBuilderFolderService>();
        var folder = await folders.CreateAsync(new NewFolderRequest("Drafted here"));
        var inFolder = Guid.Parse((await CallAsync("designer", HttpMethod.Post, "ai/draft", new { text = Description, folderKey = folder.Key })).Body!["key"]!.GetValue<string>());
        var alone = Guid.Parse((await CallAsync("designer", HttpMethod.Post, "ai/draft", new { text = Description })).Body!["key"]!.GetValue<string>());
        var marks = GetRequiredService<DraftMarks>();
        Assume.That(await marks.GetAsync(inFolder, CancellationToken.None), Is.Not.Null);
        Assume.That(await marks.GetAsync(alone, CancellationToken.None), Is.Not.Null);

        await folders.TrashAsync(folder.Key, null);
        await folders.DeleteAsync(folder.Key, null);
        await TrashFormAsync(alone);
        await folders.EmptyTrashAsync((_, _) => Task.FromResult(true), null);

        Assert.Multiple(async () =>
        {
            Assert.That(await marks.GetAsync(inFolder, CancellationToken.None), Is.Null, "with its folder");
            Assert.That(await marks.GetAsync(alone, CancellationToken.None), Is.Null, "with the trash");
        });
    }

    private async Task<int> FormCountAsync() =>
        (int)(await GetRequiredService<IFormBuilderLibraryService>().ListChildrenAsync(People.Users["admin"], null, new PageRequest(0, 500)))!.Total;
}

/// <summary>What a form's save notifications said of where the form came from, in the order they came.</summary>
internal sealed class DraftedHeard : INotificationAsyncHandler<FormSavingNotification>, INotificationAsyncHandler<FormSavedNotification>
{
    private static readonly ConcurrentQueue<string> Said = new();

    /// <summary>What was heard.</summary>
    public static IReadOnlyList<string> Heard => [.. Said];

    /// <summary>Forgets what was heard.</summary>
    public static void Clear() => Said.Clear();

    /// <inheritdoc />
    public Task HandleAsync(FormSavingNotification notification, CancellationToken cancellationToken)
    {
        Said.Enqueue($"saving {(notification.IsNew ? "new" : "draft")} {notification.CreatedFrom}");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task HandleAsync(FormSavedNotification notification, CancellationToken cancellationToken)
    {
        Said.Enqueue($"saved {(notification.IsNew ? "new" : "draft")} {notification.CreatedFrom}");
        return Task.CompletedTask;
    }
}
