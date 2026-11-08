namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;

/// <summary>
/// Settings, Access (ADR-0013, ADR-0218): every Umbraco user group with its global grant, the permissions in their three
/// parts, what each needs, the host's rule that keeps the grants from being changed here, and the folders and forms with
/// access of their own.
/// </summary>
internal sealed record AccessResponseModel
{
    /// <summary>The user groups, the administrators first, then by name.</summary>
    public required IReadOnlyList<AccessGroupResponseModel> Groups { get; init; }

    /// <summary>The permissions in their parts, <c>forms</c>, <c>entries</c> and <c>library</c>, each in the order of ADR-0013's table.</summary>
    public required IReadOnlyList<AccessPartResponseModel> Parts { get; init; }

    /// <summary>The permissions that need another, such as <c>editEntries</c> needing <c>viewEntries</c>.</summary>
    public required IReadOnlyList<AccessNeedResponseModel> Needs { get; init; }

    /// <summary>The host's rule that keeps the global grants from being changed here; null when none does.</summary>
    public AccessLockResponseModel? Lock { get; init; }

    /// <summary>The folders and forms outside the trash that some group has a grant of its own on, or that are restricted, by name.</summary>
    public required IReadOnlyList<AccessItemResponseModel> Items { get; init; }
}

/// <summary>An Umbraco user group as Access shows it.</summary>
/// <param name="Key">The group's key.</param>
/// <param name="Name">Its name.</param>
/// <param name="Icon">Its icon, as Umbraco names it.</param>
/// <param name="IsAdministrators">Whether it is the administrators' group, which holds every permission everywhere.</param>
/// <param name="HasSection">Whether its users can open the Form Builder section.</param>
/// <param name="Permissions">What it holds here: its global grant, or its own grant on a folder or form.</param>
internal sealed record AccessGroupResponseModel(Guid Key, string Name, string? Icon, bool IsAdministrators, bool HasSection, IReadOnlyList<string> Permissions);

/// <summary>A part of the permissions.</summary>
/// <param name="Alias">The part: <c>forms</c>, <c>entries</c> or <c>library</c>.</param>
/// <param name="Permissions">Its permissions, by name.</param>
internal sealed record AccessPartResponseModel(string Alias, IReadOnlyList<string> Permissions);

/// <summary>A permission that needs another.</summary>
/// <param name="Permission">The permission.</param>
/// <param name="Needs">The one it needs.</param>
internal sealed record AccessNeedResponseModel(string Permission, string Needs);

/// <summary>The host's rule that keeps access from being changed here.</summary>
/// <param name="Field"><c>*</c> for the whole access section, <c>grants</c> for the global grants.</param>
/// <param name="ConfigurationKey">The configuration key behind it.</param>
internal sealed record AccessLockResponseModel(string Field, string ConfigurationKey);

/// <summary>A folder or form with access of its own.</summary>
/// <param name="Type"><c>folder</c> or <c>form</c>.</param>
/// <param name="Key">Its key.</param>
/// <param name="Name">Its name.</param>
/// <param name="IsRestricted">Whether only the grants on it and below count for it.</param>
/// <param name="Grants">The groups' own grants on it.</param>
internal sealed record AccessItemResponseModel(string Type, Guid Key, string Name, bool IsRestricted, IReadOnlyList<AccessGrantResponseModel> Grants);

/// <summary>A group's own grant.</summary>
/// <param name="Group">The group's key.</param>
/// <param name="Permissions">What the grant holds, by name.</param>
internal sealed record AccessGrantResponseModel(Guid Group, IReadOnlyList<string> Permissions);

/// <summary>A save of the global grants, each group's grant from what the editor saw.</summary>
internal sealed record AccessRequestModel
{
    /// <summary>The groups' grants; a group whose permissions stay as they were is left as it is.</summary>
    public required IReadOnlyList<AccessGrantRequestModel> Grants { get; init; }
}

/// <summary>A group's grant in a save.</summary>
/// <param name="Group">The group's key.</param>
/// <param name="Expected">What the grant held as the editor saw it, by name.</param>
/// <param name="Permissions">What it is to hold, by name; none to remove it.</param>
internal sealed record AccessGrantRequestModel(Guid Group, IReadOnlyList<string> Expected, IReadOnlyList<string> Permissions);

/// <summary>A folder's or form's access, as its Permissions sidebar shows it (ADR-0218).</summary>
internal sealed record AccessTargetResponseModel
{
    /// <summary><c>folder</c> or <c>form</c>.</summary>
    public required string Type { get; init; }

    /// <summary>Its key.</summary>
    public required Guid Key { get; init; }

    /// <summary>Its name.</summary>
    public required string Name { get; init; }

    /// <summary>Whether only the grants on it and below count for it.</summary>
    public required bool IsRestricted { get; init; }

    /// <summary>A form's concurrency version, which a change of its restriction sends back; null for a folder.</summary>
    public int? Version { get; init; }

    /// <summary>The user groups other than the administrators, each with its own grant on it.</summary>
    public required IReadOnlyList<AccessGroupResponseModel> Groups { get; init; }

    /// <summary>The permissions a folder or form can be granted, by name, in the order of ADR-0013's table.</summary>
    public required IReadOnlyList<string> Permissions { get; init; }

    /// <summary>The permissions that need another.</summary>
    public required IReadOnlyList<AccessNeedResponseModel> Needs { get; init; }

    /// <summary>The host's rule that keeps access from being changed here; null when none does.</summary>
    public AccessLockResponseModel? Lock { get; init; }
}

/// <summary>A save of a folder's or form's grants and restriction, in one unit of work.</summary>
internal sealed record AccessTargetRequestModel
{
    /// <summary>The groups' grants on it.</summary>
    public required IReadOnlyList<AccessGrantRequestModel> Grants { get; init; }

    /// <summary>Whether only the grants on it and below are to count for it.</summary>
    public required bool IsRestricted { get; init; }

    /// <summary>A form's concurrency version as the editor read it, which a change of its restriction needs.</summary>
    public int? Version { get; init; }
}
