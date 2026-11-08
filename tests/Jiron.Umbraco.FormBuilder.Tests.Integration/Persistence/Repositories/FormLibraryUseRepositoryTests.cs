using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;

/// <summary>
/// What of the library a form's draft and live version use (ADR-0214): the form repository writes the rows with every
/// version it writes, a form taken offline forgets its live ones, a form deleted for good all of them, and an option list
/// stays while any row names it.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class FormLibraryUseRepositoryTests : RepositoryTestBase
{
    private static readonly Guid Topics = Guid.Parse("72000000-0000-4000-8000-000000000011");
    private static readonly Guid Countries = Guid.Parse("72000000-0000-4000-8000-000000000012");

    private ILibraryUseRepository Uses => Repo<ILibraryUseRepository>();

    [Test]
    public async Task TheRows_FollowTheDraftAndTheLiveVersion_ThroughSavesPublishesUnpublishingAndADelete()
    {
        var created = await Forms.CreateAsync(NewForm("contact") with { Draft = Using((Topics, 2)) });
        var form = created.Form;
        var users = await Uses.UsersAsync(LibraryUseKind.OptionList, Topics);

        var published = (await Forms.PublishAsync(form.Id, form.ConcurrencyVersion, Using((Topics, 1), (Countries, 1)), null, null, Editor))!.Form;
        var both = await Uses.UsersAsync(LibraryUseKind.OptionList, Topics);
        var counts = await Uses.FormCountsAsync(LibraryUseKind.OptionList);

        var saved = (await Forms.SaveDraftAsync(form.Id, published.ConcurrencyVersion, Using(), null, Editor))!.Form;
        var liveOnly = await Uses.UsersAsync(LibraryUseKind.OptionList, Topics);

        var offline = (await Forms.UnpublishAsync(form.Id, saved.ConcurrencyVersion, Editor))!;
        var none = await Uses.UsersAsync(LibraryUseKind.OptionList, Topics);

        Assert.Multiple(() =>
        {
            Assert.That(users, Is.EqualTo(new[] { new LibraryUser(form.Id, form.Key, form.Name, null, false, InDraft: true, InLive: false, Uses: 2) }));
            Assert.That(both, Is.EqualTo(new[] { users[0] with { InLive = true } }), "the draft's two fields, the live version's one");
            Assert.That(counts, Is.EqualTo(new Dictionary<Guid, int> { [Topics] = 1, [Countries] = 1 }), "forms, not versions");
            Assert.That(liveOnly.Single().InDraft, Is.False);
            Assert.That(liveOnly.Single().InLive, Is.True);
            Assert.That(liveOnly.Single().Uses, Is.EqualTo(1));
            Assert.That(offline.PublishedVersionId, Is.Null);
            Assert.That(none, Is.Empty, "offline, and a draft that uses nothing");
        });
    }

    [Test]
    public async Task AFormInTheRecycleBin_StillUsesItsLists_AndAFormDeletedForGood_TakesItsRows()
    {
        var form = (await Forms.CreateAsync(NewForm("contact") with { Draft = Using((Topics, 1)) })).Form;
        var trashed = (await Forms.TrashAsync(form.Id, form.ConcurrencyVersion, Editor))!;
        var inBin = (await Uses.UsersAsync(LibraryUseKind.OptionList, Topics)).Single();

        Assert.That(await Forms.DeleteAsync(trashed.Id, trashed.ConcurrencyVersion), Is.True);

        Assert.Multiple(async () =>
        {
            Assert.That(inBin.IsTrashed, Is.True);
            Assert.That(await Uses.UsersAsync(LibraryUseKind.OptionList, Topics), Is.Empty);
            Assert.That(await Uses.FormCountsAsync(LibraryUseKind.OptionList), Is.Empty);
        });
    }

    [Test]
    public async Task AnOptionList_StaysWhileAFormUsesIt_AndGoesOnceNoneDoes()
    {
        var list = await OptionLists.CreateAsync(new NewOptionList(Topics, "topics", "Topics", OptionListKind.Manual));
        var unused = await OptionLists.CreateAsync(new NewOptionList(Countries, "countries", "Countries", OptionListKind.Countries));
        var form = (await Forms.CreateAsync(NewForm("contact") with { Draft = Using((Topics, 1)) })).Form;

        var refused = Assert.ThrowsAsync<FormBuilderConflictException>(() => OptionLists.DeleteAsync(list.Id, list.ConcurrencyVersion));
        _ = await Forms.SaveDraftAsync(form.Id, form.ConcurrencyVersion, Using(), null, Editor);

        Assert.Multiple(async () =>
        {
            Assert.That(refused!.Conflict, Is.EqualTo(FormBuilderConflict.InUse));
            Assert.That(await OptionLists.GetAsync(Topics), Is.Not.Null);
            Assert.That(await OptionLists.DeleteAsync(unused.Id, unused.ConcurrencyVersion), Is.True, "a list no form uses goes at once");
            Assert.That(await OptionLists.DeleteAsync(list.Id, list.ConcurrencyVersion), Is.True, "and so does one the last form stopped using");
        });
    }

    // A draft or a publish whose definition uses these lists, as the form documents would say so.
    private static VersionDocument Using(params (Guid List, int Count)[] lists) =>
        Document() with { Uses = [.. lists.Select(list => new LibraryUse(LibraryUseKind.OptionList, list.List, list.Count))] };
}
