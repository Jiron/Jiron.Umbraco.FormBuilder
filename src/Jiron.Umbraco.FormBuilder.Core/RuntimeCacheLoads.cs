using Umbraco.Cms.Core.Cache;
using Umbraco.Extensions;

namespace Jiron.Umbraco.FormBuilder.Core;

/// <summary>
/// One load per cached value and server (ADR-0043, ADR-0048, amended in 4.8c and 4.8g): concurrent misses await the
/// same read instead of each reading the database. The load lives in the runtime cache under its loading key, which a
/// change clears together with the value, so a load that began before the change answers its callers but is not kept,
/// even when the change comes as it ends, and the next miss reads again. The load runs outside the context of the
/// caller that started it.
/// </summary>
internal static class RuntimeCacheLoads
{
    /// <summary>
    /// The value under <paramref name="key"/>, or the one <paramref name="load"/> reads, shared with every miss that comes
    /// while it runs. The load runs without the caller's cancellation, as other callers may wait for it; the caller
    /// stops waiting when <paramref name="cancellationToken"/> is cancelled. A load that fails is forgotten as it fails,
    /// even when every caller stopped waiting for it, so the next miss reads anew.
    /// </summary>
    public static async Task<T> GetAsync<T>(IAppPolicyCache cache, string key, string loadingKey, Func<Task<T>> load, TimeSpan duration, CancellationToken cancellationToken)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(load);
        if (cache.GetCacheItem<T>(key) is { } hit)
        {
            return hit;
        }

        var loading = cache.GetCacheItem(loadingKey, () => new Loading<T>(load, failed => Forget(cache, loadingKey, failed)), duration)!;
        T value;
        try
        {
            value = await loading.Value.WaitAsync(cancellationToken);
        }
        catch (Exception) when (loading.Value.IsFaulted || loading.Value.IsCanceled)
        {
            // The load forgets itself as it fails, but perhaps after its callers hear of it: a caller forgets it before
            // it passes the failure on, so no miss after it waits for the failed load.
            Forget(cache, loadingKey, loading);
            throw;
        }

        // One caller keeps the value, and only while the load is still the current one. A change clears the load with the
        // value, as one step of the cache, so a change between the first check and the insert is found by the second,
        // which takes the value out again: a value read before a change is never kept.
        if (loading.Claim() && ReferenceEquals(cache.Get(loadingKey), loading))
        {
            cache.Insert(key, () => value, duration);
            if (ReferenceEquals(cache.Get(loadingKey), loading))
            {
                Forget(cache, loadingKey, loading);
            }
            else
            {
                cache.Clear(key);
            }
        }

        return value;
    }

    private static void Forget(IAppPolicyCache cache, string loadingKey, object loading)
    {
        if (ReferenceEquals(cache.Get(loadingKey), loading))
        {
            cache.Clear(loadingKey);
        }
    }

    // A load that starts once, when the first caller asks for it, on the thread pool and without that caller's execution
    // context: it is shared, so it runs in no caller's unit of work, Umbraco scope or cancellation (ADR-0043). Task.Run
    // turns a load that throws before its first await into a failed task. A load that fails or is cancelled says so as it
    // ends, whether or not a caller still waits for it, and its error counts as observed.
    private sealed class Loading<T>
    {
        private readonly Lazy<Task<T>> _value;
        private int _claimed;

        public Loading(Func<Task<T>> load, Action<Loading<T>> failed) =>
            _value = new(() => Start(load, failed), LazyThreadSafetyMode.ExecutionAndPublication);

        public Task<T> Value => _value.Value;

        // True for the one caller that keeps the value: the first to ask once the load is done.
        public bool Claim() => Interlocked.Exchange(ref _claimed, 1) == 0;

        private Task<T> Start(Func<Task<T>> load, Action<Loading<T>> failed)
        {
            if (ExecutionContext.IsFlowSuppressed())
            {
                return Run(load, failed);
            }

            using (ExecutionContext.SuppressFlow())
            {
                return Run(load, failed);
            }
        }

        private Task<T> Run(Func<Task<T>> load, Action<Loading<T>> failed)
        {
            var task = Task.Run(load);
            _ = task.ContinueWith(
                (done, self) =>
                {
                    _ = done.Exception;
                    failed((Loading<T>)self!);
                },
                this,
                CancellationToken.None,
                TaskContinuationOptions.NotOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            return task;
        }
    }
}
