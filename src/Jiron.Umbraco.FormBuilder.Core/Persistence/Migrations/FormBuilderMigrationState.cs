using System.Data.Common;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Scoping;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;

/// <inheritdoc />
internal sealed partial class FormBuilderMigrationState(
    IScopeProvider scopeProvider,
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
    public async ValueTask<bool> IsPendingAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = _snapshot;
        if (snapshot is { Pending: false })
        {
            return false;
        }

        if (runtimeState.Level != RuntimeLevel.Run)
        {
            return true;
        }

        var now = timeProvider.GetUtcNow();
        if (snapshot is not null && now - snapshot.CheckedAt < RecheckAfter)
        {
            return true;
        }

        var pending = !string.Equals(await ReadStoredStateAsync(cancellationToken), FinalState, StringComparison.Ordinal);
        _snapshot = new Snapshot(pending, now);
        return pending;
    }

    /// <inheritdoc />
    public void Invalidate() => _snapshot = null;

    private async Task<string?> ReadStoredStateAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeProvider.CreateScope(autoComplete: true);
            var syntax = scope.SqlContext.SqlSyntax;
            var sql = scope.SqlContext.Sql(
                $"SELECT {syntax.GetQuotedColumnName("value")} FROM {syntax.GetQuotedTableName(Constants.DatabaseSchema.Tables.KeyValue)} WHERE {syntax.GetQuotedColumnName("key")} = @0",
                StateKey);
            return await scope.Database.ExecuteScalarAsync<string?>(sql, cancellationToken);
        }
        catch (DbException exception)
        {
            // An unreadable state counts as pending: Jiron Form Builder must not run on a schema it cannot vouch for.
            LogStateUnreadable(logger, exception);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Jiron Form Builder could not read its migration state; it treats its migrations as pending")]
    private static partial void LogStateUnreadable(ILogger logger, Exception exception);

    private sealed record Snapshot(bool Pending, DateTimeOffset CheckedAt);
}
