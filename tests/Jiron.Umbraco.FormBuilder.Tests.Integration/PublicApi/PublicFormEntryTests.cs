using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.Navigation;
using Umbraco.Cms.Core.Strings;
using Umbraco.Cms.Tests.Common.Testing;
using static Jiron.Umbraco.FormBuilder.Tests.Integration.Forms.FormServiceTesting;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.PublicApi;

/// <summary>
/// The entries endpoint of the public API (ADR-0088) on a real site: a submission is stored with its event, its count,
/// its action runs and its search text in one unit and answered with its receipt and outcome; a retry gets the same
/// receipt, also once the form is full or closed, and other answers with its id are refused; a revision the answers do
/// not fit, a consent wording that changed, a closed or full form, a form for members, a trashed form and a submission
/// without an answer are refused with their codes and store nothing; an answer kept until the actions run is stored
/// only while an action may still run; an entry from a page without an address in its culture stores no path; a
/// redirect to a page leads to its address, and one that renders to another site, names nothing or leads to a page that
/// is gone is not followed; a message comes from the dictionary in the culture, goes on with its address and delay, and
/// says whether it shows the answers; and without an outcome that applies the visitor gets the default message.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class PublicFormEntryTests : PublicApiTestBase
{
    private static readonly FormField Email = Field("email", "email");
    private static readonly FormField Secret = Field("secret", "text") with { Sensitive = true };
    private static readonly FormField Note = Field("note", "textarea") with { Keep = FieldKeep.UntilActionsRun };
    private static readonly FormField Phone = Field("phone", "phone");
    private static readonly FormField Company = Field("company", "text");

    private static readonly FormOutcome Thanks = new()
    {
        Key = Guid.Parse("30000000-0000-4000-8000-000000000088"),
        Message = new LocalizedHtml("""<p onclick="steal()">Thanks, {{ field.name }}.</p>"""),
    };

    private static readonly FormAction Notify = new() { Key = Guid.Parse("30000000-0000-4000-8000-000000000188"), Type = "testing.mail", Name = "Notify" };
    private static readonly FormAction Archived = new() { Key = Guid.Parse("30000000-0000-4000-8000-000000000288"), Type = "testing.mail", Name = "Archived", Enabled = false };

    private IEntryRepository Entries => GetRequiredService<IEntryRepository>();

    private IFormRepository FormRows => GetRequiredService<IFormRepository>();

    [Test]
    public async Task ASubmission_IsStoredWithItsEventCountRunsAndSearchText_AndAnsweredWithItsReceiptAndOutcome()
    {
        var key = await PublishAsync(change: definition => definition with
        {
            Outcomes = [Thanks],
            Actions = [Notify, Archived],
            Settings = definition.Settings with { Developer = definition.Settings.Developer with { QueryValues = ["source"], ContextValues = ["campaign"] } },
        }, fields: [NameField, Email, Secret, Note]);
        var page = await PageAsync();
        var submissionId = Guid.NewGuid();
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        using var response = await PostAsync(key, $$$"""
            {"submissionId":"{{{submissionId}}}","revision":1,"culture":"en-US","contentKey":"{{{page}}}",
             "values":{"name":"Ada Lovelace","email":"ada@example.com","secret":"JFB_FAKE hidden","note":"A note"},
             "query":{"source":"newsletter","other":"x"},"context":{"campaign":"autumn","other":"y"}}
            """);
        var answer = await JsonAsync(response);
        using var another = await PostAsync(key, """{"revision":1,"values":{"name":"Grace"}}""");
        var second = await JsonAsync(another);

        var form = (await FormRows.GetAsync(key))!;
        var entry = (await Entries.GetBySubmissionAsync(form.Id, submissionId))!;
        var catalogue = (await FormRows.ListFieldsAsync(form.Id)).ToDictionary(field => field.Values.FieldKey, field => field.Id);
        var values = (await Entries.ListValuesAsync([entry.Id])).ToDictionary(value => value.FieldId, value => value.Text);
        var events = await GetRequiredService<IEntryEventRepository>().ListForEntryAsync(entry.Id);
        var runs = await GetRequiredService<IActionRunRepository>().ListForEntryAsync(entry.Id);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var stats = await GetRequiredService<IDailyStatRepository>().ListAsync(form.Id, today.AddDays(-1), today.AddDays(1));
        var year = entry.SubmittedUtc.Year;

        Assert.Multiple(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/json"));
            Assert.That(Header(response, "Cache-Control"), Is.EqualTo("no-store"));
            Assert.That(answer["receipt"]!["submissionId"]!.GetValue<string>(), Is.EqualTo(submissionId.ToString()));
            Assert.That(answer["receipt"]!["reference"]!.GetValue<string>(), Is.EqualTo($"{year}-00001"));
            Assert.That(answer["receipt"]!["submittedUtc"]!.GetValue<DateTime>(), Is.EqualTo(entry.SubmittedUtc.UtcDateTime).Within(TimeSpan.FromMilliseconds(1)));
            Assert.That(entry.SubmittedUtc, Is.GreaterThan(before));
            Assert.That(Text(answer["outcome"]), Is.EqualTo("""{"kind":"message","html":"<p>Thanks, Ada Lovelace.</p>"}"""), "rendered with the answers and sanitised");
            Assert.That(another.StatusCode, Is.EqualTo(HttpStatusCode.OK), await another.Content.ReadAsStringAsync());
            Assert.That(Guid.TryParse(second["receipt"]!["submissionId"]!.GetValue<string>(), out var made) && made != Guid.Empty && made != submissionId, Is.True, "the server makes an id when none is posted");
            Assert.That(second["receipt"]!["reference"]!.GetValue<string>(), Is.EqualTo($"{year}-00002"));

            Assert.That((entry.State, entry.IsTest, entry.Culture, entry.ContentKey), Is.EqualTo((EntryState.Submitted, false, "en-US", (Guid?)page)));
            Assert.That(entry.PageUrl, Is.EqualTo("/contact/"), "the page's path from its address, never from the visitor");
            Assert.That(entry.Context, Is.EqualTo("""{"campaign":"autumn"}"""), "only the context values the form lists, and no query value");
            Assert.That(entry.SubmissionHash, Does.Match("^[0-9a-f]{64}$"));
            Assert.That(entry.IpAddress, Is.Null);
            Assert.That(values[catalogue[NameField.Key]], Is.EqualTo("Ada Lovelace"));
            Assert.That(values[catalogue[Secret.Key]], Is.EqualTo("JFB_FAKE hidden"));
            Assert.That(values[catalogue[Note.Key]], Is.EqualTo("A note"), "kept until the queued run has run");
            Assert.That(entry.SearchText, Does.Contain("ada lovelace").And.Contain("ada@example.com").And.Contain("a note"));
            Assert.That(entry.SearchText, Does.Not.Contain("hidden"), "a sensitive answer is not searchable");
            Assert.That(events.Select(each => (each.Kind, each.Details, each.UserKey)), Is.EqualTo(new[] { (SubmissionEntries.SubmittedEvent, $$"""{"outcome":"{{Thanks.Key}}","hidden":[]}""", (Guid?)null) }), "the outcome the rules chose, and nothing they hid (ADR-0123)");
            Assert.That(runs.Select(run => (run.ActionKey, run.ActionType, run.Trigger, run.State)), Is.EqualTo(new[] { (Notify.Key, "testing.mail", ActionTrigger.Submitted, ActionRunState.Pending) }), "the enabled action only");
            Assert.That(stats.Select(stat => (stat.Metric, stat.Dimension, stat.Value)), Is.EqualTo(new[] { (SubmissionEntries.SubmissionsMetric, string.Empty, 2L) }));
        });
    }

    [Test]
    public async Task AnAnswerKeptUntilTheActionsRun_IsNotStored_WhenNoActionCanRun()
    {
        var key = await PublishAsync(fields: [NameField, Note]);

        using var response = await PostAsync(key, """{"revision":1,"values":{"name":"Ada","note":"Only for the actions"}}""");
        var form = (await FormRows.GetAsync(key))!;
        var ids = await Entries.ListIdsAsync(form.Id, 10);
        var catalogue = (await FormRows.ListFieldsAsync(form.Id)).ToDictionary(field => field.Values.FieldKey, field => field.Id);
        var values = await Entries.ListValuesAsync(ids.ToList());
        var entry = (await Entries.GetByIdAsync(ids.Single()))!;

        Assert.Multiple(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
            Assert.That(values.Select(value => value.FieldId), Is.EqualTo(new[] { catalogue[NameField.Key] }));
            Assert.That(entry.SearchText, Does.Not.Contain("actions"));
        });
    }

    [Test]
    public async Task AnAnswerKeptUntilTheActionsRun_IsStored_WhileAnActionMayStillRun()
    {
        var onApproval = new FormAction { Key = Guid.NewGuid(), Type = "testing.mail", Name = "On approval", Trigger = ActionTrigger.Approved };
        var onRejection = onApproval with { Key = Guid.NewGuid(), Name = "On rejection", Trigger = ActionTrigger.Rejected };
        FormDefinition Submission(FormDefinition definition, Func<FormSubmissionSettings, FormSubmissionSettings> change) =>
            definition with { Settings = definition.Settings with { Submission = change(definition.Settings.Submission) } };
        var forms = new Dictionary<string, Guid>
        {
            ["any action, once the address is confirmed"] = await PublishAsync("Confirmed", change: definition => Submission(definition, settings => settings with { Verification = new EmailVerification { EmailField = Email.Key } }) with { Actions = [Notify] }, fields: [NameField, Email, Note]),
            ["an action on approval, approved at once"] = await PublishAsync("Approved", change: definition => definition with { Actions = [onApproval] }, fields: [NameField, Email, Note]),
            ["an action on rejection, while it waits for approval"] = await PublishAsync("Reviewed", change: definition => Submission(definition, settings => settings with { Approval = ApprovalMode.Manual }) with { Actions = [onRejection] }, fields: [NameField, Email, Note]),
            ["an action on rejection, approved at once"] = await PublishAsync("Never rejected", change: definition => definition with { Actions = [onRejection] }, fields: [NameField, Email, Note]),
        };

        var stored = new Dictionary<string, string>();
        foreach (var (what, key) in forms)
        {
            using var response = await PostAsync(key, """{"revision":1,"values":{"name":"Ada","email":"ada@example.com","note":"For the actions"}}""");
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), $"{what}: {await response.Content.ReadAsStringAsync()}");
            var form = (await FormRows.GetAsync(key))!;
            var entry = (await Entries.GetByIdAsync((await Entries.ListIdsAsync(form.Id, 10)).Single()))!;
            var note = (await FormRows.ListFieldsAsync(form.Id)).Single(field => field.Values.FieldKey == Note.Key).Id;
            var kept = (await Entries.ListValuesAsync([entry.Id])).Any(value => value.FieldId == note);
            var runs = await GetRequiredService<IActionRunRepository>().ListForEntryAsync(entry.Id);
            stored[what] = $"{entry.State}, {(kept ? "kept" : "not kept")}, {runs.Count} runs";
        }

        Assert.That(stored, Is.EqualTo(new Dictionary<string, string>
        {
            ["any action, once the address is confirmed"] = "Unverified, kept, 0 runs",
            ["an action on approval, approved at once"] = "Submitted, kept, 0 runs",
            ["an action on rejection, while it waits for approval"] = "Submitted, kept, 0 runs",
            ["an action on rejection, approved at once"] = "Submitted, not kept, 0 runs",
        }), string.Join(" | ", stored));
    }

    [Test]
    public async Task AnEntryFromAPageWithoutAnAddressInItsCulture_StoresNoPath()
    {
        Assert.That((await GetRequiredService<ILanguageService>().CreateAsync(new Language("da-DK", "Danish"), Constants.Security.SuperUserKey)).Success, Is.True);
        var page = await PageAsync(variant: true);
        var key = await PublishAsync();

        using var english = await PostAsync(key, $$$"""{"revision":1,"culture":"en-US","contentKey":"{{{page}}}","values":{"name":"Ada"}}""");
        using var danish = await PostAsync(key, $$$"""{"revision":1,"culture":"da-DK","contentKey":"{{{page}}}","values":{"name":"Ada"}}""");
        var form = (await FormRows.GetAsync(key))!;
        var entries = await Task.WhenAll((await Entries.ListIdsAsync(form.Id, 10)).Select(async id => (await Entries.GetByIdAsync(id))!));

        Assert.Multiple(async () =>
        {
            Assert.That(english.StatusCode, Is.EqualTo(HttpStatusCode.OK), await english.Content.ReadAsStringAsync());
            Assert.That(danish.StatusCode, Is.EqualTo(HttpStatusCode.OK), await danish.Content.ReadAsStringAsync());
            Assert.That(
                entries.OrderBy(entry => entry.Number).Select(entry => (entry.Culture, entry.ContentKey, entry.PageUrl)),
                Is.EqualTo(new[] { ("en-US", (Guid?)page, (string?)"/contact/"), ("da-DK", (Guid?)page, (string?)null) }),
                "the page is published in English only, so it has no Danish address");
        });
    }

    [Test]
    public async Task ARetry_GetsTheSameReceipt_AlsoOnceTheFormIsFullOrClosed_AndOtherAnswersWithItsIdAreRefused()
    {
        var key = await PublishAsync(change: definition => definition with
        {
            Outcomes = [Thanks],
            Settings = definition.Settings with { Availability = definition.Settings.Availability with { MaxEntries = 1 } },
        });
        var submissionId = Guid.NewGuid();
        var ada = $$$"""{"submissionId":"{{{submissionId}}}","revision":1,"values":{"name":"Ada"}}""";
        var openBefore = await OpenAsync(key);

        using var first = await PostAsync(key, ada);
        var openAfter = await OpenAsync(key);
        using var retry = await PostAsync(key, ada);
        using var full = await PostAsync(key, """{"revision":1,"values":{"name":"Grace"}}""");
        using var changed = await PostAsync(key, $$$"""{"submissionId":"{{{submissionId}}}","revision":1,"values":{"name":"Grace"}}""");

        await SaveAndPublishAsync(key, definition => definition with
        {
            Settings = definition.Settings with { Availability = definition.Settings.Availability with { MaxEntries = null, ClosesAt = Instant(DateTime.UtcNow.AddMinutes(-1)) } },
        });
        using var retryClosed = await PostAsync(key, ada);
        using var closed = await PostAsync(key, """{"revision":1,"values":{"name":"Grace"}}""");
        var openClosed = await OpenAsync(key);
        var form = (await FormRows.GetAsync(key))!;

        Assert.Multiple(async () =>
        {
            Assert.That((openBefore, openAfter, openClosed), Is.EqualTo((true, false, false)), "the form endpoint's open follows the limit and the closing time");
            Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK), await first.Content.ReadAsStringAsync());
            Assert.That(retry.StatusCode, Is.EqualTo(HttpStatusCode.OK), await retry.Content.ReadAsStringAsync());
            Assert.That(await retry.Content.ReadAsStringAsync(), Is.EqualTo(await first.Content.ReadAsStringAsync()), "the same receipt and outcome");
            Assert.That(await RefusalAsync(full), Is.EqualTo("403 entry_limit_reached"));
            Assert.That(await RefusalAsync(changed), Is.EqualTo("409 duplicate_submission"));
            Assert.That(retryClosed.StatusCode, Is.EqualTo(HttpStatusCode.OK), await retryClosed.Content.ReadAsStringAsync());
            Assert.That(await retryClosed.Content.ReadAsStringAsync(), Is.EqualTo(await first.Content.ReadAsStringAsync()), "the outcome of the version it was sent with");
            Assert.That(await RefusalAsync(closed), Is.EqualTo("403 form_closed"));
            Assert.That(await Entries.ListIdsAsync(form.Id, 10), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task ARevisionTheAnswersDoNotFit_IsAnsweredFormChanged_AndAnOlderOneTheyFit_IsReadByTheCurrentFields()
    {
        // Revision 1 asks for a name, a phone number and a company; revision 2 renames the phone field, drops the
        // company and adds an e-mail address. Another form's revision 2 requires an answer revision 1 did not ask for.
        var key = await PublishAsync(fields: [NameField, Phone, Company]);
        await SaveAndPublishAsync(key, definition => WithFields(definition, NameField, Phone with { Alias = "telephone", FormerAliases = ["phone"] }, Email));
        var required = await PublishAsync("Required later");
        await SaveAndPublishAsync(required, definition => WithFields(definition, NameField, Email with { Required = true }));

        var answers = new Dictionary<string, string>();
        foreach (var (what, form, body) in new[]
        {
            ("an older revision the answers fit", key, """{"revision":1,"values":{"name":"Ada","phone":"+44 20 7946 0000"}}"""),
            ("an older revision with an answer to a field the form dropped", key, """{"revision":1,"values":{"name":"Ada","company":"Analytical Engines"}}"""),
            ("a newer revision", key, """{"revision":3,"values":{"name":"Ada"}}"""),
            ("the current revision with a former alias", key, """{"revision":2,"values":{"name":"Ada","phone":"+44 20 7946 0000"}}"""),
            ("the current revision", key, """{"revision":2,"values":{"name":"Ada","telephone":"+44 20 7946 0000","email":"ada@example.com"}}"""),
            ("an older revision without a field now required", required, """{"revision":1,"values":{"name":"Ada"}}"""),
            ("an older revision with an answer it did not ask for", required, """{"revision":1,"values":{"name":"Ada","email":"ada@example.com"}}"""),
            ("the current revision without the required answer", required, """{"revision":2,"values":{"name":"Ada"}}"""),
        })
        {
            using var response = await PostAsync(form, body);
            answers[what] = await ErrorsAsync(response);
        }

        var row = (await FormRows.GetAsync(key))!;
        var catalogue = (await FormRows.ListFieldsAsync(row.Id)).ToDictionary(field => field.Values.FieldKey, field => field.Id);
        var entries = await Entries.ListIdsAsync(row.Id, 10);
        var values = await Entries.ListValuesAsync([entries[0]]);
        var stored = await Entries.GetByIdAsync(entries[0]);

        Assert.Multiple(async () =>
        {
            Assert.That(answers, Is.EqualTo(new Dictionary<string, string>
            {
                ["an older revision the answers fit"] = "200",
                ["an older revision with an answer to a field the form dropped"] = "409 form_changed",
                ["a newer revision"] = "409 form_changed",
                ["the current revision with a former alias"] = "400 invalid_request invalid_value at values.phone",
                ["the current revision"] = "200",
                ["an older revision without a field now required"] = "409 form_changed",
                ["an older revision with an answer it did not ask for"] = "409 form_changed",
                ["the current revision without the required answer"] = "422 validation_failed required at email",
            }), string.Join(" | ", answers));
            Assert.That(values.Where(value => value.FieldId == catalogue[Phone.Key]).Select(value => value.Text), Is.EqualTo(new[] { "+44 20 7946 0000" }), "the older alias names the field it was");
            Assert.That(stored!.FormVersionId, Is.EqualTo((await FormRows.GetAsync(key))!.PublishedVersionId), "stored with the version the site serves");
            Assert.That(await Entries.ListIdsAsync((await FormRows.GetAsync(required))!.Id, 10), Is.Empty);
        });
    }

    [Test]
    public async Task AClosedForm_AFormForMembers_ATrashedForm_AndASubmissionWithoutAnAnswer_AreRefused_AndStoreNothing()
    {
        var notYet = await PublishAsync("Not yet", change: definition => definition with
        {
            Settings = definition.Settings with { Availability = definition.Settings.Availability with { OpensAt = Instant(DateTime.UtcNow.AddDays(1)) } },
        });
        var members = await PublishAsync("Members", change: definition => definition with
        {
            Settings = definition.Settings with { Availability = definition.Settings.Availability with { Access = MemberAccess.Members } },
        });
        var trashed = await PublishAsync("Trashed");
        await Forms.TrashAsync(trashed, (await Forms.GetAsync(trashed))!.ConcurrencyVersion, Editor);
        var open = await PublishAsync("Open", fields: [NameField, Email, Field("fullName", "name")]);

        var answers = new Dictionary<string, string>();
        foreach (var (what, form, body) in new[]
        {
            ("a form before its opening time", notYet, """{"revision":1,"values":{"name":"Ada"}}"""),
            ("a form for members", members, """{"revision":1,"values":{"name":"Ada"}}"""),
            ("a form in the trash", trashed, """{"revision":1,"values":{"name":"Ada"}}"""),
            ("a form that does not exist", Guid.NewGuid(), """{"revision":1,"values":{"name":"Ada"}}"""),
            ("no answer at all", open, """{"revision":1,"values":{}}"""),
            ("only empty answers", open, """{"revision":1,"values":{"name":"  ","email":null}}"""),
            ("only empty parts", open, """{"revision":1,"values":{"fullName.first":" ","fullName.last":""}}"""),
            ("no values", open, """{"revision":1}"""),
        })
        {
            using var response = await PostAsync(form, body);
            answers[what] = await ErrorsAsync(response);
        }

        var stored = 0;
        foreach (var form in new[] { notYet, members, trashed, open })
        {
            stored += (await Entries.ListIdsAsync((await FormRows.GetAsync(form))!.Id, 10)).Count;
        }

        Assert.Multiple(() =>
        {
            Assert.That(answers, Is.EqualTo(new Dictionary<string, string>
            {
                ["a form before its opening time"] = "403 form_closed",
                ["a form for members"] = "403 member_required",
                ["a form in the trash"] = "404 form_not_found",
                ["a form that does not exist"] = "404 form_not_found",
                ["no answer at all"] = "422 validation_failed empty at ",
                ["only empty answers"] = "422 validation_failed empty at ",
                ["only empty parts"] = "422 validation_failed empty at ",
                ["no values"] = "422 validation_failed empty at ",
            }), string.Join(" | ", answers));
            Assert.That(stored, Is.Zero);
        });
    }

    [Test]
    public async Task AConsent_PostedWithTheHashOfAWordingThatChanged_IsAnsweredFormChanged_AndStoresNothing()
    {
        var consent = Field("privacy", "consent", """{"text":{"":"#Forms.Consent"}}""") with { Required = true };
        var dictionary = GetRequiredService<IDictionaryItemService>();
        var english = (await GetRequiredService<ILanguageService>().GetDefaultLanguageAsync())!;
        var item = new DictionaryItem("Forms.Consent") { Translations = [new DictionaryTranslation(english, "I agree to the notice.")] };
        Assert.That((await dictionary.CreateAsync(item, Constants.Security.SuperUserKey)).Success, Is.True);
        var key = await PublishAsync(fields: [NameField, consent]);
        var shown = await ConsentHashAsync(key, consent.Key);

        var changed = (await dictionary.GetAsync("Forms.Consent"))!;
        changed.Translations.Single(translation => translation.LanguageIsoCode == english.IsoCode).Value = "I agree to the new notice.";
        Assert.That((await dictionary.UpdateAsync(changed, Constants.Security.SuperUserKey)).Success, Is.True);
        string Body(string hash) => $$$"""{"revision":1,"culture":"en-US","values":{"name":"Ada","privacy":true},"consents":{"privacy":"{{{hash}}}"}}""";
        using var stale = await PostAsync(key, Body(shown));
        var form = (await FormRows.GetAsync(key))!;
        var storedAfterStale = await Entries.ListIdsAsync(form.Id, 10);
        using var current = await PostAsync(key, Body(await ConsentHashAsync(key, consent.Key)));
        using var unticked = await PostAsync(key, """{"revision":1,"culture":"en-US","values":{"name":"Ada","privacy":false}}""");

        Assert.Multiple(async () =>
        {
            Assert.That(await RefusalAsync(stale), Is.EqualTo("409 form_changed"));
            Assert.That(storedAfterStale, Is.Empty);
            Assert.That(current.StatusCode, Is.EqualTo(HttpStatusCode.OK), await current.Content.ReadAsStringAsync());
            Assert.That(await ErrorsAsync(unticked), Is.EqualTo("422 validation_failed required at privacy"));
        });
    }

    [Test]
    public async Task ARedirectThatRendersToAnotherSite_IsNotFollowed_AndTheVisitorGetsTheDefaultMessage_WithAWarning()
    {
        // The publish rule refuses a path whose first segment is a merge tag, and the merge tags leave a tag there empty; a
        // version stored before that rule holds one, which renders as //evil.example whatever the answer.
        var next = Field("next", "text");
        var redirect = new FormOutcome { Key = Guid.NewGuid(), Kind = OutcomeKind.Redirect, Redirect = new OutcomeRedirect { Url = "/thanks/{{ field.next }}" } };
        var key = await PublishAsync(change: definition => definition with { Outcomes = [redirect] }, fields: [NameField, next]);
        using var followed = await PostAsync(key, """{"revision":1,"values":{"name":"Ada","next":"a b/c"}}""");

        var row = (await FormRows.GetAsync(key))!;
        var published = (await Forms.GetPublishedAsync(key))!.Definition;
        var unsafeOutcome = redirect with { Redirect = new OutcomeRedirect { Url = "/{{ field.next }}/evil.example" } };
        Assert.That(RedirectUrls.Problem(unsafeOutcome.Redirect!.Url!), Is.Not.Null, "the publish rule refuses it now");
        _ = await FormRows.PublishAsync(row.Id, row.ConcurrencyVersion, Stored(published with { Outcomes = [unsafeOutcome] }), null, null, Editor);
        GetRequiredService<FormCacheInvalidator>().Changed(key);
        using var response = await PostAsync(key, """{"revision":1,"values":{"name":"Grace","next":"anything"}}""");
        var warnings = GetRequiredService<LogCapture>().Entries.Where(entry => entry.Category == typeof(SubmissionOutcomes).FullName).ToList();

        Assert.Multiple(async () =>
        {
            Assert.That(Text((await JsonAsync(followed))["outcome"]), Is.EqualTo("""{"kind":"redirect","url":"/thanks/a%20b%2Fc"}"""), "a path on the site is followed, its answer encoded");
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
            Assert.That(Text((await JsonAsync(response))["outcome"]), Is.EqualTo("""{"kind":"message"}"""), "the page's own thank-you");
            Assert.That(warnings.Select(entry => entry.Level), Is.EqualTo(new[] { LogLevel.Warning }));
            Assert.That(warnings[0].Message, Does.Contain(redirect.Key.ToString()).And.Not.Contain("Grace").And.Not.Contain("evil"));
        });
    }

    [Test]
    public async Task ARedirectToAPage_LeadsToItsAddress_AndOneThatLeadsNowhere_IsNotFollowed()
    {
        var page = await PageAsync();
        var toPage = new FormOutcome { Key = Guid.NewGuid(), Kind = OutcomeKind.Redirect, Redirect = new OutcomeRedirect { Content = page } };
        var thenToPage = Thanks with { Redirect = new OutcomeRedirect { Content = page, DelaySeconds = 3 } };
        var redirecting = await PublishAsync("Redirect", change: definition => definition with { Outcomes = [toPage] });
        var thanking = await PublishAsync("Message", change: definition => definition with { Outcomes = [thenToPage] });
        using var followed = await PostAsync(redirecting, """{"revision":1,"values":{"name":"Ada"}}""");
        using var shown = await PostAsync(thanking, """{"revision":1,"values":{"name":"Ada"}}""");

        // A version stored before the publish rule, whose redirect names neither a page nor an address.
        var row = (await FormRows.GetAsync(redirecting))!;
        var published = (await Forms.GetPublishedAsync(redirecting))!.Definition;
        _ = await FormRows.PublishAsync(row.Id, row.ConcurrencyVersion, Stored(published with { Outcomes = [toPage with { Redirect = new OutcomeRedirect() }] }), null, null, Editor);
        GetRequiredService<FormCacheInvalidator>().Changed(redirecting);
        using var nowhere = await PostAsync(redirecting, """{"revision":1,"values":{"name":"Ada"}}""");

        // The page is unpublished meanwhile.
        var contents = GetRequiredService<IContentService>();
        contents.Unpublish(contents.GetById(page)!);
        await GetRequiredService<IDocumentNavigationManagementService>().RebuildAsync();
        await GetRequiredService<IPublishStatusManagementService>().InitializeAsync(CancellationToken.None);
        using var gone = await PostAsync(thanking, """{"revision":1,"values":{"name":"Ada"}}""");
        var warnings = GetRequiredService<LogCapture>().Entries.Where(entry => entry.Category == typeof(SubmissionOutcomes).FullName).ToList();

        Assert.Multiple(async () =>
        {
            Assert.That(Text((await JsonAsync(followed))["outcome"]), Is.EqualTo("""{"kind":"redirect","url":"/contact/"}"""));
            Assert.That(Text((await JsonAsync(shown))["outcome"]), Is.EqualTo("""{"kind":"message","html":"<p>Thanks, Ada.</p>","url":"/contact/","delaySeconds":3}"""));
            Assert.That(Text((await JsonAsync(nowhere))["outcome"]), Is.EqualTo("""{"kind":"message"}"""), "the page's own thank-you");
            Assert.That(Text((await JsonAsync(gone))["outcome"]), Is.EqualTo("""{"kind":"message","html":"<p>Thanks, Ada.</p>"}"""), "the message without the page");
            Assert.That(warnings, Has.Count.EqualTo(2), "the redirect to nowhere and the one to the unpublished page");
            Assert.That(warnings.Select(entry => entry.Message), Is.All.Contains("cannot be followed"));
        });
    }

    [Test]
    public async Task AMessageFromTheDictionary_IsShownInTheCulture_AndWithoutAnOutcomeThatApplies_TheDefaultOne()
    {
        var english = (await GetRequiredService<ILanguageService>().GetDefaultLanguageAsync())!;
        var item = new DictionaryItem("Forms.Thanks") { Translations = [new DictionaryTranslation(english, """<p onclick="steal()">Thank you, {{ field.name }}.</p>""")] };
        Assert.That((await GetRequiredService<IDictionaryItemService>().CreateAsync(item, Constants.Security.SuperUserKey)).Success, Is.True);
        var fromDictionary = Thanks with { Message = new LocalizedHtml("#Forms.Thanks") };
        var onlyForGrace = Thanks with
        {
            Rules = [new FormRule { Key = Guid.NewGuid(), Effect = RuleEffect.Use, Conditions = [new RuleCondition { Field = NameField.Key, Operator = "equals", Value = new ConditionOperand.Text("Grace") }] }],
        };
        var dictionary = await PublishAsync("Dictionary", change: definition => definition with { Outcomes = [fromDictionary] });
        var ruled = await PublishAsync("Ruled", change: definition => definition with { Outcomes = [onlyForGrace] });

        using var thanked = await PostAsync(dictionary, """{"revision":1,"culture":"en-US","values":{"name":"Ada"}}""");
        using var none = await PostAsync(ruled, """{"revision":1,"values":{"name":"Ada"}}""");
        using var grace = await PostAsync(ruled, """{"revision":1,"values":{"name":"Grace"}}""");

        Assert.Multiple(async () =>
        {
            Assert.That(Text((await JsonAsync(thanked))["outcome"]), Is.EqualTo("""{"kind":"message","html":"<p>Thank you, Ada.</p>"}"""), "rendered and sanitised");
            Assert.That(Text((await JsonAsync(none))["outcome"]), Is.EqualTo("""{"kind":"message"}"""), "no outcome applies: the page's own thank-you");
            Assert.That(Text((await JsonAsync(grace))["outcome"]), Is.EqualTo("""{"kind":"message","html":"<p>Thanks, Grace.</p>"}"""));
        });
    }

    [Test]
    public async Task ASendThatNamesItsForm_GetsTheTokenOfTheCopy_WhichTheRenderEndpointShowsOnce()
    {
        var key = await PublishAsync(change: definition => definition with { Outcomes = [Thanks with { ShowAnswers = true }] });
        var plain = await PublishAsync(change: definition => definition with { Outcomes = [Thanks] });

        using var named = await PostAsync(key, """{"revision":1,"instance":"jfb-1","values":{"name":"Ada"}}""");
        using var unnamed = await PostAsync(key, """{"revision":1,"values":{"name":"Grace"}}""");
        using var wrong = await PostAsync(key, """{"revision":1,"instance":"no id","values":{"name":"Mary"}}""");
        using var noCopy = await PostAsync(plain, """{"revision":1,"instance":"jfb-1","values":{"name":"Ada"}}""");
        var outcome = (await JsonAsync(named))["outcome"]!;
        var token = Uri.EscapeDataString(outcome["copy"]!.GetValue<string>());
        var address = $"{Root}/forms/{key}/render?instance=jfb-1&page=%2Fcontact%2F&jfb-sent={token}";
        using var first = await SendAsync(HttpMethod.Get, address);
        var firstHtml = await first.Content.ReadAsStringAsync();
        using var later = await SendAsync(HttpMethod.Get, address);
        var laterHtml = await later.Content.ReadAsStringAsync();
        using var otherPlace = await SendAsync(HttpMethod.Get, $"{Root}/forms/{key}/render?instance=jfb-2&page=%2Fcontact%2F&jfb-sent={token}");

        Assert.Multiple(async () =>
        {
            Assert.That(outcome["showAnswers"]!.GetValue<bool>(), Is.True);
            Assert.That((await JsonAsync(unnamed))["outcome"]!.AsObject().ContainsKey("copy"), Is.False, "a request that names no form gets no token");
            Assert.That(wrong.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest), "one that is no form's id is refused");
            Assert.That((await JsonAsync(noCopy))["outcome"]!.AsObject().ContainsKey("copy"), Is.False, "an outcome without the copy has none");
            Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK), firstHtml);
            Assert.That(firstHtml, Does.Contain("jfb-summary").And.Contain("Ada").And.Contain("data-jfb-print"), "the answers, as a post's outcome shows them");
            Assert.That(first.Headers.CacheControl?.NoStore, Is.True, "answers are never stored on the way");
            Assert.That(firstHtml, Does.Contain("href=\"/contact/#jfb-1\""), "the link to the form on the page that asked, not the endpoint");
            Assert.That(laterHtml, Does.Not.Contain("jfb-summary").And.Contain("data-jfb-copy-shown"), "the answers show once, a line in their place");
            Assert.That(await otherPlace.Content.ReadAsStringAsync(), Does.Not.Contain("data-jfb-copy"), "the token is for the form it named");
        });
    }

    [Test]
    public async Task AMessageThatGoesOn_CarriesItsAddressAndDelay_AndWhetherItShowsTheAnswers()
    {
        var later = Thanks with { Redirect = new OutcomeRedirect { Url = "/next/{{ field.name }}", DelaySeconds = 5 }, ShowAnswers = true };
        var atOnce = Thanks with { Redirect = new OutcomeRedirect { Url = "/next" } };
        var slow = await PublishAsync(change: definition => definition with { Outcomes = [later] });
        var quick = await PublishAsync(change: definition => definition with { Outcomes = [atOnce] });

        using var afterAWhile = await PostAsync(slow, """{"revision":1,"values":{"name":"Ada"}}""");
        using var straightOn = await PostAsync(quick, """{"revision":1,"values":{"name":"Ada"}}""");

        Assert.Multiple(async () =>
        {
            Assert.That(Text((await JsonAsync(afterAWhile))["outcome"]), Is.EqualTo("""{"kind":"message","html":"<p>Thanks, Ada.</p>","url":"/next/Ada","delaySeconds":5,"showAnswers":true}"""));
            Assert.That(Text((await JsonAsync(straightOn))["outcome"]), Is.EqualTo("""{"kind":"message","html":"<p>Thanks, Ada.</p>","url":"/next","delaySeconds":0}"""), "a delay of none is said, and answers not shown are left out");
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.Services.AddLogCapture();
    }

    private static FormField Field(string alias, string type, string? settings = null) => new()
    {
        Key = Guid.NewGuid(),
        Type = type,
        Alias = alias,
        Label = new LocalizedText(alias),
        Settings = settings is null ? null : SettingsObject.Parse(settings),
    };

    // A node as JSON, its markup unescaped so a message reads as written.
    private static string Text(JsonNode? node) =>
        node?.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }) ?? "null";

    // An instant as definitions hold it: in UTC, in whole seconds.
    private static DateTime Instant(DateTime utc) => new(utc.Ticks - (utc.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);

    // The status, the code and each error with where it is, such as "422 validation_failed required at name"; "200" for an entry.
    private static async Task<string> ErrorsAsync(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.OK)
        {
            return "200";
        }

        var body = await response.Content.ReadAsStringAsync();
        var problem = body.Length > 0 ? JsonNode.Parse(body) as JsonObject : null;
        var errors = problem?["errors"] is JsonArray list ? " " + string.Join("; ", list.Select(error => $"{error!["code"]} at {error["path"]}")) : string.Empty;
        return $"{(int)response.StatusCode} {problem?["code"]?.GetValue<string>() ?? "(no code)"}{errors}";
    }

    private async Task<HttpResponseMessage> PostAsync(Guid form, string json) =>
        await SendAsync(HttpMethod.Post, $"{Root}/forms/{form}/entries", request => request.Content = new StringContent(json, Encoding.UTF8, "application/json"));

    private async Task<bool> OpenAsync(Guid form)
    {
        using var response = await GetAsync($"{Root}/forms/{form}");
        return (await JsonAsync(response))["open"]!.GetValue<bool>();
    }

    private async Task<string> ConsentHashAsync(Guid form, Guid field)
    {
        using var response = await GetAsync($"{Root}/forms/{form}?culture=en-US");
        return (await JsonAsync(response))["consents"]![field.ToString()]!.GetValue<string>();
    }

    // A published page under a published root, the site's only one. In the test host, content made outside a request does
    // not reach the navigation and the publish states, so the test builds both from the database, as a site does when it
    // boots with this content.
    private async Task<Guid> PageAsync(bool variant = false)
    {
        var types = GetRequiredService<IContentTypeService>();
        var contents = GetRequiredService<IContentService>();
        var type = new ContentType(GetRequiredService<IShortStringHelper>(), -1)
        {
            Alias = "jfbPage",
            Name = "Page",
            AllowedAsRoot = true,
            Variations = variant ? ContentVariation.Culture : ContentVariation.Nothing,
        };
        Assert.That((await types.CreateAsync(type, Constants.Security.SuperUserKey)).Success, Is.True);
        type.AllowedContentTypes = [new ContentTypeSort(type.Key, 0, type.Alias)];
        Assert.That((await types.UpdateAsync(type, Constants.Security.SuperUserKey)).Success, Is.True);

        // A variant page is published in English only.
        string[] cultures = variant ? ["en-US"] : ["*"];
        Content Named(string name, Content? parent)
        {
            var content = parent is null ? new Content(name, -1, type) : new Content(name, parent, type);
            if (variant)
            {
                content.SetCultureName(name, "en-US");
            }

            contents.Save(content);
            contents.Publish(content, cultures);
            return content;
        }

        var page = Named("Contact", Named("Shop", null));
        await GetRequiredService<IDocumentNavigationManagementService>().RebuildAsync();
        await GetRequiredService<IPublishStatusManagementService>().InitializeAsync(CancellationToken.None);
        return page.Key;
    }
}
