using Jiron.Umbraco.FormBuilder.Core;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
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
        // A test asks far more of one address than any visitor does; a test that holds a rate limit names its own below.
        foreach (var policy in new[] { "Read", "Challenge", "Submit", "Upload", "Events", "Verify" })
        {
            foreach (var limit in new[] { "PerAddressPerMinute", "PerAddressPerHour", "PerAddressAndFormPerMinute", "PerFormPerMinute", "SitePerSecond" })
            {
                InMemoryConfiguration[$"{FormBuilderOptions.SectionName}:Limits:{policy}:{limit}"] = "0";
            }
        }

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
    /// <summary>Whether each test starts with the migration plan run; a test of what happens while it is pending says no.</summary>
    protected virtual bool RunsTheMigrations => true;

    [SetUp]
    public async Task MigrateAsync()
    {
        if (!RunsTheMigrations)
        {
            return;
        }

        var results = await GetRequiredService<PackageMigrationRunner>().RunPackagePlansAsync([FormBuilderConstants.MigrationPlanName]);
        Assert.That(results.Single().Successful, Is.True, results.Single().Exception?.ToString());
    }
}
