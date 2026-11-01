using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Microsoft.Extensions.Logging;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.FieldTypes;

/// <summary>
/// Whether a field type can be used on the site (ADR-0057): available by default; a type that needs configuration says
/// why; and the service that asks every type at once reports a check that is too slow, throws or answers nothing as not
/// configured, with the reason that its setup could not be checked, logged with the type's alias.
/// </summary>
public sealed class FieldTypeAvailabilityTests
{
    // How long a test waits for work the thread pool runs: only a guard against a hang, never a measure of time, because
    // a busy test run may keep the pool's threads for seconds.
    private static readonly TimeSpan Hang = TimeSpan.FromMinutes(1);

    [Test]
    public void Available_HasNoReason_AndNotConfigured_HoldsItsReason()
    {
        var missing = FieldTypeAvailability.NotConfigured("#acme_noKeys");

        Assert.Multiple(() =>
        {
            Assert.That((FieldTypeAvailability.Available.IsAvailable, FieldTypeAvailability.Available.Reason), Is.EqualTo((true, (string?)null)));
            Assert.That((missing.IsAvailable, missing.Reason), Is.EqualTo((false, "#acme_noKeys")));
            Assert.Throws<ArgumentException>(() => FieldTypeAvailability.NotConfigured(" "), "a reason that is blank");
            Assert.Throws<ArgumentNullException>(() => FieldTypeAvailability.NotConfigured(null!), "no reason");
        });
    }

    [Test]
    public async Task ATypeThatSaysNothing_IsAvailable_ThroughTheInterface_AndTheBaseClass()
    {
        IFormBuilderFieldType plain = new Plain();
        IFormBuilderFieldType builtIn = new TextFieldType();

        Assert.Multiple(async () =>
        {
            Assert.That(await plain.CheckAvailabilityAsync(CancellationToken.None), Is.SameAs(FieldTypeAvailability.Available));
            Assert.That(await builtIn.CheckAvailabilityAsync(CancellationToken.None), Is.SameAs(FieldTypeAvailability.Available));
        });
        foreach (var type in TestFieldTypes.Registry.Types.Where(type => type.Declaration.Alias != "captcha"))
        {
            Assert.That(await type.CheckAvailabilityAsync(CancellationToken.None), Is.SameAs(FieldTypeAvailability.Available), $"{type.Declaration.Alias}: only the captcha needs configuration");
        }
    }

