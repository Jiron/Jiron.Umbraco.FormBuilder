using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Audit;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>
/// Activity > Audit log through the management API (ROADMAP 12.4b, ADR-0041, ADR-0219): the newest first, with the user's
/// name, and the names of the items each action is on or names in its details (a form, a user group, an entry by its
/// number, a field by its label), an item that is gone named by none; the filters of what, who, which form and when; the
/// search over people and items; and the export as CSV with its formula guard, which is itself recorded.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class AuditApiTests : ManagementApiTestBase
{
    private const string Admin = "admin";

    private IAuditRepository Audit => GetRequiredService<IAuditRepository>();

    [Test]
    public async Task TheLog_ListsTheNewestFirst_WithWhoDidIt_AndTheNamesOfWhatItNames()
    {
        var site = await SeedAsync();

        var answer = await CallAsync(Admin, HttpMethod.Get, "audit");

        Assert.Multiple(() =>
        {
            Assert.That(answer.Outcome, Is.EqualTo("200"), answer.ToString());
            var items = answer.Body!["items"]!.AsArray();
            Assert.That(items[0]!["action"]!.GetValue<string>(), Is.EqualTo(EntryPanels.RevealedAction), "the newest first");
            Assert.That(items[0]!["userName"]!.GetValue<string>(), Is.EqualTo("privacyOfficer"));
            Assert.That(items[0]!["entityKey"]!.GetValue<Guid>(), Is.EqualTo(site.Entry));
            Assert.That(items[0]!["details"]!["field"]!.GetValue<Guid>(), Is.EqualTo(NameField.Key));
            var names = answer.Body["names"]!.AsObject();
            Assert.That(names[site.Form.ToString()]!["name"]!.GetValue<string>(), Is.EqualTo("Job application"));
            Assert.That(names[site.Entry.ToString()]!["number"]!.GetValue<long>(), Is.EqualTo(1));
            Assert.That(names[site.Entry.ToString()]!["name"]!.GetValue<string>(), Is.EqualTo("Job application"));
            Assert.That(names[NameField.Key.ToString()]!["name"]!.GetValue<string>(), Is.EqualTo("Name"));
            Assert.That(names[NameField.Key.ToString()]!["kind"]!.GetValue<string>(), Is.EqualTo("field"));
            Assert.That(names[People.GroupKeys["viewer"].ToString()]!["name"]!.GetValue<string>(), Is.EqualTo("jfbViewers"));
            Assert.That(names.ContainsKey(site.Gone.ToString()), Is.False, "a deleted item has no name");
            var system = items.Single(item => item!["action"]!.GetValue<string>() == "form.draftsPruned")!;
            Assert.That(system["userKey"], Is.Null, "Jiron Form Builder's own work");
        });
    }

    [Test]
    public async Task TheFilters_NarrowTheLog_ByWhatWhoWhichFormAndWhen()
    {
        var site = await SeedAsync();

        async Task<IReadOnlyList<string>> ActionsAsync(string query) =>
            [.. (await CallAsync(Admin, HttpMethod.Get, $"audit?{query}")).Body!["items"]!.AsArray().Select(item => item!["action"]!.GetValue<string>())];

        var unknown = await CallAsync(Admin, HttpMethod.Get, "audit?what=everything");
        Assert.Multiple(async () =>
        {
            Assert.That(await ActionsAsync("what=library"), Is.EqualTo(new[] { "optionList.created" }));
            Assert.That(await ActionsAsync("what=settings"), Is.EqualTo(new[] { "access.grantChanged", "settings.saved" }));
            Assert.That(await ActionsAsync($"user={People["designer"].Key}"), Is.EqualTo(new[] { "optionList.created", "form.published" }));
            Assert.That(await ActionsAsync($"form={site.Form}"), Is.EqualTo(new[] { "entry.sensitiveRevealed", "form.draftsPruned", "form.published" }));
            Assert.That(await ActionsAsync("days=1"), Has.Count.EqualTo(7));
            Assert.That(unknown.Outcome, Is.EqualTo("400 invalid_request"), unknown.ToString());
        });
    }

    [Test]
    public async Task TheSearch_FindsPeopleByNameOrEmail_AndItemsByName()
    {
        await SeedAsync();

        async Task<IReadOnlyList<string>> ActionsAsync(string search) =>
            [.. (await CallAsync(Admin, HttpMethod.Get, $"audit?search={Uri.EscapeDataString(search)}")).Body!["items"]!.AsArray().Select(item => item!["action"]!.GetValue<string>())];

        Assert.Multiple(async () =>
        {
            Assert.That(await ActionsAsync("privacyOff"), Is.EqualTo(new[] { "entry.sensitiveRevealed" }), "a person's name");
            Assert.That(await ActionsAsync("designer@jfb"), Is.EqualTo(new[] { "optionList.created", "form.published" }), "a person's email");
            Assert.That(await ActionsAsync("job APP"), Is.EqualTo(new[] { "entry.sensitiveRevealed", "form.draftsPruned", "form.published" }), "a form's name, whatever its letter case");
            Assert.That(await ActionsAsync("Countries"), Is.EqualTo(new[] { "optionList.created" }), "an option list's name");
            Assert.That(await ActionsAsync("nobody at all"), Is.Empty);
        });
    }

    [Test]
    public async Task TheExport_WritesTheFilteredLogAsCsv_GuardedAgainstFormulas_AndIsRecorded()
    {
        var site = await SeedAsync();

        var response = await Api.SendAsync(Admin, HttpMethod.Get, $"audit/export?form={site.Form}", body: null);
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var text = Encoding.UTF8.GetString(bytes);
        var lines = text.TrimStart('\uFEFF').Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        var recorded = await Audit.QueryAsync(new AuditQuery { Action = AuditGroups.ExportedAction }, PageRequest.First(5));

        Assert.Multiple(() =>
        {
            Assert.That((int)response.StatusCode, Is.EqualTo(200), text);
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/csv"));
            Assert.That(response.Content.Headers.ContentDisposition?.FileName, Does.Match("audit-log-\\d{4}-\\d{2}-\\d{2}\\.csv"));
            Assert.That(bytes.Take(3), Is.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }), "the mark, so spreadsheets read UTF-8");
            Assert.That(lines[0], Is.EqualTo("occurredUtc,user,userKey,action,entityType,entityKey,item,details"));
            Assert.That(lines, Has.Length.EqualTo(4));
            Assert.That(lines[1], Does.Contain(",privacyOfficer,").And.Contain(",entry.sensitiveRevealed,"));
            Assert.That(lines[3], Does.Contain($",form,{site.Form},Job application,"));
            Assert.That(recorded.Items, Has.Count.EqualTo(1));
            Assert.That(recorded.Items[0].Details, Does.Contain("\"rows\":3"));
        });
    }

    [Test]
    public async Task AnExportedCell_ThatASpreadsheetWouldRun_StartsWithAnApostrophe()
    {
        var formula = await FormAsync("=HYPERLINK(\"x\")");
        await Audit.AddAsync(new NewAuditEntry("form.renamed", FormAudit.EntityType, formula, People["designer"].Key));

        var response = await Api.SendAsync(Admin, HttpMethod.Get, $"audit/export?form={formula}", body: null);
        var text = await response.Content.ReadAsStringAsync();

        Assert.That(text, Does.Contain(",\"'=HYPERLINK(\"\"x\"\")\","), text);
    }

    // A form with an entry; its publish by the designer, a prune by the site itself, a reveal of the entry's Name field by
    // the privacy officer, recorded on the entry as the panel records it; an option list by the designer, a grant to the
    // viewers and a settings save by the admin, and an action on a form that is gone. Each is a moment apart, so the newest
    // is the reveal.
    private async Task<Seeded> SeedAsync()
    {
        var form = await FormAsync("Job application");
        await FillAsync(form);
        var row = (await GetRequiredService<IFormRepository>().GetAsync(form))!;
        var entry = (await GetRequiredService<IEntryRepository>().CreateAsync(new NewEntry
        {
            Key = Guid.NewGuid(),
            FormId = row.Id,
            FormVersionId = row.DraftVersionId!.Value,
            State = EntryState.Submitted,
            SubmissionId = Guid.NewGuid(),
            SubmissionHash = new string('a', 64),
            Culture = "en-US",
        }))!;
        var list = await GetRequiredService<IOptionListRepository>().CreateAsync(new NewOptionList(Guid.NewGuid(), "countries", "Countries", OptionListKind.Manual));
        var gone = Guid.NewGuid();

        // Cleared first: the set-up's own actions are not this test's.
        await GetRequiredService<FormBuilderDatabase>().WriteAsync(db => db.ExecuteAsync("DELETE FROM jfbAuditEntry"));
        await AddAsync(new NewAuditEntry("form.trashed", FormAudit.EntityType, gone, People["admin"].Key));
        await AddAsync(new NewAuditEntry("settings.saved", "settings", null, People["admin"].Key, new AuditDetails().AddIdentifier("section", "email").Add("version", 2)));
        await AddAsync(new NewAuditEntry("access.grantChanged", "access", null, People["admin"].Key, new AuditDetails().AddIdentifier("target", "global").Add("group", People.GroupKeys["viewer"])));
        await AddAsync(new NewAuditEntry("form.published", FormAudit.EntityType, form, People["designer"].Key, new AuditDetails().Add("version", 2)));
        await AddAsync(new NewAuditEntry("optionList.created", "optionList", list.Key, People["designer"].Key));
        await AddAsync(new NewAuditEntry("form.draftsPruned", FormAudit.EntityType, form, null));
        await AddAsync(new NewAuditEntry(EntryPanels.RevealedAction, EntryPanels.EntityType, entry.Key, People["privacyOfficer"].Key, new AuditDetails().Add("form", form).Add("field", NameField.Key)));
        return new Seeded(form, entry.Key, gone);
    }

    // Recorded a moment after the one before, as SQL Server keeps times to about 3 ms.
    private async Task AddAsync(NewAuditEntry entry)
    {
        await Audit.AddAsync(entry);
        await Task.Delay(20);
    }

    private sealed record Seeded(Guid Form, Guid Entry, Guid Gone);
}
