using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence;

/// <summary>
/// The pending-migration state against a real database (ADR-0033): it notices a migration that ran on another server
/// once its "pending" answer is a few seconds old, and it stays current once current; the package's tables are there
/// from the plan's first step on, though later ones are pending (27.1).
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class MigrationStateTests : FormBuilderMigrationTestBase
{
    private readonly TestTimeProvider _clock = new();

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        base.CustomTestSetup(builder);
        builder.Services.RemoveAll<TimeProvider>();
        builder.Services.AddSingleton<TimeProvider>(_clock);
    }

    [Test]
    public async Task CurrentState_StaysCurrent_ForTheLifeOfTheProcess()
    {
        var state = GetRequiredService<IFormBuilderMigrationState>();
        await RunAsync(new FormBuilderMigrationPlan());
        Assert.That(await state.IsPendingAsync(), Is.False);

        // A newer plan only arrives with a new package version, which restarts the site.
        GetRequiredService<IKeyValueService>().SetValue(StateKey, FormBuilderMigrationPlan.StateCreateSchema);
        _clock.Advance(FormBuilderMigrationState.RecheckAfter * 10);

        Assert.That(await state.IsPendingAsync(), Is.False);
    }

    // Running the plan through the Upgrader publishes no notification here, as when another server migrates.
    [Test]
    public async Task PendingAnswer_IsReusedWithinTheInterval_AndReadAgainAfterIt()
    {
        var state = GetRequiredService<IFormBuilderMigrationState>();
        Assert.That(await state.IsPendingAsync(), Is.True);
        await RunAsync(new FormBuilderMigrationPlan());

        _clock.Advance(FormBuilderMigrationState.RecheckAfter - TimeSpan.FromMilliseconds(1));
        var withinInterval = await state.IsPendingAsync();
        _clock.Advance(TimeSpan.FromMilliseconds(1));
        var afterInterval = await state.IsPendingAsync();

        Assert.Multiple(() =>
        {
            Assert.That(withinInterval, Is.True, "the remembered answer");
            Assert.That(afterInterval, Is.False, "read again");
        });
    }

    [Test]
    public async Task TheTables_AreThereFromThePlansFirstStep_ThoughLaterOnesArePending()
    {
        var state = GetRequiredService<IFormBuilderMigrationState>();
        var before = (await state.HasTablesAsync(), await state.IsPendingAsync());
        await RunAsync(new TestPlan(1));
        state.Invalidate();
        var first = (await state.HasTablesAsync(), await state.IsPendingAsync());
        await RunAsync(new FormBuilderMigrationPlan());
        state.Invalidate();
        var complete = (await state.HasTablesAsync(), await state.IsPendingAsync());

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo((false, true)), "no state, no tables");
            Assert.That(first, Is.EqualTo((true, true)), "the first step creates every table");
            Assert.That(complete, Is.EqualTo((true, false)));
        });
    }

    [Test]
    public async Task PartlyMigratedDatabase_IsPending_UntilTheLastStepRan()
    {
        var state = GetRequiredService<IFormBuilderMigrationState>();
        await RunAsync(new TestPlan(Steps.Count - 1));

        var partly = await state.IsPendingAsync();
        await RunAsync(new FormBuilderMigrationPlan());
        state.Invalidate();
        var complete = await state.IsPendingAsync();

        Assert.Multiple(() =>
        {
            Assert.That(partly, Is.True);
            Assert.That(complete, Is.False, "Invalidate forgets the remembered answer at once");
        });
    }
}
