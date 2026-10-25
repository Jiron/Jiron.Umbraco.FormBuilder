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
    public async Task ARename_ChangesOnlyTheName()
    {
        var folder = await Folders.CreateAsync(new NewFolder(Guid.NewGuid(), null, "Old name", 3, IsRestricted: true, UserKey: Editor));
        var other = Guid.NewGuid();
        Clock.Advance(TimeSpan.FromMinutes(2));

        var renamed = await Folders.RenameAsync(folder.Id, "New name", other);

        Assert.Multiple(async () =>
        {
            Assert.That(renamed, Is.EqualTo(folder with { Name = "New name", UpdatedUtc = Now.AddMinutes(2), UpdatedByUserKey = other }), "the order and the restriction stay");
            Assert.That(await Folders.RenameAsync(-1, "New name", other), Is.Null);
            Assert.That(() => Folders.RenameAsync(folder.Id, string.Empty, other), Throws.ArgumentException);
        });
    }

    [Test]
    public async Task ARestriction_ChangesOnlyTheFlag_OutsideTheTrash()
    {
        var folder = await Folders.CreateAsync(new NewFolder(Guid.NewGuid(), null, "HR", 3, UserKey: Editor));
        var trashed = await CreateFolderAsync("Old");
        await Folders.TrashAsync(trashed.Id, Editor);
        var other = Guid.NewGuid();
        Clock.Advance(TimeSpan.FromMinutes(2));

        var restricted = await Folders.RestrictAsync(folder.Id, true, other);
        var freed = await Folders.RestrictAsync(folder.Id, false, null);

        Assert.Multiple(async () =>
        {
            Assert.That(restricted, Is.EqualTo(folder with { IsRestricted = true, UpdatedUtc = Now.AddMinutes(2), UpdatedByUserKey = other }), "the name and the order stay");
            Assert.That(freed, Is.EqualTo(restricted! with { IsRestricted = false, UpdatedByUserKey = null }));
            Assert.That(await Folders.RestrictAsync(-1, true, other), Is.Null);
            Assert.That(
                () => Folders.RestrictAsync(trashed.Id, true, other),
                Throws.InstanceOf<FormBuilderConflictException>().With.Property(nameof(FormBuilderConflictException.Conflict)).EqualTo(FormBuilderConflict.Trashed));
            Assert.That((await Folders.GetByIdAsync(trashed.Id))!.IsRestricted, Is.False);
        });
    }

    [Test]
    public async Task Trashing_TakesEveryFolderBelow_WithOneNewKey_AndKeepsTheTree()
    {
        var parent = await CreateFolderAsync("Parent");
        var child = await CreateFolderAsync("Child", parent.Id);
        var grandchild = await CreateFolderAsync("Grandchild", child.Id);
        var earlier = await CreateFolderAsync("Earlier", parent.Id);
        var below = await CreateFolderAsync("Below earlier", earlier.Id);
        var other = await CreateFolderAsync("Other");
        var before = (await Folders.TrashAsync(earlier.Id, Editor))!;
        var other2 = Guid.NewGuid();
        Clock.Advance(TimeSpan.FromMinutes(1));

        var trashed = (await Folders.TrashAsync(parent.Id, other2))!;

        Assert.Multiple(async () =>
        {
            Assert.That(trashed.Folders.Select(f => f.Id), Is.EqualTo(new[] { parent.Id, child.Id, grandchild.Id }), "the folder first, then in the tree's order");
            Assert.That(trashed.TrashKey, Is.Not.Null.And.Not.EqualTo(before.TrashKey));
            Assert.That(trashed.Folders.Select(f => f.TrashKey), Has.All.EqualTo(trashed.TrashKey));
            Assert.That(trashed.Folders.Select(f => f.TrashedUtc), Has.All.EqualTo(Now.AddMinutes(1)));
            Assert.That(trashed.Folders.Select(f => f.UpdatedByUserKey), Has.All.EqualTo(other2));
            Assert.That(trashed.Folders.Select(f => f.ParentId), Is.EqualTo(new int?[] { null, parent.Id, child.Id }), "the trash keeps the tree");
            Assert.That(before.Folders.Select(f => f.Id), Is.EqualTo(new[] { earlier.Id, below.Id }));
            Assert.That(await Folders.GetByIdAsync(earlier.Id), Is.EqualTo(before.Folders[0]), "a folder trashed before keeps its own key");
            Assert.That(await Folders.GetByIdAsync(below.Id), Is.EqualTo(before.Folders[1]));
            Assert.That((await Folders.GetByIdAsync(other.Id))!.TrashedUtc, Is.Null);
            Assert.That(await Folders.TrashAsync(parent.Id, Editor), Is.Null, "in the trash already");
            Assert.That(await Folders.TrashAsync(child.Id, Editor), Is.Null);
            Assert.That(await Folders.TrashAsync(-1, Editor), Is.Null);
        });
    }

    [Test]
    public async Task Restoring_BringsBackWhatWentWithIt_IntoItsParent()
    {
        var top = await CreateFolderAsync("Top");
        var parent = await CreateFolderAsync("Parent", top.Id);
        var child = await CreateFolderAsync("Child", parent.Id);
        var grandchild = await CreateFolderAsync("Grandchild", child.Id);
        var earlier = await CreateFolderAsync("Earlier", parent.Id);
        var below = await CreateFolderAsync("Below earlier", earlier.Id);
        await Folders.TrashAsync(earlier.Id, Editor);
        var trashed = (await Folders.TrashAsync(parent.Id, Editor))!;
        var other = Guid.NewGuid();
        Clock.Advance(TimeSpan.FromMinutes(1));

        var restored = (await Folders.RestoreAsync(parent.Id, top.Id, other))!;

        Assert.Multiple(async () =>
        {
            Assert.That(restored.TrashKey, Is.EqualTo(trashed.TrashKey));
            Assert.That(restored.Folders.Select(f => f.Id), Is.EqualTo(new[] { parent.Id, child.Id, grandchild.Id }));
            Assert.That(restored.Folders.Select(f => (f.TrashedUtc, f.TrashKey)), Has.All.EqualTo(((DateTimeOffset?)null, (Guid?)null)));
            Assert.That(restored.Folders.Select(f => (f.UpdatedUtc, f.UpdatedByUserKey)), Has.All.EqualTo((Now.AddMinutes(1), (Guid?)other)));
            Assert.That(restored.Folders.Select(f => f.ParentId), Is.EqualTo(new int?[] { top.Id, parent.Id, child.Id }), "back into its parent, the tree as it was");
            Assert.That(restored.Folders, Is.EqualTo(new[] { await Folders.GetByIdAsync(parent.Id), await Folders.GetByIdAsync(child.Id), await Folders.GetByIdAsync(grandchild.Id) }), "as stored");
            Assert.That((await Folders.GetByIdAsync(earlier.Id))!.TrashedUtc, Is.Not.Null, "a folder trashed before stays in the trash");
            Assert.That((await Folders.GetByIdAsync(below.Id))!.TrashedUtc, Is.Not.Null, "with what went with it");
            Assert.That(await Folders.RestoreAsync(parent.Id, top.Id, Editor), Is.Null, "outside the trash");
            Assert.That(await Folders.RestoreAsync(-1, null, Editor), Is.Null);
        });
    }

    [Test]
    public async Task ARestore_IsRefused_WhenItWouldLandElsewhereThanTheCallerChecked()
    {
        var parent = await CreateFolderAsync("Parent");
        var child = await CreateFolderAsync("Child", parent.Id);
        await Folders.TrashAsync(child.Id, Editor);

        AssertConflict(() => Folders.RestoreAsync(child.Id, null, Editor), FormBuilderConflict.LandingChanged);
        await Folders.TrashAsync(parent.Id, Editor);
        AssertConflict(() => Folders.RestoreAsync(child.Id, parent.Id, Editor), FormBuilderConflict.LandingChanged);

        Assert.That((await Folders.GetByIdAsync(child.Id))!.TrashedUtc, Is.Not.Null, "still in the trash");
    }

    [Test]
    public async Task Restoring_KeepsTheFoldersBelowInTheirPlace_AlsoOneMadeBeforeItsParent()
    {
        // The older folder has the lower id, so a database that changes the rows in id order and lets a statement see
        // its own changes meets it while its parent is still in the trash; SQL Server's statements never see their
        // own changes.
        var top = await CreateFolderAsync("Top");
        var older = await CreateFolderAsync("Older");
        var parent = await CreateFolderAsync("Parent", top.Id);
        await Folders.MoveAsync(older.Id, parent.Id, Editor);
        await Folders.TrashAsync(top.Id, Editor);

        var restored = (await Folders.RestoreAsync(top.Id, null, Editor))!;

        Assert.Multiple(() =>
        {
            Assert.That(older.Id, Is.LessThan(parent.Id));
            Assert.That(restored.Folders.Select(f => (f.Id, f.ParentId)), Is.EquivalentTo(new[] { (top.Id, (int?)null), (parent.Id, top.Id), (older.Id, parent.Id) }), "only the folder itself can go to the root");
        });
    }

    [Test]
    public async Task AFolderRestoredOnItsOwn_GoesToTheRoot_WithWhatWentWithItBelowIt()
    {
        var parent = await CreateFolderAsync("Parent");
        var child = await CreateFolderAsync("Child", parent.Id);
        var grandchild = await CreateFolderAsync("Grandchild", child.Id);
        var sibling = await CreateFolderAsync("Sibling", parent.Id);
        var trashed = (await Folders.TrashAsync(parent.Id, Editor))!;

        var restored = (await Folders.RestoreAsync(child.Id, null, Editor))!;
        var later = (await Folders.RestoreAsync(parent.Id, null, Editor))!;

        Assert.Multiple(() =>
        {
            Assert.That(restored.TrashKey, Is.EqualTo(trashed.TrashKey));
            Assert.That(restored.Folders.Select(f => (f.Id, f.ParentId)), Is.EqualTo(new[] { (child.Id, (int?)null), (grandchild.Id, child.Id) }), "the root, as its parent is in the trash");
            Assert.That(later.Folders.Select(f => (f.Id, f.ParentId)), Is.EqualTo(new[] { (parent.Id, (int?)null), (sibling.Id, parent.Id) }), "the rest comes back with the parent");
        });
    }

    [Test]
    public async Task NothingIsCreatedOrMovedBelowAFolderInTheTrash_AndAFolderInTheTrashDoesNotMove()
    {
        var trashed = await CreateFolderAsync("Trashed");
        var inside = await CreateFolderAsync("Inside", trashed.Id);
        var live = await CreateFolderAsync("Live");
        await Folders.TrashAsync(trashed.Id, Editor);

        Assert.Multiple(async () =>
        {
            AssertRefused(() => Folders.CreateAsync(new NewFolder(Guid.NewGuid(), trashed.Id, "New")), FormBuilderConflict.MissingFolder);
            AssertRefused(() => Folders.CreateAsync(new NewFolder(Guid.NewGuid(), inside.Id, "New")), FormBuilderConflict.MissingFolder);
            AssertRefused(() => Folders.MoveAsync(live.Id, trashed.Id, Editor), FormBuilderConflict.MissingFolder);
            AssertRefused(() => Folders.MoveAsync(trashed.Id, live.Id, Editor), FormBuilderConflict.Trashed);
            AssertRefused(() => Folders.MoveAsync(inside.Id, null, Editor), FormBuilderConflict.Trashed);
            Assert.That(Count(FormBuilderTables.Folder), Is.EqualTo(3), "nothing was written");
            Assert.That((await Folders.GetByIdAsync(live.Id))!.ParentId, Is.Null);
            Assert.That((await Folders.GetByIdAsync(inside.Id))!.ParentId, Is.EqualTo(trashed.Id));
        });
    }

    [Test]
    public async Task OnlyAnEmptyFolderInTheTrash_IsDeleted()
    {
        var withFolder = await CreateFolderAsync("With a folder");
        var inside = await CreateFolderAsync("Inside", withFolder.Id);
        var withForm = await CreateFolderAsync("With a form");
        await CreateFormAsync(folderId: withForm.Id);
        var empty = await CreateFolderAsync("Empty");
        var live = await CreateFolderAsync("Live");
        foreach (var folder in new[] { withFolder, withForm, empty })
        {
            await Folders.TrashAsync(folder.Id, Editor);
        }

        Assert.Multiple(async () =>
        {
            AssertConflict(() => Folders.DeleteAsync(live.Id), FormBuilderConflict.NotTrashed);
            AssertConflict(() => Folders.DeleteAsync(withFolder.Id), FormBuilderConflict.FolderNotEmpty);
            AssertConflict(() => Folders.DeleteAsync(withForm.Id), FormBuilderConflict.FolderNotEmpty);
            Assert.That(await Folders.DeleteAsync(empty.Id), Is.True);
            Assert.That(await Folders.DeleteAsync(empty.Id), Is.False, "deleted already");
            Assert.That(await Folders.DeleteAsync(inside.Id), Is.True);
            Assert.That(await Folders.DeleteAsync(withFolder.Id), Is.True, "empty now");
            Assert.That(await Folders.GetByIdAsync(withForm.Id), Is.Not.Null);
            Assert.That(await Folders.GetByIdAsync(live.Id), Is.Not.Null);
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
