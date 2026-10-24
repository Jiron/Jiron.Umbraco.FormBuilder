using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Configuration;

/// <summary>Host configurations for the tests of section 4.8's options: bound, validated and started as a site does.</summary>
internal static class HostOptions
{
    /// <summary>A site's folders; only names, nothing is created.</summary>
    public static readonly string Site = Path.Combine(Path.GetTempPath(), "jfb-site");

    /// <summary>The site's web root.</summary>
    public static readonly string WebRoot = Path.Combine(Site, "wwwroot");

    /// <summary>The configuration of <paramref name="settings"/>, keyed as appsettings.json flattens them.</summary>
    public static IConfiguration Configuration(IReadOnlyDictionary<string, string?>? settings = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings ?? new Dictionary<string, string?>()).Build();

    /// <summary>The site's folders, as Web supplies them.</summary>
    public static IHostFolders Folders()
    {
        var folders = Substitute.For<IHostFolders>();
        folders.ContentRoot.Returns(Site);
        folders.WebRoot.Returns(WebRoot);
        return folders;
    }

    /// <summary>The options bound from <paramref name="settings"/>, as the site binds them.</summary>
    public static FormBuilderOptions Bind(IReadOnlyDictionary<string, string?> settings)
    {
        var options = new FormBuilderOptions();
        Configuration(settings).GetSection(FormBuilderOptions.SectionName).Bind(options);
        return options;
    }

    /// <summary>The validator's failures for <paramref name="settings"/>, bound as the site binds them; empty when they pass.</summary>
    public static IReadOnlyList<string> Failures(IReadOnlyDictionary<string, string?> settings)
    {
        var configuration = Configuration(settings);
        var options = new FormBuilderOptions();
        configuration.GetSection(FormBuilderOptions.SectionName).Bind(options);
        return Failures(options, configuration);
    }

    /// <summary>The validator's failures for options set in code, with no configuration keys.</summary>
    public static IReadOnlyList<string> Failures(FormBuilderOptions options, IConfiguration? configuration = null)
    {
        var validator = new FormBuilderOptionsValidator(Folders(), (configuration ?? Configuration()).GetSection(FormBuilderOptions.SectionName));
        var result = validator.Validate(Options.DefaultName, options);
        Assert.That(result.Skipped, Is.False);
        return result.Failed ? result.Failures!.ToList() : [];
    }

    /// <summary>
    /// The services of a site with <paramref name="settings"/>, as the core composer registers the host configuration,
    /// validated as the host validates them before it serves anything.
    /// </summary>
    public static ServiceProvider Services(IReadOnlyDictionary<string, string?>? settings = null, GlobalizationFacts? facts = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(Folders());
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        services.AddFormBuilderOptions(Configuration(settings));
        if (facts is not null)
        {
            services.AddSingleton(facts);
        }

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    /// <summary>What the host runs before the site serves anything (<c>ValidateOnStart</c>).</summary>
    public static void Start(IServiceProvider services) => services.GetRequiredService<IStartupValidator>().Validate();
}
