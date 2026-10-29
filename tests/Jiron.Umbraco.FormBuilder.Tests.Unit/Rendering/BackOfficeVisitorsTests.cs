using System.Security.Claims;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NSubstitute;
using Umbraco.Cms.Core;
using Umbraco.Extensions;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Rendering;

/// <summary>
/// Whether a page's request comes from a signed-in backoffice user (ADR-0095): only a ticket of the backoffice's cookie
/// scheme counts, read with that scheme's format, unexpired and holding a backoffice identity; a large cookie in chunks
/// counts as one, and anything else, a cookie of another name among them, does not.
/// </summary>
[TestFixture]
public sealed class BackOfficeVisitorsTests
{
    private const string CookieName = "UMB_UCONTEXT";

    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    private readonly TicketDataFormat _format = new(new EphemeralDataProtectionProvider().CreateProtector("jfb-backoffice-test"));

    [Test]
    public void ABackofficeUsersUnexpiredTicket_IsASignedInUser()
    {
        Assert.That(Visitors().IsSignedIn(Request(_format.Protect(Ticket(Now.AddMinutes(20))))), Is.True);
    }

    [Test]
    public void ATicketWithoutAnExpiry_IsASignedInUser()
    {
        Assert.That(Visitors().IsSignedIn(Request(_format.Protect(Ticket(expires: null)))), Is.True);
    }

    [Test]
    public void AnExpiredTicket_IsNone()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Visitors().IsSignedIn(Request(_format.Protect(Ticket(Now.AddSeconds(-1))))), Is.False);
            Assert.That(Visitors().IsSignedIn(Request(_format.Protect(Ticket(Now)))), Is.False, "a ticket expires at its time");
        });
    }

    [Test]
    public void ATicketOfAnotherProtector_OrNoTicketAtAll_IsNone()
    {
        var other = new TicketDataFormat(new EphemeralDataProtectionProvider().CreateProtector("jfb-backoffice-test"));

        Assert.Multiple(() =>
        {
            Assert.That(Visitors().IsSignedIn(Request(other.Protect(Ticket(Now.AddMinutes(20))))), Is.False, "another key ring's ticket does not unprotect");
            Assert.That(Visitors().IsSignedIn(Request("not-a-ticket")), Is.False);
            Assert.That(Visitors().IsSignedIn(new DefaultHttpContext()), Is.False, "no cookie");
            Assert.That(Visitors().IsSignedIn(Request(_format.Protect(Ticket(Now.AddMinutes(20))), name: "UMB_OTHER")), Is.False, "a cookie of another name");
        });
    }

    [Test]
    public void ATicketWithoutABackofficeIdentity_IsNone()
    {
        var member = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "reader")], "UmbracoMember"));
        var ticket = new AuthenticationTicket(member, new AuthenticationProperties { ExpiresUtc = Now.AddMinutes(20) }, Constants.Security.BackOfficeAuthenticationType);

        Assert.That(Visitors().IsSignedIn(Request(_format.Protect(ticket))), Is.False);
    }

    [Test]
    public void ALargeCookieInChunks_IsJoined()
    {
        var protectedTicket = _format.Protect(Ticket(Now.AddMinutes(20)));
        var half = protectedTicket.Length / 2;
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = $"{CookieName}=chunks-2; {CookieName}C1={protectedTicket[..half]}; {CookieName}C2={protectedTicket[half..]}";

        Assert.That(Visitors().IsSignedIn(context), Is.True);
    }

    [Test]
    public void WithoutABackofficeCookieName_NoRequestIsASignedInUser()
    {
        Assert.That(Visitors(name: null).IsSignedIn(Request(_format.Protect(Ticket(Now.AddMinutes(20))))), Is.False);
    }

    [Test]
    public void WithoutARequest_TheQuestionIsRefused() =>
        Assert.That(() => Visitors().IsSignedIn(null!), Throws.ArgumentNullException);

    private BackOfficeVisitors Visitors(string? name = CookieName)
    {
        var options = Substitute.For<IOptionsMonitor<CookieAuthenticationOptions>>();
        var cookie = new CookieAuthenticationOptions { TicketDataFormat = _format };
        if (name is not null)
        {
            cookie.Cookie.Name = name;
        }

        options.Get(Constants.Security.BackOfficeAuthenticationType).Returns(cookie);
        return new BackOfficeVisitors(options, new TestTimeProvider(Now));
    }

    // A backoffice user's ticket, as Umbraco's sign-in writes one.
    private static AuthenticationTicket Ticket(DateTimeOffset? expires)
    {
        var identity = new ClaimsIdentity(Constants.Security.BackOfficeAuthenticationType);
        identity.AddRequiredClaims("-1", Constants.Security.SuperUserKey, "admin@jfb.test", "Administrator", [-1], [-1], "en-US", "stamp", ["content"], ["admin"]);
        return new AuthenticationTicket(new ClaimsPrincipal(identity), new AuthenticationProperties { ExpiresUtc = expires }, Constants.Security.BackOfficeAuthenticationType);
    }

    private static DefaultHttpContext Request(string value, string name = CookieName)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = $"{name}={value}";
        return context;
    }
}
