using Jiron.Umbraco.FormBuilder.Core.Access;
using Microsoft.AspNetCore.Authorization;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Authorization;

/// <summary>What a permission is checked against (ADR-0049).</summary>
internal enum FormBuilderAccessResourceKind
{
    /// <summary>What belongs to no folder or form, such as the library or the settings.</summary>
    Global,

    /// <summary>A folder, or the root.</summary>
    Folder,

    /// <summary>A form.</summary>
    Form,
}

/// <summary>
/// The thing an endpoint checks a permission against: globally, a folder by key (null: the root), or a form by the row
/// the endpoint read. The permission policies take it from <see cref="IAuthorizationService"/>; without it they check
/// globally.
/// </summary>
internal sealed record FormBuilderAccessResource
{
    private FormBuilderAccessResource(FormBuilderAccessResourceKind kind, Guid? folderKey, FormAccessTarget? form)
    {
        Kind = kind;
        FolderKey = folderKey;
        Form = form;
    }

    /// <summary>Globally.</summary>
    public static FormBuilderAccessResource Global { get; } = new(FormBuilderAccessResourceKind.Global, null, null);

    /// <summary>Which kind of thing it is.</summary>
    public FormBuilderAccessResourceKind Kind { get; }

    /// <summary>The folder, or the form's folder; null for the root.</summary>
    public Guid? FolderKey { get; }

    /// <summary>The form, for <see cref="FormBuilderAccessResourceKind.Form"/>.</summary>
    public FormAccessTarget? Form { get; }

    /// <summary>A folder by key, or the root for null.</summary>
    public static FormBuilderAccessResource ForFolder(Guid? folderKey) => new(FormBuilderAccessResourceKind.Folder, folderKey, null);

    /// <summary>A form, as the endpoint read it.</summary>
    public static FormBuilderAccessResource ForForm(FormAccessTarget form)
    {
        ArgumentNullException.ThrowIfNull(form);
        return new(FormBuilderAccessResourceKind.Form, form.FolderKey, form);
    }
}

/// <summary>Why a check of Jiron Form Builder's policies failed (ADR-0049), which decides the answer.</summary>
internal enum FormBuilderDenialKind
{
    /// <summary>The user lacks the Forms section: <c>403</c>.</summary>
    NoSection,

    /// <summary>The user cannot see the folder or form: <c>404</c>, as for a key that does not exist.</summary>
    NotFound,

    /// <summary>The user sees it but lacks the permission: <c>403</c> naming it.</summary>
    MissingPermission,
}

/// <summary>
/// The reason a Jiron Form Builder handler gives when it refuses (ADR-0049). An unknown or anonymous user gets no
/// denial: the check fails as for anyone the backoffice does not know.
/// </summary>
/// <param name="handler">The handler that refused.</param>
/// <param name="kind">Why.</param>
/// <param name="permission">The permission checked; none for the section.</param>
internal sealed class FormBuilderAccessDenial(IAuthorizationHandler handler, FormBuilderDenialKind kind, FormBuilderPermissions permission)
    : AuthorizationFailureReason(handler, MessageOf(kind, permission))
{
    /// <summary>Why the check failed.</summary>
    public FormBuilderDenialKind Kind { get; } = kind;

    /// <summary>The permission checked; none for the section.</summary>
    public FormBuilderPermissions Permission { get; } = permission;

    /// <summary>The denial of a failed check, or null when it has none (an unknown or anonymous user).</summary>
    public static FormBuilderAccessDenial? Of(AuthorizationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Failure?.FailureReasons.OfType<FormBuilderAccessDenial>().FirstOrDefault();
    }

    private static string MessageOf(FormBuilderDenialKind kind, FormBuilderPermissions permission) => kind switch
    {
        FormBuilderDenialKind.NoSection => "The user has no access to the Forms section.",
        FormBuilderDenialKind.NotFound => "The user cannot see the folder or form.",
        _ => $"The user lacks the permission '{PermissionRules.NameOf(permission)}'.",
    };
}

/// <summary>Any user of the Forms section.</summary>
internal sealed class FormBuilderSectionRequirement : IAuthorizationRequirement
{
    private FormBuilderSectionRequirement()
    {
    }

    /// <summary>The one requirement.</summary>
    public static FormBuilderSectionRequirement Instance { get; } = new();
}

/// <summary>Any user of the Forms section who sees the resource of the check (ADR-0050).</summary>
internal sealed class FormBuilderVisibilityRequirement : IAuthorizationRequirement
{
    private FormBuilderVisibilityRequirement()
    {
    }

    /// <summary>The one requirement.</summary>
    public static FormBuilderVisibilityRequirement Instance { get; } = new();
}

/// <summary>A permission, on the resource of the check or globally.</summary>
/// <param name="Permission">Exactly one permission.</param>
internal sealed record FormBuilderPermissionRequirement(FormBuilderPermissions Permission) : IAuthorizationRequirement;
