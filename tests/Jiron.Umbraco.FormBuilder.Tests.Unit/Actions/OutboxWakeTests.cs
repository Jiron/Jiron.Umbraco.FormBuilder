using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Microsoft.Extensions.Options;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Actions;

/// <summary>
/// The wake of this server's worker (ROADMAP 8.1, ADR-0007): wakes add up to one, a wake while sending is paused does nothing, and
/// turning the pause off wakes the worker, so every queued run goes out.
/// </summary>
[TestFixture]
public sealed class OutboxWakeTests
{
    [Test]
    public async Task WakesAddUpToOne()
    {
        using var wake = new OutboxWake(new Monitor());

        wake.Wake();
        wake.Wake();

        Assert.Multiple(async () =>
        {
            Assert.That(await TakeAsync(wake), Is.True);
            Assert.That(await TakeAsync(wake), Is.False);
        });
        await Task.CompletedTask;
    }

    [Test]
    public async Task WhilePaused_AWakeDoesNothing()
    {
        var monitor = new Monitor();
        monitor.Current.Actions.Paused = true;
        using var wake = new OutboxWake(monitor);

        wake.Wake();

        Assert.Multiple(async () =>
        {
            Assert.That(wake.Paused, Is.True);
            Assert.That(await TakeAsync(wake), Is.False);
        });
        await Task.CompletedTask;
    }

    [Test]
    public async Task TurningThePauseOff_WakesTheWorker_AndTurningItOnDoesNot()
    {
        var monitor = new Monitor();
        monitor.Current.Actions.Paused = true;
        using var wake = new OutboxWake(monitor);

        monitor.Change(paused: true);
        var whileOn = await TakeAsync(wake);
        monitor.Change(paused: false);
        var afterOff = await TakeAsync(wake);

        Assert.Multiple(() =>
        {
            Assert.That(whileOn, Is.False);
            Assert.That(afterOff, Is.True);
        });
    }

    [Test]
    public async Task AChangeThatFailsValidation_KeepsTheLastGoodAnswer_AndNeverThrows()
    {
        var monitor = new Monitor();
        using var wake = new OutboxWake(monitor);
        Assert.That(wake.Paused, Is.False);
        monitor.Current.Actions.Paused = true;
        Assert.That(wake.Paused, Is.True);

        monitor.Failing = true;
        wake.Wake();

        Assert.Multiple(async () =>
        {
            Assert.That(wake.Paused, Is.True);
            Assert.That(await TakeAsync(wake), Is.False);
        });
        await Task.CompletedTask;
    }

    [Test]
    public void DisposingIt_StopsListening()
    {
        var monitor = new Monitor();
        var wake = new OutboxWake(monitor);

        wake.Dispose();

        Assert.That(monitor.Listeners, Is.Zero);
    }

    private static async Task<bool> TakeAsync(OutboxWake wake)
    {
        using var shortly = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        try
        {
            return await wake.WaitAsync(shortly.Token);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private sealed class Monitor : IOptionsMonitor<FormBuilderOptions>
    {
        private readonly List<Action<FormBuilderOptions, string?>> _listeners = [];

        public FormBuilderOptions Current { get; } = new();

        public bool Failing { get; set; }

        public FormBuilderOptions CurrentValue => Failing ? throw new OptionsValidationException(string.Empty, typeof(FormBuilderOptions), ["JFB_TEST: a typo"]) : Current;

        public int Listeners => _listeners.Count;

        public FormBuilderOptions Get(string? name) => Current;

        public IDisposable OnChange(Action<FormBuilderOptions, string?> listener)
        {
            _listeners.Add(listener);
            return new Listening(() => _listeners.Remove(listener));
        }

        public void Change(bool paused)
        {
            Current.Actions.Paused = paused;
            foreach (var listener in _listeners.ToList())
            {
                listener(Current, null);
            }
        }

        private sealed class Listening(Action stop) : IDisposable
        {
            public void Dispose() => stop();
        }
    }
}
