using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>The Overview's newest entries through the management API (ROADMAP 9.1, ADR-0165): what has arrived, of the forms the user may view.</summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class RecentEntriesApiTests : ManagementApiTestBase
{
    private const string Admin = "admin";

    private IEntryRepository Entries => GetRequiredService<IEntryRepository>();

    private async Task<Guid> PublishedAsync(string name, Guid? folder = null)
    {
        var forms = GetRequiredService<IFormBuilderFormService>();
        var created = await forms.CreateAsync(new NewFormRequest(name, FolderKey: folder));
        var draft = (await forms.GetDraftAsync(created.Key))!;
        var saved = (await forms.SaveDraftAsync(created.Key, draft.Form.ConcurrencyVersion, draft.Definition!, null, null))!;
        await forms.PublishAsync(created.Key, saved.Form.ConcurrencyVersion, null, null);
        return created.Key;
    }

    private async Task<StoredEntry> EntryAsync(Guid form, EntryState state = EntryState.Submitted, bool isTest = false)
    {
        var row = (await GetRequiredService<IFormRepository>().GetAsync(form))!;
        return (await Entries.CreateAsync(new NewEntry
        {
            Key = Guid.NewGuid(),
            FormId = row.Id,
            FormVersionId = row.PublishedVersionId!.Value,
            State = state,
            IsTest = isTest,
            SubmissionId = Guid.NewGuid(),
            SubmissionHash = new string('a', 64),
            Culture = "en-US",
        }))!;
    }

    private static IEnumerable<Guid> Keys(ApiAnswer answer) => answer.Body!["items"]!.AsArray().Select(item => item!["key"]!.GetValue<Guid>());

    [Test]
    public async Task TheList_HoldsWhatHasArrived_NewestFirst_AndNothingElse()
    {
        var form = await PublishedAsync("Contact");
        var older = await EntryAsync(form, EntryState.Approved);
        var rejected = await EntryAsync(form, EntryState.Rejected);
        var newest = await EntryAsync(form);
        await EntryAsync(form, EntryState.Spam);
        await EntryAsync(form, EntryState.Draft);
        await EntryAsync(form, EntryState.Unverified);
        await EntryAsync(form, isTest: true);

        var answer = await CallAsync(Admin, HttpMethod.Get, "entry/recent");

        Assert.Multiple(() =>
        {
            Assert.That(answer.Outcome, Is.EqualTo("200"), answer.ToString());
            Assert.That(Keys(answer), Is.EqualTo(new[] { newest.Key, rejected.Key, older.Key }));
            var first = answer.Body!["items"]![0]!;
            Assert.That(first["formName"]!.GetValue<string>(), Is.EqualTo("Contact"));
            Assert.That(first["formKey"]!.GetValue<Guid>(), Is.EqualTo(form));
            Assert.That(first["number"]!.GetValue<int>(), Is.EqualTo(newest.Number));
            Assert.That(first["unseen"]!.GetValue<bool>(), Is.True);
            Assert.That(first["title"], Is.Null, "a form without an entry title names its entries by number");
            Assert.That(first.AsObject().Select(member => member.Key), Is.EquivalentTo(new[] { "key", "formKey", "formName", "title", "number", "submitted", "unseen" }), "no answers");
        });
    }

    [Test]
    public async Task TheList_StopsAtTen()
    {
        var form = await PublishedAsync("Busy");
        for (var count = 0; count < RecentEntries.Count + 2; count++)
        {
            await EntryAsync(form);
        }

        Assert.That(Keys(await CallAsync(Admin, HttpMethod.Get, "entry/recent")).Count(), Is.EqualTo(RecentEntries.Count));
    }

    [Test]
    public async Task ATrashedForm_LeavesTheList()
    {
        var form = await PublishedAsync("Gone");
        await EntryAsync(form);
        await GetRequiredService<IFormBuilderFormService>().TrashAsync(form, (await GetRequiredService<IFormRepository>().GetAsync(form))!.ConcurrencyVersion, null);

        Assert.That(Keys(await CallAsync(Admin, HttpMethod.Get, "entry/recent")), Is.Empty);
    }

    [Test]
    public async Task TheList_HoldsOnlyTheFormsAUserMayView()
    {
        var open = await PublishedAsync("Open");
        var folder = await FolderAsync("HR");
        await RestrictAsync(folder, "hr", FormBuilderPermissions.ViewEntries);
        var restricted = await PublishedAsync("Salaries", folder);
        var everyone = await EntryAsync(open);
        var hrOnly = await EntryAsync(restricted);

        var viewer = await CallAsync("viewer", HttpMethod.Get, "entry/recent");
        var hr = await CallAsync("hr", HttpMethod.Get, "entry/recent");
        var designer = await CallAsync("designer", HttpMethod.Get, "entry/recent");
        var admin = await CallAsync(Admin, HttpMethod.Get, "entry/recent");

        Assert.Multiple(() =>
        {
            Assert.That(Keys(viewer), Is.EqualTo(new[] { everyone.Key }), "the restricted folder stays out of view");
            Assert.That(Keys(hr), Is.EqualTo(new[] { hrOnly.Key }), "the folder's grant is theirs, and nothing else");
            Assert.That(Keys(designer), Is.Empty, "design doesn't view entries");
            Assert.That(Keys(admin), Is.EquivalentTo(new[] { everyone.Key, hrOnly.Key }));
        });
    }
}
