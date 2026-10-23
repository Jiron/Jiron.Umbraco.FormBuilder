using System.Reflection;

namespace Jiron.Umbraco.FormBuilder.Testing;

/// <summary>
/// The repository folder a test assembly was built from, so tests read the repository's files in place (ADR-0005):
/// tests/Directory.Build.props writes it into every test assembly as the <c>JfbRepositoryRoot</c> metadata.
/// </summary>
public static class RepositoryRoot
{
    /// <summary>The metadata key.</summary>
    public const string MetadataKey = "JfbRepositoryRoot";

    /// <summary>The repository folder of a test assembly.</summary>
    /// <exception cref="InvalidOperationException">The assembly carries no root, or the folder is gone.</exception>
    public static string Of(Assembly testAssembly)
    {
        ArgumentNullException.ThrowIfNull(testAssembly);
        var root = testAssembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == MetadataKey)?.Value;
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
        {
            throw new InvalidOperationException($"{testAssembly.GetName().Name} has no repository root; tests/Directory.Build.props sets it.");
        }

        return root;
    }

    /// <summary>A path inside the repository folder of a test assembly, from parts relative to its root.</summary>
    public static string Path(Assembly testAssembly, params string[] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        return System.IO.Path.Combine([Of(testAssembly), .. parts]);
    }
}
