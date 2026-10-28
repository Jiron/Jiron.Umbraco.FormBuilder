using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Jiron.Umbraco.FormBuilder.Web.PublicApi;

/// <summary>Forms, as headless sites render them (section 4.6, ADR-0084).</summary>
/// <param name="requests">What the endpoint reads of a request and answers with.</param>
/// <param name="forms">The published forms.</param>
[Route("forms")]
internal sealed class FormController(FormRequests requests, PublishedFormCache forms) : FormBuilderPublicApiController
{
    /// <summary>
    /// A published form for headless rendering, when its settings allow it: its public projection with its texts in the
    /// culture, and what the endpoint adds. <c>Cache-Control: no-cache</c> with an <c>ETag</c> that is a hash of the
    /// answer, so it follows the publish, the culture, the dictionary and the host's designer locks.
    /// </summary>
    /// <param name="keyOrAlias">The form's key, or its alias in any letter case.</param>
    /// <param name="culture">The culture: one of the site's languages, otherwise the default language.</param>
    /// <param name="contentKey">The page the form is shown on, whose site's settings apply.</param>
    /// <param name="cancellationToken">The request's cancellation, with its time limit.</param>
    [HttpGet("{keyOrAlias}")]
    [FormBuilderRateLimit(FormBuilderRateLimitPolicies.Read)]
    [FormBuilderBodyLimit(0)]
    [FormBuilderTimeLimit(10)]
    [ProducesResponseType<FormDefinitionResponseModel>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, FormBuilderProblemResult.MediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, FormBuilderProblemResult.MediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, FormBuilderProblemResult.MediaType)]
    public async Task<IActionResult> Definition(string keyOrAlias, [FromQuery] string? culture, [FromQuery] string? contentKey, CancellationToken cancellationToken)
    {
        Guid? content = null;
        if (contentKey is not null)
        {
            if (!Guid.TryParse(contentKey, out var key))
            {
                return FormBuilderPublicProblems.InvalidRequest("contentKey");
            }

            content = key;
        }

        if (!FormRequests.IsCulture(culture))
        {
            return FormBuilderPublicProblems.InvalidRequest("culture");
        }

        if (await requests.KeyOfAsync(keyOrAlias, cancellationToken) is not { } formKey
            || await forms.GetAsync(formKey, cancellationToken) is not { } form
            || !form.Published.Definition.Settings.Developer.Headless)
        {
            return FormBuilderPublicProblems.FormNotFound();
        }

        var answer = await requests.DefinitionAsync(form, await requests.CultureAsync(culture), await requests.SiteAsync(content), cancellationToken);
        Response.Headers.ETag = answer.ETag;
        Response.Headers.CacheControl = "no-cache";
        if (Matches(Request.GetTypedHeaders().IfNoneMatch, answer.ETag))
        {
            return StatusCode(StatusCodes.Status304NotModified);
        }

        return File(answer.Body, "application/json; charset=utf-8");
    }

    // Whether the client holds this answer already: its tag, by the weak comparison If-None-Match uses, or any.
    private static bool Matches(IList<EntityTagHeaderValue> held, string etag)
    {
        var current = new EntityTagHeaderValue(etag);
        return held.Any(tag => tag.Equals(EntityTagHeaderValue.Any) || tag.Compare(current, useStrongComparison: false));
    }
}

/// <summary>
/// Every address below the base path that names no endpoint: <c>404 not_found</c>, not a page of the site. The route has
/// no order of its own: an endpoint's route is more precise than the catch-all and wins, and the catch-all's literal base
/// path is more precise than the site's own route for content, which an order would let win.
/// </summary>
[Route("{**path}")]
[ApiExplorerSettings(IgnoreApi = true)]
internal sealed class NoEndpointController : FormBuilderPublicApiController
{
    /// <summary>The answer to any method, an <c>OPTIONS</c> that is no preflight included; a <c>HEAD</c> gets the status alone.</summary>
    [AcceptVerbs("GET", "HEAD", "POST", "PUT", "PATCH", "DELETE", "OPTIONS")]
    [FormBuilderRateLimit(FormBuilderRateLimitPolicies.Read)]
    [FormBuilderBodyLimit(0)]
    [FormBuilderTimeLimit(10)]
    public IActionResult Missing() => HttpMethods.IsHead(Request.Method) ? NotFound() : FormBuilderPublicProblems.NotFound();
}

/// <summary>A form for headless rendering: what the endpoint adds, and the visitor's form (ADR-0084).</summary>
internal sealed record FormDefinitionResponseModel
{
    /// <summary>The form's alias.</summary>
    public required string Alias { get; init; }

    /// <summary>The public revision: it goes up whenever a publish changes what visitors get; submissions post it.</summary>
    public required int Revision { get; init; }

    /// <summary>The culture the texts are in: the one asked for when the site has it, otherwise the default language.</summary>
    public required string Culture { get; init; }

    /// <summary>The site's time zone, an IANA id, in which "today" and dates relative to it are counted.</summary>
    public required string TimeZone { get; init; }

    /// <summary>Whether the form takes entries now by its opening and closing times; a closed form shows its closed message.</summary>
    public required bool Open { get; init; }

    /// <summary>
    /// The form's public projection with its texts in the culture: the pages, groups and fields, their rules and checks,
    /// and the settings the page needs.
    /// </summary>
    public required JsonElement Form { get; init; }
}
