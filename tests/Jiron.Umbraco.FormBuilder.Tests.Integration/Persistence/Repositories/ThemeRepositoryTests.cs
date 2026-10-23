using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;

/// <summary>
/// The themes of the library (ADR-0015, ADR-0037), on top of the shared tests of the library rows: the alias is fixed,
/// and a theme a published form names stays.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class ThemeRepositoryTests : AliasedLibraryRepositoryTests
{
    private const string Tokens = """{"colorAccent":"#0b57d0","radius":"6px"}""";

    private protected override string Table => FormBuilderTables.Theme;

    [Test]
    public async Task ATheme_KeepsItsBaseTokensAndCss_AndItsAlias()
    {
        var created = await Themes.CreateAsync(new NewTheme(Guid.NewGuid(), "brand", "Brand", "default"));

        var changed = await Themes.UpdateAsync(new ThemeUpdate(created.Id, 1, "Brand", "bootstrap5", Tokens, ".jfb-form { gap: 1rem; }"));
        var cleared = await Themes.UpdateAsync(new ThemeUpdate(created.Id, 2, "Brand", "bootstrap5", Tokens, null));

        Assert.Multiple(() =>
        {
            Assert.That(created with { Id = 0, Key = Guid.Empty }, Is.EqualTo(new StoredTheme(0, Guid.Empty, "brand", "Brand", "default", "{}", null, 1, Now, Now)));
            Assert.That(changed, Is.EqualTo(created with { BaseTheme = "bootstrap5", Tokens = Tokens, CustomCss = ".jfb-form { gap: 1rem; }", ConcurrencyVersion = 2 }));
            Assert.That(cleared?.CustomCss, Is.Null);
            Assert.That(cleared?.Alias, Is.EqualTo("brand"), "an update never touches the alias");
        });
    }

    [Test]
    public async Task ATheme_StaysWhileAPublishedFormNamesIt_InAnyLetterCase()
    {
        var theme = await Themes.CreateAsync(new NewTheme(Guid.NewGuid(), "brand", "Brand", "default"));
        var form = await CreateFormAsync();
        var published = (await Forms.PublishAsync(form.Id, form.ConcurrencyVersion, Document(), "BRAND", null, Editor))!.Form;
        var trashed = (await Forms.TrashAsync(published.Id, published.ConcurrencyVersion, Editor))!;

        Assert.Multiple(async () =>
        {
            AssertRefused(() => Themes.DeleteAsync(theme.Id, theme.ConcurrencyVersion), FormBuilderConflict.InUse, "a trashed form can come back");
            Assert.That(await Themes.GetByIdAsync(theme.Id), Is.EqualTo(theme), "the refused delete changed nothing");
            Assert.That(
                async () => await Themes.DeleteAsync(theme.Id, theme.ConcurrencyVersion + 1),
                Throws.InstanceOf<FormBuilderConcurrencyException>(),
                "a stale delete says so first");
            await Forms.UnpublishAsync(trashed.Id, trashed.ConcurrencyVersion, Editor);
            Assert.That(await Themes.DeleteAsync(theme.Id, theme.ConcurrencyVersion), Is.True, "unpublishing let go of the theme");
        });
    }

    [Test]
    public async Task AnotherThemesName_DoesNotKeepATheme()
    {
        var theme = await Themes.CreateAsync(new NewTheme(Guid.NewGuid(), "brand", "Brand", "default"));
        var form = await CreateFormAsync();
        await Forms.PublishAsync(form.Id, form.ConcurrencyVersion, Document(), "brand2", null, Editor);

        Assert.That(await Themes.DeleteAsync(theme.Id, theme.ConcurrencyVersion), Is.True);
    }

    private protected override async Task<LibraryRow> CreateAsync(string alias, string name = "Name", Guid? key = null) =>
        Row(await Themes.CreateAsync(new NewTheme(key ?? Guid.NewGuid(), alias, name, "default", Tokens)))!;

    private protected override async Task<LibraryRow?> GetAsync(Guid key) => Row(await Themes.GetAsync(key));

    private protected override async Task<LibraryRow?> GetByIdAsync(int id) => Row(await Themes.GetByIdAsync(id));

    private protected override async Task<LibraryRow?> GetByAliasAsync(string alias) => Row(await Themes.GetByAliasAsync(alias));

    private protected override async Task<Page<LibraryRow>> ListAsync(string? search, PageRequest page)
    {
        var themes = await Themes.ListAsync(search, page);
        return new Page<LibraryRow>(themes.Items.Select(theme => Row(theme)!).ToList(), themes.Total);
    }

    private protected override async Task<LibraryRow?> UpdateAsync(LibraryRow row, int expectedConcurrencyVersion, string name) =>
        Row(await Themes.UpdateAsync(new ThemeUpdate(row.Id, expectedConcurrencyVersion, name, "unstyled", "{}", ".x { }")));

    private protected override Task<bool> DeleteAsync(LibraryRow row, int expectedConcurrencyVersion) =>
        Themes.DeleteAsync(row.Id, expectedConcurrencyVersion);

    private static LibraryRow? Row(StoredTheme? theme) =>
        theme is null ? null : new(theme.Id, theme.Key, theme.Alias, theme.Name, theme.ConcurrencyVersion, theme.CreatedUtc, theme.UpdatedUtc, theme);
}
