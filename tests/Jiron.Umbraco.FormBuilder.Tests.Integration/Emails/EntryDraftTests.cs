using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using MimeKit;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.Navigation;
using Umbraco.Cms.Core.Strings;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Emails;

/// <summary>Save and resume, from a draft through its mail and its link to the entry it becomes (ROADMAP 8.6, ADR-0163).</summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed partial class EntryDraftTests : EmailTestBase
{
    private const string Address = "ada@jfb.test";

    private static readonly FormField EmailField = new() { Key = Guid.Parse("c4000000-0000-4000-8000-000000000001"), Type = "email", Alias = "contact", Label = new LocalizedText("Email") };

    private static readonly FormField Hidden = new() { Key = Guid.Parse("c4000000-0000-4000-8000-000000000002"), Type = "text", Alias = "secretNote", Label = new LocalizedText("Note"), Sensitive = true };

    private static readonly FormField Consent = new() { Key = Guid.Parse("c4000000-0000-4000-8000-000000000003"), Type = "consent", Alias = "agree", Label = new LocalizedText("Agree"), Settings = SettingsObject.Parse("""{"text":{"":"I agree."}}""") };

    private static readonly FormField Seat = new()
    {
        Key = Guid.Parse("c4000000-0000-4000-8000-000000000004"),
        Type = "dropdown",
        Alias = "seat",
        Label = new LocalizedText("Seat"),
        Options = new FieldOptions { Items = [new FieldOption { Value = "front", Label = new LocalizedText("Front"), Places = 1 }] },
    };

    private static readonly FormField Code = new()
    {
        Key = Guid.Parse("c4000000-0000-4000-8000-000000000005"),
        Type = "text",
        Alias = "code",
        Label = new LocalizedText("Code"),
        Validation = [new UniquePerFormCheck()],
    };

    private static readonly FormField Mandatory = new() { Key = Guid.Parse("c4000000-0000-4000-8000-000000000006"), Type = "text", Alias = "mandatory", Label = new LocalizedText("Mandatory"), Required = true };

    private Guid _page;

    private readonly HashSet<string> _seen = [];

    private IFormBuilderSettingsService SiteSettings => GetRequiredService<IFormBuilderSettingsService>();

    private static Func<FormDefinition, FormDefinition> Saves(int days = 10, params FormAction[] actions) => definition => definition with
    {
        Actions = actions,
        Settings = definition.Settings with { Submission = definition.Settings.Submission with { Resume = new SaveAndResume { Days = days } } },
    };

    private Task<Guid> FormAsync(params FormAction[] actions) =>
        PublishAsync(name: "Registration", change: Saves(10, actions), fields: [NameField, EmailField, Hidden, Consent, Seat, Code, Mandatory]);

    [GeneratedRegex("href=\"(?<link>[^\"]*)\"")]
    private static partial Regex LinkPattern();

    private static string LinkOf(MimeMessage mail) => WebUtility.HtmlDecode(LinkPattern().Match(((TextPart)mail.Body!).Text).Groups["link"].Value);

    private static string TokenOf(string link) => link[(link.IndexOf("#jfb-resume=", StringComparison.Ordinal) + 12)..];

    private async Task<HttpResponseMessage> DraftAsync(Guid form, string values = """{"name":"Ada"}""", string more = "", string email = Address)
    {
        using var content = new StringContent($$"""{"revision":1,"contentKey":"{{_page}}","email":"{{email}}","values":{{values}}{{more}}}""", Encoding.UTF8, "application/json");
        return await SendAsync(HttpMethod.Post, $"{Root}/forms/{form}/drafts", request => request.Content = content);
    }

    private async Task<HttpResponseMessage> ResumeAsync(string token)
    {
        using var content = new StringContent(new JsonObject { ["token"] = token }.ToJsonString(), Encoding.UTF8, "application/json");
        return await SendAsync(HttpMethod.Post, $"{Root}/drafts/resume", request => request.Content = content);
    }

    private async Task<string> SavedTokenAsync(Guid form, string values = """{"name":"Ada"}""")
    {
        using var answer = await DraftAsync(form, values);
        Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.OK), await answer.Content.ReadAsStringAsync());
        await SendDueAsync();
        return Written().Select(LinkOf).Select(TokenOf).First(_seen.Add);
    }

    private async Task<HttpResponseMessage> SubmitDraftAsync(Guid form, string token, string values)
    {
        using var content = new StringContent($$"""{"revision":1,"resume":"{{token}}","values":{{values}}}""", Encoding.UTF8, "application/json");
        return await SendAsync(HttpMethod.Post, $"{Root}/forms/{form}/entries", request => request.Content = content);
    }

    private async Task<IReadOnlyList<StoredEntry>> AllEntriesAsync(Guid form)
    {
        var row = (await FormRows.GetAsync(form))!;
        var found = new List<StoredEntry>();
        foreach (var id in await Entries.ListIdsAsync(row.Id, 1000))
        {
            found.Add((await Entries.GetByIdAsync(id))!);
        }

        return found;
    }

    [SetUp]
    public async Task GiveTheSiteItsAddressAndAPage()
    {
        var current = await SiteSettings.GetAsync<GeneralSettings>();
        await SiteSettings.SaveAsync(current.Value with { BaseUrl = "https://forms.jfb.test" }, current.Version, Editor);
        var types = GetRequiredService<IContentTypeService>();
        var contents = GetRequiredService<IContentService>();
        var type = new global::Umbraco.Cms.Core.Models.ContentType(GetRequiredService<IShortStringHelper>(), -1) { Alias = "jfbPage", Name = "Page", AllowedAsRoot = true };
        await types.CreateAsync(type, Constants.Security.SuperUserKey);
        var page = new Content("Contact", -1, type);
        contents.Save(page);
        contents.Publish(page, ["*"]);
        await GetRequiredService<IDocumentNavigationManagementService>().RebuildAsync();
        await GetRequiredService<IPublishStatusManagementService>().InitializeAsync(CancellationToken.None);
        _page = page.Key;
    }

    [Test]
    public async Task ADraft_AnswersWithItsToken_ForThePageThatSaved_AndTheMailHoldsTheLink_InTheAddresssFragment()
    {
        var form = await FormAsync(Action("first"));

        using var answer = await DraftAsync(form);
        var body = await answer.Content.ReadAsStringAsync();
        await SendDueAsync();

        var mail = Written().Single();
        var link = LinkOf(mail);
        var token = TokenOf(link);
        var draft = (await AllEntriesAsync(form)).Single();
        Assert.Multiple(async () =>
        {
            Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.OK), body);
            Assert.That(JsonNode.Parse(body)!["expiresUtc"], Is.Not.Null);
            Assert.That(JsonNode.Parse(body)!["resume"]!.GetValue<string>(), Is.EqualTo(token), "the page that saved names the draft to the entry that follows (ADR-0252)");
            Assert.That(body, Does.Not.Contain("forms.jfb.test"), "never the link's address");
            Assert.That(link, Does.StartWith("https://forms.jfb.test/").And.Contain("#jfb-resume="));
            Assert.That(new Uri(link).Query, Is.Empty, "a token in a query reaches server logs");
            Assert.That(mail.To.Mailboxes.Single().Address, Is.EqualTo(Address));
            Assert.That(((TextPart)mail.Body!).Text, Does.Contain("Registration").And.Contain("10 days"));
            Assert.That((await Emails.ListForEntryAsync(draft.Id)).Single().Kind, Is.EqualTo(EmailKind.Resume));
            Assert.That(draft.State, Is.EqualTo(EntryState.Draft));
            Assert.That(draft.ResumeTokenHash, Is.EqualTo(EntryDrafts.HashOf(token)).And.Not.EqualTo(token));
            Assert.That(await Runs.ListForEntryAsync(draft.Id), Is.Empty);
            Assert.That(Scripted.Started, Is.Empty);
            Assert.That(await Events.ListForEntryAsync(draft.Id), Has.None.Matches<StoredEntryEvent>(item => item.Details.Contains(Address, StringComparison.Ordinal)), "only the mail keeps the address");
        });
    }

    [Test]
    public async Task ALink_GivesTheAnswersBack_AsOftenAsItIsOpened_WithThePage()
    {
        var form = await FormAsync();
        var first = (await GetRequiredService<IFormBuilderFormService>().GetDraftAsync(form))!.Definition!.Pages[0].Key;
        using var saved = await DraftAsync(form, """{"name":"Ada","contact":"ada@jfb.test"}""", $$""","pageKey":"{{first}}" """);
        await SendDueAsync();
        var token = TokenOf(LinkOf(Written().Single()));

        using var once = await ResumeAsync(token);
        using var again = await ResumeAsync(token);
        var body = await JsonAsync(once);

        Assert.Multiple(async () =>
        {
            Assert.That(once.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(body["form"]!.GetValue<Guid>(), Is.EqualTo(form));
            Assert.That(body["revision"]!.GetValue<int>(), Is.EqualTo(1));
            Assert.That(body["pageKey"]!.GetValue<Guid>(), Is.EqualTo(first));
            Assert.That(body["values"]!["name"]![0]!.GetValue<string>(), Is.EqualTo("Ada"));
            Assert.That(body["values"]!["contact"]![0]!.GetValue<string>(), Is.EqualTo("ada@jfb.test"));
            Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That((await JsonAsync(again))["values"]!.ToJsonString(), Is.EqualTo(body["values"]!.ToJsonString()));
        });
    }

    [Test]
    public async Task ADraft_NeedNotBeWhole_AndKeepsNoFileConsentOrSensitiveAnswer_NorOneThatDoesNotRead()
    {
        var form = await FormAsync();

        var token = await SavedTokenAsync(form, """{"name":"Ada","contact":"no address","secretNote":"hidden-note","agree":"true"}""");
        using var back = await ResumeAsync(token);

        var values = (await JsonAsync(back))["values"]!.AsObject();
        Assert.Multiple(() =>
        {
            Assert.That(values["name"]!.AsArray().Select(value => value!.GetValue<string>()), Is.EqualTo(new[] { "Ada" }));
            Assert.That(values.Where(value => value.Value!.AsArray().Count == 0).Select(value => value.Key), Is.EquivalentTo(new[] { "contact", "seat", "code", "mandatory" }), "left empty, or not read: the page empties what it starts them with");
            Assert.That(values.Select(value => value.Key), Has.None.EqualTo("secretNote").And.None.EqualTo("agree"), "what a draft never keeps is not named, not even as empty");
        });
    }

    [Test]
    public async Task TheFieldsLeftEmpty_AreNamedOnlyWhileTheSiteServesTheVersionTheDraftWasSavedWith()
    {
        var form = await FormAsync();
        var token = await SavedTokenAsync(form);

        // Published again since: a field new to the form was never left empty by this visitor.
        var added = new FormField { Key = Guid.Parse("c4000000-0000-4000-8000-000000000030"), Type = "text", Alias = "added", Label = new LocalizedText("Added") };
        await SaveAndPublishAsync(form, definition => WithFields(definition, NameField, EmailField, Hidden, Consent, Seat, Code, Mandatory, added));
        using var back = await ResumeAsync(token);

        var values = (await JsonAsync(back))["values"]!.AsObject();
        Assert.That(values.Select(value => value.Key), Is.EqualTo(new[] { "name" }));
    }

    [Test]
    public async Task ADraftOfNothingYet_IsSaved_AsItsLinkComesBackToThePage()
    {
        var form = await FormAsync();

        using var saved = await DraftAsync(form, "{}");

        Assert.Multiple(async () =>
        {
            Assert.That(saved.StatusCode, Is.EqualTo(HttpStatusCode.OK), await saved.Content.ReadAsStringAsync());
            Assert.That(await AllEntriesAsync(form), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task WhatALinkGivesBack_IsWhatASubmissionPosts_ForPartsGridsRowsAndOwnTexts()
    {
        var speed = new GridRow { Key = Guid.Parse("c4000000-0000-4000-8000-000000000010"), Label = new LocalizedText("Speed") };
        var person = new FormField { Key = Guid.Parse("c4000000-0000-4000-8000-000000000011"), Type = "name", Alias = "person", Label = new LocalizedText("Name") };
        var rating = new FormField
        {
            Key = Guid.Parse("c4000000-0000-4000-8000-000000000012"),
            Type = "grid",
            Alias = "rating",
            Label = new LocalizedText("Rating"),
            Options = new FieldOptions { Items = [new FieldOption { Value = "good", Label = new LocalizedText("Good") }, new FieldOption { Value = "bad", Label = new LocalizedText("Bad") }] },
            Rows = [speed],
        };
        var fruit = new FormField
        {
            Key = Guid.Parse("c4000000-0000-4000-8000-000000000013"),
            Type = "checkboxes",
            Alias = "fruit",
            Label = new LocalizedText("Fruit"),
            Settings = SettingsObject.Parse("""{"other":true}"""),
            Options = new FieldOptions { Items = [new FieldOption { Value = "a", Label = new LocalizedText("A") }, new FieldOption { Value = "b", Label = new LocalizedText("B") }] },
        };
        var guests = new FormField
        {
            Key = Guid.Parse("c4000000-0000-4000-8000-000000000014"),
            Type = "repeater",
            Alias = "guests",
            Label = new LocalizedText("Guests"),
            Fields = [new FormField { Key = Guid.Parse("c4000000-0000-4000-8000-000000000015"), Type = "text", Alias = "guestName", Label = new LocalizedText("Name") }],
        };
        var form = await PublishAsync(name: "Rich", change: Saves(), fields: [NameField, person, rating, fruit, guests]);
        var posted = $$"""{"name":"Ada","person.first":"Ada","person.last":"Lovelace","rating.{{speed.Key}}":"good","fruit":["a","b"],"guests":[{"guestName":"Ben"},{"guestName":"Cy"}]}""";

        var token = await SavedTokenAsync(form, posted);
        using var back = await ResumeAsync(token);
        var values = (await JsonAsync(back))["values"]!;
        using var again = await SendValuesAsync(form, values.ToJsonString());
        var stored = (await AllEntriesAsync(form)).Single(entry => entry.State != EntryState.Draft);

        Assert.Multiple(() =>
        {
            Assert.That(values["person.first"]![0]!.GetValue<string>(), Is.EqualTo("Ada"));
            Assert.That(values["person.last"]![0]!.GetValue<string>(), Is.EqualTo("Lovelace"));
            Assert.That(values[$"rating.{speed.Key}"]![0]!.GetValue<string>(), Is.EqualTo("good"));
            Assert.That(values["fruit"]!.AsArray().Select(value => value!.GetValue<string>()), Is.EqualTo(new[] { "a", "b" }));
            Assert.That(values["guests"]![1]!["guestName"]![0]!.GetValue<string>(), Is.EqualTo("Cy"));
            Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.OK), values.ToJsonString());
            Assert.That(stored, Is.Not.Null);
        });
    }

    [Test]
    public async Task AConsentWithItsWording_IsNotKeptEither()
    {
        var form = await FormAsync();
        using var read = await GetAsync($"{Root}/forms/{form}?culture=en-US");
        var hash = (await JsonAsync(read))["consents"]![Consent.Key.ToString()]!.GetValue<string>();

        using var saved = await DraftAsync(form, """{"name":"Ada","agree":true}""", $$""","consents":{"agree":"{{hash}}"}""");
        await SendDueAsync();
        using var back = await ResumeAsync(TokenOf(LinkOf(Written().Single())));

        Assert.Multiple(async () =>
        {
            Assert.That(saved.StatusCode, Is.EqualTo(HttpStatusCode.OK), await saved.Content.ReadAsStringAsync());
            var values = (await JsonAsync(back))["values"]!.AsObject();
            Assert.That(values["name"]!.AsArray(), Has.Count.EqualTo(1));
            Assert.That(values.Select(value => value.Key), Has.None.EqualTo("agree"));
        });
    }

    [Test]
    public async Task AFieldMarkedSensitiveSince_DoesNotComeBack()
    {
        var note = new FormField { Key = Guid.Parse("c4000000-0000-4000-8000-000000000020"), Type = "text", Alias = "note", Label = new LocalizedText("Note") };
        var form = await PublishAsync(name: "Marked later", change: Saves(), fields: [NameField, note]);
        var token = await SavedTokenAsync(form, """{"name":"Ada","note":"since then private"}""");

        await SaveAndPublishAsync(form, definition => WithFields(definition, NameField, note with { Sensitive = true }));
        using var back = await ResumeAsync(token);

        Assert.That((await JsonAsync(back))["values"]!.AsObject().Select(value => value.Key), Is.EqualTo(new[] { "name" }));
    }

    [Test]
    public async Task ADraft_HoldsNoPlace_AndTheSubmitTakesIt_OrFailsAsFull()
    {
        var form = await FormAsync();
        var token = await SavedTokenAsync(form, """{"name":"Ada","seat":"front"}""");
        var row = (await FormRows.GetAsync(form))!;
        var place = new OptionPlace(Seat.Key, "front");
        var whileADraft = (await Entries.GetPlacesTakenAsync(row.Id, [place])).GetValueOrDefault(place);

        using (var other = await SendValuesAsync(form, """{"name":"Ben","seat":"front","mandatory":"x"}"""))
        {
            Assert.That(other.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        using var late = await SubmitDraftAsync(form, token, """{"name":"Ada","seat":"front","mandatory":"x"}""");
        var refusal = await late.Content.ReadAsStringAsync();
        using var stillThere = await ResumeAsync(token);

        Assert.Multiple(() =>
        {
            Assert.That(whileADraft, Is.Zero);
            Assert.That(late.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity), refusal);
            Assert.That(refusal, Does.Contain("full"));
            Assert.That(stillThere.StatusCode, Is.EqualTo(HttpStatusCode.OK), "a draft the submit could not finish stays");
        });
    }

    [Test]
    public async Task ADraft_HoldsNoUniqueAnswer_AndTheSubmitFailsAtTheField_WhenAnotherTookIt()
    {
        var form = await FormAsync();
        var token = await SavedTokenAsync(form, """{"name":"Ada","code":"A-1"}""");

        using var other = await SendValuesAsync(form, """{"name":"Ben","code":"A-1","mandatory":"x"}""");
        using var late = await SubmitDraftAsync(form, token, """{"name":"Ada","code":"A-1","mandatory":"x"}""");
        var refusal = await late.Content.ReadAsStringAsync();

        Assert.Multiple(() =>
        {
            Assert.That(other.StatusCode, Is.EqualTo(HttpStatusCode.OK), "the draft does not hold it");
            Assert.That(late.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity), refusal);
            Assert.That(refusal, Does.Contain("\"path\":\"code\""));
        });
    }

    [Test]
    public async Task TheSubmit_MakesAnEntry_RunsItsActions_AndTakesTheDraftAway()
    {
        var form = await FormAsync(Action("first"));
        var token = await SavedTokenAsync(form);

        using var sent = await SubmitDraftAsync(form, token, """{"name":"Ada","seat":"front","mandatory":"x"}""");
        var entry = (await AllEntriesAsync(form)).Single();
        await DrainAsync(entry.Id);
        using var second = await ResumeAsync(token);

        var row = (await FormRows.GetAsync(form))!;
        var today = DateOnly.FromDateTime(Clock.GetUtcNow().UtcDateTime);
        var stats = await GetRequiredService<IDailyStatRepository>().ListAsync(row.Id, today.AddDays(-1), today.AddDays(1));
        Assert.Multiple(() =>
        {
            Assert.That(sent.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(entry.State, Is.Not.EqualTo(EntryState.Draft));
            Assert.That(Scripted.Started, Is.EqualTo(new[] { "first" }));
            Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.NotFound), "a link past its submit finds nothing");
            Assert.That(stats.Where(stat => stat.Metric == SubmissionEntries.SubmissionsMetric).Sum(stat => stat.Value), Is.EqualTo(1), "the draft never counted");
        });
    }

    [Test]
    public async Task ASubmitWithAnotherFormsLink_TakesNothingAway()
    {
        var form = await FormAsync();
        var token = await SavedTokenAsync(form);
        var other = await PublishAsync(name: "Other", change: Saves(), fields: [NameField]);

        using var sent = await SubmitDraftAsync(other, token, """{"name":"Ben"}""");
        using var back = await ResumeAsync(token);

        Assert.Multiple(() =>
        {
            Assert.That(sent.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(back.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        });
    }

    [Test]
    public async Task ADraftsOwnMail_StaysWhileTheDraftDoes_SoItIsFoundByItsAddress_AndGoesOnceTheDraftHas()
    {
        // A link that works longer than mails are kept, on a form that asks for no address of its own.
        var form = await PublishAsync(name: "Long", change: Saves(90), fields: [NameField]);
        var token = await SavedTokenAsync(form);
        var draft = (await AllEntriesAsync(form)).Single();
        var people = GetRequiredService<IPersonDataRepository>();
        var mailbox = Core.Email.EmailAddresses.KeyOf(Address)!;

        Clock.Advance(Core.Email.EmailOutbox.Keep + TimeSpan.FromDays(10));
        await Emails.PurgeAsync(10);
        var whileADraft = await Emails.ListForEntryAsync(draft.Id);
        var found = await people.FindByVisitorMailAsync(mailbox, 10);

        // The entry is sent: the draft goes, and its mail at the next purge.
        using var sent = await SubmitDraftAsync(form, token, """{"name":"Ada"}""");
        await Emails.PurgeAsync(10);
        var after = await people.FindByVisitorMailAsync(mailbox, 10);

        Assert.Multiple(() =>
        {
            Assert.That(whileADraft.Select(mail => mail.Kind), Is.EqualTo(new[] { EmailKind.Resume }), "kept past the mails' own time, as the draft keeps no address");
            Assert.That(found, Is.EqualTo(new[] { draft.Id }), "Privacy > People finds the draft by the address it was mailed to");
            Assert.That(sent.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(after, Has.None.EqualTo(draft.Id));
        });
    }

    [Test]
    public async Task ALinkOfOneDay_SaysOneDay_InTheMail()
    {
        var form = await PublishAsync(name: "Brief", change: Saves(days: 1), fields: [NameField]);
        using (await DraftAsync(form))
        {
        }

        Assert.That(((TextPart)(await QueuedMailAsync(form)).Body!).Text, Does.Contain("for 1 day.").And.Not.Contain("1 days"));
    }

    [Test]
    public async Task ALinkPastItsTime_IsGone_AndTheDraftIsPurged_WithItsWaitingMail()
    {
        var form = await PublishAsync(name: "Short", change: Saves(days: 2), fields: [NameField]);
        using (await DraftAsync(form))
        {
        }

        var token = TokenOf(LinkOf(await QueuedMailAsync(form)));
        var row = (await AllEntriesAsync(form)).Single();
        Clock.Advance(TimeSpan.FromDays(2) - TimeSpan.FromSeconds(1));
        using var inTime = await ResumeAsync(token);
        Clock.Advance(TimeSpan.FromSeconds(1));
        using var late = await ResumeAsync(token);
        var refusal = await RefusalAsync(late);
        await GetRequiredService<IEnumerable<IOutboxQueue>>().OfType<EntryChangeQueue>().Single().PassAsync("test", DateTimeOffset.MaxValue, CancellationToken.None);
        await SendDueAsync();
        using var purged = await ResumeAsync(token);

        Assert.Multiple(async () =>
        {
            Assert.That(inTime.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(late.StatusCode, Is.EqualTo(HttpStatusCode.Gone));
            Assert.That(refusal, Is.EqualTo("410 resume_expired"));
            Assert.That(await Entries.GetByIdAsync(row.Id), Is.Null);
            Assert.That(Written(), Is.Empty, "the mail that was waiting went with it");
            Assert.That(purged.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        });
    }

    [Test]
    public async Task ALinkThatIsNoLink_FindsNothing()
    {
        using var answer = await ResumeAsync("made-up");

        Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task TheResumeMail_ObeysTheVisitorAddressLimits_AndTheAnswerDoesNotTell()
    {
        MailLimits.PerRecipientPerDay = 1;
        var form = await FormAsync();

        using var first = await DraftAsync(form);
        using var second = await DraftAsync(form);
        await SendDueAsync();

        var drafts = await AllEntriesAsync(form);
        var kept = new List<EmailState>();
        foreach (var draft in drafts)
        {
            kept.AddRange((await Emails.ListForEntryAsync(draft.Id)).Select(mail => mail.State));
        }

        Assert.Multiple(() =>
        {
            Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(Written(), Has.Count.EqualTo(1));
            Assert.That(drafts, Has.Count.EqualTo(1), "a draft no link can reach goes at once");
            Assert.That(kept, Does.Not.Contain(EmailState.Limited));
        });
    }

    [Test]
    public async Task AHoneypotFilled_IsAnsweredTheSame_AndStoresAndSendsNothing()
    {
        var form = await FormAsync();

        using var answer = await DraftAsync(form, more: ""","honeypot":"Buy now" """);
        await SendDueAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(await AllEntriesAsync(form), Is.Empty);
            Assert.That(Written(), Is.Empty);
        });
    }

    [Test]
    public async Task AHoneypotFilled_IsToldNothing_ByItsChallengeOrItsPage()
    {
        var form = await FormAsync();
        var challenge = await ChallengeAsync(form);

        using var first = await DraftAsync(form, more: $$""","honeypot":"Buy now","challenge":"{{challenge}}" """);
        using var second = await DraftAsync(form, more: $$""","honeypot":"Buy now","challenge":"{{challenge}}" """);
        var noPage = new StringContent("""{"revision":1,"email":"ada@jfb.test","values":{"name":"Ada"},"honeypot":"Buy now"}""", Encoding.UTF8, "application/json");
        using var nowhere = await SendAsync(HttpMethod.Post, $"{Root}/forms/{form}/drafts", request => request.Content = noPage);

        Assert.Multiple(() =>
        {
            Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), "as a real draft's");
            Assert.That(nowhere.StatusCode, Is.EqualTo(HttpStatusCode.Conflict), "as a real draft's");
        });
    }

    [Test]
    public async Task ASavingAgainThatTheLimitsHoldBack_LeavesTheDraftItCameFrom_AndAnswersTheSame()
    {
        MailLimits.PerRecipientPerDay = 1;
        var form = await FormAsync();
        var first = await SavedTokenAsync(form, """{"name":"Ada"}""");

        using var again = await DraftAsync(form, """{"name":"Ada Lovelace"}""", $$""","resume":"{{first}}" """);
        await SendDueAsync();
        var named = (await JsonAsync(again))["resume"]!.GetValue<string>();
        using var old = await ResumeAsync(first);
        using var none = await ResumeAsync(named);

        Assert.Multiple(async () =>
        {
            Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.OK), "answered as a saving is");
            Assert.That(Written(), Has.Count.EqualTo(1), "no second mail");
            Assert.That(old.StatusCode, Is.EqualTo(HttpStatusCode.OK), "the link the visitor holds still works");
            Assert.That((await JsonAsync(old))["values"]!["name"]![0]!.GetValue<string>(), Is.EqualTo("Ada"));
            Assert.That(none.StatusCode, Is.EqualTo(HttpStatusCode.NotFound), "the token the answer names is no draft's");
            Assert.That(await AllEntriesAsync(form), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task SavingAgain_TakesThePlaceOfTheDraftItCameFrom()
    {
        var form = await FormAsync();
        var first = await SavedTokenAsync(form, """{"name":"Ada"}""");

        using var again = await DraftAsync(form, """{"name":"Ada Lovelace"}""", $$""","resume":"{{first}}" """);
        await SendDueAsync();
        var second = Written().Select(LinkOf).Select(TokenOf).Single(token => token != first);
        using var old = await ResumeAsync(first);
        using var fresh = await ResumeAsync(second);

        Assert.Multiple(async () =>
        {
            Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(old.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
            Assert.That((await JsonAsync(fresh))["values"]!["name"]![0]!.GetValue<string>(), Is.EqualTo("Ada Lovelace"));
            Assert.That(await AllEntriesAsync(form), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task ADraftNoLongerThere_StopsTheSubmitThatNamesIt_AsAnotherGotThereFirst()
    {
        var form = await FormAsync();
        var token = await SavedTokenAsync(form);
        var draft = (await AllEntriesAsync(form)).Single();
        var id = await GetRequiredService<EntryDrafts>().FindAsync(token, draft.FormId);
        var served = (await GetRequiredService<Jiron.Umbraco.FormBuilder.Core.Forms.PublishedFormCache>().GetAsync(form))!.Published;
        await Entries.DeleteAsync(draft.Id);
        var scope = new Jiron.Umbraco.FormBuilder.Core.MergeTags.MergeTagScope { Form = served.Definition, FormName = "Registration", FormAlias = served.Form.Alias, Culture = "en-US" };
        var answers = new Jiron.Umbraco.FormBuilder.Core.Entries.SubmissionAnswers([], [], new Jiron.Umbraco.FormBuilder.Logic.FormState([], [], new Dictionary<string, IReadOnlyList<string>>(), new Dictionary<string, string?>(), new Dictionary<string, IReadOnlyList<Jiron.Umbraco.FormBuilder.Logic.RowState>>(), [], null), Jiron.Umbraco.FormBuilder.Core.MergeTags.MergeTagAnswers.None);
        var submission = new NewSubmission(draft.FormId, draft.FormVersionId, served, answers, new Dictionary<Guid, Jiron.Umbraco.FormBuilder.Core.Entries.ConsentWording>(), Guid.NewGuid(), new string('a', 64), "en-US", new Jiron.Umbraco.FormBuilder.Core.Forms.RequestPage(null, null), new Dictionary<string, string>(), Draft: id);

        var refused = Assert.ThrowsAsync<FormBuilderConflictException>(async () => await GetRequiredService<SubmissionEntries>().CreateAsync(submission, scope));

        Assert.That(refused!.Conflict, Is.EqualTo(FormBuilderConflict.DuplicateSubmission));
    }

    [Test]
    public async Task HiddenAndKeptForActionsAndSensitiveGroupAnswers_DoNotComeBack()
    {
        var hiddenValue = new FormField { Key = Guid.NewGuid(), Type = "hidden", Alias = "internalCode", Label = new LocalizedText("Internal"), DefaultValue = FieldDefaultValue.Of("JFB_FAKE-internal") };
        var kept = new FormField { Key = Guid.NewGuid(), Type = "text", Alias = "brief", Label = new LocalizedText("Brief"), Keep = FieldKeep.UntilActionsRun };
        var groupChild = new FormField { Key = Guid.NewGuid(), Type = "text", Alias = "childNote", Label = new LocalizedText("Child") };
        var group = new FormField { Key = Guid.NewGuid(), Type = "repeater", Alias = "notes", Label = new LocalizedText("Notes"), Sensitive = true, Fields = [groupChild] };
        var form = await PublishAsync(name: "Careful", change: Saves(), fields: [NameField, hiddenValue, kept, group]);

        var token = await SavedTokenAsync(form, """{"name":"Ada","brief":"short lived","notes":[{"childNote":"sensitive"}]}""");
        using var back = await ResumeAsync(token);

        var draft = (await AllEntriesAsync(form)).Single();
        var stored = await Entries.ListValuesAsync([draft.Id]);
        Assert.Multiple(async () =>
        {
            Assert.That((await JsonAsync(back))["values"]!.AsObject().Select(value => value.Key), Is.EqualTo(new[] { "name" }));
            Assert.That(stored, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task AFormThatStoresNoEntries_KeepsNoDraft()
    {
        var form = await PublishAsync(name: "Keeps none", change: definition => Saves()(definition) with { Settings = Saves()(definition).Settings with { Submission = Saves()(definition).Settings.Submission with { Store = EntryStorage.Never } } }, fields: [NameField]);

        var on = await FormAsync();
        using var answer = await DraftAsync(form);
        using var offered = await GetAsync($"{Root}/forms/{on}?culture=en-US");
        using var notOffered = await GetAsync($"{Root}/forms/{form}?culture=en-US");

        Assert.Multiple(async () =>
        {
            Assert.That((await JsonAsync(offered))["form"]!["settings"]!["resume"]!.GetValue<bool>(), Is.True);
            Assert.That((await JsonAsync(notOffered))["form"]!["settings"]!["resume"]!.GetValue<bool>(), Is.False, "the page offers it to nobody");
            Assert.That(await RefusalAsync(answer), Is.EqualTo("409 resume_unavailable"));
            Assert.That(await AllEntriesAsync(form), Is.Empty);
        });
    }

    [Test]
    public async Task ALink_FindsNothing_OnceTheFormNoLongerOffersIt()
    {
        var form = await FormAsync();
        var token = await SavedTokenAsync(form);

        await SaveAndPublishAsync(form, definition => definition with { Settings = definition.Settings with { Submission = definition.Settings.Submission with { Resume = null } } });
        using var answer = await ResumeAsync(token);

        Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task TextThatLooksLikeParts_ComesBackAsIsWas()
    {
        var note = new FormField { Key = Guid.NewGuid(), Type = "textarea", Alias = "note", Label = new LocalizedText("Note") };
        var form = await PublishAsync(name: "Plain", change: Saves(), fields: [NameField, note]);

        var token = await SavedTokenAsync(form, new JsonObject { ["name"] = "Ada", ["note"] = """{"a":"b"}""" }.ToJsonString());
        using var back = await ResumeAsync(token);

        Assert.That((await JsonAsync(back))["values"]!["note"]![0]!.GetValue<string>(), Is.EqualTo("""{"a":"b"}"""));
    }

    [Test]
    public async Task AChallenge_MakesOneDraft()
    {
        var form = await FormAsync();
        var challenge = await ChallengeAsync(form);

        using var first = await DraftAsync(form, more: $$""","challenge":"{{challenge}}" """);
        using var second = await DraftAsync(form, more: $$""","challenge":"{{challenge}}" """);
        using var none = await DraftAsync(form, more: ""","challenge":null """);

        Assert.Multiple(() =>
        {
            Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
            Assert.That(none.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        });
    }

    [Test]
    public async Task TheChallengeThatSavedADraft_StillMakesItsOneEntry_WhichTakesTheDraftAway()
    {
        var form = await FormAsync();
        var challenge = await ChallengeAsync(form);
        using var saved = await DraftAsync(form, more: $$""","challenge":"{{challenge}}" """);
        var token = (await JsonAsync(saved))["resume"]!.GetValue<string>();

        // The visitor carries on in the same page and sends: the same challenge, which what the page holds is bound to.
        const string Answers = """{"name":"Ada","mandatory":"yes"}""";
        using var content = new StringContent($$"""{"revision":1,"resume":"{{token}}","challenge":"{{challenge}}","values":{{Answers}}}""", Encoding.UTF8, "application/json");
        using var sent = await SendAsync(HttpMethod.Post, $"{Root}/forms/{form}/entries", request => request.Content = content);
        using var content2 = new StringContent($$"""{"revision":1,"challenge":"{{challenge}}","values":{{Answers}}}""", Encoding.UTF8, "application/json");
        using var again = await SendAsync(HttpMethod.Post, $"{Root}/forms/{form}/entries", request => request.Content = content2);

        var entries = await AllEntriesAsync(form);
        Assert.Multiple(async () =>
        {
            Assert.That(saved.StatusCode, Is.EqualTo(HttpStatusCode.OK), await saved.Content.ReadAsStringAsync());
            Assert.That(sent.StatusCode, Is.EqualTo(HttpStatusCode.OK), await sent.Content.ReadAsStringAsync());
            Assert.That(entries.Select(entry => entry.State), Has.None.EqualTo(EntryState.Draft), "the entry took its draft away");
            Assert.That(entries, Has.Count.EqualTo(1));
            Assert.That(again.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized), "one entry a challenge, as before");
        });
    }

    [Test]
    public async Task AnAddressThatIsNone_IsRefused_AtTheAddress()
    {
        var form = await FormAsync();

        using var answer = await DraftAsync(form, email: "not an address");
        var body = await answer.Content.ReadAsStringAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity), body);
            Assert.That(body, Does.Contain("\"path\":\"email\""));
            Assert.That(await AllEntriesAsync(form), Is.Empty);
        });
    }

    [Test]
    public async Task AFormThatDoesNotOfferIt_OrASiteWithNoAddress_CannotSave()
    {
        var off = await PublishAsync(name: "Off", fields: [NameField]);
        var on = await FormAsync();

        using var refused = await DraftAsync(off);
        var code = await RefusalAsync(refused);
        var current = await SiteSettings.GetAsync<GeneralSettings>();
        await SiteSettings.SaveAsync(current.Value with { BaseUrl = null }, current.Version, Editor);
        using var noAddress = await DraftAsync(on);
        var noAddressCode = await RefusalAsync(noAddress);

        Assert.Multiple(async () =>
        {
            Assert.That(code, Is.EqualTo("409 resume_unavailable"));
            Assert.That(noAddressCode, Is.EqualTo("409 resume_unavailable"));
            Assert.That(await AllEntriesAsync(on), Is.Empty);
        });
    }

    [TestCase("en", "Continue your form")]
    [TestCase("de-CH", "Ihr Formular")]
    [TestCase("fr", "Reprendre")]
    [TestCase("it", "Riprendi")]
    [TestCase("nl-BE", "Ga verder")]
    [TestCase("es", "Contin")]
    [TestCase("da", "Forts")]
    [TestCase("ja", "Continue your form")]
    public void TheWords_FollowTheLanguage_OrEnglish(string culture, string start) =>
        Assert.That(ResumeWords.Of(culture).Subject, Does.StartWith(start));

    private async Task<MimeMessage> QueuedMailAsync(Guid form)
    {
        await GetRequiredService<IEnumerable<IOutboxQueue>>().OfType<EntryChangeQueue>().Single().PassAsync("test", DateTimeOffset.MaxValue, CancellationToken.None);
        var draft = (await AllEntriesAsync(form)).Single();
        var mail = (await Emails.ListForEntryAsync(draft.Id)).Single();
        return new MimeMessage { Body = new TextPart("html") { Text = mail.HtmlBody ?? string.Empty } };
    }

    private async Task<HttpResponseMessage> SendValuesAsync(Guid form, string values)
    {
        using var content = new StringContent($$"""{"revision":1,"values":{{values}}}""", Encoding.UTF8, "application/json");
        return await SendAsync(HttpMethod.Post, $"{Root}/forms/{form}/entries", request => request.Content = content);
    }
}
