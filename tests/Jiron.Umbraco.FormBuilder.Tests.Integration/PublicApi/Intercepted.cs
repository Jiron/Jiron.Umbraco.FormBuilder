using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.PublicApi;

/// <summary>
/// A service with every call passed on, except the calls a test hooks by the method's name: the hook gets the arguments
/// and the call itself, and answers in its place, so a test can make the store answer as it would a moment later or
/// earlier. DispatchProxy derives the proxy from it, so it stays unsealed.
/// </summary>
/// <typeparam name="T">The service's interface.</typeparam>
#pragma warning disable CA1852
internal class Intercepted<T> : DispatchProxy
    where T : class
{
    private readonly ConcurrentDictionary<string, Func<object?[], Func<object?>, object?>> _hooks = new(StringComparer.Ordinal);
    private T _inner = null!;

    /// <summary>A service over <paramref name="inner"/>.</summary>
    public static T Over(T inner)
    {
        var proxy = Create<T, Intercepted<T>>();
        ((Intercepted<T>)(object)proxy)._inner = inner;
        return proxy;
    }

    /// <summary>The hooks of a service made by <see cref="Over"/>.</summary>
    public static Intercepted<T> Of(T service) => (Intercepted<T>)(object)service;

    /// <summary>Answers every call of the method with the hook, until <see cref="Off"/>.</summary>
    public void On(string method, Func<object?[], Func<object?>, object?> hook) => _hooks[method] = hook;

    /// <summary>Passes every call of the method on again.</summary>
    public void Off(string method) => _hooks.TryRemove(method, out _);

    /// <inheritdoc />
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);
        object? Call() => Pass(targetMethod, args);
        return _hooks.TryGetValue(targetMethod.Name, out var hook) ? hook(args ?? [], Call) : Call();
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
