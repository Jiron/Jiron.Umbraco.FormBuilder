using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;

/// <summary>
/// A service combines repository calls into one transaction by running them inside one unit of work (ADR-0036): they
/// commit together, or not at all.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class UnitOfWorkTests : RepositoryTestBase
{
    [Test]
    public async Task RepositoryWrites_InsideOneUnit_CommitTogether()
    {
        var key = Guid.NewGuid();

        var saved = await Database.WriteAsync(async _ =>
        {
            var folder = await Folders.CreateAsync(new NewFolder(key, null, "Together"));
            var created = await Forms.CreateAsync(NewForm(folderId: folder.Id));
            return await Forms.PublishAsync(created.Form.Id, created.Form.ConcurrencyVersion, Document(), null, null, Editor);
        });

        Assert.Multiple(async () =>
        {
            Assert.That(saved?.Form.PublishedVersionId, Is.Not.Null);
            Assert.That((await Folders.GetAsync(key))?.Id, Is.EqualTo(saved?.Form.FolderId));
        });
    }

    [Test]
    public async Task RepositoryWrites_InsideOneUnit_RollBackTogether_WhenItFails()
    {
        var key = Guid.NewGuid();

        Assert.That(
            async () => await Database.WriteAsync<bool>(async _ =>
            {
                var folder = await Folders.CreateAsync(new NewFolder(key, null, "Together"));
                await Forms.CreateAsync(NewForm("together", folder.Id));
                throw new InvalidOperationException("JFB_TEST: the service changes its mind");
            }),
            Throws.InvalidOperationException);

        Assert.Multiple(async () =>
        {
            Assert.That(await Folders.GetAsync(key), Is.Null);
            Assert.That(await Forms.GetByAliasAsync("together"), Is.Null);
        });
    }

    [Test]
    public async Task ARefusedChange_RollsBackTheUnitItRanIn()
    {
        var existing = await CreateFormAsync("taken");
        var key = Guid.NewGuid();

        Assert.That(
            async () => await Database.WriteAsync(async _ =>
            {
                await Folders.CreateAsync(new NewFolder(key, null, "Before the refusal"));
                return await Forms.CreateAsync(NewForm("TAKEN"));
            }),
            Throws.InstanceOf<FormBuilderConflictException>());

        Assert.Multiple(async () =>
        {
            Assert.That(await Folders.GetAsync(key), Is.Null);
            Assert.That(await Forms.GetByIdAsync(existing.Id), Is.EqualTo(existing));
        });
    }

    [Test]
    public async Task UnitsOnTheirOwn_CommitOnTheirOwn()
    {
        var kept = await CreateFolderAsync("Kept");

        Assert.That(async () => await Folders.CreateAsync(new NewFolder(kept.Key, null, "Duplicate")), Throws.InstanceOf<FormBuilderConflictException>());

        Assert.That(await Folders.GetByIdAsync(kept.Id), Is.EqualTo(kept));
    }
}
