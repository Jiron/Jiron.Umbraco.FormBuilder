using System.Collections.Concurrent;
using Jiron.Umbraco.FormBuilder.Core.Files;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Support;

/// <summary>
/// A file store in memory, for the tests of what deletes files (ADR-0041): it keeps what is saved by id, as the contract
/// of <see cref="IFormBuilderFileStore"/> says, can be made to refuse its deletes, and runs a callback after each one.
/// </summary>
internal sealed class MemoryFileStore : IFormBuilderFileStore
{
    private readonly ConcurrentDictionary<string, byte[]> _files = new(StringComparer.Ordinal);

    /// <summary>True to make every delete fail, as a disk that refuses would.</summary>
    public bool FailDeletes { get; set; }

    /// <summary>Runs after each delete with the id it deleted, or null.</summary>
    public Func<string, Task>? AfterDelete { get; set; }

    /// <summary>The ids stored now.</summary>
    public IReadOnlyCollection<string> Ids => [.. _files.Keys];

    /// <inheritdoc />
    public async Task SaveAsync(string id, Stream content, CancellationToken cancellationToken = default)
    {
        FormBuilderFileId.ThrowIfInvalid(id);
        ArgumentNullException.ThrowIfNull(content);
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy, cancellationToken);
        if (!_files.TryAdd(id, copy.ToArray()))
        {
            throw new IOException("A file is stored under the id already.");
        }
    }

    /// <inheritdoc />
    public Task<Stream?> OpenReadAsync(string id, CancellationToken cancellationToken = default)
    {
        FormBuilderFileId.ThrowIfInvalid(id);
        return Task.FromResult<Stream?>(_files.TryGetValue(id, out var bytes) ? new MemoryStream(bytes, writable: false) : null);
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        FormBuilderFileId.ThrowIfInvalid(id);
        cancellationToken.ThrowIfCancellationRequested();
        if (FailDeletes)
        {
            throw new IOException("JFB_TEST: the disk refuses the delete");
        }

        _files.TryRemove(id, out _);
        if (AfterDelete is { } after)
        {
            await after(id);
        }
    }
}
