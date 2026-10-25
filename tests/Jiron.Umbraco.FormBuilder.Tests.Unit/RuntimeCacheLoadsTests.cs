using Jiron.Umbraco.FormBuilder.Core;
using Umbraco.Cms.Core.Cache;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit;

/// <summary>
/// One load per cached value and server (ADR-0043, ADR-0048, amended in 4.8c), on Umbraco's own runtime cache:
/// concurrent misses share a load, a change during it keeps it from being cached, a failed load is not kept, and a
/// caller that stops waiting leaves the load to the others.
/// </summary>
[TestFixture]
public sealed class RuntimeCacheLoadsTests
{
    private const string Prefix = "JFB_TEST.";
    private const string Key = Prefix + "value";
    private const string Loading = Key + ".loading";
    private static readonly TimeSpan Duration = TimeSpan.FromHours(1);

    private ObjectCacheAppCache _cache = null!;
    private int _loads;

    [SetUp]
    public void SetUp()
    {
        _cache = new ObjectCacheAppCache();
        _loads = 0;
    }

    [TearDown]
    public void TearDown() => _cache.Dispose();

    [Test]
    public async Task ConcurrentMisses_ShareOneLoad_AndTheValueIsKept()
    {
        var gate = Gate();

        var first = GetAsync(() => Counted(gate.Task));
        var second = GetAsync(() => Counted(gate.Task));
        gate.SetResult("value");
        var (one, two) = (await first, await second);
        var hit = await GetAsync(() => Counted(Task.FromResult("other")));

        Assert.Multiple(() =>
        {
            Assert.That((one, two, hit), Is.EqualTo(("value", "value", "value")));
            Assert.That(_loads, Is.EqualTo(1), "one read for both misses, none for the hit");
            Assert.That(_cache.Get(Key), Is.EqualTo("value"), "kept");
            Assert.That(_cache.Get(Loading), Is.Null, "the load is done with");
        });
    }

    [Test]
    public async Task AChangeDuringTheLoad_AnswersItsCallers_ButTheValueIsNotKept()
    {
        var gate = Gate();

        var waiting = GetAsync(() => Counted(gate.Task));
        _cache.ClearByKey(Prefix);
        gate.SetResult("before the change");
        var answered = await waiting;
        var kept = _cache.Get(Key);
        var next = await GetAsync(() => Counted(Task.FromResult("after the change")));

        Assert.Multiple(() =>
        {
            Assert.That(answered, Is.EqualTo("before the change"));
            Assert.That(kept, Is.Null, "the rows may be older than the change");
            Assert.That(next, Is.EqualTo("after the change"), "the next miss reads again");
            Assert.That(_loads, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task AFailedLoad_IsNotKept_AndTheNextMissLoadsAgain()
    {
        Assert.That(async () => await GetAsync(() => Counted(Task.FromException<string>(new InvalidOperationException("JFB_TEST: the database is away")))), Throws.InvalidOperationException);
        var loading = _cache.Get(Loading);
        var next = await GetAsync(() => Counted(Task.FromResult("value")));

        Assert.Multiple(() =>
        {
            Assert.That(loading, Is.Null, "the failure is not kept");
            Assert.That(next, Is.EqualTo("value"));
            Assert.That(_loads, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task AFailedLoadThatAChangeOvertook_LeavesTheNextLoadAlone()
    {
        var failing = Gate();
        var later = Gate();

        var overtaken = GetAsync(() => Counted(failing.Task));
        _cache.ClearByKey(Prefix);
        var current = GetAsync(() => Counted(later.Task));
        failing.SetException(new InvalidOperationException("JFB_TEST: the database is away"));
        Assert.That(async () => await overtaken, Throws.InvalidOperationException);
        later.SetResult("value");

        Assert.Multiple(async () =>
        {
            Assert.That(await current, Is.EqualTo("value"));
            Assert.That(_cache.Get(Key), Is.EqualTo("value"), "the failure cleared only its own load");
            Assert.That(_loads, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task ALoadThatThrowsBeforeItsFirstAwait_IsNotKeptEither()
    {
        Assert.That(async () => await GetAsync(() => Counted(Broken())), Throws.InvalidOperationException);
        var loading = _cache.Get(Loading);
        var next = await GetAsync(() => Counted(Task.FromResult("value")));

        Assert.Multiple(() =>
        {
            Assert.That(loading, Is.Null, "the failure is not kept");
            Assert.That(next, Is.EqualTo("value"));
        });

        static Task<string> Broken() => throw new InvalidOperationException("JFB_TEST: the load broke before it began");
    }

    [Test]
    public async Task ACallerThatStopsWaiting_LeavesTheLoadToTheOthers()
    {
        var gate = Gate();
        using var stop = new CancellationTokenSource();

        var impatient = GetAsync(() => Counted(gate.Task), stop.Token);
        var patient = GetAsync(() => Counted(gate.Task));
        await stop.CancelAsync();
        Assert.That(async () => await impatient, Throws.InstanceOf<OperationCanceledException>());
        gate.SetResult("value");

        Assert.Multiple(async () =>
        {
            Assert.That(await patient, Is.EqualTo("value"));
            Assert.That(_loads, Is.EqualTo(1));
            Assert.That(_cache.Get(Key), Is.EqualTo("value"));
        });
    }

    private static TaskCompletionSource<string> Gate() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private Task<string> GetAsync(Func<Task<string>> load, CancellationToken cancellationToken = default) =>
        RuntimeCacheLoads.GetAsync(_cache, Key, Loading, load, Duration, cancellationToken);

    private Task<string> Counted(Task<string> value)
    {
        Interlocked.Increment(ref _loads);
        return value;
    }
}
