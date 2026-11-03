using System.Collections.Concurrent;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Setup;

/// <summary>What the setup tests need made up.</summary>
internal static class SetupTestForms
{
    /// <summary>A form that exists, with only what the checklist reads.</summary>
    public static StoredForm Form() => new(
        1,
        Guid.Parse("60000000-0000-4000-8000-000000000002"),
        null,
        "contact",
        "Contact",
        null,
        null,
        0,
        1,
        0,
        null,
        false,
        null,
        null,
        DateTimeOffset.UnixEpoch,
        DateTimeOffset.UnixEpoch,
        null,
        null);

    /// <summary>The default file store, which a probe tells from a store of the site's own.</summary>
    public static PhysicalFileStore PhysicalStore()
    {
        var folders = Substitute.For<IHostFolders>();
        var root = Path.Combine(Path.GetTempPath(), "jfb-setup-tests");
        folders.ContentRoot.Returns(root);
        folders.WebRoot.Returns(Path.Combine(root, "wwwroot"));
        return new PhysicalFileStore(Options.Create(new FormBuilderOptions()), folders, NullLogger<PhysicalFileStore>.Instance);
    }
}

/// <summary>A key-value store in memory, as far as the heartbeat uses it.</summary>
internal sealed class InMemoryKeyValues : IKeyValueRepository
{
    private readonly ConcurrentDictionary<string, string?> _values = new(StringComparer.Ordinal);

    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(_values.GetValueOrDefault(key));

    public Task<IReadOnlyList<KeyValuePair<string, string?>>> ListAsync(string prefix, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task SetAsync(string key, string? value, CancellationToken cancellationToken = default)
    {
        _values[key] = value;
        return Task.CompletedTask;
    }

    public Task<bool> TryAddAsync(string key, string? value, CancellationToken cancellationToken = default) => Task.FromResult(_values.TryAdd(key, value));

    public Task<bool> DeleteAsync(string key, CancellationToken cancellationToken = default) => Task.FromResult(_values.TryRemove(key, out _));

    public Task<bool> TryReplaceAsync(string key, string expected, string? value, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<bool> TryDeleteAsync(string key, string expected, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<int> DeleteOlderAsync(string prefix, DateTime before, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
