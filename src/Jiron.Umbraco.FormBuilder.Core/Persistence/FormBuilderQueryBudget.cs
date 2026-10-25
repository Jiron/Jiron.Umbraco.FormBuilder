using System.Data.Common;
using System.Reflection;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence;

/// <summary>
/// The time budget of one read (ADR-0003, ADR-0039): past its deadline the running statement stops, so one bad plan
/// never holds the gate, and through SQLite's single writer every write of the site, for long.
/// </summary>
/// <remarks>
/// <para>
/// On SQL Server the unit's cancellation token is cancelled at the deadline, which stops the running command. On
/// SQLite that is not enough, because Microsoft.Data.Sqlite does not cancel a statement once it runs: SQLite's
/// progress handler, set on the unit's connection while it runs, checks the deadline every
/// <see cref="StepsPerCheck"/> virtual-machine steps and interrupts the statement past it.
/// </para>
/// <para>
/// Core references no database client (ADR-0036), so the handler is found by type name, as database errors are
/// (<see cref="FormBuilderDatabaseErrors"/>), on the connection inside Umbraco's wrappers.
/// </para>
/// </remarks>
internal sealed class FormBuilderQueryBudget : IDisposable
{
    /// <summary>How many SQLite virtual-machine steps run between two checks of the deadline.</summary>
    internal const int StepsPerCheck = 10_000;

    /// <summary>The time one list may take (ADR-0003): past it the read stops with <c>query_timeout</c>.</summary>
    internal static readonly TimeSpan Default = TimeSpan.FromSeconds(10);

    private static readonly Lazy<SqliteBinding?> Sqlite = new(SqliteBinding.Find);

    private readonly CancellationToken _caller;
    private readonly CancellationTokenSource _deadline;
    private readonly CancellationTokenSource _token;
    private readonly TimeProvider _time;
    private readonly long _started;
    private readonly TimeSpan _budget;
    private readonly object? _handle;

    // SQLite calls it until the handler is cleared, so the budget holds the reference.
    private readonly Delegate? _progress;
    private bool _tripped;

    private FormBuilderQueryBudget(TimeSpan budget, TimeProvider time, DbConnection? sqlite, CancellationToken caller)
    {
        _budget = budget;
        _time = time;
        _caller = caller;
        _started = time.GetTimestamp();
        _deadline = new CancellationTokenSource(budget, time);
        _token = CancellationTokenSource.CreateLinkedTokenSource(caller, _deadline.Token);
        if (sqlite is null || Sqlite.Value is not { } binding)
        {
            return;
        }

        _handle = binding.Handle(sqlite);
        if (_handle is not null)
        {
            _progress = binding.Callback(this);
            binding.SetHandler(_handle, StepsPerCheck, _progress);
        }
    }

    /// <summary>The token the unit's statements run with: cancelled by the caller, or at the deadline.</summary>
    public CancellationToken Token => _token.Token;

    /// <summary>Whether the deadline passed: the read's failure is then <c>query_timeout</c>.</summary>
    public bool Expired => _tripped || _deadline.IsCancellationRequested;

    /// <summary>Whether SQLite's progress handler watches the statements (always false on SQL Server).</summary>
    public bool Armed => _progress is not null;

    /// <summary>
    /// Starts the budget of a unit whose statements run on <paramref name="connection"/>; on SQLite it arms the progress
    /// handler on the provider's connection inside it.
    /// </summary>
    public static FormBuilderQueryBudget Start(DbConnection? connection, bool sqlite, TimeSpan budget, TimeProvider time, CancellationToken caller)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(budget, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(time);
        return new FormBuilderQueryBudget(budget, time, sqlite ? connection : null, caller);
    }

    /// <summary>Clears the progress handler before the connection goes back to Umbraco, then ends the deadline.</summary>
    public void Dispose()
    {
        if (_handle is not null)
        {
            Sqlite.Value!.SetHandler(_handle, 0, null);
        }

        _token.Dispose();
        _deadline.Dispose();
    }

