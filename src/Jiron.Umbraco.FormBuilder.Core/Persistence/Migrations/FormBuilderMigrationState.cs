using System.Data.Common;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Services;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;

/// <inheritdoc />
/// <remarks>
/// The state is read as a unit of work (ADR-0036): behind the gate, so a running write does not turn it away on SQLite,
/// and again after a lock conflict, so a busy moment does not answer "pending" for the next few seconds.
/// </remarks>
internal sealed partial class FormBuilderMigrationState(
    FormBuilderDatabase database,
    IRuntimeState runtimeState,
    TimeProvider timeProvider,
    ILogger<FormBuilderMigrationState> logger)
    : IFormBuilderMigrationState
{
    /// <summary>How long a "pending" answer is trusted before the database is read again.</summary>
    internal static readonly TimeSpan RecheckAfter = TimeSpan.FromSeconds(5);

    /// <summary>Where Umbraco keeps the plan's state.</summary>
    internal const string StateKey = Constants.Conventions.Migrations.KeyValuePrefix + FormBuilderConstants.MigrationPlanName;

    /// <summary>The state the plan reaches when every step has run, read from the plan itself.</summary>
    internal static readonly string FinalState = new FormBuilderMigrationPlan().FinalState;

    // Replaced as a whole, so readers never see a torn value. "Current" holds for the life of the process: a newer
    // plan only arrives with a new package version, which restarts the site.
    private volatile Snapshot? _snapshot;

    /// <inheritdoc />
    public async ValueTask<bool> IsPendingAsync(CancellationToken cancellationToken = default) =>
        await SnapshotAsync(cancellationToken) is not { Pending: false };

    /// <inheritdoc />
    public async ValueTask<bool> HasTablesAsync(CancellationToken cancellationToken = default) =>
        await SnapshotAsync(cancellationToken) is { HasTables: true };

    // The remembered state, read again while it is pending and older than RecheckAfter; null while Umbraco is not running.
    private async ValueTask<Snapshot?> SnapshotAsync(CancellationToken cancellationToken)
    {
        var snapshot = _snapshot;
        if (snapshot is { Pending: false })
        {
            return snapshot;
        }

        if (runtimeState.Level != RuntimeLevel.Run)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        if (snapshot is not null && now - snapshot.CheckedAt < RecheckAfter)
        {
            return snapshot;
        }

        var stored = await ReadStoredStateAsync(cancellationToken);
        snapshot = new Snapshot(!string.Equals(stored, FinalState, StringComparison.Ordinal), !string.IsNullOrEmpty(stored), now);
        _snapshot = snapshot;
        return snapshot;
    }

    /// <inheritdoc />
    public void Invalidate() => _snapshot = null;

    private async Task<string?> ReadStoredStateAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await database.ReadAsync(
                db =>
                {
                    var syntax = db.SqlContext.SqlSyntax;
                    return db.ScalarAsync<string?>(
                        $"SELECT {syntax.GetQuotedColumnName("value")} FROM {syntax.GetQuotedTableName(Constants.DatabaseSchema.Tables.KeyValue)} WHERE {syntax.GetQuotedColumnName("key")} = @0",
                        StateKey);
                },
                cancellationToken);
        }
        catch (DbException exception)
        {
            // An unreadable state counts as pending, after the reruns: Jiron Form Builder must not run on a schema it
            // cannot vouch for.
            LogStateUnreadable(logger, LoggedException.Of(exception));
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Jiron Form Builder could not read its migration state; it treats its migrations as pending")]
    private static partial void LogStateUnreadable(ILogger logger, LoggedException exception);

    // HasTables: the plan has a state at all, so its first step, which creates every table, has run.
    private sealed record Snapshot(bool Pending, bool HasTables, DateTimeOffset CheckedAt);
}
