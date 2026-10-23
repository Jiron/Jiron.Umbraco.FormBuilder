using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using NSubstitute;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Sync;
using Umbraco.Cms.Infrastructure.Migrations;
using Umbraco.Cms.Infrastructure.Migrations.Notifications;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Persistence;

/// <summary>
/// After a plan ran (ADR-0033): Jiron Form Builder's plan resets the migration state, and when one of its steps ran,
/// every server refreshes its user groups, because the section grant writes them directly.
/// </summary>
[FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
public sealed class FormBuilderMigrationsExecutedHandlerTests
{
    private readonly IFormBuilderMigrationState _state = Substitute.For<IFormBuilderMigrationState>();
    private readonly IServerMessenger _messenger = Substitute.For<IServerMessenger>();
    private readonly ICacheRefresher _userGroups = Substitute.For<ICacheRefresher>();
    private readonly FormBuilderMigrationsExecutedHandler _handler;

    public FormBuilderMigrationsExecutedHandlerTests()
    {
        _userGroups.RefresherUniqueId.Returns(UserGroupCacheRefresher.UniqueId);
        _handler = new FormBuilderMigrationsExecutedHandler(_state, new DistributedCache(_messenger, new CacheRefresherCollection(() => [_userGroups])));
    }

    [Test]
    public void OurPlan_WithCompletedSteps_ResetsTheStateAndRefreshesTheUserGroupsOnEveryServer()
    {
        var plan = new FormBuilderMigrationPlan();

        _handler.Handle(Executed(plan, plan.Transitions[string.Empty]!));

        _state.Received(1).Invalidate();
        _messenger.Received(1).QueueRefreshAll(_userGroups);
    }

    [Test]
    public void OurPlan_WithNothingToDo_ResetsTheState_AndLeavesTheCachesAlone()
    {
        _handler.Handle(Executed(new FormBuilderMigrationPlan()));

        _state.Received(1).Invalidate();
        Assert.That(_messenger.ReceivedCalls(), Is.Empty);
    }

    [Test]
    public void AnotherPlan_ChangesNothing()
    {
        var other = new OtherPlan();

        _handler.Handle(Executed(other, other.Transitions[string.Empty]!));

        Assert.Multiple(() =>
        {
            Assert.That(_state.ReceivedCalls(), Is.Empty);
            Assert.That(_messenger.ReceivedCalls(), Is.Empty);
        });
    }

    private static MigrationPlansExecutedNotification Executed(MigrationPlan plan, params MigrationPlan.Transition[] completed) =>
        new([new ExecutedMigrationPlan(plan, string.Empty, plan.FinalState, successful: true, completed)]);

    private sealed class OtherPlan : MigrationPlan
    {
        public OtherPlan()
            : base("JfbTest.Other")
        {
            From(string.Empty).To<CreateSchemaV1>("jfb-test-other");
        }
    }
}
