using Jiron.Umbraco.FormBuilder.Core.Access;
using Microsoft.AspNetCore.Authorization;
using OpenIddict.Validation.AspNetCore;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Authorization;

/// <summary>
/// The authorization policies of the management API (ADR-0013, ADR-0049): one for the Forms section and one per
/// permission, named after the permission's stable name. Each needs a user signed in with the backoffice's token. A
/// permission's policy checks globally when an endpoint carries it, and against a <see cref="FormBuilderAccessResource"/>
/// when an endpoint asks <see cref="IAuthorizationService"/> with one.
/// </summary>
internal static class FormBuilderPolicies
{
    /// <summary>The start of every policy's name.</summary>
    public const string Prefix = "Jiron.FormBuilder.Policy.";

    /// <summary>Any user of the Forms section, for what every user of it may call.</summary>
    public const string Section = Prefix + "Section";

    /// <summary>Design forms.</summary>
    public const string Design = Prefix + "design";

    /// <summary>Manage a form's actions.</summary>
    public const string ManageActions = Prefix + "manageActions";

    /// <summary>View entries.</summary>
    public const string ViewEntries = Prefix + "viewEntries";

    /// <summary>View sensitive values unmasked.</summary>
    public const string ViewSensitive = Prefix + "viewSensitive";

    /// <summary>Edit entries.</summary>
    public const string EditEntries = Prefix + "editEntries";

    /// <summary>Delete entries.</summary>
    public const string DeleteEntries = Prefix + "deleteEntries";

    /// <summary>Export entries.</summary>
    public const string Export = Prefix + "export";

    /// <summary>Manage the library: option lists, themes, email layouts and templates.</summary>
    public const string ManageLibrary = Prefix + "manageLibrary";

    /// <summary>Manage the settings, the audit log and the deliveries log.</summary>
    public const string ManageSettings = Prefix + "manageSettings";

    /// <summary>Manage permissions.</summary>
    public const string ManagePermissions = Prefix + "managePermissions";

    /// <summary>Use the privacy tools.</summary>
    public const string PrivacyTools = Prefix + "privacyTools";

    /// <summary>The policy of one permission.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not exactly one permission.</exception>
    public static string For(FormBuilderPermissions permission) => Prefix + PermissionRules.NameOf(permission);

    /// <summary>Adds every policy.</summary>
    public static void AddTo(AuthorizationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.AddPolicy(Section, policy => Backoffice(policy).AddRequirements(FormBuilderSectionRequirement.Instance));
        foreach (var permission in PermissionRules.Each)
        {
            options.AddPolicy(For(permission), policy => Backoffice(policy).AddRequirements(new FormBuilderPermissionRequirement(permission)));
        }
    }

    // The backoffice's token, as Umbraco's own management API takes it; without one, the call is challenged.
    private static AuthorizationPolicyBuilder Backoffice(AuthorizationPolicyBuilder policy) =>
        policy.AddAuthenticationSchemes(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme).RequireAuthenticatedUser();
}
