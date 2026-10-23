using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Upgrades;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Infrastructure.Migrations.Notifications;
using Umbraco.Extensions;

namespace Jiron.Umbraco.FormBuilder.Core.Composing;

/// <summary>
/// Registers Jiron Form Builder's core services with Umbraco, which discovers this composer automatically: the
/// database migration plan (ADR-0033) and the pending-migration state.
/// </summary>
public sealed class FormBuilderCoreComposer : IComposer
{
    /// <inheritdoc />
    public void Compose(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton(TimeProvider.System);

        // Umbraco also finds the plan by type scanning; its collection keeps one of each type.
        builder.PackageMigrationPlans().Add<FormBuilderMigrationPlan>();
        builder.Services.TryAddSingleton<IFormBuilderMigrationState, FormBuilderMigrationState>();
        builder.AddNotificationHandler<MigrationPlansExecutedNotification, FormBuilderMigrationsExecutedHandler>();

        // Form definitions (ADR-0002, ADR-0034): the upgrader chain (no upgraders while v1 is the only schema), the
        // limits (bound from host configuration in 4.4) and the reader and writer.
        builder.Services.TryAddSingleton<FormDefinitionUpgrades>();
        builder.Services.TryAddSingleton(new DefinitionLimits());
        builder.Services.TryAddSingleton<FormDefinitionSerializer>();
    }
}
