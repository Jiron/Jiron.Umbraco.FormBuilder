using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Infrastructure.Examine;
using Umbraco.Extensions;

namespace Jiron.Umbraco.FormBuilder.Testing.Startup;

/// <summary>
/// The test host's start-up work, each piece only when the host turns it on:
/// <list type="bullet">
/// <item><see cref="StartupMarkerPopulator"/> (<c>JironFormBuilderTesting:StartupMarker</c>). Umbraco registers its index
/// populators in <see cref="AddExamineComposer"/>, and composers without an order between them run in any order, so this
/// one runs after it: that puts the marker after every populator Umbraco registers.</item>
/// <item><see cref="IndexRebuildAtOnceHandler"/> in place of Umbraco's start-up rebuild
/// (<c>JironFormBuilderTesting:IndexRebuildAtOnce</c>), which <see cref="AddExamineComposer"/> registers too. When
/// Umbraco's handler is not there to take out, the host stops at start-up and says so: a rebuild of Umbraco's of its
/// own a minute later would run while the tests do.</item>
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

        if (section.GetValue<bool>(nameof(FormBuilderTestingOptions.IndexRebuildAtOnce)))
        {
            ReplaceUmbracosStartupRebuild(builder);
        }
    }

    /// <summary>
    /// Takes Umbraco's handler of the started notification that rebuilds the indexes after a minute out of the services,
    /// and registers <see cref="IndexRebuildAtOnceHandler"/> instead.
    /// </summary>
    /// <exception cref="InvalidOperationException">Umbraco's handler is not registered.</exception>
    public static void ReplaceUmbracosStartupRebuild(IUmbracoBuilder builder)
    {
        var umbracos = builder.Services
            .Where(descriptor => descriptor.ServiceType == typeof(INotificationAsyncHandler<UmbracoApplicationStartedNotification>)
                && descriptor.ImplementationType == typeof(RebuildOnStartedHandler))
            .ToList();
        foreach (var descriptor in umbracos)
        {
            builder.Services.Remove(descriptor);
        }

        if (umbracos.Count == 0)
        {
            throw new InvalidOperationException(
                $"{nameof(FormBuilderTestingOptions.IndexRebuildAtOnce)} is on, but Umbraco's start-up rebuild ({nameof(RebuildOnStartedHandler)}) is not registered, so it cannot be "
                + "replaced; a rebuild of Umbraco's own could then run a minute after the start, while the tests do.");
        }

        builder.Services.AddSingleton<IndexRebuildOnce>();
        builder.AddNotificationAsyncHandler<UmbracoApplicationStartedNotification, IndexRebuildAtOnceHandler>();
    }
}
