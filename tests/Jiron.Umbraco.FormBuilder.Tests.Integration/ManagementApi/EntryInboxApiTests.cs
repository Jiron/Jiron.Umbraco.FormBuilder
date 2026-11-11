using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Retention;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>
/// The Inbox, Spam, the Form filter's forms and the unseen badge through the management API (ROADMAP 11.1, ADR-0209), and a
/// member's entries for the member editor's Form entries view (ROADMAP 14.3, ADR-0231).
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class EntryInboxApiTests : ManagementApiTestBase
{
    private const string Admin = "admin";

    private IEntryRepository Entries => GetRequiredService<IEntryRepository>();

    [Test]
    public async Task TheInbox_HoldsOnlyTheFormsEachUserMayView_AndNamesThemWithWhatTheUserMayDo()
    {
        var open = await PublishedAsync("Contact");
        var folder = await FolderAsync("People");
        var restricted = await PublishedAsync("Applications", folder);
        await RestrictAsync(folder, "hr", FormBuilderPermissions.ViewEntries);
        var openEntry = await EntryAsync(open, EntryState.Submitted);
        var restrictedEntry = await EntryAsync(restricted, EntryState.Submitted);

        var viewer = await CallAsync("viewer", HttpMethod.Post, "entries/inbox", new { count = true });
        var hr = await CallAsync("hr", HttpMethod.Post, "entries/inbox", new { count = true });
        var admin = await CallAsync(Admin, HttpMethod.Post, "entries/inbox", new { count = true });
        var handler = await CallAsync("entryHandler", HttpMethod.Post, "entries/inbox", new { });
        var nobody = await CallAsync("noAccess", HttpMethod.Post, "entries/inbox", new { count = true });
        var viewerForms = await CallAsync("viewer", HttpMethod.Get, "entries/forms");
        var hrForms = await CallAsync("hr", HttpMethod.Get, "entries/forms?search=appl");
        var viewerUnseen = await CallAsync("viewer", HttpMethod.Get, "entries/unseen");
        var adminUnseen = await CallAsync(Admin, HttpMethod.Get, "entries/unseen");
        var quiet = await PublishedAsync("Survey");
        var chosen = await CallAsync("viewer", HttpMethod.Post, "entries/inbox", new { filter = new { forms = new[] { open, quiet } } });

        Assert.Multiple(() =>
        {
            Assert.That(Keys(chosen, "forms"), Is.EqualTo(new[] { open, quiet }), "a chosen form without entries is named too, for its chip");
            Assert.That(viewer.Outcome, Is.EqualTo("200"), viewer.ToString());
            Assert.That(Keys(viewer, "items"), Is.EqualTo(new[] { openEntry.Key }), "no entry of the restricted folder's form");
            Assert.That(viewer.Body!["total"]!.GetValue<int>(), Is.EqualTo(1));
            Assert.That(viewer.Body!["items"]![0]!["form"]!.GetValue<Guid>(), Is.EqualTo(open));
            Assert.That(Keys(viewer, "forms"), Is.EqualTo(new[] { open }));
            Assert.That(Permissions(viewer), Is.EqualTo(new[] { "viewEntries" }));
            Assert.That(Keys(hr, "items"), Is.EqualTo(new[] { restrictedEntry.Key }), "the folder's own group sees its form only");
            Assert.That(admin.Body!["total"]!.GetValue<int>(), Is.EqualTo(2));
            Assert.That(Permissions(handler), Does.Contain("editEntries"), "the bulk bar offers what the form allows");
            Assert.That((nobody.Outcome, nobody.Body!["items"]!.AsArray().Count, nobody.Body!["total"]!.GetValue<int>()), Is.EqualTo(("200", 0, 0)), "a user who may view no form's entries");
            Assert.That(viewerForms.Body!.AsArray().Select(form => form!["key"]!.GetValue<Guid>()), Is.EqualTo(new[] { open }));
            Assert.That(hrForms.Body!.AsArray().Select(form => form!["name"]!.GetValue<string>()), Is.EqualTo(new[] { "Applications" }));
            Assert.That((viewerUnseen.Outcome, viewerUnseen.Body!["count"]!.GetValue<int>(), viewerUnseen.Body!["more"]!.GetValue<bool>()), Is.EqualTo(("200", 1, false)));
            Assert.That(adminUnseen.Body!["count"]!.GetValue<int>(), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task TheView_NamesTheFormsOfTheSets_AndLeavesOutWhatIsGone()
    {
        var open = await PublishedAsync("Contact");
        var other = await PublishedAsync("Newsletter");
        var trashed = await PublishedAsync("Old survey");
        var ofForm = Guid.NewGuid();
        var everywhere = Guid.NewGuid();
        var ofTrashed = Guid.NewGuid();
        var someForms = Guid.NewGuid();
        var trashedOnly = Guid.NewGuid();

        var saved = new[]
        {
            await CallAsync(Admin, HttpMethod.Put, $"entry/filter-set/{ofForm}", new { name = "Contact waiting", form = open, filter = new { forms = new[] { open }, states = new[] { "submitted" } } }),
            await CallAsync(Admin, HttpMethod.Put, $"entry/filter-set/{everywhere}", new { name = "Failed anywhere", filter = new { actions = "failed" } }),
            await CallAsync(Admin, HttpMethod.Put, $"entry/filter-set/{ofTrashed}", new { name = "Survey", form = trashed, filter = new { } }),
            await CallAsync(Admin, HttpMethod.Put, $"entry/filter-set/{someForms}", new { name = "Both", filter = new { forms = new[] { other, trashed } } }),
            await CallAsync(Admin, HttpMethod.Put, $"entry/filter-set/{trashedOnly}", new { name = "Old only", filter = new { forms = new[] { trashed } } }),
        };
        await TrashFormAsync(trashed);
        var view = await CallAsync(Admin, HttpMethod.Get, "entries/view");

        Assert.Multiple(() =>
        {
            Assert.That(saved.Select(answer => answer.Outcome), Is.All.EqualTo("200"), string.Join(", ", saved.Select(answer => answer.ToString())));
            Assert.That(view.Outcome, Is.EqualTo("200"), view.ToString());
            Assert.That(Keys(view, "filterSets"), Is.EquivalentTo(new[] { ofForm, everywhere, someForms }), "a set of a trashed form could not filter the Inbox, nor one of no form left");
            var both = view.Body!["filterSets"]!.AsArray().Single(set => set!["key"]!.GetValue<Guid>() == someForms)!;
            Assert.That(both["filter"]!["forms"]!.AsArray().Select(form => form!.GetValue<Guid>()), Is.EqualTo(new[] { other }), "the trashed form goes, the other stays");
            Assert.That(view.Body!["forms"]!.AsArray().Select(form => form!["name"]!.GetValue<string>()), Is.EquivalentTo(new[] { "Contact", "Newsletter" }));
        });
    }

    [Test]
    public async Task OpeningAnEntry_TakesItOffTheBadge_AndBehindTheUnseenInTheInbox()
    {
        var form = await PublishedAsync("Contact");
        var older = await EntryAsync(form, EntryState.Submitted);
        var newer = await EntryAsync(form, EntryState.Submitted);

        var opened = await CallAsync(Admin, HttpMethod.Get, $"form/{form}/entry/{older.Key}");
        var openedAgain = await CallAsync(Admin, HttpMethod.Get, $"form/{form}/entry/{newer.Key}");
        var unseen = await CallAsync(Admin, HttpMethod.Get, "entries/unseen");
        var third = await EntryAsync(form, EntryState.Submitted);
        var page = await CallAsync(Admin, HttpMethod.Post, "entries/inbox", new { });
        var byReceived = await CallAsync(Admin, HttpMethod.Post, "entries/inbox", new { filter = new { sort = new { descending = false } } });

        Assert.Multiple(() =>
        {
            Assert.That((opened.Outcome, openedAgain.Outcome), Is.EqualTo(("200", "200")), opened.ToString());
            Assert.That(unseen.Body!["count"]!.GetValue<int>(), Is.Zero, "both opened");
            Assert.That(Keys(page, "items"), Is.EqualTo(new[] { third.Key, newer.Key, older.Key }), "unseen first, then the newest");
            Assert.That(page.Body!["items"]![0]!["unseen"]!.GetValue<bool>(), Is.True);
            Assert.That(Keys(byReceived, "items"), Is.EqualTo(new[] { older.Key, newer.Key, third.Key }), "a sort the user picks replaces it");
        });
    }

    [Test]
    public async Task Spam_ListsOnlySpam_AndEmptySpamAsksFirst_ThenDeletesWhatTheUserMayDelete()
    {
        var open = await PublishedAsync("Contact");
        var folder = await FolderAsync("People");
        var restricted = await PublishedAsync("Applications", folder);
        await RestrictAsync(folder, "hr", FormBuilderPermissions.ViewEntries);
        var spam = await EntryAsync(open, EntryState.Spam);
        var kept = await EntryAsync(open, EntryState.Submitted);
        var restrictedSpam = await EntryAsync(restricted, EntryState.Spam);

        var list = await CallAsync(Admin, HttpMethod.Post, "entries/spam", new { count = true });
        var inbox = await CallAsync(Admin, HttpMethod.Post, "entries/inbox", new { });
        var viewerAsks = await CallAsync("viewer", HttpMethod.Post, "entries/spam/empty", new { confirm = true });
        var asked = await CallAsync("privacyOfficer", HttpMethod.Post, "entries/spam/empty", new { });
        var stillThere = await Entries.GetByIdAsync(spam.Id);
        var emptied = await CallAsync("privacyOfficer", HttpMethod.Post, "entries/spam/empty", new { confirm = true });

        Assert.Multiple(async () =>
        {
            Assert.That(list.Outcome, Is.EqualTo("200"), list.ToString());
            Assert.That(Keys(list, "items"), Is.EquivalentTo(new[] { spam.Key, restrictedSpam.Key }));
            Assert.That(Keys(inbox, "items"), Is.EqualTo(new[] { kept.Key }), "the Inbox holds no spam");
            Assert.That((viewerAsks.Outcome, viewerAsks.Body!["entries"]!.GetValue<int>(), viewerAsks.Body!["deleted"]!.GetValue<bool>()), Is.EqualTo(("200", 0, false)), "a user who may delete no entry deletes none");
            Assert.That((asked.Body!["entries"]!.GetValue<int>(), asked.Body!["deleted"]!.GetValue<bool>(), asked.Body!["more"]!.GetValue<bool>()), Is.EqualTo((1, false, false)), "what would go, for the dialog");
            Assert.That(stillThere, Is.Not.Null, "asking deletes nothing");
            Assert.That((emptied.Outcome, emptied.Body!["entries"]!.GetValue<int>(), emptied.Body!["deleted"]!.GetValue<bool>()), Is.EqualTo(("200", 1, true)), emptied.ToString());
            Assert.That(await Entries.GetByIdAsync(spam.Id), Is.Null);
            Assert.That(await Entries.GetByIdAsync(restrictedSpam.Id), Is.Not.Null, "the restricted folder's spam is not the privacy officer's to delete");
            Assert.That(await Entries.GetByIdAsync(kept.Id), Is.Not.Null);
        });
    }

    [Test]
    public async Task EmptySpam_LeavesTheEntriesALegalHoldKeeps_AndPagesOnPastThem_SayingHowManyStay()
    {
        var form = await PublishedAsync("Contact");
        var spam = new List<StoredEntry>();
        for (var i = 0; i < EntryDeletions.MaxEntries + 2; i++)
        {
            spam.Add(await EntryAsync(form, EntryState.Spam));
        }

        // The first and the last, so one of them lies on each page of a delete, whichever way the list is ordered.
        await CallAsync(Admin, HttpMethod.Get, "me");
        var adminKey = GetRequiredService<IUserService>().GetByEmail("api-admin@jfb.test")!.Key;
        var holds = GetRequiredService<EntryHolds>();
        foreach (var held in new[] { spam[0], spam[^1] })
        {
            Assert.That(await holds.HoldAsync(held.Key, DateTimeOffset.UtcNow.AddDays(30), "Court case", adminKey), Is.EqualTo(EntryHoldRefusal.None));
        }

        var asked = await CallAsync(Admin, HttpMethod.Post, "entries/spam/empty", new { });
        var emptied = await CallAsync(Admin, HttpMethod.Post, "entries/spam/empty", new { confirm = true });
        var again = await CallAsync(Admin, HttpMethod.Post, "entries/spam/empty", new { confirm = true });
        var left = await CallAsync(Admin, HttpMethod.Post, "entries/spam", new { count = true });

        Assert.Multiple(() =>
        {
            Assert.That((asked.Body!["entries"]!.GetValue<int>(), asked.Body!["held"]!.GetValue<int>(), asked.Body!["more"]!.GetValue<bool>()), Is.EqualTo((EntryDeletions.MaxEntries, 2, false)), asked.ToString());
            Assert.That((emptied.Body!["entries"]!.GetValue<int>(), emptied.Body!["held"]!.GetValue<int>(), emptied.Body!["deleted"]!.GetValue<bool>(), emptied.Body!["more"]!.GetValue<bool>()), Is.EqualTo((EntryDeletions.MaxEntries, 2, true, false)), "past the held entry on the first page to the page after it");
            Assert.That((again.Body!["entries"]!.GetValue<int>(), again.Body!["held"]!.GetValue<int>(), again.Body!["deleted"]!.GetValue<bool>()), Is.EqualTo((0, 2, false)), "the held ones stay");
            Assert.That(Keys(left, "items"), Is.EquivalentTo(new[] { spam[0].Key, spam[^1].Key }));
        });
    }

    [Test]
    public async Task WhatTheInboxCannotAnswer_IsRefusedByItsPath()
    {
        var one = await PublishedAsync("Contact");
        var two = await PublishedAsync("Newsletter");
        var folder = await FolderAsync("People");
        var hidden = await PublishedAsync("Applications", folder);
        await RestrictAsync(folder, "hr", FormBuilderPermissions.ViewEntries);
        await EntryAsync(one, EntryState.Submitted);

        var fields = await CallAsync(Admin, HttpMethod.Post, "entries/inbox", new { filter = new { forms = new[] { one, two }, fields = new[] { new { field = NameField.Key, test = "answered" } } } });
        var outOfReach = await CallAsync("viewer", HttpMethod.Post, "entries/inbox", new { filter = new { forms = new[] { one, hidden } } });
        var cursor = await CallAsync(Admin, HttpMethod.Post, "entries/spam", new { after = "nothing" });
        var manyForms = await CallAsync(Admin, HttpMethod.Post, "entries/inbox", new { filter = new { forms = Enumerable.Range(0, EntryLists.MaxForms + 1).Select(_ => Guid.NewGuid()).ToArray() } });
        var oneForm = await CallAsync(Admin, HttpMethod.Post, "entries/inbox", new { filter = new { forms = new[] { one }, fields = new[] { new { field = NameField.Key, test = "notAnswered" } } }, columns = new[] { NameField.Key } });

        Assert.Multiple(() =>
        {
            Assert.That((fields.Outcome, Path(fields)), Is.EqualTo(("400 field_not_filterable", "filter.fields[0].field")), "fields need exactly one form");
            Assert.That((outOfReach.Outcome, Path(outOfReach)), Is.EqualTo(("400 form_not_found", "filter.forms[1]")), outOfReach.ToString());
            Assert.That((cursor.Outcome, Path(cursor)), Is.EqualTo(("400 invalid_cursor", "after")), cursor.ToString());
            Assert.That((manyForms.Outcome, Path(manyForms)), Is.EqualTo(("400 too_many_forms", "filter.forms")), manyForms.ToString());
            Assert.That(oneForm.Outcome, Is.EqualTo("200"), oneForm.ToString());
            Assert.That(oneForm.Body!["items"]![0]!["cells"]![0]!["field"]!.GetValue<Guid>(), Is.EqualTo(NameField.Key), "one form's columns");
        });
    }

    private static string? Path(ApiAnswer answer) => answer.Body?["errors"]?[0]?["path"]?.GetValue<string>();

    private static Guid[] Keys(ApiAnswer answer, string list) =>
        [.. answer.Body![list]!.AsArray().Select(item => item!["key"]!.GetValue<Guid>())];

    private static string[] Permissions(ApiAnswer answer) =>
        [.. answer.Body!["forms"]![0]!["permissions"]!.AsArray().Select(name => name!.GetValue<string>())];

    private async Task<Guid> PublishedAsync(string name, Guid? folder = null)
    {
        var forms = GetRequiredService<IFormBuilderFormService>();
        var created = await forms.CreateAsync(new NewFormRequest(name, FolderKey: folder));
        var draft = (await forms.GetDraftAsync(created.Key))!;
        var saved = (await forms.SaveDraftAsync(created.Key, draft.Form.ConcurrencyVersion, Fillable(draft.Definition!), null, null))!;
        await forms.PublishAsync(created.Key, saved.Form.ConcurrencyVersion, null, null);
        return created.Key;
    }

    [Test]
    public async Task AMembersEntries_AreThoseOfTheFormsEachUserMayView_NewestFirst_WithoutDrafts()
    {
        var open = await PublishedAsync("Contact");
        var folder = await FolderAsync("People");
        var restricted = await PublishedAsync("Applications", folder);
        await RestrictAsync(folder, "hr", FormBuilderPermissions.ViewEntries);
        var member = Guid.NewGuid();
        var first = await EntryAsync(open, EntryState.Approved, member);
        var hidden = await EntryAsync(restricted, EntryState.Submitted, member);
        var second = await EntryAsync(open, EntryState.Rejected, member);
        _ = await EntryAsync(open, EntryState.Draft, member);
        _ = await EntryAsync(open, EntryState.Approved, Guid.NewGuid());
        _ = await EntryAsync(open, EntryState.Approved);

        var viewer = await CallAsync("viewer", HttpMethod.Get, $"members/{member}/entries");
        var admin = await CallAsync(Admin, HttpMethod.Get, $"members/{member}/entries");
        var nobody = await CallAsync("noAccess", HttpMethod.Get, $"members/{member}/entries");
        var stranger = await CallAsync(Admin, HttpMethod.Get, $"members/{Guid.NewGuid()}/entries");
        var badCursor = await CallAsync(Admin, HttpMethod.Get, $"members/{member}/entries?after=nonsense");

        Assert.Multiple(() =>
        {
            Assert.That(viewer.Outcome, Is.EqualTo("200"), viewer.ToString());
            Assert.That(Keys(viewer, "items"), Is.EqualTo(new[] { second.Key, first.Key }), "newest first, without the restricted form's entry, a draft or another's (ADR-0231)");
            Assert.That(viewer.Body!["total"]!.GetValue<int>(), Is.EqualTo(2), "the count leaves the forms out without a trace");
            Assert.That(Keys(viewer, "forms"), Is.EqualTo(new[] { open }));
            Assert.That(Keys(admin, "items"), Is.EqualTo(new[] { second.Key, hidden.Key, first.Key }));
            Assert.That((nobody.Outcome, nobody.Body!["items"]!.AsArray().Count), Is.EqualTo(("200", 0)), "a user who may view no form's entries");
            Assert.That((stranger.Outcome, stranger.Body!["items"]!.AsArray().Count), Is.EqualTo(("200", 0)), "a member without entries");
            Assert.That(badCursor.Outcome, Is.EqualTo("400 invalid_cursor"), badCursor.ToString());
        });
    }

    private async Task<StoredEntry> EntryAsync(Guid form, EntryState state, Guid? member = null)
    {
        var row = (await GetRequiredService<IFormRepository>().GetAsync(form))!;
        return (await Entries.CreateAsync(new NewEntry
        {
            Key = Guid.NewGuid(),
            FormId = row.Id,
            FormVersionId = row.PublishedVersionId!.Value,
            State = state,
            SubmissionId = Guid.NewGuid(),
            SubmissionHash = new string('a', 64),
            Culture = "en-US",
            MemberKey = member,
        }))!;
    }
}
