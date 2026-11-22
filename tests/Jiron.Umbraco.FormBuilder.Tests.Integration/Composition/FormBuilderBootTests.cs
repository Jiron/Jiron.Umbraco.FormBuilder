using Jiron.Umbraco.FormBuilder.Core;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Composing;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Testing.People;
using Jiron.Umbraco.FormBuilder.Testing.Startup;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Jiron.Umbraco.FormBuilder.Web.Composing;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.BackgroundJobs;
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
    // What the composers registered, read as the site booted.
    private IServiceCollection? _services;

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
    public void Boot_EachServiceOfCore_ResolvesAsOneOfItsOwnPerSite()
    {
        // The form service in its parts (ADR-0053), each with its own interface and registration, beside the other services.
        Assert.Multiple(() =>
        {
            AssertOnePerSite<IFormBuilderFormService, FormBuilderFormService>();
            AssertOnePerSite<IFormBuilderFormVersionService, FormBuilderFormVersionService>();
            AssertOnePerSite<IFormBuilderFormExchangeService, FormBuilderFormExchangeService>();
            AssertOnePerSite<IFormBuilderFolderService, FormBuilderFolderService>();
            AssertOnePerSite<IFormBuilderLibraryService, FormBuilderLibraryService>();
            AssertOnePerSite<IFormBuilderAccessService, FormBuilderAccessService>();
            AssertOnePerSite<IFormBuilderSettingsService, FormBuilderSettingsService>();
            AssertOnePerSite<IFormBuilderMergeTagService, FormBuilderMergeTagService>();
            AssertOnePerSite<FormBuilderChanges<FormBuilderFormService>, FormBuilderChanges<FormBuilderFormService>>();
            AssertOnePerSite<FormBuilderChanges<FormBuilderFolderService>, FormBuilderChanges<FormBuilderFolderService>>();
        });
    }

    [Test]
    public void Boot_EveryNotificationHandlerOfJironFormBuilder_IsRegistered_ForEachNotificationItHandles()
    {
        Type[] kinds = [typeof(INotificationHandler<>), typeof(INotificationAsyncHandler<>)];
        var handlers = new[] { typeof(FormBuilderCoreComposer).Assembly, typeof(FormBuilderWebComposer).Assembly }
            .SelectMany(assembly => assembly.GetTypes())
            .Where(type => type is { IsClass: true, IsAbstract: false })
            .SelectMany(type => type.GetInterfaces()
                .Where(service => service.IsGenericType && kinds.Contains(service.GetGenericTypeDefinition()))
                .Select(service => (Service: service, Handler: type)))
            .ToList();
        var registered = _services!.Where(descriptor => !descriptor.IsKeyedService).Select(descriptor => (descriptor.ServiceType, descriptor.ImplementationType)).ToHashSet();

        Assert.Multiple(() =>
        {
            Assert.That(registered, Has.Member((typeof(INotificationHandler<UmbracoApplicationStartedNotification>), typeof(FileStoreLoadBalancingCheck))), "the start-up check of the file store's root");
            Assert.That(handlers.Where(pair => !registered.Contains(pair)).Select(pair => $"{pair.Handler.Name} for {pair.Service.GetGenericArguments()[0].Name}"), Is.Empty, "handlers no composer registers");
            Assert.That(handlers, Has.Count.GreaterThanOrEqualTo(4), "the handlers found");
        });
    }

    [Test]
    public void Boot_WithoutTheStartupMarker_RegistersNoMarkerPopulator()
    {
        Assert.That(GetRequiredService<IEnumerable<IIndexPopulator>>().OfType<StartupMarkerPopulator>(), Is.Empty);
    }

    [Test]
    public void Boot_WithoutTheStartupWorkAtOnce_KeepsUmbracosStartupRebuildAndJobs()
    {
        var handlers = _services!
            .Where(descriptor => descriptor.ServiceType == typeof(INotificationAsyncHandler<UmbracoApplicationStartedNotification>))
            .Select(descriptor => descriptor.ImplementationType)
            .ToList();
        var hosted = _services!.Where(descriptor => !descriptor.IsKeyedService && descriptor.ServiceType == typeof(IHostedService)).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(handlers, Has.Member(typeof(RebuildOnStartedHandler)), "a site rebuilds as Umbraco does");
            Assert.That(handlers, Has.No.Member(typeof(IndexRebuildAtOnceHandler)));
            Assert.That(hosted.Count(descriptor => descriptor.ImplementationType == typeof(DistributedBackgroundJobHostedService)), Is.EqualTo(1), "a site runs Umbraco's jobs as Umbraco does");
            Assert.That(_services!.Any(descriptor => descriptor.ServiceType == typeof(UmbracoJobsAfterSeeding)), Is.False);
            Assert.That(GetRequiredService<IOptionsMonitor<DistributedJobSettings>>().CurrentValue.Delay, Is.EqualTo(TimeSpan.FromMinutes(1)), "and with Umbraco's delay");
        });
    }

    [Test]
    public void Boot_WithoutTheStrictCsp_RegistersNoNonceProvider()
    {
        Assert.That(Services.GetService<IFormBuilderCspNonceProvider>(), Is.Null, "a site has none by default, and the test host's policy is off");
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
        _services = builder.Services;

        // The harness leaves out Umbraco's background jobs; this is how Umbraco's own registration adds their service
        // (IndexRebuildAtOnceHandlerTests reads it from Umbraco), so the composers can be seen to leave it.
        builder.Services.AddHostedService<DistributedBackgroundJobHostedService>();
        builder.AddComposers();
        builder.Services.AddLogCapture();
    }

    private void AssertOnePerSite<TService, TImplementation>()
        where TService : class
    {
        var first = GetRequiredService<TService>();
        Assert.That(first, Is.TypeOf<TImplementation>(), typeof(TService).Name);
        Assert.That(GetRequiredService<TService>(), Is.SameAs(first), typeof(TService).Name + " is one per site");
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Captured {Number}")]
    private static partial void LogCaptured(ILogger logger, int number);
}