    [Test]
    public async Task TheCaptcha_IsNotConfigured_UntilAProviderHasItsKeys()
    {
        var none = await new CaptchaFieldType(Providers(false, false)).CheckAvailabilityAsync(CancellationToken.None);
        var one = await new CaptchaFieldType(Providers(false, true)).CheckAvailabilityAsync(CancellationToken.None);
        var built = await TestFieldTypes.Registry.Types.Single(type => type.Declaration.Alias == "captcha").CheckAvailabilityAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That((none.IsAvailable, none.Reason), Is.EqualTo((false, "#jfbFieldTypeAvailability_captchaNoKeys")));
            Assert.That(CaptchaFieldType.NoKeys, Is.EqualTo("#jfbFieldTypeAvailability_captchaNoKeys"));
            Assert.That(one, Is.SameAs(FieldTypeAvailability.Available));
            Assert.That((built.IsAvailable, built.Reason), Is.EqualTo((false, CaptchaFieldType.NoKeys)), "the built-in type of a site without keys");
        });
    }

    [Test]
    public async Task ATypeOfTheBaseClass_SaysWhatItLacks_ThroughTheInterface()
    {
        IFormBuilderFieldType keyed = new Keyed();

        var answer = await keyed.CheckAvailabilityAsync(CancellationToken.None);

        Assert.That((answer.IsAvailable, answer.Reason), Is.EqualTo((false, "No payment provider has its keys.")));
    }

    [Test]
    public async Task TheService_AnswersEachTypeInTheRegistrysOrder_WithItsOwnAnswer()
    {
        var keys = FieldTypeAvailability.NotConfigured("#acme_noKeys");
        var (service, logger) = Service(
            new Checked("acme.ready", _ => ValueTask.FromResult(FieldTypeAvailability.Available)),
            new Checked("acme.keys", async token =>
            {
                await Task.Yield();
                return keys;
            }));

        var answers = await service.CheckAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(answers, Is.EqualTo(new[] { FieldTypeAvailability.Available, keys }));
            Assert.That(logger.Entries, Is.Empty);
        });
    }

    [Test]
    public async Task ACheckThatThrowsOrAnswersNothing_IsNotConfigured_BecauseItsSetupCouldNotBeChecked_AndLogged()
    {
        var (service, logger) = Service(
            new Checked("acme.throws", _ => throw new InvalidOperationException("No connection.")),
            new Checked("acme.faults", async _ =>
            {
                await Task.Yield();
                throw new InvalidOperationException("Timed out upstream.");
            }),
            new Checked("acme.null", _ => ValueTask.FromResult<FieldTypeAvailability>(null!)),
            new Checked("acme.cancels", _ => throw new OperationCanceledException()));

        var answers = await service.CheckAsync(CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(answers.Select(answer => (answer.IsAvailable, answer.Reason)), Is.All.EqualTo((false, FieldTypeAvailabilityService.CheckFailed)));
            Assert.That(FieldTypeAvailabilityService.CheckFailed, Is.EqualTo("#jfbFieldTypeAvailability_checkFailed"));
            Assert.That(logger.Entries.Select(entry => (entry.Level, entry.Exception?.Message)).OrderBy(entry => entry.Message, StringComparer.Ordinal), Is.EqualTo(new[]
            {
                (LogLevel.Error, (string?)null),
                (LogLevel.Error, "No connection."),
                (LogLevel.Error, "The operation was canceled."),
                (LogLevel.Error, "Timed out upstream."),
            }));
            Assert.That(logger.Entries.Select(entry => entry.Message), Has.Some.Contains("'acme.throws'").And.Some.Contains("'acme.null'").And.Some.Contains("'acme.faults'").And.Some.Contains("'acme.cancels'"));
        });
    }

    [Test]
    public async Task ACheckThatTakesLongerThanFiveSeconds_IsNotConfigured_TellsTheCheckToStop_AndLogsAWarning()
    {
        var timers = new ManualTimers();
        var listening = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var given = CancellationToken.None;
        var (service, logger) = Service(timers, new Checked("acme.slow", async token =>
        {
            // The token itself, not a callback on it: the check may end, and drop what it registered, before the
            // callbacks of its cancellation have all run.
            given = token;
            listening.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return FieldTypeAvailability.Available;
        }));

        var answering = service.CheckAsync(CancellationToken.None);
        await WaitForTimerAsync(timers);
        Assert.That(timers.Timers[0].DueTime, Is.EqualTo(TimeSpan.FromSeconds(5)).And.EqualTo(FieldTypeAvailabilityService.Limit));
        // The thread pool starts the check when it has a thread to spare, which a busy test run may delay for seconds;
        // the limit passes once the check listens to its token.
        await listening.Task.WaitAsync(Hang);
        timers.FireAll();
        var answers = await answering.WaitAsync(Hang);

        Assert.Multiple(() =>
        {
            Assert.That((answers[0].IsAvailable, answers[0].Reason), Is.EqualTo((false, FieldTypeAvailabilityService.CheckFailed)));
            Assert.That(given.IsCancellationRequested, Is.True, "the check's token is cancelled before the answer comes");
            Assert.That(logger.Entries.Select(entry => (entry.Level, entry.Message)), Is.EqualTo(new[]
            {
                (LogLevel.Warning, "The field type 'acme.slow' took more than 5 seconds to say whether it is configured; the designer shows it as not configured"),
            }));
        });
    }

    [Test]
    public async Task ACheckThatLooksAtItsTokenOnlyAfterTheLimit_FindsItCancelled()
    {
        var timers = new ManualTimers();
        var answered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (service, _) = Service(timers, new Checked("acme.late", async token =>
        {
            // By now the service has answered and let go of the source that gives the token.
            await answered.Task;
            using var _ = token.Register(() => stopped.TrySetResult());
            return FieldTypeAvailability.Available;
        }));

        var answering = service.CheckAsync(CancellationToken.None);
        await WaitForTimerAsync(timers);
        timers.FireAll();
        var answers = await answering.WaitAsync(Hang);
        answered.SetResult();
        await stopped.Task.WaitAsync(Hang);

        Assert.That(answers[0].Reason, Is.EqualTo(FieldTypeAvailabilityService.CheckFailed));
    }

    [Test]
    public async Task ACheckThatBlocksItsThread_CostsNoMoreThanTheLimit()
    {
        var timers = new ManualTimers();
        using var release = new ManualResetEventSlim();
        var (service, _) = Service(timers, new Checked("acme.blocking", _ =>
        {
            // A check that ignores its token and blocks.
            release.Wait(TimeSpan.FromSeconds(30), CancellationToken.None);
            return ValueTask.FromResult(FieldTypeAvailability.Available);
        }));

        var answering = service.CheckAsync(CancellationToken.None);
        await WaitForTimerAsync(timers);
        timers.FireAll();
        var answers = await answering.WaitAsync(Hang);
        release.Set();

        Assert.That(answers[0].Reason, Is.EqualTo(FieldTypeAvailabilityService.CheckFailed), "a check that blocks runs on the thread pool, so the answer does not wait for it");
    }

    [Test]
    public async Task TheRequestsCancellation_IsPassedOn_BeforeAndDuringTheChecks()
    {
        using var before = new CancellationTokenSource();
        await before.CancelAsync();
        var asked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (service, logger) = Service(new Checked("acme.ready", _ =>
        {
            asked.TrySetResult();
            return ValueTask.FromResult(FieldTypeAvailability.Available);
        }));
        Assert.That(async () => await service.CheckAsync(before.Token), Throws.InstanceOf<OperationCanceledException>(), "cancelled before");

        // A check that had started would have run on the thread pool long before this.
        Assert.That(await Task.WhenAny(asked.Task, Task.Delay(TimeSpan.FromMilliseconds(500))), Is.Not.SameAs(asked.Task), "a request cancelled before asks no type");

        using var during = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (waiting, waitingLogger) = Service(new Checked("acme.waits", async token =>
        {
            started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return FieldTypeAvailability.Available;
        }));
        var answering = waiting.CheckAsync(during.Token);
        await started.Task.WaitAsync(Hang);
        await during.CancelAsync();

        Assert.Multiple(() =>
        {
            Assert.That(async () => await answering, Throws.InstanceOf<OperationCanceledException>(), "cancelled while a check waits");
            Assert.That(logger.Entries.Concat(waitingLogger.Entries), Is.Empty, "a cancelled request is no broken check");
        });
    }

    private static FormBuilderCaptchaProviderCollection Providers(params bool[] configured) =>
        new(() => configured.Select(IFormBuilderCaptchaProvider (keys) => new Provider(keys)));

    private static (FieldTypeAvailabilityService Service, RecordingLogger Logger) Service(params IFormBuilderFieldType[] types) =>
        Service(TimeProvider.System, types);

    private static (FieldTypeAvailabilityService Service, RecordingLogger Logger) Service(TimeProvider time, params IFormBuilderFieldType[] types)
    {
        var logger = new RecordingLogger();
        return (new FieldTypeAvailabilityService(new FieldTypeRegistry(types), time, logger), logger);
    }

    // The time limit's timer, which the service sets as it starts each check.
    private static async Task WaitForTimerAsync(ManualTimers timers)
    {
        var waited = TimeSpan.Zero;
        while (timers.Timers.Count == 0)
        {
            Assert.That(waited, Is.LessThan(TimeSpan.FromSeconds(10)), "the service sets its time limit");
            await Task.Delay(10);
            waited += TimeSpan.FromMilliseconds(10);
        }
    }

    private static FieldTypeDeclaration Declare(string alias) => new()
    {
        Alias = alias,
        Name = alias,
        Icon = "icon-settings",
        PaletteGroup = FieldPaletteGroup.Content,
        Features = FieldFeatures.VisibilityRules,
    };

    // A captcha provider with or without its keys.
    private sealed class Provider(bool configured) : IFormBuilderCaptchaProvider
    {
        public string Name => "acme";

        public bool IsConfigured => configured;

        public FormBuilderCaptchaClient? Client => null;

        public Task<FormBuilderCaptchaOutcome> VerifyAsync(FormBuilderCaptchaCheck check, CancellationToken cancellationToken) => Task.FromResult(FormBuilderCaptchaOutcome.Passed);
    }

    // A type whose availability a test decides.
    private sealed class Checked(string alias, Func<CancellationToken, ValueTask<FieldTypeAvailability>> check) : IFormBuilderFieldType
    {
        public FieldTypeDeclaration Declaration { get; } = Declare(alias);

        public FieldSettingsSchema SettingsSchema => FieldSettingsSchema.Empty;

        public ValueTask<FieldTypeAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken) => check(cancellationToken);
    }

    // A type of the base class that needs configuration the site lacks.
    private sealed class Keyed() : FormBuilderFieldTypeBase<NoFieldSettings>(Declare("acme.payment"))
    {
        public override ValueTask<FieldTypeAvailability> CheckAvailabilityAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(FieldTypeAvailability.NotConfigured("No payment provider has its keys."));
    }

    // A type written against the interface alone, whose availability is the interface's.
    private sealed class Plain : IFormBuilderFieldType
    {
        public FieldTypeDeclaration Declaration { get; } = Declare("plain");

        public FieldSettingsSchema SettingsSchema => FieldSettingsSchema.Empty;
    }

    private sealed class RecordingLogger : ILogger<FieldTypeAvailabilityService>
    {
        private readonly Lock _lock = new();
        private readonly List<(LogLevel Level, string Message, Exception? Exception)> _entries = [];

        public IReadOnlyList<(LogLevel Level, string Message, Exception? Exception)> Entries
        {
            get
            {
                lock (_lock)
                {
                    return [.. _entries];
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_lock)
            {
                _entries.Add((logLevel, formatter(state, exception), exception));
            }
        }
    }
}
