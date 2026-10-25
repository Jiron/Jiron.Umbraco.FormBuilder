using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
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

    /// <summary>A registry of the built-in types, changed as a composer would change them.</summary>
    /// <exception cref="InvalidOperationException">The changed types have problems, as start-up would refuse them.</exception>
    public static FieldTypeRegistry Build(Action<FormBuilderFieldTypeCollectionBuilder>? change = null)
    {
        var builder = new FormBuilderFieldTypeCollectionBuilder();
        change?.Invoke(builder);
        var services = new ServiceCollection();
        builder.RegisterWith(services);
        using var provider = services.BuildServiceProvider();
        return new FieldTypeRegistry(provider.GetRequiredService<FormBuilderFieldTypeCollection>());
    }
}
