using System.ComponentModel.DataAnnotations;
using Asp.Versioning;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Email;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Setup;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Web.Common.Routing;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Library;

/// <summary>
/// The library's email layouts (ROADMAP 12.1, ADR-0215): the list with how many email actions name each, what the editor
/// needs, a layout to edit, the forms that use it, its changes, a duplicate, the preview of a sample entry in a layout as
/// the editor holds it, and a test mail in a saved one to the user's own address. Every endpoint needs
/// <c>manageLibrary</c>, which is granted for the whole section.
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/email-layout")]
[ApiExplorerSettings(GroupName = "Email layout")]
internal sealed class EmailLayoutLibraryController(EmailLayoutLibrary library, ILanguageService languages, IUserService users, TestLimits limits) : FormBuilderManagementApiController
{
    /// <summary>A page of the layouts by name, each with how many email actions name it and whether it is the default.</summary>
    /// <param name="skip">How many layouts to skip.</param>
    /// <param name="take">How many layouts to return, at most 500.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("library")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageLibrary)]
    [ProducesResponseType(typeof(EmailLayoutRowsResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Rows([FromQuery, Range(0, int.MaxValue)] int skip = 0, [FromQuery, Range(1, PageRequest.MaxTake)] int take = 100, CancellationToken cancellationToken = default)
    {
        var page = await library.RowsAsync(new PageRequest(skip, take), cancellationToken);
        return Ok(new EmailLayoutRowsResponseModel { Total = page.Total, Items = [.. page.Items.Select(EmailLayoutRowResponseModel.Of)] });
    }

    /// <summary>What the editor needs besides a layout: the site's languages, mail's own colours and the site's default layout.</summary>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("editor")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageLibrary)]
    [ProducesResponseType(typeof(EmailLayoutEditorResponseModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> Editor(CancellationToken cancellationToken = default)
    {
        var all = await languages.GetAllAsync();
        return Ok(new EmailLayoutEditorResponseModel
        {
            Languages = [.. all.OrderByDescending(one => one.IsDefault).Select(one => new OptionListLanguageResponseModel { Culture = one.IsoCode, Name = one.CultureName, IsDefault = one.IsDefault })],
            Light = EmailColoursResponseModel.Of(EmailRenderer.LightColours)!,
            Dark = EmailColoursResponseModel.Of(EmailRenderer.DarkColours)!,
            DefaultLayout = await library.DefaultKeyAsync(cancellationToken),
        });
    }

    /// <summary>A layout as its editor shows it.</summary>
    /// <param name="key">The layout.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("{key:guid}")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageLibrary)]
    [ProducesResponseType(typeof(EmailLayoutResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid key, CancellationToken cancellationToken = default) =>
        await library.GetAsync(key, cancellationToken) is { } layout ? Ok(EmailLayoutResponseModel.Of(layout)) : FormBuilderProblems.EmailLayoutNotFound();

    /// <summary>
    /// The forms whose email actions name the layout in their draft or live version, forms in the recycle bin included:
    /// those the user may open by name, with how many actions, the others only counted.
    /// </summary>
    /// <param name="key">The layout.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("{key:guid}/used-by")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageLibrary)]
    [ProducesResponseType(typeof(LibraryUsersResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UsedBy(Guid key, CancellationToken cancellationToken = default) =>
        await library.GetAsync(key, cancellationToken) is null
            ? FormBuilderProblems.EmailLayoutNotFound()
            : Ok(LibraryUsersResponseModel.Of(await library.UsersAsync(RequestAccess.User, key, cancellationToken)));

    /// <summary>Creates a layout; answers <c>400</c> with every problem, each at its path.</summary>
    /// <param name="request">The layout.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageLibrary)]
    [ProducesResponseType(typeof(EmailLayoutResponseModel), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(CreateEmailLayoutRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Created(Stored(await library.CreateAsync(request.Key ?? Guid.NewGuid(), request.Layout.ToInput(), RequestAccess.User.Key, cancellationToken)));
    }

    /// <summary>
    /// Saves a layout, based on the version the editor read: <c>409 concurrency_conflict</c> when it changed meanwhile,
    /// and <c>400</c> with every problem.
    /// </summary>
    /// <param name="key">The layout.</param>
    /// <param name="request">The layout, with the version it is based on.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPut("{key:guid}")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageLibrary)]
    [ProducesResponseType(typeof(EmailLayoutResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid key, UpdateEmailLayoutRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var changed = await library.UpdateAsync(key, request.ConcurrencyVersion, request.Layout.ToInput(), RequestAccess.User.Key, cancellationToken);
        return changed is { Layout: null, Problems.Count: 0 } ? FormBuilderProblems.EmailLayoutNotFound() : Ok(EmailLayoutResponseModel.Of(Stored(changed)));
    }

    /// <summary>
    /// Deletes a layout for good, based on the version read: <c>409 in_use</c> while an email action names it,
    /// <c>409 is_default</c> while it is the site's default, and <c>409 concurrency_conflict</c> when it changed meanwhile.
    /// </summary>
    /// <param name="key">The layout.</param>
    /// <param name="concurrencyVersion">The version the delete is based on.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpDelete("{key:guid}")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageLibrary)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid key, int concurrencyVersion, CancellationToken cancellationToken = default) =>
        await library.DeleteAsync(key, concurrencyVersion, RequestAccess.User.Key, cancellationToken) switch
        {
            EmailLayoutDelete.Deleted => NoContent(),
            EmailLayoutDelete.IsDefault => FormBuilderProblems.IsDefault(),
            _ => FormBuilderProblems.EmailLayoutNotFound(),
        };

    /// <summary>A copy of a layout with the name given and the first free alias after its own.</summary>
    /// <param name="key">The layout.</param>
    /// <param name="request">The copy's name.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("{key:guid}/duplicate")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageLibrary)]
    [ProducesResponseType(typeof(EmailLayoutResponseModel), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Duplicate(Guid key, DuplicateEmailLayoutRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await library.DuplicateAsync(key, request.Name, RequestAccess.User.Key, cancellationToken) is { } created
            ? Created(Stored(created))
            : FormBuilderProblems.EmailLayoutNotFound();
    }

    /// <summary>
    /// A sample entry's mail in a layout as the editor holds it, saved or not, in a culture, with its light and dark styles;
    /// a logo on the site is shown under this site's address. The problems a save would refuse instead, each at its path.
    /// </summary>
    /// <param name="request">The design and the culture.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("preview")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageLibrary)]
    [ProducesResponseType(typeof(EmailLayoutPreviewResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Preview(PreviewEmailLayoutRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var site = $"{Request.Scheme}://{Request.Host}{Request.PathBase}";
        var (mail, problems) = await library.PreviewAsync(request.Design.ToDesign(), request.Culture ?? "en", site, cancellationToken);
        return Ok(new EmailLayoutPreviewResponseModel { Html = mail?.Html ?? string.Empty, Problems = [.. problems.Select(LibraryProblemResponseModel.Of)] });
    }

    /// <summary>
    /// Sends a test mail in a saved layout to the user's own address, through the outbox: <c>409 email_not_configured</c>
    /// without a mail server or pickup folder, <c>409 email_no_address</c> for an account without an address.
    /// </summary>
    /// <param name="key">The layout.</param>
    /// <param name="request">The mail's culture.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("{key:guid}/test")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageLibrary)]
    [ProducesResponseType(typeof(TestEmailResponseModel), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Test(Guid key, TestEmailLayoutRequestModel? request = null, CancellationToken cancellationToken = default)
    {
        if (limits.Refusal(RequestAccess.User.Key, Response) is { } limited)
        {
            return limited;
        }

        var user = await users.GetAsync(RequestAccess.User.Key);
        return await library.SendTestAsync(key, user?.Email, request?.Culture ?? user?.Language, RequestAccess.User.Key, cancellationToken) switch
        {
            null => FormBuilderProblems.EmailLayoutNotFound(),
            TestEmailStatus.Queued => Accepted(new TestEmailResponseModel { To = user!.Email }),
            TestEmailStatus.NotConfigured => FormBuilderProblems.EmailNotConfigured(),
            _ => FormBuilderProblems.EmailNoAddress(),
        };
    }

    private ObjectResult Created(StoredEmailLayout layout)
    {
        Response.Headers[Constants.Headers.GeneratedResource] = layout.Key.ToString();
        return StatusCode(StatusCodes.Status201Created, EmailLayoutResponseModel.Of(layout));
    }

    // The stored layout of a change, or the problems that refused it as a 400 with each at its path.
    private static StoredEmailLayout Stored(EmailLayoutResult result) =>
        result.Layout ?? throw new FormBuilderValidationException(result.Problems);
}
