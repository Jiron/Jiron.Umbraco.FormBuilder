using Jiron.Umbraco.FormBuilder.Core.Themes;

namespace Jiron.Umbraco.FormBuilder.Demo;

/// <summary>
/// The theme the demo shows its forms in (ROADMAP 6.3a): a page asked for with <c>?theme=bootstrap5</c> shows every form
/// on it in that theme, and the demo keeps the choice in a cookie, so the next pages and a post without JavaScript keep
/// it too; <c>?theme=</c> with no theme the site has goes back to each form's own. Only a theme the site registers
/// counts, so no visitor's word reaches a form or the log. A way to look at the themes in the demo, not a feature of the
/// package: a site names a form's theme in the form, in its General settings or where it places the form.
/// </summary>
public static class DemoThemes
{
    /// <summary>The query parameter that picks the theme.</summary>
    public const string Query = "theme";

    /// <summary>The cookie that keeps it.</summary>
    public const string Cookie = "jfb-demo-theme";

    // The request's choice once the middleware has read it: a theme's alias, or empty for each form's own.
    private const string Chosen = "Jiron.FormBuilder.Demo.Theme";

    /// <summary>The theme this request's forms render in, or null for each form's own.</summary>
    /// <param name="context">The request.</param>
    public static string? Of(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var name = context.Items.TryGetValue(Chosen, out var chosen) ? chosen as string : context.Request.Cookies[Cookie];
        return Known(context, name) ? name : null;
    }

    /// <summary>Reads <c>?theme=</c> on every request and keeps it in the cookie, or drops the cookie for none.</summary>
    /// <param name="app">The demo's pipeline.</param>
    public static IApplicationBuilder UseDemoThemes(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        if (context.Request.Query.TryGetValue(Query, out var values))
        {
            var name = values.ToString();
            if (Known(context, name))
            {
                context.Response.Cookies.Append(Cookie, name, new CookieOptions { HttpOnly = true, IsEssential = true, SameSite = SameSiteMode.Lax, Secure = context.Request.IsHttps });
                context.Items[Chosen] = name;
            }
            else
            {
                context.Response.Cookies.Delete(Cookie);
                context.Items[Chosen] = string.Empty;
            }
        }

        await next(context);
    });

    private static bool Known(HttpContext context, string? name) =>
        !string.IsNullOrEmpty(name) && context.RequestServices.GetRequiredService<FormBuilderThemeCollection>().Any(theme => theme.Name == name);
}
