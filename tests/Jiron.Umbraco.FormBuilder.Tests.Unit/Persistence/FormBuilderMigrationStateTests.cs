using System.Data.Common;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Infrastructure.Scoping;

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

    private static FormBuilderMigrationState Create(RuntimeLevel level, IScopeProvider scopes, TimeProvider? clock = null)
    {
        var runtime = Substitute.For<IRuntimeState>();
        runtime.Level.Returns(level);
        return new FormBuilderMigrationState(scopes, runtime, clock ?? new TestTimeProvider(), NullLogger<FormBuilderMigrationState>.Instance);
    }

    private sealed class TestDbException() : DbException("JFB_TEST: the database is not there.");
}
