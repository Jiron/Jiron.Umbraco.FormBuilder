using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Umbraco.Cms.Core.Manifest;
using Umbraco.Cms.Infrastructure.Manifest;

namespace Jiron.Umbraco.FormBuilder.Samples;

/// <summary>One sample turned on, with its extensions of the backoffice as an <c>umbraco-package.json</c> lists them.</summary>
/// <param name="Sample">The sample's name.</param>
/// <param name="Extensions">Its manifests: objects that serialise as the manifests' JSON does.</param>
internal sealed record SampleOn(string Sample, IReadOnlyList<object> Extensions);

/// <summary>
/// The backoffice's package manifest of the samples turned on (ADR-0234). A site's own extension usually lists its
/// manifests in <c>App_Plugins/&lt;name&gt;/umbraco-package.json</c>; the samples give theirs from code instead, which
/// Umbraco reads beside every such file, so a sample's editor, preview or view comes with its server part and never
/// without it. Their scripts are static files under <see cref="Folder"/> either way.
/// </summary>
/// <param name="samples">The samples turned on.</param>
internal sealed class SampleManifests(IEnumerable<SampleOn> samples) : IPackageManifestReader
{
    /// <summary>Where the samples' backoffice files are served.</summary>
    public const string Folder = "/App_Plugins/JironFormBuilderSamples/";

    /// <inheritdoc />
    public Task<IEnumerable<PackageManifest>> ReadPackageManifestsAsync()
    {
        // The samples' words come once, whichever samples are on, also for those whose names alone need them.
        var on = samples.ToList();
        var extensions = on.SelectMany(sample => sample.Extensions).Concat([Words("en"), Words("de")]).ToArray();
        IEnumerable<PackageManifest> manifests = on.Count == 0
            ? []
            :
            [
                new PackageManifest
                {
                    Id = "Jiron.Umbraco.FormBuilder.Samples",
                    Name = "Jiron Form Builder samples",
                    AllowTelemetry = false,
                    Extensions = extensions,
                },
            ];
        return Task.FromResult(manifests);
    }

    /// <summary>Turns a sample on, once, with its manifests; false when it was on already.</summary>
    /// <param name="services">The site's services.</param>
    /// <param name="sample">The sample's name.</param>
    /// <param name="extensions">Its manifests.</param>
    /// <returns>Whether it was turned on now.</returns>
    public static bool TurnOn(IServiceCollection services, string sample, params object[] extensions)
    {
        if (services.Any(service => service.ImplementationInstance is SampleOn on && on.Sample == sample))
        {
            return false;
        }

        services.AddSingleton(new SampleOn(sample, extensions));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IPackageManifestReader, SampleManifests>());
        return true;
    }

    /// <summary>A manifest of the samples' words in English and German, for the backoffice's language.</summary>
    /// <param name="culture">The language, <c>en</c> or <c>de</c>.</param>
    /// <returns>The manifest.</returns>
    private static object Words(string culture) => new
    {
        type = "localization",
        alias = $"Jiron.FormBuilder.Samples.Localization.{culture}",
        name = $"Jiron Form Builder samples ({culture})",
        js = $"{Folder}lang/{culture}.js",
        meta = new { culture },
    };
}
