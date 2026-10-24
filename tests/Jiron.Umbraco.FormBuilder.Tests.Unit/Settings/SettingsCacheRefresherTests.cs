using Jiron.Umbraco.FormBuilder.Core.Settings;
using NSubstitute;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Serialization;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Settings;

/// <summary>
/// What a saved section clears from a server's cache (ADR-0008, ADR-0043): its scope's key and loading token, and for
/// the global scope every scope's, since each site's values rest on the global row; nothing of another section.
/// </summary>
[TestFixture]
public sealed class SettingsCacheRefresherTests
{
    private const string SiteA = "6c0e4a1e-5f0b-4b8e-9a51-2d3f7c9e1a01";
    private const string SiteB = "6c0e4a1e-5f0b-4b8e-9a51-2d3f7c9e1a02";

    private static readonly string[] Keys =
    [
        "Jiron.FormBuilder.Settings.global.general",
        "Jiron.FormBuilder.Settings.global.general.loading",
        $"Jiron.FormBuilder.Settings.{SiteA}.general",
        $"Jiron.FormBuilder.Settings.{SiteA}.general.loading",
        $"Jiron.FormBuilder.Settings.{SiteB}.general",
        "Jiron.FormBuilder.Settings.global.email",
        $"Jiron.FormBuilder.Settings.{SiteA}.email",
        "Jiron.FormBuilder.Settings.global.generalx",
        "Jiron.FormBuilder.Settings.global.xgeneral",
        "Jiron.FormBuilder.Settings.global.general.other",
        "Elsewhere.Settings.global.general",
        "Other.Jiron.FormBuilder.Settings.global.general",
    ];

    // The keys that are not Jiron Form Builder's settings, though one holds such a key.
    private static readonly string[] Untouched = Keys[^2..];

    [Test]
    public void TheKey_OfASectionInAScope_IsNamedAsAdr0008Says()
    {
        Assert.That(SettingsCacheKeys.Of("global", "general"), Is.EqualTo("Jiron.FormBuilder.Settings.global.general"));
    }

    [Test]
    public void AGlobalSave_ClearsTheSectionInEveryScope_WithTheLoadingTokens()
    {
        Assert.That(
            Left(new SettingsCacheChange("global", "general")),
            Is.EquivalentTo(Keys.Except(Keys[..5])));
    }

    [Test]
    public void ASiteSave_ClearsOnlyThatSitesSection()
    {
        Assert.That(
            Left(new SettingsCacheChange(SiteA, "general")),
            Is.EquivalentTo(Keys.Except([Keys[2], Keys[3]])));
    }

    [TestCase("Global", "general", Description = "a scope in another letter case")]
    [TestCase("global", "gen.*", Description = "a pattern for a section")]
    [TestCase("global", "", Description = "no section")]
    [TestCase("[^.]+", "general", Description = "a pattern for a scope")]
    public void AChangeItCannotRead_ClearsEverySection_AndNothingElse(string scope, string section)
    {
        Assert.That(Left(new SettingsCacheChange(scope, section)), Is.EqualTo(Untouched));
    }

    [Test]
    public void RefreshingAll_ClearsEverySection_AndNothingElse()
    {
        var cache = Filled();
        var refresher = new FormBuilderSettingsCacheRefresher(
            new AppCaches(cache, NoAppCache.Instance, new IsolatedCaches(_ => NoAppCache.Instance)),
            Substitute.For<IJsonSerializer>(),
            Substitute.For<IEventAggregator>(),
            Substitute.For<ICacheRefresherNotificationFactory>());

        refresher.RefreshAll();

        Assert.That(Keys.Where(key => cache.Get(key) is not null), Is.EqualTo(Untouched));
    }

    [Test]
    public void TheRefresher_HasItsOwnFixedId()
    {
        Assert.That(FormBuilderSettingsCacheRefresher.UniqueId, Is.EqualTo(Guid.Parse("b9ab1ec0-3f41-4c65-842b-339f59ee21c4")));
    }

    // The keys still cached after the change.
    private static List<string> Left(SettingsCacheChange change)
    {
        var cache = Filled();
        FormBuilderSettingsCacheRefresher.Forget(cache, change);
        return Keys.Where(key => cache.Get(key) is not null).ToList();
    }

    // A cache holding every key.
    private static ObjectCacheAppCache Filled()
    {
        var cache = new ObjectCacheAppCache();
        foreach (var key in Keys)
        {
            cache.Insert(key, () => new object());
        }

        return cache;
    }
}
