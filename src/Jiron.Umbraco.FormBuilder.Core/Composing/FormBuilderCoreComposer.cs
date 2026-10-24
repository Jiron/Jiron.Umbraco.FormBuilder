using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Upgrades;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Queries;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Infrastructure.Migrations.Notifications;
using Umbraco.Extensions;

namespace Jiron.Umbraco.FormBuilder.Core.Composing;

/// <summary>
/// Registers Jiron Form Builder's core services with Umbraco, which discovers this composer automatically: the
/// database migration plan (ADR-0033), the pending-migration state, the definition services and the persistence (ADR-0036).
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

        // The one way to the database (ADR-0007, ADR-0036): the gate, the unit of work and the repositories. All are
        // stateless apart from the gate, which must be one per process.
        builder.Services.TryAddSingleton<IFormBuilderDatabaseGate, FormBuilderDatabaseGate>();
        builder.Services.TryAddSingleton<FormBuilderDatabase>();
        builder.Services.TryAddSingleton<IKeyValueRepository, KeyValueRepository>();
        builder.Services.TryAddSingleton<IFolderRepository, FolderRepository>();
        builder.Services.TryAddSingleton<IFormRepository, FormRepository>();
        builder.Services.TryAddSingleton<ISettingsRepository, SettingsRepository>();
        builder.Services.TryAddSingleton<IOptionListRepository, OptionListRepository>();
        builder.Services.TryAddSingleton<IThemeRepository, ThemeRepository>();
        builder.Services.TryAddSingleton<IEmailLayoutRepository, EmailLayoutRepository>();
        builder.Services.TryAddSingleton<IFormTemplateRepository, FormTemplateRepository>();
        builder.Services.TryAddSingleton<IPermissionRepository, PermissionRepository>();
        builder.Services.TryAddSingleton<IEntryRepository, EntryRepository>();
        builder.Services.TryAddSingleton<IEntryEventRepository, EntryEventRepository>();
        builder.Services.TryAddSingleton<IEntryNoteRepository, EntryNoteRepository>();
        builder.Services.TryAddSingleton<IEntryQueryRepository, EntryQueryRepository>();
        builder.Services.TryAddSingleton<IActionRunRepository, ActionRunRepository>();
        builder.Services.TryAddSingleton<IEmailRepository, EmailRepository>();
    }
}
