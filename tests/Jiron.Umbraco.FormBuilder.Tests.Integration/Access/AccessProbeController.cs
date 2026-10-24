using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Validation.AspNetCore;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Access;

/// <summary>
/// A test-only controller that asks Jiron Form Builder's policies as the endpoints of 4.7 will (ADR-0049): for the
/// signed-in backoffice user, every policy globally and against folders and forms, and two endpoints that carry a
/// policy on the attribute. Only the integration tests' sites have it.
/// </summary>
[ApiController]
[Route(Route)]
[Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
public sealed class AccessProbeController(IAuthorizationService authorization) : ControllerBase
{
    /// <summary>Where it answers.</summary>
    public const string Route = "umbraco/jfb-test/access";

    /// <summary>What the policies say for every resource asked: allowed, or the denial's kind and permission.</summary>
    [HttpPost("check")]
    public async Task<IActionResult> CheckAsync(AccessProbeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var answers = new Dictionary<string, Dictionary<string, string>>();
        foreach (var asked in request.Resources)
        {
            var resource = await ResourceAsync(asked);
            var policies = new Dictionary<string, string>
            {
                ["section"] = Outcome(await authorization.AuthorizeAsync(User, resource, FormBuilderPolicies.Section)),
            };
            foreach (var permission in PermissionRules.Each)
            {
                policies[PermissionRules.NameOf(permission)] = Outcome(await authorization.AuthorizeAsync(User, resource, FormBuilderPolicies.For(permission)));
            }

            answers[asked.Name] = policies;
        }

        return Ok(answers);
    }

    /// <summary>Carries the section's policy on the attribute.</summary>
    [HttpGet("section")]
    [Authorize(Policy = FormBuilderPolicies.Section)]
    public IActionResult Section() => NoContent();

    /// <summary>Carries the design policy on the attribute, so it checks globally.</summary>
    [HttpGet("design")]
    [Authorize(Policy = FormBuilderPolicies.Design)]
    public IActionResult Design() => NoContent();

    private static string Outcome(AuthorizationResult result) =>
        result.Succeeded ? "allowed"
        : FormBuilderAccessDenial.Of(result) is { } denial ? $"{denial.Kind}:{(denial.Permission == FormBuilderPermissions.None ? "section" : PermissionRules.NameOf(denial.Permission))}"
        : "failed";

    // A form as an endpoint reads it; a folder by its key; the root without one.
    private async Task<FormBuilderAccessResource?> ResourceAsync(AccessProbeResource asked) => asked.Kind switch
    {
        "form" => FormBuilderAccessResource.ForForm(FormAccessTarget.Of((await HttpContext.RequestServices.GetRequiredService<IFormBuilderFormService>().GetAsync(asked.Key!.Value))!)),
        "folder" => FormBuilderAccessResource.ForFolder(asked.Key),
        "global" => FormBuilderAccessResource.Global,
        _ => null,
    };
}

/// <summary>What to ask the policies about.</summary>
/// <param name="Resources">The resources, each by a name the answer repeats.</param>
public sealed record AccessProbeRequest(IReadOnlyList<AccessProbeResource> Resources);

/// <summary>One resource: <c>global</c>, <c>folder</c> (no key: the root), <c>form</c>, or <c>none</c> for no resource.</summary>
/// <param name="Name">The name the answer uses.</param>
/// <param name="Kind">Which kind.</param>
/// <param name="Key">The folder's or form's key.</param>
public sealed record AccessProbeResource(string Name, string Kind, Guid? Key = null);
