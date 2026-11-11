using System.Text.Json;
using System.Text.RegularExpressions;
using Jiron.Umbraco.FormBuilder.Samples;
using Jiron.Umbraco.FormBuilder.Testing;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Infrastructure.Manifest;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Samples;

/// <summary>
/// How the samples are turned on (ADR-0234): by their names only, each once, and with their backoffice manifests, which
/// Umbraco reads beside every package's: none while no sample is on, else the samples' extensions with their words once;
/// and every term a sample names has its words in English and German.
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

    [Test]
    public void EveryTermTheSamplesUse_IsInTheirWords_InEnglishAndGerman()
    {
        var samples = RepositoryRoot.Path(typeof(SampleManifestsTests).Assembly, "samples", "Jiron.Umbraco.FormBuilder.Samples");
        var plugin = Path.Combine(samples, "wwwroot", "App_Plugins", "JironFormBuilderSamples");
        static HashSet<string> Keys(string file) => Regex.Matches(File.ReadAllText(file), @"^\s{4}([A-Za-z]+):", RegexOptions.Multiline)
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);
        var english = Keys(Path.Combine(plugin, "lang", "en.js"));
        var german = Keys(Path.Combine(plugin, "lang", "de.js"));

        // The terms the C# names as #jfbSamples_<term>, and the elements as localize.term('jfbSamples_<term>').
        var sources = Directory.EnumerateFiles(samples, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Concat(Directory.EnumerateFiles(plugin, "*.js"));
        var used = sources
            .SelectMany(file => Regex.Matches(File.ReadAllText(file), @"jfbSamples_([A-Za-z]+)").Select(match => match.Groups[1].Value))
            .ToHashSet(StringComparer.Ordinal);

        Assert.Multiple(() =>
        {
            Assert.That(used, Has.Count.GreaterThan(20), "the terms were found");
            Assert.That(used.Where(term => !english.Contains(term)).Order(StringComparer.Ordinal), Is.Empty, "terms without English words");
            Assert.That(used.Where(term => !german.Contains(term)).Order(StringComparer.Ordinal), Is.Empty, "terms without German words");
            Assert.That(german, Is.EquivalentTo(english), "the two languages' terms");
            Assert.That(english.Where(term => !used.Contains(term)).Order(StringComparer.Ordinal), Is.Empty, "words no sample uses");
        });
    }
}
