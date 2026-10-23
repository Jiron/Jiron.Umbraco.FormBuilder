using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Testing.Startup;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Umbraco.Cms.Infrastructure.Examine;
using Umbraco.Cms.Tests.Common.Testing;
using Umbraco.Cms.Tests.Integration.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Testing;

/// <summary>
/// The start-up marker on a real Umbraco with the composers run as a site runs them (ROADMAP 3.4c): it comes after
/// Umbraco's own populators, and a rebuild of every index ends with its line.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class StartupMarkerTests : UmbracoIntegrationTest
{
    [Test]
    public void Boot_WithTheMarkerOn_RegistersItAfterUmbracosPopulators()
    {
        var populators = GetRequiredService<IEnumerable<IIndexPopulator>>().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(populators[^1], Is.InstanceOf<StartupMarkerPopulator>());
            Assert.That(populators.OfType<StartupMarkerPopulator>().Count(), Is.EqualTo(1));
            Assert.That(populators.Select(populator => populator.GetType()), Has.Member(typeof(ContentIndexPopulator)));
            Assert.That(populators.Select(populator => populator.GetType().Name), Has.Member("DeliveryApiContentIndexPopulator"), "internal to Umbraco, so found by name");
        });
    }

    [Test]
    public async Task RebuildIndexesAsync_EveryIndex_EndsWithTheMarker()
    {
        await GetRequiredService<IIndexRebuilder>().RebuildIndexesAsync(onlyEmptyIndexes: false, TimeSpan.Zero, useBackgroundThread: false);

        var marker = GetRequiredService<LogCapture>().Entries.Where(entry => entry.Message.StartsWith(StartupMarkerPopulator.Marker, StringComparison.Ordinal)).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(marker, Has.Count.EqualTo(1));
            Assert.That(marker[0].Message, Does.Contain("InternalIndex").And.Contain("ExternalIndex").And.Contain("DeliveryApiContentIndex"));
        });
    }

    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        // Before the composers run, as the browser tests' host passes it in its environment.
        builder.Config[FormBuilderTestingOptions.SectionName + ":" + nameof(FormBuilderTestingOptions.StartupMarker)] = "true";
        builder.AddComposers();
        builder.Services.AddLogCapture();
    }
}
