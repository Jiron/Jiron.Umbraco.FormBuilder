using Asp.Versioning;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Web.Common.Routing;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Entries;

/// <summary>What the Inbox and Spam open with: the user's filter sets, of every form and of each.</summary>
public sealed class EntryInboxViewResponseModel
{
    /// <summary>The user's filter sets: those of every form, and those of a form the user may still view entries of.</summary>
    public required IReadOnlyList<EntryFilterSetResponseModel> FilterSets { get; init; }

    /// <summary>The forms the sets name, so the menu names each set's form, with what the user may do on each.</summary>
    public required IReadOnlyList<EntryListFormResponseModel> Forms { get; init; }
}

/// <summary>How many entries nobody opened yet the Inbox holds, for its badge.</summary>
public sealed class EntryUnseenResponseModel
{
    /// <summary>The count, at most 99.</summary>
    public required int Count { get; init; }

    /// <summary>Whether there are more than <c>count</c>: the badge reads "99+".</summary>
    public required bool More { get; init; }
}

/// <summary>Empty spam: whether to delete, after the answer that says what would go.</summary>
public sealed class EntrySpamEmptyRequestModel
{
    /// <summary>Whether to delete them; without it, the answer says what would go and deletes nothing.</summary>
    public bool Confirm { get; init; }
}

/// <summary>What emptying Spam deleted, or would delete.</summary>
public sealed class EntrySpamEmptyResponseModel
{
    /// <summary>The entries.</summary>
    public required int Entries { get; init; }

    /// <summary>Their uploaded files.</summary>
    public required int Files { get; init; }

    /// <summary>Whether it deleted them, as the request confirmed.</summary>
    public required bool Deleted { get; init; }

    /// <summary>Whether Spam holds more than one call takes; a second Empty spam deletes them.</summary>
    public required bool More { get; init; }

    /// <summary>The entries in Spam a legal hold keeps, which stay until it ends (ADR-0222).</summary>
    public required int Held { get; init; }
}

