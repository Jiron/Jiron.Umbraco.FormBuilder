using System.Reflection;
using Microsoft.Extensions.FileProviders;
using Moq;
using NUnit.Framework.Interfaces;
using Umbraco.Cms.Infrastructure.Runtime;
using Umbraco.Cms.Tests.Integration.Implementations;
using Umbraco.Cms.Tests.Integration.Testing;

[assembly: Jiron.Umbraco.FormBuilder.Tests.Integration.Support.ReleaseUmbracoRuntimes]

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Support;

/// <summary>
/// Releases what keeps a test's host in memory once the test ends. A site has one host for its life; a test run builds
/// one per test, so anything that outlives its host keeps every test's host, its services and its MVC caches reachable,
/// past this machine's memory, where the run pages until late tests' requests time out:
/// <list type="bullet">
/// <item>Umbraco's runtime listens to the process's unhandled exceptions and stays listening once its host is disposed
/// (about 25 GB by the two-thousandth test, the Phase 11 gate): <see cref="UmbracoRuntimes"/>.</item>
/// <item>The file providers Umbraco's test helper hands its hosts start file watchers nobody disposes, and its fixture's
/// environment remembers every one (on Umbraco 18, 110 hosts and 3 GB after two minutes): <see cref="TestHostEnvironments"/>.</item>
/// </list>
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
    public void AfterTest(ITest test)
    {
        UmbracoRuntimes.Release();
        TestHostEnvironments.Release(test.Fixture);
    }
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

/// <summary>
/// The file providers Umbraco's test helper hands the hosts of a fixture. Its web host environment is a mock made once per
/// fixture, whose <c>ContentRootFileProvider</c> answers a new provider at every read, and the mock records every answer.
/// A host that watches one starts a file watcher that nobody disposes; its pending read keeps the execution context it
/// started in, which on Umbraco 18 reaches the hosting environment and so the mock, whose records reach every provider
/// handed out so far and, through their watchers' callbacks, every host of the fixture. Once a test ends, the providers
/// handed out during it are disposed, which stops their watchers, and the records go.
/// </summary>
internal static class TestHostEnvironments
{
    // Umbraco's integration test base keeps the fixture's helper in a property of its own.
    private static readonly PropertyInfo Helper =
        typeof(UmbracoIntegrationTestBase).GetProperty("TestHelper", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Umbraco's integration test base has no TestHelper property.");

    /// <summary>The providers the fixture's environment handed out and still remembers.</summary>
    public static IReadOnlyList<PhysicalFileProvider> Handed(object? fixture) =>
        Records(fixture) is { } records ? [.. records.Select(record => record.ReturnValue).OfType<PhysicalFileProvider>().Distinct()] : [];

    /// <summary>Disposes the providers the fixture's environment handed out and forgets them; returns how many there were.</summary>
    public static int Release(object? fixture)
    {
        if (Records(fixture) is not { } records)
        {
            return 0;
        }

        var handed = Handed(fixture);
        foreach (var provider in handed)
        {
            provider.Dispose();
        }

        records.Clear();
        return handed.Count;
    }

    private static IInvocationList? Records(object? fixture) =>
        fixture is UmbracoIntegrationTestBase && Helper.GetValue(fixture) is TestHelper helper && helper.GetWebHostEnvironment() is IMocked mocked
            ? mocked.Mock.Invocations
            : null;
}
