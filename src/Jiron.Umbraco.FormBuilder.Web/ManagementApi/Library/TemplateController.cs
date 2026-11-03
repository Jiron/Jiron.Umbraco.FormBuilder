using System.ComponentModel.DataAnnotations;
using Asp.Versioning;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Common.ViewModels.Pagination;
using Umbraco.Cms.Web.Common.Routing;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Library;

/// <summary>
/// The template gallery (ADR-0046, ADR-0166): the built-in templates, then the site's saved form templates, each with
/// what it holds and its definition for the preview. Any user of the Forms section may ask; creating a form from one
/// needs <c>design</c> where it lands.
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/template")]
[ApiExplorerSettings(GroupName = "Template")]
internal sealed class TemplateController(FormTemplateGallery gallery) : FormBuilderManagementApiController
{
    /// <summary>A page of the gallery.</summary>
    /// <param name="skip">How many templates to pass over.</param>
    /// <param name="take">How many templates to answer with, 1 to 500.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet]
    [MapToApiVersion("1.0")]
    [FormBuilderSection]
    [ProducesResponseType(typeof(PagedViewModel<TemplateResponseModel>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List([FromQuery, Range(0, int.MaxValue)] int skip = 0, [FromQuery, Range(1, PageRequest.MaxTake)] int take = 100, CancellationToken cancellationToken = default)
    {
        var page = await gallery.ListAsync(new PageRequest(skip, take), cancellationToken);
        return Ok(new PagedViewModel<TemplateResponseModel> { Total = page.Total, Items = [.. page.Items.Select(TemplateResponseModel.Of)] });
    }
}
