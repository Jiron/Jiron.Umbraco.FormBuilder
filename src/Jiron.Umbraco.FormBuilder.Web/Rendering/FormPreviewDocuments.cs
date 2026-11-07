using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Forms;
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

/// <summary>What the preview's document is asked for (ADR-0203, ADR-0204).</summary>
/// <param name="Culture">A language of the site.</param>
/// <param name="Dark">Whether the page is dark, when the theme offers the dark scheme.</param>
/// <param name="Sample">Whether the pages start with the sample answers.</param>
/// <param name="RunsActions">Whether the test entries the document sends run their actions, as its ticket says.</param>
internal sealed record FormPreviewChoices(string Culture, bool Dark = false, bool Sample = false, bool RunsActions = false);

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
/// back to a post. Fill with sample answers renders the sample entry's answers as every page's starting values, as a
/// page renders a post's answers, and says so to the preview script, which adds the sample files (ADR-0204). Nothing
/// loads from another host. None of its records prints its ticket.
/// </summary>
/// <param name="rendering">What a page renders for a form.</param>
/// <param name="assets">The tags' addresses of a form's theme.</param>
/// <param name="files">The website assets, among them the preview script.</param>
/// <param name="tickets">The preview's tickets.</param>
/// <param name="paths">The public API's paths, which the preview script lends the ticket to.</param>
/// <param name="engine">The site's view engine.</param>
/// <param name="tempData">The site's temp data.</param>
/// <param name="previews">The versions the preview shows, whose sample answers it renders.</param>
/// <param name="samples">The sample entry's answers.</param>
internal sealed class FormPreviewDocuments(
    FormRendering rendering,
    PageAssets assets,
    WebsiteAssets files,
    PreviewTickets tickets,
    FormBuilderPublicApiPaths paths,
    ICompositeViewEngine engine,
    ITempDataProvider tempData,
    PreviewForms previews,
    SampleEntries samples)
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
    /// <param name="choices">The language, the colour scheme, the sample answers and whether its test entries run their actions.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <exception cref="InvalidOperationException">The form's view cannot be found.</exception>
    public async Task<FormPreviewResult> RenderAsync(ActionContext action, Guid form, string name, int version, FormPreviewChoices choices, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(choices);
        var http = action.HttpContext;
        var culture = choices.Culture;
        FormRenderTargets.Set(http, new FormRenderTarget(Instance, "/", null, culture, Preview: version));
        if (choices.Sample && await previews.GetAsync(form, version, cancellationToken) is { } sampled)
        {
            var definition = sampled.Published.Definition;
            FormPosts.Set(http, form, Instance, Sampled(definition, await samples.PostedAsync(definition, sampled.Published.Form.Name, sampled.Published.Form.Alias, culture, cancellationToken)));
        }

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
        var ticket = tickets.Issue(form, version, choices.RunsActions);
        var addresses = assets.Addresses(http.Request.PathBase, theme);
        var shownDark = choices.Dark && theme.DarkScheme;
        return new FormPreviewResult(FormPreviewStatus.Done, new FormPreviewDocument(
            Document(markup, name, addresses, files.Url(http.Request.PathBase, WebsiteAssets.Preview), (http.Request.PathBase + new PathString(paths.Root)).ToUriComponent(), ticket, cultureInfo, shownDark, choices.Sample),
            theme.DarkScheme,
            ticket));
    }

    // The sample answers as a post leaves them for the page it renders again (ADR-0100): a repeatable group's fields as
    // its one row's, the others by their keys; the first page shows (ADR-0204).
    private static FormPostState Sampled(FormDefinition definition, IReadOnlyDictionary<Guid, FieldAnswers> posted)
    {
        var answers = new Dictionary<Guid, FieldAnswers>();
        var rows = new Dictionary<Guid, IReadOnlyList<FormPostRow>>();
        foreach (var field in definition.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields))
        {
            if (field.Fields.Count > 0)
            {
                rows[field.Key] = [new FormPostRow { Answers = field.Fields.Where(child => posted.ContainsKey(child.Key)).ToDictionary(child => child.Key, child => posted[child.Key]) }];
            }
            else if (posted.TryGetValue(field.Key, out var given))
            {
                answers[field.Key] = given;
            }
        }

        return new FormPostState { Answers = answers, Rows = rows };
    }

    // The document: its title, the stylesheets, the ground, the preview script before the runtime, so the runtime's
    // first request carries the ticket, and the form.
    private static string Document(string markup, string name, ElementAssets addresses, string previewScript, string api, PreviewTicket ticket, CultureInfo culture, bool dark, bool sample)
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
            .Append("\" data-lifetime=\"").Append(((int)PreviewTickets.Lifetime.TotalSeconds).ToString(CultureInfo.InvariantCulture)).Append('"');
        if (sample)
        {
            // The preview script gives each file field a sample file once the form has woken (ADR-0204).
            html.Append(" data-sample=\"true\"");
        }

        html.Append('>');
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
