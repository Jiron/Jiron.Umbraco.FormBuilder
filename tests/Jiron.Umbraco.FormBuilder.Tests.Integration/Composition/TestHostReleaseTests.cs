using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Composition;

/// <summary>
/// The test run's own health: once a test ends, its host's runtime stops listening to the process's unhandled exceptions,
/// so the host can go (<see cref="ReleaseUmbracoRuntimesAttribute"/>). Each test sees only its own runtime listening;
/// whichever runs second would see the first's too if it had stayed.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class TestHostReleaseTests : FormBuilderTestServerTestBase
{
    [Test]
    public void AFirstTestsHost_ListensAlone() =>
        Assert.That(UmbracoRuntimes.Listening, Is.EqualTo(1), "only this test's runtime, none of an earlier test's");

    [Test]
    public void ASecondTestsHost_ListensAlone() =>
        Assert.That(UmbracoRuntimes.Listening, Is.EqualTo(1), "only this test's runtime, none of an earlier test's");
}
