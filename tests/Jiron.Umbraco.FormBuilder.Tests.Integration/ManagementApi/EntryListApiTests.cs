using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Queries;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>A form's entries list through the management API (ROADMAP 11.1, ADR-0207).</summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class EntryListApiTests : ManagementApiTestBase
{
    private const string Admin = "admin";

    private IEntryRepository Entries => GetRequiredService<IEntryRepository>();

    [Test]
    public async Task TheView_OffersTheFieldsColumnsAndSets_AndAPageCarriesItsRows()
    {
        var form = await PublishedAsync();
        var entry = await EntryAsync(form);
        var setKey = Guid.NewGuid();

        var columns = await CallAsync(Admin, HttpMethod.Put, $"form/{form}/entries/columns", new { columns = new[] { NameField.Key } });
        var saved = await CallAsync(Admin, HttpMethod.Put, $"entry/filter-set/{setKey}", new { name = " Waiting ", form, filter = new { states = new[] { "submitted" }, received = new { preset = "last7Days" } } });
        var view = await CallAsync(Admin, HttpMethod.Get, $"form/{form}/entries/view");
        var page = await CallAsync(Admin, HttpMethod.Post, $"form/{form}/entries", new { filter = new { states = new[] { "submitted" } }, columns = new[] { NameField.Key }, count = true });

        Assert.Multiple(() =>
        {
            Assert.That(columns.Outcome, Is.EqualTo("204"), columns.ToString());
            Assert.That(saved.Outcome, Is.EqualTo("200"), saved.ToString());
            Assert.That(saved.Body!["name"]!.GetValue<string>(), Is.EqualTo("Waiting"), "trimmed");
            Assert.That(view.Outcome, Is.EqualTo("200"), view.ToString());
            Assert.That(view.Body!["fields"]![0]!["key"]!.GetValue<Guid>(), Is.EqualTo(NameField.Key));
            Assert.That(view.Body!["fields"]![0]!["kind"]!.GetValue<string>(), Is.EqualTo("text"));
            Assert.That(view.Body!["fields"]![0]!["tests"]!.AsArray().Select(test => test!.GetValue<string>()), Is.EqualTo(new[] { "oneOf", "noneOf", "startsWith", "answered", "notAnswered" }));
            Assert.That(view.Body!["columns"]!.AsArray().Select(column => column!.GetValue<Guid>()), Is.EqualTo(new[] { NameField.Key }), "the same on every device");
            Assert.That(view.Body!["filterSets"]![0]!["filter"]!["received"]!["preset"]!.GetValue<string>(), Is.EqualTo("last7Days"));
            Assert.That(page.Outcome, Is.EqualTo("200"), page.ToString());
            Assert.That(page.Body!["total"]!.GetValue<int>(), Is.EqualTo(1));
            Assert.That(page.Body!["more"]!.GetValue<bool>(), Is.False);
            Assert.That(page.Body!["items"]![0]!["key"]!.GetValue<Guid>(), Is.EqualTo(entry.Key));
            Assert.That(page.Body!["items"]![0]!["state"]!.GetValue<string>(), Is.EqualTo("submitted"));
            Assert.That(page.Body!["items"]![0]!["unseen"]!.GetValue<bool>(), Is.True);
            Assert.That(page.Body!["items"]![0]!["actions"]!["delivered"]!.GetValue<int>(), Is.Zero);
            Assert.That(page.Body!["items"]![0]!["cells"]![0]!["field"]!.GetValue<Guid>(), Is.EqualTo(NameField.Key));
        });
    }

    [Test]
    public async Task WhatAListCannotAnswer_IsRefusedByItsPath()
    {
        var form = await PublishedAsync();

        var column = await CallAsync(Admin, HttpMethod.Put, $"form/{form}/entries/columns", new { columns = new[] { Guid.NewGuid() } });
        var state = await CallAsync(Admin, HttpMethod.Post, $"form/{form}/entries", new { filter = new { states = new[] { "submitted", "draft" } } });
        var test = await CallAsync(Admin, HttpMethod.Post, $"form/{form}/entries", new { filter = new { fields = new[] { new { field = NameField.Key, test = "between", min = 1 } } } });
        var cursor = await CallAsync(Admin, HttpMethod.Post, $"form/{form}/entries", new { after = "nothing" });
        var otherForm = await CallAsync(Admin, HttpMethod.Post, $"form/{form}/entries", new { filter = new { forms = new[] { Guid.NewGuid() } } });
        var manyFilters = await CallAsync(Admin, HttpMethod.Post, $"form/{form}/entries", new { filter = new { fields = Enumerable.Range(0, EntryQuerySql.MaxFilters + 1).Select(_ => new { field = NameField.Key, test = "answered" }).ToArray() } });
        var longSearch = await CallAsync(Admin, HttpMethod.Post, $"form/{form}/entries", new { filter = new { search = new string('a', EntrySearchText.MaxTermLength + 1) } });

        Assert.Multiple(() =>
        {
            Assert.That((column.Outcome, Path(column)), Is.EqualTo(("400 field_not_column", "columns[0]")), column.ToString());
            Assert.That((otherForm.Outcome, Path(otherForm)), Is.EqualTo(("400 form_not_allowed", "filter.forms")), "a form's list holds its own entries only");
            Assert.That((manyFilters.Outcome, Path(manyFilters)), Is.EqualTo(("400 too_many_filters", "filter.fields")), manyFilters.ToString());
            Assert.That((longSearch.Outcome, Path(longSearch)), Is.EqualTo(("400 search_too_long", "filter.search")), longSearch.ToString());
            Assert.That((state.Outcome, Path(state)), Is.EqualTo(("400 invalid_state", "filter.states[1]")), state.ToString());
            Assert.That((test.Outcome, Path(test)), Is.EqualTo(("400 test_not_allowed", "filter.fields[0].test")), test.ToString());
            Assert.That((cursor.Outcome, Path(cursor)), Is.EqualTo(("400 invalid_cursor", "after")), cursor.ToString());
        });
    }

    [Test]
    public async Task FilterSets_AreTheUsersOwn_AndKeepOnlyFormsTheyMayView()
    {
        var form = await PublishedAsync();
        var mine = Guid.NewGuid();

        var saved = await CallAsync("viewer", HttpMethod.Put, $"entry/filter-set/{mine}", new { name = "Mine", form, filter = new { } });
        var adminView = await CallAsync(Admin, HttpMethod.Get, $"form/{form}/entries/view");
        var viewerView = await CallAsync("viewer", HttpMethod.Get, $"form/{form}/entries/view");
        var taken = await CallAsync("viewer", HttpMethod.Put, $"entry/filter-set/{Guid.NewGuid()}", new { name = "MINE", form, filter = new { } });
        var unseen = await CallAsync("designer", HttpMethod.Put, $"entry/filter-set/{Guid.NewGuid()}", new { name = "Theirs", form, filter = new { } });
        var everyForm = await CallAsync("viewer", HttpMethod.Put, $"entry/filter-set/{Guid.NewGuid()}", new { name = "Everywhere", filter = new { fields = new[] { new { field = NameField.Key, test = "answered" } } } });
        var deleted = await CallAsync("viewer", HttpMethod.Delete, $"entry/filter-set/{mine}");
        var again = await CallAsync("viewer", HttpMethod.Delete, $"entry/filter-set/{mine}");

        Assert.Multiple(() =>
        {
            Assert.That(saved.Outcome, Is.EqualTo("200"), saved.ToString());
            Assert.That(adminView.Body!["filterSets"]!.AsArray(), Is.Empty, "another user's sets are not offered");
            Assert.That(viewerView.Body!["filterSets"]!.AsArray().Select(set => set!["key"]!.GetValue<Guid>()), Is.EqualTo(new[] { mine }));
            Assert.That((taken.Outcome, Path(taken)), Is.EqualTo(("400 name_taken", "name")), "names compare without case");
            Assert.That(unseen.Outcome, Is.EqualTo("404 form_not_found"), "a form the user may not view entries of");
            Assert.That((everyForm.Outcome, Path(everyForm)), Is.EqualTo(("400 no_form", "filter.fields")), everyForm.ToString());
            Assert.That((deleted.Outcome, deleted.Body!["deleted"]!.GetValue<bool>()), Is.EqualTo(("200", true)));
            Assert.That((again.Outcome, again.Body!["deleted"]!.GetValue<bool>()), Is.EqualTo(("200", false)), "gone already");
        });
    }

    [Test]
    public async Task TheBulkBar_DeletesAndRerunsUpToAPage_AndNamesWhatItDid()
    {
        var form = await PublishedAsync();
        var entry = await EntryAsync(form);

        var none = await CallAsync(Admin, HttpMethod.Post, $"form/{form}/entries/delete", new { entries = Array.Empty<Guid>() });
        var many = await CallAsync(Admin, HttpMethod.Post, $"form/{form}/entries/run-actions", new { entries = Enumerable.Range(0, EntryDeletions.MaxEntries + 1).Select(_ => Guid.NewGuid()) });
        var plan = await CallAsync(Admin, HttpMethod.Post, $"form/{form}/entries/run-actions", new { entries = new[] { entry.Key } });
        var asked = await CallAsync("privacyOfficer", HttpMethod.Post, $"form/{form}/entries/delete", new { entries = new[] { entry.Key, Guid.NewGuid() } });
        var kept = await Entries.GetByIdAsync(entry.Id);
        var deleted = await CallAsync("privacyOfficer", HttpMethod.Post, $"form/{form}/entries/delete", new { entries = new[] { entry.Key, Guid.NewGuid() }, confirm = true });

        Assert.Multiple(async () =>
        {
            Assert.That(none.Outcome, Is.EqualTo("400 entries_invalid"));
            Assert.That(many.Outcome, Is.EqualTo("400 entries_invalid"));
            Assert.That(plan.Outcome, Is.EqualTo("200"), plan.ToString());
            Assert.That(plan.Body!["actions"]!.AsArray(), Is.Empty, "the form has no actions");
            Assert.That((plan.Body!["entries"]!.GetValue<int>(), plan.Body!["leftOut"]!.GetValue<int>(), plan.Body!["queued"]!.GetValue<bool>()), Is.EqualTo((0, 1, false)));
            Assert.That((asked.Body!["entries"]!.GetValue<int>(), asked.Body!["files"]!.GetValue<int>(), asked.Body!["deleted"]!.GetValue<bool>()), Is.EqualTo((1, 0, false)), "what would go, for the dialog");
            Assert.That(kept, Is.Not.Null, "asking deletes nothing");
            Assert.That(deleted.Outcome, Is.EqualTo("200"), deleted.ToString());
            Assert.That((deleted.Body!["entries"]!.GetValue<int>(), deleted.Body!["deleted"]!.GetValue<bool>()), Is.EqualTo((1, true)));
            Assert.That(await Entries.GetByIdAsync(entry.Id), Is.Null);
        });
    }

    private static string? Path(ApiAnswer answer) => answer.Body?["errors"]?[0]?["path"]?.GetValue<string>();

    private async Task<Guid> PublishedAsync()
    {
        var forms = GetRequiredService<IFormBuilderFormService>();
        var created = await forms.CreateAsync(new NewFormRequest($"Form {Guid.NewGuid():N}", UserKey: null));
        var draft = (await forms.GetDraftAsync(created.Key))!;
        var saved = (await forms.SaveDraftAsync(created.Key, draft.Form.ConcurrencyVersion, Fillable(draft.Definition!), null, null))!;
        await forms.PublishAsync(created.Key, saved.Form.ConcurrencyVersion, null, null);
        return created.Key;
    }

    private async Task<StoredEntry> EntryAsync(Guid form)
    {
        var row = (await GetRequiredService<IFormRepository>().GetAsync(form))!;
        return (await Entries.CreateAsync(new NewEntry
        {
            Key = Guid.NewGuid(),
            FormId = row.Id,
            FormVersionId = row.PublishedVersionId!.Value,
            State = EntryState.Submitted,
            SubmissionId = Guid.NewGuid(),
            SubmissionHash = new string('a', 64),
            Culture = "en-US",
        }))!;
    }
}