/// <summary>
/// The Inbox and Spam (IA 6.1 and 6.2, ADR-0209): the entries of every form the user may view entries of, the forms to
/// filter them by, the unseen count of the Inbox's badge, and Empty spam. Every endpoint answers any user of the section:
/// one who may view no form's entries gets empty lists and a count of none. A page names its forms with what the user may
/// do on each, so the bulk bar offers per row what its form allows, and acts through each form's own endpoints.
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/entries")]
[ApiExplorerSettings(GroupName = "Entries")]
internal sealed class EntryInboxController(
    EntryLists lists,
    EntryUserSettings settings,
    EntrySpamBin spam,
    IFormBuilderFormService forms,
    IFormBuilderAccessService access) : FormBuilderManagementApiController
{
    /// <summary>The most the badge counts; beyond, it says "99+".</summary>
    internal const int MostUnseen = 99;

    /// <summary>The most forms the Form filter offers for one search.</summary>
    internal const int MostForms = 50;

    /// <summary>
    /// What the Inbox and Spam open with: the user's filter sets with the forms they name. A set of a form the user may no
    /// longer view entries of, or that is gone or in the Recycle Bin, is left out, as it could not filter the list; so are
    /// such forms among those a set filters by, and a set none of whose forms is left, which would otherwise widen to every
    /// form.
    /// </summary>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("view")]
    [MapToApiVersion("1.0")]
    [FormBuilderSection]
    [ProducesResponseType(typeof(EntryInboxViewResponseModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> View(CancellationToken cancellationToken = default)
    {
        var named = new Dictionary<Guid, EntryListFormResponseModel?>();
        async Task<bool> ViewsAsync(Guid key)
        {
            if (!named.TryGetValue(key, out var form))
            {
                named[key] = form = await ViewableAsync(key, cancellationToken);
            }

            return form is not null;
        }

        var shown = new List<EntryFilterSetResponseModel>();
        foreach (var set in await settings.GetFilterSetsAsync(RequestAccess.User.Key, cancellationToken))
        {
            if (set.Form is { } key && !await ViewsAsync(key))
            {
                continue;
            }

            var kept = new List<Guid>();
            foreach (var form in set.Filter.Forms)
            {
                if (await ViewsAsync(form))
                {
                    kept.Add(form);
                }
            }

            if (set.Filter.Forms.Count > 0 && kept.Count == 0)
            {
                continue;
            }

            shown.Add(EntryListMapping.Model(kept.Count == set.Filter.Forms.Count ? set : set with { Filter = set.Filter with { Forms = kept } }));
        }

        return Ok(new EntryInboxViewResponseModel { FilterSets = shown, Forms = [.. named.Values.OfType<EntryListFormResponseModel>()] });
    }

    /// <summary>A page of the Inbox: every form's entries the user may view, unseen first unless sorted, 50 at a time.</summary>
    /// <param name="request">Which entries, the columns (with one form chosen) and the page.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("inbox")]
    [MapToApiVersion("1.0")]
    [FormBuilderSection]
    [ProducesResponseType(typeof(EntryListPageResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public Task<IActionResult> Inbox(EntryListRequestModel request, CancellationToken cancellationToken = default) =>
        ListAsync(EntryListPlace.Inbox, request, cancellationToken);

    /// <summary>A page of Spam: every form's entries in Spam the user may view, newest first, 50 at a time.</summary>
    /// <param name="request">Which entries, the columns (with one form chosen) and the page.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("spam")]
    [MapToApiVersion("1.0")]
    [FormBuilderSection]
    [ProducesResponseType(typeof(EntryListPageResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public Task<IActionResult> Spam(EntryListRequestModel request, CancellationToken cancellationToken = default) =>
        ListAsync(EntryListPlace.Spam, request, cancellationToken);

    /// <summary>The forms the user may view entries of whose name holds the text, by name, for the Form filter.</summary>
    /// <param name="search">What the name holds; none for the first forms by name.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("forms")]
    [MapToApiVersion("1.0")]
    [FormBuilderSection]
    [ProducesResponseType(typeof(IReadOnlyList<EntryListFormResponseModel>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Forms(string? search = null, CancellationToken cancellationToken = default)
    {
        var visibility = await access.GetFormVisibilityAsync(RequestAccess.User, FormBuilderPermissions.ViewEntries, cancellationToken);
        var found = await lists.ListFormsAsync(visibility, search, MostForms, cancellationToken);
        return Ok(found.Select(form => new EntryListFormResponseModel { Key = form.Key, Name = form.Name }).ToList());
    }

    /// <summary>How many entries nobody opened yet the Inbox holds, up to 99, for its badge.</summary>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("unseen")]
    [MapToApiVersion("1.0")]
    [FormBuilderSection]
    [ProducesResponseType(typeof(EntryUnseenResponseModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> Unseen(CancellationToken cancellationToken = default)
    {
        var visibility = await access.GetFormVisibilityAsync(RequestAccess.User, FormBuilderPermissions.ViewEntries, cancellationToken);
        var count = await lists.CountUnseenAsync(visibility, MostUnseen, cancellationToken);
        return Ok(new EntryUnseenResponseModel { Count = count.Value, More = count.More });
    }

    /// <summary>
    /// Empties Spam: deletes for good the entries in Spam of the forms the user may delete entries of, with their files and
    /// timelines; without <c>confirm</c>, says what would go, for the dialog that asks first.
    /// </summary>
    /// <param name="request">Whether to delete them.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("spam/empty")]
    [MapToApiVersion("1.0")]
    [FormBuilderSection]
    [ProducesResponseType(typeof(EntrySpamEmptyResponseModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> EmptySpam(EntrySpamEmptyRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var visibility = await access.GetFormVisibilityAsync(RequestAccess.User, FormBuilderPermissions.DeleteEntries, cancellationToken);
        var emptying = request.Confirm
            ? await spam.EmptyAsync(visibility, RequestAccess.User.Key, cancellationToken)
            : await spam.PlanAsync(visibility, cancellationToken);
        return Ok(new EntrySpamEmptyResponseModel { Entries = emptying.Entries, Files = emptying.Files, Deleted = request.Confirm && emptying.Entries > 0, More = emptying.More, Held = emptying.Held });
    }

    // A form the user may view entries of, named with what the user may do on it; null for one gone, trashed or out of reach.
    private async Task<EntryListFormResponseModel?> ViewableAsync(Guid key, CancellationToken cancellationToken)
    {
        if (await forms.GetAsync(key, cancellationToken) is not { TrashedUtc: null } info)
        {
            return null;
        }

        var permissions = await access.GetForFormAsync(RequestAccess.User, FormAccessTarget.Of(info), cancellationToken);
        return permissions.HasFlag(FormBuilderPermissions.ViewEntries)
            ? new EntryListFormResponseModel { Key = info.Key, Name = info.Name, Permissions = PermissionRules.NamesOf(permissions) }
            : null;
    }

    // A page of the place, its forms named with what the user may do on each.
    private async Task<IActionResult> ListAsync(EntryListPlace place, EntryListRequestModel request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var problems = new List<DefinitionProblem>();
        var filter = EntryListMapping.Filter(request.Filter, "filter", problems);
        if (problems.Count > 0)
        {
            throw new FormBuilderValidationException(problems);
        }

        var visibility = await access.GetFormVisibilityAsync(RequestAccess.User, FormBuilderPermissions.ViewEntries, cancellationToken);
        var page = await lists.ListAcrossAsync(place, visibility, new EntryListRequest(filter) { Columns = request.Columns ?? [], After = request.After, Count = request.Count }, cancellationToken);
        var model = EntryListMapping.Model(page);
        return Ok(new EntryListPageResponseModel
        {
            Items = model.Items,
            Next = model.Next,
            Total = model.Total,
            More = model.More,
            Forms = await EntryListMapping.FormsAsync(page.Forms, forms, access, RequestAccess.User, cancellationToken),
        });
    }
}
