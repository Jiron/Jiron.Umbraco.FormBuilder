using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using Jiron.Umbraco.FormBuilder.Logic;
using Jiron.Umbraco.FormBuilder.Web.PublicApi;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

/// <summary>Whether the preview's document was written (ADR-0203).</summary>
internal enum FormPreviewStatus
{
    /// <summary>The document holds the form.</summary>
    Done,

    /// <summary>The form or the version is missing, in the trash or unreadable.</summary>
    NotFound,

    /// <summary>The version's rules fail the check both runtimes run before they evaluate (ADR-0002), so none is evaluated.</summary>
    RulesInvalid,
}

/// <summary>The preview's document (ADR-0203).</summary>
/// <param name="Html">The whole document, which the backoffice writes into the sandboxed frame.</param>
/// <param name="DarkScheme">Whether the form's theme offers the dark scheme, which the preview may switch to.</param>
/// <param name="Ticket">The ticket the document holds.</param>
internal sealed record FormPreviewDocument(string Html, bool DarkScheme, PreviewTicket Ticket)
{
    /// <inheritdoc />
    public override string ToString() => nameof(FormPreviewDocument);
}

/// <summary>A preview's document, or why there is none.</summary>
/// <param name="Status">Whether it was written.</param>
/// <param name="Document">The document, when it was.</param>
internal sealed record FormPreviewResult(FormPreviewStatus Status, FormPreviewDocument? Document = null)
{
    /// <inheritdoc />
    public override string ToString() => nameof(FormPreviewResult);
}

/// <summary>
/// The preview's documents (ADR-0203): a version of a form rendered as a page of the site renders it, by the same view
/// component's service, as though it were published, for a test member: its theme's views, the culture asked for, the
/// base rules and the theme's stylesheet, the runtime, and the preview script with the ticket that lets the sandboxed
/// frame call the public API. The page is titled with the form's name, its ground follows the colour scheme, and the
/// form and its buttons have no address to post to, as the headless element's (ADR-0128), so the runtime never falls
/// back to a post. Nothing loads from another host. None of its records prints its ticket.
/// </summary>
/// <param name="rendering">What a page renders for a form.</param>
/// <param name="assets">The tags' addresses of a form's theme.</param>
/// <param name="files">The website assets, among them the preview script.</param>
/// <param name="tickets">The preview's tickets.</param>
/// <param name="paths">The public API's paths, which the preview script lends the ticket to.</param>
/// <param name="engine">The site's view engine.</param>
/// <param name="tempData">The site's temp data.</param>
internal sealed class FormPreviewDocuments(
    FormRendering rendering,
    PageAssets assets,
    WebsiteAssets files,
    PreviewTickets tickets,
    FormBuilderPublicApiPaths paths,
    ICompositeViewEngine engine,
    ITempDataProvider tempData)
{
    /// <summary>The name of the meta element that holds the ticket, which the preview script reads.</summary>
    public const string TicketMeta = "jfb-preview";

    /// <summary>The class of the document's root in the dark scheme (ADR-0106).</summary>
    public const string DarkClass = "jfb-scheme-dark";

    /// <summary>The instance id of the preview's one form.</summary>
    public const string Instance = "jfb-1";

    // The page's ground: the browser's own colours in the scheme shown, said outright so a check of the page reads them
    // as the browser paints them, the system's sans-serif where a site's page has its own font, and room around the form
    // that shrinks on a phone.
    private const string Ground = "html{color-scheme:light;background:Canvas;color:CanvasText}html." + DarkClass + "{color-scheme:dark}body{margin:0;padding:clamp(16px,4vw,40px);font-family:system-ui,sans-serif}";

    /// <summary>The version of the form as the preview shows it.</summary>
    /// <param name="action">The request the views write for.</param>
    /// <param name="form">The form's key.</param>
    /// <param name="name">The form's name, which titles the page.</param>
    /// <param name="version">The number of the version, the draft as it was saved.</param>
    /// <param name="culture">A language of the site.</param>
    /// <param name="dark">Whether the page is dark, when the theme offers the dark scheme.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <exception cref="InvalidOperationException">The form's view cannot be found.</exception>
    public async Task<FormPreviewResult> RenderAsync(ActionContext action, Guid form, string name, int version, string culture, bool dark, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        var http = action.HttpContext;
        FormRenderTargets.Set(http, new FormRenderTarget(Instance, "/", null, culture, Preview: version));
        RenderedForm? rendered;
        try
        {
            rendered = await rendering.RenderAsync(http, form, null, null, cancellationToken);
        }
        catch (LogicException)
        {
            return new FormPreviewResult(FormPreviewStatus.RulesInvalid);
        }

        if (rendered is not { Theme: { } theme } shown)
        {
            return new FormPreviewResult(FormPreviewStatus.NotFound);
        }

        var found = engine.GetView(null, shown.View, isMainPage: false);
        if (!found.Success)
        {
            throw new InvalidOperationException($"The view '{shown.View}' cannot be found.");
        }

        // Written in the culture previewed, as a page in it is: a site's own view may read the dictionary or format a date.
        var cultureInfo = CultureInfo.GetCultureInfo(culture);
        var markup = await RazorViewText.WriteAsync(found.View, action, shown.Model, tempData, cultureInfo);
        var ticket = tickets.Issue(form, version);
        var addresses = assets.Addresses(http.Request.PathBase, theme);
        var shownDark = dark && theme.DarkScheme;
        return new FormPreviewResult(FormPreviewStatus.Done, new FormPreviewDocument(
            Document(markup, name, addresses, files.Url(http.Request.PathBase, WebsiteAssets.Preview), (http.Request.PathBase + new PathString(paths.Root)).ToUriComponent(), ticket, cultureInfo, shownDark),
            theme.DarkScheme,
            ticket));
    }

    // The document: its title, the stylesheets, the ground, the preview script before the runtime, so the runtime's
    // first request carries the ticket, and the form.
    private static string Document(string markup, string name, ElementAssets addresses, string previewScript, string api, PreviewTicket ticket, CultureInfo culture, bool dark)
    {
        var encoder = HtmlEncoder.Default;
        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"").Append(encoder.Encode(culture.Name)).Append("\" dir=\"").Append(culture.TextInfo.IsRightToLeft ? "rtl" : "ltr").Append('"');
        if (dark)
        {
            html.Append(" class=\"").Append(DarkClass).Append('"');
        }

        html.Append("><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        html.Append("<title>").Append(encoder.Encode(name)).Append("</title>");
        html.Append("<meta name=\"").Append(TicketMeta).Append("\" content=\"").Append(encoder.Encode(ticket.Value))
            .Append("\" data-form=\"").Append(ticket.FormKey.ToString("D"))
            .Append("\" data-api=\"").Append(encoder.Encode(api))
            .Append("\" data-lifetime=\"").Append(((int)PreviewTickets.Lifetime.TotalSeconds).ToString(CultureInfo.InvariantCulture)).Append("\">");
        foreach (var style in addresses.Styles)
        {
            html.Append("<link rel=\"stylesheet\" href=\"").Append(encoder.Encode(style)).Append("\">");
        }

        html.Append("<style>").Append(Ground).Append("</style>");
        html.Append("<script type=\"module\" src=\"").Append(encoder.Encode(previewScript)).Append("\"></script>");
        html.Append("<script type=\"module\" src=\"").Append(encoder.Encode(addresses.Script)).Append("\"></script>");
        html.Append("</head><body>").Append(markup.Trim()).Append("</body></html>");
        return html.ToString();
    }
}
