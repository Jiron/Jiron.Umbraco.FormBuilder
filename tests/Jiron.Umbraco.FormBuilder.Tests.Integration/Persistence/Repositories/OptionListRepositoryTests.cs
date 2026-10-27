using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;

/// <summary>The option lists of the library (ADR-0037), on top of the shared tests of the library rows.</summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class OptionListRepositoryTests : RenamableLibraryRepositoryTests
{
    private const string Items = """[{"value":"red","label":{"en":"Red"}}]""";

    private protected override string Table => FormBuilderTables.OptionList;

    [Test]
    public async Task AList_KeepsItsKindSourceItemsAndCache()
    {
        var created = await OptionLists.CreateAsync(new NewOptionList(Guid.NewGuid(), "colours", "Colours", OptionListKind.Manual, Items: Items, CacheSeconds: 0));
        Clock.Advance(TimeSpan.FromMinutes(1));

        var changed = await OptionLists.UpdateAsync(new OptionListUpdate(created.Id, 1, "colours", "Colours", OptionListKind.Url, """{"url":"https://options.jfb.test/colours.json"}""", "[]", 300));

        Assert.Multiple(async () =>
        {
            Assert.That(created with { Id = 0, Key = Guid.Empty }, Is.EqualTo(new StoredOptionList(0, Guid.Empty, "colours", "Colours", OptionListKind.Manual, "{}", Items, 0, 1, Now, Now)));
            Assert.That(changed, Is.EqualTo(created with { Kind = OptionListKind.Url, Config = """{"url":"https://options.jfb.test/colours.json"}""", Items = "[]", CacheSeconds = 300, ConcurrencyVersion = 2, UpdatedUtc = Now.AddMinutes(1) }));
            Assert.That(await OptionLists.GetAsync(created.Key), Is.EqualTo(changed));
        });
    }

    private protected override async Task<LibraryRow> CreateAsync(string alias, string name = "Name", Guid? key = null) =>
        Row(await OptionLists.CreateAsync(new NewOptionList(key ?? Guid.NewGuid(), alias, name, OptionListKind.Manual, Items: Items)))!;

    private protected override async Task<LibraryRow?> GetAsync(Guid key) => Row(await OptionLists.GetAsync(key));

    private protected override async Task<LibraryRow?> GetByIdAsync(int id) => Row(await OptionLists.GetByIdAsync(id));

    private protected override async Task<LibraryRow?> GetByAliasAsync(string alias) => Row(await OptionLists.GetByAliasAsync(alias));

    private protected override async Task<Page<LibraryRow>> ListAsync(string? search, PageRequest page)
    {
        var lists = await OptionLists.ListAsync(search, page);
        return new Page<LibraryRow>(lists.Items.Select(list => Row(list)!).ToList(), lists.Total);
    }

    private protected override async Task<LibraryRow?> UpdateAsync(LibraryRow row, int expectedConcurrencyVersion, string name) =>
        Row(await OptionLists.UpdateAsync(new OptionListUpdate(row.Id, expectedConcurrencyVersion, row.Alias, name, OptionListKind.File, "{}", "[]", 60)));

    private protected override async Task<LibraryRow?> ChangeAliasAsync(LibraryRow row, int expectedConcurrencyVersion, string alias) =>
        Row(await OptionLists.UpdateAsync(new OptionListUpdate(row.Id, expectedConcurrencyVersion, alias, row.Name, OptionListKind.Manual, "{}", Items, 0)));

    private protected override Task<bool> DeleteAsync(LibraryRow row, int expectedConcurrencyVersion) =>
        OptionLists.DeleteAsync(row.Id, expectedConcurrencyVersion);

    private static LibraryRow? Row(StoredOptionList? list) =>
        list is null ? null : new(list.Id, list.Key, list.Alias, list.Name, list.ConcurrencyVersion, list.CreatedUtc, list.UpdatedUtc, list);
}
