using Umbraco.Cms.Infrastructure.Migrations;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;

/// <summary>
/// Step 10 (<see cref="FormBuilderMigrationPlan.StateActionRunLastAttempt"/>): when a run's latest attempt started, which a
/// claim writes, and the index that finds an action's newest runs, for the last run on an action's card (ADR-0195). The
/// index holds what the read filters on too, so the runs it passes over, waiting for their first try, cost no lookups. It
/// writes its names out by hand, as a step that has shipped never changes.
/// </summary>
internal sealed class AddActionRunLastAttemptV1(IMigrationContext context) : AsyncMigrationBase(context)
{
    /// <summary>The index's name.</summary>
    public const string IndexName = "IX_jfbActionRun_actionKey_id_state_attempts_entryId";

    /// <inheritdoc />
    protected override Task MigrateAsync()
    {
        if (!ColumnExists(FormBuilderTables.ActionRun, "lastAttemptUtc"))
        {
            Create.Column("lastAttemptUtc").OnTable(FormBuilderTables.ActionRun).AsDateTime().Nullable().Do();
        }

        if (!IndexExists(IndexName))
        {
            Create.Index(IndexName).OnTable(FormBuilderTables.ActionRun).OnColumn("actionKey").Ascending().OnColumn("id").Ascending().OnColumn("state").Ascending().OnColumn("attempts").Ascending().OnColumn("entryId").Ascending().WithOptions().NonClustered().Do();
        }

        return Task.CompletedTask;
    }
}
