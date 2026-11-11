using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Samples;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Infrastructure.Manifest;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Samples;

/// <summary>
/// How the samples are turned on (ADR-0234): by their names only, each once, and with their backoffice manifests, which
/// Umbraco reads beside every package's: none while no sample is on, else the samples' extensions with their words once.
/// </summary>
public sealed class SampleManifestsTests
{
    [Test]
    public void ASampleOfNoName_IsRefused_NamingTheSamples()
    {
        var builder = Substitute.For<IUmbracoBuilder>();

        Assert.That(
            () => builder.AddFormBuilderSample("colours"),
            Throws.ArgumentException.With.Message.StartsWith("There is no sample 'colours'; the samples are colour, embedView, crm, products, iCalendar, "));
    }

    [Test]
    public async Task NoSampleOn_AddsNoManifest()
    {
        var manifests = await new SampleManifests([]).ReadPackageManifestsAsync();

        Assert.That(manifests, Is.Empty);
    }

    [Test]
    public async Task TheSamplesOn_GiveOneManifest_OfTheirExtensions_AndTheirWordsOnce()
    {
        var services = new ServiceCollection();
        var first = SampleManifests.TurnOn(services, "one", new { type = "jfbSettingEditor", alias = "A" });
        var again = SampleManifests.TurnOn(services, "one", new { type = "jfbSettingEditor", alias = "A" });
        SampleManifests.TurnOn(services, "two", new { type = "workspaceView", alias = "B" });
        using var provider = services.BuildServiceProvider();

        var readers = provider.GetServices<IPackageManifestReader>().ToList();
        var manifest = (await readers.Single().ReadPackageManifestsAsync()).Single();
        var extensions = JsonSerializer.SerializeToElement(manifest.Extensions).EnumerateArray()
            .Select(extension => $"{extension.GetProperty("type").GetString()} {extension.GetProperty("alias").GetString()}")
            .ToList();

        Assert.Multiple(() =>
        {
            Assert.That((first, again), Is.EqualTo((true, false)), "a sample turned on twice is on once");
            Assert.That((manifest.Id, manifest.Name, manifest.AllowTelemetry), Is.EqualTo(("Jiron.Umbraco.FormBuilder.Samples", "Jiron Form Builder samples", false)));
            Assert.That(extensions, Is.EqualTo(new[]
            {
                "jfbSettingEditor A",
                "workspaceView B",
                "localization Jiron.FormBuilder.Samples.Localization.en",
                "localization Jiron.FormBuilder.Samples.Localization.de",
            }));
        });
    }
}
