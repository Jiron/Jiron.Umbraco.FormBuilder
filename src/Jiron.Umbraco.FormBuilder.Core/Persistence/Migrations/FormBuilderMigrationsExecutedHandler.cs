using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Infrastructure.Migrations.Notifications;
using Umbraco.Extensions;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;

/// <summary>
/// After Jiron Form Builder's plan ran: forgets the remembered migration state, and refreshes the user group cache on
/// every server when a step ran, because the section grant (step 3) writes user group data directly.
/// </summary>
internal sealed class FormBuilderMigrationsExecutedHandler(IFormBuilderMigrationState migrationState, DistributedCache distributedCache)
    : INotificationHandler<MigrationPlansExecutedNotification>
{
    /// <inheritdoc />
    public void Handle(MigrationPlansExecutedNotification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);
        var ours = notification.ExecutedPlans.Where(plan => plan.Plan.Name == FormBuilderConstants.MigrationPlanName).ToList();
        if (ours.Count == 0)
        {
            return;
        }

        migrationState.Invalidate();
        if (ours.Any(plan => plan.CompletedTransitions.Count > 0))
        {
            distributedCache.RefreshAllUserGroupCache();
        }
    }
}
