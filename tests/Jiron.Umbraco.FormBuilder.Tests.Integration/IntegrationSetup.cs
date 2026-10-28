namespace Jiron.Umbraco.FormBuilder.Tests.Integration;

/// <summary>
/// Umbraco's global set-up for integration tests (the test database pool, the working directory), and a folder of this
/// run's own for the bodies ASP.NET Core buffers to disk (<c>ASPNETCORE_TEMP</c>), so a test can watch that an upload is
/// never buffered (ADR-0087). ASP.NET Core reads the variable once per process, so it is set before any test runs.
/// NUnit runs set-up fixtures only from the test assembly, so this wrapper calls the one in
/// Umbraco.Cms.Tests.Integration.
/// </summary>
[SetUpFixture]
public sealed class IntegrationSetup
{
    private readonly GlobalSetupTeardown _umbraco = new();

    /// <summary>The folder ASP.NET Core buffers bodies in during this run.</summary>
    public static string BufferFolder { get; } = Path.Combine(Path.GetTempPath(), $"jfb-aspnet-temp-{Environment.ProcessId}");

    [OneTimeSetUp]
    public void SetUp()
    {
        Directory.CreateDirectory(BufferFolder);
        Environment.SetEnvironmentVariable("ASPNETCORE_TEMP", BufferFolder);
        _umbraco.SetUp();
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        _umbraco.TearDown();
        try
        {
            Directory.Delete(BufferFolder, recursive: true);
        }
        catch (IOException exception)
        {
            TestContext.Progress.WriteLine($"The buffer folder stays: {exception.Message}");
        }
    }
}
