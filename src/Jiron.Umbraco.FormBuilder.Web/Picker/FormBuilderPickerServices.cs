using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Infrastructure.BackgroundJobs;

namespace Jiron.Umbraco.FormBuilder.Web.Picker;

/// <summary>
/// The registrations of "used on" (ADR-0017, ADR-0131), which the web composer makes: the form's UDI type, the picker's
/// reference factory, the index with its content handlers, the reader, and the rebuild with its job. The picker's data
/// editor and value converter are found by Umbraco itself.
/// </summary>
internal static class FormBuilderPickerServices
{
    /// <summary>Registers "used on". Safe to call more than once.</summary>
    public static IUmbracoBuilder AddFormBuilderPicker(this IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        FormPickerReferences.RegisterUdiType();
        if (!builder.DataValueReferenceFactories().Has<FormPickerReferences>())
        {
            builder.DataValueReferenceFactories().Append<FormPickerReferences>();
        }

        builder.Services.TryAddSingleton<FormReferenceIndex>();
        builder.Services.TryAddSingleton<FormUsage>();
        builder.Services.TryAddSingleton<FormReferenceRebuild>();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IDistributedBackgroundJob, FormReferenceRebuildJob>());
        builder.AddNotificationAsyncHandler<ContentSavedNotification, FormReferenceHandler>();
        builder.AddNotificationAsyncHandler<ContentPublishedNotification, FormReferenceHandler>();
        builder.AddNotificationAsyncHandler<ContentUnpublishedNotification, FormReferenceHandler>();
        builder.AddNotificationAsyncHandler<ContentMovedToRecycleBinNotification, FormReferenceHandler>();
        builder.AddNotificationAsyncHandler<ContentMovedNotification, FormReferenceHandler>();
        builder.AddNotificationAsyncHandler<ContentCopiedNotification, FormReferenceHandler>();
        builder.AddNotificationAsyncHandler<ContentDeletedNotification, FormReferenceHandler>();
        return builder;
    }
}
