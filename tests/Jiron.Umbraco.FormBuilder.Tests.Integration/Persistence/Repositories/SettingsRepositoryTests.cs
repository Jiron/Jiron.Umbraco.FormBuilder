using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;

/// <summary>
/// The settings sections (ADR-0008, ADR-0033): one row per scope and section, saved against the version the editor
/// read, so a stale save changes nothing.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class SettingsRepositoryTests : RepositoryTestBase
{
    private static readonly string Site = SettingsScopes.ForSite(Guid.Parse("2b9d6f0e-6c4a-4f7e-9a31-5e8c0d7b1a22"));

    [Test]
    public async Task TheGlobalRowsOfTheMigration_AreThere_AtVersion1()
    {
        var global = await Settings.ListAsync(SettingsScopes.Global);

        Assert.Multiple(() =>
        {
            Assert.That(SettingsScopes.Global, Is.EqualTo(CreateDefaultSettingsV1.GlobalScope));
            Assert.That(global.Select(row => row.Section), Is.EqualTo(CreateDefaultSettingsV1.Sections.Order(StringComparer.Ordinal)));
            Assert.That(global.Select(row => row.Version), Is.All.EqualTo(1));
            Assert.That(global.Select(row => row.Json), Is.All.EqualTo("{}"));
            Assert.That(global.Select(row => row.UpdatedByUserKey), Is.All.Null);
        });
    }

    [Test]
    public async Task ASave_StoresTheSection_WithTheNextVersion()
    {
        Clock.Advance(TimeSpan.FromHours(1));

        var saved = await Settings.SaveAsync(new SettingsSave(SettingsScopes.Global, "email", 1, 1, """{"fromAddress":"site@jfb.test"}""", Editor));

        Assert.Multiple(async () =>
        {
            Assert.That(saved, Is.EqualTo(new StoredSettings(SettingsScopes.Global, "email", 1, 2, """{"fromAddress":"site@jfb.test"}""", Now.AddHours(1), Editor)));
            Assert.That(await Settings.GetAsync(SettingsScopes.Global, "email"), Is.EqualTo(saved));
        });
    }

    [Test]
    public async Task AStaleSave_IsRefused_AndChangesNothing()
    {
        await Settings.SaveAsync(new SettingsSave(SettingsScopes.Global, "files", 1, 1, """{"maxSizeMb":10}""", Editor));
        var before = await Settings.GetAsync(SettingsScopes.Global, "files");

        Assert.Multiple(async () =>
        {
            Assert.That(
                async () => await Settings.SaveAsync(new SettingsSave(SettingsScopes.Global, "files", 1, 1, """{"maxSizeMb":99}""", Guid.NewGuid())),
                Throws.InstanceOf<FormBuilderConcurrencyException>());
            Assert.That(await Settings.GetAsync(SettingsScopes.Global, "files"), Is.EqualTo(before));
        });
    }

    [Test]
    public async Task ASitesFirstSave_InsertsItsRowOnce()
    {
        var first = await Settings.SaveAsync(new SettingsSave(Site, "general", 1, 0, """{"a":1}""", Editor));

        Assert.Multiple(async () =>
        {
            Assert.That(first.Version, Is.EqualTo(1));
            Assert.That(
                async () => await Settings.SaveAsync(new SettingsSave(Site, "general", 1, 0, """{"a":2}""", Editor)),
                Throws.InstanceOf<FormBuilderConcurrencyException>(),
                "someone saved the section first");
            Assert.That(
                async () => await Settings.SaveAsync(new SettingsSave(Site, "privacy", 1, 3, "{}", Editor)),
                Throws.InstanceOf<FormBuilderConcurrencyException>(),
                "a version for a row that is not there");
            Assert.That((await Settings.GetAsync(Site, "general"))?.Json, Is.EqualTo("""{"a":1}"""));
            Assert.That(await Settings.GetAsync(Site, "privacy"), Is.Null);
        });
    }

    [Test]
    public async Task ParallelSaves_FromTheSameVersion_LetExactlyOneThrough()
    {
        var outcomes = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => Task.Run(async () =>
        {
            try
            {
                await Settings.SaveAsync(new SettingsSave(SettingsScopes.Global, "protection", 1, 1, $$"""{"n":{{i}}}""", Editor));
                return true;
            }
            catch (FormBuilderConcurrencyException)
            {
                return false;
            }
        })));
        var firstSaves = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => Task.Run(async () =>
        {
            try
            {
                await Settings.SaveAsync(new SettingsSave(Site, "protection", 1, 0, $$"""{"n":{{i}}}""", Editor));
                return true;
            }
            catch (FormBuilderConcurrencyException)
            {
                return false;
            }
        })));

        Assert.Multiple(async () =>
        {
            Assert.That(outcomes.Count(saved => saved), Is.EqualTo(1));
            Assert.That((await Settings.GetAsync(SettingsScopes.Global, "protection"))?.Version, Is.EqualTo(2));
            Assert.That(firstSaves.Count(saved => saved), Is.EqualTo(1), "a site's row is inserted once");
            Assert.That(Count(FormBuilderTables.Setting, $"{Q("scope")} = @0", Site), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ASection_IsListedForEveryScope_TheGlobalRowFirst()
    {
        var later = SettingsScopes.ForSite(Guid.Parse("00000000-0000-0000-0000-000000000001"));
        await Settings.SaveAsync(new SettingsSave(Site, "email", 1, 0, "{}", Editor));
        await Settings.SaveAsync(new SettingsSave(later, "email", 1, 0, "{}", Editor));

        var rows = await Settings.ListSectionAsync("email");

        Assert.Multiple(async () =>
        {
            Assert.That(rows.Select(row => row.Scope), Is.EqualTo(new[] { SettingsScopes.Global, later, Site }));
            Assert.That((await Settings.ListAsync(Site)).Select(row => row.Section), Is.EqualTo(new[] { "email" }));
        });
    }

    [Test]
    public async Task ASitesSection_IsDeletedAgainstItsVersion()
    {
        var saved = await Settings.SaveAsync(new SettingsSave(Site, "email", 1, 0, "{}", Editor));
        var resaved = await Settings.SaveAsync(new SettingsSave(Site, "email", 1, saved.Version, """{"x":1}""", Editor));

        Assert.Multiple(async () =>
        {
            Assert.That(
                async () => await Settings.DeleteAsync(Site, "email", saved.Version),
                Throws.InstanceOf<FormBuilderConcurrencyException>());
            Assert.That(await Settings.DeleteAsync(Site, "email", resaved.Version), Is.True);
            Assert.That(await Settings.DeleteAsync(Site, "email", resaved.Version), Is.False, "deleted already");
            Assert.That(await Settings.GetAsync(Site, "email"), Is.Null);
        });
    }
}
