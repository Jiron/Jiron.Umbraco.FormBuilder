using Umbraco.Cms.Core.Cache;
using Umbraco.Extensions;

namespace Jiron.Umbraco.FormBuilder.Core;

/// <summary>
/// One load per cached value and server (ADR-0043, ADR-0048, amended in 4.8c): concurrent misses await the same read
/// instead of each reading the database. The load lives in the runtime cache under its loading key, which a change
/// clears together with the value, so a load that began before the change answers its callers but is not kept, and
/// the next miss reads again.
/// </summary>
internal static class RuntimeCacheLoads
{
    /// <summary>
    /// The value under <paramref name="key"/>, or the one <paramref name="load"/> reads, shared with every miss that comes
    /// while it runs. The load runs without the caller's cancellation, as other callers may wait for it; the caller
    /// stops waiting when <paramref name="cancellationToken"/> is cancelled. A load that fails is not kept.
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

        var loading = cache.GetCacheItem(loadingKey, () => new Loading<T>(load), duration)!;
        T value;
        try
        {
            value = await loading.Value.WaitAsync(cancellationToken);
        }
        catch (Exception) when (loading.Value.IsFaulted || loading.Value.IsCanceled)
        {
            Forget(cache, loadingKey, loading);
            throw;
        }

        // Kept only while the load is still the current one: a change cleared it, or the first caller kept it already.
        if (ReferenceEquals(cache.Get(loadingKey), loading))
        {
            cache.Insert(key, () => value, duration);
            Forget(cache, loadingKey, loading);
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

    // A load that starts once, when the first caller asks for it. Awaiting it inside an async method turns a load that
    // throws before its first await into a failed task, which the callers forget like any other failure.
    private sealed class Loading<T>(Func<Task<T>> load)
    {
        private readonly Lazy<Task<T>> _value = new(() => RunAsync(load), LazyThreadSafetyMode.ExecutionAndPublication);

        public Task<T> Value => _value.Value;

        private static async Task<T> RunAsync(Func<Task<T>> load) => await load();
    }
}
