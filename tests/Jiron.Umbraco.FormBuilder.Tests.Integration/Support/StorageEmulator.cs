using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Jiron.Umbraco.FormBuilder.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Support;

/// <summary>
/// The local storage emulator the blob store's tests run against (ROADMAP 15.2, ADR-0234): Azurite's blob service, a
/// development tool installed with the repository's scripts (<c>npm ci --prefix scripts</c>, as CI does before the tests),
/// started in memory on a port the system assigns, without telemetry, for an account of its own whose key is made for the
/// run and never written down. Stopping it stops its whole process tree and removes its folder.
/// </summary>
public sealed partial class StorageEmulator : IAsyncDisposable
{
    private const string Account = "jfbtests";

    private readonly Process _process;
    private readonly string _folder;

    private StorageEmulator(Process process, string folder, int port, string key)
    {
        _process = process;
        _folder = folder;
        ConnectionString = $"DefaultEndpointsProtocol=http;AccountName={Account};AccountKey={key};BlobEndpoint=http://127.0.0.1:{port}/{Account};";
        BlobEndpoint = new Uri($"http://127.0.0.1:{port}/{Account}/");
    }

    /// <summary>The connection string of the run's account.</summary>
    public string ConnectionString { get; }

    /// <summary>The blob service's address for the run's account.</summary>
    public Uri BlobEndpoint { get; }

    /// <summary>Starts the emulator and waits until it listens.</summary>
    /// <returns>The running emulator.</returns>
    /// <exception cref="InvalidOperationException">Azurite is not installed, or it did not start within a minute.</exception>
    public static async Task<StorageEmulator> StartAsync()
    {
        var main = RepositoryRoot.Path(typeof(StorageEmulator).Assembly, "scripts", "node_modules", "azurite", "dist", "src", "blob", "main.js");
        if (!File.Exists(main))
        {
            throw new InvalidOperationException("Azurite is missing: run `npm ci --prefix scripts` in the repository first.");
        }

        var folder = Path.Combine(Path.GetTempPath(), "jfb-azurite-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var start = new ProcessStartInfo("node")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in new[] { main, "--blobHost", "127.0.0.1", "--blobPort", "0", "--inMemoryPersistence", "--extentMemoryLimit", "256", "--disableTelemetry", "--silent", "--skipApiVersionCheck" })
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["AZURITE_ACCOUNTS"] = $"{Account}:{key}";
        // In memory, it writes no data; its debug log and lock files would go to its working folder.
        start.WorkingDirectory = folder;
        var process = Process.Start(start) ?? throw new InvalidOperationException("Azurite did not start.");
        var listening = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        process.OutputDataReceived += (_, line) =>
        {
            if (line.Data is { } text && Listening().Match(text) is { Success: true } found)
            {
                listening.TrySetResult(int.Parse(found.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
            }
        };
        process.ErrorDataReceived += (_, _) => { };
        process.Exited += (_, _) => listening.TrySetException(new InvalidOperationException("Azurite stopped before it listened."));
        process.EnableRaisingEvents = true;
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            var port = await listening.Task.WaitAsync(TimeSpan.FromMinutes(1));
            return new StorageEmulator(process, folder, port, key);
        }
        catch
        {
            Stop(process, folder);
            throw;
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Stop(_process, _folder);
        return ValueTask.CompletedTask;
    }

    private static void Stop(Process process, string folder)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(10_000);
            }
        }
        catch (InvalidOperationException)
        {
            // It had stopped already.
        }
        finally
        {
            process.Dispose();
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
                // Left for the system's temp cleanup.
            }
        }
    }

    [GeneratedRegex(@"listens on https?://127\.0\.0\.1:(\d+)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex Listening();
}
