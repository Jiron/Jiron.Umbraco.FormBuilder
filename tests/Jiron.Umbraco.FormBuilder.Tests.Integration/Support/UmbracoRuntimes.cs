using System.Reflection;
using NUnit.Framework.Interfaces;
using Umbraco.Cms.Infrastructure.Runtime;

[assembly: Jiron.Umbraco.FormBuilder.Tests.Integration.Support.ReleaseUmbracoRuntimes]

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Support;

/// <summary>
/// Umbraco's runtime listens to the process's unhandled exceptions and stays listening once its host is disposed, which a
/// site never notices, as it has one runtime for its life. A test run builds a host per test, so every test's host, its
/// services and its MVC caches stayed reachable from that static event: about 25 GB by the two-thousandth test, past this
/// machine's memory, where the run paged until late tests' requests timed out (the Phase 11 gate). After each test the
/// runtimes' listeners go, and their hosts with them.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class ReleaseUmbracoRuntimesAttribute : Attribute, ITestAction
{
    /// <inheritdoc />
    public ActionTargets Targets => ActionTargets.Test;

    /// <inheritdoc />
    public void BeforeTest(ITest test)
    {
    }

    /// <inheritdoc />
    public void AfterTest(ITest test) => UmbracoRuntimes.Release();
}

/// <summary>The runtimes that listen to the process's unhandled exceptions.</summary>
internal static class UmbracoRuntimes
{
    // AppDomain.UnhandledException keeps its listeners in a static field of AppContext; its name is the runtime's own, so
    // it is found by its type.
    private static readonly FieldInfo Listeners =
        typeof(AppContext).GetFields(BindingFlags.Static | BindingFlags.NonPublic).SingleOrDefault(field => field.FieldType == typeof(UnhandledExceptionEventHandler))
        ?? throw new InvalidOperationException("The process's unhandled exception listeners were not found on AppContext.");

    /// <summary>How many of Umbraco's runtimes listen now.</summary>
    public static int Listening => Of().Count;

    /// <summary>Stops every runtime's listening; returns how many did.</summary>
    public static int Release()
    {
        var runtimes = Of();
        foreach (var listener in runtimes)
        {
            AppDomain.CurrentDomain.UnhandledException -= listener;
        }

        return runtimes.Count;
    }

    private static List<UnhandledExceptionEventHandler> Of() =>
        Listeners.GetValue(null) is UnhandledExceptionEventHandler listeners
            ? [.. listeners.GetInvocationList().Cast<UnhandledExceptionEventHandler>().Where(listener => listener.Target is CoreRuntime)]
            : [];
}
