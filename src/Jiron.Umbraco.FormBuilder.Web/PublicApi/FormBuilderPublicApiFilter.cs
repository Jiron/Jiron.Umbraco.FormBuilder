using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Jiron.Umbraco.FormBuilder.Web.PublicApi.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;

namespace Jiron.Umbraco.FormBuilder.Web.PublicApi;

/// <summary>
/// Applies each action's declarations before its body is read or bound (ADR-0012, ADR-0084): its time limit, on the
/// site's <see cref="TimeProvider"/>, cancels the request's token, which the middleware answers with
/// <c>504 timeout</c>; a declared length above its body limit, or any body for an action that takes none, answers
/// <c>413 payload_too_large</c> and closes the connection; a body without a declared length is held to the limit by
/// the server while it is read; and while Jiron Form Builder's migrations are pending, every action answers
/// <c>503 migrations_pending</c> with <c>Retry-After</c> and touches no table (ADR-0033). An action without a
/// declaration gets the strictest: no body and <see cref="DefaultTimeLimit"/>. An answer whose bytes the action built is
/// written on the visitor's own token, past the time limit too (ADR-0193). A preview frame's request reaches only an
/// action that takes it (<see cref="FormBuilderPreviewAttribute"/>), for the form its ticket names, and is otherwise
/// answered <c>401 preview_ticket_invalid</c> (ADR-0203).
/// </summary>
/// <param name="migrations">Whether the migration plan has run.</param>
/// <param name="time">The site's clock.</param>
/// <param name="host">The host's limits for files, which give the largest upload.</param>
internal sealed class FormBuilderPublicApiFilter(IFormBuilderMigrationState migrations, TimeProvider time, FieldHostLimits host) : IAsyncResourceFilter, IAsyncAlwaysRunResultFilter
{
    /// <summary>The time limit of an action that declares none.</summary>
    public static readonly TimeSpan DefaultTimeLimit = TimeSpan.FromSeconds(10);

    /// <inheritdoc />
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        var http = context.HttpContext;
        var metadata = context.ActionDescriptor.EndpointMetadata;
        var limit = metadata.OfType<FormBuilderTimeLimitAttribute>().FirstOrDefault()?.Limit ?? DefaultTimeLimit;
        var bodyLimit = metadata.OfType<FormBuilderBodyLimitAttribute>().FirstOrDefault()?.BytesFor(host) ?? 0;

        if (PreviewRequest.Of(http) is { } preview && (!metadata.OfType<FormBuilderPreviewAttribute>().Any() || !NamesForm(context, preview.FormKey)))
        {
            http.Response.Headers.WWWAuthenticate = PreviewTickets.Scheme;
            context.Result = FormBuilderPublicProblems.PreviewTicketInvalid();
            return;
        }

        if (TooLarge(http.Request, bodyLimit))
        {
            FormBuilderPublicProblems.CloseConnection(http);
            context.Result = FormBuilderPublicProblems.PayloadTooLarge();
            return;
        }

        // The server's own limit becomes the endpoint's, which an upload lowers to its field's: Files:MaxFileBytes is the
        // host's setting for uploads, and Kestrel's default would refuse a file the host allows.
        if (bodyLimit > 0 && http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } server)
        {
            server.MaxRequestBodySize = bodyLimit;
        }

        var left = http.RequestAborted;
        using var timer = new CancellationTokenSource(limit, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(left, timer.Token);
        http.Features.Set(new FormBuilderTimeLimitFeature(timer.Token, left));
        http.RequestAborted = linked.Token;
        try
        {
            if (await migrations.IsPendingAsync(linked.Token))
            {
                context.Result = FormBuilderPublicProblems.MigrationsPending(http);
                return;
            }

            // A form's rate limits need the form, which takes a read: after the migrations' check, and within the time limit (ADR-0149).
            if (http.Features.Get<FormLimitsToTake>() is { } limits
                && await http.RequestServices.GetRequiredService<FormRateLimits>().FinishAsync(limits, linked.Token) is { Allowed: false } refused)
            {
                context.Result = FormBuilderPublicProblems.RateLimited(http, refused);
                return;
            }

            await next();
        }
        finally
        {
            http.RequestAborted = left;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// MVC writes a file result on the request's token and, should that be cancelled, aborts the connection rather than
    /// send part of a file: a time limit that ran out after the action built its answer would cut the connection, with no
    /// answer and no <c>504</c>. The bytes are built, so the visitor's own token writes them (ADR-0193). A result that does
    /// its work as it writes, such as a rendered view, keeps the time limit.
    /// </remarks>
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        if (context.Result is FileContentResult && context.HttpContext.Features.Get<FormBuilderTimeLimitFeature>() is { } limit)
        {
            context.HttpContext.RequestAborted = limit.Left;
        }

        await next();
    }

    // Whether the action's route names the form by its key, as the actions the preview uses do.
    private static bool NamesForm(ResourceExecutingContext context, Guid form) =>
        context.RouteData.Values.TryGetValue("key", out var value)
        && value is string text
        && Guid.TryParseExact(text, "D", out var key)
        && key == form;

    // A declared length past the limit, or a body of any length for an action that takes none.
    private static bool TooLarge(HttpRequest request, long limit)
    {
        if (request.ContentLength is { } length)
        {
            return length > limit;
        }

        var hasBody = request.HttpContext.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody
            ?? request.Headers.TransferEncoding.Count > 0;
        return limit == 0 && hasBody;
    }
}
