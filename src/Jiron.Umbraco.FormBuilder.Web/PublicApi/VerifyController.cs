using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace Jiron.Umbraco.FormBuilder.Web.PublicApi;

/// <summary>
/// The page behind a confirmation link (section 4.6, ADR-0162). A <c>GET</c> only shows a button, because mail scanners follow links;
/// the button's <c>POST</c> confirms. The page stands alone, with no script and nothing from elsewhere, and keeps its token out of what
/// any other page gets to read.
/// </summary>
/// <param name="verification">Email verification.</param>
[Route("verify")]
[ApiExplorerSettings(IgnoreApi = true)]
internal sealed class VerifyController(EntryVerification verification) : FormBuilderPublicApiController
{
    private const string ConfirmAction = "confirm";
    private const string ResendAction = "resend";
    private const string FormContentType = "application/x-www-form-urlencoded";

    private const string Style = "*{box-sizing:border-box}html{color-scheme:light dark}body{margin:0;min-height:100dvh;display:grid;place-items:center;padding:16px;background:Canvas;color:CanvasText;font:1rem/1.5 system-ui,sans-serif}main{width:100%;max-width:32rem}h1{margin:0 0 .5rem;font-size:1.5rem;line-height:1.25}p{margin:0 0 1.5rem}button{min-height:44px;padding:.625rem 1.25rem;border:1px solid CanvasText;border-radius:6px;background:CanvasText;color:Canvas;font:inherit;font-weight:600;cursor:pointer}button:focus-visible{outline:3px solid Highlight;outline-offset:2px}";

    private static readonly string StyleHash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(Style)));

    /// <summary>The page of a link: its button, or where the link stands. It changes nothing.</summary>
    /// <param name="t">The link's token.</param>
    /// <param name="cancellationToken">The request's cancellation, with its time limit.</param>
    [HttpGet]
    [FormBuilderRateLimit(FormBuilderRateLimitPolicies.Verify)]
    [FormBuilderBodyLimit(0)]
    [FormBuilderTimeLimit(10)]
    public async Task<IActionResult> Show([FromQuery] string? t, CancellationToken cancellationToken = default) =>
        Page(await verification.ReadAsync(t, cancellationToken), t);

    /// <summary>What the page's button asks: confirming the address, or a new link.</summary>
    /// <param name="cancellationToken">The request's cancellation, with its time limit.</param>
    [HttpPost]
    [FormBuilderRateLimit(FormBuilderRateLimitPolicies.Verify)]
    [FormBuilderBodyLimit(VerificationTokens.MaxLength * 2)]
    [FormBuilderTimeLimit(10)]
    public async Task<IActionResult> Post(CancellationToken cancellationToken = default)
    {
        if (!string.Equals(Request.ContentType?.Split(';')[0].Trim(), FormContentType, StringComparison.OrdinalIgnoreCase))
        {
            return Page(new VerificationAnswer(VerificationStatus.Invalid, string.Empty), null);
        }

        // The public API reads no form by its own means, so nothing else reads a body past its limit (ADR-0012).
        using var reader = new StreamReader(Request.Body, Encoding.UTF8);
        var form = QueryHelpers.ParseQuery(await reader.ReadToEndAsync(cancellationToken));
        var token = form.GetValueOrDefault("t").ToString();
        var answer = form.GetValueOrDefault("action").ToString() == ResendAction
            ? await verification.ResendAsync(token, cancellationToken)
            : await verification.ConfirmAsync(token, cancellationToken);
        return Page(answer, token);
    }

    private ContentResult Page(VerificationAnswer answer, string? token)
    {
        var words = VerificationWords.Of(answer.Culture);
        var (heading, text, action, button) = answer.Status switch
        {
            VerificationStatus.Waiting => (words.WaitingHeading, words.WaitingText, ConfirmAction, words.Button),
            VerificationStatus.Confirmed => (words.ConfirmedHeading, words.ConfirmedText, null, null),
            VerificationStatus.Expired => (words.ExpiredHeading, words.ExpiredText, ResendAction, words.ResendButton),
            VerificationStatus.Resent => (words.ResentHeading, words.ResentText, null, null),
            _ => (words.InvalidHeading, words.InvalidText, (string?)null, (string?)null),
        };
        var form = action is null
            ? string.Empty
            : string.Create(
                CultureInfo.InvariantCulture,
                $"""<form method="post"><input type="hidden" name="t" value="{WebUtility.HtmlEncode(token)}"><input type="hidden" name="action" value="{action}"><button type="submit">{WebUtility.HtmlEncode(button)}</button></form>""");
        var language = answer.Culture.Length > 0 && VerificationWords.Languages.Contains(answer.Culture.Split('-')[0].ToLowerInvariant()) ? answer.Culture.Split('-')[0].ToLowerInvariant() : "en";
        Response.Headers.ContentSecurityPolicy = $"default-src 'none'; style-src 'sha256-{StyleHash}'; form-action 'self'; base-uri 'none'; frame-ancestors 'none'";
        // Not no-referrer: a browser then says its form comes from nowhere, and the public API refuses that (ADR-0012).
        Response.Headers["Referrer-Policy"] = "same-origin";
        Response.Headers["X-Robots-Tag"] = "noindex, nofollow";
        return new ContentResult
        {
            StatusCode = answer.Status == VerificationStatus.Invalid ? StatusCodes.Status404NotFound : StatusCodes.Status200OK,
            ContentType = "text/html; charset=utf-8",
            Content = $"""<!doctype html><html lang="{language}"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><meta name="robots" content="noindex, nofollow"><title>{WebUtility.HtmlEncode(heading)}</title><style>{Style}</style></head><body><main><h1>{WebUtility.HtmlEncode(heading)}</h1><p>{WebUtility.HtmlEncode(text)}</p>{form}</main></body></html>""",
        };
    }
}
