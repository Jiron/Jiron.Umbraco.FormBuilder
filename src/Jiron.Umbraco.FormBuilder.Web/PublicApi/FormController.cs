using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace Jiron.Umbraco.FormBuilder.Web.PublicApi;

/// <summary>Forms, as headless sites render them, and their fields' options (section 4.6, ADR-0084, ADR-0086).</summary>
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

        return Answered(await requests.DefinitionAsync(form, await requests.CultureAsync(culture), await requests.PageAsync(content), cancellationToken));
    }

    /// <summary>
    /// A field's options for the answers a request gives up its chain of parents, for a cascading field once a visitor
    /// answers its parent: each parent's answers held to the options that parent offers, so no text reaches a source
    /// unless an option has it (ADR-0081). Every published form not in the trash answers, as the page that shows it.
    /// <c>available</c> is false when the field's source could not give its options. <c>Cache-Control: no-cache</c>
    /// with an <c>ETag</c> that is a hash of the answer.
    /// </summary>
    /// <param name="key">The form's key.</param>
    /// <param name="fieldKey">The key of a field of the form, a repeatable group's among them, that has options.</param>
    /// <param name="culture">The culture: one of the site's languages, otherwise the default language.</param>
    /// <param name="contentKey">The page the form is shown on, which a source may read.</param>
    /// <param name="parent">The answers of the field's parent, once per answer.</param>
    /// <param name="cancellationToken">The request's cancellation, with its time limit.</param>
    /// <remarks>
    /// A field further up the chain answers as <c>parent.{fieldKey}</c>, once per answer; the field's own parent may
    /// too.
    /// </remarks>
    [HttpGet("{key}/options/{fieldKey}")]
    [FormBuilderRateLimit(FormBuilderRateLimitPolicies.Read)]
    [FormBuilderBodyLimit(0)]
    [FormBuilderTimeLimit(20)]
    [ProducesResponseType<FieldOptionsResponseModel>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, FormBuilderProblemResult.MediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, FormBuilderProblemResult.MediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, FormBuilderProblemResult.MediaType)]
    public async Task<IActionResult> Options(
        string key,
        string fieldKey,
        [FromQuery] string? culture,
        [FromQuery] string? contentKey,
        [FromQuery] string[]? parent,
        CancellationToken cancellationToken)
    {
        Guid? content = null;
        if (contentKey is not null)
        {
            if (!Guid.TryParse(contentKey, out var page))
            {
                return FormBuilderPublicProblems.InvalidRequest("contentKey");
            }

            content = page;
        }

        if (!FormRequests.IsCulture(culture))
        {
            return FormBuilderPublicProblems.InvalidRequest("culture");
        }

        if (!Guid.TryParseExact(key, "D", out var formKey) || await forms.GetAsync(formKey, cancellationToken) is not { } form)
        {
            return FormBuilderPublicProblems.FormNotFound();
        }

        if (!Guid.TryParseExact(fieldKey, "D", out var fieldGuid) || OptionField(form.Published.Definition, fieldGuid) is not { } field)
        {
            return FormBuilderPublicProblems.NotFound();
        }

        if (Answers(field, parent ?? []) is not { } answers)
        {
            return FormBuilderPublicProblems.InvalidRequest("parent");
        }

        return Answered(await requests.OptionsAsync(form, field, asked => answers.TryGetValue(asked, out var given) ? given : [], await requests.CultureAsync(culture), await requests.PageAsync(content), cancellationToken));
    }

    // The field with this key, a repeatable group's child among them, when it has options of its own or from a source.
    private static FormField? OptionField(FormDefinition definition, Guid key) =>
        definition.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields).SelectMany(field => field.Fields.Prepend(field))
            .FirstOrDefault(field => field.Key == key) is { Options: not null } found ? found : null;

    // The answers the query gives by field: the parent's as parent and as parent.{key}, every other field's as
    // parent.{key}; null when a parent. name holds no field key.
    private Dictionary<Guid, List<string>>? Answers(FormField field, string[] parent)
    {
        var answers = new Dictionary<Guid, List<string>>();
        if (field.Options?.Parent is { } own && parent.Length > 0)
        {
            answers[own] = [.. parent];
        }

        foreach (var (name, values) in Request.Query)
        {
            if (!name.StartsWith(ParentPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!Guid.TryParseExact(name.AsSpan(ParentPrefix.Length), "D", out var answered))
            {
                return null;
            }

            if (!answers.TryGetValue(answered, out var list))
            {
                answers[answered] = list = [];
            }

            list.AddRange(values.OfType<string>());
        }

        return answers;
    }

    // An answer with its tag and no-cache, or 304 when the client holds it.
    private IActionResult Answered(FormAnswer answer)
    {
        Response.Headers.ETag = answer.ETag;
        Response.Headers.CacheControl = "no-cache";
        if (Matches(Request.GetTypedHeaders().IfNoneMatch, answer.ETag))
        {
            return StatusCode(StatusCodes.Status304NotModified);
        }

        return File(answer.Body, "application/json; charset=utf-8");
    }

    // The start of a query name that gives the answers of a field up the chain.
    private const string ParentPrefix = "parent.";

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
    /// and the settings the page needs; a consent's text is the wording it shows, its merge tags rendered.
    /// </summary>
    public required JsonElement Form { get; init; }

    /// <summary>
    /// What the browser renders the visitor texts with (ADR-0085); null when every one shows as it is written:
    /// <c>texts</c>, each that renders as its owner's key, the property it is and its context (<c>html</c> or
    /// <c>text</c>); <c>values</c>, each path's value but answer recall's by context; <c>recall</c>, the form answer
    /// recall reads, cut down to what the texts need; <c>words</c>, the culture's words for yes and no; and
    /// <c>culture</c>, its formats.
    /// </summary>
    public JsonElement? MergeTags { get; init; }

    /// <summary>Each consent field's wording hash by its key, which a submission posts with the ticked consent (ADR-0009).</summary>
    public required IReadOnlyDictionary<Guid, string> Consents { get; init; }
}

/// <summary>A field's options for its parents' answers (ADR-0086).</summary>
internal sealed record FieldOptionsResponseModel
{
    /// <summary>The culture the labels are in: the one asked for when the site has it, otherwise the default language.</summary>
    public required string Culture { get; init; }

    /// <summary>
    /// Whether the field's options could be given: false when its list or source is missing, failed or took too long, or
    /// a parent's options could not be given; a field without its options shows as one whose source failed then.
    /// </summary>
    public required bool Available { get; init; }

    /// <summary>The options, in the order visitors see them; none when there are none for the answers.</summary>
    public required IReadOnlyList<FieldOptionResponseModel> Options { get; init; }
}

/// <summary>One option of a field.</summary>
internal sealed record FieldOptionResponseModel
{
    /// <summary>What an answer posts and an entry stores.</summary>
    public required string Value { get; init; }

    /// <summary>The label in the culture; the value when the option has none.</summary>
    public required string Label { get; init; }

    /// <summary>A text below the label; only when the option has one.</summary>
    public string? Description { get; init; }

    /// <summary>The key of a media item shown with the option; only when the option has one.</summary>
    public Guid? Image { get; init; }
}
