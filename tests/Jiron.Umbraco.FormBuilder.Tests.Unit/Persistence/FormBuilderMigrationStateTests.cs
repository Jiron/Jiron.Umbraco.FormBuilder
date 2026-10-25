using System.Data;
using System.Data.Common;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Microsoft.Extensions.Logging.Abstractions;
using NPoco;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Scoping;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Cms.Infrastructure.Scoping;
using IScope = Umbraco.Cms.Infrastructure.Scoping.IScope;
using IScopeProvider = Umbraco.Cms.Infrastructure.Scoping.IScopeProvider;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Persistence;

/// <summary>The cases of the pending-migration state that need no database (ADR-0033).</summary>
public sealed class FormBuilderMigrationStateTests
{
    [TestCase(RuntimeLevel.Install)]
    [TestCase(RuntimeLevel.Upgrade)]
    [TestCase(RuntimeLevel.Boot)]
    [TestCase(RuntimeLevel.BootFailed)]
    [TestCase(RuntimeLevel.Unknown)]
    public async Task IsPending_WhileUmbracoIsNotRunning_IsTrue_WithoutTouchingTheDatabase(RuntimeLevel level)
    {
        var scopes = Substitute.For<IScopeProvider>();
        var state = Create(level, scopes);

        Assert.That(await state.IsPendingAsync(), Is.True);
        Assert.That(scopes.ReceivedCalls(), Is.Empty);
    }

    [Test]
    public async Task IsPending_WhenTheStateCannotBeRead_IsTrue_AndIsReadAgainLater()
    {
        var scopes = Substitute.For<IScopeProvider>();
        scopes.CreateScope().ThrowsForAnyArgs(new TestDbException());
        var clock = new TestTimeProvider();
        var state = Create(RuntimeLevel.Run, scopes, clock);

        var first = await state.IsPendingAsync();
        var remembered = await state.IsPendingAsync();
        clock.Advance(FormBuilderMigrationState.RecheckAfter);
        var again = await state.IsPendingAsync();

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.True);
            Assert.That(remembered, Is.True);
            Assert.That(again, Is.True);
            Assert.That(scopes.ReceivedCalls().Count(), Is.EqualTo(2), "read once, then again after the interval");
        });
    }

    [Test]
    public async Task IsPending_AfterALockConflict_ReadsTheStateAgain_InsteadOfAnsweringPending()
    {
        var busy = Scope(Task.FromException<string>(new SqliteException(5)));
        var answered = Scope(Task.FromResult(FormBuilderMigrationState.FinalState));
        var scopes = Substitute.For<IScopeProvider>();
        scopes.CreateScope(Arg.Any<IsolationLevel>(), Arg.Any<RepositoryCacheMode>(), Arg.Any<IEventDispatcher?>(), Arg.Any<IScopedNotificationPublisher?>(), Arg.Any<bool?>(), Arg.Any<bool>(), Arg.Any<bool>())
            .Returns(busy, answered);
        var timers = new ManualTimers();
        var state = Create(RuntimeLevel.Run, scopes, timers: timers);

        var pending = state.IsPendingAsync().AsTask();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (timers.Timers.Count == 0)
        {
            Assert.That(DateTime.UtcNow, Is.LessThan(deadline), "the busy read never waited to run again");
            await Task.Delay(5);
        }

        timers.Timers[0].Fire();

        Assert.Multiple(async () =>
        {
            Assert.That(await pending.WaitAsync(TimeSpan.FromSeconds(10)), Is.False, "the second attempt read the final state");
            Assert.That(timers.Timers[0].DueTime, Is.EqualTo(FormBuilderDatabase.Waits[0]));
            Assert.That(scopes.ReceivedCalls().Count(), Is.EqualTo(2), "a scope per attempt");
        });
    }

    [Test]
    public void FinalState_IsThePlansLastState_AndTheKeyIsUmbracosForThePlanName()
    {
        Assert.Multiple(() =>
        {
            Assert.That(FormBuilderMigrationState.FinalState, Is.EqualTo(FormBuilderMigrationPlan.Steps[^1].State));
            Assert.That(FormBuilderMigrationState.FinalState, Is.EqualTo(new FormBuilderMigrationPlan().FinalState));
            Assert.That(FormBuilderMigrationState.StateKey, Is.EqualTo("Umbraco.Core.Upgrader.State+Jiron.Umbraco.FormBuilder"));
            Assert.That(FormBuilderMigrationState.RecheckAfter, Is.EqualTo(TimeSpan.FromSeconds(5)));
        });
    }

    private static FormBuilderMigrationState Create(RuntimeLevel level, IScopeProvider scopes, TimeProvider? clock = null, ManualTimers? timers = null)
    {
        var runtime = Substitute.For<IRuntimeState>();
        runtime.Level.Returns(level);
        var accessor = Substitute.For<IScopeAccessor>();
        accessor.AmbientScope.Returns((IScope?)null);
        var database = new FormBuilderDatabase(scopes, accessor, new PassThroughGate(), timers ?? new ManualTimers(), NullLogger<FormBuilderDatabase>.Instance);
        return new FormBuilderMigrationState(database, runtime, clock ?? new TestTimeProvider(), NullLogger<FormBuilderMigrationState>.Instance);
    }

    /// <summary>A scope whose database answers the state read with <paramref name="answer"/>.</summary>
    private static IScope Scope(Task<string> answer)
    {
        var db = Substitute.For<IUmbracoDatabase>();
        db.ExecuteScalarAsync<string>(Arg.Any<Sql>(), Arg.Any<CancellationToken>()).Returns(answer);
        var scope = Substitute.For<IScope>();
        scope.Database.Returns(db);
        return scope;
    }

    private sealed class TestDbException() : DbException("JFB_TEST: the database is not there.");

    /// <summary>Shaped like Microsoft.Data.Sqlite's exception; 5 is SQLite's busy code.</summary>
    private sealed class SqliteException(int code) : DbException("JFB_TEST: the database is busy.")
    {
        public int SqliteErrorCode { get; } = code;
    }

    private sealed class PassThroughGate : IFormBuilderDatabaseGate
    {
        public Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken = default) => work();
    }
}
