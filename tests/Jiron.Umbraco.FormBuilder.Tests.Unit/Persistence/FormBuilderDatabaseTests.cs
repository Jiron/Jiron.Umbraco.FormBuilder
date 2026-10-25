using System.Collections.Concurrent;
using System.Data;
using System.Data.Common;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Scoping;
using Umbraco.Cms.Infrastructure.Scoping;
using IScope = Umbraco.Cms.Infrastructure.Scoping.IScope;
using IScopeProvider = Umbraco.Cms.Infrastructure.Scoping.IScopeProvider;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Persistence;

/// <summary>
/// The unit of work (ADR-0036): a scope per unit, a commit only when the work returns, and another attempt after a
/// transient error only where that is safe, after waits taken from the clock. The integration tests show the same on a
/// real database; these cover what a real database cannot be made to do on cue.
/// </summary>
[TestFixture]
public sealed class FormBuilderDatabaseTests
{
    /// <summary>
    /// How long a test waits for a unit that must not run again. One that did would wait on the manual clock for
    /// good, so the test fails with a timeout instead of hanging.
    /// </summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly ConcurrentQueue<IScope> _scopes = new();
    private IScopeProvider _scopeProvider = null!;
    private IScopeAccessor _scopeAccessor = null!;
    private ManualTimers _time = null!;
    private RecordingLogger _log = null!;
    private FormBuilderDatabase _database = null!;

    [SetUp]
    public void SetUp()
    {
        _scopes.Clear();
        _scopeProvider = Substitute.For<IScopeProvider>();
        ScopesAre(() => Substitute.For<IScope>());
        _scopeAccessor = Substitute.For<IScopeAccessor>();
        _scopeAccessor.AmbientScope.Returns((IScope?)null);
        _time = new ManualTimers();
        _log = new RecordingLogger();
        _database = new FormBuilderDatabase(_scopeProvider, _scopeAccessor, new PassThroughGate(), _time, _log);
    }

