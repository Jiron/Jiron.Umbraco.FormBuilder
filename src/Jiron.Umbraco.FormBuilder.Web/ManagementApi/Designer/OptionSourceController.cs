using System.Text.Json;
using Asp.Versioning;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Web.Common.Routing;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Designer;

/// <summary>
/// Where a choice field's options may come from (ADR-0073, ADR-0172): the kinds a field may configure, each with its
/// configuration's settings schema as the field types endpoint serves a type's, and the site's code sources. Any user of
/// the Forms section may ask: the registries describe the site's code, the same for everyone.
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/option-source")]
[ApiExplorerSettings(GroupName = "Option source")]
internal sealed class OptionSourceController(OptionKindRegistry kinds, OptionSourceRegistry sources) : FormBuilderManagementApiController
{
    /// <summary>The kinds a field may configure, in the order of their enum, and the code sources in the registry's order.</summary>
    [HttpGet]
    [MapToApiVersion("1.0")]
    [FormBuilderSection]
    [ProducesResponseType(typeof(OptionSourcesResponseModel), StatusCodes.Status200OK)]
    public IActionResult List() => Ok(OptionSourcesResponseModel.Of(kinds, sources));
}

/// <summary>
/// The library's option lists as the inspector's source picker offers them (ADR-0172): names, kinds and how many options
/// a list that holds them has. Any user of the Forms section may ask, as anyone who designs a form may pick a list.
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/option-list")]
[ApiExplorerSettings(GroupName = "Option source")]
internal sealed class OptionListController(OptionListService lists) : FormBuilderManagementApiController
{
    /// <summary>Every list, by name.</summary>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet]
    [MapToApiVersion("1.0")]
    [FormBuilderSection]
    [ProducesResponseType(typeof(OptionListsResponseModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken = default) =>
        Ok(new OptionListsResponseModel { Items = [.. (await lists.ListAsync(cancellationToken)).Select(OptionListItemResponseModel.Of)] });
}

/// <summary>
/// A field's options as a draft holds them, resolved for the designer's canvas (ADR-0172). It needs <c>design</c> on the
/// form: a configured JSON address makes the site call it, which only someone who may design the form may ask for.
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/form/{key:guid}/options")]
[ApiExplorerSettings(GroupName = "Option source")]
internal sealed class DraftOptionsController(DraftOptions options) : FormBuilderManagementApiController
{
    /// <summary>The options of the field as the request holds them, in its culture, with every option of a cascading field.</summary>
    /// <param name="key">The form.</param>
    /// <param name="request">The field's key, type and options, and the culture.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.Design, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(ResolvedOptionsResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Resolve(Guid key, DraftOptionsRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // The form's key is the permission's, which its filter read from the route before the action ran.
        _ = key;
        if (await options.ResolveAsync(request.Field, request.Type, request.Options, request.Culture, cancellationToken) is not { } resolved)
        {
            ModelState.AddModelError("options", "The options do not read as a field's.");
            return FormBuilderProblems.InvalidRequest(ModelState);
        }

        return Ok(ResolvedOptionsResponseModel.Of(resolved));
    }
}

/// <summary>Where a field's options may come from (ADR-0172).</summary>
internal sealed record OptionSourcesResponseModel
{
    /// <summary>The kinds a field may configure as its source, in the order of their enum.</summary>
    public required IReadOnlyList<OptionKindResponseModel> Kinds { get; init; }

    /// <summary>The site's code sources, in the registry's order.</summary>
    public required IReadOnlyList<OptionSourceResponseModel> Sources { get; init; }

    /// <summary>The model of the site's kinds and sources.</summary>
    public static OptionSourcesResponseModel Of(OptionKindRegistry kinds, OptionSourceRegistry sources)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        ArgumentNullException.ThrowIfNull(sources);
        return new OptionSourcesResponseModel
        {
            Kinds = [.. kinds.All.Where(kind => kind.OnField).Select(kind => new OptionKindResponseModel { Kind = kind.Name, Settings = FieldSettingResponseModel.Of(kind.Schema.Settings, []) })],
            Sources = [.. sources.Ordered.Select(source => new OptionSourceResponseModel { Name = source.Name, Label = source.Label })],
        };
    }
}

