namespace Jiron.Umbraco.FormBuilder.Core;

/// <summary>
/// Jiron Form Builder's fixed identifiers (ADR-0001). Each is chosen once and never changes, because databases and
/// user groups store them. The package's name and its migration plan's name are public, for a site that runs Umbraco's
/// package migrations itself, such as from an install script of its own; the others are the package's own.
/// </summary>
public static class FormBuilderConstants
{
    /// <summary>The NuGet id of the package sites install, under which Umbraco's Packages section lists the migrations.</summary>
    internal const string PackageId = "Jiron.Umbraco.FormBuilder";

    /// <summary>
    /// The package's name, <c>Jiron Form Builder</c>, under which Umbraco knows its migrations: what
    /// <c>PackageMigrationRunner.RunPackageMigrationsIfPendingAsync</c> takes to run them when they are pending.
    /// </summary>
    public const string PackageName = "Jiron Form Builder";

    /// <summary>
    /// The migration plan's name, <c>Jiron.Umbraco.FormBuilder</c>, which is also the key of its state in Umbraco's
    /// key-value table (section 4.5): what <c>PackageMigrationRunner.RunPackagePlansAsync</c> takes to run the plan.
    /// </summary>
    public const string MigrationPlanName = "Jiron.Umbraco.FormBuilder";

    /// <summary>The alias of the Forms section (D12), which user groups store in their allowed sections.</summary>
    internal const string SectionAlias = "Jiron.FormBuilder.Section";
}
