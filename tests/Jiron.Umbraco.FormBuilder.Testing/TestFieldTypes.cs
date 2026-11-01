using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Outbound;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Microsoft.Extensions.DependencyInjection;

namespace Jiron.Umbraco.FormBuilder.Testing;

/// <summary>
/// Field type registries for tests that run without a site (ADR-0055), made the way a site makes its own: the collection
/// builder with the built-in types, changed as a composer would change it, registered with a container that creates
/// the types.
/// </summary>
internal static class TestFieldTypes
{
    /// <summary>The registry of a site that leaves the built-in types as they are.</summary>
    public static FieldTypeRegistry Registry { get; } = Build();

    /// <summary>What the built-in captcha type needs: the built-in providers of a site without keys, which call nobody.</summary>
    /// <param name="services">The services of a registry being built.</param>
    public static void AddCaptchaProviders(IServiceCollection services)
    {
        new FormBuilderCaptchaProviderCollectionBuilder().RegisterWith(services);
        services.AddOptions<FormBuilderOptions>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IFormBuilderHttpTransport>(new NoTransport());
    }

    /// <summary>A registry of the built-in types, changed as a composer would change them.</summary>
    /// <exception cref="InvalidOperationException">The changed types have problems, as start-up would refuse them.</exception>
    public static FieldTypeRegistry Build(Action<FormBuilderFieldTypeCollectionBuilder>? change = null)
    {
        var builder = new FormBuilderFieldTypeCollectionBuilder();
        change?.Invoke(builder);
        var services = new ServiceCollection();
        builder.RegisterWith(services);
        AddCaptchaProviders(services);
        using var provider = services.BuildServiceProvider();

        // The built-in captcha type asks them for its availability, after this provider is gone.
        _ = provider.GetRequiredService<FormBuilderCaptchaProviderCollection>().Count;
        return new FieldTypeRegistry(provider.GetRequiredService<FormBuilderFieldTypeCollection>());
    }

    private sealed class NoTransport : IFormBuilderHttpTransport
    {
        public Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("A registry without keys calls nobody.");
    }
}
