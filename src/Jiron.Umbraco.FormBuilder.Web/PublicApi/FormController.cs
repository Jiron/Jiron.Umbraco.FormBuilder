using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Themes;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Jiron.Umbraco.FormBuilder.Web.Rendering.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace Jiron.Umbraco.FormBuilder.Web.PublicApi;

/// <summary>
/// Forms, as headless sites render them and as a page's markup, their fields' options, the files visitors upload to
/// them, the pages they check and the entries they submit, and the receipts of those (section 4.6, ADR-0084, ADR-0086,
/// ADR-0087, ADR-0088, ADR-0089, ADR-0104, ADR-0128).
/// </summary>
/// <param name="requests">What the endpoints of forms read of a request and answer with.</param>
/// <param name="pages">The culture and the page a request is for.</param>
/// <param name="uploads">What the upload endpoint reads of a request and answers with.</param>
/// <param name="entries">What the entries endpoint reads of a request and answers with.</param>
/// <param name="availability">Whether a form takes entries now.</param>
/// <param name="forms">The published forms.</param>
/// <param name="themes">The site's themes, which a page's markup is rendered in.</param>
/// <param name="rendering">What a page renders for a form, which the element endpoint answers as JSON.</param>
/// <param name="assets">The addresses of the stylesheets and the script a form's page loads.</param>
/// <param name="challenges">What the challenge endpoint answers.</param>
[Route("forms")]
internal sealed class FormController(
    FormRequests requests,
    FormPages pages,
    UploadRequests uploads,
    EntryRequests entries,
    FormAvailability availability,
    PublishedFormCache forms,
    FormBuilderThemes themes,
    FormRendering rendering,
    PageAssets assets,
    ChallengeRequests challenges) : FormBuilderPublicApiController
{
    /// <summary>The start of a query name that gives a context value to the element endpoint, as <c>&lt;jfb-render&gt;</c>'s attributes do.</summary>
    internal const string ContextQueryPrefix = "context-";

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

        if (!FormPages.IsCulture(culture))
        {
            return FormBuilderPublicProblems.InvalidRequest("culture");
        }

        if (await requests.KeyOfAsync(keyOrAlias, cancellationToken) is not { } formKey
            || await forms.GetAsync(formKey, cancellationToken) is not { } form
            || !form.Published.Definition.Settings.Developer.Headless)
        {
            return FormBuilderPublicProblems.FormNotFound();
        }

        return Answered(await requests.DefinitionAsync(form, await pages.CultureAsync(culture), await pages.PageAsync(content), cancellationToken));
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

        if (!FormPages.IsCulture(culture))
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

        return Answered(await requests.OptionsAsync(form, field, asked => answers.TryGetValue(asked, out var given) ? given : [], await pages.CultureAsync(culture), await pages.PageAsync(content), cancellationToken));
    }

    /// <summary>
    /// A published form's markup as a page shows it at one place (ADR-0004, ADR-0104), for a page's runtime that met
    /// <c>form_changed</c> on a page that may come from a cache: the same view component, in the page's culture, for its
    /// page and content, with the page's instance id, without the asset tags the page has already. Every published form
    /// not in the trash answers, as the page that shows it; a form for members shows what it shows the member signed in
    /// on the request, or its notice. No outcome shows. <c>Cache-Control: no-store</c>.
    /// </summary>
    /// <param name="key">The form's key.</param>
    /// <param name="instance">The instance id the page gave the form, such as <c>jfb-2</c>.</param>
    /// <param name="page">
    /// The page's path as the browser has it (<c>location.pathname</c>), which the form posts to without JavaScript: a
    /// local path with no query or fragment.
    /// </param>
    /// <param name="culture">The page's culture: one of the site's languages, otherwise the default language.</param>
    /// <param name="contentKey">The page's content, whose site's settings apply.</param>
    /// <param name="theme">The theme the page's placement names (ADR-0105), or none for the form's own; one the site lacks renders as <c>default</c>.</param>
    /// <param name="cancellationToken">The request's cancellation, with its time limit.</param>
    [HttpGet("{key}/render")]
    [FormBuilderRateLimit(FormBuilderRateLimitPolicies.Read)]
    [FormBuilderBodyLimit(0)]
    [FormBuilderTimeLimit(20)]
    [ProducesResponseType<string>(StatusCodes.Status200OK, "text/html")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, FormBuilderProblemResult.MediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, FormBuilderProblemResult.MediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, FormBuilderProblemResult.MediaType)]
    public async Task<IActionResult> Render(
        string key,
        [FromQuery, Required] string? instance,
        [FromQuery, Required] string? page,
        [FromQuery] string? culture,
        [FromQuery] string? contentKey,
        [FromQuery] string? theme,
        CancellationToken cancellationToken)
    {
        if (instance is null || !FormInstances.IsId(instance))
        {
            return FormBuilderPublicProblems.InvalidRequest("instance");
        }

        if (!IsPagePath(page))
        {
            return FormBuilderPublicProblems.InvalidRequest("page");
        }

        Guid? content = null;
        if (contentKey is not null)
        {
            if (!Guid.TryParse(contentKey, out var shown))
            {
                return FormBuilderPublicProblems.InvalidRequest("contentKey");
            }

            content = shown;
        }

        if (!FormPages.IsCulture(culture))
        {
            return FormBuilderPublicProblems.InvalidRequest("culture");
        }

        if (theme is not null && !FormBuilderThemes.IsAlias(theme))
        {
            return FormBuilderPublicProblems.InvalidRequest("theme");
        }

        if (!Guid.TryParseExact(key, "D", out var formKey) || await forms.GetAsync(formKey, cancellationToken) is null)
        {
            return FormBuilderPublicProblems.FormNotFound();
        }

        // A theme the site lacks renders as default, as the page renders it, without a line in the log or a name kept
        // for it: the name is the request's, and a page whose placement names it is logged as it renders (ADR-0105).
        var placed = theme is null ? null : themes.Find(theme)?.Name ?? BuiltInThemes.DefaultAlias;
        FormRenderTargets.Set(HttpContext, new FormRenderTarget(instance, page, content, culture, placed));
        var markup = new ViewComponentResult
        {
            ViewComponentName = FormBuilderFormViewComponent.Name,
            Arguments = new { form = formKey, assets = false },
            ContentType = "text/html; charset=utf-8",
            StatusCode = StatusCodes.Status200OK,
        };
        return new CultureResult(markup, CultureInfo.GetCultureInfo(await pages.CultureAsync(culture)));
    }

    /// <summary>
    /// A published form's first render as the headless element writes it (ADR-0128): the view model the render endpoint
    /// renders, for the same members, themes and availability, in the page's culture, with the instance id the element
    /// gave the form and the definition endpoint's answer as the model data's text, as JSON with the stylesheets and the
    /// script the page loads; without what only a post without JavaScript uses. A form that does not allow headless use
    /// answers <c>404</c>, as the definition endpoint does. <c>Cache-Control: no-store</c>.
    /// </summary>
    /// <param name="key">The form's key.</param>
    /// <param name="instance">The instance id the element gave the form, such as <c>jfb-1</c>.</param>
    /// <param name="culture">The page's culture: one of the site's languages, otherwise the default language.</param>
    /// <param name="contentKey">The page's content, whose site's settings apply.</param>
    /// <param name="theme">The theme the placement names (ADR-0105), or none for the form's own; one the site lacks renders as <c>default</c>.</param>
    /// <param name="cancellationToken">The request's cancellation, with its time limit.</param>
    [HttpGet("{key}/element")]
    [FormBuilderRateLimit(FormBuilderRateLimitPolicies.Read)]
    [FormBuilderBodyLimit(0)]
    [FormBuilderTimeLimit(20)]
    [ProducesResponseType<FormElementResponseModel>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, FormBuilderProblemResult.MediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, FormBuilderProblemResult.MediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, FormBuilderProblemResult.MediaType)]
    public async Task<IActionResult> Element(
        string key,
        [FromQuery, Required] string? instance,
        [FromQuery] string? culture,
        [FromQuery] string? contentKey,
        [FromQuery] string? theme,
        CancellationToken cancellationToken)
    {
        if (instance is null || !FormInstances.IsId(instance))
        {
            return FormBuilderPublicProblems.InvalidRequest("instance");
        }

        Guid? content = null;
        if (contentKey is not null)
        {
            if (!Guid.TryParse(contentKey, out var shown))
            {
                return FormBuilderPublicProblems.InvalidRequest("contentKey");
            }

            content = shown;
        }

        if (!FormPages.IsCulture(culture))
        {
            return FormBuilderPublicProblems.InvalidRequest("culture");
        }

        if (theme is not null && !FormBuilderThemes.IsAlias(theme))
        {
            return FormBuilderPublicProblems.InvalidRequest("theme");
        }

        var context = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, values) in Request.Query)
        {
            if (!name.StartsWith(ContextQueryPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (values.Count != 1 || name.Length == ContextQueryPrefix.Length || !context.TryAdd(name[ContextQueryPrefix.Length..], values[0]))
            {
                return FormBuilderPublicProblems.InvalidRequest(name);
            }
        }

        if (!Guid.TryParseExact(key, "D", out var formKey)
            || await forms.GetAsync(formKey, cancellationToken) is not { } form
            || !form.Published.Definition.Settings.Developer.Headless)
        {
            return FormBuilderPublicProblems.FormNotFound();
        }

        // Only the names the form lists reach the rendering, which logs a name it does not take once per process: a name a
        // request makes up is neither kept nor logged, as a theme the site lacks is not (6.8a).
        var listed = form.Published.Definition.Settings.Developer.ContextValues;
        foreach (var name in context.Keys.Where(name => !listed.Contains(name, StringComparer.OrdinalIgnoreCase)).ToList())
        {
            context.Remove(name);
        }

        // The page's culture for the models, as the render endpoint's views have it; a theme the site lacks renders as
        // default (ADR-0105).
        var shownCulture = CultureInfo.GetCultureInfo(await pages.CultureAsync(culture));
        CultureInfo.CurrentCulture = shownCulture;
        CultureInfo.CurrentUICulture = shownCulture;
        var placed = theme is null ? null : themes.Find(theme)?.Name ?? BuiltInThemes.DefaultAlias;
        FormRenderTargets.Set(HttpContext, new FormRenderTarget(instance, "/", content, culture, placed));
        if (await rendering.RenderAsync(HttpContext, formKey, context, null, cancellationToken) is not { Model: FormViewModel model, Theme: { } formTheme })
        {
            return FormBuilderPublicProblems.FormNotFound();
        }

        Response.Headers.CacheControl = "no-store";
        var answer = new ElementAnswer(ElementAnswer.Version, model, assets.Addresses(Request.PathBase, formTheme));
        return File(ElementJson.Write(answer), "application/json; charset=utf-8");
    }

    /// <summary>
    /// Uploads one file to a field before the submission (ADR-0006, ADR-0087), streamed as a <c>multipart/form-data</c>
    /// body: a <c>challenge</c> part, which only a server key leaves out, then the <c>file</c> part, and nothing after it. The file is held to the
    /// field's limit, its type's or the site's, whichever is smaller, before and while it is read, and checked on its
    /// first bytes before any is stored: a program or a script, an extension the site blocks, one the field does not
    /// take, and bytes that are not the kind the extension names are refused, whatever the client says the file is.
    /// The answer's token, which a submission posts in the field's value, counts for 24 hours. A form that takes no entry
    /// now, by its times or its entry limit, takes no upload either (ADR-0088).
    /// </summary>
    /// <param name="key">The form's key.</param>
    /// <param name="fieldKey">The key of a field of the form that takes files, a repeatable group's among them.</param>
    /// <param name="cancellationToken">The request's cancellation, with its time limit.</param>
    /// <remarks>
    /// A refusal of the file carries one error at <c>file</c> whose code is the field's kind of error (<c>fileSize</c>,
    /// <c>fileType</c> or <c>invalid</c>), so the page shows the field's own message. Every refusal closes the
    /// connection on HTTP/1.x.
    /// </remarks>
    [HttpPost("{key}/uploads")]
    [FormBuilderRateLimit(FormBuilderRateLimitPolicies.Upload)]
    [FormBuilderBodyLimit(FormBuilderBodyLimitAttribute.LargestUpload)]
    [FormBuilderTimeLimit(300)]
    [FormBuilderUploadBody]
    [ProducesResponseType<FileUploadResponseModel>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, FormBuilderProblemResult.MediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, FormBuilderProblemResult.MediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, FormBuilderProblemResult.MediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, FormBuilderProblemResult.MediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge, FormBuilderProblemResult.MediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType, FormBuilderProblemResult.MediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, FormBuilderProblemResult.MediaType)]
    public async Task<IActionResult> Upload(string key, [FromQuery, Required] string? fieldKey, CancellationToken cancellationToken)
    {
        if (!Guid.TryParseExact(fieldKey, "D", out var fieldGuid))
        {
            FormBuilderPublicProblems.CloseConnection(HttpContext);
            return FormBuilderPublicProblems.InvalidRequest("fieldKey");
        }

        if (!Guid.TryParseExact(key, "D", out var formKey) || await forms.GetAsync(formKey, cancellationToken) is not { } form)
        {
            FormBuilderPublicProblems.CloseConnection(HttpContext);
            return FormBuilderPublicProblems.FormNotFound();
        }

        if (uploads.FieldOf(form.Published.Definition, fieldGuid) is not { } field)
        {
            FormBuilderPublicProblems.CloseConnection(HttpContext);
            return FormBuilderPublicProblems.NotFound();
        }

        if (await availability.CheckAsync(form.Published.Definition.Settings.Availability, form.Published.Form.Key, cancellationToken) is { } unavailable)
        {
            FormBuilderPublicProblems.CloseConnection(HttpContext);
            return unavailable == FormUnavailable.Closed ? FormBuilderPublicProblems.FormClosed() : FormBuilderPublicProblems.EntryLimitReached();
        }

        // A form for members takes files only from a member it admits, with their own challenge (ADR-0012, ADR-0138).
        Guid? member = null;
        if (form.Published.Definition.Settings.Availability is { Access: not MemberAccess.Anyone } forMembers)
        {
            if (!await ChallengeRequests.AdmitsAsync(HttpContext, forMembers))
            {
                FormBuilderPublicProblems.CloseConnection(HttpContext);
                return FormBuilderPublicProblems.MemberRequired();
            }

            member = await HttpContext.RequestServices.GetRequiredService<FormMembers>().KeyAsync();
        }

        return await uploads.UploadAsync(HttpContext, form.Published.Form.Key, field, member, cancellationToken);
    }

    /// <summary>
    /// A signed challenge for a published form (ADR-0012, ADR-0138), which a page fetches when its visitor first touches the
    /// form, so that cached pages stay cacheable, and with it what cached markup cannot carry: the places left on the
    /// form's own options and what its rules ask of a signed-in member. Every submission, page check and upload posts it
    /// back, and it makes one entry. It counts for 24 hours; an earlier one, even expired, carries its issue time over.
    /// </summary>
    /// <param name="key">The form's key.</param>
    /// <param name="cancellationToken">The request's cancellation, with its time limit.</param>
    [HttpPost("{key}/challenge")]
    [FormBuilderRateLimit(FormBuilderRateLimitPolicies.Challenge)]
    [FormBuilderBodyLimit(ChallengeRequests.MaxBodyBytes)]
    [FormBuilderTimeLimit(10)]
    [FormBuilderJsonBody(typeof(ChallengeRequestModel))]
    [ProducesResponseType<ChallengeResponseModel>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, FormBuilderProblemResult.MediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, FormBuilderProblemResult.MediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, FormBuilderProblemResult.MediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType, FormBuilderProblemResult.MediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, FormBuilderProblemResult.MediaType)]
    public async Task<IActionResult> Challenge(string key, CancellationToken cancellationToken)
    {
        // A form the site does not serve answers before its body is read.
        if (!Guid.TryParseExact(key, "D", out var formKey) || await forms.GetAsync(formKey, cancellationToken) is not { } form)
        {
            FormBuilderPublicProblems.CloseConnection(HttpContext);
            return FormBuilderPublicProblems.FormNotFound();
        }

        return await challenges.IssueAsync(HttpContext, form.Published, cancellationToken);
    }

    /// <summary>
    /// Submits a visitor's answers to a published form (ADR-0088), as JSON: by field alias, as the form endpoint names
    /// them, with the revision the page showed, each ticked consent's wording hash and, for a field that stores files,
    /// the tokens its uploads answered with. The answer is the entry's receipt and the outcome to show; a retry with the
    /// same submission id and the same answers gets the same again, even once the form has closed.
    /// </summary>
    /// <param name="key">The form's key.</param>
    /// <param name="cancellationToken">The request's cancellation, with its time limit.</param>
    /// <remarks>
    /// <c>422 validation_failed</c> lists each error with its code (the field's kind of error, <c>check</c>, <c>rule</c>
    /// or <c>empty</c>) and its path (the alias, <c>grid.rowKey</c>, <c>group[row].child</c>, or empty for the form).
    /// <c>409 form_changed</c> asks the page to load the form again; <c>409 duplicate_submission</c> says the id was used
    /// for other answers, or by another challenge. <c>401 challenge_required</c> asks the page for a new challenge: one
    /// missing, forged, expired, another form's, or used for another entry.
    /// </remarks>
    [HttpPost("{key}/entries")]
    [FormBuilderRateLimit(FormBuilderRateLimitPolicies.Submit)]
    [FormBuilderBodyLimit(EntryRequests.MaxBodyBytes)]
    [FormBuilderTimeLimit(30)]
    [FormBuilderJsonBody(typeof(EntryRequestModel))]
    [ProducesResponseType<EntryResponseModel>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, FormBuilderProblemResult.MediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, FormBuilderProblemResult.MediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, FormBuilderProblemResult.MediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, FormBuilderProblemResult.MediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, FormBuilderProblemResult.MediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge, FormBuilderProblemResult.MediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType, FormBuilderProblemResult.MediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, FormBuilderProblemResult.MediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, FormBuilderProblemResult.MediaType)]
    public async Task<IActionResult> Entries(string key, CancellationToken cancellationToken)
    {
        // A form the site does not serve answers before its body is read.
        if (!Guid.TryParseExact(key, "D", out var formKey) || await forms.GetAsync(formKey, cancellationToken) is null)
        {
            FormBuilderPublicProblems.CloseConnection(HttpContext);
            return FormBuilderPublicProblems.FormNotFound();
        }

        return await entries.SubmitAsync(HttpContext, formKey, cancellationToken);
    }

    /// <summary>
    /// Checks one page's answers on the server before the visitor goes on (ADR-0089), as the submission will check them:
    /// a submission's body with the page's key, holding the answers the visitor gave so far, which the rules and checks
    /// read as the submission's. <c>204</c> when the page's answers pass; nothing is stored either way.
    /// </summary>
    /// <param name="key">The form's key.</param>
    /// <param name="cancellationToken">The request's cancellation, with its time limit.</param>
    /// <remarks>
    /// <c>422 validation_failed</c> lists the errors of the page's fields only, as the entries endpoint lists them. Every
    /// check runs but <c>uniquePerForm</c>, which only a submission runs, so the endpoint cannot tell whether an answer is
    /// taken. <c>403 form_closed</c> and <c>403 entry_limit_reached</c> come as early as the page that meets them.
    /// </remarks>
    [HttpPost("{key}/validate")]
    [FormBuilderRateLimit(FormBuilderRateLimitPolicies.Submit)]
    [FormBuilderBodyLimit(EntryRequests.MaxBodyBytes)]
    [FormBuilderTimeLimit(30)]
    [FormBuilderJsonBody(typeof(PageRequestModel))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, FormBuilderProblemResult.MediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, FormBuilderProblemResult.MediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, FormBuilderProblemResult.MediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, FormBuilderProblemResult.MediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, FormBuilderProblemResult.MediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge, FormBuilderProblemResult.MediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType, FormBuilderProblemResult.MediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity, FormBuilderProblemResult.MediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, FormBuilderProblemResult.MediaType)]
    public async Task<IActionResult> Validate(string key, CancellationToken cancellationToken)
    {
        // A form the site does not serve answers before its body is read.
        if (!Guid.TryParseExact(key, "D", out var formKey) || await forms.GetAsync(formKey, cancellationToken) is null)
        {
            FormBuilderPublicProblems.CloseConnection(HttpContext);
            return FormBuilderPublicProblems.FormNotFound();
        }

        return await entries.ValidateAsync(HttpContext, formKey, cancellationToken);
    }

    /// <summary>
    /// The receipt and the outcome of the entry a submission id names on this form (ADR-0012, ADR-0089), for a page whose
    /// answer to a submission was lost: what a retry of the same answers gets. Every miss answers the same
    /// <c>404 not_found</c>, so submission ids cannot be probed: the entry must have been made with the posted challenge,
    /// whose signature counts here for 24 hours past its expiry, or by the request's server key.
    /// </summary>
    /// <param name="key">The form's key.</param>
    /// <param name="cancellationToken">The request's cancellation, with its time limit.</param>
    [HttpPost("{key}/receipt")]
    [FormBuilderRateLimit(FormBuilderRateLimitPolicies.Submit)]
    [FormBuilderBodyLimit(EntryRequests.MaxReceiptBodyBytes)]
    [FormBuilderTimeLimit(30)]
    [FormBuilderJsonBody(typeof(ReceiptRequestModel))]
    [ProducesResponseType<EntryResponseModel>(StatusCodes.Status200OK, "application/json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest, FormBuilderProblemResult.MediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, FormBuilderProblemResult.MediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound, FormBuilderProblemResult.MediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge, FormBuilderProblemResult.MediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status415UnsupportedMediaType, FormBuilderProblemResult.MediaType)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, FormBuilderProblemResult.MediaType)]
    public async Task<IActionResult> Receipt(string key, CancellationToken cancellationToken)
    {
        // A form the site does not serve answers before its body is read.
        if (!Guid.TryParseExact(key, "D", out var formKey) || await forms.GetAsync(formKey, cancellationToken) is null)
        {
            FormBuilderPublicProblems.CloseConnection(HttpContext);
            return FormBuilderPublicProblems.FormNotFound();
        }

        return await entries.ReceiptAsync(HttpContext, formKey, cancellationToken);
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

    /// <summary>The longest page path the render endpoint takes.</summary>
    internal const int MaxPagePathLength = 2_048;

    // The characters of a URL path besides letters, digits and escapes (RFC 3986: unreserved, sub-delims, ':', '@', '/').
    private const string PathCharacters = "-._~!$&'()*+,;=:@/";

    /// <summary>
    /// Whether a page path is local, as <c>location.pathname</c> gives it: one leading slash, then only a URL path's
    /// characters (no query, no fragment, no backslash, no second slash at the start), each <c>%</c> before two hex
    /// digits, at most <see cref="MaxPagePathLength"/> characters.
    /// </summary>
    /// <param name="page">The path.</param>
    internal static bool IsPagePath([NotNullWhen(true)] string? page)
    {
        if (page is not { Length: > 0 and <= MaxPagePathLength } || page[0] != '/' || (page.Length > 1 && page[1] == '/'))
        {
            return false;
        }

        for (var at = 1; at < page.Length; at++)
        {
            var character = page[at];
            if (character == '%')
            {
                // Its two hex digits are a path's characters too.
                if (at + 2 >= page.Length || !char.IsAsciiHexDigit(page[at + 1]) || !char.IsAsciiHexDigit(page[at + 2]))
                {
                    return false;
                }
            }
            else if (!char.IsAsciiLetterOrDigit(character) && !PathCharacters.Contains(character, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    // Whether the client holds this answer already: its tag, by the weak comparison If-None-Match uses, or any.
    private static bool Matches(IList<EntityTagHeaderValue> held, string etag)
    {
        var current = new EntityTagHeaderValue(etag);
        return held.Any(tag => tag.Equals(EntityTagHeaderValue.Any) || tag.Compare(current, useStrongComparison: false));
    }
}

/// <summary>
/// An action's result rendered with a culture as the current culture and UI culture, as Umbraco sets them on a page in
/// that culture, so a form's markup from the render endpoint is the page's (ADR-0104), a site's own views included. The
/// culture is the async method's own: the request's comes back when it ends. The markup is written to memory first and
/// then to the response asynchronously: a view component's result writes a rich text (the model data among them)
/// synchronously, which overflows the response's writer on a large form, and Kestrel refuses a synchronous write.
/// </summary>
/// <param name="result">The result.</param>
/// <param name="culture">The culture.</param>
internal sealed class CultureResult(IActionResult result, CultureInfo culture) : IActionResult
{
    /// <inheritdoc />
    public async Task ExecuteResultAsync(ActionContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        var response = context.HttpContext.Response;
        var body = response.Body;
        await using var buffer = new MemoryStream();
        response.Body = buffer;
        try
        {
            await result.ExecuteResultAsync(context);
        }
        finally
        {
            response.Body = body;
        }

        response.ContentLength = buffer.Length;
        buffer.Position = 0;
        await buffer.CopyToAsync(body, context.HttpContext.RequestAborted);
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
    /// What a browser decides each field by, as the server does (ADR-0109), by the field's key, a repeatable group's child
    /// fields too.
    /// </summary>
    public required IReadOnlyDictionary<Guid, FormFieldLogicResponseModel> Fields { get; init; }

    /// <summary>
    /// The culture's formats, which messages and merge tags print numbers and dates with (ADR-0064): <c>dates</c> (the
    /// short, medium, long, time and date-and-time patterns), the months' and days' names, <c>am</c>, <c>pm</c> and
    /// <c>minus</c>, <c>number</c> and <c>currency</c>.
    /// </summary>
    public required JsonElement Formats { get; init; }

    /// <summary>The culture's words for yes and no, which answers and messages show (ADR-0066).</summary>
    public required FormWordsResponseModel Words { get; init; }

    /// <summary>
    /// What the browser renders the visitor texts with (ADR-0085); null when every one shows as it is written:
    /// <c>texts</c>, each that renders as its owner's key, the property it is and its context (<c>html</c> or
    /// <c>text</c>); <c>values</c>, each path's value but answer recall's by context; and <c>recall</c>, the form answer
    /// recall reads, cut down to what the texts need. They render with the answer's <c>formats</c> and <c>words</c>.
    /// </summary>
    public JsonElement? MergeTags { get; init; }

    /// <summary>Each consent field's wording hash by its key, which a submission posts with the ticked consent (ADR-0009).</summary>
    public required IReadOnlyDictionary<Guid, string> Consents { get; init; }
}

/// <summary>
/// The headless element's form (ADR-0128): the version of the answer's shape, the view model of the form's first
/// render, and what the page loads.
/// </summary>
internal sealed record FormElementResponseModel
{
    /// <summary>The version of the answer's shape; the element of the same install reads version 1.</summary>
    public required int Contract { get; init; }

    /// <summary>
    /// The form's view model, as the built-in views get it, in camel case: each page, group and field with its ids,
    /// classes, texts, marks and state, a field's control as an object named by its <c>kind</c>, the definition
    /// endpoint's answer as <c>modelData</c>'s text; without the form's action, encoding and post values and a button's
    /// <c>formaction</c>, which only a post without JavaScript uses.
    /// </summary>
    public required JsonElement Form { get; init; }

    /// <summary>The stylesheets (the base rules, then the theme's) and the runtime's module, below the site's path base.</summary>
    public required FormElementAssetsResponseModel Assets { get; init; }
}

/// <summary>What the headless element's form loads (ADR-0128), below the site's path base, each under its fingerprint.</summary>
internal sealed record FormElementAssetsResponseModel
{
    /// <summary>The stylesheets: the base rules, then the theme's when it has one.</summary>
    public required IReadOnlyList<string> Styles { get; init; }

    /// <summary>The website runtime's module.</summary>
    public required string Script { get; init; }
}

/// <summary>What a browser decides a field by (ADR-0109): its kinds, whether it can be required and whose type it is.</summary>
internal sealed record FormFieldLogicResponseModel
{
    /// <summary>
    /// What rules compare the field's answers as (<c>text</c>, <c>number</c>, <c>date</c>, <c>time</c>, <c>dateTime</c>,
    /// <c>choice</c>, <c>choices</c>, <c>checkbox</c> or <c>files</c>); absent for a field whose answers no condition reads.
    /// </summary>
    public string? Kind { get; init; }

    /// <summary>What rules compare a grid row's answers as; only for a field with rows.</summary>
    public string? RowKind { get; init; }

    /// <summary>Whether a rule may require the field.</summary>
    public required bool CanBeRequired { get; init; }

    /// <summary>
    /// Whether the field's type is the built-in one of its alias, which a browser reads and checks as the server does; a
    /// site's own type is read and checked on the server only.
    /// </summary>
    public required bool BuiltIn { get; init; }
}

/// <summary>A culture's words for yes and no.</summary>
internal sealed record FormWordsResponseModel
{
    /// <summary>Yes.</summary>
    public required string Yes { get; init; }

    /// <summary>No.</summary>
    public required string No { get; init; }
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