    // SQLite's progress callback, whose argument is the unused user data: a value other than 0 interrupts the statement.
    private int OnProgress(object? _)
    {
        if (_caller.IsCancellationRequested)
        {
            return 1;
        }

        if (_time.GetElapsedTime(_started) < _budget)
        {
            return 0;
        }

        _tripped = true;
        return 1;
    }

    /// <summary>Microsoft.Data.Sqlite's connection and SQLitePCLRaw's progress handler, found by name once.</summary>
    private sealed class SqliteBinding
    {
        // Umbraco wraps the provider's connection (retries, profiling, deferred transactions); a few layers deep at most.
        private const int MaxWrappers = 8;
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private readonly Type _connection;
        private readonly PropertyInfo _handle;
        private readonly MethodInfo _setHandler;
        private readonly Type _callback;

        private SqliteBinding(Type connection, PropertyInfo handle, MethodInfo setHandler, Type callback)
        {
            _connection = connection;
            _handle = handle;
            _setHandler = setHandler;
            _callback = callback;
        }

        /// <summary>The binding, or null where Microsoft.Data.Sqlite is not loaded (a SQL Server site).</summary>
        public static SqliteBinding? Find()
        {
            var connection = Type.GetType("Microsoft.Data.Sqlite.SqliteConnection, Microsoft.Data.Sqlite", throwOnError: false);
            var handle = connection?.GetProperty("Handle", BindingFlags.Instance | BindingFlags.Public);
            var raw = Type.GetType("SQLitePCL.raw, SQLitePCLRaw.core", throwOnError: false);
            var callback = Type.GetType("SQLitePCL.delegate_progress, SQLitePCLRaw.core", throwOnError: false);
            if (connection is null || handle is null || raw is null || callback is null)
            {
                return null;
            }

            var setHandler = raw.GetMethod("sqlite3_progress_handler", BindingFlags.Static | BindingFlags.Public, [handle.PropertyType, typeof(int), callback, typeof(object)]);
            return setHandler is null ? null : new SqliteBinding(connection, handle, setHandler, callback);
        }

        /// <summary>The native handle of the SQLite connection inside <paramref name="connection"/>, or null.</summary>
        public object? Handle(DbConnection connection)
        {
            object? current = connection;
            for (var depth = 0; depth < MaxWrappers && current is not null; depth++)
            {
                if (_connection.IsInstanceOfType(current))
                {
                    return _handle.GetValue(current);
                }

                current = Inner(current);
            }

            return null;
        }

        /// <summary>The progress callback of <paramref name="budget"/>, as SQLitePCLRaw's delegate type.</summary>
        public Delegate Callback(FormBuilderQueryBudget budget) =>
            Delegate.CreateDelegate(_callback, budget, typeof(FormBuilderQueryBudget).GetMethod(nameof(OnProgress), Members)!);

        /// <summary>Sets the progress handler, or clears it with 0 steps and no callback.</summary>
        public void SetHandler(object handle, int steps, Delegate? callback) => _setHandler.Invoke(null, [handle, steps, callback, null]);

        // The connection a wrapper wraps: its first property or field that holds another connection.
        private static DbConnection? Inner(object wrapper)
        {
            var type = wrapper.GetType();
            var properties = type.GetProperties(Members)
                .Where(property => typeof(DbConnection).IsAssignableFrom(property.PropertyType) && property.GetIndexParameters().Length == 0)
                .Select(property => property.GetValue(wrapper) as DbConnection);
            var fields = type.GetFields(Members)
                .Where(field => typeof(DbConnection).IsAssignableFrom(field.FieldType))
                .Select(field => field.GetValue(wrapper) as DbConnection);
            return properties.Concat(fields).FirstOrDefault(inner => inner is not null && !ReferenceEquals(inner, wrapper));
        }
    }
}
