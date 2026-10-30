using Jiron.Umbraco.FormBuilder.Core.Themes;

namespace Jiron.Umbraco.FormBuilder.Demo;

/// <summary>
/// The theme and the colour scheme the demo shows its forms in (ROADMAP 6.3a, 6.3b, 6.3d): a page asked for with
/// <c>?theme=bootstrap5</c> shows every form on it in that theme, and one asked for with <c>?scheme=dark</c> (or
/// <c>?scheme=auto</c>, dark when the visitor's system asks for it) gives the site its dark design and its forms the
/// dark scheme, through the scheme class on the page's root. The demo keeps each choice in a cookie, so the next pages
/// and a post without JavaScript keep it too; <c>?theme=</c> with no theme the site has goes back to each form's own,
/// and <c>?scheme=</c> with neither value goes back to light. A page in <c>bootstrap5</c> loads Bootstrap, and dark
/// puts Bootstrap in its dark colour mode. Only a theme the site registers counts, so no visitor's word reaches a form
/// or the log. A way to look at the themes in the demo, not a feature of the package: a site names a form's theme in
/// the form, in its General settings or where it places the form, and sets the scheme class itself.
/// </summary>
public static class DemoThemes
{
    /// <summary>The query parameter that picks the theme.</summary>
    public const string Query = "theme";

    /// <summary>The cookie that keeps it.</summary>
    public const string Cookie = "jfb-demo-theme";

    /// <summary>The query parameter that picks the colour scheme: <c>dark</c> or <c>auto</c>.</summary>
    public const string SchemeQuery = "scheme";

    /// <summary>The cookie that keeps it.</summary>
    public const string SchemeCookie = "jfb-demo-scheme";

    /// <summary>
    /// The demo's copy of Bootstrap 5.3.8 (MIT, THIRD-PARTY-NOTICES), which a page whose forms show in <c>bootstrap5</c>
    /// loads before the site's own styles, as a Bootstrap site loads Bootstrap itself (ROADMAP 6.3d, ADR-0108). Beside it
    /// sits Bootstrap's right-to-left build, <c>bootstrap.rtl.min.css</c>, for a page set right to left.
    /// </summary>
    public const string Bootstrap = "/demo/vendor/bootstrap-5.3.8/bootstrap.min.css";

    /// <summary>The theme that needs Bootstrap on its page.</summary>
    public const string BootstrapTheme = "bootstrap5";

    // The request's choices once the middleware has read them: a theme's alias or a scheme, or empty for none.
    private const string Chosen = "Jiron.FormBuilder.Demo.Theme";
    private const string ChosenScheme = "Jiron.FormBuilder.Demo.Scheme";

    /// <summary>The theme this request's forms render in, or null for each form's own.</summary>
    /// <param name="context">The request.</param>
    public static string? Of(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var name = context.Items.TryGetValue(Chosen, out var chosen) ? chosen as string : context.Request.Cookies[Cookie];
        return Known(context, name) ? name : null;
    }

    /// <summary>Whether this request's page loads Bootstrap: its forms show in <c>bootstrap5</c>.</summary>
    /// <param name="context">The request.</param>
    public static bool LoadsBootstrap(HttpContext context) => Of(context) == BootstrapTheme;

    /// <summary>The colour scheme this request's page shows, <c>dark</c> or <c>auto</c>, or null for light.</summary>
    /// <param name="context">The request.</param>
    public static string? SchemeOf(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var scheme = context.Items.TryGetValue(ChosenScheme, out var chosen) ? chosen as string : context.Request.Cookies[SchemeCookie];
        return IsScheme(scheme) ? scheme : null;
    }

    /// <summary>Reads <c>?theme=</c> and <c>?scheme=</c> on every request and keeps each in its cookie, or drops it for none.</summary>
    /// <param name="app">The demo's pipeline.</param>
    public static IApplicationBuilder UseDemoThemes(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        Remember(context, Query, Cookie, Chosen, name => Known(context, name));
        Remember(context, SchemeQuery, SchemeCookie, ChosenScheme, IsScheme);
        await next(context);
    });

    private static void Remember(HttpContext context, string query, string cookie, string item, Func<string, bool> valid)
    {
        if (!context.Request.Query.TryGetValue(query, out var values))
        {
            return;
        }

        var value = values.ToString();
        if (valid(value))
        {
            context.Response.Cookies.Append(cookie, value, new CookieOptions { HttpOnly = true, IsEssential = true, SameSite = SameSiteMode.Lax, Secure = context.Request.IsHttps });
            context.Items[item] = value;
        }
        else
        {
            context.Response.Cookies.Delete(cookie);
            context.Items[item] = string.Empty;
        }
    }

    private static bool IsScheme(string? scheme) => scheme is "dark" or "auto";

    private static bool Known(HttpContext context, string? name) =>
        !string.IsNullOrEmpty(name) && context.RequestServices.GetRequiredService<FormBuilderThemeCollection>().Any(theme => theme.Name == name);
}
