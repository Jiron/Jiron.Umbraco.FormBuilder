using System.Text.Json;
using Asp.Versioning;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Web.Common.Routing;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Designer;

/// <summary>
/// The form check of the draft the designer holds (ROADMAP 9.5b, ADR-0177): every error, warning and hint, each with the
/// path of the element and the setting it is about, as publishing would weigh them. It needs <c>design</c> on the form,
/// as the draft it reads is the designer's, and it changes nothing.
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/form/{key:guid}/check")]
[ApiExplorerSettings(GroupName = "Form check")]
internal sealed class FormCheckController(FormCheck check, FormDefinitionSerializer serializer) : FormBuilderManagementApiController
{
    /// <summary>The issues of the definition the request holds: the errors first, then the warnings, then the hints.</summary>
    /// <param name="key">The form.</param>
    /// <param name="request">The draft's definition.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.Design, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(FormCheckResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Check(Guid key, FormCheckRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The form's key is the permission's, which its filter read from the route before the action ran.
        _ = key;
        var read = serializer.Read(request.Definition.GetRawText());
        if (read.Definition is not { } definition)
        {
            throw new FormBuilderValidationException(read.Problems);
        }

        var issues = await check.CheckAsync(definition, cancellationToken);
        return Ok(new FormCheckResponseModel { Issues = [.. issues.Select(FormCheckIssueResponseModel.Of)] });
    }
}

/// <summary>What the form check is asked to check.</summary>
internal sealed record FormCheckRequestModel
{
    /// <summary>The draft's definition as the designer holds it, read as a draft save reads it.</summary>
    public required JsonElement Definition { get; init; }
}

/// <summary>The form check's issues.</summary>
internal sealed record FormCheckResponseModel
{
    /// <summary>The errors first, then the warnings, then the hints, each in the order the checks found them.</summary>
    public required IReadOnlyList<FormCheckIssueResponseModel> Issues { get; init; }
}

/// <summary>An issue of the form check.</summary>
internal sealed record FormCheckIssueResponseModel
{
    /// <summary><c>error</c>, which keeps the form from going live; <c>warning</c> or <c>hint</c>, which never do.</summary>
    public required string Severity { get; init; }

    /// <summary>What the issue is, such as <c>label_missing</c> or <c>unknown_path</c>.</summary>
    public required string Code { get; init; }

    /// <summary>The JSON path of the element and the setting it is about, such as <c>$.pages[0].groups[0].fields[1].label</c>.</summary>
    public required string Path { get; init; }

    /// <summary>The issue in English; the designer says it by its code.</summary>
    public required string Message { get; init; }

    /// <summary>The model of an issue.</summary>
    public static FormCheckIssueResponseModel Of(FormCheckIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        return new FormCheckIssueResponseModel
        {
            Severity = issue.Severity switch
            {
                FormCheckSeverity.Error => "error",
                FormCheckSeverity.Warning => "warning",
                _ => "hint",
            },
            Code = issue.Code,
            Path = issue.Path,
            Message = issue.Message,
        };
    }
}
