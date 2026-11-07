using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Email;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>
/// The entry panel through the management API (ROADMAP 11.2, ADR-0208): each endpoint by the permission it needs, a note
/// its author's to change, an edit's problems by their path, a forward only to those the site trusts, and the entry's
/// link naming its form only to who may view its entries.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class EntryPanelApiTests : ManagementApiTestBase
{
    private const string Admin = "admin";

    private static readonly FormField Salary = new()
    {
        Key = Guid.Parse("30000000-0000-4000-8000-0000000000b1"),
        Type = "text",
        Alias = "salary",
        Label = new LocalizedText("Salary"),
        Sensitive = true,
    };

    private static readonly FormField Age = new()
    {
        Key = Guid.Parse("30000000-0000-4000-8000-0000000000b2"),
        Type = "number",
        Alias = "age",
        Label = new LocalizedText("Age"),
    };

    [Test]
    public async Task ThePanel_AnswersEachUserByWhatTheyMayDo()
    {
        var (form, entry) = await EntryAsync();
        var path = $"form/{form}/entry/{entry.Key}";

        var viewed = await CallAsync("viewer", HttpMethod.Get, path);
        var handled = await CallAsync("entryHandler", HttpMethod.Get, path);
        var hidden = await CallAsync("viewer", HttpMethod.Post, $"{path}/reveal", new { field = Salary.Key });
        var revealed = await CallAsync("privacyOfficer", HttpMethod.Post, $"{path}/reveal", new { field = Salary.Key });
        var timeline = await CallAsync("viewer", HttpMethod.Get, $"{path}/timeline");
        var unseen = await CallAsync("designer", HttpMethod.Get, path);
        var gone = await CallAsync(Admin, HttpMethod.Get, $"form/{form}/entry/{Guid.NewGuid()}");

        Assert.Multiple(() =>
        {
            Assert.That(viewed.Outcome, Is.EqualTo("200"), viewed.ToString());
            Assert.That(viewed.Body!["permissions"]!.AsArray().Select(name => name!.GetValue<string>()), Is.EqualTo(new[] { "viewEntries" }));
            var answers = viewed.Body!["pages"]![0]!["groups"]![0]!["answers"]!.AsArray();
            Assert.That(answers[0]!["texts"]!.AsArray().Select(text => text!.GetValue<string>()), Is.EqualTo(new[] { "Ada" }));
            Assert.That((answers[1]!["masked"]!.GetValue<bool>(), answers[1]!["texts"]!.AsArray().Count), Is.EqualTo((true, 0)), "a sensitive answer never leaves unasked");
            Assert.That(answers[0]!["edit"], Is.Null, "a viewer edits nothing");
            Assert.That(handled.Body!["pages"]![0]!["groups"]![0]!["answers"]![0]!["edit"]!["kind"]!.GetValue<string>(), Is.EqualTo("text"));
            Assert.That(hidden.Outcome, Does.StartWith("403"), hidden.ToString());
            Assert.That(revealed.Body!["texts"]!.AsArray().Select(text => text!.GetValue<string>()), Is.EqualTo(new[] { "90000" }), revealed.ToString());
            Assert.That(timeline.Body!["events"]!.AsArray().Select(item => (item!["kind"]!.GetValue<string>(), item["user"]?.GetValue<string>())), Does.Contain(("opened", (string?)"viewer")), timeline.ToString());
            Assert.That(unseen.Outcome, Does.StartWith("403"), "a user who may not view the form's entries");
            Assert.That(gone.Outcome, Is.EqualTo("404 entry_not_found"));
        });
    }

    [Test]
    public async Task ANote_IsItsAuthorsToChange()
    {
        var (form, entry) = await EntryAsync();
        var path = $"form/{form}/entry/{entry.Key}/notes";

        var added = await CallAsync("viewer", HttpMethod.Post, path, new { body = "Call back on Monday." });
        var id = added.Body!["id"]!.GetValue<int>();
        var theirs = await CallAsync(Admin, HttpMethod.Put, $"{path}/{id}", new { body = "Mine now." });
        var changed = await CallAsync("viewer", HttpMethod.Put, $"{path}/{id}", new { body = "Call back on Tuesday." });
        var blank = await CallAsync("viewer", HttpMethod.Post, path, new { body = "   " });
        var listed = await CallAsync(Admin, HttpMethod.Get, path);

        Assert.Multiple(() =>
        {
            Assert.That((added.Outcome, added.Body!["mine"]!.GetValue<bool>(), added.Body!["author"]!.GetValue<string>()), Is.EqualTo(("200", true, "viewer")), added.ToString());
            Assert.That(theirs.Outcome, Is.EqualTo("403 note_not_yours"));
            Assert.That(changed.Body!["body"]!.GetValue<string>(), Is.EqualTo("Call back on Tuesday."));
            Assert.That(blank.Outcome, Is.EqualTo("400 note_invalid"));
            Assert.That((listed.Body![0]!["body"]!.GetValue<string>(), listed.Body![0]!["mine"]!.GetValue<bool>()), Is.EqualTo(("Call back on Tuesday.", false)));
        });
    }

    [Test]
    public async Task AnEdit_AnswersItsProblemsByPath()
    {
        var (form, entry) = await EntryAsync();
        var path = $"form/{form}/entry/{entry.Key}/answers";

        var invalid = await CallAsync("entryHandler", HttpMethod.Put, path, new { answers = new[] { new { field = Age.Key, values = new[] { "many" } } } });
        var saved = await CallAsync("entryHandler", HttpMethod.Put, path, new { answers = new[] { new { field = Age.Key, values = new[] { "37" } } } });
        var twice = await CallAsync("entryHandler", HttpMethod.Put, path, new { answers = new[] { new { field = Age.Key, values = new[] { "1" } }, new { field = Age.Key, values = new[] { "2" } } } });
        var viewer = await CallAsync("viewer", HttpMethod.Put, path, new { answers = new[] { new { field = Age.Key, values = new[] { "38" } } } });

        Assert.Multiple(() =>
        {
            Assert.That((invalid.Outcome, Path(invalid)), Is.EqualTo(("400 invalid", "answers[0].values")), invalid.ToString());
            Assert.That((saved.Outcome, saved.Body!["changed"]!.GetValue<int>()), Is.EqualTo(("200", 1)), saved.ToString());
            Assert.That(twice.Outcome, Does.StartWith("400"), "each field once");
            Assert.That(viewer.Outcome, Does.StartWith("403"));
        });
    }

    [Test]
    public async Task AForward_GoesOnlyToThoseTheSiteTrusts()
    {
        var (form, entry) = await EntryAsync();
        var path = $"form/{form}/entry/{entry.Key}/forward";
        await CallAsync("viewer", HttpMethod.Get, $"form/{form}/entry/{entry.Key}");
        await CallAsync("designer", HttpMethod.Get, "me");
        var settings = GetRequiredService<IFormBuilderSettingsService>();
        var email = await settings.GetAsync<EmailSettings>();
        await settings.SaveAsync(email.Value with { ForwardAddresses = ["hr@example.org"] }, email.Version, null);
        var users = GetRequiredService<IUserService>();
        var viewerKey = users.GetByEmail("api-viewer@jfb.test")!.Key;
        var designerKey = users.GetByEmail("api-designer@jfb.test")!.Key;

        var recipients = await CallAsync(Admin, HttpMethod.Get, path);
        var toViewer = await CallAsync(Admin, HttpMethod.Post, path, new { user = viewerKey, message = "Could you look at this one?" });
        var toTeam = await CallAsync(Admin, HttpMethod.Post, path, new { address = "HR@example.org", includeSensitive = true });
        var byPrivacy = await CallAsync("privacyOfficer", HttpMethod.Post, path, new { address = "hr@example.org" });
        var toStranger = await CallAsync(Admin, HttpMethod.Post, path, new { address = "someone@example.org" });
        var toDesigner = await CallAsync(Admin, HttpMethod.Post, path, new { user = designerKey });
        var toNobody = await CallAsync(Admin, HttpMethod.Post, path, new { message = "Hello" });
        var mail = await GetRequiredService<IEmailRepository>().ListForEntryAsync(entry.Id);

        Assert.Multiple(() =>
        {
            Assert.That(recipients.Body!["users"]!.AsArray().Select(user => user!["name"]!.GetValue<string>()), Does.Contain("viewer").And.Not.Contain("designer"), recipients.ToString());
            Assert.That(recipients.Body!["addresses"]!.AsArray().Select(address => address!.GetValue<string>()), Is.EqualTo(new[] { "hr@example.org" }));
            Assert.That(toViewer.Outcome, Is.EqualTo("204"), toViewer.ToString());
            Assert.That(toTeam.Outcome, Is.EqualTo("204"), toTeam.ToString());
            Assert.That(byPrivacy.Outcome, Does.StartWith("403"), "forwarding needs editEntries");
            Assert.That(toStranger.Outcome, Is.EqualTo("400 forward_recipient_not_allowed"));
            Assert.That(toDesigner.Outcome, Is.EqualTo("400 forward_recipient_not_allowed"), "a user who may not view the form's entries");
            Assert.That(toNobody.Outcome, Is.EqualTo("400 forward_invalid"));
            Assert.That(mail.Select(item => (item.Kind, item.To)), Is.EquivalentTo(new[] { (EmailKind.Forward, "api-viewer@jfb.test"), (EmailKind.Forward, "hr@example.org") }));
            var plain = mail.Single(item => item.To == "api-viewer@jfb.test").HtmlBody!;
            Assert.That(plain, Does.Contain("Could you look at this one?").And.Contain("Ada").And.Not.Contain("90000"), "sensitive values stay out unless chosen");
            Assert.That(mail.Single(item => item.To == "hr@example.org").HtmlBody, Does.Contain("90000"), "chosen by a user who may see them");
        });
    }

    [Test]
    public async Task TheEntrysLink_NamesItsForm_OnlyToWhoMayViewItsEntries()
    {
        var (form, entry) = await EntryAsync();

        var viewer = await CallAsync("viewer", HttpMethod.Get, $"entry/{entry.Key}/form");
        var designer = await CallAsync("designer", HttpMethod.Get, $"entry/{entry.Key}/form");
        var gone = await CallAsync("viewer", HttpMethod.Get, $"entry/{Guid.NewGuid()}/form");

        Assert.Multiple(() =>
        {
            Assert.That(viewer.Body!["form"]!.GetValue<Guid>(), Is.EqualTo(form), viewer.ToString());
            Assert.That((designer.Outcome, designer.Body!["form"]), Is.EqualTo(("200", (System.Text.Json.Nodes.JsonNode?)null)), "nothing of an entry the user may not view");
            Assert.That(gone.Body!["form"], Is.Null, "the same as for an entry that is gone");
        });
    }

    private static string? Path(ApiAnswer answer) => answer.Body?["errors"]?[0]?["path"]?.GetValue<string>();

    // A published form with a name, a sensitive salary and an age, and an entry that answered the name and the salary.
    private async Task<(Guid Form, StoredEntry Entry)> EntryAsync()
    {
        var forms = GetRequiredService<IFormBuilderFormService>();
        var created = await forms.CreateAsync(new NewFormRequest($"Form {Guid.NewGuid():N}", UserKey: null));
        var draft = (await forms.GetDraftAsync(created.Key))!;
        var definition = Fillable(draft.Definition!);
        definition = definition with { Pages = [definition.Pages[0] with { Groups = [definition.Pages[0].Groups[0] with { Fields = [NameField, Salary, Age] }] }] };
        var saved = (await forms.SaveDraftAsync(created.Key, draft.Form.ConcurrencyVersion, definition, null, null))!;
        await forms.PublishAsync(created.Key, saved.Form.ConcurrencyVersion, null, null);
        var row = (await GetRequiredService<IFormRepository>().GetAsync(created.Key))!;
        var published = (await forms.GetDraftAsync(created.Key))!.Definition!;
        var types = GetRequiredService<FieldTypeRegistry>();
        EntryFieldAnswer[] answers =
        [
            new(NameField.Key, FieldValues.Read(types.FindType("text")!, NameField, new FieldAnswers(["Ada"])).Values),
            new(Salary.Key, FieldValues.Read(types.FindType("text")!, Salary, new FieldAnswers(["90000"])).Values),
        ];
        var entry = await GetRequiredService<EntryAnswerStore>().CreateAsync(
            new NewEntry
            {
                Key = Guid.NewGuid(),
                FormId = row.Id,
                FormVersionId = row.PublishedVersionId!.Value,
                State = EntryState.Submitted,
                SubmissionId = Guid.NewGuid(),
                SubmissionHash = new string('a', 64),
                Culture = "en-US",
            },
            published,
            answers,
            new Dictionary<Guid, ConsentWording>());
        return (created.Key, entry!);
    }
}
