using System.Globalization;
using Asp.Versioning;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Insights;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Access;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Authorization;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Entries;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.Entities;
using Umbraco.Cms.Core.Security.Authorization;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Web.Common.Routing;
using Umbraco.Extensions;

namespace Jiron.Umbraco.FormBuilder.Web.ManagementApi.Insights;

/// <summary>
/// The insights of forms (ADR-0211): a form's report and its answers' summaries (ADR-0212), which need <c>viewEntries</c> on
/// the form; the site's table of the forms the user may view entries of, for the Overview; and the boxes the user shows as
/// tables. A span is UTC days
/// from <c>from</c> to <c>to</c> (<c>yyyy-MM-dd</c>), at most 366 of them; without both, the last 30 days to today.
/// </summary>
/// <param name="reports">The reports.</param>
/// <param name="answers">The answers' summaries.</param>
/// <param name="published">The published forms, whose pages a report follows.</param>
/// <param name="tables">The boxes the user shows as tables.</param>
/// <param name="access">Which forms the user may view entries of.</param>
/// <param name="entities">The website pages' names.</param>
/// <param name="appCaches">What the user's access to a page is read through.</param>
/// <param name="users">The user who asks, as Umbraco knows them.</param>
/// <param name="time">The clock, whose UTC day ends the span a request leaves out.</param>
/// <param name="timeZones">The time zone every site shares, in which an entry's hour is counted.</param>
[ApiVersion("1.0")]
[BackOfficeRoute(FormBuilderManagementApi.Route)]
[ApiExplorerSettings(GroupName = "Insights")]
internal sealed class InsightController(
    InsightReports reports,
    AnswerSummaries answers,
    PublishedFormCache published,
    InsightUserSettings tables,
    IFormBuilderAccessService access,
    IEntityService entities,
    AppCaches appCaches,
    IAuthorizationHelper users,
    TimeProvider time,
    SiteTimeZones timeZones) : FormBuilderManagementApiController
{
    /// <summary>How many days a span covers that a request leaves out.</summary>
    public const int DefaultDays = 30;

    /// <summary>
    /// A form's report over a span: its numbers, entries per day, week or month, who reaches each page, the median time
    /// to complete, the fields that stop visitors most, the top website pages, entries by hour, the members who sent
    /// entries, and the span compared with where one is given.
    /// </summary>
    /// <param name="key">The form.</param>
    /// <param name="from">The first day.</param>
    /// <param name="to">The last day.</param>
    /// <param name="groupBy"><c>day</c> (the default), <c>week</c> or <c>month</c>.</param>
    /// <param name="compareFrom">The first day of the span compared with, or none for no comparison.</param>
    /// <param name="compareTo">Its last day, with <paramref name="compareFrom"/>.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("form/{key:guid}/insights")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ViewEntries, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(InsightReportResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Report(Guid key, string? from, string? to, string? groupBy, string? compareFrom, string? compareTo, CancellationToken cancellationToken = default)
    {
        var problems = new List<DefinitionProblem>();
        var span = Span(from, to, "from", "to", problems) ?? Default();
        var compare = compareFrom is null && compareTo is null ? null : Span(compareFrom, compareTo, "compareFrom", "compareTo", problems);
        var grouping = groupBy switch
        {
            null or "day" => InsightGrouping.Day,
            "week" => InsightGrouping.Week,
            "month" => InsightGrouping.Month,
            _ => (InsightGrouping?)null,
        };
        if (grouping is null)
        {
            problems.Add(new DefinitionProblem("invalid_value", "groupBy", "Group by day, week or month."));
        }

        if (problems.Count > 0)
        {
            throw new FormBuilderValidationException(problems);
        }

        var definition = (await published.GetAsync(key, cancellationToken))?.Published.Definition;
        if (await reports.ReportAsync(key, definition, new InsightRequest(span, grouping!.Value, compare), cancellationToken) is not { } report)
        {
            return FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Form);
        }

        var origins = Names(report.Origins.Select(origin => origin.Content));
        return Ok(new InsightReportResponseModel
        {
            SiteOn = report.SiteOn,
            FormOn = report.FormOn,
            Totals = InsightTotalsResponseModel.Of(report.Totals),
            Before = report.Before is { } before ? InsightTotalsResponseModel.Of(before) : null,
            Periods = [.. report.Periods.Select(period => new InsightPeriodResponseModel { Start = period.Start, Entries = period.Entries, Before = period.Before })],
            Pages = [.. report.Pages.Select(page => new InsightPageResponseModel { Key = page.Page, Name = TitleOf(definition, page.Page), Reached = page.Reached })],
            MedianCompletion = report.MedianCompletion,
            Fields = [.. report.Fields.Select(field => new InsightFieldResponseModel { Key = field.Field, Label = field.Label, Stops = field.Stops, Kind = field.Kind })],
            Origins = [.. report.Origins.Select(origin => new InsightOriginResponseModel { Key = origin.Content, Name = origins.GetValueOrDefault(origin.Content), Entries = origin.Entries })],
            Hours = report.Hours,
            Members = report.Members.Members,
            MemberEntries = report.Members.Entries,
            MembersBefore = report.MembersBefore,
            TimeZone = (await timeZones.GetAsync(null, cancellationToken)).Id,
            Tables = await tables.GetTablesAsync(RequestAccess.User.Key, cancellationToken),
        });
    }

    /// <summary>
    /// The site's table of forms over a span, for the Overview: the forms the user may view entries of with the most
    /// entries, then views, each with the numbers of the span compared with. Any user of the section may ask; it is
    /// empty for one who may view entries nowhere.
    /// </summary>
    /// <param name="from">The first day.</param>
    /// <param name="to">The last day.</param>
    /// <param name="compareFrom">The first day of the span compared with; without both, as many days just before the span.</param>
    /// <param name="compareTo">Its last day.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpGet("insights/glance")]
    [MapToApiVersion("1.0")]
    [FormBuilderSection]
    [ProducesResponseType(typeof(InsightGlanceResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Glance(string? from, string? to, string? compareFrom, string? compareTo, CancellationToken cancellationToken = default)
    {
        var problems = new List<DefinitionProblem>();
        var span = Span(from, to, "from", "to", problems) ?? Default();
        var compare = Span(compareFrom, compareTo, "compareFrom", "compareTo", problems) ?? Before(span);
        if (compare.From.Year < 1753)
        {
            problems.Add(new DefinitionProblem("invalid_value", "from", "The span compared with, as many days just before, starts in 1753 or later."));
        }

        if (problems.Count > 0)
        {
            throw new FormBuilderValidationException(problems);
        }

        var visibility = await access.GetFormVisibilityAsync(RequestAccess.User, FormBuilderPermissions.ViewEntries, cancellationToken);
        return Ok(new InsightGlanceResponseModel
        {
            Items = [.. (await reports.GlanceAsync(visibility, span, compare, cancellationToken)).Select(glance => new InsightGlanceItemResponseModel
            {
                Key = glance.Form,
                Name = glance.Name,
                Totals = InsightTotalsResponseModel.Of(glance.Totals),
                Before = InsightTotalsResponseModel.Of(glance.Before),
            })],
        });
    }

    /// <summary>
    /// The answers of a form's entries summarised per field (ADR-0212): those the entries list's filter holds among the
    /// entries received in the span, never a test entry, one in Spam, a sensitive field or one kept only until the actions
    /// run; each field by what its type says its answers are, with the newest free-text answers.
    /// </summary>
    /// <param name="key">The form.</param>
    /// <param name="request">The span and the filter.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("form/{key:guid}/insights/answers")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ViewEntries, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(InsightAnswersResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Answers(Guid key, InsightAnswersRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return await answers.SummarizeAsync(key, AnswerRequestOf(request.From, request.To, request.Filter), cancellationToken) is { } report
            ? Ok(InsightAnswersResponseModel.Of(report))
            : FormBuilderProblems.NotFound(FormBuilderAccessResourceKind.Form);
    }

    /// <summary>
    /// The next free-text answers of a field, from where the summary or the page before ended, as the summary read them;
    /// 404 for a field the summaries show no texts of.
    /// </summary>
    /// <param name="key">The form.</param>
    /// <param name="field">The field.</param>
    /// <param name="request">The span, the filter and where to start.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPost("form/{key:guid}/insights/answers/{field:guid}/texts")]
    [MapToApiVersion("1.0")]
    [FormBuilderPermission(FormBuilderPermissions.ViewEntries, On = FormBuilderAccessResourceKind.Form, Key = "key")]
    [ProducesResponseType(typeof(InsightAnswerTextsResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AnswerTexts(Guid key, Guid field, InsightAnswerTextsRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (await answers.TextsAsync(key, field, AnswerRequestOf(request.From, request.To, request.Filter), request.After, cancellationToken) is not { } page)
        {
            return FormBuilderProblems.FieldNotFound();
        }

        return Ok(new InsightAnswerTextsResponseModel { Texts = [.. page.Texts.Select(InsightAnswerTextResponseModel.Of)], More = page.More });
    }

    /// <summary>Keeps the boxes the user shows as tables, the same on every device and for every form; the answer is what it kept.</summary>
    /// <param name="request">The boxes.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    [HttpPut("insights/tables")]
    [MapToApiVersion("1.0")]
    [FormBuilderSection]
    [ProducesResponseType(typeof(InsightTablesResponseModel), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Tables(InsightTablesRequestModel request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        await tables.SetTablesAsync(RequestAccess.User.Key, request.Tables ?? [], cancellationToken);
        return Ok(new InsightTablesResponseModel { Tables = await tables.GetTablesAsync(RequestAccess.User.Key, cancellationToken) });
    }

    // The span and the filter of a request for answers; its problems refused together.
    private AnswerRequest AnswerRequestOf(string? from, string? to, EntryListFilterRequestModel? filter)
    {
        var problems = new List<DefinitionProblem>();
        var span = Span(from, to, "from", "to", problems) ?? Default();
        var read = EntryListMapping.Filter(filter, "filter", problems);
        if (problems.Count > 0)
        {
            throw new FormBuilderValidationException(problems);
        }

        return new AnswerRequest(span, read);
    }

    // As many days as a span, just before it.
    private static InsightSpan Before(InsightSpan span)
    {
        var days = span.To.DayNumber - span.From.DayNumber + 1;
        return new InsightSpan(span.From.AddDays(-days), span.From.AddDays(-1));
    }

    // A span from its two days, each yyyy-MM-dd, the last on or after the first, at most 366 days from 1753 on; null for
    // a span the request leaves out, and for one it gets wrong.
    private static InsightSpan? Span(string? from, string? to, string fromPath, string toPath, List<DefinitionProblem> problems)
    {
        if (from is null && to is null)
        {
            return null;
        }

        var first = Day(from, fromPath, problems);
        var last = Day(to, toPath, problems);
        if (first is null || last is null)
        {
            return null;
        }

        if (last < first || last.Value.DayNumber - first.Value.DayNumber >= DailyStatRepository.MaxListDays)
        {
            problems.Add(new DefinitionProblem("invalid_value", toPath, $"The last day comes on or after the first, at most {DailyStatRepository.MaxListDays} days on."));
            return null;
        }

        return new InsightSpan(first.Value, last.Value);
    }

    // The last 30 days to today, by the UTC day.
    private InsightSpan Default()
    {
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        return new InsightSpan(today.AddDays(1 - DefaultDays), today);
    }

    private static DateOnly? Day(string? text, string path, List<DefinitionProblem> problems)
    {
        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) && day.Year >= 1753)
        {
            return day;
        }

        problems.Add(new DefinitionProblem("invalid_value", path, "A day is yyyy-MM-dd, from 1753 on."));
        return null;
    }

    // A page's title in the default culture; null for one without, or whose title names a dictionary item.
    private static string? TitleOf(FormDefinition? definition, Guid page) =>
        definition?.Pages.FirstOrDefault(each => each.Key == page)?.Title?.Default is { Length: > 0 } title && !LocalizedString.TryGetDictionaryKey(title, out _) ? title : null;

    // The names of the website pages the user may open in Content.
    private Dictionary<Guid, string> Names(IEnumerable<Guid> keys)
    {
        var names = new Dictionary<Guid, string>();
        var wanted = keys.ToArray();
        if (wanted.Length == 0)
        {
            return names;
        }

        var user = users.GetUmbracoUser(User);
        foreach (var page in entities.GetAll(UmbracoObjectTypes.Document, wanted).OfType<IDocumentEntitySlim>())
        {
            if (user.HasContentPathAccess(page, entities, appCaches) && page.Name is { Length: > 0 } name)
            {
                names[page.Key] = name;
            }
        }

        return names;
    }
}
