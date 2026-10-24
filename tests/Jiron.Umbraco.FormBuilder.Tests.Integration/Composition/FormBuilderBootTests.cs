using Jiron.Umbraco.FormBuilder.Core.Composing;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Testing.People;
using Jiron.Umbraco.FormBuilder.Testing.Startup;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Jiron.Umbraco.FormBuilder.Web.Composing;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Examine;
using Umbraco.Cms.Tests.Common.Testing;
using Umbraco.Cms.Tests.Integration.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Composition;

/// <summary>
/// Boots a real Umbraco on a fresh database with Jiron Form Builder's composers, found and run the way a site runs
/// them (<c>AddComposers</c>), with the Testing project's clock turned on (ROADMAP 3.4a).
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed partial class FormBuilderBootTests : UmbracoIntegrationTest
{
    /// <summary>The configuration of every test's site, as a host passes it in its environment.</summary>
    public FormBuilderBootTests() => InMemoryConfiguration[FormBuilderTestingOptions.SectionName + ":Clock"] = "true";

    [Test]
    public void Boot_WithTheComposers_ReachesTheRunLevel()
    {
        Assert.That(GetRequiredService<IRuntimeState>().Level, Is.EqualTo(RuntimeLevel.Run));
    }

    [Test]
    public void Boot_TypeLoader_FindsEveryComposerOfJironFormBuilder()
    {
        var composers = GetRequiredService<TypeLoader>().GetTypes<IComposer>().ToList();

        Assert.That(composers, Is.SupersetOf(new[] { typeof(FormBuilderCoreComposer), typeof(FormBuilderWebComposer), typeof(FormBuilderTestingComposer), typeof(StartupMarkerComposer) }));
    }

    [Test]
    public void Boot_TheTestingComposer_RegisteredItsServicesAndTheClock()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GetRequiredService<TestPeople>(), Is.Not.Null);
            Assert.That(GetRequiredService<TimeProvider>(), Is.SameAs(GetRequiredService<TestClock>()));
        });
    }

    [Test]
    public void Boot_WithoutTheStartupMarker_RegistersNoMarkerPopulator()
    {
        Assert.That(GetRequiredService<IEnumerable<IIndexPopulator>>().OfType<StartupMarkerPopulator>(), Is.Empty);
    }

    [Test]
    public void Boot_LogCapture_ReceivesWhatAServiceLogs()
    {
        LogCaptured(GetRequiredService<ILogger<FormBuilderBootTests>>(), 42);

        var entry = GetRequiredService<LogCapture>().Entries.Single(log => log.Category == typeof(FormBuilderBootTests).FullName);
        Assert.Multiple(() =>
        {
            Assert.That(entry.Level, Is.EqualTo(LogLevel.Warning));
            Assert.That(entry.Message, Is.EqualTo("Captured 42"));
        });
    }

    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        builder.AddComposers();
        builder.Services.AddLogCapture();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Captured {Number}")]
    private static partial void LogCaptured(ILogger logger, int number);
}
