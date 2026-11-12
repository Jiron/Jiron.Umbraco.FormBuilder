using System.Globalization;
using System.Net;
using Jiron.Umbraco.FormBuilder.Core;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

/// <summary>
/// What a page shows where its form failed while it rendered (ROADMAP 16.4, ADR-0237): nothing for a visitor, so the page
/// and its other forms render, and for a signed-in backoffice user a short note that names the exception's type. The page
/// is kept out of every cache, so its next view renders the form again, and the log gets the exception's types and stack,
/// never its message (ADR-0226), with the reference the page gave.
/// </summary>
/// <param name="backOffice">Whether the request comes from a signed-in backoffice user.</param>
/// <param name="logger">Where the failure is logged.</param>
internal sealed partial class FormRenderFailures(BackOfficeVisitors backOffice, ILogger<FormRenderFailures> logger)
{
    // How much of a reference the log keeps.
    private const int ReferenceLength = 100;

    /// <summary>Whether a failure is one a page outlives: anything but a request the visitor left, or a lack of memory.</summary>
    /// <param name="exception">What rendering threw.</param>
    /// <param name="aborted">The request's cancellation.</param>
    public static bool Caught(Exception exception, CancellationToken aborted) =>
        exception is not OutOfMemoryException && !(exception is OperationCanceledException && aborted.IsCancellationRequested);

    /// <summary>What shows in the form's place: the note for a signed-in backoffice user, null for anyone else.</summary>
    /// <param name="httpContext">The request.</param>
    /// <param name="form">The reference the page gave.</param>
    /// <param name="exception">What rendering threw.</param>
    public IHtmlContent? InPlace(HttpContext httpContext, object? form, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);
        PageCaching.NoStore(httpContext);
        LogFailed(logger, Reference(form), LoggedException.Of(exception));
        if (!backOffice.IsSignedIn(httpContext))
        {
            return null;
        }

        // Written here, not by a view: a view may be what failed.
        var words = RenderingWords.Of(CultureInfo.CurrentUICulture.Name);
        var text = string.Format(CultureInfo.InvariantCulture, words.BrokenText, exception.GetType().FullName);
        return new HtmlString($"<div class=\"jfb-notice\" data-jfb-broken><h2>{WebUtility.HtmlEncode(words.BrokenHeading)}</h2><p>{WebUtility.HtmlEncode(text)}</p></div>");
    }

    // The reference as the page gave it: a key, an alias, or a value's type.
    private static string Reference(object? form) => form switch
    {
        null => "(none)",
        Guid key => key.ToString("D"),
        FormBuilderFormReference placed => placed.Key.ToString("D"),
        string text => text.Trim() is { Length: > ReferenceLength } cut ? cut[..ReferenceLength] : text.Trim(),
        _ => form.GetType().FullName ?? form.GetType().Name,
    };

    [LoggerMessage(Level = LogLevel.Error, Message = "Jiron Form Builder could not render form {Form}; the page shows nothing in its place to visitors and is kept out of caches")]
    private static partial void LogFailed(ILogger logger, string form, LoggedException exception);
}
