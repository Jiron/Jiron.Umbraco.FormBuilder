using System.Globalization;
using System.Net.Mime;
using System.Text;
using System.Text.Json;
using Asp.Versioning;
using Jiron.Umbraco.FormBuilder.Core;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Audit;
using Jiron.Umbraco.FormBuilder.Core.Exports;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Web.Common.Routing;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Activity;

/// <summary>
/// Activity > Audit log (section 4.10, ADR-0041, ADR-0219): who did what to which item and when, the newest first, with
/// filters, a search over people and items, and an export as CSV, which is itself recorded. It needs <c>manageSettings</c>.
/// </summary>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route + "/audit")]
[ApiExplorerSettings(GroupName = "Audit")]
internal sealed class AuditController(
    IAuditRepository audit,
    AuditNames names,
    IUserService users,
    IFormRepository forms,
    IFolderRepository folders,
    IOptionListRepository optionLists,
    IThemeRepository themes,
    IEmailLayoutRepository layouts,
    IFormTemplateRepository templates,
    TimeProvider time) : FormBuilderManagementApiController
{
    /// <summary>The most actions one page holds.</summary>
    public const int MaxTake = 100;

    /// <summary>The most actions one export writes.</summary>
    public const int MaxExportRows = 100_000;

    /// <summary>The longest search.</summary>
    public const int MaxSearchLength = 200;

    private const int ExportPage = 500;

    /// <summary>A page of the log, the newest first, with the names of the users and items it names. <c>400</c> for a filter it does not know.</summary>
    /// <param name="what"><c>forms</c>, <c>entries</c>, <c>library</c> or <c>settings</c>; none for all.</param>
    /// <param name="user">Only this user's actions.</param>
    /// <param name="form">Only the actions on this form or its entries.</param>
    /// <param name="days">Only the last so many days, 1 to 3,660; none for all.</param>
    /// <param name="search">A name or email of a person, or a name of a form, folder or item of the library, at most 200 characters.</param>
    /// <param name="skip">The actions to pass over.</param>
    /// <param name="take">The actions to read, 1 to 100.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageSettings)]
    [ProducesResponseType(typeof(AuditListResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> List(
        string? what = null,
        Guid? user = null,
        Guid? form = null,
        int? days = null,
        string? search = null,
        long skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        if (skip < 0 || take is < 1 or > MaxTake)
        {
            ModelState.AddModelError(skip < 0 ? nameof(skip) : nameof(take), "range");
        }

        if (await QueryAsync(what, user, form, days, search, cancellationToken) is not { } query)
        {
            return FormBuilderProblems.InvalidRequest(ModelState);
        }

        var page = await audit.QueryAsync(query, new PageRequest(skip, take), cancellationToken);
        var (userNames, items) = await names.OfAsync(page.Items, cancellationToken);
        return Ok(new AuditListResponseModel
        {
            Items = [.. page.Items.Select(entry => Model(entry, userNames))],
            Total = page.Total,
            Names = items,
        });
    }

    /// <summary>
    /// The filtered log as CSV, the newest first, at most 100,000 actions: the time in UTC, the user, the action, the item and
    /// the details as JSON, each cell guarded against formulas. The export is recorded first. <c>400</c> as for the list.
    /// </summary>
    /// <param name="what"><c>forms</c>, <c>entries</c>, <c>library</c> or <c>settings</c>; none for all.</param>
    /// <param name="user">Only this user's actions.</param>
    /// <param name="form">Only the actions on this form or its entries.</param>
    /// <param name="days">Only the last so many days, 1 to 3,660; none for all.</param>
    /// <param name="search">As for the list.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("export")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ManageSettings)]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK, "text/csv")]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Export(
        string? what = null,
        Guid? user = null,
        Guid? form = null,
        int? days = null,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        if (await QueryAsync(what, user, form, days, search, cancellationToken) is not { } query)
        {
            return FormBuilderProblems.InvalidRequest(ModelState);
        }

        var total = (await audit.QueryAsync(query, PageRequest.First(1), cancellationToken)).Total;
        var rows = (int)Math.Min(total, MaxExportRows);
        var details = new AuditDetails()
            .Add("rows", rows)
            .AddIdentifier("what", what)
            .Add("user", user)
            .Add("form", form)
            .Add("days", days)
            .Add("searched", search is not null);
        var recorded = await audit.AddAsync(new NewAuditEntry(AuditGroups.ExportedAction, AuditGroups.EntityType, null, RequestAccess.User.Key, details), cancellationToken);

        // What was recorded before the export's own record, which names its filters and so may match them.
        var before = query.BeforeUtc is { } asked && asked < recorded.OccurredUtc ? asked : recorded.OccurredUtc;
        var snapshot = query with { BeforeUtc = before };

        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.ContentSecurityPolicy = "sandbox; default-src 'none'";
        var day = time.GetUtcNow().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        Response.Headers.ContentDisposition = new ContentDisposition { FileName = $"audit-log-{day}.csv", Inline = false }.ToString();
        return new StreamedCsv((body, aborted) => WriteAsync(snapshot, rows, body, aborted));
    }

    // The query of the filters, with the search's matches; null with the model state's errors for a filter it can't read.
    private async Task<AuditQuery?> QueryAsync(string? what, Guid? user, Guid? form, int? days, string? search, CancellationToken cancellationToken)
    {
        AuditGroup? group = null;
        if (what is not null)
        {
            if (EnumNames<AuditGroup>.ByName.TryGetValue(what, out var parsed))
            {
                group = parsed;
            }
            else
            {
                ModelState.AddModelError(nameof(what), "unknown");
            }
        }

        if (days is < 1 or > 3660)
        {
            ModelState.AddModelError(nameof(days), "range");
        }

        if (search is { Length: > MaxSearchLength })
        {
            ModelState.AddModelError(nameof(search), "length");
        }

        if (!ModelState.IsValid)
        {
            return null;
        }

        return new AuditQuery
        {
            Entities = group is { } chosen ? AuditGroups.KindsOf(chosen) : null,
            UserKey = user,
            FormKey = form,
            FromUtc = days is { } span ? time.GetUtcNow().AddDays(-span) : null,
            Search = string.IsNullOrWhiteSpace(search) ? null : await SearchAsync(search.Trim(), cancellationToken),
        };
    }

    // The people whose name, user name or email holds the text, and the forms, folders and library items whose name does.
    private async Task<AuditSearch> SearchAsync(string text, CancellationToken cancellationToken)
    {
        var filter = new UserFilter { NameFilters = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { text } };
        var found = await users.FilterAsync(RequestAccess.User.Key, filter, 0, AuditSearch.MaxMatches, UserOrder.Name, Direction.Ascending);
        var people = found.Result?.Items.Select(person => person.Key).ToList() ?? [];

        const int Each = 40;
        var named = (await forms.ListAsync(new FormFilter { AllFolders = true, Trashed = null, Search = text }, PageRequest.First(AuditSearch.MaxForms), cancellationToken)).Items.Select(item => item.Key).ToList();
        var items = new List<Guid>(named);
        items.AddRange((await folders.ListAllAsync(cancellationToken)).Where(folder => folder.Name.Contains(text, StringComparison.OrdinalIgnoreCase)).Take(Each).Select(folder => folder.Key));
        items.AddRange((await optionLists.ListAsync(text, PageRequest.First(Each), cancellationToken)).Items.Select(item => item.Key));
        items.AddRange((await themes.ListAsync(text, PageRequest.First(Each), cancellationToken)).Items.Select(item => item.Key));
        items.AddRange((await layouts.ListAsync(text, PageRequest.First(Each), cancellationToken)).Items.Select(item => item.Key));
        items.AddRange((await templates.ListAsync(new FormTemplateFilter(Search: text), PageRequest.First(Each), cancellationToken)).Items.Select(item => item.Key));
        return new AuditSearch([.. people.Distinct().Take(AuditSearch.MaxMatches)], [.. items.Distinct().Take(AuditSearch.MaxMatches)], named);
    }

    private async Task WriteAsync(AuditQuery query, int rows, Stream body, CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), 1 << 16, leaveOpen: true) { NewLine = "\r\n" };
        await writer.WriteLineAsync(Row(["occurredUtc", "user", "userKey", "action", "entityType", "entityKey", "item", "details"]));
        for (var skip = 0; skip < rows; skip += ExportPage)
        {
            var page = await audit.QueryAsync(query, new PageRequest(skip, Math.Min(ExportPage, rows - skip)), cancellationToken);
            var (userNames, items) = await names.OfAsync(page.Items, cancellationToken);
            foreach (var entry in page.Items)
            {
                var item = entry.EntityKey is { } key && items.TryGetValue(key, out var named) ? named.Name : string.Empty;
                await writer.WriteLineAsync(Row(
                [
                    entry.OccurredUtc.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture),
                    entry.UserKey is { } person ? userNames.GetValueOrDefault(person, string.Empty) : string.Empty,
                    entry.UserKey?.ToString("D") ?? string.Empty,
                    entry.Action,
                    entry.EntityType,
                    entry.EntityKey?.ToString("D") ?? string.Empty,
                    item,
                    entry.Details,
                ]));
            }

            if (page.Items.Count == 0)
            {
                break;
            }
        }

        await writer.FlushAsync(cancellationToken);
    }

    private static string Row(IEnumerable<string> cells) => string.Join(",", cells.Select(CsvExportFormat.Cell));

    private static AuditEntryResponseModel Model(StoredAuditEntry entry, IReadOnlyDictionary<Guid, string> userNames)
    {
        using var details = JsonDocument.Parse(entry.Details);
        return new AuditEntryResponseModel
        {
            Id = entry.Id,
            OccurredUtc = entry.OccurredUtc,
            UserKey = entry.UserKey,
            UserName = entry.UserKey is { } key ? userNames.GetValueOrDefault(key) : null,
            Action = entry.Action,
            EntityType = entry.EntityType,
            EntityKey = entry.EntityKey,
            Details = details.RootElement.Clone(),
        };
    }

    // The CSV written straight to the response, once the filters of the result have run, as nothing may touch the headers
    // after its first byte.
    private sealed class StreamedCsv(Func<Stream, CancellationToken, Task> write) : ActionResult
    {
        public override Task ExecuteResultAsync(ActionContext context)
        {
            var response = context.HttpContext.Response;
            response.ContentType = "text/csv; charset=utf-8";
            return write(response.Body, context.HttpContext.RequestAborted);
        }
    }
}