    [Test]
    public async Task AWrite_RunsInItsOwnScope_AndCommitsWhenItsWorkReturns()
    {
        var result = await _database.WriteAsync(_ => Task.FromResult(42));

        var scope = _scopes.Single();
        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(42));
            _scopeProvider.Received(1).CreateScope(Arg.Any<IsolationLevel>(), Arg.Any<RepositoryCacheMode>(), Arg.Any<IEventDispatcher?>(), Arg.Any<IScopedNotificationPublisher?>(), Arg.Any<bool?>(), Arg.Any<bool>(), false);
            scope.Received(1).Complete();
            scope.Received(1).Dispose();
        });
    }

    [Test]
    public void AWriteWhoseWorkThrows_EndsItsScopeWithoutCommitting()
    {
        Assert.That(() => _database.WriteAsync<int>(_ => throw new InvalidOperationException("JFB_TEST")), Throws.InvalidOperationException);

        var scope = _scopes.Single();
        Assert.Multiple(() =>
        {
            scope.DidNotReceive().Complete();
            scope.Received(1).Dispose();
        });
    }

    [Test]
    public async Task ARead_RunsInAScopeThatCompletesItself()
    {
        await _database.ReadAsync(_ => Task.FromResult(true));

        _scopeProvider.Received(1).CreateScope(Arg.Any<IsolationLevel>(), Arg.Any<RepositoryCacheMode>(), Arg.Any<IEventDispatcher?>(), Arg.Any<IScopedNotificationPublisher?>(), Arg.Any<bool?>(), Arg.Any<bool>(), true);
    }

    [Test]
    public async Task ALockConflict_RunsTheUnitAgain_InANewScope_AfterTheWaitsOfTheClock()
    {
        var attempts = 0;

        var unit = _database.WriteAsync(_ => ++attempts < 3 ? throw new SqliteException(5) : Task.FromResult(attempts));
        await FireWaitAsync(0, FormBuilderDatabase.Waits[0]);
        await FireWaitAsync(1, FormBuilderDatabase.Waits[1]);

        Assert.Multiple(async () =>
        {
            Assert.That(await unit.WaitAsync(TimeSpan.FromSeconds(10)), Is.EqualTo(3));
            Assert.That(_scopes, Has.Count.EqualTo(3), "a scope per attempt");
            Assert.That(_scopes.Count(scope => scope.ReceivedCalls().Any(call => call.GetMethodInfo().Name == nameof(IScope.Complete))), Is.EqualTo(1), "only the last attempt commits");
            Assert.That(FormBuilderDatabase.Waits, Is.EqualTo(new[] { TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(250) }));
        });
    }

    [Test]
    public async Task ALockConflictThatLasts_GivesUpAfterThreeAttempts()
    {
        var attempts = 0;

        var unit = _database.WriteAsync<int>(_ =>
        {
            attempts++;
            throw new SqliteException(5);
        });
        await FireWaitAsync(0, FormBuilderDatabase.Waits[0]);
        await FireWaitAsync(1, FormBuilderDatabase.Waits[1]);

        Assert.That(async () => await unit.WaitAsync(TimeSpan.FromSeconds(10)), Throws.InstanceOf<SqliteException>());
        Assert.Multiple(() =>
        {
            Assert.That(attempts, Is.EqualTo(FormBuilderDatabase.Attempts));
            Assert.That(_time.Timers, Has.Count.EqualTo(2), "no wait after the last attempt");
        });
    }

    [Test]
    public async Task EachRetry_IsLoggedByTheErrorsCode_WithoutItsMessage()
    {
        var attempts = 0;

        var unit = _database.ReadAsync(_ => ++attempts == 1 ? throw new SqliteException(5) : Task.FromResult(true));
        await FireWaitAsync(0, FormBuilderDatabase.Waits[0]);
        await unit.WaitAsync(TimeSpan.FromSeconds(10));

        var entry = _log.Entries.Single();
        Assert.Multiple(() =>
        {
            Assert.That(entry.Level, Is.EqualTo(LogLevel.Warning));
            Assert.That(entry.Message, Does.Contain("attempt 1").And.Contain("SqliteException 5"));
            Assert.That(entry.Message, Does.Not.Contain("JFB_TEST"));
            Assert.That(entry.HasException, Is.False, "an exception would carry the message, which may quote values");
        });
    }

    [Test]
    public async Task ALostConnection_RunsAReadAgain_ButNeverAWrite()
    {
        var reads = 0;
        var writes = 0;

        var read = _database.ReadAsync(_ => ++reads == 1 ? throw new SqlException(-2) : Task.FromResult(reads));
        await FireWaitAsync(0, FormBuilderDatabase.Waits[0]);

        Assert.Multiple(async () =>
        {
            Assert.That(await read.WaitAsync(TimeSpan.FromSeconds(10)), Is.EqualTo(2));
            Assert.That(
                () => _database.WriteAsync<int>(_ =>
                {
                    writes++;
                    throw new SqlException(-2);
                }).WaitAsync(Patience),
                Throws.InstanceOf<SqlException>(),
                "its commit may have reached the server");
            Assert.That(writes, Is.EqualTo(1));
        });
    }

    [Test]
    public void AnErrorWhileTheCommitIsOnItsWay_IsNotRunAgain()
    {
        // The outcome of a commit that failed on its way is unknown, so running the unit again could write it twice.
        ScopesAre(() =>
        {
            var scope = Substitute.For<IScope>();
            scope.When(s => s.Dispose()).Do(_ => throw new SqliteException(5));
            return scope;
        });
        var attempts = 0;

        Assert.That(
            () => _database.WriteAsync(_ =>
            {
                attempts++;
                return Task.FromResult(true);
            }).WaitAsync(Patience),
            Throws.InstanceOf<SqliteException>());
        Assert.Multiple(() =>
        {
            Assert.That(attempts, Is.EqualTo(1));
            Assert.That(_time.Timers, Is.Empty);
        });
    }

    [Test]
    public void InsideAScopeThatUmbracoOpened_NothingRunsAgain()
    {
        // Only the owner of the outer transaction can start it over.
        _scopeAccessor.AmbientScope.Returns(Substitute.For<IScope>());
        var attempts = 0;

        Assert.That(
            () => _database.WriteAsync<int>(_ =>
            {
                attempts++;
                throw new SqliteException(5);
            }).WaitAsync(Patience),
            Throws.InstanceOf<SqliteException>());
        Assert.That(attempts, Is.EqualTo(1));
    }

    [Test]
    public async Task InUnitOfWork_InsideAUnit_OrAScopeThatUmbracoOpened_AndNotOutside()
    {
        var outside = _database.InUnitOfWork;
        var inWrite = await _database.WriteAsync(_ => Task.FromResult(_database.InUnitOfWork));
        var inRead = await _database.ReadAsync(_ => Task.FromResult(_database.InUnitOfWork));
        _scopeAccessor.AmbientScope.Returns(Substitute.For<IScope>());

        Assert.Multiple(() =>
        {
            Assert.That((outside, inWrite, inRead), Is.EqualTo((false, true, true)), "this test's scopes are not ambient");
            Assert.That(_database.InUnitOfWork, Is.True, "inside a scope that Umbraco opened");
        });
    }

    [Test]
    public void ACancelledUnit_IsNotRunAgain()
    {
        using var cancel = new CancellationTokenSource();
        var attempts = 0;

        Assert.That(
            () => _database.WriteAsync<int>(
                async _ =>
                {
                    attempts++;
                    await cancel.CancelAsync();
                    throw new SqliteException(5);
                },
                cancel.Token).WaitAsync(Patience),
            Throws.InstanceOf<SqliteException>());
        Assert.That(attempts, Is.EqualTo(1));
    }

    [Test]
    public async Task TheUnitsCancellationToken_ReachesItsStatements_AndNotTheCaller()
    {
        using var cancel = new CancellationTokenSource();
        var seen = CancellationToken.None;

        await _database.ReadAsync(
            _ =>
            {
                seen = FormBuilderDbExtensions.CurrentToken;
                return Task.FromResult(true);
            },
            cancel.Token);

        Assert.Multiple(() =>
        {
            Assert.That(seen, Is.EqualTo(cancel.Token));
            Assert.That(FormBuilderDbExtensions.CurrentToken, Is.EqualTo(CancellationToken.None));
        });
    }

    [Test]
    public async Task AQueryPastItsBudget_FailsWithQueryTimeout_AndIsNotRunAgain()
    {
        var attempts = 0;
        var query = _database.QueryAsync<int>(
            async _ =>
            {
                attempts++;
                await Task.Delay(Timeout.Infinite, FormBuilderDbExtensions.CurrentToken);
                return 1;
            },
            TimeSpan.FromSeconds(10));

        await FireWaitAsync(0, TimeSpan.FromSeconds(10));

        Assert.That(
            () => query.WaitAsync(Patience),
            Throws.InstanceOf<FormBuilderQueryTimeoutException>().With.InnerException.InstanceOf<OperationCanceledException>());
        Assert.Multiple(() =>
        {
            Assert.That(attempts, Is.EqualTo(1), "a timeout never runs again");
            _scopes.Single().Received(1).Dispose();
        });
    }

    [Test]
    public async Task ACallersCancellation_EndsAQuery_AsACancellation()
    {
        using var cancel = new CancellationTokenSource();
        var query = _database.QueryAsync<int>(
            async _ =>
            {
                await Task.Delay(Timeout.Infinite, FormBuilderDbExtensions.CurrentToken);
                return 1;
            },
            TimeSpan.FromSeconds(10),
            cancel.Token);

        await cancel.CancelAsync();

        Assert.That(() => query.WaitAsync(Patience), Throws.InstanceOf<OperationCanceledException>());
        Assert.That(_scopes, Has.Count.EqualTo(1), "a cancelled read is not run again");
    }

    [Test]
    public void AStatementStoppedForTheCaller_IsACancellation()
    {
        // On SQLite the progress handler stops the statement for a caller who cancelled; the client says interrupted.
        using var cancel = new CancellationTokenSource();

        Assert.That(
            () => _database.QueryAsync<int>(
                async _ =>
                {
                    await cancel.CancelAsync();
                    throw new SqliteException(9);
                },
                TimeSpan.FromSeconds(10),
                cancel.Token).WaitAsync(Patience),
            Throws.InstanceOf<OperationCanceledException>().With.InnerException.InstanceOf<SqliteException>());
    }

    [Test]
    public void AStatementStoppedInsideAReadForTheCaller_IsACancellation()
    {
        // A read inside a budgeted query runs on the connection that the progress handler interrupts.
        using var cancel = new CancellationTokenSource();

        Assert.That(
            () => _database.ReadAsync<int>(
                async _ =>
                {
                    await cancel.CancelAsync();
                    throw new SqliteException(9);
                },
                cancel.Token).WaitAsync(Patience),
            Throws.InstanceOf<OperationCanceledException>().With.InnerException.InstanceOf<SqliteException>());
    }

    [Test]
    public async Task ARefusalPastTheDeadline_StaysARefusal()
    {
        var query = _database.QueryAsync<int>(
            async _ =>
            {
                await Task.Delay(Timeout.Infinite, FormBuilderDbExtensions.CurrentToken).ContinueWith(_ => { }, TaskScheduler.Default);
                throw new ArgumentException("JFB_TEST more values than one statement takes");
            },
            TimeSpan.FromSeconds(10));

        await FireWaitAsync(0, TimeSpan.FromSeconds(10));

        Assert.That(() => query.WaitAsync(Patience), Throws.ArgumentException);
    }

    [Test]
    public void ARefusalAfterTheCallerCancelled_StaysARefusal()
    {
        using var cancel = new CancellationTokenSource();

        Assert.That(
            () => _database.QueryAsync<int>(
                async _ =>
                {
                    await cancel.CancelAsync();
                    throw new ArgumentException("JFB_TEST refused");
                },
                TimeSpan.FromSeconds(10),
                cancel.Token).WaitAsync(Patience),
            Throws.ArgumentException);
    }

    [Test]
    public async Task ABudgetedReadInsideAnother_KeepsTheOuterBudget()
    {
        var query = _database.QueryAsync(
            _ => _database.QueryAsync<int>(
                async _ =>
                {
                    await Task.Delay(Timeout.Infinite, FormBuilderDbExtensions.CurrentToken);
                    return 1;
                },
                TimeSpan.FromHours(1)),
            TimeSpan.FromSeconds(10));

        await FireWaitAsync(0, TimeSpan.FromSeconds(10));

        Assert.That(() => query.WaitAsync(Patience), Throws.InstanceOf<FormBuilderQueryTimeoutException>());
        Assert.That(_time.Timers, Has.Count.EqualTo(1), "the inner read starts no budget of its own");
    }

    [Test]
    public async Task AQuery_ReturnsItsResult_WithItsOwnTokenThatTheCallerNeverSees()
    {
        using var cancel = new CancellationTokenSource();
        var seen = CancellationToken.None;

        var result = await _database.QueryAsync(
            _ =>
            {
                seen = FormBuilderDbExtensions.CurrentToken;
                return Task.FromResult(42);
            },
            TimeSpan.FromSeconds(10),
            cancel.Token);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(42));
            Assert.That(seen.CanBeCanceled, Is.True);
            Assert.That(seen, Is.Not.EqualTo(cancel.Token), "the deadline's token, linked to the caller's");
            Assert.That(FormBuilderDbExtensions.CurrentToken, Is.EqualTo(CancellationToken.None));
            _scopes.Single().Received(1).Dispose();
        });
    }

    [Test]
    public async Task OnSQLite_AQueryWithoutAConnectionToWatch_RunsAndSaysSo()
    {
        ScopesAre(() =>
        {
            var scope = Substitute.For<IScope>();
            scope.Database.SqlContext.SqlSyntax.ProviderName.Returns("Microsoft.Data.Sqlite");
            return scope;
        });

        var result = await _database.QueryAsync(_ => Task.FromResult(7), TimeSpan.FromSeconds(10));

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.EqualTo(7));
            Assert.That(_log.Entries.Single().Level, Is.EqualTo(LogLevel.Warning));
            Assert.That(_log.Entries.Single().Message, Does.Contain("without a time limit"));
        });
    }

    [Test]
    public void AQuery_NeedsWorkAndABudget()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => _database.QueryAsync<int>(null!, TimeSpan.FromSeconds(1)), Throws.ArgumentNullException);
            Assert.That(() => _database.QueryAsync(_ => Task.FromResult(1), TimeSpan.Zero), Throws.InstanceOf<ArgumentOutOfRangeException>());
        });
    }

    [Test]
    public async Task AUnitInsideAnother_KeepsTheOuterUnitsToken_AndAddsItsCallers()
    {
        using var outer = new CancellationTokenSource();
        using var own = new CancellationTokenSource();
        var (none, same, linked) = (CancellationToken.None, CancellationToken.None, CancellationToken.None);

        await _database.ReadAsync(
            async _ =>
            {
                none = await _database.ReadAsync(_ => Task.FromResult(FormBuilderDbExtensions.CurrentToken));
                same = await _database.ReadAsync(_ => Task.FromResult(FormBuilderDbExtensions.CurrentToken), outer.Token);
                linked = await _database.ReadAsync(_ => Task.FromResult(FormBuilderDbExtensions.CurrentToken), own.Token);
                return true;
            },
            outer.Token);

        Assert.Multiple(() =>
        {
            Assert.That(none, Is.EqualTo(outer.Token), "an inner unit without a token of its own keeps the outer one");
            Assert.That(same, Is.EqualTo(outer.Token), "the same token stays itself");
            Assert.That(linked, Is.Not.EqualTo(own.Token).And.Not.EqualTo(outer.Token), "both, linked");
            Assert.That(linked.CanBeCanceled, Is.True);
        });
    }

    [Test]
    public async Task AUnitInsideAnother_IsCancelledByEitherCaller()
    {
        using var outer = new CancellationTokenSource();
        using var own = new CancellationTokenSource();
        var byOuter = false;
        var byOwn = false;

        await _database.ReadAsync(
            async _ =>
            {
                await _database.ReadAsync(
                    async _ =>
                    {
                        await own.CancelAsync();
                        byOwn = FormBuilderDbExtensions.CurrentToken.IsCancellationRequested;
                        return true;
                    },
                    own.Token);
                return true;
            },
            outer.Token);

        using var second = new CancellationTokenSource();
        await _database.ReadAsync(
            async _ =>
            {
                await _database.ReadAsync(
                    async _ =>
                    {
                        await outer.CancelAsync();
                        byOuter = FormBuilderDbExtensions.CurrentToken.IsCancellationRequested;
                        return true;
                    },
                    second.Token);
                return true;
            },
            outer.Token);

        Assert.Multiple(() =>
        {
            Assert.That(byOwn, Is.True, "the inner caller's cancellation reaches its statements");
            Assert.That(byOuter, Is.True, "and so does the outer unit's");
        });
    }

    [Test]
    public async Task AReadInsideABudgetedQuery_KeepsTheQuerysDeadline()
    {
        var (outer, inner) = (CancellationToken.None, CancellationToken.None);

        await _database.QueryAsync(
            async _ =>
            {
                outer = FormBuilderDbExtensions.CurrentToken;
                inner = await _database.ReadAsync(_ => Task.FromResult(FormBuilderDbExtensions.CurrentToken));
                return true;
            },
            TimeSpan.FromSeconds(10));

        Assert.Multiple(() =>
        {
            Assert.That(outer.CanBeCanceled, Is.True, "the deadline's token");
            Assert.That(inner, Is.EqualTo(outer), "the inner read's statements stop at the same deadline");
        });
    }

    [Test]
    public async Task ABudgetedQueryInsideAUnit_KeepsTheOuterUnitsCancellation()
    {
        using var outer = new CancellationTokenSource();
        var cancelled = false;

        await _database.ReadAsync(
            _ => _database.QueryAsync(
                async _ =>
                {
                    await outer.CancelAsync();
                    cancelled = FormBuilderDbExtensions.CurrentToken.IsCancellationRequested;
                    return true;
                },
                TimeSpan.FromSeconds(10)),
            outer.Token);

        Assert.That(cancelled, Is.True, "the query's deadline token follows the outer unit's cancellation");
    }

    [Test]
    public async Task AWriteInsideAWrite_KeepsTheOuterUnitsToken()
    {
        using var outer = new CancellationTokenSource();
        var inner = CancellationToken.None;

        await _database.WriteAsync(
            async _ =>
            {
                inner = await _database.WriteAsync(_ => Task.FromResult(FormBuilderDbExtensions.CurrentToken));
                return true;
            },
            outer.Token);

        Assert.That(inner, Is.EqualTo(outer.Token));
    }

    [Test]
    public async Task AWriteInsideARead_IsRefused_AndInsideAWrite_ItJoins()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => _database.ReadAsync(_ => _database.WriteAsync(_ => Task.FromResult(1))), Throws.InvalidOperationException.With.Message.Contains("inside a read"));
            Assert.That(() => _database.QueryAsync(_ => _database.WriteAsync(_ => Task.FromResult(1)), TimeSpan.FromSeconds(10)), Throws.InvalidOperationException.With.Message.Contains("inside a read"));
        });

        var joined = await _database.WriteAsync(async _ => await _database.WriteAsync(_ => Task.FromResult(1)) + await _database.ReadAsync(_ => Task.FromResult(2)));
        var after = await _database.WriteAsync(_ => Task.FromResult(3));

        Assert.Multiple(() =>
        {
            Assert.That(joined, Is.EqualTo(3), "writes and reads inside a write join it");
            Assert.That(after, Is.EqualTo(3), "a write after a read is no write inside it");
        });
    }

    private void ScopesAre(Func<IScope> create) =>
        _scopeProvider.CreateScope(Arg.Any<IsolationLevel>(), Arg.Any<RepositoryCacheMode>(), Arg.Any<IEventDispatcher?>(), Arg.Any<IScopedNotificationPublisher?>(), Arg.Any<bool?>(), Arg.Any<bool>(), Arg.Any<bool>())
            .Returns(_ =>
            {
                var scope = create();
                _scopes.Enqueue(scope);
                return scope;
            });

    /// <summary>Waits until the unit asks the clock for its next wait, checks how long it is, and lets it pass.</summary>
    private async Task FireWaitAsync(int index, TimeSpan expected)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (_time.Timers.Count <= index)
        {
            Assert.That(DateTime.UtcNow, Is.LessThan(deadline), $"the unit never asked for wait {index + 1}");
            await Task.Delay(5);
        }

        var timer = _time.Timers[index];
        Assert.That(timer.DueTime, Is.EqualTo(expected));
        timer.Fire();
    }

    private sealed class PassThroughGate : IFormBuilderDatabaseGate
    {
        public Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken = default) => work();
    }

    private sealed class RecordingLogger : ILogger<FormBuilderDatabase>
    {
        public ConcurrentQueue<(LogLevel Level, string Message, bool HasException)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Enqueue((logLevel, formatter(state, exception), exception is not null));
    }

    /// <summary>Shaped like Microsoft.Data.Sqlite's exception.</summary>
    private sealed class SqliteException(int code) : DbException("JFB_TEST secret value")
    {
        public int SqliteErrorCode { get; } = code;
    }

    /// <summary>Shaped like Microsoft.Data.SqlClient's exception.</summary>
    private sealed class SqlException(int number) : DbException("JFB_TEST secret value")
    {
        public int Number { get; } = number;
    }
}
