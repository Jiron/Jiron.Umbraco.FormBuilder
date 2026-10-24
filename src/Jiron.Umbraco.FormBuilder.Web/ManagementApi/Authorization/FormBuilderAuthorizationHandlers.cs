using Jiron.Umbraco.FormBuilder.Core.Access;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Umbraco.Cms.Core.DependencyInjection;
using Umbraco.Cms.Core.Security.Authorization;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Authorization;

/// <summary>The registrations of the management API's authorization (ADR-0049), which the Web composer makes.</summary>
internal static class FormBuilderAuthorization
{
    /// <summary>
    /// Adds the policies and their handlers, as singletons: Umbraco's singleton event authorizers consume
    /// <see cref="IAuthorizationService"/>, so a scoped handler fails the site's start-up (ADR-0049).
    /// </summary>
    public static IUmbracoBuilder AddFormBuilderAuthorization(this IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IAuthorizationHandler, FormBuilderSectionHandler>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IAuthorizationHandler, FormBuilderPermissionHandler>());
        builder.Services.Configure<AuthorizationOptions>(FormBuilderPolicies.AddTo);
        return builder;
    }
}

/// <summary>Lets a user of the Forms section through, reading their sections as Umbraco gives them (ADR-0049).</summary>
internal sealed class FormBuilderSectionHandler(IAuthorizationHelper users) : AuthorizationHandler<FormBuilderSectionRequirement>
{
    /// <inheritdoc />
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, FormBuilderSectionRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (users.TryGetUmbracoUser(context.User, out var user))
        {
            if (AccessUser.Of(user).HasSection)
            {
                context.Succeed(requirement);
            }
            else
            {
                context.Fail(new FormBuilderAccessDenial(this, FormBuilderDenialKind.NoSection, FormBuilderPermissions.None));
            }
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Lets a user through who holds the permission on the resource of the check, or globally without one (ADR-0048,
/// ADR-0049). It refuses a user without the Forms section first, then one who cannot see the folder or form, then one
/// who lacks the permission, each with its <see cref="FormBuilderAccessDenial"/>.
/// </summary>
internal sealed class FormBuilderPermissionHandler(IAuthorizationHelper users, IFormBuilderAccessService access)
    : AuthorizationHandler<FormBuilderPermissionRequirement>
{
    /// <inheritdoc />
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, FormBuilderPermissionRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);
        if (!users.TryGetUmbracoUser(context.User, out var umbracoUser))
        {
            return;
        }

        var user = AccessUser.Of(umbracoUser);
        if (!user.HasSection)
        {
            context.Fail(new FormBuilderAccessDenial(this, FormBuilderDenialKind.NoSection, requirement.Permission));
            return;
        }

        var resource = context.Resource as FormBuilderAccessResource ?? FormBuilderAccessResource.Global;
        var held = await HeldAsync(user, resource);
        if ((held & requirement.Permission) == requirement.Permission)
        {
            context.Succeed(requirement);
            return;
        }

        var kind = await SeesAsync(user, resource, held) ? FormBuilderDenialKind.MissingPermission : FormBuilderDenialKind.NotFound;
        context.Fail(new FormBuilderAccessDenial(this, kind, requirement.Permission));
    }

    private Task<FormBuilderPermissions> HeldAsync(AccessUser user, FormBuilderAccessResource resource) => resource.Kind switch
    {
        FormBuilderAccessResourceKind.Form => access.GetForFormAsync(user, resource.Form!),
        FormBuilderAccessResourceKind.Folder => access.GetForFolderAsync(user, resource.FolderKey),
        _ => access.GetGlobalAsync(user),
    };

    // What the user's lists show: a form they hold something on, and a folder they hold something on or that leads to
    // one; the root and the global checks always (ADR-0048, ADR-0049).
    private async Task<bool> SeesAsync(AccessUser user, FormBuilderAccessResource resource, FormBuilderPermissions held) => resource switch
    {
        { Kind: FormBuilderAccessResourceKind.Form } => held != FormBuilderPermissions.None,
        { Kind: FormBuilderAccessResourceKind.Folder, FolderKey: { } folderKey } =>
            held != FormBuilderPermissions.None || (await access.GetFolderVisibilityAsync(user)).Allows(folderKey),
        _ => true,
    };
}
