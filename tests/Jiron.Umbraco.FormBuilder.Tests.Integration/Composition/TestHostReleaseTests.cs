using System.Reflection;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.FileProviders.Physical;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Composition;

/// <summary>
/// The test run's own health: once a test ends, nothing keeps its host in memory (<see cref="ReleaseUmbracoRuntimesAttribute"/>).
/// Its runtime stops listening to the process's unhandled exceptions, and the file providers the fixture's environment
/// handed it are disposed and forgotten. Each test sees only its own runtime listening and its own providers; whichever
/// runs second would see the first's too if they had stayed (NUnit keeps one instance of a fixture for all its tests).
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class TestHostReleaseTests : FormBuilderTestServerTestBase
{
    private readonly List<PhysicalFileProvider> _earlier = [];

    [Test]
    public void AFirstTestsHost_ListensAlone() =>
        Assert.That(UmbracoRuntimes.Listening, Is.EqualTo(1), "only this test's runtime, none of an earlier test's");

    [Test]
    public void ASecondTestsHost_ListensAlone() =>
        Assert.That(UmbracoRuntimes.Listening, Is.EqualTo(1), "only this test's runtime, none of an earlier test's");

    [Test]
    public void AFirstTestsHost_HoldsItsOwnFileProvidersAlone() => HoldsItsOwnFileProvidersAlone();

    [Test]
    public void ASecondTestsHost_HoldsItsOwnFileProvidersAlone() => HoldsItsOwnFileProvidersAlone();

    [Test]
    public void ATestsFileProviders_AreDisposedAndForgotten_WhenReleased()
    {
        var handed = TestHostEnvironments.Handed(this);
        Assert.That(handed, Is.Not.Empty, "the host read its content root");
        // As a host that watches its content root does (on Umbraco 18 every host does; on 17 none). With a file system
        // watcher, whatever the environment says: the .NET SDK's container images set DOTNET_USE_POLLING_FILE_WATCHER,
        // under which a provider polls instead and runs no watcher this test could see (ROADMAP 26.1).
        handed[^1].UsePollingFileWatcher = false;
        _ = handed[^1].Watch("*.jfb");
        Assert.That(handed.Where(Watches), Is.Not.Empty, "a provider watches");

        var released = TestHostEnvironments.Release(this);

        Assert.Multiple(() =>
        {
            Assert.That(released, Is.EqualTo(handed.Count));
            Assert.That(TestHostEnvironments.Handed(this), Is.Empty, "forgotten");
            Assert.That(handed.Where(Watches), Is.Empty, "disposed, so none watches any more");
        });
    }

    private void HoldsItsOwnFileProvidersAlone()
    {
        var handed = TestHostEnvironments.Handed(this);
        Assert.Multiple(() =>
        {
            Assert.That(handed, Is.Not.Empty, "the host read its content root");
            Assert.That(handed.Intersect(_earlier), Is.Empty, "none an earlier test's host was handed");
        });
        _earlier.AddRange(handed);
    }

    // A provider that was asked to watch keeps a PhysicalFilesWatcher, which runs a FileSystemWatcher until the provider is
    // disposed. Neither is public on the provider, so each is found by its type.
    private static bool Watches(PhysicalFileProvider provider) =>
        FieldOf<PhysicalFilesWatcher>(provider) is { } watcher && FieldOf<FileSystemWatcher>(watcher) is { EnableRaisingEvents: true };

    private static T? FieldOf<T>(object owner)
        where T : class =>
        owner.GetType().GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Where(field => field.FieldType == typeof(T)).Select(field => field.GetValue(owner)).OfType<T>().SingleOrDefault();
}
