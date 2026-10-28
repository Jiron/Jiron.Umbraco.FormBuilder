using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Jiron.Umbraco.FormBuilder.Web.PublicApi;

/// <summary>
/// Applies each action's declarations before its body is read or bound (ADR-0012, ADR-0084): its time limit, on the
/// site's <see cref="TimeProvider"/>, cancels the request's token, which the middleware answers with
/// <c>504 timeout</c>; a declared length above its body limit, or any body for an action that takes none, answers
/// <c>413 payload_too_large</c> and closes the connection; and while Jiron Form Builder's migrations are pending,
/// every action answers <c>503 migrations_pending</c> with <c>Retry-After</c> and touches no table (ADR-0033). An
/// action without a declaration gets the strictest: no body and <see cref="DefaultTimeLimit"/>.
/// </summary>
/// <param name="migrations">Whether the migration plan has run.</param>
/// <param name="time">The site's clock.</param>
internal sealed class FormBuilderPublicApiFilter(IFormBuilderMigrationState migrations, TimeProvider time) : IAsyncResourceFilter
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
        var bodyLimit = metadata.OfType<FormBuilderBodyLimitAttribute>().FirstOrDefault()?.Bytes ?? 0;

        if (TooLarge(http.Request, bodyLimit))
        {
            if (HttpProtocol.IsHttp11(http.Request.Protocol) || HttpProtocol.IsHttp10(http.Request.Protocol))
            {
                http.Response.Headers.Connection = "close";
            }

            context.Result = FormBuilderPublicProblems.PayloadTooLarge();
            return;
        }

        var left = http.RequestAborted;
        using var timer = new CancellationTokenSource(limit, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(left, timer.Token);
        http.Features.Set(new FormBuilderTimeLimitFeature(timer.Token));
        http.RequestAborted = linked.Token;
        try
        {
            if (await migrations.IsPendingAsync(linked.Token))
            {
                context.Result = FormBuilderPublicProblems.MigrationsPending(http);
                return;
            }

            await next();
        }
        finally
        {
            http.RequestAborted = left;
        }
    }

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
