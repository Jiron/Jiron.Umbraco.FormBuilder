using System.Globalization;
using System.Reflection;
using Umbraco.Cms.Core.Cache;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Architecture;

/// <summary>
/// The unit tests run against the Umbraco that NuGet resolves for the packages' range, its floor (D3), and the floor
/// clears the runtime cache reliably (ADR-0074): before 17.6.0 a key removed and inserted again could drop out of the
/// cache's own key set, so a later clear by prefix left the value kept. Lowering the range below 17.6.0 fails here.
/// </summary>
[TestFixture]
public sealed class UmbracoFloorTests
{
    private static readonly Version Floor = new(17, 6, 0);

    [Test]
    public void TheTests_RunOnAnUmbracoWhoseRuntimeCacheClearsReliably()
    {
        var informational = typeof(ObjectCacheAppCache).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var numbers = informational?.Split('+', '-')[0];

        Assert.That(Version.TryParse(numbers, out var version), Is.True, string.Create(CultureInfo.InvariantCulture, $"Umbraco's version '{informational}'"));
        Assert.That(version, Is.GreaterThanOrEqualTo(Floor), "Umbraco before 17.6.0 can keep a value its runtime cache was told to clear (ADR-0074).");
    }
}
