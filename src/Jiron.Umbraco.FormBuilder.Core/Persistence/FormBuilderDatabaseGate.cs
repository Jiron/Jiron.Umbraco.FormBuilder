using Umbraco.Cms.Infrastructure.Persistence;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence;

/// <inheritdoc cref="IFormBuilderDatabaseGate" />
/// <remarks>The provider is read on first use, because the database may only be configured during the install.</remarks>
internal sealed class FormBuilderDatabaseGate(IUmbracoDatabaseFactory databaseFactory) : IFormBuilderDatabaseGate, IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    // Set inside RunAsync, so it flows into the work and everything the work awaits, but not back to the caller.
    private readonly AsyncLocal<bool> _inside = new();
    private bool? _gated;

    /// <summary>True when the gate queues work (SQLite). Not cached until the provider is known.</summary>
    internal bool Gated
    {
        get
        {
            if (_gated is { } known)
            {
                return known;
            }

            var provider = databaseFactory.SqlContext?.SqlSyntax?.ProviderName;
            if (string.IsNullOrEmpty(provider))
            {
                return false;
            }

            var gated = provider.Contains("Sqlite", StringComparison.OrdinalIgnoreCase);
            _gated = gated;
            return gated;
        }
    }

    /// <inheritdoc />
    public async Task<T> RunAsync<T>(Func<Task<T>> work, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (!Gated || _inside.Value)
        {
            return await work();
        }

        await _semaphore.WaitAsync(cancellationToken);
        try
        {
            _inside.Value = true;
            return await work();
        }
        finally
        {
            _inside.Value = false;
            _semaphore.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose() => _semaphore.Dispose();
}
