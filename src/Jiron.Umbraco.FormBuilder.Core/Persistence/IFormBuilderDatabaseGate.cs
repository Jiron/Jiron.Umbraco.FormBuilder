namespace Jiron.Umbraco.FormBuilder.Core.Persistence;

/// <summary>
/// Lets one unit of Jiron Form Builder's database work run at a time per process on SQLite (D7, ADR-0036). Concurrent
/// read-then-write transactions on Umbraco's shared-cache SQLite connections fail with "database table is locked", and
/// their synchronous retries starve the thread pool; queueing them asynchronously avoids both. SQLite sites run on one
/// server, so this costs no scale. On SQL Server the gate lets everything through.
/// </summary>
/// <remarks>
/// <para>The gate is re-entrant: work running inside it may run more gated work, which joins instead of waiting for
/// itself. Do not start parallel database work inside the gate.</para>
/// <para>Do not enter it from inside an outer Umbraco scope that has written (for example a content-saving
/// notification handler): that transaction holds SQLite's write lock while it waits for the gate. React to the "-ed"
/// notification instead.</para>
/// </remarks>
internal interface IFormBuilderDatabaseGate
{
    /// <summary>Runs <paramref name="work"/> once the gate is free (at once on SQL Server or when already inside).</summary>
    Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken = default);
}
