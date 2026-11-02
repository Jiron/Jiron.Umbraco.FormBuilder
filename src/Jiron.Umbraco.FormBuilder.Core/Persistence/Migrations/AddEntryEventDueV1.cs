using Umbraco.Cms.Infrastructure.Migrations;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;

/// <summary>
/// Step 8 (<see cref="FormBuilderMigrationPlan.StateEntryEventDue"/>): when an entry's event has something left to do, as a user's
/// change has once its undo window is over (ADR-0162), with the index the queue finds it by. It writes its names out by hand, as a step
/// that has shipped never changes.
/// </summary>
internal sealed class AddEntryEventDueV1(IMigrationContext context) : AsyncMigrationBase(context)
{
    /// <summary>The index's name.</summary>
    public const string IndexName = "IX_jfbEntryEvent_dueUtc";

    /// <inheritdoc />
    protected override Task MigrateAsync()
    {
        if (!ColumnExists(FormBuilderTables.EntryEvent, "dueUtc"))
        {
            Create.Column("dueUtc").OnTable(FormBuilderTables.EntryEvent).AsDateTime().Nullable().Do();
        }

        if (!IndexExists(IndexName))
        {
            Create.Index(IndexName).OnTable(FormBuilderTables.EntryEvent).OnColumn("dueUtc").Ascending().WithOptions().NonClustered().Do();
        }

        return Task.CompletedTask;
    }
}
