using System.Security.Cryptography;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Jiron.Umbraco.FormBuilder.Testing.Csp;

/// <summary>
/// The Content Security Policy of a test host whose <see cref="FormBuilderTestingOptions.Csp"/> is on (ROADMAP 6.4k,
/// ADR-0004), as a strict site sets it: no inline script, style attribute, plugin or other host. A page's scripts are
/// <c>'self'</c>, which is all a page behind the output cache can carry; a request with the <c>jfb-test-nonce</c> query
/// value gets a nonce of its own instead, which <see cref="TestCspNonces"/> gives <c>&lt;jfb-assets&gt;</c>, with
/// <c>'strict-dynamic'</c> when the value says so, and is never stored, as a page with a nonce must not be.
/// </summary>
internal sealed class TestCspMiddleware(RequestDelegate next)
{
    /// <summary>The query value that asks for a nonce: <c>nonce</c>, or <c>strict-dynamic</c> for the policy with it.</summary>
    public const string NonceQuery = "jfb-test-nonce";

    /// <summary>The value of <see cref="NonceQuery"/> that adds <c>'strict-dynamic'</c>.</summary>
    public const string StrictDynamic = "strict-dynamic";

    /// <summary>Sets the request's policy, and for a nonce keeps the response out of every cache.</summary>
    /// <param name="context">The request.</param>
    public Task InvokeAsync(HttpContext context)
    {
        var asked = context.Request.Query[NonceQuery].ToString();
        var scripts = "'self'";
        if (asked.Length > 0)
        {
            var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
            context.Items[TestCspNonces.ItemKey] = nonce;
            scripts = "'nonce-" + nonce + "'" + (asked == StrictDynamic ? " 'strict-dynamic'" : string.Empty);
            context.Response.Headers.CacheControl = "no-store";
        }

        var policy = "default-src 'self'; script-src " + scripts + "; style-src 'self'; img-src 'self' data:; "
            + "object-src 'none'; base-uri 'self'; form-action 'self'; frame-ancestors 'self'";

        // Set as the response starts, so a page the output cache serves carries it too.
        context.Response.OnStarting(() =>
        {
            context.Response.Headers.ContentSecurityPolicy = policy;
            return Task.CompletedTask;
        });
        return next(context);
    }
}

/// <summary>The request's nonce, which <see cref="TestCspMiddleware"/> made; none for a request that asked for none.</summary>
internal sealed class TestCspNonces : IFormBuilderCspNonceProvider
{
    /// <summary>Where the request keeps its nonce.</summary>
    public const string ItemKey = "Jiron.FormBuilder.Testing.CspNonce";

    /// <inheritdoc />
    public string? GetNonce(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        return httpContext.Items.TryGetValue(ItemKey, out var nonce) ? nonce as string : null;
    }
}

/// <summary>Adds <see cref="TestCspMiddleware"/> in front of the host's pipeline, in the Development environment only.</summary>
internal sealed class TestCspStartupFilter(IOptions<FormBuilderTestingOptions> options, IHostEnvironment environment) : IStartupFilter
{
    /// <inheritdoc />
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        if (options.Value.Csp && environment.IsDevelopment())
        {
            app.UseMiddleware<TestCspMiddleware>();
        }

        next(app);
    };
}
