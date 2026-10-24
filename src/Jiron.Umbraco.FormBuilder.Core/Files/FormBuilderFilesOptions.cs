namespace Jiron.Umbraco.FormBuilder.Core.Files;

/// <summary>
/// The host configuration of uploaded files (section 4.8, <c>JironFormBuilder:Files</c>), part of
/// <see cref="Configuration.FormBuilderOptions"/> (ADR-0042). The upload steps add the limits; 4.3f added where the
/// default store keeps the files (ADR-0006, ADR-0041).
/// </summary>
public sealed class FormBuilderFilesOptions
{
    /// <summary>The configuration key of these options.</summary>
    public const string SectionName = "JironFormBuilder:Files";

    /// <summary>The folder of the default store unless the host sets another.</summary>
    public const string DefaultRoot = "~/umbraco/Data/JironFormBuilder/Uploads";

    /// <summary>
    /// The folder the default store keeps the files in: <c>~/</c> followed by a path relative to the site's content root, or an
    /// absolute path, for example a folder that the servers of a load-balanced site share. It must not lie inside the web
    /// root or contain it, as written or with its links followed: a site configured so does not start. Name the folder by
    /// its own path, because the check cannot see through an administrative share, a drive letter made with <c>subst</c>, a
    /// short 8.3 name or a link inside the web root that points to the folder.
    /// </summary>
    public string Root { get; set; } = DefaultRoot;
}
