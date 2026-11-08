using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Themes;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Settings;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Core.Sync;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Themes;

/// <summary>
/// The library themes the website renders, kept per alias in the runtime cache (ADR-0215), as the option lists' are
/// (ADR-0073): an alias read before the theme exists, a theme read and then changed, and one deleted each show at once and
/// tell every server; another server's change reaches this one only through its cache instruction, read from its JSON.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class LibraryThemeCacheTests : RepositoryTestBase
{
    private ThemeLibrary Library => GetRequiredService<ThemeLibrary>();

    private LibraryThemes Rendered => GetRequiredService<LibraryThemes>();

    private RecordingServerMessenger Messenger => GetRequiredService<RecordingServerMessenger>();

    [Test]
    public async Task AThemeReadMissing_ThenCreatedChangedAndDeleted_ShowsEachChangeAtOnce_AndTellsEveryServer()
    {
        var missing = await Rendered.FindAsync("brand");
        var created = (await Library.CreateAsync(new ThemeInput("brand", "Brand", "default", ThemeTokens.None, null), Editor)).Theme!;
        var first = await Rendered.FindAsync("brand");
        var changed = (await Library.UpdateAsync(created.Key, created.ConcurrencyVersion, new ThemeInput("brand", "Brand colours", "default", ThemeTokens.None, null), Editor)).Theme!;
        var second = await Rendered.FindAsync("BRAND");
        var deleted = await Library.DeleteAsync(changed.Key, changed.ConcurrencyVersion, Editor);
        var gone = await Rendered.FindAsync("brand");

        Assert.Multiple(() =>
        {
            Assert.That(missing, Is.Null, "an alias the library lacks is kept as missing");
            Assert.That(first?.Label, Is.EqualTo("Brand"), "the create shows although the alias was read as missing");
            Assert.That(second?.Label, Is.EqualTo("Brand colours"), "the change shows at once, in any letter case");
            Assert.That(deleted, Is.EqualTo(ThemeDelete.Deleted));
            Assert.That(gone, Is.Null, "a deleted theme is gone at once");
            Assert.That(Messenger.Refreshes.Count(refresh => refresh.Refresher == FormBuilderThemeCacheRefresher.UniqueId), Is.EqualTo(3), "the create, the change and the delete");
        });
    }

    [Test]
    public async Task AnotherServersInstruction_ReadFromItsJson_DropsThisServersCopyOfTheTheme()
    {
        var created = (await Library.CreateAsync(new ThemeInput("brand", "Brand", "default", ThemeTokens.None, null), Editor)).Theme!;
        _ = await Rendered.FindAsync("brand");

        // Another server changes the row; only its cache instruction reaches this one, as JSON.
        _ = await Themes.UpdateAsync(new ThemeUpdate(created.Id, created.ConcurrencyVersion, "Brand colours", "default", "{}", null));
        var kept = await Rendered.FindAsync("brand");
        var refresher = (IJsonCacheRefresher)GetRequiredService<CacheRefresherCollection>()[FormBuilderThemeCacheRefresher.UniqueId]!;
        refresher.Refresh(GetRequiredService<IJsonSerializer>().Serialize(new[] { new ThemeCacheChange("brand") }));
        var refreshed = await Rendered.FindAsync("brand");

        Assert.Multiple(() =>
        {
            Assert.That(kept?.Label, Is.EqualTo("Brand"), "kept until told");
            Assert.That(refreshed?.Label, Is.EqualTo("Brand colours"));
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        base.CustomTestSetup(builder);
        builder.Services.AddUnique(new AppCaches(new ObjectCacheAppCache(), NoAppCache.Instance, new IsolatedCaches(_ => NoAppCache.Instance)));
        builder.Services.AddSingleton<RecordingServerMessenger>();
        builder.Services.AddUnique<IServerMessenger>(provider => provider.GetRequiredService<RecordingServerMessenger>());
    }
}
