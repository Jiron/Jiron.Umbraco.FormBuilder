namespace Jiron.Umbraco.FormBuilder.Core.Access;

/// <summary>Who holds Jiron Form Builder's permissions, from the host configuration (section 4.8, ADR-0013, ADR-0048).</summary>
public sealed class FormBuilderAccessOptions
{
    /// <summary>
    /// The global grants, each naming a user group by its alias and the permissions it holds everywhere, for example
    /// <c>{ "group": "editor", "permissions": [ "viewEntries" ] }</c>. With at least one, they replace the backoffice's
    /// global grants, which it then shows read-only; grants on folders and forms stay the backoffice's. Each group is
    /// listed once, and a permission comes with the one it needs (<c>export</c> with <c>viewEntries</c>).
    /// </summary>
    public IList<HostGrant> Grants { get; } = [];
}

/// <summary>A global grant from the host configuration (ADR-0048).</summary>
public sealed class HostGrant
{
    /// <summary>The user group's alias, for example <c>editor</c>. A group the site does not have is granted nothing.</summary>
    public string? Group { get; set; }

    /// <summary>
    /// The permissions, in any case: <c>design</c>, <c>manageActions</c>, <c>viewEntries</c>, <c>viewSensitive</c>,
    /// <c>editEntries</c>, <c>deleteEntries</c>, <c>export</c>, <c>manageLibrary</c>, <c>manageSettings</c>,
    /// <c>managePermissions</c> or <c>privacyTools</c>.
    /// </summary>
    public IList<string> Permissions { get; } = [];
}
