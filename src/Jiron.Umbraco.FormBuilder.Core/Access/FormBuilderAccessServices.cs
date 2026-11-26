using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace Jiron.Umbraco.FormBuilder.Core.Access;

/// <summary>The registrations of the permission model (ADR-0013, ADR-0048), which the core composer makes.</summary>
internal static class FormBuilderAccessServices
{
    /// <summary>
    /// Registers the access service, the snapshot's cache and its refresher, and the handlers that follow Umbraco's user
    /// groups: a deleted group takes its grants with it, and a saved one drops the snapshot, as host grants name groups
    /// by alias.
    /// </summary>
    public static IUmbracoBuilder AddFormBuilderAccess(this IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton<AccessSnapshotInvalidator>();
        builder.Services.TryAddSingleton<IFormBuilderAccessService, FormBuilderAccessService>();
        builder.CacheRefreshers().Add<FormBuilderAccessCacheRefresher>();
        builder.AddNotificationAsyncHandler<UserGroupDeletedNotification, FormBuilderUserGroupHandler>();
        builder.AddNotificationHandler<UserGroupSavedNotification, FormBuilderUserGroupHandler>();
        return builder;
    }
}

/// <summary>
/// Follows Umbraco's user groups (ADR-0048): a deleted group's grants go, and any change drops the snapshot. Before the
/// package's tables are there, a group has no grants to take away (27.1).
/// </summary>
internal sealed class FormBuilderUserGroupHandler(IFormBuilderAccessService access, AccessSnapshotInvalidator snapshots, FormBuilderTablesCheck tables)
    : INotificationAsyncHandler<UserGroupDeletedNotification>, INotificationHandler<UserGroupSavedNotification>
{
    /// <inheritdoc />
    public async Task HandleAsync(UserGroupDeletedNotification notification, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        if (!await tables.AreThereAsync(cancellationToken))
        {
            snapshots.Changed();
            return;
        }

        foreach (var group in notification.DeletedEntities)
        {
            await access.RemoveGroupAsync(group.Key, cancellationToken);
        }
    }

    /// <inheritdoc />
    public void Handle(UserGroupSavedNotification notification) => snapshots.Changed();
}
