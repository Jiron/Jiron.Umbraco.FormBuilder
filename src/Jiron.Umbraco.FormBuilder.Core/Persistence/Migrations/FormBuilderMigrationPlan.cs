using Umbraco.Cms.Core.Packaging;
using Umbraco.Cms.Infrastructure.Migrations;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;

/// <summary>
/// Jiron Form Builder's database migrations (section 4.5, ADR-0003, ADR-0033): one package plan, run once per database by
/// Umbraco's package migrations, each step atomically with its state. Every step is idempotent all the same, so a
/// rerun after a manual run or a restored backup is harmless. States are never renamed or reused, and a step that has
/// shipped is never edited: every later change is a new step.
/// </summary>
internal sealed class FormBuilderMigrationPlan : PackageMigrationPlan
{
    /// <summary>Step 1: every table of schema v1 (section 4.5).</summary>
    public const string StateCreateSchema = "jfb-001-create-schema";

    /// <summary>Step 2: the global row of each backoffice settings section (ADR-0008).</summary>
    public const string StateDefaultSettings = "jfb-002-default-settings";

    /// <summary>Step 3: the Forms section for Umbraco's administrators, once (ADR-0013).</summary>
    public const string StateGrantSection = "jfb-003-grant-forms-section";

    /// <summary>Step 4: a rebuild of the "used on" index, asked for once (ADR-0131).</summary>
    public const string StateRequestReferenceRebuild = "jfb-004-request-reference-rebuild";

    /// <summary>The state after the index of the files that wait for a scan (ADR-0144).</summary>
    public const string StateFileScanIndex = "jfb-005-file-scan-index";

    /// <summary>The state after that index also tells an entry's files from uploads (ADR-0148).</summary>
    public const string StateFileScanOrder = "jfb-006-file-scan-order";

    /// <summary>The state after the index that counts the last hour's mail to visitors (ADR-0159).</summary>
    public const string StateEmailTimeIndex = "jfb-007-email-time-index";

    /// <summary>The state after an entry's event can say when something is left to do (ADR-0162).</summary>
    public const string StateEntryEventDue = "jfb-008-entry-event-due";

    /// <summary>The state after the index that finds the newest entries of every form (ADR-0165).</summary>
    public const string StateEntryTimeIndex = "jfb-009-entry-time-index";

    /// <summary>The state after a run keeps when its latest attempt started, with the index of an action's runs (ADR-0195).</summary>
    public const string StateActionRunLastAttempt = "jfb-010-action-run-last-attempt";

    /// <summary>The state after the index that finds the entries nobody opened yet across forms (ADR-0209).</summary>
    public const string StateEntryUnseenIndex = "jfb-011-entry-unseen-index";

    /// <summary>The state after the table of what of the library each form uses (ADR-0214).</summary>
    public const string StateFormLibraryUses = "jfb-012-form-library-uses";

    /// <summary>The steps in order, with the state each one leaves. The tests build their copies of the plan from it.</summary>
    internal static readonly IReadOnlyList<(string State, Type Step)> Steps =
    [
        (StateCreateSchema, typeof(CreateSchemaV1)),
        (StateDefaultSettings, typeof(CreateDefaultSettingsV1)),
        (StateGrantSection, typeof(GrantFormsSectionV1)),
        (StateRequestReferenceRebuild, typeof(RequestReferenceRebuildV1)),
        (StateFileScanIndex, typeof(AddFileScanIndexV1)),
        (StateFileScanOrder, typeof(ChangeFileScanIndexV1)),
        (StateEmailTimeIndex, typeof(AddEmailTimeIndexV1)),
        (StateEntryEventDue, typeof(AddEntryEventDueV1)),
        (StateEntryTimeIndex, typeof(AddEntryTimeIndexV1)),
        (StateActionRunLastAttempt, typeof(AddActionRunLastAttemptV1)),
        (StateEntryUnseenIndex, typeof(AddEntryUnseenIndexV1)),
        (StateFormLibraryUses, typeof(AddFormLibraryUsesV1)),
    ];

    /// <summary>Creates the plan with Jiron Form Builder's package id, name and plan name.</summary>
    public FormBuilderMigrationPlan()
        : base(FormBuilderConstants.PackageId, FormBuilderConstants.PackageName, FormBuilderConstants.MigrationPlanName)
    {
    }

    /// <summary>
    /// Always false: an upgrade runs only the steps after the stored state (ADR-0003). Explicit, so the choice is
    /// visible and survives a change of the base class.
    /// </summary>
    public override bool IgnoreCurrentState => false;

    /// <inheritdoc />
    protected override void DefinePlan() => Define(this);

    /// <summary>Adds <see cref="Steps"/> to a plan that starts from the empty state.</summary>
    internal static void Define(MigrationPlan plan)
    {
        foreach (var (state, step) in Steps)
        {
            plan.To(state, step);
        }
    }
}
