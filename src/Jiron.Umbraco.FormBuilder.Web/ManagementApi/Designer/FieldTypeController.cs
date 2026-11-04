using Asp.Versioning;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Web.Common.Routing;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Designer;

/// <summary>
/// The site's field types for the designer (ADR-0057): the palette's groups, the inspector's sections, and every type
/// with its settings schema, the host's locks of its settings (ADR-0060) and whether the site has what it needs, and the
/// changes of type a field of one type may make (ADR-0059). Any user of the Forms section may ask: the registry and the
/// locks describe the site's code and configuration, the same for everyone.
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/field-type")]
[ApiExplorerSettings(GroupName = "Field type")]
internal sealed class FieldTypeController(FieldTypeRegistry registry, FieldSettingLocks locks, FieldTypeAvailabilityService availability) : FormBuilderManagementApiController
{
    /// <summary>Every field type of the site, in the palette's order, with the groups the designer shows them in.</summary>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet]
    [MapToApiVersion("1.0")]
    [FormBuilderSection]
    [ProducesResponseType(typeof(FieldTypesResponseModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken = default) =>
        Ok(FieldTypesResponseModel.Of(registry.Types, await availability.CheckAsync(cancellationToken), locks));

    /// <summary>
    /// The changes of type a field of the type may make, in the palette's order, each with what it keeps, narrows, loses
    /// and needs, the host's locks of its target applied (ADR-0060); <c>404 field_type_not_found</c> for a type the site
    /// lacks.
    /// </summary>
    /// <param name="alias">The type's alias.</param>
    [HttpGet("{alias}/changes")]
    [MapToApiVersion("1.0")]
    [FormBuilderSection]
    [ProducesResponseType(typeof(FieldTypeChangesResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public IActionResult Changes(string alias) =>
        registry.FindType(alias) is { } type
            ? Ok(FieldTypeChangesResponseModel.Of(type, registry.Types, locks))
            : FormBuilderProblems.FieldTypeNotFound();
}
