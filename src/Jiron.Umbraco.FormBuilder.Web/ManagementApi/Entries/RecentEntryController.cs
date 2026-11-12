using Asp.Versioning;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Web.Common.Routing;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Entries;

/// <summary>
/// The newest entries a user may view, for the Overview (ADR-0165). Any user of the section may ask, and the list holds only the
/// forms they hold <c>viewEntries</c> on, so it is empty for a user who holds it nowhere.
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/entry/recent")]
[ApiExplorerSettings(GroupName = "Entries")]
internal sealed class RecentEntryController(RecentEntries recent) : FormBuilderManagementApiController
{
    /// <summary>The newest entries, newest first, each named by its title or number, with no answers.</summary>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet]
    [MapToApiVersion("1.0")]
    [FormBuilderSection]
    [ProducesResponseType(typeof(RecentEntriesResponseModel), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken = default) =>
        Ok(new RecentEntriesResponseModel { Items = (await recent.ListAsync(RequestAccess.User, cancellationToken)).Select(RecentEntryResponseModel.Of).ToList() });
}

/// <summary>One of the newest entries.</summary>
internal sealed class RecentEntryResponseModel
{
    /// <summary>The entry's key.</summary>
    public required Guid Key { get; init; }

    /// <summary>The form's key.</summary>
    public required Guid FormKey { get; init; }

    /// <summary>The form's name.</summary>
    public required string FormName { get; init; }

    /// <summary>The entry's title, without sensitive answers; null when it has none, and its number names it.</summary>
    public string? Title { get; init; }

    /// <summary>The entry's number within the form.</summary>
    public required int Number { get; init; }

    /// <summary>When it was received.</summary>
    public required DateTimeOffset Submitted { get; init; }

    /// <summary>Whether nobody has opened it yet.</summary>
    public required bool Unseen { get; init; }

    internal static RecentEntryResponseModel Of(RecentEntry entry) => new()
    {
        Key = entry.Key,
        FormKey = entry.FormKey,
        FormName = entry.FormName,
        Title = entry.Title,
        Number = entry.Number,
        Submitted = entry.Submitted,
        Unseen = entry.Unseen,
    };
}

/// <summary>The newest entries a user may view.</summary>
internal sealed class RecentEntriesResponseModel
{
    /// <summary>The entries, newest first.</summary>
    public required IReadOnlyList<RecentEntryResponseModel> Items { get; init; }
}
