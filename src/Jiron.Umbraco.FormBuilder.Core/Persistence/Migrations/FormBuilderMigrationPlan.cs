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

    /// <summary>The steps in order, with the state each one leaves. The tests build their copies of the plan from it.</summary>
    internal static readonly IReadOnlyList<(string State, Type Step)> Steps =
    [
        (StateCreateSchema, typeof(CreateSchemaV1)),
        (StateDefaultSettings, typeof(CreateDefaultSettingsV1)),
        (StateGrantSection, typeof(GrantFormsSectionV1)),
        (StateRequestReferenceRebuild, typeof(RequestReferenceRebuildV1)),
        (StateFileScanIndex, typeof(AddFileScanIndexV1)),
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