/// <summary>A kind a field may configure as its source.</summary>
internal sealed record OptionKindResponseModel
{
    /// <summary>The kind as a field's <c>options.source</c> names it, such as <c>countries</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>The settings of its configuration, at <c>options.config</c>, in the order they are shown.</summary>
    public required IReadOnlyList<FieldSettingResponseModel> Settings { get; init; }
}

/// <summary>A code source a field of the kind <c>code</c> may name.</summary>
internal sealed record OptionSourceResponseModel
{
    /// <summary>The name a configuration's <c>source</c> holds.</summary>
    public required string Name { get; init; }

    /// <summary>What the backoffice calls it: plain text, or a <c>#</c> key of its localisation.</summary>
    public required string Label { get; init; }
}

/// <summary>The library's option lists.</summary>
internal sealed record OptionListsResponseModel
{
    /// <summary>The lists, by name.</summary>
    public required IReadOnlyList<OptionListItemResponseModel> Items { get; init; }
}

/// <summary>A library option list.</summary>
internal sealed record OptionListItemResponseModel
{
    /// <summary>The key a field's <c>options.list</c> names it by.</summary>
    public required Guid Key { get; init; }

    /// <summary>Its alias.</summary>
    public required string Alias { get; init; }

    /// <summary>Its name.</summary>
    public required string Name { get; init; }

    /// <summary>Its kind, such as <c>manual</c>; null for a kind this version lacks, which offers no options.</summary>
    public string? Kind { get; init; }

    /// <summary>How many options a manual or uploaded list holds; null for a list that resolves its options.</summary>
    public int? Count { get; init; }

    /// <summary>The model of a list's summary.</summary>
    public static OptionListItemResponseModel Of(OptionListSummary list)
    {
        ArgumentNullException.ThrowIfNull(list);
        return new OptionListItemResponseModel { Key = list.Key, Alias = list.Alias, Name = list.Name, Kind = list.Kind?.Name, Count = list.Count };
    }
}

/// <summary>A field's options as a draft holds them, to resolve.</summary>
internal sealed record DraftOptionsRequestModel
{
    /// <summary>The field's key in the draft.</summary>
    public required Guid Field { get; init; }

    /// <summary>The field's type.</summary>
    public required string Type { get; init; }

    /// <summary>The field's <c>options</c> object as the draft holds it.</summary>
    public required JsonElement Options { get; init; }

    /// <summary>The culture of the labels, such as <c>de-DE</c>; empty for the default language's texts.</summary>
    public required string Culture { get; init; }
}

/// <summary>A field's resolved options.</summary>
internal sealed record ResolvedOptionsResponseModel
{
    /// <summary>The options, in the order visitors see them.</summary>
    public required IReadOnlyList<ResolvedOptionResponseModel> Options { get; init; }

    /// <summary>Whether the source gave its options; false for one that failed, took too long or isn't configured yet.</summary>
    public required bool IsAvailable { get; init; }

    /// <summary>The model of resolved options.</summary>
    public static ResolvedOptionsResponseModel Of(FormBuilderResolvedOptions resolved)
    {
        ArgumentNullException.ThrowIfNull(resolved);
        return new ResolvedOptionsResponseModel
        {
            Options = [.. resolved.Options.Select(option => new ResolvedOptionResponseModel
            {
                Value = option.Value,
                Label = option.Label,
                Description = option.Description,
                Image = option.Image,
                Places = option.Places,
                ParentValues = option.ParentValues,
            })],
            IsAvailable = resolved.IsAvailable,
        };
    }
}

/// <summary>One resolved option.</summary>
internal sealed record ResolvedOptionResponseModel
{
    /// <summary>What entries store.</summary>
    public required string Value { get; init; }

    /// <summary>The label in the culture asked for, or null for the value.</summary>
    public string? Label { get; init; }

    /// <summary>A text below the label, or null.</summary>
    public string? Description { get; init; }

    /// <summary>The key of a media item shown with it, or null.</summary>
    public Guid? Image { get; init; }

    /// <summary>Its places, or null for no limit.</summary>
    public int? Places { get; init; }

    /// <summary>The parent's answers it belongs to; empty for every answer.</summary>
    public required IReadOnlyList<string> ParentValues { get; init; }
}
