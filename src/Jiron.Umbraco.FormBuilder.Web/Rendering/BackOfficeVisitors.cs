using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core;
using Umbraco.Extensions;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

/// <summary>
/// Whether a page's request comes from a signed-in backoffice user (ADR-0033, ADR-0095). Umbraco authenticates its
/// backoffice cookie only on backoffice paths, so on a website page this reads it as Umbraco's own preview does: the
/// cookie of Umbraco's backoffice scheme, its chunks joined when it is large, unprotected with that scheme's ticket
/// format, unexpired, and holding a backoffice identity. It signs nobody in and writes no cookie; what it decides is
/// only whether the note while the migrations are pending shows.
/// </summary>
/// <param name="cookies">The cookie options of each scheme, of which the backoffice's.</param>
/// <param name="time">The clock a ticket's expiry is read against.</param>
internal sealed class BackOfficeVisitors(IOptionsMonitor<CookieAuthenticationOptions> cookies, TimeProvider time)
{
    // Joins a cookie the browser sends in chunks, as ASP.NET Core's cookie authentication writes a large one.
    private readonly ChunkingCookieManager _chunks = new();

    /// <summary>Whether the request carries a backoffice user's unexpired sign-in.</summary>
    /// <param name="httpContext">The request.</param>
    public bool IsSignedIn(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        var options = cookies.Get(Constants.Security.BackOfficeAuthenticationType);
        if (options.Cookie.Name is not { Length: > 0 } name || _chunks.GetRequestCookie(httpContext, name) is not { Length: > 0 } cookie)
        {
            return false;
        }

        return options.TicketDataFormat.Unprotect(cookie) is { } ticket
            && (ticket.Properties.ExpiresUtc is not { } expires || expires > time.GetUtcNow())
            && ticket.Principal.GetUmbracoIdentity() is not null;
    }
}
