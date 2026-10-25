using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
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
    public void AForm_SaysWhereItIs_ItsState_AndWhatTheUserHoldsOnIt()
    {
        var info = Form();

        var model = FormResponseModel.Of(info, FormBuilderPermissions.Design | FormBuilderPermissions.Export);

        Assert.Multiple(() =>
        {
            Assert.That((model.Key, model.Alias, model.Name, model.FolderKey), Is.EqualTo((info.Key, "contact", "Contact", info.FolderKey)));
            Assert.That((model.ConcurrencyVersion, model.IsPublished, model.PublicRevision, model.IsRestricted), Is.EqualTo((7, true, 3, true)));
            Assert.That((model.TrashedUtc, model.CreatedUtc, model.UpdatedUtc), Is.EqualTo(((DateTimeOffset?)Trashed, Created, Updated)));
            Assert.That(model.Permissions, Is.EqualTo(new[] { "design", "export" }));
        });
    }

    [Test]
    public void ADraft_CarriesTheCanonicalDocument_ItsProblems_AndItsVersions()
    {
        var info = Form();
        var definition = new FormDefinition { Key = info.Key, Pages = [new FormPage { Key = Guid.NewGuid() }] };
        var form = FormResponseModel.Of(info, FormBuilderPermissions.Design);

        var read = FormDraftResponseModel.Of(new FormDraft(info, definition, [new DefinitionProblem("limit_exceeded", "$.pages", "Too many.")], 4, 2, true), form);
        var unreadable = FormDraftResponseModel.Of(new FormDraft(info, null, [], 1, null, false), form);

        Assert.Multiple(() =>
        {
            Assert.That(read.Form, Is.SameAs(form));
            Assert.That(read.Definition?.GetRawText(), Is.EqualTo(FormDefinitionSerializer.WriteString(definition)), "the canonical document");
            Assert.That(read.Problems, Is.EqualTo(new[] { new DefinitionProblemResponseModel("limit_exceeded", "$.pages", "Too many.") }));
            Assert.That((read.DraftVersion, read.PublishedVersion, read.HasUnpublishedChanges), Is.EqualTo((4, (int?)2, true)));
            Assert.That((unreadable.Definition, unreadable.Problems.Count, unreadable.PublishedVersion, unreadable.HasUnpublishedChanges), Is.EqualTo(((JsonElement?)null, 0, (int?)null, false)));
        });
    }

    [Test]
    public void ADeletion_CountsWhatWent()
    {
        var deletion = new LibraryDeletion([new FolderSummary(Guid.NewGuid(), "Archive"), new FolderSummary(Guid.NewGuid(), "Old")], [new FormSummary(Guid.NewGuid(), "notes", "Notes")], 7, 3);

        var model = LibraryDeletionResponseModel.Of(deletion);

        Assert.Multiple(() =>
        {
            Assert.That((model.Folders, model.Forms, model.Entries, model.Files), Is.EqualTo((2, 1, 7L, 3L)));
            var form = LibraryDeletionResponseModel.Of(new FormDeletion(Guid.NewGuid(), 5, 2));
            Assert.That((form.Folders, form.Forms, form.Entries, form.Files), Is.EqualTo((0, 1, 5L, 2L)), "a form on its own");
        });
    }

    private static FormInfo Form() => new(Guid.NewGuid(), "contact", "Contact", Guid.NewGuid(), 7, true, 3, true, Trashed, Created, Updated, Guid.NewGuid(), Guid.NewGuid());
}
