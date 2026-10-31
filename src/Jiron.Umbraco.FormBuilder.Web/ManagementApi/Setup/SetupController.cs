using Asp.Versioning;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Jiron.Umbraco.FormBuilder.Web.Picker;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core;
using Umbraco.Cms.Web.Common.Routing;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Setup;

/// <summary>
/// Setup (section 4.7): "Create a form block type for me" (ROADMAP 6.7b), which creates Umbraco types and so needs
/// <c>manageSettings</c> and Umbraco's Settings section.
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/setup")]
[ApiExplorerSettings(GroupName = "Setup")]
internal sealed class SetupController(FormBlockTypes blocks) : FormBuilderManagementApiController
{
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
