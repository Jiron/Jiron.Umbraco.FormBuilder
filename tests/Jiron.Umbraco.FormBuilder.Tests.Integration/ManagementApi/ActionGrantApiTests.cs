using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Testing.People;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Actions;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>
/// What an action needs beyond <c>manageActions</c> (16.1's security review, ADR-0235): a content action the user's own
/// Umbraco rights where it creates, updates or publishes, and an action that copies sensitive answers <c>viewSensitive</c>.
/// Each holds where an action is saved, restored or replaced into the draft, where a publish makes it run, and where the
/// preview's test entries run it.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class ActionGrantApiTests : ManagementApiTestBase
{
    private static readonly Guid SalaryKey = Guid.Parse("30000000-0000-4000-8000-0000000000b2");

    [Test]
    public async Task AContentAction_IsSetUpOnlyByWhoMayCreateWhereItGoes_AndPublishWhereItPublishes()
    {
        var caller = await CallerAsync();
        var form = await FormAsync("Stories");
        await FillAsync(form);

        var formsOnly = await SaveWithAsync(caller, "formsOnly", form, Content(publish: false));
        var browsers = await SaveWithAsync(caller, "browser", form, Content(publish: false));
        var creators = await SaveWithAsync(caller, "creator", form, Content(publish: false));
        var creatorsPublishing = await SaveWithAsync(caller, "creator", form, Content(publish: true));
        var publishers = await SaveWithAsync(caller, "publisher", form, Content(publish: true));

        Assert.Multiple(() =>
        {
            Assert.That(formsOnly.Outcome, Is.EqualTo("403 content_rights"), formsOnly.ToString());
            Assert.That(formsOnly.Body!["gap"]!.GetValue<string>(), Is.EqualTo("contentSection"));
            Assert.That(browsers.Outcome, Is.EqualTo("403 content_rights"), "browsing is not creating");
            Assert.That(browsers.Body!["gap"]!.GetValue<string>(), Is.EqualTo("content"));
            Assert.That(creators.Status, Is.EqualTo(System.Net.HttpStatusCode.OK), creators.ToString());
            Assert.That(creatorsPublishing.Outcome, Is.EqualTo("403 content_rights"), "publishing needs Umbraco's publish too");
            Assert.That(publishers.Status, Is.EqualTo(System.Net.HttpStatusCode.OK), publishers.ToString());
        });
    }

    [Test]
    public async Task AContentActionAlreadyThere_LeavesOtherChangesAlone_ButItsOwnChangeIsHeldAgain()
    {
        var caller = await CallerAsync();
        var form = await FormAsync("Stories");
        await FillAsync(form);
        var action = Content(publish: false);
        Assert.That((await SaveWithAsync(caller, "creator", form, action)).Status, Is.EqualTo(System.Net.HttpStatusCode.OK));

        // Another change to the draft, by someone who may not create content: the action is as it was.
        var (version, definition) = await DraftAsync(caller, "formsOnly", form);
        definition["pages"]![0]!["groups"]![0]!["fields"]![0]!["label"] = new JsonObject { [string.Empty] = "Your name" };
        var other = await caller.CallAsync("formsOnly", HttpMethod.Put, $"form/{form}/draft", new { concurrencyVersion = version, definition });

        // The action's own name changes: what it saves is decided anew.
        (version, definition) = await DraftAsync(caller, "formsOnly", form);
        definition["actions"]![0]!["settings"]!["name"] = "Someone else's story";
        var renamed = await caller.CallAsync("formsOnly", HttpMethod.Put, $"form/{form}/draft", new { concurrencyVersion = version, definition });

        Assert.Multiple(() =>
        {
            Assert.That(other.Status, Is.EqualTo(System.Net.HttpStatusCode.OK), other.ToString());
            Assert.That(renamed.Outcome, Is.EqualTo("403 content_rights"), renamed.ToString());
        });
    }

    [Test]
    public async Task APublish_IsHeldAgainstWhatIsLive_AndThePreviewsTestEntriesAgainstEverything()
    {
        var caller = await CallerAsync();
        var form = await FormAsync("Stories");
        await FillAsync(form);

        // The draft holds a content action someone with the rights saved; the publish is someone else's.
        var forms = GetRequiredService<IFormBuilderFormService>();
        var draft = (await forms.GetDraftAsync(form))!;
        var saved = (await forms.SaveDraftAsync(form, draft.Form.ConcurrencyVersion, draft.Definition! with { Actions = [ContentAction(publish: false)] }, null, null))!;
        var concurrency = saved.Form.ConcurrencyVersion;

        var publish = await caller.CallAsync("formsOnly", HttpMethod.Post, $"form/{form}/publish", new { concurrencyVersion = concurrency });
        var preview = await caller.CallAsync("formsOnly", HttpMethod.Get, $"form/{form}/preview?actions=true");
        var ticket = await caller.CallAsync("formsOnly", HttpMethod.Post, $"form/{form}/preview/ticket", new { version = saved.Version!.Value, runActions = true });
        var quietTicket = await caller.CallAsync("formsOnly", HttpMethod.Post, $"form/{form}/preview/ticket", new { version = saved.Version!.Value, runActions = false });

        Assert.Multiple(() =>
        {
            Assert.That(publish.Outcome, Is.EqualTo("403 content_rights"), publish.ToString());
            Assert.That(preview.Outcome, Is.EqualTo("403 content_rights"), "test entries that run the draft's actions");
            Assert.That(ticket.Outcome, Is.EqualTo("403 content_rights"));
            Assert.That(quietTicket.Status, Is.EqualTo(System.Net.HttpStatusCode.OK), "test entries that run nothing");
        });
    }

    [Test]
    public async Task AnActionThatCopiesSensitiveAnswers_NeedsViewSensitive_AsDoesChangingWhereSuchAnActionSendsThem()
    {
        var form = await FormAsync("Payroll");
        var forms = GetRequiredService<IFormBuilderFormService>();
        var draft = (await forms.GetDraftAsync(form))!;
        var withSalary = Fillable(draft.Definition!);
        withSalary = withSalary with { Pages = [withSalary.Pages[0] with { Groups = [withSalary.Pages[0].Groups[0] with { Fields = [.. withSalary.Pages[0].Groups[0].Fields, Salary] }] }] };
        var plain = Email("Notify the team", "team@jfb.test", includeSalary: false);
        _ = await forms.SaveDraftAsync(form, draft.Form.ConcurrencyVersion, withSalary with { Actions = [plain] }, null, null);

        // The designer manages actions but may not see sensitive answers.
        var (version, definition) = await DraftAsync(Api, "designer", form);
        definition["actions"]!.AsArray().Add(Json(Email("Notify me", "me@jfb.test", includeSalary: true)));
        var copying = await CallAsync("designer", HttpMethod.Put, $"form/{form}/draft", new { concurrencyVersion = version, definition });

        // One with the permission sets it up; then the designer redirects it.
        var byAdmin = await CallAsync("admin", HttpMethod.Put, $"form/{form}/draft", new { concurrencyVersion = version, definition });
        (version, definition) = await DraftAsync(Api, "designer", form);
        var copyingAction = definition["actions"]!.AsArray().Single(action => action!["name"]!.GetValue<string>() == "Notify me")!;
        copyingAction["settings"]!["to"] = new JsonArray("someone@jfb.test");
        var redirected = await CallAsync("designer", HttpMethod.Put, $"form/{form}/draft", new { concurrencyVersion = version, definition });

        // Changing the action that copies nothing, and moving the actions round, are the designer's to do.
        (version, definition) = await DraftAsync(Api, "designer", form);
        var actions = definition["actions"]!.AsArray();
        actions.Single(action => action!["name"]!.GetValue<string>() == "Notify the team")!["settings"]!["to"] = new JsonArray("sales@jfb.test");
        var reordered = new JsonArray([.. actions.Reverse().Select(action => action!.DeepClone())]);
        definition["actions"] = reordered;
        var others = await CallAsync("designer", HttpMethod.Put, $"form/{form}/draft", new { concurrencyVersion = version, definition });

        Assert.Multiple(() =>
        {
            Assert.That(copying.Outcome, Is.EqualTo("403 forbidden"), copying.ToString());
            Assert.That(copying.Body!["permission"]!.GetValue<string>(), Is.EqualTo("viewSensitive"));
            Assert.That(byAdmin.Status, Is.EqualTo(System.Net.HttpStatusCode.OK), byAdmin.ToString());
            Assert.That(redirected.Outcome, Is.EqualTo("403 forbidden"), "where the salary goes is decided anew");
            Assert.That(others.Status, Is.EqualTo(System.Net.HttpStatusCode.OK), others.ToString());
        });
    }

    [Test]
    public async Task APublishThatMakesAnActionCopySensitiveAnswers_NeedsViewSensitive()
    {
        var form = await FormAsync("Payroll");
        var forms = GetRequiredService<IFormBuilderFormService>();
        var draft = (await forms.GetDraftAsync(form))!;
        var withSalary = Fillable(draft.Definition!);
        withSalary = withSalary with { Pages = [withSalary.Pages[0] with { Groups = [withSalary.Pages[0].Groups[0] with { Fields = [.. withSalary.Pages[0].Groups[0].Fields, Salary] }] }] };
        var saved = (await forms.SaveDraftAsync(form, draft.Form.ConcurrencyVersion, withSalary with { Actions = [Email("Notify me", "me@jfb.test", includeSalary: true)] }, null, null))!;

        var refused = await CallAsync("designer", HttpMethod.Post, $"form/{form}/publish", new { concurrencyVersion = saved.Form.ConcurrencyVersion });
        const FormBuilderPermissions Designers = FormBuilderPermissions.Design | FormBuilderPermissions.ManageActions | FormBuilderPermissions.ManageLibrary;
        await GetRequiredService<IFormBuilderAccessService>().ChangeGrantAsync(new AccessGrantChange(People.GroupKeys["designer"], PermissionTarget.Global, Guid.Empty, Designers, Designers | FormBuilderPermissions.ViewEntries | FormBuilderPermissions.ViewSensitive, null));
        var published = await CallAsync("designer", HttpMethod.Post, $"form/{form}/publish", new { concurrencyVersion = saved.Form.ConcurrencyVersion });

        Assert.Multiple(() =>
        {
            Assert.That(refused.Outcome, Is.EqualTo("403 forbidden"), refused.ToString());
            Assert.That(refused.Body!["permission"]!.GetValue<string>(), Is.EqualTo("viewSensitive"));
            Assert.That(published.Status, Is.EqualTo(System.Net.HttpStatusCode.OK), published.ToString());
        });
    }

    private static FormField Salary { get; } = new()
    {
        Key = SalaryKey,
        Type = "number",
        Alias = "salary",
        Label = new LocalizedText([new(string.Empty, "Salary")]),
        Sensitive = true,
    };

    private static FormAction ContentAction(bool publish) => new()
    {
        Key = Guid.NewGuid(),
        Type = "content",
        Name = "Save each story",
        Settings = SettingsObject.Parse($$"""{"documentType":"jfbStory","name":"Story","publish":{{(publish ? "true" : "false")}}}"""),
    };

    private static JsonObject Content(bool publish) => Json(ContentAction(publish));

    private static FormAction Email(string name, string to, bool includeSalary) => new()
    {
        Key = Guid.NewGuid(),
        Type = "email",
        Name = name,
        Settings = SettingsObject.Parse(includeSalary
            ? $$"""{"to":["{{to}}"],"subject":{"":"S"},"body":{"":"<p>B</p>"},"includeSensitive":["{{SalaryKey}}"]}"""
            : $$$"""{"to":["{{{to}}}"],"subject":{"":"S"},"body":{"":"<p>B</p>"}}"""),
    };

    private static JsonObject Json(FormAction action) => new()
    {
        ["key"] = action.Key.ToString(),
        ["type"] = action.Type,
        ["name"] = action.Name,
        ["enabled"] = true,
        ["trigger"] = "submitted",
        ["stopOnFailure"] = false,
        ["settings"] = JsonNode.Parse(action.Settings!.ToString()),
    };

    private static async Task<(int Version, JsonObject Definition)> DraftAsync(ManagementApiCaller caller, string role, Guid form)
    {
        var draft = await caller.CallAsync(role, HttpMethod.Get, $"form/{form}/draft");
        return (draft.Body!["form"]!["concurrencyVersion"]!.GetValue<int>(), draft.Body["definition"]!.AsObject());
    }

    // Saves the draft with the action as the only one.
    private static async Task<ApiAnswer> SaveWithAsync(ManagementApiCaller caller, string role, Guid form, JsonObject action)
    {
        var (version, definition) = await DraftAsync(caller, role, form);
        definition["actions"] = new JsonArray(action);
        return await caller.CallAsync(role, HttpMethod.Put, $"form/{form}/draft", new { concurrencyVersion = version, definition });
    }

    // Four groups that design forms and manage their actions: one without Umbraco's Content section, and three with it,
    // whose users may browse, browse and create, or browse, create and publish everywhere.
    private async Task<ManagementApiCaller> CallerAsync()
    {
        var groups = new Dictionary<string, string>
        {
            ["formsOnly"] = await GroupAsync("jfbFormsOnly", content: false),
            ["browser"] = await GroupAsync("jfbBrowsers", content: true),
            ["creator"] = await GroupAsync("jfbCreators", content: true, ActionNew.ActionLetter),
            ["publisher"] = await GroupAsync("jfbPublishers", content: true, ActionNew.ActionLetter, ActionPublish.ActionLetter),
        };
        return new ManagementApiCaller(Client, Services, role => [groups[role]]);
    }

    private async Task<string> GroupAsync(string alias, bool content, params string[] permissions)
    {
        await using var scope = Services.CreateAsyncScope();
        var people = scope.ServiceProvider.GetRequiredService<TestPeople>();
        var sections = new TestUserGroup { Alias = alias, Name = alias, ContentRootAccess = content, Sections = { Integration.Access.AccessPersonas.Section } };
        if (content)
        {
            sections.Sections.Add(Constants.Applications.Content);
        }

        var group = await people.EnsureUserGroupAsync(sections);
        if (permissions.Length > 0)
        {
            group.Permissions = new HashSet<string>([ActionBrowse.ActionLetter, .. permissions]);
            Assert.That((await scope.ServiceProvider.GetRequiredService<IUserGroupService>().UpdateAsync(group, Constants.Security.SuperUserKey)).Success, Is.True);
        }

        await GrantAsync(group.Key, PermissionTarget.Global, Guid.Empty, FormBuilderPermissions.Design | FormBuilderPermissions.ManageActions);
        return alias;
    }
}
