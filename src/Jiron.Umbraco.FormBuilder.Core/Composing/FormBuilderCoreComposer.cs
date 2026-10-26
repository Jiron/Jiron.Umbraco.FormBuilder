using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Upgrades;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Queries;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Retention;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Infrastructure.Migrations.Notifications;
using Umbraco.Extensions;

namespace Jiron.Umbraco.FormBuilder.Core.Composing;

/// <summary>
/// Registers Jiron Form Builder's core services with Umbraco, which discovers this composer automatically: the host
/// configuration and the start-up checks (ADR-0042), the database migration plan (ADR-0033), the pending-migration
/// state, the definition services, the persistence (ADR-0036), the file store (ADR-0041), the backoffice settings
/// (ADR-0043), the permission model (ADR-0048), the field types (ADR-0055), the form service (ADR-0044), entries'
/// answers (ADR-0070) and the retention job (ADR-0045).
/// </summary>
public sealed class FormBuilderCoreComposer : IComposer
{
    /// <inheritdoc />
    public void Compose(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton(TimeProvider.System);

        // The host configuration, checked with the runtime when the site starts (ADR-0042).
        builder.Services.AddFormBuilderOptions(builder.Config);

        // Umbraco also finds the plan by type scanning; its collection keeps one of each type.
        builder.PackageMigrationPlans().Add<FormBuilderMigrationPlan>();
        builder.Services.TryAddSingleton<IFormBuilderMigrationState, FormBuilderMigrationState>();
        builder.AddNotificationHandler<MigrationPlansExecutedNotification, FormBuilderMigrationsExecutedHandler>();

        // Form definitions (ADR-0002, ADR-0034): the upgrader chain (no upgraders while v1 is the only schema) and the
        // reader and writer, with the limits of the host configuration.
        builder.Services.TryAddSingleton<FormDefinitionUpgrades>();
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
        builder.Services.TryAddSingleton<IConsentTextRepository, ConsentTextRepository>();
        builder.Services.TryAddSingleton<IEntryEventRepository, EntryEventRepository>();
        builder.Services.TryAddSingleton<IEntryNoteRepository, EntryNoteRepository>();
        builder.Services.TryAddSingleton<IEntryQueryRepository, EntryQueryRepository>();
        builder.Services.TryAddSingleton<IActionRunRepository, ActionRunRepository>();
        builder.Services.TryAddSingleton<IEmailRepository, EmailRepository>();
        builder.Services.TryAddSingleton<IAuditRepository, AuditRepository>();
        builder.Services.TryAddSingleton<IDailyStatRepository, DailyStatRepository>();
        builder.Services.TryAddSingleton<IReferenceRepository, ReferenceRepository>();

        // The steps every change of the services takes (ADR-0053), one per service, so each logs as its service.
        builder.Services.TryAddSingleton(typeof(FormBuilderChanges<>));

        // The file store (ADR-0006, ADR-0041): the physical default, whose root the host configuration checks, and the
        // check that a load-balanced site does not keep it on each server's own disk.
        builder.Services.AddFormBuilderFiles();
        builder.AddNotificationHandler<UmbracoApplicationStartedNotification, FileStoreLoadBalancingCheck>();

        // The backoffice settings (ADR-0008, ADR-0043): the sections, the cached reader, the save and the refresher.
        builder.AddFormBuilderSettings();

        // The permission model (ADR-0013, ADR-0048): the evaluation with its cached snapshot, the grants and restrictions.
        builder.AddFormBuilderAccess();

        // The field types (ADR-0055): the collection with the built-in ones, its registry and the check of both at start.
        builder.AddFormBuilderFieldTypes();

        // Merge tags (ADR-0065): the site's own sources and filters beside the built-in ones, the culture formats and the
        // service, with the check of the site's names at start.
        builder.AddFormBuilderMergeTags();

        // The form service (ADR-0044): create, save, publish and the other changes, with their notifications.
        builder.AddFormBuilderForms();

        // Entries' answers (ADR-0070): the consent wordings, and the store that writes the rows and reads them back typed.
        builder.AddFormBuilderEntries();

        // The retention job (ADR-0045): the draft-version cleanup so far.
        builder.AddFormBuilderRetention();
    }
}
