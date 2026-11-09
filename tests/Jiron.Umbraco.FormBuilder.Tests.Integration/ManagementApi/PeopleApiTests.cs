using System.IO.Compression;
using System.Net;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Exports;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>
/// Privacy > People's endpoints (ROADMAP 13.1 part 13.2, IA 9, ADR-0223): a privacy officer finds a person's entries in every
/// form and nobody else may; a request names an address or a member, never both; the erase is previewed, then deletes; a
/// hold keeps an entry from it; and the export runs in the background, followed by its owner and downloaded with its
/// ticket, which reaches no form's export.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class PeopleApiTests : ManagementApiTestBase
{
    private const string Ada = "ada@jfb.test";

    private static readonly Guid Editor = Guid.Parse("7d1c3a52-31f4-4e57-9d2e-61b0f6a1c223");

    private static readonly FormField EmailField = new() { Key = Guid.Parse("e2230000-0000-4000-8000-000000000101"), Type = "email", Alias = "email", Label = new LocalizedText("Email") };

    [Test]
    public async Task Find_ListsThePersonsEntriesInEveryForm_ForAPrivacyOfficerOnly_AndNamesAnAddressOrAMember()
    {
        var contact = await FormAsync("Contact");
        var survey = await FormAsync("Survey");
        var first = await EntryAsync(contact, Ada);
        var second = await EntryAsync(survey, Ada.ToUpperInvariant());
        await EntryAsync(contact, "grace@jfb.test");

        var found = await CallAsync("privacyOfficer", HttpMethod.Post, "people/find", new { email = Ada });
        var designer = await CallAsync("designer", HttpMethod.Post, "people/find", new { email = Ada });
        var both = await CallAsync("privacyOfficer", HttpMethod.Post, "people/find", new { email = Ada, member = Guid.NewGuid() });
        var neither = await CallAsync("privacyOfficer", HttpMethod.Post, "people/find", new { });
        var notAnAddress = await CallAsync("privacyOfficer", HttpMethod.Post, "people/find", new { email = "Ada Lovelace" });

        Assert.Multiple(() =>
        {
            Assert.That(found.Outcome, Is.EqualTo("200"), found.Text);
            var entries = found.Body!["entries"]!.AsArray();
            Assert.That(entries.Select(entry => entry!["key"]!.GetValue<Guid>()), Is.EqualTo(new[] { second, first }), "newest first, in both forms");
            Assert.That(entries[0]!["form"]!["name"]!.GetValue<string>(), Is.EqualTo("Survey"));
            Assert.That((found.Body["forms"]!.GetValue<int>(), found.Body["more"]!.GetValue<bool>()), Is.EqualTo((2, false)));
            Assert.That((designer.Outcome, designer.Body?["permission"]?.GetValue<string>()), Is.EqualTo(("403 forbidden", "privacyTools")));
            Assert.That(both.Outcome, Is.EqualTo("400 person_invalid"), both.Text);
            Assert.That(neither.Outcome, Is.EqualTo("400 person_invalid"), neither.Text);
            Assert.That(notAnAddress.Outcome, Is.EqualTo("400 email_invalid"), notAnAddress.Text);
        });
    }

    [Test]
    public async Task Erase_IsPreviewedFirst_LeavesAHeldEntry_AndRefusesAModeItDoesNotKnow()
    {
        var contact = await FormAsync("Contact");
        var held = await EntryAsync(contact, Ada);
        var gone = await EntryAsync(contact, Ada);

        var hold = await CallAsync("privacyOfficer", HttpMethod.Post, "people/hold", new { email = Ada, entries = new[] { held }, until = DateTimeOffset.UtcNow.AddDays(10), reason = "Court case" });
        var tooLate = await CallAsync("privacyOfficer", HttpMethod.Post, "people/hold", new { email = Ada, entries = new[] { held }, until = DateTimeOffset.UtcNow.AddDays(400), reason = "Court case" });
        var noReason = await CallAsync("privacyOfficer", HttpMethod.Post, "people/hold", new { email = Ada, entries = new[] { held }, until = DateTimeOffset.UtcNow.AddDays(10), reason = " " });
        var twoLines = await CallAsync("privacyOfficer", HttpMethod.Post, "people/hold", new { email = Ada, entries = new[] { held }, until = DateTimeOffset.UtcNow.AddDays(10), reason = "Court case\nand more" });
        var preview = await CallAsync("privacyOfficer", HttpMethod.Post, "people/erase/preview", new { email = Ada });
        var unknown = await CallAsync("privacyOfficer", HttpMethod.Post, "people/erase", new { email = Ada, mode = "shred" });
        var erased = await CallAsync("privacyOfficer", HttpMethod.Post, "people/erase", new { email = Ada, mode = "delete" });
        var released = await CallAsync("privacyOfficer", HttpMethod.Post, "people/release", new { email = Ada, entries = new[] { held } });
        var left = await CallAsync("privacyOfficer", HttpMethod.Post, "people/find", new { email = Ada });

        Assert.Multiple(() =>
        {
            Assert.That((hold.Outcome, hold.Body?["entries"]?.GetValue<int>()), Is.EqualTo(("200", (int?)1)), hold.Text);
            Assert.That(tooLate.Outcome, Is.EqualTo("400 hold_end"), tooLate.Text);
            Assert.That((noReason.Outcome, twoLines.Outcome), Is.EqualTo(("400 hold_reason", "400 hold_reason")), noReason.Text);
            Assert.That(preview.Body!.ToJsonString(), Does.StartWith("""{"entries":1,"drafts":0,"held":1,"files":0,"emails":0,"more":false,"forms":[{"""));
            Assert.That(unknown.Outcome, Is.EqualTo("400 mode_invalid"));
            Assert.That((erased.Outcome, erased.Body?["entries"]?.GetValue<int>(), erased.Body?["held"]?.GetValue<int>()), Is.EqualTo(("200", (int?)1, (int?)1)), erased.Text);
            Assert.That(erased.Body!["auditId"]!.GetValue<long>(), Is.GreaterThan(0));
            Assert.That(released.Body!["entries"]!.GetValue<int>(), Is.EqualTo(1));
            Assert.That(left.Body!["entries"]!.AsArray().Select(entry => entry!["key"]!.GetValue<Guid>()), Is.EqualTo(new[] { held }), $"the held entry stays; {gone} went");
        });
    }

    [Test]
    public async Task TheExport_IsFollowedByItsOwnerOnly_AndDownloadsThroughItsOwnTicket()
    {
        var contact = await FormAsync("Contact");
        await EntryAsync(contact, Ada);

        var started = await CallAsync("privacyOfficer", HttpMethod.Post, "people/export", new { email = Ada });
        var key = started.Body!["key"]!.GetValue<Guid>();
        await GetRequiredService<EntryExportJobs>().PassAsync("test", CancellationToken.None);
        var status = await CallAsync("privacyOfficer", HttpMethod.Get, $"people/export/{key}");
        var someoneElse = await CallAsync("admin", HttpMethod.Get, $"people/export/{key}");
        var ticket = await CallAsync("privacyOfficer", HttpMethod.Post, $"people/export/{key}/ticket");
        var link = ticket.Body!["url"]!.GetValue<string>()[ManagementApiCaller.BasePath.Length..];
        using var download = await Api.SendAsync(null, link);
        var bytes = await download.Content.ReadAsByteArrayAsync();
        var withoutTicket = await CallAsync(null, HttpMethod.Get, $"people/export/{key}/file");
        var asAForm = await CallAsync(null, HttpMethod.Get, $"form/{contact}/entries/export/jobs/{key}/file?{link[(link.IndexOf('?', StringComparison.Ordinal) + 1)..]}");

        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        Assert.Multiple(() =>
        {
            Assert.That((started.Outcome, started.Body!["state"]!.GetValue<string>(), started.Body!["total"]!.GetValue<int>()), Is.EqualTo(("200", "queued", 1)), started.Text);
            Assert.That((status.Body!["state"]!.GetValue<string>(), status.Body!["done"]!.GetValue<int>()), Is.EqualTo(("ready", 1)), status.Text);
            Assert.That(status.Body!["fileName"]!.GetValue<string>(), Does.StartWith("personal-data-"));
            Assert.That(someoneElse.Outcome, Is.EqualTo("404 export_not_found"), "another user does not follow it");
            Assert.That(download.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(archive.Entries.Select(entry => entry.Name), Is.EqualTo(new[] { "entry.json" }));
            Assert.That(withoutTicket.Outcome, Is.EqualTo("404 export_not_found"));
            Assert.That(asAForm.Outcome, Is.EqualTo("404 export_not_found"), "its ticket reaches no form's export");
        });
    }

    private async Task<Guid> FormAsync(string name)
    {
        var forms = GetRequiredService<IFormBuilderFormService>();
        var created = await forms.CreateAsync(new NewFormRequest(name, UserKey: Editor));
        var draft = (await forms.GetDraftAsync(created.Key))!;
        var definition = draft.Definition!;
        var field = new FormField { Key = Guid.NewGuid(), Type = "text", Alias = "name", Label = new LocalizedText([new(string.Empty, "Name")]) };
        definition = definition with { Pages = [definition.Pages[0] with { Groups = [definition.Pages[0].Groups[0] with { Fields = [field, EmailField] }] }] };
        var saved = (await forms.SaveDraftAsync(created.Key, draft.Form.ConcurrencyVersion, definition, null, Editor))!;
        _ = (await forms.PublishAsync(created.Key, saved.Form.ConcurrencyVersion, null, Editor))!;
        return created.Key;
    }

    // An approved entry of the form whose email field holds the address.
    private async Task<Guid> EntryAsync(Guid form, string email)
    {
        var row = (await GetRequiredService<IFormRepository>().GetAsync(form))!;
        var field = (await GetRequiredService<IFormRepository>().ListFieldsAsync(row.Id)).Single(item => item.Values.FieldKey == EmailField.Key);
        var entry = await GetRequiredService<IEntryRepository>().CreateAsync(new NewEntry
        {
            Key = Guid.NewGuid(),
            FormId = row.Id,
            FormVersionId = row.PublishedVersionId!.Value,
            State = EntryState.Approved,
            SubmissionId = Guid.NewGuid(),
            SubmissionHash = new string('c', 64),
            Values = [new NewEntryValue(field.Id, email)],
        });
        return entry!.Key;
    }
}
