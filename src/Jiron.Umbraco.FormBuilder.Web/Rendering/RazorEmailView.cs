using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.Email;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

/// <summary>
/// Writes a mail's body with a view in <c>Views/JironFormBuilder/Emails</c> (ROADMAP 8.3, ADR-0160). It runs on the worker, so it
/// has no request of its own and makes one with a scope of its own. What it writes goes on as any body does, through the mail's
/// sanitiser and layout. It asks for the view engine only when a mail names a view, as the action types are made at start-up.
/// </summary>
/// <param name="services">Where a view's scope and the site's view engine come from.</param>
internal sealed class RazorEmailView(IServiceScopeFactory services) : IFormBuilderEmailView
{
    /// <summary>Where the site's views of mail are.</summary>
    public const string Folder = "~/Views/JironFormBuilder/Emails/";

    /// <inheritdoc />
    public async Task<string?> RenderAsync(string view, EmailViewModel model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(model);
        if (!EmailViewNames.IsName(view))
        {
            return null;
        }

        await using var scope = services.CreateAsyncScope();

        // Not a main page, so a site's _ViewStart and its layout stay out of the mail.
        var found = scope.ServiceProvider.GetRequiredService<ICompositeViewEngine>().GetView(null, Folder + view + ".cshtml", isMainPage: false);
        if (!found.Success)
        {
            return null;
        }

        // Dates and texts the view writes follow the mail; the worker's thread gets its own back as this method ends.
        if (CultureOf(model.Culture) is { } culture)
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }

        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider, RequestAborted = cancellationToken };
        var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
        return await RazorViewText.WriteAsync(found.View, action, model, scope.ServiceProvider.GetRequiredService<ITempDataProvider>());
    }

    private static CultureInfo? CultureOf(string culture)
    {
        try
        {
            return CultureInfo.GetCultureInfo(culture);
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }
}
