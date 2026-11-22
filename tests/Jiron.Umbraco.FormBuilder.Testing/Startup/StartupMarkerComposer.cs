using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Infrastructure.BackgroundJobs;
using Umbraco.Cms.Infrastructure.Examine;
using Umbraco.Extensions;

namespace Jiron.Umbraco.FormBuilder.Testing.Startup;

/// <summary>
/// The test host's start-up work, each piece only when the host turns it on:
/// <list type="bullet">
/// <item><see cref="StartupMarkerPopulator"/> (<c>JironFormBuilderTesting:StartupMarker</c>). Umbraco registers its index
/// populators in <see cref="AddExamineComposer"/>, and composers without an order between them run in any order, so this
/// one runs after it: that puts the marker after every populator Umbraco registers.</item>
/// <item>Umbraco's two pieces of start-up work as soon as the host's own seeding has finished
/// (<c>JironFormBuilderTesting:StartupWorkAtOnce</c>): <see cref="IndexRebuildAtOnceHandler"/> in place of Umbraco's
/// start-up rebuild, which <see cref="AddExamineComposer"/> registers too, and <see cref="UmbracoJobsAfterSeeding"/>
/// in place of Umbraco's hosted service of the distributed jobs, with no delay before the jobs. Both wait for
/// <see cref="StartupSeeding"/>, by order and not by time. When either of Umbraco's is not there to take out, the host
/// stops at start-up and says so: Umbraco's own would then run a minute later, while the tests do.</item>
/// </list>
/// </summary>
[ComposeAfter(typeof(AddExamineComposer))]
public sealed class StartupMarkerComposer : IComposer
{
    /// <inheritdoc />
    public void Compose(IUmbracoBuilder builder)
    {
        var section = builder.Config.GetSection(FormBuilderTestingOptions.SectionName);
        if (section.GetValue<bool>(nameof(FormBuilderTestingOptions.StartupMarker)))
        {
            builder.Services.AddSingleton<IIndexPopulator, StartupMarkerPopulator>();
        }

        if (section.GetValue<bool>(nameof(FormBuilderTestingOptions.StartupWorkAtOnce)))
        {
            ReplaceUmbracosStartupRebuild(builder);
            StartUmbracosJobsAfterSeeding(builder);
        }
    }

    /// <summary>
    /// Takes Umbraco's handler of the started notification that rebuilds the indexes after a minute out of the services,
    /// and registers <see cref="IndexRebuildAtOnceHandler"/> instead.
    /// </summary>
    /// <exception cref="InvalidOperationException">Umbraco's handler is not registered.</exception>
    public static void ReplaceUmbracosStartupRebuild(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var umbracos = builder.Services
            .Where(descriptor => !descriptor.IsKeyedService
                && descriptor.ServiceType == typeof(INotificationAsyncHandler<UmbracoApplicationStartedNotification>)
                && descriptor.ImplementationType == typeof(RebuildOnStartedHandler))
            .ToList();
        foreach (var descriptor in umbracos)
        {
            builder.Services.Remove(descriptor);
        }

        if (umbracos.Count == 0)
        {
            throw new InvalidOperationException(
                $"{nameof(FormBuilderTestingOptions.StartupWorkAtOnce)} is on, but Umbraco's start-up rebuild ({nameof(RebuildOnStartedHandler)}) is not registered, so it cannot be "
                + "replaced; a rebuild of Umbraco's own could then run a minute after the start, while the tests do.");
        }

        builder.Services.AddStartupSeeding();
        builder.Services.AddSingleton<IndexRebuildOnce>();
        builder.AddNotificationAsyncHandler<UmbracoApplicationStartedNotification, IndexRebuildAtOnceHandler>();
    }

    /// <summary>
    /// Takes Umbraco's hosted service of the distributed jobs out of the services and registers
    /// <see cref="UmbracoJobsAfterSeeding"/> in its place, which starts an instance of Umbraco's once the seeding has
    /// finished; the jobs then start with no delay of their own.
    /// </summary>
    /// <exception cref="InvalidOperationException">Umbraco's hosted service is not registered.</exception>
    public static void StartUmbracosJobsAfterSeeding(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Umbraco registers it with AddHostedService<DistributedBackgroundJobHostedService>(): a singleton of
        // IHostedService by its type.
        var services = builder.Services;
        var index = -1;
        for (var i = services.Count - 1; i >= 0; i--)
        {
            var descriptor = services[i];
            if (!descriptor.IsKeyedService
                && descriptor.ServiceType == typeof(IHostedService)
                && descriptor.ImplementationType == typeof(DistributedBackgroundJobHostedService))
            {
                services.RemoveAt(i);
                index = i;
            }
        }

        if (index < 0)
        {
            throw new InvalidOperationException(
                $"{nameof(FormBuilderTestingOptions.StartupWorkAtOnce)} is on, but Umbraco's hosted service of the distributed jobs ({nameof(DistributedBackgroundJobHostedService)}) "
                + "is not registered as a hosted service by its type, so it cannot wait for the seeding; its jobs could then run beside it.");
        }

        services.AddStartupSeeding();
        services.AddSingleton(provider => new UmbracoJobsAfterSeeding(
            provider.GetRequiredService<StartupSeeding>(),
            () => ActivatorUtilities.CreateInstance<DistributedBackgroundJobHostedService>(provider),
            provider.GetRequiredService<ILogger<UmbracoJobsAfterSeeding>>()));

        // In Umbraco's place among the hosted services, so it starts and stops where Umbraco's did.
        services.Insert(index, ServiceDescriptor.Singleton<IHostedService>(provider => provider.GetRequiredService<UmbracoJobsAfterSeeding>()));
        services.PostConfigure<DistributedJobSettings>(settings => settings.Delay = TimeSpan.Zero);
    }
}
