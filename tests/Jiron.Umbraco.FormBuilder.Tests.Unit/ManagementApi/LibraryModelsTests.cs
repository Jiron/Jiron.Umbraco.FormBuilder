using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Library;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.ManagementApi;

/// <summary>The management API's models of the library (ADR-0050): each says what its source holds, permissions by their stable names.</summary>
[TestFixture]
public sealed class LibraryModelsTests
{
    private static readonly DateTimeOffset Created = new(2026, 10, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Updated = Created.AddDays(1);
    private static readonly DateTimeOffset Trashed = Created.AddDays(2);

    [Test]
    public void AnItem_SaysWhatItIs_WhereItIs_AndWhatTheUserHoldsOnIt()
    {
        var parent = Guid.NewGuid();
        var folder = LibraryItemResponseModel.Of(new LibraryItem(LibraryItemKind.Folder, Guid.NewGuid(), "Events", null, parent, true, false, Trashed, true, FormBuilderPermissions.Design | FormBuilderPermissions.ViewEntries));
        var form = LibraryItemResponseModel.Of(new LibraryItem(LibraryItemKind.Form, Guid.NewGuid(), "Contact", "contact", null, false, true, null, false, FormBuilderPermissions.None));

        Assert.Multiple(() =>
        {
            Assert.That((folder.Kind, folder.Name, folder.Alias, folder.ParentKey), Is.EqualTo(("folder", "Events", (string?)null, (Guid?)parent)));
            Assert.That((folder.IsRestricted, folder.IsPublished, folder.IsTrashed, folder.HasChildren), Is.EqualTo((true, false, true, true)));
            Assert.That(folder.Permissions, Is.EqualTo(new[] { "design", "viewEntries" }), "in the table's order");
            Assert.That((form.Kind, form.Name, form.Alias, form.ParentKey), Is.EqualTo(("form", "Contact", "contact", (Guid?)null)));
            Assert.That((form.IsRestricted, form.IsPublished, form.IsTrashed, form.HasChildren), Is.EqualTo((false, true, false, false)));
            Assert.That(form.Permissions, Is.Empty);
            Assert.That(() => LibraryItemResponseModel.Of(null!), Throws.ArgumentNullException);
        });
    }

    [Test]
    public void AFolder_SaysWhereItIs_WhenItChanged_AndWhatTheUserHoldsOnIt()
    {
        var info = new FolderInfo(Guid.NewGuid(), "Events", Guid.NewGuid(), 3, true, Trashed, Created, Updated, Guid.NewGuid(), Guid.NewGuid());

        var model = FolderResponseModel.Of(info, FormBuilderPermissions.ManageActions | FormBuilderPermissions.Export);

        Assert.Multiple(() =>
        {
            Assert.That((model.Key, model.Name, model.ParentKey, model.IsRestricted), Is.EqualTo((info.Key, "Events", info.ParentKey, true)));
            Assert.That((model.TrashedUtc, model.CreatedUtc, model.UpdatedUtc), Is.EqualTo(((DateTimeOffset?)Trashed, Created, Updated)));
            Assert.That(model.Permissions, Is.EqualTo(new[] { "manageActions", "export" }));
        });
    }

    [Test]
    public void AChange_NamesTheFolderFirst_ThenWhatWentWithIt()
    {
        var top = new FolderInfo(Guid.NewGuid(), "Archive", null, 0, false, Trashed, Created, Updated, null, null);
        var below = new FolderSummary(Guid.NewGuid(), "Old");
        var notes = new FormSummary(Guid.NewGuid(), "notes", "Notes");

        var change = LibraryChangeResponseModel.Of(new FolderChange(top, [below], [notes]));
        var alone = LibraryChangeResponseModel.Of(new FolderChange(top, [], []));

        Assert.Multiple(() =>
        {
            Assert.That(change.Folders, Is.EqualTo(new[] { top.Key, below.Key }));
            Assert.That(change.Forms, Is.EqualTo(new[] { notes.Key }));
            Assert.That(alone.Folders, Is.EqualTo(new[] { top.Key }), "the folder even when nothing went with it");
            Assert.That(alone.Forms, Is.Empty);
        });
    }

    [Test]
    public void ADeletion_CountsWhatWent()
    {
        var deletion = new LibraryDeletion([new FolderSummary(Guid.NewGuid(), "Archive"), new FolderSummary(Guid.NewGuid(), "Old")], [new FormSummary(Guid.NewGuid(), "notes", "Notes")], 7, 3);

        var model = LibraryDeletionResponseModel.Of(deletion);

        Assert.That((model.Folders, model.Forms, model.Entries, model.Files), Is.EqualTo((2, 1, 7L, 3L)));
    }
}
