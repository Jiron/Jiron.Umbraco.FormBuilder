using Asp.Versioning;
using Jiron.Umbraco.FormBuilder.Core;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Jiron.Umbraco.FormBuilder.Web.Picker;
using Jiron.Umbraco.FormBuilder.Web.Setup;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Web.Common.Routing;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Setup;

/// <summary>
/// Setup (section 4.7): "Create a form block type for me" (ROADMAP 6.7b), which creates Umbraco types and so needs
/// <c>manageSettings</c> and Umbraco's Settings section.
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/setup")]
[ApiExplorerSettings(GroupName = "Setup")]
internal sealed class SetupController(FormBlockTypes blocks, SetupChecklist checklist, FormBuilderHealth health, IFormBuilderAccessService access, IUserService users) : FormBuilderManagementApiController
{
    /// <summary>
    /// The setup checklist (section 4.13): each step with whether the site has done it and whether the user may, whether setup was
    /// ever complete, and the newest form the user may preview. It says nothing but that, so any user of the section may ask.
    /// </summary>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("checklist")]
    [MapToApiVersion("1.0")]
    [FormBuilderSection]
    [ProducesResponseType(typeof(ChecklistResponseModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> Checklist(CancellationToken cancellationToken = default)
    {
        var user = await users.GetAsync(RequestAccess.User.Key);
        return Ok(ChecklistResponseModel.Of(await checklist.GetAsync(RequestAccess.User, user?.AllowedSections.Contains(Constants.Applications.Settings) == true, cancellationToken)));
    }

    /// <summary>What is wrong with the site, in the user's language; nothing when all checks pass. Any user of the section may ask.</summary>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("health")]
    [MapToApiVersion("1.0")]
    [FormBuilderSection]
    [ProducesResponseType(typeof(HealthResponseModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> Health(CancellationToken cancellationToken = default)
    {
        var user = await users.GetAsync(RequestAccess.User.Key);
        var namesSites = (await access.GetGlobalAsync(RequestAccess.User, cancellationToken)).HasFlag(FormBuilderPermissions.ManageSettings);
        return Ok(HealthResponseModel.Of(await health.FindAsync(user?.Language, namesSites, cancellationToken)));
    }

    /// <summary>
    /// Creates the element type <c>Form</c> and the form picker's data type where they are missing, and never changes
    /// one that is there; answers both with whether this call created each.
    /// </summary>
    [HttpPost("form-block")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageSettings, UmbracoSection = Constants.Applications.Settings)]
    [ProducesResponseType(typeof(FormBlockTypesResponseModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateFormBlock() => Ok(FormBlockTypesResponseModel.Of(await blocks.CreateAsync(RequestAccess.User.Key)));
}

/// <summary>One step of the setup checklist.</summary>
public sealed class ChecklistItemResponseModel
{
    /// <summary>The step: <c>email</c>, <c>form</c>, <c>testEntry</c>, <c>formBlock</c> or <c>access</c>.</summary>
    public required string Step { get; init; }

    /// <summary>Whether the site has done it, whoever did.</summary>
    public required bool Done { get; init; }

    /// <summary>Whether the user may do it; the Overview otherwise says who can.</summary>
    public required bool CanDo { get; init; }
}

/// <summary>The setup checklist as a user sees it.</summary>
public sealed class ChecklistResponseModel
{
    /// <summary>The steps, in their order.</summary>
    public required IReadOnlyList<ChecklistItemResponseModel> Items { get; init; }

    /// <summary>Whether all were done once; the Overview then shows the checklist only on request.</summary>
    public required bool Complete { get; init; }

    /// <summary>The newest form the user may preview, which the third step opens; null for none.</summary>
    public Guid? PreviewForm { get; init; }

    internal static ChecklistResponseModel Of(Checklist checklist) => new()
    {
        Items = checklist.Items.Select(item => new ChecklistItemResponseModel { Step = EnumNames<ChecklistStep>.ByValue[item.Step], Done = item.Done, CanDo = item.CanDo }).ToList(),
        Complete = checklist.Complete,
        PreviewForm = checklist.PreviewForm,
    };
}

/// <summary>One thing that is wrong with the site.</summary>
public sealed class HealthFindingResponseModel
{
    /// <summary>What it is, which never changes.</summary>
    public required string Code { get; init; }

    /// <summary>What is wrong and what to do, in the user's language.</summary>
    public required string Message { get; init; }

    /// <summary>
    /// The screen that fixes it, when one does: <c>settingsEmail</c>, <c>settingsGeneral</c>, <c>deliveries</c>,
    /// <c>privacyDataCollection</c>, <c>privacyRetention</c> or <c>settingsProtection</c>.
    /// </summary>
    public string? Link { get; init; }

    /// <summary>Whether the checklist's first step says the same while it is open.</summary>
    public required bool CoversEmail { get; init; }
}

/// <summary>What is wrong with the site; nothing when all checks pass.</summary>
public sealed class HealthResponseModel
{
    /// <summary>The findings, in the order of the checks.</summary>
    public required IReadOnlyList<HealthFindingResponseModel> Findings { get; init; }

    internal static HealthResponseModel Of(IReadOnlyList<HealthFinding> findings) => new()
    {
        Findings = findings.Select(finding => new HealthFindingResponseModel
        {
            Code = finding.Code,
            Message = finding.Message,
            Link = finding.Link is { } link ? EnumNames<HealthLink>.ByValue[link] : null,
            CoversEmail = finding.CoversEmail,
        }).ToList(),
    };
}

/// <summary>One of Umbraco's types the form block needs.</summary>
public sealed class FormBlockPartResponseModel
{
    /// <summary>The type's key.</summary>
    public required Guid Key { get; init; }

    /// <summary>The element type's alias, or the data type's name.</summary>
    public required string Name { get; init; }

    /// <summary>Whether this call created it; false for one that was there, which it left as it is.</summary>
    public required bool Created { get; init; }
}

/// <summary>The form block's types: the element type <c>Form</c> and the form picker's data type its property uses.</summary>
public sealed class FormBlockTypesResponseModel
{
    /// <summary>The element type, which an editor allows in a block list or grid.</summary>
    public required FormBlockPartResponseModel ElementType { get; init; }

    /// <summary>The form picker's data type.</summary>
    public required FormBlockPartResponseModel DataType { get; init; }

    internal static FormBlockTypesResponseModel Of(FormBlockTypesResult result) => new()
    {
        ElementType = Part(result.ElementType),
        DataType = Part(result.DataType),
    };

    private static FormBlockPartResponseModel Part(FormBlockPart part) => new() { Key = part.Key, Name = part.Name, Created = part.Created };
}
