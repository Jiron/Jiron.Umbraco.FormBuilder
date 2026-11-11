using Umbraco.Cms.Infrastructure.Migrations;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;

/// <summary>
/// Step 15 (<see cref="FormBuilderMigrationPlan.StateEntrySite"/>): the site an entry was sent from, the root of its page or
/// of the site its request's host named, so its mail and its links follow that site once its page is gone or when it named
/// none (ADR-0232). Entries sent before keep none and follow their page as before. It writes its names out by hand, as a
/// step that has shipped never changes.
/// </summary>
internal sealed class AddEntrySiteV1(IMigrationContext context) : AsyncMigrationBase(context)
{
    /// <summary>The column's name.</summary>
    public const string ColumnName = "siteKey";

    /// <inheritdoc />
    protected override Task MigrateAsync()
    {
        if (!ColumnExists(FormBuilderTables.Entry, ColumnName))
        {
            Create.Column(ColumnName).OnTable(FormBuilderTables.Entry).AsGuid().Nullable().Do();
        }

        return Task.CompletedTask;
    }
}
