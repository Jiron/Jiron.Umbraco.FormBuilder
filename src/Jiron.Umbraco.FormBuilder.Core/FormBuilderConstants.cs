namespace Jiron.Umbraco.FormBuilder.Core;

/// <summary>
/// Jiron Form Builder's fixed identifiers (ADR-0001). Each is chosen once and never changes, because databases and
/// user groups store them.
/// </summary>
internal static class FormBuilderConstants
{
    /// <summary>The NuGet id of the package sites install, under which Umbraco's Packages section lists the migrations.</summary>
    public const string PackageId = "Jiron.Umbraco.FormBuilder";

    /// <summary>The display name (D1).</summary>
    public const string PackageName = "Jiron Form Builder";

    /// <summary>The migration plan's name, the key of its stored state (section 4.5).</summary>
    public const string MigrationPlanName = "Jiron.Umbraco.FormBuilder";

    /// <summary>The alias of the Forms section (D12), which user groups store in their allowed sections.</summary>
    public const string SectionAlias = "Jiron.FormBuilder.Section";
}
