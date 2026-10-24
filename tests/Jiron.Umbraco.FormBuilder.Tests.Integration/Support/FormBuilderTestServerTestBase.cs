using Jiron.Umbraco.FormBuilder.Core;
using Umbraco.Cms.Infrastructure.Install;
using Umbraco.Cms.Tests.Integration.TestServerTest;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Support;

/// <summary>
/// Umbraco's test server with a hook for the site's configuration, which must be set before each test's host is built
/// (tests/README.md). NUnit runs set-ups from the base class down, so a fixture's own set-up would run after the host
/// exists.
/// </summary>
public abstract class ConfiguredTestServerTestBase : UmbracoTestServerTestBase
{
    /// <summary>Adds the test's configuration, then builds the host.</summary>
    [SetUp]
    public override void Setup()
    {
        ConfigureHost(InMemoryConfiguration);
        base.Setup();
    }

    /// <summary>Configuration for the host about to be built; called before every test.</summary>
    protected virtual void ConfigureHost(IDictionary<string, string> configuration)
    {
    }
}

/// <summary>
/// Umbraco's test server with Jiron Form Builder's migration plan run, as a site has it after installing the package.
/// The test hosts keep unattended package migrations off (appsettings.Tests.json), so each test runs the plan through
/// Umbraco's own package migration runner; a fixture boots the runtime (<c>[UmbracoTest(Boot = true)]</c>) so that the
/// Management API answers. NUnit runs this set-up after the host's and before a derived fixture's own.
/// </summary>
public abstract class FormBuilderTestServerTestBase : ConfiguredTestServerTestBase
{
    /// <summary>Runs Jiron Form Builder's plan.</summary>
    [SetUp]
    public async Task MigrateAsync()
    {
        var results = await GetRequiredService<PackageMigrationRunner>().RunPackagePlansAsync([FormBuilderConstants.MigrationPlanName]);
        Assert.That(results.Single().Successful, Is.True, results.Single().Exception?.ToString());
    }
}
