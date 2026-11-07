using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Queries;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Actions;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Entries;

/// <summary>
/// The Inbox and Spam (ROADMAP 11.3, ADR-0209): every visible form's entries, unseen first and paged across that seam,
/// the Form filter with one form's fields, Spam alone, the unseen count of the badge, and Empty spam.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class EntryInboxTests : ActionTestBase
{
    private static readonly FormField Amount = new()
    {
        Key = Guid.Parse("c4100000-0000-4000-8000-000000000101"),
        Type = "number",
        Alias = "amount",
        Label = new LocalizedText("Amount"),
    };

    private EntryLists Lists => GetRequiredService<EntryLists>();

    private Task<Guid> FormAsync() => PublishAsync(name: $"Requests {Guid.NewGuid():N}", fields: [NameField, Amount]);

    [Test]
    public async Task TheInbox_ListsEveryVisibleFormsEntries_UnseenFirst_AndPagesAcrossTheSeam()
    {
        var first = await FormAsync();
        var second = await FormAsync();
        var hidden = await FormAsync();
        var trashed = await FormAsync();
        var stored = new List<StoredEntry>();
        for (var i = 0; i < 119; i++)
        {
            stored.Add(await StoreAsync(i % 2 == 0 ? first : second, EntryState.Submitted));
        }

        stored.Add(await StoreAsync(first, EntryState.Approved));
        await StoreAsync(first, EntryState.Spam);
        await StoreAsync(second, EntryState.Submitted, change: entry => entry with { IsTest = true });
        await StoreAsync(hidden, EntryState.Submitted);
        await StoreAsync(trashed, EntryState.Submitted);
        await RestrictAsync(hidden);
        var trashedRow = (await FormRows.GetAsync(trashed))!;
        await GetRequiredService<IFormBuilderFormService>().TrashAsync(trashed, trashedRow.ConcurrencyVersion, null);

        // The sixty oldest are seen already, so they come last though they are not the newest: the first page is unseen
        // only, the second turns from the unseen to the seen, and the third goes on among the seen.
        foreach (var entry in stored.Take(60))
        {
            await Entries.MarkSeenAsync(entry.Id);
        }

        var visibility = new FormVisibility { Root = true };
        var pages = new List<EntryListPage> { await Lists.ListAcrossAsync(EntryListPlace.Inbox, visibility, new EntryListRequest(new EntryListFilter()) { Count = true }) };
        while (pages[^1].Next is { } next)
        {
            pages.Add(await Lists.ListAcrossAsync(EntryListPlace.Inbox, visibility, new EntryListRequest(new EntryListFilter()) { After = next }));
        }

        var page = pages[0];
        var unseen = stored.Skip(60).Reverse().Select(entry => entry.Key);
        var seen = stored.Take(60).Reverse().Select(entry => entry.Key);

        Assert.Multiple(() =>
        {
            Assert.That(page.Count, Is.EqualTo(new EntryCount(120, More: false)), "no Spam, test entry, hidden or trashed form's entry");
            Assert.That(pages.SelectMany(one => one.Rows).Select(row => row.Key), Is.EqualTo(unseen.Concat(seen)), "unseen newest first, then seen newest first, none twice");
            Assert.That(pages.Select(one => one.Rows.Count), Is.EqualTo(new[] { 50, 50, 20 }));
            Assert.That(page.Rows.Select(row => row.Form).Distinct(), Is.EquivalentTo(new[] { first, second }));
            Assert.That(page.Forms.Select(form => form.Key), Is.EqualTo(new[] { page.Rows[0].Form, page.Rows.First(row => row.Form != page.Rows[0].Form).Form }), "the forms in the order they first appear");
        });
    }

    [Test]
    public async Task ASortTheUserChose_ReplacesUnseenFirst()
    {
        var form = await FormAsync();
        var older = await StoreAsync(form, EntryState.Submitted);
        var newer = await StoreAsync(form, EntryState.Submitted);
        await Entries.MarkSeenAsync(newer.Id);

        var own = await KeysAsync(EntryListPlace.Inbox, new EntryListFilter());
        var newest = await KeysAsync(EntryListPlace.Inbox, new EntryListFilter { Sort = new EntryListSort() });

        Assert.Multiple(() =>
        {
            Assert.That(own, Is.EqualTo(new[] { older.Key, newer.Key }), "unseen first");
            Assert.That(newest, Is.EqualTo(new[] { newer.Key, older.Key }), "Received, newest first");
        });
    }

    [Test]
    public async Task TheFormFilter_OffersOneFormsFields_AndRefusesThemForSeveralOrAFormOutOfReach()
    {
        var form = await FormAsync();
        var other = await FormAsync();
        var hidden = await FormAsync();
        await RestrictAsync(hidden);
        var small = await SubmitAsync(form, """{"name":"Ada","amount":"5"}""");
        await SubmitAsync(form, """{"name":"Grace","amount":"50"}""");
        await SubmitAsync(other, """{"name":"Linus","amount":"5"}""");
        var byAmount = new EntryFieldCondition { Field = Amount.Key, Test = EntryFieldTest.Between, Max = 10 };

        var one = await Lists.ListAcrossAsync(EntryListPlace.Inbox, new FormVisibility { Root = true }, new EntryListRequest(new EntryListFilter { Forms = [form], Fields = [byAmount] }) { Columns = [Amount.Key] });
        var several = Assert.ThrowsAsync<FormBuilderValidationException>(() => Lists.ListAcrossAsync(EntryListPlace.Inbox, new FormVisibility { Root = true }, new EntryListRequest(new EntryListFilter { Forms = [form, other], Fields = [byAmount] })));
        var outOfReach = Assert.ThrowsAsync<FormBuilderValidationException>(() => Lists.ListAcrossAsync(EntryListPlace.Inbox, new FormVisibility { Root = true }, new EntryListRequest(new EntryListFilter { Forms = [other, hidden] })));

        Assert.Multiple(() =>
        {
            Assert.That(one.Rows.Select(row => row.Key), Is.EqualTo(new[] { small.Key }));
            Assert.That(one.Rows[0].Cells.Single().Texts, Is.EqualTo(new[] { "5" }));
            Assert.That(several!.Problems.Select(problem => (problem.Code, problem.Path)), Is.EqualTo(new[] { ("field_not_filterable", "filter.fields[0].field") }));
            Assert.That(outOfReach!.Problems.Select(problem => (problem.Code, problem.Path)), Is.EqualTo(new[] { ("form_not_found", "filter.forms[1]") }));
        });
    }

    [Test]
    public async Task Spam_HoldsEveryVisibleFormsSpamAlone_AndTheInboxNoSpam()
    {
        var form = await FormAsync();
        var other = await FormAsync();
        var spam = await StoreAsync(form, EntryState.Spam);
        var moreSpam = await StoreAsync(other, EntryState.Spam);
        await StoreAsync(form, EntryState.Submitted);

        var listed = await KeysAsync(EntryListPlace.Spam, new EntryListFilter());
        var askedInbox = Assert.ThrowsAsync<FormBuilderValidationException>(() => Lists.ListAcrossAsync(EntryListPlace.Inbox, FormVisibility.Everything, new EntryListRequest(new EntryListFilter { States = [EntryState.Spam] })));
        var askedSpam = Assert.ThrowsAsync<FormBuilderValidationException>(() => Lists.ListAcrossAsync(EntryListPlace.Spam, FormVisibility.Everything, new EntryListRequest(new EntryListFilter { States = [EntryState.Approved] })));
        var nothing = await Lists.ListAcrossAsync(EntryListPlace.Spam, FormVisibility.Nothing, new EntryListRequest(new EntryListFilter()) { Count = true });

        Assert.Multiple(() =>
        {
            Assert.That(listed, Is.EqualTo(new[] { moreSpam.Key, spam.Key }), "newest first");
            Assert.That(askedInbox!.Problems.Select(problem => problem.Path), Is.EqualTo(new[] { "filter.states[0]" }), "Spam has a list of its own");
            Assert.That(askedSpam!.Problems.Select(problem => problem.Path), Is.EqualTo(new[] { "filter.states[0]" }));
            Assert.That((nothing.Rows.Count, nothing.Count), Is.EqualTo((0, (EntryCount?)new EntryCount(0, More: false))), "a user who may view no form's entries");
        });
    }

    [Test]
    public async Task TheUnseenCount_CountsWhatTheTreeMarks_UpToItsMost()
    {
        var form = await FormAsync();
        var hidden = await FormAsync();
        for (var i = 0; i < 3; i++)
        {
            await StoreAsync(form, EntryState.Submitted);
        }

        var seen = await StoreAsync(form, EntryState.Approved);
        await Entries.MarkSeenAsync(seen.Id);
        await StoreAsync(form, EntryState.Spam);
        await StoreAsync(form, EntryState.Unverified);
        await StoreAsync(form, EntryState.Submitted, change: entry => entry with { IsTest = true });
        await StoreAsync(hidden, EntryState.Submitted);
        await RestrictAsync(hidden);

        Assert.Multiple(async () =>
        {
            Assert.That(await Lists.CountUnseenAsync(new FormVisibility { Root = true }, 10), Is.EqualTo(new EntryCount(3, More: false)), "arrived, unseen, no test, visible");
            Assert.That(await Lists.CountUnseenAsync(new FormVisibility { Root = true }, 2), Is.EqualTo(new EntryCount(2, More: true)));
            Assert.That(await Lists.CountUnseenAsync(FormVisibility.Everything, 10), Is.EqualTo(new EntryCount(4, More: false)));
            Assert.That(await Lists.CountUnseenAsync(FormVisibility.Nothing, 10), Is.EqualTo(new EntryCount(0, More: false)));
        });
    }

    [Test]
    public async Task EmptySpam_SaysWhatWouldGo_ThenDeletesTheVisibleFormsSpamOnly()
    {
        var form = await FormAsync();
        var other = await FormAsync();
        var hidden = await FormAsync();
        await StoreAsync(form, EntryState.Spam);
        await StoreAsync(other, EntryState.Spam);
        var kept = await StoreAsync(hidden, EntryState.Spam);
        var notSpam = await StoreAsync(form, EntryState.Submitted);
        await RestrictAsync(hidden);
        var bin = GetRequiredService<EntrySpamBin>();
        var visibility = new FormVisibility { Root = true };

        var plan = await bin.PlanAsync(visibility);
        var emptied = await bin.EmptyAsync(visibility, Guid.NewGuid());
        var again = await bin.PlanAsync(visibility);

        Assert.Multiple(async () =>
        {
            Assert.That(plan, Is.EqualTo(new EntrySpamEmptying(2, 0, More: false)));
            Assert.That(emptied, Is.EqualTo(new EntrySpamEmptying(2, 0, More: false)));
            Assert.That(again.Entries, Is.Zero);
            Assert.That(await Entries.GetAsync(kept.Key), Is.Not.Null, "a form out of reach keeps its Spam");
            Assert.That(await Entries.GetAsync(notSpam.Key), Is.Not.Null);
        });
    }

    private async Task<List<Guid>> KeysAsync(EntryListPlace place, EntryListFilter filter) =>
        [.. (await Lists.ListAcrossAsync(place, FormVisibility.Everything, new EntryListRequest(filter))).Rows.Select(row => row.Key)];

    // Gives the form a restriction of its own, so a visibility of the root's unrestricted forms leaves it out.
    private async Task RestrictAsync(Guid form)
    {
        var row = (await FormRows.GetAsync(form))!;
        await GetRequiredService<IFormBuilderAccessService>().RestrictFormAsync(form, row.ConcurrencyVersion, true, null);
    }

    /// <summary>Stores an entry of the form in a state, a minute after the one before.</summary>
    private async Task<StoredEntry> StoreAsync(Guid form, EntryState state, Func<NewEntry, NewEntry>? change = null)
    {
        var row = (await FormRows.GetAsync(form))!;
        Clock.Advance(TimeSpan.FromMinutes(1));
        var entry = new NewEntry
        {
            Key = Guid.NewGuid(),
            FormId = row.Id,
            FormVersionId = row.PublishedVersionId!.Value,
            State = state,
            SubmissionId = Guid.NewGuid(),
            SubmissionHash = new string('a', 64),
            Culture = "en-US",
        };
        return (await Entries.CreateAsync(change is null ? entry : change(entry)))!;
    }
}
