using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;

/// <summary>
/// The "used on" rows in <c>jfbReference</c> (ADR-0017, ADR-0131): a content item's rows replaced in one unit, each once
/// and none to a form the library lacks; the deletes by content and by form; the content of a form by its key; the
/// rebuild's walk over Umbraco's documents in id order, the recycle bin's included and neither blueprints nor media;
/// its write, which holds only while a document's version is the one it read; and the delete of rows whose content has
/// gone.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class ReferenceRepositoryTests : RepositoryTestBase
{
    private IReferenceRepository References => Repo<IReferenceRepository>();

    [Test]
    public async Task Replace_WritesEachReferenceOnce_SkipsAFormTheLibraryLacks_AndReplacesTheItemsRows()
    {
        var contact = await CreateFormAsync("contact");
        var newsletter = await CreateFormAsync("newsletter");
        var page = Guid.NewGuid();
        var other = Guid.NewGuid();
        await References.ReplaceContentAsync(other, [new ContentFormReference(contact.Key, "form", string.Empty)]);

        var written = await References.ReplaceContentAsync(page, [
            new ContentFormReference(contact.Key, "form", string.Empty),
            new ContentFormReference(contact.Key, "form", string.Empty),
            new ContentFormReference(contact.Key, "blocks", "de-DE"),
            new ContentFormReference(newsletter.Key, "blocks", "de-DE"),
            new ContentFormReference(Guid.NewGuid(), "form", string.Empty),
        ]);
        var both = await References.ContentOfAsync(contact.Key);
        var again = await References.ReplaceContentAsync(page, [new ContentFormReference(newsletter.Key, "form", string.Empty)]);

        Assert.Multiple(async () =>
        {
            Assert.That(written, Is.EqualTo(3), "each once, none to a missing form");
            Assert.That(both, Is.EqualTo(new[] { page, other }.Order()), "each content once, in key order");
            Assert.That(again, Is.EqualTo(1));
            Assert.That(await References.ContentOfAsync(contact.Key), Is.EqualTo(new[] { other }), "the page's old rows gone, the other's kept");
            Assert.That(await References.ContentOfAsync(newsletter.Key), Is.EqualTo(new[] { page }));
            Assert.That(Count(FormBuilderTables.Reference), Is.EqualTo(2));
            Assert.That(await References.ContentOfAsync(Guid.NewGuid()), Is.Empty, "a form that does not exist");
            Assert.That(await References.HoldsAsync(newsletter.Key, page), Is.True);
            Assert.That(await References.HoldsAsync(contact.Key, page), Is.False, "its row went");
            Assert.That(await References.HoldsAsync(contact.Key, other), Is.True);
            Assert.That(await References.HoldsAsync(Guid.NewGuid(), page), Is.False, "a form that does not exist");
        });
    }

    [TestCase("", "")]
    [TestCase(null, "")]
    [TestCase("form", null)]
    public async Task Replace_RefusesAnEmptyOrTooLongAliasOrCulture_AndWritesNothing(string? alias, string? culture)
    {
        var form = await CreateFormAsync();
        var page = Guid.NewGuid();
        await References.ReplaceContentAsync(page, [new ContentFormReference(form.Key, "kept", string.Empty)]);
        var reference = new ContentFormReference(form.Key, alias ?? new string('a', ReferenceRepository.MaxPropertyAliasLength + 1), culture ?? new string('c', ReferenceRepository.MaxCultureLength + 1));

        Assert.ThrowsAsync<ArgumentException>(() => References.ReplaceContentAsync(page, [reference]));
        Assert.That(Count(FormBuilderTables.Reference), Is.EqualTo(1), "the old row kept");
    }

    [Test]
    public async Task TheDeletes_TakeOnlyTheirContentsOrTheirFormsRows()
    {
        var first = await CreateFormAsync("first");
        var second = await CreateFormAsync("second");
        var page = Guid.NewGuid();
        var other = Guid.NewGuid();
        await References.ReplaceContentAsync(page, [new ContentFormReference(first.Key, "form", string.Empty), new ContentFormReference(second.Key, "form", string.Empty)]);
        await References.ReplaceContentAsync(other, [new ContentFormReference(first.Key, "form", string.Empty), new ContentFormReference(second.Key, "form", string.Empty)]);

        var byContent = await References.DeleteContentAsync(page);
        var byForm = await References.DeleteFormAsync(first.Id);

        Assert.Multiple(async () =>
        {
            Assert.That((byContent, byForm), Is.EqualTo((2, 1)));
            Assert.That(await References.ContentOfAsync(first.Key), Is.Empty);
            Assert.That(await References.ContentOfAsync(second.Key), Is.EqualTo(new[] { other }));
            Assert.That(await References.DeleteContentAsync(Guid.NewGuid()), Is.Zero);
        });
    }

    [Test]
    public async Task DocumentsAfter_WalksUmbracosDocumentsInIdOrder_TheRecycleBinsToo_NotBlueprintsOrMedia()
    {
        var type = await ContentTypeAsync();
        var contents = GetRequiredService<IContentService>();
        var pages = new List<IContent>();
        foreach (var name in new[] { "One", "Two", "Three", "Four" })
        {
            var page = contents.Create(name, Constants.System.Root, type.Alias);
            Assert.That(contents.Save(page).Success, Is.True);
            pages.Add(page);
        }

        Assert.That(contents.MoveToRecycleBin(pages[2]).Success, Is.True);
        var blueprint = contents.Create("Blueprint", Constants.System.Root, type.Alias);
        contents.SaveBlueprint(blueprint, null);
        var media = GetRequiredService<IMediaService>().CreateMediaWithIdentity("Pictures", Constants.System.Root, Constants.Conventions.MediaTypes.Folder);
        var ids = pages.Select(page => page.Id).ToList();

        var all = await References.DocumentsAfterAsync(0, PageRequest.MaxTake);
        var firstTwo = await References.DocumentsAfterAsync(0, 2);
        var rest = await References.DocumentsAfterAsync(firstTwo[^1], 2);
        var none = await References.DocumentsAfterAsync(ids[^1], 2);

        Assert.Multiple(() =>
        {
            Assert.That(all, Is.EqualTo(ids), "the trashed page too, never the blueprint or the media");
            Assert.That(all, Does.Not.Contain(blueprint.Id).And.Not.Contain(media.Id));
            Assert.That(firstTwo.Concat(rest), Is.EqualTo(ids), "a walk in parts");
            Assert.That(none, Is.Empty);
            Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => References.DocumentsAfterAsync(0, 0));
            Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => References.DocumentsAfterAsync(0, PageRequest.MaxTake + 1));
        });
    }

    [Test]
    public async Task VersionsOf_NamesEachDocumentsCurrentVersion_AndASaveOrAPublishChangesIt()
    {
        var type = await ContentTypeAsync();
        var contents = GetRequiredService<IContentService>();
        var saved = contents.Create("Saved", Constants.System.Root, type.Alias);
        var published = contents.Create("Published", Constants.System.Root, type.Alias);
        var deleted = contents.Create("Deleted", Constants.System.Root, type.Alias);
        Assert.That(contents.Save([saved, published, deleted]).Success, Is.True);
        int[] ids = [saved.Id, published.Id, deleted.Id];

        var before = await References.VersionsOfAsync(ids);
        var unchanged = await References.VersionsOfAsync(ids);
        saved.Name = "Saved again";
        Assert.That(contents.Save(saved).Success, Is.True);
        Assert.That(contents.Publish(published, ["*"]).Success, Is.True);
        Assert.That(contents.Delete(deleted).Success, Is.True);
        var after = await References.VersionsOfAsync(ids);

        Assert.Multiple(async () =>
        {
            Assert.That(before.Keys, Is.EquivalentTo(ids));
            Assert.That(before[saved.Id].Id, Is.Not.EqualTo(before[published.Id].Id), "each its own");
            Assert.That(unchanged, Is.EquivalentTo(before), "two reads of an unchanged document agree");
            Assert.That(after.Keys, Is.EquivalentTo(new[] { saved.Id, published.Id }), "a deleted document is left out");
            Assert.That(after[saved.Id].Id, Is.EqualTo(before[saved.Id].Id), "a save keeps the version");
            Assert.That(after[saved.Id].Date, Is.Not.EqualTo(before[saved.Id].Date), "and moves its date");
            Assert.That(after[published.Id].Id, Is.Not.EqualTo(before[published.Id].Id), "a publish starts a new one");
            Assert.That(after[saved.Id].Id, Is.EqualTo(saved.VersionId), "the copy's own version");
            Assert.That(await References.VersionsOfAsync([]), Is.Empty);
            Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => References.VersionsOfAsync(new int[SqlArgs.MaxListLength + 1]));
        });
    }

    [Test]
    public async Task ReplaceUnchanged_WritesWhileTheVersionIsTheOneRead_AndOtherwiseLeavesTheRowsAsTheyAre()
    {
        var form = await CreateFormAsync("contact");
        var other = await CreateFormAsync("newsletter");
        var type = await ContentTypeAsync();
        var contents = GetRequiredService<IContentService>();
        var page = contents.Create("Page", Constants.System.Root, type.Alias);
        Assert.That(contents.Save(page).Success, Is.True);
        await References.ReplaceContentAsync(page.Key, [new ContentFormReference(form.Key, "form", string.Empty)]);
        var read = (await References.VersionsOfAsync([page.Id]))[page.Id];

        var unchanged = await References.ReplaceUnchangedContentAsync(page.Id, read, page.Key, [new ContentFormReference(other.Key, "form", string.Empty), new ContentFormReference(Guid.NewGuid(), "form", string.Empty)]);
        var afterUnchanged = await References.ContentOfAsync(other.Key);
        var otherVersion = await References.ReplaceUnchangedContentAsync(page.Id, read with { Id = read.Id + 1 }, page.Key, []);
        var otherDate = await References.ReplaceUnchangedContentAsync(page.Id, read with { Date = read.Date.AddSeconds(1) }, page.Key, []);
        var afterRefused = await References.ContentOfAsync(other.Key);
        page.Name = "Saved";
        Assert.That(contents.Save(page).Success, Is.True);
        var saved = await References.ReplaceUnchangedContentAsync(page.Id, read, page.Key, []);
        var deletedKey = Guid.NewGuid();
        var deleted = await References.ReplaceUnchangedContentAsync(-999_999, read, deletedKey, [new ContentFormReference(form.Key, "form", string.Empty)]);

        Assert.Multiple(async () =>
        {
            Assert.That(unchanged, Is.EqualTo(1), "each row, none to a form the library lacks");
            Assert.That(afterUnchanged, Is.EqualTo(new[] { page.Key }));
            Assert.That((otherVersion, otherDate, saved, deleted), Is.EqualTo(((int?)null, (int?)null, (int?)null, (int?)null)), "another version, another date, a save, no document");
            Assert.That(afterRefused, Is.EqualTo(new[] { page.Key }), "the rows left as they were");
            Assert.That(await References.ContentOfAsync(form.Key), Is.Empty, "and none for a document that is gone");
            Assert.ThrowsAsync<ArgumentException>(() => References.ReplaceUnchangedContentAsync(page.Id, read, page.Key, [new ContentFormReference(form.Key, string.Empty, string.Empty)]));
        });
    }

    [Test]
    public async Task DeleteOrphans_RemovesTheRowsOfContentUmbracoNoLongerHolds()
    {
        var form = await CreateFormAsync();
        var type = await ContentTypeAsync();
        var contents = GetRequiredService<IContentService>();
        var page = contents.Create("Kept", Constants.System.Root, type.Alias);
        Assert.That(contents.Save(page).Success, Is.True);
        var media = GetRequiredService<IMediaService>().CreateMediaWithIdentity("Not a page", Constants.System.Root, Constants.Conventions.MediaTypes.Folder);
        await References.ReplaceContentAsync(page.Key, [new ContentFormReference(form.Key, "form", string.Empty)]);
        await References.ReplaceContentAsync(media.Key, [new ContentFormReference(form.Key, "form", string.Empty)]);
        await References.ReplaceContentAsync(Guid.NewGuid(), [new ContentFormReference(form.Key, "form", string.Empty)]);

        var removed = await References.DeleteOrphansAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(removed, Is.EqualTo(2), "the gone content's and the media item's");
            Assert.That(await References.ContentOfAsync(form.Key), Is.EqualTo(new[] { page.Key }));
            Assert.That(await References.DeleteOrphansAsync(), Is.Zero);
        });
    }

    // A document type without properties, allowed at the root.
    private async Task<IContentType> ContentTypeAsync()
    {
        var type = new ContentType(GetRequiredService<IShortStringHelper>(), Constants.System.Root) { Alias = "jfbPlainPage", Name = "Plain page", AllowedAsRoot = true };
        Assert.That((await GetRequiredService<IContentTypeService>().CreateAsync(type, Constants.Security.SuperUserKey)).Success, Is.True);
        return type;
    }
}
