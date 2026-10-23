using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;

/// <summary>
/// The folders of the form library (section 4.5, ADR-0036): the tree stays a tree, at most <see cref="FolderRepository.MaxDepth"/>
/// levels deep, and every refusal is a condition of the write itself.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class FolderRepositoryTests : RepositoryTestBase
{
    [Test]
    public async Task AFolder_IsCreated_AtTheRootAndInAnother_AndFoundByKeyAndId()
    {
        var root = await Folders.CreateAsync(new NewFolder(Guid.NewGuid(), null, "Campaigns", 2, IsRestricted: true, UserKey: Editor));
        var child = await CreateFolderAsync("Autumn", root.Id);

        Assert.Multiple(async () =>
        {
            Assert.That(root.ParentId, Is.Null);
            Assert.That(root.Name, Is.EqualTo("Campaigns"));
            Assert.That(root.SortOrder, Is.EqualTo(2));
            Assert.That(root.IsRestricted, Is.True);
            Assert.That(root.TrashedUtc, Is.Null);
            Assert.That(root.CreatedUtc, Is.EqualTo(Now));
            Assert.That(root.UpdatedUtc, Is.EqualTo(Now));
            Assert.That(root.CreatedByUserKey, Is.EqualTo(Editor));
            Assert.That(root.UpdatedByUserKey, Is.EqualTo(Editor));
            Assert.That(child.ParentId, Is.EqualTo(root.Id));
            Assert.That(await Folders.GetAsync(child.Key), Is.EqualTo(child));
            Assert.That(await Folders.GetByIdAsync(root.Id), Is.EqualTo(root));
            Assert.That(await Folders.GetAsync(Guid.NewGuid()), Is.Null);
            Assert.That(await Folders.GetByIdAsync(-1), Is.Null);
        });
    }

    [Test]
    public async Task Creating_IsRefused_ForATakenKey_AMissingParent_AndTooDeepATree()
    {
        var folder = await CreateFolderAsync();
        var deepest = await ChainAsync(FolderRepository.MaxDepth);

        Assert.Multiple(() =>
        {
            AssertConflict(() => Folders.CreateAsync(new NewFolder(folder.Key, null, "Again")), FormBuilderConflict.DuplicateKey);
            AssertConflict(() => Folders.CreateAsync(new NewFolder(Guid.NewGuid(), -1, "Orphan")), FormBuilderConflict.MissingFolder);
            AssertConflict(() => Folders.CreateAsync(new NewFolder(Guid.NewGuid(), deepest.Id, "One too many")), FormBuilderConflict.FolderTooDeep);
            Assert.That(Count(FormBuilderTables.Folder), Is.EqualTo(1 + FolderRepository.MaxDepth), "nothing was written");
        });
    }

    [Test]
    public async Task Children_AreListedInSortOrderThenName_InTheTrashOrOutOfIt()
    {
        var parent = await CreateFolderAsync("Parent");
        var b = await CreateFolderAsync("B", parent.Id, sortOrder: 1);
        var a = await CreateFolderAsync("a", parent.Id, sortOrder: 1);
        var first = await CreateFolderAsync("Z", parent.Id, sortOrder: 0);
        var trashed = await CreateFolderAsync("Old", parent.Id);
        await Folders.TrashAsync(trashed.Id, Editor);

        Assert.Multiple(async () =>
        {
            Assert.That((await Folders.ListChildrenAsync(parent.Id, trashed: false)).Select(f => f.Id), Is.EqualTo(new[] { first.Id, a.Id, b.Id }), "names compare without regard to case");
            Assert.That((await Folders.ListChildrenAsync(parent.Id, trashed: true)).Select(f => f.Id), Is.EqualTo(new[] { trashed.Id }));
            Assert.That((await Folders.ListChildrenAsync(null, trashed: false)).Select(f => f.Id), Is.EqualTo(new[] { parent.Id }));
            Assert.That((await Folders.ListAllAsync()).Select(f => f.Id), Is.EquivalentTo(new[] { parent.Id, a.Id, b.Id, first.Id, trashed.Id }));
        });
    }

    [Test]
    public async Task AFolder_IsRenamedReorderedAndRestricted()
    {
        var folder = await CreateFolderAsync("Draft name");
        var other = Guid.NewGuid();
        Clock.Advance(TimeSpan.FromMinutes(5));

        var updated = await Folders.UpdateAsync(folder.Id, "Final name", 7, true, other);

        Assert.Multiple(async () =>
        {
            Assert.That(updated, Is.EqualTo(folder with { Name = "Final name", SortOrder = 7, IsRestricted = true, UpdatedUtc = Now.AddMinutes(5), UpdatedByUserKey = other }));
            Assert.That(await Folders.UpdateAsync(-1, "Nothing", 0, false, null), Is.Null);
        });
    }

    [Test]
    public async Task AFolder_MovesIntoAnotherFolder_AndBackToTheRoot_WithEverythingInIt()
    {
        var source = await CreateFolderAsync("Source");
        var target = await CreateFolderAsync("Target");
        var inside = await CreateFolderAsync("Inside", source.Id);

        var moved = await Folders.MoveAsync(source.Id, target.Id, Editor);
        var back = await Folders.MoveAsync(source.Id, null, Editor);

        Assert.Multiple(async () =>
        {
            Assert.That(moved?.ParentId, Is.EqualTo(target.Id));
            Assert.That(back?.ParentId, Is.Null);
            Assert.That((await Folders.GetByIdAsync(inside.Id))?.ParentId, Is.EqualTo(source.Id), "its contents come along");
            Assert.That(await Folders.MoveAsync(-1, null, Editor), Is.Null);
        });
    }

    [Test]
    public async Task Moving_IsRefused_IntoItself_BelowItself_IntoAMissingFolder_AndTooDeep()
    {
        var top = await CreateFolderAsync("Top");
        var middle = await CreateFolderAsync("Middle", top.Id);
        var bottom = await CreateFolderAsync("Bottom", middle.Id);

        // Deep is on level MaxDepth - 1: a folder with one level below it fits under deep's parent, not under deep.
        var deep = await ChainAsync(FolderRepository.MaxDepth - 1);
        var shallow = await Folders.GetByIdAsync(deep.ParentId!.Value);
        var pair = await CreateFolderAsync("Pair");
        await CreateFolderAsync("Below the pair", pair.Id);

        Assert.Multiple(async () =>
        {
            AssertConflict(() => Folders.MoveAsync(top.Id, top.Id, Editor), FormBuilderConflict.FolderIntoItself);
            AssertConflict(() => Folders.MoveAsync(top.Id, bottom.Id, Editor), FormBuilderConflict.FolderIntoItself);
            AssertConflict(() => Folders.MoveAsync(middle.Id, -1, Editor), FormBuilderConflict.MissingFolder);
            AssertConflict(() => Folders.MoveAsync(pair.Id, deep.Id, Editor), FormBuilderConflict.FolderTooDeep);
            Assert.That((await Folders.MoveAsync(pair.Id, shallow!.Id, Editor))?.ParentId, Is.EqualTo(shallow.Id), "exactly MaxDepth levels fit");
            Assert.That((await Folders.GetByIdAsync(top.Id))?.ParentId, Is.Null, "the refused moves changed nothing");
            Assert.That((await Folders.GetByIdAsync(middle.Id))?.ParentId, Is.EqualTo(top.Id));
        });
    }

    [Test]
    public async Task Trashing_KeepsTheFolderInPlace_AndRestoring_ReturnsItToItsParentOrTheRoot()
    {
        var parent = await CreateFolderAsync("Parent");
        var child = await CreateFolderAsync("Child", parent.Id);
        Clock.Advance(TimeSpan.FromMinutes(1));

        var trashedChild = await Folders.TrashAsync(child.Id, Editor);
        Clock.Advance(TimeSpan.FromMinutes(1));
        var again = await Folders.TrashAsync(child.Id, Guid.NewGuid());
        var restored = await Folders.RestoreAsync(child.Id, Editor);
        await Folders.TrashAsync(child.Id, Editor);
        await Folders.TrashAsync(parent.Id, Editor);
        var toRoot = await Folders.RestoreAsync(child.Id, Editor);
        var notTrashed = await Folders.RestoreAsync(child.Id, Editor);

        Assert.Multiple(async () =>
        {
            Assert.That(trashedChild?.TrashedUtc, Is.EqualTo(Now.AddMinutes(1)));
            Assert.That(trashedChild?.ParentId, Is.EqualTo(parent.Id), "trash keeps the tree");
            Assert.That(again, Is.EqualTo(trashedChild), "a trashed folder stays as it is");
            Assert.That(restored?.TrashedUtc, Is.Null);
            Assert.That(restored?.ParentId, Is.EqualTo(parent.Id), "back into its parent");
            Assert.That(toRoot?.ParentId, Is.Null, "the root, as its parent is in the trash");
            Assert.That(notTrashed, Is.EqualTo(toRoot), "a folder outside the trash stays as it is");
            Assert.That(await Folders.TrashAsync(-1, Editor), Is.Null);
            Assert.That(await Folders.RestoreAsync(-1, Editor), Is.Null);
        });
    }

    [Test]
    public async Task OnlyAnEmptyFolder_IsDeleted()
    {
        var withFolder = await CreateFolderAsync("With a folder");
        var inside = await CreateFolderAsync("Inside", withFolder.Id);
        var withForm = await CreateFolderAsync("With a form");
        await CreateFormAsync(folderId: withForm.Id);
        var empty = await CreateFolderAsync("Empty");

        Assert.Multiple(async () =>
        {
            AssertConflict(() => Folders.DeleteAsync(withFolder.Id), FormBuilderConflict.FolderNotEmpty);
            AssertConflict(() => Folders.DeleteAsync(withForm.Id), FormBuilderConflict.FolderNotEmpty);
            Assert.That(await Folders.DeleteAsync(empty.Id), Is.True);
            Assert.That(await Folders.DeleteAsync(empty.Id), Is.False, "deleted already");
            Assert.That(await Folders.DeleteAsync(inside.Id), Is.True);
            Assert.That(await Folders.DeleteAsync(withFolder.Id), Is.True, "empty now");
            Assert.That(await Folders.GetByIdAsync(withForm.Id), Is.Not.Null);
        });
    }

    private static void AssertConflict<T>(Func<Task<T>> change, FormBuilderConflict conflict) =>
        Assert.That(
            async () => await change(),
            Throws.InstanceOf<FormBuilderConflictException>().With.Property(nameof(FormBuilderConflictException.Conflict)).EqualTo(conflict),
            conflict.ToString());

    /// <summary>A chain of folders <paramref name="levels"/> deep; returns the deepest.</summary>
    private async Task<StoredFolder> ChainAsync(int levels)
    {
        StoredFolder? folder = null;
        for (var level = 1; level <= levels; level++)
        {
            folder = await CreateFolderAsync($"Level {level}", folder?.Id);
        }

        return folder!;
    }
}
