using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Email;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Microsoft.Extensions.DependencyInjection;
using MimeKit;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Emails;

/// <summary>A view of the site's for the tests: it writes the model's answers, or there is none of that name.</summary>
internal sealed class FakeEmailView : IFormBuilderEmailView
{
    public const string Name = "fake";

    public Task<string?> RenderAsync(string view, EmailViewModel model, CancellationToken cancellationToken) =>
        Task.FromResult(view == Name ? $"<p>From the view: {model.FormName}, {model.EntryNumber}, {model.Culture}, {model.Subject}, {model.Answers}</p>" : null);
}

/// <summary>The email action, from a submission to the pickup folder (ROADMAP 8.3, ADR-0160).</summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class EmailActionTests : EmailTestBase
{
    private const string To = "team@jfb.test";

    private static readonly FormField EmailField = new()
    {
        Key = Guid.Parse("c0000000-0000-4000-8000-000000000001"),
        Type = "email",
        Alias = "email",
        Label = new LocalizedText([new(string.Empty, "Email"), new("de", "E-Mail")]),
    };

    private static readonly FormField Message = new()
    {
        Key = Guid.Parse("c0000000-0000-4000-8000-000000000002"),
        Type = "textarea",
        Alias = "message",
        Label = new LocalizedText("Message"),
    };

    private static readonly FormField Secret = new()
    {
        Key = Guid.Parse("c0000000-0000-4000-8000-000000000003"),
        Type = "text",
        Alias = "privateNote",
        Label = new LocalizedText("Secret"),
        Sensitive = true,
    };

    private static readonly FormField Topic = new()
    {
        Key = Guid.Parse("c0000000-0000-4000-8000-000000000004"),
        Type = "dropdown",
        Alias = "topic",
        Label = new LocalizedText("Topic"),
        Options = new FieldOptions { Items = [new FieldOption { Value = "sales" }, new FieldOption { Value = "support" }, new FieldOption { Value = "other" }] },
    };

    private static readonly FormField Upload = new()
    {
        Key = Guid.Parse("c0000000-0000-4000-8000-000000000005"),
        Type = "file",
        Alias = "upload",
        Label = new LocalizedText("Upload"),
    };

    private MemoryFileStore Files => GetRequiredService<MemoryFileStore>();

    private static FormAction Email(string settings, string name = "Mail") => new()
    {
        Key = Guid.NewGuid(),
        Type = EmailActionType.Alias,
        Name = name,
        Settings = SettingsObject.Parse(settings),
    };

    private static string Texts(string to = $$"""["{{To}}"]""", string subject = "Entry {{ entry.number }}", string body = "<p>{{ field.message }}</p>", string more = "") =>
        $$$"""{"to":{{{to}}},"subject":{"":"{{{subject}}}"},"body":{"":"{{{body}}}"}{{{more}}}}""";

    private static string ToVisitor(string body = "<p>{{ field.message }}</p>", string more = "") =>
        Texts(to: "[]", body: body, more: $$""","toField":"{{EmailField.Key}}"{{more}}""");

    private Task<Guid> FormAsync(params FormAction[] actions) =>
        PublishAsync(name: Guid.NewGuid().ToString("N"), change: definition => definition with { Actions = actions }, fields: [NameField, EmailField, Message, Secret, Topic]);

    private async Task<MimeMessage> OneAsync(Guid form, string values)
    {
        var entry = await SubmitAsync(form, values);
        await DrainAsync(entry.Id);
        await SendDueAsync();
        return Written().Single();
    }

    private static string HtmlOf(MimeMessage mail) => ((TextPart)mail.Body!).Text;

    [Test]
    public async Task ATeamMail_SaysWhatTheActionSays_WithEachValueEscaped()
    {
        var form = await FormAsync(Email(Texts()));

        var mail = await OneAsync(form, """{"name":"Ada","message":"<b>Bold</b> & <script>alert(1)</script>"}""");

        Assert.Multiple(() =>
        {
            Assert.That(mail.To.Mailboxes.Single().Address, Is.EqualTo(To));
            Assert.That(mail.Subject, Is.EqualTo("Entry 1"));
            Assert.That(HtmlOf(mail), Does.Contain("&lt;b&gt;Bold&lt;/b&gt; &amp; &lt;script&gt;alert(1)&lt;/script&gt;"));
            Assert.That(HtmlOf(mail), Does.Not.Contain("<b>Bold").And.Not.Contain("<script"));
        });
    }

    [Test]
    public async Task AMail_IsRecordedUnderItsEntryAndItsRun()
    {
        var form = await FormAsync(Email(Texts()));
        var entry = await SubmitAsync(form, """{"name":"Ada"}""");

        var runs = await DrainAsync(entry.Id);

        var stored = (await Emails.ListForEntryAsync(entry.Id)).Single();
        Assert.Multiple(() =>
        {
            Assert.That(runs.Single().State, Is.EqualTo(ActionRunState.Succeeded));
            Assert.That(stored.ActionRunId, Is.EqualTo(runs.Single().Id));
            Assert.That(stored.Kind, Is.EqualTo(EmailKind.Action));
        });
    }

    [Test]
    public async Task AMail_IsInTheEntrysLanguage()
    {
        var form = await FormAsync(Email("""{"to":["team@jfb.test"],"subject":{"":"Hello","de":"Hallo"},"body":{"":"<p>English</p>","de":"<p>Deutsch: {{ entry.table }}</p>"}}"""));
        var row = (await FormRows.GetAsync(form))!;
        var name = (await FormRows.ListFieldsAsync(row.Id)).Single(field => field.Values.FieldKey == NameField.Key);
        var entry = (await Entries.CreateAsync(new NewEntry
        {
            Key = Guid.NewGuid(),
            FormId = row.Id,
            FormVersionId = row.PublishedVersionId!.Value,
            State = EntryState.Submitted,
            SubmissionId = Guid.NewGuid(),
            SubmissionHash = new string('a', 64),
            Culture = "de-DE",
            Values = [new NewEntryValue(name.Id, "Ada")],
        }))!;
        var action = (await Forms.GetDraftAsync(form))!.Definition!.Actions.Single();
        await Runs.AddAsync(entry.Id, entry.FormVersionId, Guid.NewGuid(), [new NewActionRun(action.Key, action.Type, action.Trigger, 0, false)]);

        await DrainAsync(entry.Id);
        await SendDueAsync();

        var mail = Written().Single();
        Assert.Multiple(() =>
        {
            Assert.That(mail.Subject, Is.EqualTo("Hallo"));
            Assert.That(HtmlOf(mail), Does.Contain("Deutsch").And.Contain("Name (de)"));
            Assert.That(HtmlOf(mail), Does.Contain("lang=\"de-DE\""));
        });
    }

    [Test]
    public async Task EntryTable_ListsTheUnanswered_UnlessTheActionLeavesThemOut()
    {
        var all = await FormAsync(Email(Texts(body: "<div>{{ entry.table }}</div>")));
        var answered = await FormAsync(Email(Texts(body: "<div>{{ entry.table }}</div>", more: ""","withoutUnanswered":true""")));

        var first = await SubmitAsync(all, """{"name":"Ada"}""");
        var second = await SubmitAsync(answered, """{"name":"Ada"}""");
        await DrainAsync(first.Id);
        await DrainAsync(second.Id);

        var full = (await Emails.ListForEntryAsync(first.Id)).Single();
        var short_ = (await Emails.ListForEntryAsync(second.Id)).Single();
        Assert.Multiple(() =>
        {
            Assert.That(full.HtmlBody, Does.Contain("Topic").And.Contain("Ada").And.Contain("<table"));
            Assert.That(full.TextBody, Does.Contain("Name: Ada").And.Contain("Topic:"));
            Assert.That(short_.HtmlBody, Does.Not.Contain("Topic").And.Contain("Ada"));
        });
    }

    [Test]
    public async Task ASensitiveField_StaysOut_UnlessTheActionIncludesIt()
    {
        const string Values = """{"name":"Ada","privateNote":"hidden-value"}""";
        var without = await FormAsync(Email(Texts(body: "<div>{{ entry.table }}</div><p>{{ field.privateNote }}</p>")));
        var with = await FormAsync(Email(Texts(body: "<div>{{ entry.table }}</div>", more: $$""","includeSensitive":["{{Secret.Key}}"]""")));

        var first = await SubmitAsync(without, Values);
        var second = await SubmitAsync(with, Values);
        await DrainAsync(first.Id);
        await DrainAsync(second.Id);

        Assert.Multiple(async () =>
        {
            Assert.That((await Emails.ListForEntryAsync(first.Id)).Single().HtmlBody, Does.Not.Contain("hidden-value"));
            Assert.That((await Emails.ListForEntryAsync(second.Id)).Single().HtmlBody, Does.Contain("hidden-value"));
        });
    }

    [Test]
    public async Task AConfirmation_GoesToTheAddressTyped_AsAMailOfItsOwn_WithALinkTheClientCannotFollow()
    {
        var form = await FormAsync(Email(Texts(body: "<p>Editor's <a href=\\\"https://jfb.test/own\\\">link</a></p><p>{{ field.message }}</p>", more: $$""","toField":"{{EmailField.Key}}","cc":["copy@jfb.test"]""")));
        var entry = await SubmitAsync(form, """{"name":"Ada","email":"Stranger+x@jfb.test","message":"Visit https://evil.test/x or www.evil.test now <a href='https://evil.test'>here</a>"}""");

        await DrainAsync(entry.Id);
        await SendDueAsync();

        var written = Written();
        var visitor = written.Single(mail => mail.To.Mailboxes.Single().Address == "Stranger+x@jfb.test");
        var team = written.Single(mail => mail.To.Mailboxes.Single().Address == To);
        var breaker = ((char)0x200b).ToString();
        Assert.Multiple(async () =>
        {
            Assert.That((await Emails.ListForEntryAsync(entry.Id)).Single(mail => mail.RecipientKey is not null).RecipientKey, Is.EqualTo("stranger@jfb.test"));
            Assert.That(HtmlOf(visitor).Contains("https://evil", StringComparison.Ordinal), Is.False, "the words stay, and no client makes a link of them");
            Assert.That(HtmlOf(visitor).Contains("www.evil", StringComparison.Ordinal), Is.False);
            Assert.That(HtmlOf(visitor).Contains($"https:{breaker}//evil.{breaker}test/x", StringComparison.Ordinal), Is.True);
            Assert.That(HtmlOf(visitor).Contains("<a href='", StringComparison.Ordinal), Is.False);
            Assert.That(HtmlOf(visitor), Does.Contain("href=\"https://jfb.test/own\""), "the editor's own link stays");
            Assert.That(HtmlOf(team).Contains("https://evil.test/x", StringComparison.Ordinal), Is.True, "the team sees what was typed");
            Assert.That(HtmlOf(team).Contains("href=\"https://evil", StringComparison.Ordinal), Is.False, "and no link either");
        });
    }

    [Test]
    public async Task AVisitorsAddress_GetsAMailOnlyWithinTheLimits_AndTheRunStillSucceeds()
    {
        MailLimits.PerRecipientPerDay = 2;
        var form = await FormAsync(Email(ToVisitor()));
        var stored = new List<StoredEmail>();
        StoredActionRun? last = null;

        foreach (var spelling in new[] { "stranger@jfb.test", "STRANGER@jfb.test", "stranger+a@jfb.test" })
        {
            var entry = await SubmitAsync(form, $$"""{"name":"Ada","email":"{{spelling}}"}""");
            last = (await DrainAsync(entry.Id)).Single();
            stored.AddRange(await Emails.ListForEntryAsync(entry.Id));
        }

        await SendDueAsync();

        Assert.Multiple(() =>
        {
            Assert.That(stored.Select(mail => mail.State).Last(), Is.EqualTo(EmailState.Limited));
            Assert.That(last!.State, Is.EqualTo(ActionRunState.Succeeded), "the log shows the mail held back; nothing is wrong with the action");
            Assert.That(Written(), Has.Count.EqualTo(2));
        });
    }

    [TestCase("""{"name":"Ada"}""")]
    [TestCase("""{"name":"Ada","email":""}""")]
    public async Task NoAddress_MeansNoMail_AndTheRunSucceeds(string values)
    {
        var form = await FormAsync(Email(ToVisitor()));
        var entry = await SubmitAsync(form, values);

        var runs = await DrainAsync(entry.Id);

        Assert.Multiple(async () =>
        {
            Assert.That(runs.Single().State, Is.EqualTo(ActionRunState.Succeeded));
            Assert.That(await Emails.ListForEntryAsync(entry.Id), Is.Empty);
        });
    }

    [Test]
    public async Task TheRecipientMap_ChoosesTheAddress_WhichVisitorsNeverSee()
    {
        var map = $$""","recipientMap":{"field":"{{Topic.Key}}","routes":[{"value":"sales","to":["hidden-sales@jfb.test"]},{"value":"support","to":["hidden-support@jfb.test","hidden-more@jfb.test"]}]}""";
        var form = await FormAsync(Email(Texts(to: "[]", more: map)));

        var support = await SubmitAsync(form, """{"name":"Ada","topic":"support"}""");
        var unknown = await SubmitAsync(form, """{"name":"Ada","topic":"other"}""");
        await DrainAsync(support.Id);
        await DrainAsync(unknown.Id);
        using var shown = await GetAsync($"{Root}/forms/{form}");

        var projection = await shown.Content.ReadAsStringAsync();
        Assert.Multiple(async () =>
        {
            Assert.That((await Emails.ListForEntryAsync(support.Id)).Single().To, Is.EqualTo("hidden-support@jfb.test, hidden-more@jfb.test"));
            Assert.That(await Emails.ListForEntryAsync(unknown.Id), Is.Empty, "no route, no mail");
            Assert.That(projection, Does.Not.Contain("hidden-").And.Not.Contain("recipientMap"));
        });
    }

    [Test]
    public async Task TheCreator_GetsTheMail_AtTheAddressOfTheirAccount()
    {
        var users = GetRequiredService<IUserService>();
        var admin = (await users.GetAsync(Constants.Security.SuperUserKey))!;
        admin.Email = "creator@jfb.test";
        users.Save(admin);
        var created = await Forms.CreateAsync(new NewFormRequest(Guid.NewGuid().ToString("N"), UserKey: admin.Key));
        await SaveAndPublishAsync(created.Key, definition => Headless(WithFields(definition, [NameField])) with { Actions = [Email(Texts(to: "[]", more: ""","toCreator":true"""))] });

        var mail = await OneAsync(created.Key, """{"name":"Ada"}""");

        Assert.That(mail.To.Mailboxes.Single().Address, Is.EqualTo("creator@jfb.test"));
    }

    [Test]
    public async Task ReplyToAField_TakesAnAddress_AndLeavesOutWhatIsNone()
    {
        var more = $$""","replyToField":"{{EmailField.Key}}","cc":["copy@jfb.test"],"bcc":["hidden@jfb.test"],"from":"special@jfb.test"}""";
        var form = await FormAsync(Email(Texts(more: more[..^1])));

        var mail = await OneAsync(form, """{"name":"Ada","email":"ada@jfb.test"}""");

        Assert.Multiple(() =>
        {
            Assert.That(mail.ReplyTo.Mailboxes.Single().Address, Is.EqualTo("ada@jfb.test"));
            Assert.That(mail.Cc.Mailboxes.Single().Address, Is.EqualTo("copy@jfb.test"));
            Assert.That(mail.From.Mailboxes.Single().Address, Is.EqualTo("special@jfb.test"));
        });
    }

    [Test]
    public async Task APlainTextAction_SendsOnePlainPart_WithItsValuesAsTheyAre()
    {
        var form = await FormAsync(Email(Texts(body: "Hello {{ field.name }}: {{ field.message }}", more: ""","plainText":true""")));

        var mail = await OneAsync(form, """{"name":"Ada","message":"a < b & c"}""");

        Assert.Multiple(() =>
        {
            Assert.That(((TextPart)mail.Body!).ContentType.MimeType, Is.EqualTo("text/plain"));
            Assert.That(((TextPart)mail.Body!).Text.Trim(), Is.EqualTo("Hello Ada: a < b & c"));
        });
    }

    [Test]
    public async Task ALayout_IsTheActionsWhenItNamesOne()
    {
        var form = await FormAsync(Email(Texts(more: $",\"layout\":\"{EmailLayouts.LetterKey}\"")));

        var mail = await OneAsync(form, """{"name":"Ada"}""");

        Assert.That(HtmlOf(mail), Does.Not.Contain("border:1px solid"));
    }

    [Test]
    public async Task AView_WritesTheBody_AndOneTheSiteLacksGivesWayToTheBody()
    {
        var withView = await FormAsync(Email(Texts(more: $",\"view\":\"{FakeEmailView.Name}\"")));
        var without = await FormAsync(Email(Texts(body: "<p>The action's own</p>", more: ",\"view\":\"missing\"")));

        var first = await SubmitAsync(withView, """{"name":"Ada"}""");
        var second = await SubmitAsync(without, """{"name":"Ada"}""");
        await DrainAsync(first.Id);
        await DrainAsync(second.Id);

        Assert.Multiple(async () =>
        {
            Assert.That((await Emails.ListForEntryAsync(first.Id)).Single().HtmlBody, Does.Contain("From the view:").And.Contain(", 1,").And.Contain("Entry 1").And.Contain("Name: Ada"));
            Assert.That((await Emails.ListForEntryAsync(second.Id)).Single().HtmlBody, Does.Contain("The action's own"));
        });
    }

    [Test]
    public async Task AttachedFields_SendTheirFiles_AndNoOthers()
    {
        var other = new FormField { Key = Guid.NewGuid(), Type = "file", Alias = "other", Label = new LocalizedText("Other") };
        var form = await PublishAsync(
            name: Guid.NewGuid().ToString("N"),
            change: definition => definition with { Actions = [Email(Texts(more: $$""","attachments":["{{Upload.Key}}"]"""))] },
            fields: [NameField, Upload, other]);
        var row = (await FormRows.GetAsync(form))!;
        var fields = await FormRows.ListFieldsAsync(row.Id);
        var chosen = await FileAsync(row.Id, Upload.Key, "chosen.pdf");
        var skipped = await FileAsync(row.Id, other.Key, "skipped.pdf");
        var entry = (await Entries.CreateAsync(new NewEntry
        {
            Key = Guid.NewGuid(),
            FormId = row.Id,
            FormVersionId = row.PublishedVersionId!.Value,
            State = EntryState.Submitted,
            SubmissionId = Guid.NewGuid(),
            SubmissionHash = new string('a', 64),
            Culture = "en-US",
            FileIds = [chosen.Id, skipped.Id],
            Values =
            [
                new NewEntryValue(fields.Single(field => field.Values.FieldKey == Upload.Key).Id, "chosen.pdf", FileId: chosen.Id),
                new NewEntryValue(fields.Single(field => field.Values.FieldKey == other.Key).Id, "skipped.pdf", FileId: skipped.Id),
            ],
        }))!;
        var action = (await Forms.GetDraftAsync(form))!.Definition!.Actions.Single();
        await Runs.AddAsync(entry.Id, entry.FormVersionId, Guid.NewGuid(), [new NewActionRun(action.Key, action.Type, action.Trigger, 0, false)]);

        await DrainAsync(entry.Id);
        await SendDueAsync();

        Assert.That(Written().Single().Attachments.OfType<MimePart>().Select(part => part.FileName), Is.EqualTo(new[] { "chosen.pdf" }));
    }

    [Test]
    public async Task AFileQuarantinedBeforeItsMailLeaves_StaysOut()
    {
        var form = await PublishAsync(name: Guid.NewGuid().ToString("N"), change: definition => definition with { Actions = [Email(Texts(more: $$""","attachments":["{{Upload.Key}}"]"""))] }, fields: [NameField, Upload]);
        var row = (await FormRows.GetAsync(form))!;
        var field = (await FormRows.ListFieldsAsync(row.Id)).Single(item => item.Values.FieldKey == Upload.Key);
        var file = await FileAsync(row.Id, Upload.Key, "chosen.pdf");
        var entry = (await Entries.CreateAsync(new NewEntry
        {
            Key = Guid.NewGuid(),
            FormId = row.Id,
            FormVersionId = row.PublishedVersionId!.Value,
            State = EntryState.Submitted,
            SubmissionId = Guid.NewGuid(),
            SubmissionHash = new string('a', 64),
            Culture = "en-US",
            FileIds = [file.Id],
            Values = [new NewEntryValue(field.Id, "chosen.pdf", FileId: file.Id)],
        }))!;
        var action = (await Forms.GetDraftAsync(form))!.Definition!.Actions.Single();
        await Runs.AddAsync(entry.Id, entry.FormVersionId, Guid.NewGuid(), [new NewActionRun(action.Key, action.Type, action.Trigger, 0, false)]);

        // Only the queue of runs goes: the mail waits while the scan finds something.
        await GetRequiredService<IEnumerable<IOutboxQueue>>().OfType<ActionRunQueue>().Single().PassAsync("test", DateTimeOffset.MaxValue, CancellationToken.None);
        await Entries.ChangeScanStateAsync(file.Id, FileScanStates.Clean, FileScanStates.Quarantined);

        await SendDueAsync();

        Assert.That(Written().Single().Attachments.OfType<MimePart>(), Is.Empty);
    }

    [TestCase(false, TestName = "A sensitive file stays out")]
    [TestCase(true, TestName = "A sensitive file goes where the action names it")]
    public async Task AnAttachedFile_OfASensitiveField_GoesOnlyWhereTheActionNamesIt(bool named)
    {
        var hidden = new FormField { Key = Guid.NewGuid(), Type = "file", Alias = "hiddenUpload", Label = new LocalizedText("Hidden"), Sensitive = true };
        var more = $$""","attachments":["{{hidden.Key}}"]""" + (named ? $$""","includeSensitive":["{{hidden.Key}}"]""" : string.Empty);
        var form = await PublishAsync(name: Guid.NewGuid().ToString("N"), change: definition => definition with { Actions = [Email(Texts(more: more))] }, fields: [NameField, hidden]);
        var row = (await FormRows.GetAsync(form))!;
        var field = (await FormRows.ListFieldsAsync(row.Id)).Single(item => item.Values.FieldKey == hidden.Key);
        var file = await FileAsync(row.Id, hidden.Key, "hidden.pdf");
        var entry = (await Entries.CreateAsync(new NewEntry
        {
            Key = Guid.NewGuid(),
            FormId = row.Id,
            FormVersionId = row.PublishedVersionId!.Value,
            State = EntryState.Submitted,
            SubmissionId = Guid.NewGuid(),
            SubmissionHash = new string('a', 64),
            Culture = "en-US",
            FileIds = [file.Id],
            Values = [new NewEntryValue(field.Id, "hidden.pdf", FileId: file.Id)],
        }))!;
        var action = (await Forms.GetDraftAsync(form))!.Definition!.Actions.Single();
        await Runs.AddAsync(entry.Id, entry.FormVersionId, Guid.NewGuid(), [new NewActionRun(action.Key, action.Type, action.Trigger, 0, false)]);

        await DrainAsync(entry.Id);
        await SendDueAsync();

        Assert.That(Written().Single().Attachments.OfType<MimePart>().Select(part => part.FileName), named ? Is.EqualTo(new[] { "hidden.pdf" }) : Is.Empty);
    }

    [Test]
    public async Task ATestRun_ReachesOnlyWhoAsks_AndIsLoggedAsATest()
    {
        var form = await FormAsync(Email(Texts(more: $$""","toField":"{{EmailField.Key}}","cc":["copy@jfb.test"]""")));
        var draft = (await Forms.GetDraftAsync(form))!.Definition!;
        var tests = GetRequiredService<ActionTestRuns>();

        var test = await tests.TestAsync(form, draft.Actions[0].Key, new ActionTestUser(Guid.NewGuid(), "ada@jfb.test", "Ada"), confirmed: false);
        var nobody = await tests.TestAsync(form, draft.Actions[0].Key, new ActionTestUser(Guid.NewGuid(), null, "Ben"), confirmed: false);
        await SendDueAsync();

        var written = Written().Single();
        Assert.Multiple(() =>
        {
            Assert.That(test.Result!.Outcome, Is.EqualTo(ActionTestOutcome.Sent));
            Assert.That(test.Result.Message, Does.Contain("ada@jfb.test"));
            Assert.That(nobody.Result!.Outcome, Is.EqualTo(ActionTestOutcome.Failed));
            Assert.That(written.To.Mailboxes.Single().Address, Is.EqualTo("ada@jfb.test"));
            Assert.That(written.Cc, Is.Empty);
        });
    }

    [TestCase("""{"subject":{"":"S"},"body":{"":"B"}}""", "to", TestName = "no recipient at all")]
    [TestCase("""{"to":["a@jfb.test"],"body":{"":"B"}}""", "subject", TestName = "no subject")]
    [TestCase("""{"to":["a@jfb.test"],"subject":{"":"S"}}""", "body", TestName = "no body and no view")]
    [TestCase("""{"to":["not an address"],"subject":{"":"S"},"body":{"":"B"}}""", "to", TestName = "a bad address")]
    [TestCase("""{"to":["a@jfb.test"],"cc":["nope"],"subject":{"":"S"},"body":{"":"B"}}""", "cc", TestName = "a bad copy")]
    [TestCase("""{"to":["a@jfb.test"],"bcc":["nope"],"subject":{"":"S"},"body":{"":"B"}}""", "bcc", TestName = "a bad blind copy")]
    [TestCase("""{"to":["a@jfb.test"],"from":"Team <a@jfb.test>","subject":{"":"S"},"body":{"":"B"}}""", "from", TestName = "a bad sender")]
    [TestCase("""{"toField":"c0000000-0000-4000-8000-000000000002","subject":{"":"S"},"body":{"":"B"}}""", "toField", TestName = "to a field that is no email field")]
    [TestCase("""{"to":["a@jfb.test"],"replyToField":"c0000000-0000-4000-8000-000000000099","subject":{"":"S"},"body":{"":"B"}}""", "replyToField", TestName = "reply to a field the form lacks")]
    [TestCase("""{"to":["a@jfb.test"],"includeSensitive":["c0000000-0000-4000-8000-000000000099"],"subject":{"":"S"},"body":{"":"B"}}""", "includeSensitive", TestName = "a sensitive field the form lacks")]
    [TestCase("""{"to":["a@jfb.test"],"attachments":["c0000000-0000-4000-8000-000000000099"],"subject":{"":"S"},"body":{"":"B"}}""", "attachments", TestName = "an attachment the form lacks")]
    [TestCase("""{"to":["a@jfb.test"],"view":"../secret","subject":{"":"S"}}""", "view", TestName = "a view outside its folder")]
    [TestCase("""{"to":["a@jfb.test"],"recipientMap":{"nope":1},"subject":{"":"S"},"body":{"":"B"}}""", "recipientMap", TestName = "not a map")]
    [TestCase("""{"recipientMap":{"field":"c0000000-0000-4000-8000-000000000099","routes":[{"value":"a","to":["a@jfb.test"]}]},"subject":{"":"S"},"body":{"":"B"}}""", "recipientMap", TestName = "a map of a field the form lacks")]
    [TestCase("""{"recipientMap":{"field":"c0000000-0000-4000-8000-000000000004","routes":[{"value":"a","to":["nope"]}]},"subject":{"":"S"},"body":{"":"B"}}""", "recipientMap", TestName = "a map with a bad address")]
    [TestCase("""{"recipientMap":{"field":"c0000000-0000-4000-8000-000000000004","routes":[]},"subject":{"":"S"},"body":{"":"B"}}""", "recipientMap", TestName = "a map without routes")]
    [TestCase("""{"recipientMap":{"field":"c0000000-0000-4000-8000-000000000004","routes":null},"subject":{"":"S"},"body":{"":"B"}}""", "recipientMap", TestName = "a map whose routes are null")]
    [TestCase("""{"recipientMap":{"field":"c0000000-0000-4000-8000-000000000004","routes":[{"value":null,"to":["a@jfb.test"]}]},"subject":{"":"S"},"body":{"":"B"}}""", "recipientMap", TestName = "a route without a value")]
    [TestCase("""{"recipientMap":{"field":"c0000000-0000-4000-8000-000000000004","routes":[{"value":"renamed","to":["a@jfb.test"]}]},"subject":{"":"S"},"body":{"":"B"}}""", "recipientMap", TestName = "a route that is no option")]
    [TestCase("""{"recipientMap":{"field":"c0000000-0000-4000-8000-000000000002","routes":[{"value":"a","to":["a@jfb.test"]}]},"subject":{"":"S"},"body":{"":"B"}}""", "recipientMap", TestName = "a map of a field without options")]
    [TestCase("""{"to":["a@jfb.test"],"subject":{"":"S {{ form.name"},"body":{"":"B"}}""", "subject", TestName = "a subject whose tag does not close")]
    [TestCase("""{"to":["a@jfb.test"],"subject":{"":"S"},"body":{"":"B","de":"<a href=\"{{ field.message }}\">x</a>"}}""", "body", TestName = "a body with a tag in markup, in any culture")]
    public void BadSettings_AreRefusedWhenPublishing(string settings, string setting)
    {
        var refused = Assert.ThrowsAsync<FormBuilderValidationException>(() => FormAsync(Email(settings)));

        Assert.That(refused!.Problems.Select(problem => problem.Path), Has.Some.Contains(setting));
    }

    [Test]
    public async Task OnAFormThatStoresNothing_TheMailLeaves_AndThenTheEntryGoes()
    {
        var form = await PublishAsync(
            name: Guid.NewGuid().ToString("N"),
            change: definition => definition with
            {
                Actions = [Email(Texts())],
                Settings = definition.Settings with { Submission = definition.Settings.Submission with { Store = EntryStorage.Never } },
            },
            fields: [NameField]);
        var entry = await SubmitAsync(form, """{"name":"Ada"}""");

        await DrainAsync(entry.Id);
        await SendDueAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(Written().Single().Subject, Is.EqualTo("Entry 1"));
            Assert.That(await Entries.GetByIdAsync(entry.Id), Is.Null, "nothing holds it any more");
        });
    }

    [Test]
    public async Task AVisitorsMail_CarriesNoCopyReplyToOrFile_AndACopyGoesAsAMailOfItsOwn()
    {
        var more = $$""","toField":"{{EmailField.Key}}","replyToField":"{{EmailField.Key}}","cc":["copy@jfb.test"],"bcc":["hidden@jfb.test"]""";
        var form = await FormAsync(Email(Texts(to: "[]", more: more)));
        var entry = await SubmitAsync(form, """{"name":"Ada","email":"stranger@jfb.test"}""");

        await DrainAsync(entry.Id);
        await SendDueAsync();

        var written = Written();
        var visitor = written.Single(mail => mail.To.Mailboxes.First().Address == "stranger@jfb.test");
        Assert.Multiple(async () =>
        {
            Assert.That(written, Has.Count.EqualTo(2));
            Assert.That(visitor.Cc, Is.Empty);
            Assert.That(visitor.ReplyTo, Is.Empty);
            Assert.That(written.Single(mail => mail != visitor).To.Mailboxes.Select(box => box.Address), Is.EqualTo(new[] { "copy@jfb.test", "hidden@jfb.test" }));
            Assert.That((await Emails.ListForEntryAsync(entry.Id)).Count(mail => mail.RecipientKey is not null), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task AVisitorsMail_CarriesNoFile()
    {
        var form = await PublishAsync(
            name: Guid.NewGuid().ToString("N"),
            change: definition => definition with { Actions = [Email(Texts(to: "[]", more: $$""","toField":"{{EmailField.Key}}","attachments":["{{Upload.Key}}"]"""))] },
            fields: [NameField, EmailField, Upload]);
        var row = (await FormRows.GetAsync(form))!;
        var fields = await FormRows.ListFieldsAsync(row.Id);
        var file = await FileAsync(row.Id, Upload.Key, "bot.pdf");
        var entry = (await Entries.CreateAsync(new NewEntry
        {
            Key = Guid.NewGuid(),
            FormId = row.Id,
            FormVersionId = row.PublishedVersionId!.Value,
            State = EntryState.Submitted,
            SubmissionId = Guid.NewGuid(),
            SubmissionHash = new string('a', 64),
            Culture = "en-US",
            FileIds = [file.Id],
            Values =
            [
                new NewEntryValue(fields.Single(field => field.Values.FieldKey == EmailField.Key).Id, "stranger@jfb.test"),
                new NewEntryValue(fields.Single(field => field.Values.FieldKey == Upload.Key).Id, "bot.pdf", FileId: file.Id),
            ],
        }))!;
        var action = (await Forms.GetDraftAsync(form))!.Definition!.Actions.Single();
        await Runs.AddAsync(entry.Id, entry.FormVersionId, Guid.NewGuid(), [new NewActionRun(action.Key, action.Type, action.Trigger, 0, false)]);

        await DrainAsync(entry.Id);

        Assert.That((await Emails.ListForEntryAsync(entry.Id)).Single().Attachments, Is.Empty);
    }

    [Test]
    public async Task ASensitiveField_IsNoReplyTo_UnlessTheActionIncludesIt()
    {
        var sensitive = EmailField with { Sensitive = true };
        var without = await PublishAsync(name: Guid.NewGuid().ToString("N"), change: definition => definition with { Actions = [Email(Texts(more: $",\"replyToField\":\"{EmailField.Key}\""))] }, fields: [NameField, sensitive]);
        var with = await PublishAsync(name: Guid.NewGuid().ToString("N"), change: definition => definition with { Actions = [Email(Texts(more: $$""","replyToField":"{{EmailField.Key}}","includeSensitive":["{{EmailField.Key}}"]"""))] }, fields: [NameField, sensitive]);
        const string Values = """{"name":"Ada","email":"ada@jfb.test"}""";
        var first = await SubmitAsync(without, Values);
        var second = await SubmitAsync(with, Values);

        await DrainAsync(first.Id);
        await DrainAsync(second.Id);

        Assert.Multiple(async () =>
        {
            Assert.That((await Emails.ListForEntryAsync(first.Id)).Single().ReplyTo, Is.Null);
            Assert.That((await Emails.ListForEntryAsync(second.Id)).Single().ReplyTo, Is.EqualTo("ada@jfb.test"));
        });
    }

    [Test]
    public async Task AnUndoneApproval_SendsNoMail_AndOneThatStands_SendsItsMail()
    {
        var action = Email(Texts()) with { Trigger = ActionTrigger.Approved };
        var form = await PublishAsync(
            name: Guid.NewGuid().ToString("N"),
            change: definition => definition with
            {
                Actions = [action],
                Settings = definition.Settings with { Submission = definition.Settings.Submission with { Approval = ApprovalMode.Manual } },
            },
            fields: [NameField, EmailField, Message, Secret, Upload]);
        var changes = GetRequiredService<EntryStateChanges>();
        var undone = await SubmitAsync(form, """{"name":"Ada"}""");
        var stands = await SubmitAsync(form, """{"name":"Ben"}""");

        var change = await changes.ChangeAsync(form, [undone.Key], EntryState.Approved, Editor);
        await changes.ChangeAsync(form, [stands.Key], EntryState.Approved, Editor);
        await changes.UndoAsync(form, change.ChangeKey, Editor);
        Clock.Advance(EntrySpamFolder.UndoWindow);
        await DrainAsync(stands.Id);
        await SendDueAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(await Emails.ListForEntryAsync(undone.Id), Is.Empty);
            Assert.That((await Runs.ListForEntryAsync(undone.Id)).Single().LastError?.Code, Is.EqualTo(DeliveryError.Undone));
            Assert.That(Written(), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task ARunTriedAgain_DoesNotQueueItsMailTwice()
    {
        var form = await FormAsync(Email(Texts()));
        var entry = await SubmitAsync(form, """{"name":"Ada"}""");
        var run = (await DrainAsync(entry.Id)).Single();
        var action = (await Forms.GetDraftAsync(form))!.Definition!.Actions.Single();
        var scope = (await GetRequiredService<SubmissionEntries>().ScopeAsync(entry))!;

        // What an attempt finds when its commit went through and it heard an error: its run is one the outbox holds to try again.
        var again = await GetRequiredService<ActionTypeRegistry>().Find(action.Type)!.RunAsync(
            new ActionRunContext { Action = action, Trigger = action.Trigger, Scope = scope, Attempt = 2, DeliveryKey = "again", RunId = run.Id, EntryId = entry.Id },
            CancellationToken.None);

        Assert.Multiple(async () =>
        {
            Assert.That(again.Kind, Is.EqualTo(ActionResultKind.Succeeded));
            Assert.That(await Emails.ListForEntryAsync(entry.Id), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task AViewTheSiteLacks_WithNoBodyOfItsOwn_FailsTheRun()
    {
        var form = await FormAsync(Email($$$"""{"to":["{{{To}}}"],"subject":{"":"S"},"view":"missing"}"""));
        var entry = await SubmitAsync(form, """{"name":"Ada"}""");

        var run = (await DrainAsync(entry.Id)).Single();

        Assert.Multiple(async () =>
        {
            Assert.That(run.State, Is.EqualTo(ActionRunState.Failed));
            Assert.That(run.LastError!.Code, Is.EqualTo(EmailActionType.ViewMissing));
            Assert.That(await Emails.ListForEntryAsync(entry.Id), Is.Empty);
        });
    }

    [Test]
    public async Task AHostStoppingWhileTheMailIsWritten_TriesAgain_AsNothingIsQueued()
    {
        var form = await FormAsync(Email(Texts()));
        var entry = await SubmitAsync(form, """{"name":"Ada"}""");
        var type = GetRequiredService<FormBuilderActionTypeCollection>().OfType<EmailActionType>().Single();
        using var stopped = new CancellationTokenSource();
        await stopped.CancelAsync();
        var action = (await Forms.GetDraftAsync(form))!.Definition!.Actions.Single();
        var context = new ActionRunContext
        {
            Action = action,
            Trigger = ActionTrigger.Submitted,
            Scope = new Core.MergeTags.MergeTagScope { Form = (await Forms.GetDraftAsync(form))!.Definition!, FormName = "F", FormAlias = "f", Culture = "en" },
            Attempt = 1,
            DeliveryKey = "k",
            EntryId = entry.Id,
            RunId = 1,
        };

        var result = await type.RunAsync(context, stopped.Token);

        Assert.That((result.Kind, result.Code), Is.EqualTo((ActionResultKind.TryAgain, ActionAttempts.HostStopped)));
    }

    [Test]
    public async Task EntryTable_InText_ShowsAnUnansweredField_WithNoTrailingSpace()
    {
        var form = await FormAsync(Email(Texts(body: "<div>{{ entry.table }}</div>")));
        var entry = await SubmitAsync(form, """{"name":"Ada"}""");

        await DrainAsync(entry.Id);

        var mail = (await Emails.ListForEntryAsync(entry.Id)).Single();
        Assert.That(mail.TextBody, Does.Contain("Name: Ada").And.Contain("Topic"));
    }

    [TestCase(false, TestName = "while the default is off, a new form has no action")]
    [TestCase(true, TestName = "while the default is on, a new form starts with the action to its creator")]
    public async Task ABlankForm_FollowsTheNewFormDefault(bool on)
    {
        await NotifyCreatorAsync(on);

        var created = await Forms.CreateAsync(new NewFormRequest(Guid.NewGuid().ToString("N"), UserKey: Editor));

        var actions = (await Forms.GetDraftAsync(created.Key))!.Definition!.Actions;
        Assert.That(actions.Select(action => action.Type), Is.EqualTo(on ? new[] { EmailActionType.Alias } : []));
        if (on)
        {
            Assert.That(actions[0].Settings!.TryGetProperty("toCreator", out var toCreator) && toCreator.GetBoolean(), Is.True);
        }
    }

    [TestCase(false, 0)]
    [TestCase(true, 1)]
    public async Task TheContactTemplate_FollowsTheNewFormDefault(bool on, int count)
    {
        await NotifyCreatorAsync(on);

        var created = await GetRequiredService<IFormBuilderFormExchangeService>().CreateFromTemplateAsync(new TemplateFormRequest(BuiltInFormTemplates.ContactKey, UserKey: Editor));

        Assert.That((await Forms.GetDraftAsync(created!.Key))!.Definition!.Actions.Count(action => action.Type == EmailActionType.Alias), Is.EqualTo(count));
    }

    [Test]
    public async Task TheNewFormsAction_IsOneAFormCanPublish_AndItNotifiesTheCreator()
    {
        await NotifyCreatorAsync(true);
        var users = GetRequiredService<IUserService>();
        var admin = (await users.GetAsync(Constants.Security.SuperUserKey))!;
        admin.Email = "creator@jfb.test";
        users.Save(admin);
        var created = await Forms.CreateAsync(new NewFormRequest(Guid.NewGuid().ToString("N"), UserKey: admin.Key));
        await SaveAndPublishAsync(created.Key, definition => Headless(WithFields(definition, [NameField])));

        var mail = await OneAsync(created.Key, """{"name":"Ada"}""");

        Assert.Multiple(() =>
        {
            Assert.That(mail.To.Mailboxes.Single().Address, Is.EqualTo("creator@jfb.test"));
            Assert.That(mail.Subject, Does.StartWith("New entry in "));
            Assert.That(HtmlOf(mail), Does.Contain("Ada"));
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.Services.AddSingleton<MemoryFileStore>();
        builder.Services.AddUnique<IFormBuilderFileStore>(provider => provider.GetRequiredService<MemoryFileStore>());
        builder.Services.AddSingleton<IFormBuilderEmailView, FakeEmailView>();
    }

    private async Task NotifyCreatorAsync(bool on)
    {
        var settings = GetRequiredService<Core.Settings.IFormBuilderSettingsService>();
        var current = await settings.GetAsync<Core.Settings.GeneralSettings>();
        await settings.SaveAsync(current.Value with { NewForms = new Core.Settings.NewFormDefaults { NotifyCreator = on } }, current.Version, Editor);
    }

    private async Task<StoredEntryFile> FileAsync(int formId, Guid field, string name)
    {
        var id = FormBuilderFileId.New();
        await Files.SaveAsync(id, new MemoryStream(new byte[3]));
        return (await Entries.AddFileAsync(new NewEntryFile(Guid.NewGuid(), formId, field, name, id, "application/pdf", 3, new string('a', 64), FileScanStates.Clean)))!;
    }
}
