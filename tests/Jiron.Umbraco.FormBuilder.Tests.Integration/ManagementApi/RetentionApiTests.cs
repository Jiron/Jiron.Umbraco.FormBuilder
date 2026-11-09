using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>
/// Privacy > Retention's endpoints (ROADMAP 13.1, IA 9, ADR-0222): the site's days with what the host allows and the forms
/// with days of their own, read by a privacy officer as by an administrator; a save of the days through the privacy
/// section, which keeps the host's refusal of "for good"; and the dry run, which counts by the days sent and refuses a day
/// or a span out of range.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class RetentionApiTests : ManagementApiTestBase
{
    private static readonly Guid Editor = Guid.Parse("7d1c3a52-31f4-4e57-9d2e-61b0f6a1c222");

    [Test]
    public async Task TheScreen_ReadsTheSitesDays_WhatTheHostAllows_AndTheFormsWithTheirOwn()
    {
        await FormAsync("Survey", definition => definition with { Settings = definition.Settings with { Privacy = definition.Settings.Privacy with { RetentionDays = new RetentionDaysByState { Spam = 7 } } } });
        await FormAsync("Short", definition => definition with { Settings = definition.Settings with { Submission = definition.Settings.Submission with { Store = EntryStorage.Days, StoreDays = 30 } } });
        await FormAsync("Plain", definition => definition);

        var answer = await CallAsync("privacyOfficer", HttpMethod.Get, "retention");

        Assert.Multiple(() =>
        {
            Assert.That(answer.Outcome, Is.EqualTo("200"), answer.Text);
            var body = answer.Body!;
            Assert.That(body["days"]!.ToJsonString(), Is.EqualTo("""{"submitted":365,"approved":365,"rejected":90,"spam":30}"""));
            Assert.That((body["keepForGood"]!.GetValue<bool>(), body["keepForGoodKey"]!.GetValue<string>()), Is.EqualTo((false, "JironFormBuilder:Privacy:AllowRetentionOff")));
            Assert.That(body["version"]!.GetValue<int>(), Is.GreaterThan(0));
            var forms = body["forms"]!.AsArray();
            Assert.That(forms.Select(form => form!["name"]!.GetValue<string>()), Is.EqualTo(new[] { "Short", "Survey" }), "by name, without the form that sets nothing");
            Assert.That(forms.Select(form => (form!["spam"]?.GetValue<int>(), form["storeDays"]?.GetValue<int>())), Is.EqualTo(new[] { ((int?)null, (int?)30), (7, null) }));
        });
    }

    [Test]
    public async Task ASaveOfTheDays_GoesThroughThePrivacySection_AndKeepingForGood_IsRefusedWithoutTheHost()
    {
        var read = await CallAsync("admin", HttpMethod.Get, "retention");
        var version = read.Body!["version"]!.GetValue<int>();

        var saved = await CallAsync("admin", HttpMethod.Put, "settings/privacy", new { version, values = new { retention = new { submitted = 200, approved = 365, rejected = 60, spam = 14 } } });
        var after = await CallAsync("privacyOfficer", HttpMethod.Get, "retention");
        var forGood = await CallAsync("admin", HttpMethod.Put, "settings/privacy", new { version = after.Body!["version"]!.GetValue<int>(), values = new { retention = new { submitted = 0, approved = 365, rejected = 60, spam = 14 } } });
        var outOfRange = await CallAsync("admin", HttpMethod.Put, "settings/privacy", new { version = after.Body!["version"]!.GetValue<int>(), values = new { retention = new { submitted = 3_651, approved = 365, rejected = 60, spam = 14 } } });
        var byOfficer = await CallAsync("privacyOfficer", HttpMethod.Put, "settings/privacy", new { version = after.Body!["version"]!.GetValue<int>(), values = new { retention = new { submitted = 100, approved = 365, rejected = 60, spam = 14 } } });

        Assert.Multiple(() =>
        {
            Assert.That(saved.Outcome, Is.EqualTo("200"), saved.Text);
            Assert.That(after.Body!["days"]!.ToJsonString(), Is.EqualTo("""{"submitted":200,"approved":365,"rejected":60,"spam":14}"""));
            Assert.That(forGood.Outcome, Is.EqualTo("409 setting_locked"), forGood.Text);
            Assert.That(outOfRange.Outcome, Is.EqualTo("400 validation_failed"), outOfRange.Text);
            Assert.That(outOfRange.Body!["errors"]![0]!["path"]!.GetValue<string>(), Is.EqualTo("privacy.retention.submitted"));
            Assert.That((byOfficer.Outcome, byOfficer.Body?["permission"]?.GetValue<string>()), Is.EqualTo(("403 forbidden", "manageSettings")), "changing it needs manageSettings");
        });
    }

    [Test]
    public async Task TheDryRun_CountsByTheDaysSent_AndRefusesADayOrASpanOutOfRange()
    {
        var form = await FormAsync("Contact", definition => definition);
        var row = (await GetRequiredService<IFormRepository>().GetAsync(form))!;
        var entries = GetRequiredService<IEntryRepository>();
        await entries.CreateAsync(new NewEntry { Key = Guid.NewGuid(), FormId = row.Id, FormVersionId = row.PublishedVersionId!.Value, State = EntryState.Spam, SubmissionId = Guid.NewGuid(), SubmissionHash = new string('c', 64) });

        var saved = await CallAsync("privacyOfficer", HttpMethod.Post, "retention/dry-run", new { span = 7 });
        var typed = await CallAsync("privacyOfficer", HttpMethod.Post, "retention/dry-run", new { days = new { submitted = 365, approved = 365, rejected = 90, spam = 5 }, span = 7 });
        var day = await CallAsync("admin", HttpMethod.Post, "retention/dry-run", new { days = new { submitted = 365, approved = 365, rejected = 90, spam = -1 }, span = 7 });
        var span = await CallAsync("admin", HttpMethod.Post, "retention/dry-run", new { span = 8 });

        Assert.Multiple(() =>
        {
            Assert.That((saved.Outcome, saved.Body!["entries"]!.GetValue<long>()), Is.EqualTo(("200", 0L)), saved.Text);
            Assert.That(typed.Body!.ToJsonString(), Is.EqualTo($$"""{"span":7,"entries":1,"held":0,"files":0,"forms":[{"key":"{{form}}","name":"Contact","trashed":false,"entries":1,"held":0,"files":0}]}"""));
            Assert.That(day.Outcome, Is.EqualTo("400 invalid_request"), day.Text);
            Assert.That(span.Outcome, Is.EqualTo("400 invalid_request"), span.Text);
        });
    }

    private async Task<Guid> FormAsync(string name, Func<FormDefinition, FormDefinition> change)
    {
        var forms = GetRequiredService<IFormBuilderFormService>();
        var created = await forms.CreateAsync(new NewFormRequest(name, UserKey: Editor));
        var draft = (await forms.GetDraftAsync(created.Key))!;
        var definition = draft.Definition!;
        var field = new FormField { Key = Guid.NewGuid(), Type = "text", Alias = "name", Label = new LocalizedText([new(string.Empty, "Name")]) };
        definition = definition with { Pages = [definition.Pages[0] with { Groups = [definition.Pages[0].Groups[0] with { Fields = [field] }] }] };
        var saved = (await forms.SaveDraftAsync(created.Key, draft.Form.ConcurrencyVersion, change(definition), null, Editor))!;
        _ = (await forms.PublishAsync(created.Key, saved.Form.ConcurrencyVersion, null, Editor))!;
        return created.Key;
    }
}
