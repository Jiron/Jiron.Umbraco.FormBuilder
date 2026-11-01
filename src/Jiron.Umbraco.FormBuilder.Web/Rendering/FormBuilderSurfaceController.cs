using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Logging;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Cms.Web.Website.Controllers;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

/// <summary>
/// Takes the post of a form on a page without JavaScript (ADR-0004, ADR-0100): the form posts back to its page with
/// Umbraco's route token, and the page renders again with the answers, the errors or a notice, or the visitor is sent on
/// to the outcome. Sites never call it; it is public because Umbraco routes only public surface controllers.
/// </summary>
/// <param name="umbracoContextAccessor">Umbraco's request.</param>
/// <param name="databaseFactory">Umbraco's database factory.</param>
/// <param name="services">Umbraco's services.</param>
/// <param name="appCaches">Umbraco's caches.</param>
/// <param name="profilingLogger">Umbraco's profiling logger.</param>
/// <param name="publishedUrlProvider">Umbraco's addresses of published content.</param>
public sealed class FormBuilderSurfaceController(
    IUmbracoContextAccessor umbracoContextAccessor,
    IUmbracoDatabaseFactory databaseFactory,
    ServiceContext services,
    AppCaches appCaches,
    IProfilingLogger profilingLogger,
    IPublishedUrlProvider publishedUrlProvider)
    : SurfaceController(umbracoContextAccessor, databaseFactory, services, appCaches, profilingLogger, publishedUrlProvider)
{
    /// <summary>The controller's name in Umbraco's route token.</summary>
    public const string ControllerName = "FormBuilderSurface";

    /// <summary>The action's name in Umbraco's route token.</summary>
    public const string ActionName = nameof(Post);

    /// <summary>
    /// Reads the post and answers it: the page again (<c>200</c>, or <c>429</c> with its answers over a rate limit), a
    /// redirect (<c>302</c>) to the outcome or its target, a bare <c>429</c> for a flood, or <c>400</c> for a post that
    /// reached the action without a page (Umbraco's route <c>/umbraco/surface/...</c>) or that names no form of the page.
    /// Every answer is <c>no-store</c>.
    /// </summary>
    [HttpPost]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Post()
    {
        var answer = await HttpContext.RequestServices.GetRequiredService<FormPostHandler>().HandleAsync(HttpContext, HttpContext.RequestAborted);
        return answer.Kind switch
        {
            FormPostAnswerKind.Page => CurrentUmbracoPage(),
            FormPostAnswerKind.Redirect => Redirect(answer.Url!),
            FormPostAnswerKind.Forbidden => StatusCode(StatusCodes.Status403Forbidden),
            FormPostAnswerKind.TooManyRequests => StatusCode(StatusCodes.Status429TooManyRequests),
            _ => BadRequest(),
        };
    }
}
