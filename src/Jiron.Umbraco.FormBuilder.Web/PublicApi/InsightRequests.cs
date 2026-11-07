using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Insights;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jiron.Umbraco.FormBuilder.Web.PublicApi;

/// <summary>
/// What the events endpoint reads of a request and answers with (ADR-0211): a beacon's body of at most
/// <see cref="FormInsights.MaxBeaconBytes"/>, read as bytes whatever its content type, as <c>navigator.sendBeacon</c> sends
/// it as text, which <see cref="FormInsights"/> reads and counts. The answer is <c>204</c> whether it counted or not.
/// </summary>
/// <param name="insights">What the form counts.</param>
internal sealed class InsightRequests(FormInsights insights)
{
    /// <summary>Reads a beacon of a form's page and counts it where it counts.</summary>
    /// <param name="http">The request.</param>
    /// <param name="form">The form as the site serves it.</param>
    /// <param name="cancellationToken">The request's cancellation, with its time limit.</param>
    public async Task<IActionResult> CountAsync(HttpContext http, PublishedForm form, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(form);
        var buffer = new byte[FormInsights.MaxBeaconBytes + 1];
        var length = 0;
        try
        {
            int read;
            while (length < buffer.Length && (read = await http.Request.Body.ReadAsync(buffer.AsMemory(length), cancellationToken)) > 0)
            {
                length += read;
            }
        }
        catch (BadHttpRequestException exception)
        {
            FormBuilderPublicProblems.CloseConnection(http);
            return exception.StatusCode == StatusCodes.Status413PayloadTooLarge
                ? FormBuilderPublicProblems.PayloadTooLarge()
                : FormBuilderPublicProblems.InvalidRequest(EntryBodies.Body);
        }

        if (length > FormInsights.MaxBeaconBytes)
        {
            FormBuilderPublicProblems.CloseConnection(http);
            return FormBuilderPublicProblems.PayloadTooLarge();
        }

        if (FormInsights.Read(buffer.AsSpan(0, length)) is { } beacon)
        {
            await insights.CountAsync(form, beacon, cancellationToken);
        }

        return new NoContentResult();
    }
}

/// <summary>A beacon of a form's page (ADR-0211); any other member is skipped unread.</summary>
internal sealed record InsightEventRequestModel
{
    /// <summary>What the visitor did: <c>view</c>, <c>start</c>, <c>page</c> or <c>invalid</c>.</summary>
    public required string Type { get; init; }

    /// <summary>For <c>page</c>: the key of the page the visitor reached.</summary>
    public Guid? Page { get; init; }

    /// <summary>For <c>invalid</c>: the field's path as the page names it (<c>data-jfb-alias</c>), such as <c>guests[0].name</c>.</summary>
    public string? Field { get; init; }

    /// <summary>For <c>invalid</c>: the kind of error, such as <c>required</c>, <c>check</c>, <c>rule</c> or <c>server</c>.</summary>
    public string? Kind { get; init; }
}
