using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Asp.Versioning;
using Jiron.Umbraco.FormBuilder.Core;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Web.Common.Routing;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Library;

/// <summary>
/// The library's option lists (ROADMAP 12.1, ADR-0214): the list with how many forms use each, a list to edit, the forms
/// that use it, its changes, a duplicate, the preview of contents not saved yet, a CSV or TXT file read into options, and a
/// URL list's copy with Fetch now. Every endpoint needs <c>manageLibrary</c>, which is granted for the whole section; the
/// inspector's source picker reads the lists through <c>GET option-list</c>, which any user of the section may.
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/option-list")]
[ApiExplorerSettings(GroupName = "Option list")]
internal sealed class OptionListLibraryController(
    OptionListLibrary library,
    IOptionListRepository repository,
    OptionKindRegistry kinds,
    ILanguageService languages) : FormBuilderManagementApiController
{
    /// <summary>A page of the lists by name, each with its kind and how many forms use it.</summary>
    /// <param name="skip">How many lists to skip.</param>
    /// <param name="take">How many lists to return, at most 500.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("library")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageLibrary)]
    [ProducesResponseType(typeof(OptionListRowsResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Rows([FromQuery, Range(0, int.MaxValue)] int skip = 0, [FromQuery, Range(1, PageRequest.MaxTake)] int take = 100, CancellationToken cancellationToken = default)
    {
        var page = await library.RowsAsync(new PageRequest(skip, take), cancellationToken);
        return Ok(new OptionListRowsResponseModel { Total = page.Total, Items = [.. page.Items.Select(OptionListRowResponseModel.Of)] });
    }

    /// <summary>What the editor needs besides a list: the site's languages and what each kind keeps.</summary>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("editor")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageLibrary)]
    [ProducesResponseType(typeof(OptionListEditorResponseModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> Editor(CancellationToken cancellationToken = default)
    {
        _ = cancellationToken;
        var all = await languages.GetAllAsync();
        return Ok(new OptionListEditorResponseModel
        {
            Languages = [.. all.OrderByDescending(one => one.IsDefault).Select(one => new OptionListLanguageResponseModel { Culture = one.IsoCode, Name = one.CultureName, IsDefault = one.IsDefault })],
            Kinds = [.. kinds.All.Select(kind => new OptionListKindResponseModel { Kind = kind.Name, HasItems = kind.HasItems, KeepsOptions = kind.UsesCacheSeconds, MinCacheSeconds = kind.MinCacheSeconds })],
            MaxCacheSeconds = OptionListService.MaxCacheSeconds,
            MaxOptions = OptionListService.MaxOptions,
            MaxFileBytes = OptionFileReader.MaxBytes,
        });
    }

    /// <summary>A list as its editor shows it.</summary>
    /// <param name="key">The list.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("{key:guid}")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageLibrary)]
    [ProducesResponseType(typeof(OptionListResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid key, CancellationToken cancellationToken = default) =>
        await repository.GetAsync(key, cancellationToken) is { } list ? Ok(OptionListResponseModel.Of(list)) : FormBuilderProblems.OptionListNotFound();

    /// <summary>
    /// The forms whose draft or live version uses the list, forms in the recycle bin included: those the user may open
    /// by name, the others only counted.
    /// </summary>
    /// <param name="key">The list.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("{key:guid}/used-by")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageLibrary)]
    [ProducesResponseType(typeof(LibraryUsersResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UsedBy(Guid key, CancellationToken cancellationToken = default)
    {
        if (await repository.GetAsync(key, cancellationToken) is null)
        {
            return FormBuilderProblems.OptionListNotFound();
        }

        return Ok(LibraryUsersResponseModel.Of(await library.UsersAsync(RequestAccess.User, key, cancellationToken)));
    }

    /// <summary>Creates a list; answers <c>400</c> with every problem, each at its path, when its contents or alias refuse it.</summary>
    /// <param name="request">The list.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageLibrary)]
    [ProducesResponseType(typeof(OptionListResponseModel), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create(CreateOptionListRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var created = Stored(await library.CreateAsync(request.Key ?? Guid.NewGuid(), Input(request.List), RequestAccess.User.Key, cancellationToken));
        Response.Headers[Constants.Headers.GeneratedResource] = created.Key.ToString();
        return StatusCode(StatusCodes.Status201Created, OptionListResponseModel.Of(created));
    }

    /// <summary>
    /// Saves a list, based on the version the editor read: <c>409 concurrency_conflict</c> when it changed meanwhile, and
    /// <c>400</c> with every problem when its contents or alias refuse it.
    /// </summary>
    /// <param name="key">The list.</param>
    /// <param name="request">The list, with the version it is based on.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPut("{key:guid}")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageLibrary)]
    [ProducesResponseType(typeof(OptionListResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(Guid key, UpdateOptionListRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var changed = await library.UpdateAsync(key, request.ConcurrencyVersion, Input(request.List), RequestAccess.User.Key, cancellationToken);
        return changed is { List: null, Problems.Count: 0 } ? FormBuilderProblems.OptionListNotFound() : Ok(OptionListResponseModel.Of(Stored(changed)));
    }

    /// <summary>
    /// Deletes a list, based on the version the editor read: <c>409 in_use</c> while a form's draft or live version uses
    /// it, a form in the recycle bin's included, and <c>409 concurrency_conflict</c> when it changed meanwhile.
    /// </summary>
    /// <param name="key">The list.</param>
    /// <param name="concurrencyVersion">The version the editor read.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpDelete("{key:guid}")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageLibrary)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(Guid key, int concurrencyVersion, CancellationToken cancellationToken = default) =>
        await library.DeleteAsync(key, concurrencyVersion, RequestAccess.User.Key, cancellationToken) ? NoContent() : FormBuilderProblems.OptionListNotFound();

    /// <summary>A copy of a list with the name given and the first free alias after its own.</summary>
    /// <param name="key">The list.</param>
    /// <param name="request">The copy's name.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("{key:guid}/duplicate")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageLibrary)]
    [ProducesResponseType(typeof(OptionListResponseModel), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Duplicate(Guid key, DuplicateOptionListRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await library.DuplicateAsync(key, request.Name, RequestAccess.User.Key, cancellationToken) is not { } created)
        {
            return FormBuilderProblems.OptionListNotFound();
        }

        var copy = Stored(created);
        Response.Headers[Constants.Headers.GeneratedResource] = copy.Key.ToString();
        return StatusCode(StatusCodes.Status201Created, OptionListResponseModel.Of(copy));
    }

    /// <summary>
    /// The options that contents not saved yet give, in a language of the site, as the list's fields would offer them; or,
    /// when they do not read, the problems a save would answer with, each at its path. A URL list fetches its address.
    /// </summary>
    /// <param name="request">The contents and the language.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("preview")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageLibrary)]
    [ProducesResponseType(typeof(OptionListPreviewResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Preview(PreviewOptionListRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var kind = KindOf(request.Kind);
        var preview = await library.PreviewAsync(kind, Json(request.Config, "{}"), Json(request.Items, "[]"), request.CacheSeconds, request.Key ?? Guid.Empty, request.Culture, cancellationToken);
        return Ok(new OptionListPreviewResponseModel
        {
            Options = preview.Options is { } options ? [.. options.Options.Select(option => new OptionListOptionResponseModel { Value = option.Value, Label = option.Label, Description = option.Description })] : [],
            IsAvailable = preview.Options?.IsAvailable ?? false,
            Problems = [.. preview.Problems.Select(LibraryProblemResponseModel.Of)],
        });
    }

    /// <summary>
    /// A CSV or TXT file read into a list's items (ADR-0076): one option a line, its value and then its label. A file that
    /// does not read answers <c>400</c> with each problem at <c>$.file</c>, naming its line.
    /// </summary>
    /// <param name="request">The file's name and its bytes.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("read-file")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageLibrary)]
    [ProducesResponseType(typeof(OptionFileResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public IActionResult ReadFile(ReadOptionFileRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        _ = cancellationToken;
        if (OptionFileReader.FormatOf(request.FileName) is not { } format)
        {
            throw new FormBuilderValidationException([new DefinitionProblem(DefinitionErrors.InvalidValue, OptionFileReader.Path, "Choose a CSV or a TXT file.")]);
        }

        var read = OptionFileReader.Read(request.Content, format);
        if (read.Items is not { } items)
        {
            throw new FormBuilderValidationException(read.Problems);
        }

        using var document = JsonDocument.Parse(items);
        return Ok(new OptionFileResponseModel { Items = document.RootElement.Clone(), Count = document.RootElement.GetArrayLength() });
    }

    /// <summary>What this server keeps of a URL list's address: when it was fetched and how many options it gave.</summary>
    /// <param name="key">The list.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("{key:guid}/fetched")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageLibrary)]
    [ProducesResponseType(typeof(OptionListFetchResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Fetched(Guid key, CancellationToken cancellationToken = default) =>
        await library.FetchStateAsync(key, cancellationToken) is { } state ? Ok(OptionListFetchResponseModel.Of(state)) : FormBuilderProblems.OptionListNotFound();

    /// <summary>
    /// Fetch now: every server drops the copy of a saved URL list's address and this one fetches it again; what it keeps
    /// then. A list of another kind answers <c>404</c>.
    /// </summary>
    /// <param name="key">The list.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("{key:guid}/fetch")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageLibrary)]
    [ProducesResponseType(typeof(OptionListFetchResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Fetch(Guid key, CancellationToken cancellationToken = default) =>
        await library.FetchNowAsync(key, RequestAccess.User.Key, cancellationToken) is { } state ? Ok(OptionListFetchResponseModel.Of(state)) : FormBuilderProblems.OptionListNotFound();

    // A list's values as the service takes them; a kind that is no kind's name is refused at its path.
    private static OptionListInput Input(OptionListRequestModel list)
    {
        ArgumentNullException.ThrowIfNull(list);
        return new OptionListInput(list.Alias, list.Name, KindOf(list.Kind), Json(list.Config, "{}"), Json(list.Items, "[]"), list.CacheSeconds);
    }

    private static OptionListKind KindOf(string kind) =>
        EnumNames<OptionListKind>.ByName.TryGetValue(kind ?? string.Empty, out var found)
            ? found
            : throw new FormBuilderValidationException([new DefinitionProblem(DefinitionErrors.InvalidValue, "$.kind", $"A list's kind is one of {string.Join(", ", EnumNames<OptionListKind>.All)}.")]);

    private static string Json(JsonElement? value, string none) => value is { ValueKind: not JsonValueKind.Undefined and not JsonValueKind.Null } element ? element.GetRawText() : none;

    // The stored list of a change, or the problems that refused it as a 400 with each at its path.
    private static StoredOptionList Stored(OptionListResult result) =>
        result.List ?? throw new FormBuilderValidationException(result.Problems);
}
