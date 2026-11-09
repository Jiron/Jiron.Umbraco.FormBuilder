using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Authorization;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;

/// <summary>
/// What an action of the management API needs (ADR-0050). Every action carries exactly one of
/// <see cref="FormBuilderPermissionAttribute"/>, <see cref="FormBuilderSectionAttribute"/>, for the form picker only
/// <see cref="FormBuilderContentAccessAttribute"/> and, for the download of an export's file only,
/// <see cref="FormBuilderTicketAccessAttribute"/>; the access filter reads it, and so does the permission matrix.
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

/// <summary>
/// The action needs every permission given, or with <see cref="AnyOf"/> one of them, globally or on the folder or form that
/// <see cref="FormBuilderAccessAttribute.Key"/> names.
/// </summary>
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

    /// <summary>
    /// Whether one of the permissions is enough, as for Privacy > Retention, which <c>privacyTools</c> and
    /// <c>manageSettings</c> both read (IA 9, ADR-0222); a user who holds none is refused with the first, in the table's order.
    /// </summary>
    public bool AnyOf { get; init; }
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
/// Content access instead of Jiron Form Builder's permissions (ADR-0013, ADR-0133): any backoffice user with one of
/// Umbraco's <see cref="Sections"/> may call the action, the Forms section not needed. Only the form picker's endpoints
/// carry it, and the permission matrix names them as its reviewed exception.
/// </summary>
internal sealed class FormBuilderContentAccessAttribute : FormBuilderAccessAttribute
{
    /// <summary>The sections where a user edits properties that may hold a form picker: Content, Media, Members, and Settings for the data type's own settings.</summary>
    public static readonly IReadOnlyList<string> Sections =
    [
        global::Umbraco.Cms.Core.Constants.Applications.Content,
        global::Umbraco.Cms.Core.Constants.Applications.Media,
        global::Umbraco.Cms.Core.Constants.Applications.Members,
        global::Umbraco.Cms.Core.Constants.Applications.Settings,
    ];

    /// <summary>Creates the declaration.</summary>
    public FormBuilderContentAccessAttribute()
        : base(FormBuilderPermissions.None)
    {
    }
}

/// <summary>
/// A ticket of the action's own instead of a signed-in user (ADR-0210): the action reads a ticket that the management API
/// issued after its own checks, and refuses a request without one that holds. Only the download of a background export's
/// file carries it, with <c>[AllowAnonymous]</c>, as a plain link's request carries no backoffice token; the permission
/// matrix names it as its reviewed exception.
/// </summary>
internal sealed class FormBuilderTicketAccessAttribute : FormBuilderAccessAttribute
{
    /// <summary>Creates the declaration.</summary>
    public FormBuilderTicketAccessAttribute()
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
