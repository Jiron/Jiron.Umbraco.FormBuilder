using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Authorization;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;

/// <summary>
/// What an action of the management API needs (ADR-0050). Every action carries exactly one of
/// <see cref="FormBuilderPermissionAttribute"/> and <see cref="FormBuilderSectionAttribute"/>; the access filter reads it,
/// and so does the permission matrix.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
internal abstract class FormBuilderAccessAttribute : Attribute
{
    /// <summary>Creates the declaration.</summary>
    protected FormBuilderAccessAttribute(FormBuilderPermissions permissions) => Permissions = permissions;

    /// <summary>Every permission the action needs; none for any user of the section.</summary>
    public FormBuilderPermissions Permissions { get; }

    /// <summary>What the permissions are checked on: globally (the default), a folder or a form.</summary>
    public FormBuilderAccessResourceKind On { get; set; } = FormBuilderAccessResourceKind.Global;

    /// <summary>
    /// The name of the value that holds the folder's or form's key: an action parameter bound from the route or the
    /// query, or a property of the request's body. An empty or missing folder key is the root.
    /// </summary>
    public string? Key { get; set; }

    /// <summary>
    /// A section of Umbraco's own the user needs besides the permissions, such as <c>settings</c> for an action that
    /// creates Umbraco's types; none by default.
    /// </summary>
    public string? UmbracoSection { get; set; }
}

/// <summary>The action needs every permission given, globally or on the folder or form that <see cref="FormBuilderAccessAttribute.Key"/> names.</summary>
internal sealed class FormBuilderPermissionAttribute : FormBuilderAccessAttribute
{
    /// <summary>Creates the declaration.</summary>
    /// <exception cref="ArgumentOutOfRangeException">No permission, or a value that is no permission.</exception>
    public FormBuilderPermissionAttribute(FormBuilderPermissions permissions)
        : base(permissions)
    {
        if (permissions == FormBuilderPermissions.None || (permissions & ~PermissionRules.All) != FormBuilderPermissions.None)
        {
            throw new ArgumentOutOfRangeException(nameof(permissions), permissions, "A declaration names at least one permission and nothing else.");
        }
    }
}

/// <summary>Any user of the Forms section may call the action; one who sees the folder or form, when it names one.</summary>
internal sealed class FormBuilderSectionAttribute : FormBuilderAccessAttribute
{
    /// <summary>Creates the declaration.</summary>
    public FormBuilderSectionAttribute()
        : base(FormBuilderPermissions.None)
    {
    }
}

/// <summary>
/// The request's user, and the folder or form the access filter read for its check (ADR-0050), which the action uses
/// instead of reading them again.
/// </summary>
/// <param name="User">The user, as the evaluation reads them.</param>
internal sealed record FormBuilderRequestAccess(AccessUser User)
{
    /// <summary>The form of a check on a form.</summary>
    public FormInfo? Form { get; init; }

    /// <summary>The folder of a check on a folder; null for the root.</summary>
    public FolderInfo? Folder { get; init; }
}
