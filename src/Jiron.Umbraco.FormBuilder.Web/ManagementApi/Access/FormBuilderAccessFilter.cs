using System.Reflection;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Security.Authorization;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;

/// <summary>
/// Checks every action of the management API against its declaration (ADR-0050), before the request's body is
/// validated: the Forms section (<c>403 no_section</c>), then the migration state (<c>503 migrations_pending</c>), then
/// the folder or form the declaration names (<c>404</c> when it does not exist or the user does not see it), then each
/// permission through its policy (<c>403 forbidden</c> naming it), then a section of Umbraco's own the declaration names
/// (<c>403 forbidden</c> naming it), and last the request itself
/// (<c>400 invalid_request</c>). A body that holds the key but cannot be read answers <c>400 invalid_request</c> before
/// any check of the folder or form, which it would otherwise make in the wrong place. An action without exactly one
/// declaration, or with a key that names nothing, is refused.
/// </summary>
internal sealed partial class FormBuilderAccessFilter(
    IAuthorizationService authorization,
    IAuthorizationHelper users,
    IFormBuilderMigrationState migrations,
    IFormBuilderFormService forms,
    IFormBuilderFolderService folders,
    ILogger<FormBuilderAccessFilter> logger)
    : IAsyncActionFilter
{
    /// <summary>The filter's order: before the validation of the request that <c>[ApiController]</c> adds (-2000).</summary>
    public const int FilterOrder = -3000;

    /// <inheritdoc />
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        if (await RefusalAsync(context) is { } refusal)
        {
            context.Result = refusal;
            return;
        }

        await next();
    }

    /// <summary>
    /// The value of the declaration's key (an empty or missing key is null): <see cref="KeyRead.Undeclared"/> when the
    /// declaration names no parameter or body property of a key's type, <see cref="KeyRead.Unreadable"/> when it names a
    /// property of a body that could not be read.
    /// </summary>
    internal static KeyRead ReadKey(ActionDescriptor action, IDictionary<string, object?> arguments, string name, out Guid? key)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(arguments);
        key = null;
        if (action.Parameters.FirstOrDefault(parameter => string.Equals(parameter.Name, name, StringComparison.Ordinal)) is { } named)
        {
            return IsKey(named.ParameterType) ? Read(arguments.TryGetValue(named.Name, out var value) ? value : null, out key) : KeyRead.Undeclared;
        }

        foreach (var parameter in action.Parameters.Where(parameter => parameter.BindingInfo?.BindingSource == BindingSource.Body))
        {
            if (parameter.ParameterType.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase) is { } property && IsKey(property.PropertyType))
            {
                return arguments.TryGetValue(parameter.Name, out var body) && body is not null ? Read(property.GetValue(body), out key) : KeyRead.Unreadable;
            }
        }

        return KeyRead.Undeclared;

        static bool IsKey(Type type) => type == typeof(Guid) || type == typeof(Guid?);

        static KeyRead Read(object? value, out Guid? key)
        {
            key = value is Guid guid && guid != Guid.Empty ? guid : null;
            return KeyRead.Read;
        }
    }

    private async Task<IActionResult?> RefusalAsync(ActionExecutingContext context)
    {
        var http = context.HttpContext;
        var declarations = context.ActionDescriptor.EndpointMetadata.OfType<FormBuilderAccessAttribute>().ToList();
        if (declarations.Count != 1)
        {
            LogUndeclared(logger, context.ActionDescriptor.DisplayName ?? context.ActionDescriptor.Id, declarations.Count);
            return FormBuilderProblems.Forbidden();
        }

        var declaration = declarations[0];
        if (!users.TryGetUmbracoUser(http.User, out var umbracoUser))
        {
            return new ChallengeResult();
        }

        if (!(await authorization.AuthorizeAsync(http.User, FormBuilderPolicies.Section)).Succeeded)
        {
            return FormBuilderProblems.NoSection();
        }

        if (await migrations.IsPendingAsync(http.RequestAborted))
        {
            return FormBuilderProblems.MigrationsPending(http);
        }

        Guid? key = null;
        if (declaration.On != FormBuilderAccessResourceKind.Global)
        {
            var read = declaration.Key is null ? KeyRead.Undeclared : ReadKey(context.ActionDescriptor, context.ActionArguments, declaration.Key, out key);
            if (read == KeyRead.Undeclared)
            {
                LogMisdeclared(logger, context.ActionDescriptor.DisplayName ?? context.ActionDescriptor.Id, declaration.Key ?? "(none)");
                return FormBuilderProblems.Forbidden();
            }

            if (read == KeyRead.Unreadable)
            {
                return FormBuilderProblems.InvalidRequest(context.ModelState);
            }
        }

        var access = new FormBuilderRequestAccess(AccessUser.Of(umbracoUser));
        var resource = FormBuilderAccessResource.Global;
        if (declaration.On == FormBuilderAccessResourceKind.Form)
        {
            if (key is not { } formKey || await forms.GetAsync(formKey, http.RequestAborted) is not { } form)
            {
                return FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Form);
            }

            access = access with { Form = form };
            resource = FormBuilderAccessResource.ForForm(FormAccessTarget.Of(form));
        }
        else if (declaration.On == FormBuilderAccessResourceKind.Folder)
        {
            if (key is { } folderKey)
            {
                if (await folders.GetAsync(folderKey, http.RequestAborted) is not { } folder)
                {
                    return FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Folder);
                }

                access = access with { Folder = folder };
            }

            resource = FormBuilderAccessResource.ForFolder(key);
        }

        foreach (var policy in Policies(declaration))
        {
            var result = await authorization.AuthorizeAsync(http.User, resource, policy);
            if (!result.Succeeded)
            {
                return FormBuilderProblems.Refused(result, resource.Kind);
            }
        }

        if (declaration.UmbracoSection is { } section && !umbracoUser.AllowedSections.Contains(section, StringComparer.OrdinalIgnoreCase))
        {
            return FormBuilderProblems.MissingSection(section);
        }

        if (!context.ModelState.IsValid)
        {
            return FormBuilderProblems.InvalidRequest(context.ModelState);
        }

        http.Features.Set(access);
        return null;
    }

    // A section declaration checks what the user sees, when it names a folder or form; a permission declaration each
    // permission, in the table's order.
    private static IEnumerable<string> Policies(FormBuilderAccessAttribute declaration) =>
        declaration.Permissions == FormBuilderPermissions.None
            ? declaration.On == FormBuilderAccessResourceKind.Global ? [] : [FormBuilderPolicies.Visible]
            : PermissionRules.Each.Where(permission => (declaration.Permissions & permission) != FormBuilderPermissions.None).Select(FormBuilderPolicies.For);

    /// <summary>How the key of a declaration was read.</summary>
    internal enum KeyRead
    {
        /// <summary>Read: a key, or none for the root.</summary>
        Read,

        /// <summary>No parameter or body property of a key's type has the name.</summary>
        Undeclared,

        /// <summary>The body that holds it could not be read.</summary>
        Unreadable,
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The management API action {Action} carries {Count} access declarations instead of one; it is refused.")]
    private static partial void LogUndeclared(ILogger logger, string action, int count);

    [LoggerMessage(Level = LogLevel.Error, Message = "The access declaration of the management API action {Action} names the key {Key}, which no parameter or body property holds; it is refused.")]
    private static partial void LogMisdeclared(ILogger logger, string action, string key);
}
