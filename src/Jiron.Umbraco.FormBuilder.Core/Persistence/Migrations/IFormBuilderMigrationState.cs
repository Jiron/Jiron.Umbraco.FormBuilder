namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;

/// <summary>
/// Tells whether Jiron Form Builder's database is behind its migration plan (ADR-0033). With unattended package
/// migrations off, a site runs while Jiron Form Builder's tables are missing or old. Until the plan reaches its final
/// state, the management API answers <c>503 migrations_pending</c> (4.7), so do the public API (6.1), forms render
/// nothing (6.2), the jobs do nothing (8.1), and the health check says so (9.1).
/// </summary>
internal interface IFormBuilderMigrationState
{
    /// <summary>
    /// True while the stored state of the plan differs from its final state, while Umbraco is not running yet, and
    /// when the state cannot be read. Cheap: a current schema is remembered for the life of the process, and a pending
    /// one is read again after a few seconds, so a migration on another server is noticed.
    /// </summary>
    ValueTask<bool> IsPendingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// True once the package's tables are there: the plan's first step has run, though later ones may still be pending.
    /// False while Umbraco is not running yet, before the first step, and when the state cannot be read. A state that
    /// cannot tell the two apart says the tables are there only once nothing is pending.
    /// </summary>
    async ValueTask<bool> HasTablesAsync(CancellationToken cancellationToken = default) => !await IsPendingAsync(cancellationToken);

    /// <summary>Forgets the remembered answer, for example after the plan ran on this server.</summary>
    void Invalidate();
}
