using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Testing;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Tests.Common.Testing;
using Umbraco.Cms.Tests.Integration.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Configuration;

/// <summary>
/// A real Umbraco site with host configuration (ADR-0042): the composers bind <c>JironFormBuilder</c> once, check it and
/// the runtime when the site starts, and the definition reader takes the limits the site configured.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class HostOptionsBootTests : UmbracoIntegrationTest
{
    /// <summary>The host configuration of every test's site, as a host passes it in its appsettings.json or environment.</summary>
    public HostOptionsBootTests()
    {
        InMemoryConfiguration[FormBuilderOptions.SectionName + ":Limits:Definitions:MaxPages"] = "1";
        InMemoryConfiguration[FormBuilderOptions.SectionName + ":LockedSections:0"] = "privacy";
    }

    private static string Full => File.ReadAllText(RepositoryRoot.Path(typeof(HostOptionsBootTests).Assembly, "tests", "fixtures", "definitions", "v1-full.json"));

    [Test]
    public void ASiteWithALoweredLimit_RefusesADefinitionTheDefaultAllows_AndStillReadsAStoredOne()
    {
        var serializer = GetRequiredService<FormDefinitionSerializer>();

        Assert.Multiple(() =>
        {
            Assert.That(GetRequiredService<DefinitionLimits>(), Is.EqualTo(new DefinitionLimits { MaxPages = 1 }));
            Assert.That(serializer.Read(Full).Problems.Select(p => (p.Code, p.Path)), Is.EqualTo(new[] { (DefinitionErrors.LimitExceeded, "$.pages") }));
            Assert.That(serializer.ReadStored(Full).Problems, Is.Empty);
        });
    }

    [Test]
    public void TheSite_BindsItsConfigurationOnce_AndChecksItAndTheRuntimeWhenItStarts()
    {
        var options = GetRequiredService<IOptions<FormBuilderOptions>>().Value;

        Assert.Multiple(() =>
        {
            Assert.That(options.LockedSections, Is.EqualTo(new[] { "privacy" }));
            Assert.That(GetRequiredService<IEnumerable<IValidateOptions<FormBuilderOptions>>>().Single(), Is.InstanceOf<FormBuilderOptionsValidator>());
            Assert.That(GetRequiredService<IEnumerable<IValidateOptions<FormBuilderStartupChecks>>>().Single(), Is.InstanceOf<GlobalizationStartupCheck>());
            Assert.That(() => GetRequiredService<IStartupValidator>().Validate(), Throws.Nothing);
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder) => builder.AddComposers();
}
