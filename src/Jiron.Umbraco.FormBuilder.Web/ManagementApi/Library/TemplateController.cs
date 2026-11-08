using System.ComponentModel.DataAnnotations;
using Asp.Versioning;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Api.Common.ViewModels.Pagination;
using Umbraco.Cms.Core;
using Umbraco.Cms.Web.Common.Routing;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Library;

/// <summary>
/// The template gallery (ADR-0046, ADR-0166) and the templates users save (ADR-0217): the built-in templates, then the site's
/// saved form templates, or the site's saved groups, each with what it holds and its definition for the preview. Any user
/// of the Forms section may list them; creating a form from one needs <c>design</c> where it lands. Saving, renaming and
/// deleting a template takes <c>manageLibrary</c>, and saving a form as one also <c>design</c> on the form.
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/template")]
[ApiExplorerSettings(GroupName = "Template")]
internal sealed class TemplateController(FormTemplateGallery gallery, FormTemplateLibrary library, IFormBuilderAccessService access) : FormBuilderManagementApiController
{
    /// <summary>A page of the gallery: its form templates, or with <c>kind=group</c> the site's saved groups.</summary>
    /// <param name="skip">How many templates to pass over.</param>
    /// <param name="take">How many templates to answer with, 1 to 500.</param>
    /// <param name="kind">Form templates (<c>form</c>, the default) or saved groups (<c>group</c>).</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet]
    [MapToApiVersion("1.0")]
    [FormBuilderSection]
    [ProducesResponseType(typeof(PagedViewModel<TemplateResponseModel>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List([FromQuery, Range(0, int.MaxValue)] int skip = 0, [FromQuery, Range(1, PageRequest.MaxTake)] int take = 100, [FromQuery, RegularExpression("^(form|group)$")] string kind = "form", CancellationToken cancellationToken = default)
    {
        var page = await gallery.ListAsync(new PageRequest(skip, take), kind == "group" ? FormTemplateKind.Group : FormTemplateKind.Form, cancellationToken);
        return Ok(new PagedViewModel<TemplateResponseModel> { Total = page.Total, Items = [.. page.Items.Select(TemplateResponseModel.Of)] });
    }

    /// <summary>
    /// Saves a form's stored draft as a form template, with a name and a description: <c>201</c> with the template, <c>400</c>
    /// with the problems of the name or the description, <c>403</c> without <c>manageLibrary</c>.
    /// </summary>
    /// <param name="formKey">The form.</param>
    /// <param name="request">The template's name and description.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("form/{formKey:guid}")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.Design, On = FormBuilderAccessResourceKind.Form, Key = "formKey")]
    [ProducesResponseType(typeof(TemplateResponseModel), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SaveForm(Guid formKey, TemplateNameRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if ((await access.GetGlobalAsync(RequestAccess.User, cancellationToken) & FormBuilderPermissions.ManageLibrary) == FormBuilderPermissions.None)
        {
            return FormBuilderProblems.Missing(FormBuilderPermissions.ManageLibrary);
        }

        var saved = await library.SaveFormAsync(formKey, request.Name, request.Description, RequestAccess.User.Key, cancellationToken);
        return saved is null ? FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Form) : Saved(saved);
    }

    /// <summary>
    /// Saves a group as the canvas shows it as a group template: <c>201</c> with the template and how many of its rules lost
    /// a condition that named an element outside it, or <c>400</c> with the problems, the group's at <c>$.group</c>.
    /// </summary>
    /// <param name="request">The group, its name and description.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("group")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageLibrary)]
    [ProducesResponseType(typeof(GroupTemplateSavedResponseModel), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SaveGroup(SaveGroupTemplateRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var saved = await library.SaveGroupAsync(request.Group, request.Name, request.Description, RequestAccess.User.Key, cancellationToken);
        var template = saved.Template ?? throw new FormBuilderValidationException(saved.Problems);
        return Created(template.Key, new GroupTemplateSavedResponseModel { Template = TemplateResponseModel.Of(template, gallery), RulesChanged = saved.RulesChanged });
    }

    /// <summary>Renames a saved template: <c>200</c> with it, <c>400</c> with the problems, <c>404</c> for none.</summary>
    /// <param name="key">The template.</param>
    /// <param name="request">Its name and description.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPut("{key:guid}")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageLibrary)]
    [ProducesResponseType(typeof(TemplateResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Rename(Guid key, TemplateNameRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var renamed = await library.RenameAsync(key, request.Name, request.Description, RequestAccess.User.Key, cancellationToken);
        return renamed is null
            ? FormBuilderProblems.TemplateNotFound()
            : Ok(TemplateResponseModel.Of(renamed.Template ?? throw new FormBuilderValidationException(renamed.Problems), gallery));
    }

    /// <summary>Deletes a saved template: <c>204</c>, or <c>404</c> for none; a built-in template is none.</summary>
    /// <param name="key">The template.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpDelete("{key:guid}")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageLibrary)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid key, CancellationToken cancellationToken = default) =>
        await library.DeleteAsync(key, RequestAccess.User.Key, cancellationToken) ? NoContent() : FormBuilderProblems.TemplateNotFound();

    private ObjectResult Saved(FormTemplateResult saved)
    {
        var template = saved.Template ?? throw new FormBuilderValidationException(saved.Problems);
        return Created(template.Key, TemplateResponseModel.Of(template, gallery));
    }

    // A 201 with the new template's key in the header Umbraco's clients read.
    private ObjectResult Created(Guid key, object model)
    {
        Response.Headers[Constants.Headers.GeneratedResource] = key.ToString();
        return StatusCode(StatusCodes.Status201Created, model);
    }
}
