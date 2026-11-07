using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>Changing entries' state and undoing it through the management API (ROADMAP 8.5, ADR-0023, ADR-0162).</summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class EntryStateApiTests : ManagementApiTestBase
{
    private const string Admin = "admin";

    private IEntryRepository Entries => GetRequiredService<IEntryRepository>();

    private async Task<Guid> PublishedAsync(params FormAction[] actions)
    {
        var forms = GetRequiredService<IFormBuilderFormService>();
        var created = await forms.CreateAsync(new NewFormRequest($"Form {Guid.NewGuid():N}", UserKey: null));
        var draft = (await forms.GetDraftAsync(created.Key))!;
        var saved = (await forms.SaveDraftAsync(created.Key, draft.Form.ConcurrencyVersion, Fillable(draft.Definition!) with { Actions = actions }, null, null))!;
        await forms.PublishAsync(created.Key, saved.Form.ConcurrencyVersion, null, null);
        return created.Key;
    }

    private async Task<(Guid Form, StoredEntry Entry)> EntryAsync()
    {
        var form = await PublishedAsync();
        var row = (await GetRequiredService<IFormRepository>().GetAsync(form))!;
        var entry = (await Entries.CreateAsync(new NewEntry
        {
            Key = Guid.NewGuid(),
            FormId = row.Id,
            FormVersionId = row.PublishedVersionId!.Value,
            State = EntryState.Submitted,
            SubmissionId = Guid.NewGuid(),
            SubmissionHash = new string('a', 64),
            Culture = "en-US",
        }))!;
        return (form, entry);
    }

    [Test]
    public async Task AChange_SaysWhatMoved_AndWhenItsWindowEnds_AndItsUndoPutsItBack()
    {
        var (form, entry) = await EntryAsync();
        var gone = Guid.NewGuid();

        var change = await CallAsync(Admin, HttpMethod.Post, $"form/{form}/entry-state", new { entries = new[] { entry.Key, gone }, state = "approved" });
        var moved = await Entries.GetByIdAsync(entry.Id);
        var undo = await CallAsync(Admin, HttpMethod.Post, $"form/{form}/entry-state/undo", new { changeKey = change.Body!["changeKey"]!.GetValue<Guid>() });

        Assert.Multiple(async () =>
        {
            Assert.That(change.Outcome, Is.EqualTo("200"), change.ToString());
            Assert.That(change.Body!["moved"]![0]!["key"]!.GetValue<Guid>(), Is.EqualTo(entry.Key));
            Assert.That(change.Body!["moved"]![0]!["state"]!.GetValue<string>(), Is.EqualTo("approved"));
            Assert.That(change.Body!["unmoved"]![0]!.GetValue<Guid>(), Is.EqualTo(gone));
            Assert.That(change.Body!["undoUntilUtc"]!.GetValue<DateTimeOffset>(), Is.GreaterThan(DateTimeOffset.UtcNow));
            Assert.That(moved!.State, Is.EqualTo(EntryState.Approved));
            Assert.That(undo.Outcome, Is.EqualTo("204"), undo.ToString());
            Assert.That((await Entries.GetByIdAsync(entry.Id))!.State, Is.EqualTo(EntryState.Submitted));
        });
    }

    [Test]
    public async Task ACheck_MovesNothing_AndOffersNoUndo()
    {
        var (form, entry) = await EntryAsync();

        var check = await CallAsync(Admin, HttpMethod.Post, $"form/{form}/entry-state", new { entries = new[] { entry.Key }, state = "approved", check = true });
        var stayed = await Entries.GetByIdAsync(entry.Id);

        Assert.Multiple(() =>
        {
            Assert.That(check.Outcome, Is.EqualTo("200"), check.ToString());
            Assert.That((check.Body!["moved"]!.AsArray().Count, check.Body!["unmoved"]!.AsArray().Count), Is.EqualTo((0, 0)));
            Assert.That(check.Body!["undoUntilUtc"], Is.Null);
            Assert.That(check.Body!["asks"], Is.Null, "a move that holds no places asks nothing");
            Assert.That(stayed!.State, Is.EqualTo(EntryState.Submitted), "the check moves nothing");
        });
    }

    [TestCase("draft", TestName = "A state nobody chooses")]
    [TestCase("unverified", TestName = "The state only a form sets")]
    [TestCase("", TestName = "No state")]
    public async Task AChange_ToAStateNobodyChooses_IsRefused(string state)
    {
        var (form, entry) = await EntryAsync();

        var answer = await CallAsync(Admin, HttpMethod.Post, $"form/{form}/entry-state", new { entries = new[] { entry.Key }, state });

        Assert.That(answer.Outcome, Is.EqualTo("400 entry_state_invalid"), answer.ToString());
    }

    [Test]
    public async Task AChange_OfNoEntries_OrTooMany_IsRefused()
    {
        var (form, _) = await EntryAsync();

        var none = await CallAsync(Admin, HttpMethod.Post, $"form/{form}/entry-state", new { entries = Array.Empty<Guid>(), state = "approved" });
        var many = await CallAsync(Admin, HttpMethod.Post, $"form/{form}/entry-state", new { entries = Enumerable.Range(0, EntryStateChanges.MaxEntries + 1).Select(_ => Guid.NewGuid()), state = "approved" });

        Assert.Multiple(() =>
        {
            Assert.That(none.Outcome, Is.EqualTo("400 entry_state_invalid"));
            Assert.That(many.Outcome, Is.EqualTo("400 entry_state_invalid"));
        });
    }

    [Test]
    public async Task AnUndo_OfAChangeSomeoneMovedOnFrom_IsTooLate()
    {
        var (form, entry) = await EntryAsync();
        var change = await CallAsync(Admin, HttpMethod.Post, $"form/{form}/entry-state", new { entries = new[] { entry.Key }, state = "approved" });
        await CallAsync(Admin, HttpMethod.Post, $"form/{form}/entry-state", new { entries = new[] { entry.Key }, state = "rejected" });

        var late = await CallAsync(Admin, HttpMethod.Post, $"form/{form}/entry-state/undo", new { changeKey = change.Body!["changeKey"]!.GetValue<Guid>() });

        Assert.Multiple(async () =>
        {
            Assert.That(late.Outcome, Is.EqualTo("409 entry_undo_too_late"), late.ToString());
            Assert.That((await Entries.GetByIdAsync(entry.Id))!.State, Is.EqualTo(EntryState.Rejected));
        });
    }

    [Test]
    public async Task AnUndo_OfNoSuchChange_IsNotFound()
    {
        var (form, _) = await EntryAsync();

        var answer = await CallAsync(Admin, HttpMethod.Post, $"form/{form}/entry-state/undo", new { changeKey = Guid.NewGuid() });

        Assert.That(answer.Outcome, Is.EqualTo("404 entry_change_not_found"), answer.ToString());
    }
}
