using Asp.Versioning;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Email;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Web.Common.Routing;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Setup;

/// <summary>A test mail (section 4.4, ADR-0159): it needs <c>manageSettings</c>, and goes to the user who asks and to nobody else.</summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/email/test")]
[ApiExplorerSettings(GroupName = "Email")]
internal sealed class TestEmailController(TestEmails emails, IUserService users) : FormBuilderManagementApiController
{
    /// <summary>Queues a test mail to the user who asks; it leaves like any other mail.</summary>
    /// <param name="request">Whose sender and which layout it shows.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageSettings)]
    [ProducesResponseType(typeof(TestEmailResponseModel), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Send(TestEmailRequestModel? request = null, CancellationToken cancellationToken = default)
    {
        var user = await users.GetAsync(RequestAccess.User.Key);
        var status = await emails.SendAsync(user?.Email, user?.Language, request?.Site, request?.Layout, cancellationToken);
        return status switch
        {
            TestEmailStatus.Queued => Accepted(new TestEmailResponseModel { To = user!.Email }),
            TestEmailStatus.NotConfigured => FormBuilderProblems.EmailNotConfigured(),
            _ => FormBuilderProblems.EmailNoAddress(),
        };
    }
}

/// <summary>What a test mail shows.</summary>
public sealed class TestEmailRequestModel
{
    /// <summary>The key of the site's root whose sender and layout it shows; null for the global settings.</summary>
    public Guid? Site { get; init; }

    /// <summary>The key of the layout it shows; null for the site's.</summary>
    public Guid? Layout { get; init; }
}

/// <summary>A queued test mail.</summary>
public sealed class TestEmailResponseModel
{
    /// <summary>The address it goes to: the asking user's.</summary>
    public required string To { get; init; }
}
