using Jiron.Umbraco.FormBuilder.Testing.AI.FakeAI;

namespace Jiron.Umbraco.FormBuilder.Tests.AI.Unit;

/// <summary>
/// The fake AI's set-up on a test host (ROADMAP 26.1): it tries again until Umbraco.AI has made its tables, which a
/// handler of the same notification does in an order that differs between systems, for a limited time, and says so.
/// </summary>
[TestFixture]
public sealed class FakeAIStartupTests
{
    private static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(10);

    [Test]
    public async Task ASetUpThatFindsNoTableYet_TriesAgain_AndSaysOnceThatItWaits()
    {
        var calls = 0;
        var heard = new List<string>();

        var (result, failure, attempts) = await FakeAIStartup.TryUntilAsync(
            _ => ++calls < 3 ? throw new InvalidOperationException("no such table: umbracoAIConnection") : Task.FromResult("ready"),
            TimeSpan.FromSeconds(30),
            Pause,
            TimeProvider.System,
            exception => heard.Add(exception.Message),
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That((result, failure, attempts), Is.EqualTo(("ready", (Exception?)null, 3)));
            Assert.That(heard, Is.EqualTo(new[] { "no such table: umbracoAIConnection" }), "said once, with the reason");
        });
    }

    [Test]
    public async Task ASetUpThatNeverSucceeds_EndsAtTheLimit_WithTheLastReason()
    {
        var calls = 0;

        var (result, failure, attempts) = await FakeAIStartup.TryUntilAsync<string>(
            _ => throw new InvalidOperationException($"failure {++calls}"),
            TimeSpan.FromMilliseconds(200),
            Pause,
            TimeProvider.System,
            _ => { },
            CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(result, Is.Null);
            Assert.That(failure!.Message, Is.EqualTo($"failure {calls}"), "the last reason");
            Assert.That(attempts, Is.EqualTo(calls).And.GreaterThan(1));
        });
    }

    [Test]
    public void AHostThatStops_StopsTheSetUp()
    {
        using var stopping = new CancellationTokenSource();
        stopping.Cancel();

        Assert.That(
            () => FakeAIStartup.TryUntilAsync<string>(_ => throw new InvalidOperationException("no table"), TimeSpan.FromSeconds(30), Pause, TimeProvider.System, _ => { }, stopping.Token),
            Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public void TheLimit_LeavesUmbracoAITimeForItsMigrations_AndTheHarnessTimeToHearOfIt()
    {
        // The browser tests' harness waits four minutes for a host to be ready (tests/e2e/support/host.ts).
        Assert.That(FakeAIStartup.Limit, Is.GreaterThanOrEqualTo(TimeSpan.FromSeconds(30)).And.LessThan(TimeSpan.FromMinutes(4)));
    }
}
