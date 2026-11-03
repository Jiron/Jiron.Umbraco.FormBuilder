using Jiron.Umbraco.FormBuilder.Web.ManagementApi;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jiron.Umbraco.FormBuilder.Web.PublicApi;

/// <summary>
/// A saved form's way back (section 4.6, ADR-0163). The token comes from the link's fragment, which no browser sends to a server, and is
/// posted in a body, so it reaches no log.
/// </summary>
/// <param name="entries">What the endpoints of a submission read of a request and answer with.</param>
[Route("drafts")]
internal sealed class DraftsController(EntryRequests entries) : FormBuilderPublicApiController
{
    /// <summary>The answers of the draft a resume link names, and the page to go on from. The link keeps working until the draft is submitted or its time is over.</summary>
    /// <param name="cancellationToken">The request's cancellation, with its time limit.</param>
    /// <remarks><c>404 not_found</c> for an unknown token or a draft already submitted; <c>410 resume_expired</c> for a link past its time.</remarks>
    [HttpPost("resume")]
    [FormBuilderRateLimit(FormBuilderRateLimitPolicies.Verify)]
    [FormBuilderBodyLimit(EntryRequests.MaxReceiptBodyBytes)]
    [FormBuilderTimeLimit(30)]
    [FormBuilderJsonBody(typeof(ResumeRequestModel))]
    [ProducesResponseType<ResumeResponseModel>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, FormBuilderProblemResult.MediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, FormBuilderProblemResult.MediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, FormBuilderProblemResult.MediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status410Gone, FormBuilderProblemResult.MediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge, FormBuilderProblemResult.MediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType, FormBuilderProblemResult.MediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, FormBuilderProblemResult.MediaType)]
    public Task<IActionResult> Resume(CancellationToken cancellationToken) => entries.ResumeAsync(HttpContext, cancellationToken);
}
