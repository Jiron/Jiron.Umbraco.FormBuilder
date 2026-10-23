namespace Jiron.Umbraco.FormBuilder.Tests.Integration;

/// <summary>
/// Umbraco's global set-up for integration tests (the test database pool, the working directory). NUnit runs set-up
/// fixtures only from the test assembly, so this wrapper calls the one in Umbraco.Cms.Tests.Integration.
/// </summary>
[SetUpFixture]
public sealed class IntegrationSetup
{
    private readonly GlobalSetupTeardown _umbraco = new();

    [OneTimeSetUp]
    public void SetUp() => _umbraco.SetUp();

    [OneTimeTearDown]
    public void TearDown() => _umbraco.TearDown();
}
