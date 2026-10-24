using System.Reflection;

namespace Jiron.Umbraco.FormBuilder.Core;

/// <summary>The version of Jiron Form Builder that runs, as its packages carry it (D16).</summary>
internal static class FormBuilderVersion
{
    /// <summary>The package version without build metadata, for example <c>17.0.0-alpha.0</c>.</summary>
    public static string Package { get; } = Read(typeof(FormBuilderVersion).Assembly);

    /// <summary>The informational version of an assembly without its build metadata (the part after a <c>+</c>).</summary>
    public static string Read(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString(3)
            ?? "0.0.0";
        return WithoutMetadata(version);
    }

    /// <summary>A version without its build metadata: the part after the first <c>+</c>, and the <c>+</c> itself.</summary>
    public static string WithoutMetadata(string version)
    {
        ArgumentNullException.ThrowIfNull(version);
        var metadata = version.IndexOf('+', StringComparison.Ordinal);
        return metadata < 0 ? version : version[..metadata];
    }
}
