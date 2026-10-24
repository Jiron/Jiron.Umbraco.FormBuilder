using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;

/// <summary>
/// The rows of the forms (section 4.5, ADR-0002, ADR-0036): aliases unique and found in any letter case (ADR-0033), and every
/// change checked against the concurrency version it was based on.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class FormRepositoryTests : RepositoryTestBase
{
    [Test]
    public async Task AForm_IsCreated_WithItsFirstDraft()
    {
        var folder = await CreateFolderAsync();
        var form = NewForm("contactUs", folder.Id) with { IsRestricted = true };

        var saved = await Forms.CreateAsync(form);

        Assert.Multiple(async () =>
        {
            Assert.That(saved.Form.Key, Is.EqualTo(form.Key));
            Assert.That(saved.Form.Alias, Is.EqualTo("contactUs"));
            Assert.That(saved.Form.Name, Is.EqualTo("Contact"));
            Assert.That(saved.Form.FolderId, Is.EqualTo(folder.Id));
            Assert.That(saved.Form.IsRestricted, Is.True);
            Assert.That(saved.Form.DraftVersionId, Is.EqualTo(saved.Version.Info.Id));
            Assert.That(saved.Form.PublishedVersionId, Is.Null);
            Assert.That(saved.Form.PublicRevision, Is.Zero);
            Assert.That(saved.Form.ConcurrencyVersion, Is.EqualTo(1));
            Assert.That(saved.Form.LastEntryNumber, Is.Zero);
            Assert.That(saved.Form.ThemeAlias, Is.Null);
            Assert.That(saved.Form.TrashedUtc, Is.Null);
            Assert.That(saved.Form.CreatedUtc, Is.EqualTo(Now));
            Assert.That(saved.Form.CreatedByUserKey, Is.EqualTo(Editor));
            Assert.That(saved.Version.Info.Number, Is.EqualTo(1));
            Assert.That(saved.Version.Info.Kind, Is.EqualTo(FormVersionKind.Draft));
            Assert.That(saved.Version.Info.Comment, Is.EqualTo("Created"));
            Assert.That(saved.Version.Info.PublicRevision, Is.Null);
            Assert.That(saved.Version.Info.CreatedByUserKey, Is.EqualTo(Editor));
            Assert.That(saved.Version.Definition, Is.EqualTo(form.Draft.Definition));
            Assert.That(saved.Version.Info.DefinitionHash, Is.EqualTo(form.Draft.DefinitionHash));
            Assert.That(await Forms.GetAsync(form.Key), Is.EqualTo(saved.Form));
            Assert.That(await Forms.GetByIdAsync(saved.Form.Id), Is.EqualTo(saved.Form));
            Assert.That(await Forms.GetVersionByIdAsync(saved.Version.Info.Id), Is.EqualTo(saved.Version));
        });
    }

    [TestCase("contactUs")]
    [TestCase("CONTACTUS")]
    [TestCase("contactus")]
    [TestCase("ContactUs")]
    public async Task AForm_IsFoundByItsAlias_InAnyLetterCase(string lookup)
    {
        var form = await CreateFormAsync("contactUs");
        await CreateFormAsync("contactUsToo");

        var found = await Forms.GetByAliasAsync(lookup);

        Assert.Multiple(async () =>
        {
            Assert.That(found?.Id, Is.EqualTo(form.Id));
            Assert.That(found?.Alias, Is.EqualTo("contactUs"), "stored as it was written");
            Assert.That(await Forms.GetByAliasAsync("contact"), Is.Null, "no partial match");
        });
    }

    [Test]
    public async Task Creating_IsRefused_ForAnAliasTakenInAnyCase_ATakenKey_AndAMissingFolder()
    {
        var form = await CreateFormAsync("contactUs");
        await Forms.TrashAsync(form.Id, form.ConcurrencyVersion, Editor);

        Assert.Multiple(() =>
        {
            AssertConflict(() => Forms.CreateAsync(NewForm("CONTACTUS")), FormBuilderConflict.DuplicateAlias, "trashed forms keep their alias");
            AssertConflict(() => Forms.CreateAsync(NewForm() with { Key = form.Key }), FormBuilderConflict.DuplicateKey);
            AssertConflict(() => Forms.CreateAsync(NewForm(folderId: -1)), FormBuilderConflict.MissingFolder);
            Assert.That(Count(FormBuilderTables.Form), Is.EqualTo(1), "nothing was written");
            Assert.That(Count(FormBuilderTables.FormVersion), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Lists_FilterByFolderTrashAndText_PageInTheDatabase_AndCountTheWhole()
    {
        var folder = await CreateFolderAsync();
        var alpha = await CreateFormAsync("alpha", name: "Alpha survey");
        var beta = await CreateFormAsync("betaFeedback", name: "Beta 2030");
        var gamma = await CreateFormAsync("gamma", folder.Id, "Gamma 100% sure");
        var delta = await CreateFormAsync("delta", folder.Id, "Delta");
        await Forms.TrashAsync(delta.Id, delta.ConcurrencyVersion, Editor);

        async Task<IReadOnlyList<int>> IdsAsync(FormFilter filter) => (await Forms.ListAsync(filter, PageRequest.First(50))).Items.Select(form => form.Id).ToList();

        var firstPage = await Forms.ListAsync(FormFilter.Live, new PageRequest(0, 2));
        var secondPage = await Forms.ListAsync(FormFilter.Live, new PageRequest(2, 2));
        var beyond = await Forms.ListAsync(FormFilter.Live, new PageRequest(10, 2));

        Assert.Multiple(async () =>
        {
            Assert.That(await IdsAsync(FormFilter.Live), Is.EqualTo(new[] { alpha.Id, beta.Id, gamma.Id }), "by name, outside the trash");
            Assert.That(await IdsAsync(new FormFilter()), Is.EqualTo(new[] { alpha.Id, beta.Id }), "the root");
            Assert.That(await IdsAsync(new FormFilter { FolderId = folder.Id }), Is.EqualTo(new[] { gamma.Id }));
            Assert.That(await IdsAsync(new FormFilter { FolderId = folder.Id, Trashed = true }), Is.EqualTo(new[] { delta.Id }));
            Assert.That(await IdsAsync(new FormFilter { AllFolders = true, Trashed = null }), Is.EqualTo(new[] { alpha.Id, beta.Id, delta.Id, gamma.Id }));
            Assert.That(await IdsAsync(FormFilter.Live with { Search = "SURVEY" }), Is.EqualTo(new[] { alpha.Id }), "the name, in any case");
            Assert.That(await IdsAsync(FormFilter.Live with { Search = "feedback" }), Is.EqualTo(new[] { beta.Id }), "the alias");
            Assert.That(await IdsAsync(FormFilter.Live with { Search = "0%" }), Is.EqualTo(new[] { gamma.Id }), "% is no wildcard: Beta 2030 has a 0 too");
            Assert.That(await IdsAsync(FormFilter.Live with { Search = "a_" }), Is.Empty, "_ is no wildcard");
            Assert.That(firstPage.Items.Select(form => form.Id), Is.EqualTo(new[] { alpha.Id, beta.Id }));
            Assert.That(secondPage.Items.Select(form => form.Id), Is.EqualTo(new[] { gamma.Id }));
            Assert.That(new[] { firstPage.Total, secondPage.Total, beyond.Total }, Is.All.EqualTo(3));
            Assert.That(beyond.Items, Is.Empty);
        });
    }

    [Test]
    public async Task AForm_IsRenamedMovedAndRestricted_RaisingItsConcurrencyVersion()
    {
        var folder = await CreateFolderAsync();
        var form = await CreateFormAsync("contact");
        var other = Guid.NewGuid();
        Clock.Advance(TimeSpan.FromMinutes(2));

        var updated = await Forms.UpdateAsync(new FormUpdate(form.Id, form.ConcurrencyVersion, "Contact", "Get in touch", folder.Id, true, other));

        Assert.Multiple(async () =>
        {
            Assert.That(updated, Is.EqualTo(form with
            {
                Alias = "Contact",
                Name = "Get in touch",
                FolderId = folder.Id,
                IsRestricted = true,
                ConcurrencyVersion = form.ConcurrencyVersion + 1,
                UpdatedUtc = Now.AddMinutes(2),
                UpdatedByUserKey = other,
            }), "its own alias in another case is no conflict");
            Assert.That(await Forms.UpdateAsync(new FormUpdate(-1, 1, "missing", "Missing", null, false, null)), Is.Null);
        });
    }

    [Test]
    public async Task Updating_IsRefused_WhenStale_ForATakenAlias_AndForAMissingFolder_ChangingNothing()
    {
        var form = await CreateFormAsync("contact");
        await CreateFormAsync("newsletter");
        var current = form.ConcurrencyVersion;

        Assert.Multiple(async () =>
        {
            Assert.That(
                async () => await Forms.UpdateAsync(new FormUpdate(form.Id, current - 1, "contact", "Stale", null, false, null)),
                Throws.InstanceOf<FormBuilderConcurrencyException>());
            AssertConflict(() => Forms.UpdateAsync(new FormUpdate(form.Id, current, "NEWSLETTER", "Taken", null, false, null)), FormBuilderConflict.DuplicateAlias);
            AssertConflict(() => Forms.UpdateAsync(new FormUpdate(form.Id, current, "contact", "Lost", -1, false, null)), FormBuilderConflict.MissingFolder);
            Assert.That(await Forms.GetByIdAsync(form.Id), Is.EqualTo(form));
        });
    }

    [Test]
    public async Task ParallelChanges_FromTheSameVersion_LetExactlyOneThrough()
    {
        var form = await CreateFormAsync();

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => Task.Run(async () =>
        {
            try
            {
                return await Forms.SaveDraftAsync(form.Id, form.ConcurrencyVersion, Document($$"""{"pages":[],"n":{{i}}}"""), null, Editor) is not null;
            }
            catch (FormBuilderConcurrencyException)
            {
                return false;
            }
        })));

        Assert.Multiple(async () =>
        {
            Assert.That(outcomes.Count(won => won), Is.EqualTo(1));
            Assert.That((await Forms.GetByIdAsync(form.Id))?.ConcurrencyVersion, Is.EqualTo(form.ConcurrencyVersion + 1));
            Assert.That(Count(FormBuilderTables.FormVersion, $"{Q("formId")} = @0", form.Id), Is.EqualTo(2), "one version 1, one save");
        });
    }

    [Test]
    public async Task Unpublishing_TakesTheFormOffline_KeepingItsVersionsAndRevision()
    {
        var form = await CreateFormAsync();
        var published = (await Forms.PublishAsync(form.Id, form.ConcurrencyVersion, Document(), "calm", null, Editor))!;

        var offline = await Forms.UnpublishAsync(form.Id, published.Form.ConcurrencyVersion, Editor);
        var again = await Forms.UnpublishAsync(form.Id, offline!.ConcurrencyVersion, Editor);

        Assert.Multiple(async () =>
        {
            Assert.That(offline.PublishedVersionId, Is.Null);
            Assert.That(offline.ThemeAlias, Is.Null);
            Assert.That(offline.PublicRevision, Is.EqualTo(1), "a republish of the same projection keeps it");
            Assert.That(offline.ConcurrencyVersion, Is.EqualTo(published.Form.ConcurrencyVersion + 1));
            Assert.That(again, Is.EqualTo(offline), "an unpublished form stays as it is");
            Assert.That(await Forms.GetVersionByIdAsync(published.Version.Info.Id), Is.Not.Null);
            Assert.That(
                async () => await Forms.UnpublishAsync(form.Id, published.Form.ConcurrencyVersion, Editor),
                Throws.InstanceOf<FormBuilderConcurrencyException>());
            Assert.That(await Forms.UnpublishAsync(-1, 1, Editor), Is.Null);
        });
    }

    [Test]
    public async Task Trashing_AndRestoring_RaiseTheVersion_AndRestoreReturnsToTheFolderOrTheRoot()
    {
        var live = await CreateFolderAsync("Live");
        var doomed = await CreateFolderAsync("Doomed");
        var inLive = await CreateFormAsync(folderId: live.Id);
        var inDoomed = await CreateFormAsync(folderId: doomed.Id);
        Clock.Advance(TimeSpan.FromMinutes(3));

        var trashed = await Forms.TrashAsync(inLive.Id, inLive.ConcurrencyVersion, Editor);
        var again = await Forms.TrashAsync(inLive.Id, trashed!.ConcurrencyVersion, Editor);
        var restored = await Forms.RestoreAsync(inLive.Id, trashed.ConcurrencyVersion, Editor);
        var outside = await Forms.RestoreAsync(inLive.Id, restored!.ConcurrencyVersion, Editor);
        var trashedToo = await Forms.TrashAsync(inDoomed.Id, inDoomed.ConcurrencyVersion, Editor);
        await Folders.TrashAsync(doomed.Id, Editor);
        var toRoot = await Forms.RestoreAsync(inDoomed.Id, trashedToo!.ConcurrencyVersion, Editor);

        Assert.Multiple(async () =>
        {
            Assert.That(trashed.TrashedUtc, Is.EqualTo(Now.AddMinutes(3)));
            Assert.That(trashed.FolderId, Is.EqualTo(live.Id));
            Assert.That(trashed.ConcurrencyVersion, Is.EqualTo(inLive.ConcurrencyVersion + 1));
            Assert.That(again, Is.EqualTo(trashed), "a trashed form stays as it is");
            Assert.That(restored.TrashedUtc, Is.Null);
            Assert.That(restored.FolderId, Is.EqualTo(live.Id), "back into its folder");
            Assert.That(restored.ConcurrencyVersion, Is.EqualTo(trashed.ConcurrencyVersion + 1));
            Assert.That(outside, Is.EqualTo(restored), "a form outside the trash stays as it is");
            Assert.That(toRoot?.FolderId, Is.Null, "the root, as its folder is in the trash");
            Assert.That(
                async () => await Forms.TrashAsync(inLive.Id, inLive.ConcurrencyVersion, Editor),
                Throws.InstanceOf<FormBuilderConcurrencyException>());
            Assert.That(await Forms.TrashAsync(-1, 1, Editor), Is.Null);
            Assert.That(await Forms.RestoreAsync(-1, 1, Editor), Is.Null);
        });
    }

    [Test]
    public async Task Deleting_RemovesTheFormWithItsVersionsAndCatalogue_OnlyWithoutEntries()
    {
        var form = await CreateFormAsync();
        var published = (await Forms.PublishAsync(form.Id, form.ConcurrencyVersion, Document(), null, null, Editor))!;
        await Forms.AddFieldAsync(form.Id, Field("email"));
        var created = await CreateFormAsync();
        await AddEntryAsync(created.Id, created.DraftVersionId!.Value);
        var used = (await Forms.GetByIdAsync(created.Id))!;

        Assert.Multiple(async () =>
        {
            AssertConflict(() => Forms.DeleteAsync(used.Id), FormBuilderConflict.InUse);
            Assert.That(await Forms.GetByIdAsync(used.Id), Is.EqualTo(used), "the refused delete changed nothing");
            Assert.That(await Forms.DeleteAsync(published.Form.Id), Is.True);
            Assert.That(await Forms.DeleteAsync(published.Form.Id), Is.False, "deleted already");
            Assert.That(await Forms.GetByIdAsync(form.Id), Is.Null);
            Assert.That(Count(FormBuilderTables.FormVersion, $"{Q("formId")} = @0", form.Id), Is.Zero);
            Assert.That(Count(FormBuilderTables.FormField, $"{Q("formId")} = @0", form.Id), Is.Zero);
        });
    }

    internal static CatalogueFieldValues Field(string alias, int sortOrder = 0) =>
        new(Guid.NewGuid(), alias, alias.ToUpperInvariant(), "text", "Text", false, sortOrder, null, null, null);

    internal static void AssertConflict<T>(Func<Task<T>> change, FormBuilderConflict conflict, string? why = null) =>
        Assert.That(
            async () => await change(),
            Throws.InstanceOf<FormBuilderConflictException>().With.Property(nameof(FormBuilderConflictException.Conflict)).EqualTo(conflict),
            why ?? conflict.ToString());
}
