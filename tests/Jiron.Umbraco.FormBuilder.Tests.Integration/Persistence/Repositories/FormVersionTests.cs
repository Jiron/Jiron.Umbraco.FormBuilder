using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;

/// <summary>
/// The versions of a form (ADR-0002): every save and publish appends one, the public revision moves only when the
/// projection changes, and the retention job may prune only what nothing needs.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class FormVersionTests : RepositoryTestBase
{
    [Test]
    public async Task ASave_AppendsTheNextVersion_MakesItTheDraft_AndRaisesTheConcurrencyVersion()
    {
        var form = await CreateFormAsync();
        var draft = Document("""{"pages":[1]}""");
        Clock.Advance(TimeSpan.FromMinutes(1));

        var saved = await Forms.SaveDraftAsync(form.Id, form.ConcurrencyVersion, draft, "Second", Editor);

        Assert.Multiple(async () =>
        {
            Assert.That(saved, Is.Not.Null);
            Assert.That(saved!.Version.Info.Number, Is.EqualTo(2));
            Assert.That(saved.Version.Info.Kind, Is.EqualTo(FormVersionKind.Draft));
            Assert.That(saved.Version.Info.Comment, Is.EqualTo("Second"));
            Assert.That(saved.Version.Info.CreatedUtc, Is.EqualTo(Now.AddMinutes(1)));
            Assert.That(saved.Version.Definition, Is.EqualTo(draft.Definition));
            Assert.That(saved.Form.DraftVersionId, Is.EqualTo(saved.Version.Info.Id));
            Assert.That(saved.Form.ConcurrencyVersion, Is.EqualTo(form.ConcurrencyVersion + 1));
            Assert.That(saved.Form.UpdatedUtc, Is.EqualTo(Now.AddMinutes(1)));
            Assert.That(await Forms.GetVersionAsync(form.Id, 2), Is.EqualTo(saved.Version));
            Assert.That(await Forms.GetVersionAsync(form.Id, 3), Is.Null);
        });
    }

    [Test]
    public async Task AStaleSave_IsRefused_AndAppendsNothing()
    {
        var form = await CreateFormAsync();
        await Forms.SaveDraftAsync(form.Id, form.ConcurrencyVersion, Document(), null, Editor);

        Assert.Multiple(async () =>
        {
            Assert.That(
                async () => await Forms.SaveDraftAsync(form.Id, form.ConcurrencyVersion, Document("""{"pages":[2]}"""), null, Editor),
                Throws.InstanceOf<FormBuilderConcurrencyException>());
            Assert.That(Count(FormBuilderTables.FormVersion, $"{Q("formId")} = @0", form.Id), Is.EqualTo(2));
            Assert.That(await Forms.SaveDraftAsync(-1, 1, Document(), null, Editor), Is.Null, "a missing form");
        });
    }

    [Test]
    public async Task APublish_StartsTheRevisionAt1_AndRaisesItOnlyWhenTheProjectionChanges()
    {
        var form = await CreateFormAsync();
        var first = Document("""{"v":1}""", "public A");
        var sameProjection = Document("""{"v":2}""", "public A");
        var changed = Document("""{"v":3}""", "public B");
        Clock.Advance(TimeSpan.FromMinutes(1));

        var one = (await Forms.PublishAsync(form.Id, form.ConcurrencyVersion, first, "calm", "Go live", Editor))!;
        var two = (await Forms.PublishAsync(form.Id, one.Form.ConcurrencyVersion, sameProjection, "calm", null, Editor))!;
        var three = (await Forms.PublishAsync(form.Id, two.Form.ConcurrencyVersion, changed, "bold", null, Editor))!;
        var four = (await Forms.PublishAsync(form.Id, three.Form.ConcurrencyVersion, first, null, null, Editor))!;

        Assert.Multiple(() =>
        {
            Assert.That(new[] { one, two, three, four }.Select(saved => saved.Version.Info.PublicRevision), Is.EqualTo(new int?[] { 1, 1, 2, 3 }), "A, A again, B, then A after B");
            Assert.That(new[] { one, two, three, four }.Select(saved => saved.Form.PublicRevision), Is.EqualTo(new[] { 1, 1, 2, 3 }));
            Assert.That(new[] { one, two, three, four }.Select(saved => saved.Version.Info.Number), Is.EqualTo(new[] { 2, 3, 4, 5 }));
            Assert.That(one.Version.Info.Kind, Is.EqualTo(FormVersionKind.Published));
            Assert.That(one.Version.Info.PublishedUtc, Is.EqualTo(Now.AddMinutes(1)));
            Assert.That(one.Version.Info.PublishedByUserKey, Is.EqualTo(Editor));
            Assert.That(one.Version.Info.Comment, Is.EqualTo("Go live"));
            Assert.That(one.Form.PublishedVersionId, Is.EqualTo(one.Version.Info.Id));
            Assert.That(one.Form.DraftVersionId, Is.EqualTo(form.DraftVersionId), "the draft stays the latest draft row");
            Assert.That(new[] { one, two, three, four }.Select(saved => saved.Form.ThemeAlias), Is.EqualTo(new[] { "calm", "calm", "bold", null }));
            Assert.That(four.Form.ConcurrencyVersion, Is.EqualTo(form.ConcurrencyVersion + 4));
        });
    }

    [Test]
    public async Task AStalePublish_IsRefused_AndChangesNothing()
    {
        var form = await CreateFormAsync();
        await Forms.SaveDraftAsync(form.Id, form.ConcurrencyVersion, Document(), null, Editor);

        Assert.Multiple(async () =>
        {
            Assert.That(
                async () => await Forms.PublishAsync(form.Id, form.ConcurrencyVersion, Document(), null, null, Editor),
                Throws.InstanceOf<FormBuilderConcurrencyException>());
            Assert.That((await Forms.GetByIdAsync(form.Id))?.PublishedVersionId, Is.Null);
            Assert.That(await Forms.PublishAsync(-1, 1, Document(), null, null, Editor), Is.Null, "a missing form");
        });
    }

    [Test]
    public async Task TheVersionList_IsNewestFirst_PagedInTheDatabase_WithoutDefinitions()
    {
        var form = await CreateFormAsync();
        var version = form.ConcurrencyVersion;
        for (var i = 0; i < 4; i++)
        {
            version = (await Forms.SaveDraftAsync(form.Id, version, Document($$"""{"n":{{i}}}"""), $"save {i}", Editor))!.Form.ConcurrencyVersion;
        }

        var first = await Forms.ListVersionsAsync(form.Id, new PageRequest(0, 2));
        var last = await Forms.ListVersionsAsync(form.Id, new PageRequest(4, 2));

        Assert.Multiple(async () =>
        {
            Assert.That(first.Items.Select(info => info.Number), Is.EqualTo(new[] { 5, 4 }));
            Assert.That(first.Items.Select(info => info.Comment), Is.EqualTo(new[] { "save 3", "save 2" }));
            Assert.That(last.Items.Select(info => info.Number), Is.EqualTo(new[] { 1 }));
            Assert.That(new[] { first.Total, last.Total }, Is.All.EqualTo(5));
            Assert.That((await Forms.ListVersionsAsync(-1, PageRequest.First(10))).Total, Is.Zero);
        });
    }

    [Test]
    public async Task AVersion_TakesAComment_AndAPin_WithoutChangingTheForm()
    {
        var form = await CreateFormAsync();

        var pinned = await Forms.UpdateVersionAsync(form.Id, 1, "Approved by legal", true);
        var cleared = await Forms.UpdateVersionAsync(form.Id, 1, null, false);

        Assert.Multiple(async () =>
        {
            Assert.That(pinned?.Comment, Is.EqualTo("Approved by legal"));
            Assert.That(pinned?.IsPinned, Is.True);
            Assert.That(cleared?.Comment, Is.Null);
            Assert.That(cleared?.IsPinned, Is.False);
            Assert.That(await Forms.GetByIdAsync(form.Id), Is.EqualTo(form), "no change to the form's row");
            Assert.That(await Forms.UpdateVersionAsync(form.Id, 9, null, true), Is.Null);
        });
    }

    [Test]
    public async Task Pruning_RemovesOnlyADraftThatNothingNeeds()
    {
        var form = await CreateFormAsync();
        var version = form.ConcurrencyVersion;
        for (var i = 0; i < 4; i++)
        {
            version = (await Forms.SaveDraftAsync(form.Id, version, Document($$"""{"n":{{i}}}"""), null, Editor))!.Form.ConcurrencyVersion;
        }

        // 1 is unused, 2 is pinned, 3 has an entry (a preview), 5 is the draft, 6 is published.
        var published = (await Forms.PublishAsync(form.Id, version, Document(), null, null, Editor))!;
        await Forms.UpdateVersionAsync(form.Id, 2, null, true);
        AddEntry(form.Id, (await Forms.GetVersionAsync(form.Id, 3))!.Info.Id);
        var draftNumber = (await Forms.GetVersionByIdAsync(published.Form.DraftVersionId!.Value))!.Info.Number;

        Assert.Multiple(async () =>
        {
            Assert.That(await Forms.PruneDraftAsync(form.Id, 1), Is.True, "unused");
            Assert.That(await Forms.PruneDraftAsync(form.Id, 1), Is.False, "pruned already");
            Assert.That(await Forms.PruneDraftAsync(form.Id, 2), Is.False, "pinned");
            Assert.That(await Forms.PruneDraftAsync(form.Id, 3), Is.False, "an entry points at it");
            Assert.That(await Forms.PruneDraftAsync(form.Id, draftNumber), Is.False, "the form's draft");
            Assert.That(await Forms.PruneDraftAsync(form.Id, published.Version.Info.Number), Is.False, "published");
            Assert.That(Count(FormBuilderTables.FormVersion, $"{Q("formId")} = @0", form.Id), Is.EqualTo(5));
        });
    }
}
