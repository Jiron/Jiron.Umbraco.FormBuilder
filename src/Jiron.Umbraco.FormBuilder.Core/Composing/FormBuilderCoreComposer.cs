using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Upgrades;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Core.Outbound;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Queries;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Retention;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Jiron.Umbraco.FormBuilder.Core.Themes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Infrastructure.BackgroundJobs;
using Umbraco.Cms.Infrastructure.Migrations.Notifications;
using Umbraco.Extensions;

namespace Jiron.Umbraco.FormBuilder.Core.Composing;

/// <summary>
/// Registers Jiron Form Builder's core services with Umbraco, which discovers this composer automatically: the host
/// configuration and the start-up checks (ADR-0042), the database migration plan (ADR-0033), the pending-migration
/// state, the definition services, the persistence (ADR-0036), the file store (ADR-0041), the backoffice settings
/// (ADR-0043), the permission model (ADR-0048), the field types (ADR-0055), option lists (ADR-0073), the form service
/// (ADR-0044), entries' answers (ADR-0070) and the retention job (ADR-0045).
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

        // The themes (ADR-0105): the collection with the built-in ones, its registry and the check of both at start.
        builder.AddFormBuilderThemes();

        // Merge tags (ADR-0065): the site's own sources and filters beside the built-in ones, the culture formats and the
        // service, with the check of the site's names at start.
        builder.AddFormBuilderMergeTags();

        // The form service (ADR-0044): create, save, publish and the other changes, with their notifications.
        builder.AddFormBuilderForms();

        // Calls to other hosts (ADR-0076): the transport, which connects only to globally reachable addresses, and the
        // calls over it, held to the host configuration's hosts and schemes.
        builder.Services.AddFormBuilderOutbound();

        // Option lists (ADR-0073): the option sources with the standard lists, the kinds, the library's lists kept until
        // they change, the option service and the check of a field's source when a form is published, which runs after
        // the structural validation, as the checks run in the order they are registered.
        builder.AddFormBuilderOptionLists();

        // Entries' answers (ADR-0070, ADR-0072): the reader of a submission's answers over its form, the consent
        // wordings, and the store that writes the rows and reads them back typed.
        builder.AddFormBuilderEntries();

        // Uploads (ADR-0087): a file checked against its field and the host's limits and stored before its submission,
        // over the field types, the forms, the entries' files and the file store.
        builder.Services.TryAddSingleton<WaitingUploads>();
        builder.Services.TryAddSingleton<FileUploads>();

        // The submission pipeline (ADR-0088): when a form takes entries, the outcomes, the transaction that stores an entry
        // with its event, its action runs and its count, and the pipeline over the forms, the answers and the entries.
        builder.Services.TryAddSingleton<FormAvailability>();
        builder.Services.TryAddSingleton<SubmissionOutcomes>();
        builder.Services.TryAddSingleton<OptionPlaces>();
        builder.Services.TryAddSingleton<SubmissionProtection>();
        builder.Services.TryAddSingleton<EntrySpamFolder>();
        _ = builder.JironFormBuilder().SpamChecks();
        _ = builder.JironFormBuilder().CaptchaProviders();
        _ = builder.JironFormBuilder().FileScanners();
        builder.Services.TryAddSingleton<FileScans>();
        builder.Services.TryAddSingleton<EntryFileDownloads>();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IRecurringBackgroundJob, FileScanJob>());
        builder.Services.TryAddSingleton<CaptchaHosts>();
        builder.Services.TryAddSingleton<UniqueAnswers>();
        builder.Services.TryAddSingleton<SubmissionEntries>();
        builder.Services.TryAddSingleton<FormSubmissions>();

        // The retention job (ADR-0045): the draft-version cleanup first.
        builder.AddFormBuilderRetention();

        // The action framework (ADR-0007, ADR-0158): the action types, the worker with its job, and what settles an entry.
        builder.AddFormBuilderActions();
    }
}
