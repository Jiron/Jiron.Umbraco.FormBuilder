using System.Reflection;
using System.Runtime.ExceptionServices;
using Jiron.Umbraco.FormBuilder.Core.Forms;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.PublicApi;

/// <summary>
/// The site's form service with every call passed on, except the read of a published form, which a test can count, make
/// fail or hold until it lets go. DispatchProxy derives the proxy from it, so it stays unsealed.
/// </summary>
#pragma warning disable CA1852
internal class WatchedForms : DispatchProxy
{
    private IFormBuilderFormService _inner = null!;
    private int _reads;

    /// <summary>How many reads of a published form the service was asked for.</summary>
    public int PublishedReads => Volatile.Read(ref _reads);

    /// <summary>Whether a read of a published form fails unexpectedly.</summary>
    public bool Fail { get; set; }

    /// <summary>When set, a read of a published form waits until the gate opens.</summary>
    public ReadGate? Gate { get; set; }

    /// <summary>A service over <paramref name="inner"/>.</summary>
    public static WatchedForms Over(IFormBuilderFormService inner)
    {
        var proxy = (WatchedForms)(object)Create<IFormBuilderFormService, WatchedForms>();
        proxy._inner = inner;
        return proxy;
    }

    /// <inheritdoc />
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        if (targetMethod.Name != nameof(IFormBuilderFormService.GetPublishedAsync))
        {
            return Pass(targetMethod, args);
        }

        Interlocked.Increment(ref _reads);
        if (Fail)
        {
            return Task.FromException<PublishedForm?>(new InvalidOperationException("JFB_TEST: the read fails."));
        }

        return Gate is { } gate ? HeldAsync(gate, targetMethod, args) : Pass(targetMethod, args);
    }

    private async Task<PublishedForm?> HeldAsync(ReadGate gate, MethodInfo targetMethod, object?[]? args)
    {
        await gate.PassAsync();
        return await (Task<PublishedForm?>)Pass(targetMethod, args)!;
    }

    private object? Pass(MethodInfo targetMethod, object?[]? args)
    {
        try
        {
            return targetMethod.Invoke(_inner, args);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is { } thrown)
        {
            ExceptionDispatchInfo.Throw(thrown);
            throw;
        }
    }
}
#pragma warning restore CA1852

/// <summary>Holds the reads of published forms until a test opens it, and says when the first one waits.</summary>
internal sealed class ReadGate
{
    private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _open = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Completes when a read waits at the gate.</summary>
    public Task Reached => _reached.Task;

    /// <summary>Lets every read through, the waiting ones and those to come.</summary>
    public void Open() => _open.TrySetResult();

    /// <summary>Waits until the gate opens.</summary>
    public Task PassAsync()
    {
        _reached.TrySetResult();
        return _open.Task;
    }
}
