using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Actions;

/// <summary>
/// Two processors on one database (ROADMAP 8.1, ADR-0007, research/spike-background.md): SQLite cannot be shared reliably between
/// hosts, so the second one runs in this process, with claims of its own, while submissions come in. The faults of the spike come
/// from a hash of the seed: receivers that always refuse, refusals, attempts slower than the lease, attempts cut off before and
/// after their effect, and servers that walk away before they record anything. The verdict holds every run to its state: a
/// succeeded run has one effect, a failed one none, a skipped one no attempt, an uncertain one at most one, a pending one waits behind
/// an uncertain run that stops the rest, and nothing has its effect twice. A claim without its state condition fails this test (the
/// mutant in the step's checks), and the real check with two hosts on SQL Server runs in CI.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class TwoProcessorsTests : ActionTestBase
{
    private const int EntryCount = 60;

    private readonly ConcurrentDictionary<string, int> _effects = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _firstStarts = new();
    private readonly ConcurrentDictionary<string, bool> _started = new(StringComparer.Ordinal);

    [TestCase(1)]
    [TestCase(2)]
    public async Task NothingHasItsEffectTwice_AndEveryRunEndsInTheStateItsAttemptsExplain(int seed)
    {
        foreach (var name in new[] { "hook", "mail", "content", "last" })
        {
            Scripted.Scripts[name] = (context, token) => AttemptAsync(seed, context, token);
        }

        var stops = await ActionFormAsync(Action("hook", stopsTheRest: true), Action("mail"), Action("content"), Action("last"));
        var plain = await ActionFormAsync(Action("hook"), Action("mail"), Action("content"), Action("last"));
        var serverA = Server(seed, "server-a");
        var serverB = Server(seed, "server-b");

        using var done = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var submitted = 0;
        var entries = new ConcurrentBag<StoredEntry>();
        var submitting = Task.Run(async () =>
        {
            for (var i = 0; i < EntryCount; i++)
            {
                entries.Add(await SubmitAsync(i % 4 == 0 ? stops : plain, "{\"name\":\"Entry " + i + "\"}"));
                Interlocked.Increment(ref submitted);
            }
        });

        // A minute of the clock goes by every few milliseconds, so leases run out and retries fall due while both servers work.
        var ticking = Task.Run(async () =>
        {
            while (!done.IsCancellationRequested)
            {
                Clock.Advance(TimeSpan.FromSeconds(15));
                await Task.Delay(TimeSpan.FromMilliseconds(5), CancellationToken.None);
            }
        });
        async Task WorkAsync(ActionRunQueue queue, string claimant)
        {
            while (!done.IsCancellationRequested)
            {
                await queue.PassAsync(claimant, Clock.GetUtcNow() + TimeSpan.FromHours(1), CancellationToken.None);
                await Task.Delay(TimeSpan.FromMilliseconds(2), CancellationToken.None);
            }
        }

        var workers = Task.WhenAll(WorkAsync(serverA, "server-a"), WorkAsync(serverB, "server-b"));
        await submitting.WaitAsync(done.Token);
        while (!done.IsCancellationRequested && !await SettledAsync(entries))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(100), CancellationToken.None);
        }

        var settled = !done.IsCancellationRequested;
        await done.CancelAsync();
        await Task.WhenAll(workers, ticking);

        var all = new List<(StoredEntry Entry, StoredActionRun Run)>();
        foreach (var entry in entries)
        {
            all.AddRange((await Runs.ListForEntryAsync(entry.Id)).Select(run => (entry, run)));
        }

        var violations = new List<string>();
        foreach (var batch in all.GroupBy(item => item.Run.ChangeKey))
        {
            var runs = batch.OrderBy(item => item.Run.SortOrder).ToList();
            foreach (var (entry, run) in runs)
            {
                var effects = _effects.GetValueOrDefault(Key(entry, run));
                switch (run.State)
                {
                    case ActionRunState.Succeeded when effects != 1:
                    case ActionRunState.Failed when effects != 0 || run.Attempts != ActionAttempts.MaxAttempts:
                    case ActionRunState.Skipped when effects != 0 || run.Attempts != 0:
                    case ActionRunState.Uncertain when effects > 1:
                    case ActionRunState.Running:
                        violations.Add($"{run.State} with {effects} effects after {run.Attempts} attempts");
                        break;
                    case ActionRunState.Pending when !runs.Any(earlier => earlier.Run.SortOrder < run.SortOrder && earlier.Run.StopOnFailure && earlier.Run.State == ActionRunState.Uncertain):
                        violations.Add($"pending behind nothing, after {run.Attempts} attempts");
                        break;
                }
            }

            var order = _firstStarts.ToList();
            var started = runs.Where(item => _started.ContainsKey(Key(item.Entry, item.Run))).Select(item => order.IndexOf(Key(item.Entry, item.Run))).ToList();
            if (!started.SequenceEqual(started.Order()))
            {
                violations.Add("a run started before an earlier one of its batch");
            }
        }

        var states = all.GroupBy(item => item.Run.State).ToDictionary(group => group.Key, group => group.Count());
        var twice = _effects.Values.Count(count => count > 1);
        var claimants = all.Where(item => item.Run.LockedBy is not null).Select(item => item.Run.LockedBy!.Split('/')[0]).Distinct().ToList();
        Assert.Multiple(() =>
        {
            Assert.That(settled, Is.True, "every run came to rest in time: " + string.Join(", ", states));
            Assert.That(entries, Has.Count.EqualTo(EntryCount));
            Assert.That(all, Has.Count.EqualTo(EntryCount * 4));
            Assert.That(twice, Is.Zero, "no run has its effect twice");
            Assert.That(violations, Is.Empty);
            Assert.That(claimants, Is.EquivalentTo(new[] { "server-a", "server-b" }), "both servers did work");
            Assert.That(states.GetValueOrDefault(ActionRunState.Failed), Is.GreaterThan(0), "the faults reached a failure for good");
            Assert.That(states.GetValueOrDefault(ActionRunState.Uncertain), Is.GreaterThan(0), "the faults reached an uncertain run");
            Assert.That(states.GetValueOrDefault(ActionRunState.Succeeded), Is.GreaterThan(EntryCount), "most go through");
        });
        await TestContext.Out.WriteLineAsync(string.Join(", ", states.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key} {pair.Value}")));
    }

    private static string Key(StoredEntry entry, StoredActionRun run) => $"{entry.Key:N}.{run.ActionKey:N}.{run.ChangeKey:N}";

    private static int Roll(int seed, string what)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{seed}:{what}"));
        return (int)(BitConverter.ToUInt32(bytes, 0) % 100);
    }

    // Every run came to rest: nothing running, nothing due now or later, unless it waits behind an uncertain run that stops the rest.
    private async Task<bool> SettledAsync(ConcurrentBag<StoredEntry> entries)
    {
        if (entries.Count < EntryCount)
        {
            return false;
        }

        foreach (var entry in entries)
        {
            var runs = await Runs.ListForEntryAsync(entry.Id);
            if (runs.Any(run => run.State == ActionRunState.Running
                || (run.State == ActionRunState.Pending && !runs.Any(earlier => earlier.SortOrder < run.SortOrder && earlier.StopOnFailure && earlier.State == ActionRunState.Uncertain))))
            {
                return false;
            }
        }

        return true;
    }

    private ActionRunQueue Server(int seed, string name)
    {
        var faulty = new WalksAway(Runs, seed, name);
        var attempts = ActivatorUtilities.CreateInstance<ActionAttempts>(Services, (IActionRunRepository)faulty);
        return ActivatorUtilities.CreateInstance<ActionRunQueue>(Services, (IActionRunRepository)faulty, attempts);
    }

    private async Task<ActionResult> AttemptAsync(int seed, ActionRunContext context, CancellationToken token)
    {
        var key = context.DeliveryKey;
        if (_started.TryAdd(key, true))
        {
            _firstStarts.Enqueue(key);
        }

        if (Roll(seed, key) < 4)
        {
            return ActionResult.TryAgain("always_refused");
        }

        var roll = Roll(seed, $"{key}/{context.Attempt}");
        switch (roll)
        {
            case < 12:
                return ActionResult.TryAgain("refused");
            case < 15:
                // Slower than the lease, then delivered.
                var until = Clock.GetUtcNow() + ActionRunQueue.Lease + TimeSpan.FromMinutes(1);
                while (Clock.GetUtcNow() < until)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(5), CancellationToken.None);
                }

                _effects.AddOrUpdate(key, 1, (_, count) => count + 1);
                return ActionResult.Succeeded;
            case < 17:
                _effects.AddOrUpdate(key, 1, (_, count) => count + 1);
                return ActionResult.Uncertain("cut_after");
            case < 18:
                return ActionResult.Uncertain("cut_before");
            default:
                token.ThrowIfCancellationRequested();
                _effects.AddOrUpdate(key, 1, (_, count) => count + 1);
                return ActionResult.Succeeded;
        }
    }

    // The repository as a server sees it that may walk away after an attempt: its outcome never reaches the database.
    private sealed class WalksAway(IActionRunRepository inner, int seed, string server) : IActionRunRepository
    {
        private readonly ConcurrentDictionary<string, bool> _gone = new(StringComparer.Ordinal);

        public Task<IReadOnlyList<StoredActionRun>?> AddAsync(long entryId, int formVersionId, Guid changeKey, IReadOnlyList<NewActionRun> runs, TimeSpan wait = default, CancellationToken cancellationToken = default) =>
            inner.AddAsync(entryId, formVersionId, changeKey, runs, wait, cancellationToken);

        public Task<StoredActionRun?> GetAsync(long runId, CancellationToken cancellationToken = default) => inner.GetAsync(runId, cancellationToken);

        public Task<IReadOnlyList<StoredActionRun>> ListForEntryAsync(long entryId, CancellationToken cancellationToken = default) => inner.ListForEntryAsync(entryId, cancellationToken);

        public Task<IReadOnlyList<long>> DueAsync(int take, CancellationToken cancellationToken = default) => inner.DueAsync(take, cancellationToken);

        public async Task<StoredActionRun?> ClaimAsync(long runId, string claimant, TimeSpan lease, CancellationToken cancellationToken = default)
        {
            var claimed = await inner.ClaimAsync(runId, claimant, lease, cancellationToken);
            if (claimed is not null && Roll(seed, $"{server}/{runId}/{claimed.Attempts}") < 3)
            {
                _gone[claimed.LockedBy!] = true;
            }

            return claimed;
        }

        public Task<bool> SucceedAsync(long runId, string token, CancellationToken cancellationToken = default) =>
            _gone.ContainsKey(token) ? Task.FromResult(true) : inner.SucceedAsync(runId, token, cancellationToken);

        public Task<bool> RetryAsync(long runId, string token, TimeSpan delay, DeliveryError error, CancellationToken cancellationToken = default) =>
            _gone.ContainsKey(token) ? Task.FromResult(true) : inner.RetryAsync(runId, token, delay, error, cancellationToken);

        public Task<bool> FailAsync(long runId, string token, DeliveryError error, CancellationToken cancellationToken = default) =>
            _gone.ContainsKey(token) ? Task.FromResult(true) : inner.FailAsync(runId, token, error, cancellationToken);

        public Task<bool> CutOffAsync(long runId, string token, DeliveryError error, CancellationToken cancellationToken = default) =>
            _gone.ContainsKey(token) ? Task.FromResult(true) : inner.CutOffAsync(runId, token, error, cancellationToken);

        public Task<int> ExpireLeasesAsync(CancellationToken cancellationToken = default) => inner.ExpireLeasesAsync(cancellationToken);

        public Task<int> CountFailedSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default) => inner.CountFailedSinceAsync(since, cancellationToken);

        public Task<StoredActionRun?> RequeueAsync(long runId, ActionRunState expected, CancellationToken cancellationToken = default) => inner.RequeueAsync(runId, expected, cancellationToken);

        public Task<int> SkipChangeAsync(Guid changeKey, CancellationToken cancellationToken = default) => inner.SkipChangeAsync(changeKey, cancellationToken);

        public Task<int> SkipChangeOfEntryAsync(Guid changeKey, long entryId, CancellationToken cancellationToken = default) => inner.SkipChangeOfEntryAsync(changeKey, entryId, cancellationToken);

        public Task<int> SkipUnstartedOfEntryAsync(long entryId, CancellationToken cancellationToken = default) => inner.SkipUnstartedOfEntryAsync(entryId, cancellationToken);

        public Task<int> CountOfChangeAsync(Guid changeKey, CancellationToken cancellationToken = default) => inner.CountOfChangeAsync(changeKey, cancellationToken);
    }
}
