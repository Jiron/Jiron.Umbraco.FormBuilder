using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;

/// <summary>The email layouts of the library (ADR-0037), on top of the shared tests of the library rows.</summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class EmailLayoutRepositoryTests : RenamableLibraryRepositoryTests
{
    private const string Layout = """{"logo":null,"footer":{"en":"JFB_TEST footer"}}""";

    private protected override string Table => FormBuilderTables.EmailLayout;

    [Test]
    public async Task ALayout_KeepsItsJson()
    {
        var created = await EmailLayouts.CreateAsync(new NewEmailLayout(Guid.NewGuid(), "plain", "Plain"));

        var changed = await EmailLayouts.UpdateAsync(new EmailLayoutUpdate(created.Id, 1, "plain", "Plain", Layout));

        Assert.Multiple(async () =>
        {
            Assert.That(created with { Id = 0, Key = Guid.Empty }, Is.EqualTo(new StoredEmailLayout(0, Guid.Empty, "plain", "Plain", "{}", 1, Now, Now)));
            Assert.That(changed, Is.EqualTo(created with { Layout = Layout, ConcurrencyVersion = 2 }));
            Assert.That(await EmailLayouts.GetByAliasAsync("PLAIN"), Is.EqualTo(changed));
        });
    }

    private protected override async Task<LibraryRow> CreateAsync(string alias, string name = "Name", Guid? key = null) =>
        Row(await EmailLayouts.CreateAsync(new NewEmailLayout(key ?? Guid.NewGuid(), alias, name, Layout)))!;

    private protected override async Task<LibraryRow?> GetAsync(Guid key) => Row(await EmailLayouts.GetAsync(key));

    private protected override async Task<LibraryRow?> GetByIdAsync(int id) => Row(await EmailLayouts.GetByIdAsync(id));

    private protected override async Task<LibraryRow?> GetByAliasAsync(string alias) => Row(await EmailLayouts.GetByAliasAsync(alias));

    private protected override async Task<Page<LibraryRow>> ListAsync(string? search, PageRequest page)
    {
        var layouts = await EmailLayouts.ListAsync(search, page);
        return new Page<LibraryRow>(layouts.Items.Select(layout => Row(layout)!).ToList(), layouts.Total);
    }

    private protected override async Task<LibraryRow?> UpdateAsync(LibraryRow row, int expectedConcurrencyVersion, string name) =>
        Row(await EmailLayouts.UpdateAsync(new EmailLayoutUpdate(row.Id, expectedConcurrencyVersion, row.Alias, name, "{}")));

    private protected override async Task<LibraryRow?> ChangeAliasAsync(LibraryRow row, int expectedConcurrencyVersion, string alias) =>
        Row(await EmailLayouts.UpdateAsync(new EmailLayoutUpdate(row.Id, expectedConcurrencyVersion, alias, row.Name, Layout)));

    private protected override Task<bool> DeleteAsync(LibraryRow row, int expectedConcurrencyVersion) =>
        EmailLayouts.DeleteAsync(row.Id, expectedConcurrencyVersion);

    private static LibraryRow? Row(StoredEmailLayout? layout) =>
        layout is null ? null : new(layout.Id, layout.Key, layout.Alias, layout.Name, layout.ConcurrencyVersion, layout.CreatedUtc, layout.UpdatedUtc, layout);
}
