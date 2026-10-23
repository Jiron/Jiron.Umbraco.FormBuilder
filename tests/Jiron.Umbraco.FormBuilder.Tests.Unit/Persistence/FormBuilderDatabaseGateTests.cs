using Jiron.Umbraco.FormBuilder.Core.Persistence;
using NSubstitute;
using Umbraco.Cms.Infrastructure.Persistence;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Persistence;

/// <summary>The SQLite gate (D7, ADR-0036): one unit of work at a time, re-entrant, and a pass-through on SQL Server.</summary>
[TestFixture]
public sealed class FormBuilderDatabaseGateTests
{
    [Test]
    public async Task OnSqlite_WorkRunsOneAtATime()
    {
        using var gate = Gate("Microsoft.Data.Sqlite");
        var running = 0;
        var peak = 0;

        await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => Task.Run(() => gate.RunAsync(async () =>
        {
            var now = Interlocked.Increment(ref running);
            InterlockedMax(ref peak, now);
            await Task.Delay(2);
            Interlocked.Decrement(ref running);
            return true;
        }))));

        Assert.That(peak, Is.EqualTo(1));
    }

    [Test]
    public async Task OnSqlServer_WorkRunsTogether()
    {
        using var gate = Gate("Microsoft.Data.SqlClient");
        using var allIn = new CountdownEvent(5);

        var all = Task.WhenAll(Enumerable.Range(0, 5).Select(_ => Task.Run(() => gate.RunAsync(async () =>
        {
            allIn.Signal();
            await Task.Run(() => allIn.Wait(TimeSpan.FromSeconds(10)));
            return allIn.IsSet;
        }))));

        Assert.That(await all, Is.All.True, "every unit was inside at the same time");
    }

    [Test]
    public async Task NestedWork_JoinsTheGate_InsteadOfWaitingForItself()
    {
        using var gate = Gate("Microsoft.Data.Sqlite");

        var result = await gate.RunAsync(() => gate.RunAsync(() => gate.RunAsync(() => Task.FromResult(42))))
            .WaitAsync(TimeSpan.FromSeconds(5));

        Assert.That(result, Is.EqualTo(42));
    }

    [Test]
    public async Task AfterTheWork_TheCallerIsOutside_SoOtherWorkCanEnter()
    {
        using var gate = Gate("Microsoft.Data.Sqlite");
        await gate.RunAsync(() => Task.FromResult(1));

        var second = Task.Run(() => gate.RunAsync(() => Task.FromResult(2)));

        Assert.That(await second.WaitAsync(TimeSpan.FromSeconds(5)), Is.EqualTo(2));
    }

    [Test]
    public async Task FailingWork_ReleasesTheGate()
    {
        using var gate = Gate("Microsoft.Data.Sqlite");

        Assert.That(() => gate.RunAsync<int>(() => throw new InvalidOperationException("JFB_TEST")), Throws.InvalidOperationException);

        Assert.That(await gate.RunAsync(() => Task.FromResult(7)).WaitAsync(TimeSpan.FromSeconds(5)), Is.EqualTo(7));
    }

    [Test]
    public async Task WaitingForTheGate_CanBeCancelled()
    {
        using var gate = Gate("Microsoft.Data.Sqlite");
        var release = new TaskCompletionSource();
        var holder = Task.Run(() => gate.RunAsync(async () =>
        {
            await release.Task;
            return true;
        }));
        await Task.Delay(50);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        Assert.That(async () => await gate.RunAsync(() => Task.FromResult(true), cancel.Token), Throws.InstanceOf<OperationCanceledException>());

        release.SetResult();
        Assert.That(await holder, Is.True);
    }

    [Test]
    public void BeforeTheDatabaseIsConfigured_TheGateDecidesAgainLater()
    {
        // During the install the provider is unknown; caching "not SQLite" then would leave SQLite ungated for good.
        var factory = Substitute.For<IUmbracoDatabaseFactory>();
        factory.SqlContext.Returns((ISqlContext)null!);
        using var gate = new FormBuilderDatabaseGate(factory);

        Assert.That(gate.Gated, Is.False);

        var context = Substitute.For<ISqlContext>();
        context.SqlSyntax.ProviderName.Returns("Microsoft.Data.Sqlite");
        factory.SqlContext.Returns(context);
        Assert.That(gate.Gated, Is.True);
    }

    private static FormBuilderDatabaseGate Gate(string providerName)
    {
        var factory = Substitute.For<IUmbracoDatabaseFactory>();
        factory.SqlContext.SqlSyntax.ProviderName.Returns(providerName);
        return new FormBuilderDatabaseGate(factory);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while ((current = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }
}
