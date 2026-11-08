using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Projection;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Themes;
using Jiron.Umbraco.FormBuilder.Web.PublicApi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

/// <summary>
/// The theme editor's sample page (ADR-0215): a sample form with a field of each common kind, help, a placeholder, an
/// answer its check refuses with the error summary, and both buttons, rendered as a page of the site renders a form, by
/// the same view models and the base theme's views and class map, under the preview's alias, so the layer the editor
/// writes into the page styles it as the website styles a form in the theme. The page links the base rules and the base
/// theme's stylesheet and holds an empty style element for the layer, has no script, and its form has no address to post
/// to, so the editor's frame shows it with scripts off and changes only the layer as the editor changes the theme.
/// </summary>
/// <param name="registry">The themes in code a library theme builds on.</param>
/// <param name="requests">What the form endpoint answers, which the model data is.</param>
/// <param name="pages">The page the form is shown on: none.</param>
/// <param name="models">The view models.</param>
/// <param name="types">The field types, which the public projection reads.</param>
/// <param name="assets">The stylesheets of the base theme.</param>
/// <param name="engine">The site's view engine.</param>
/// <param name="tempData">The site's temp data.</param>
internal sealed class ThemePreviewDocuments(
    FormBuilderThemes registry,
    FormRequests requests,
    FormPages pages,
    FormModels models,
    FieldTypeRegistry types,
    PageAssets assets,
    ICompositeViewEngine engine,
    ITempDataProvider tempData)
{
    /// <summary>The id of the style element the editor writes the layer into.</summary>
    public const string LayerId = "jfb-theme-layer";

    /// <summary>The class on the page's root that shows the dark scheme, as the form preview's (ADR-0203).</summary>
    public const string DarkClass = FormPreviewDocuments.DarkClass;

    private const string Instance = "jfb-1";

    // The page's ground, light or dark as the root's class says, as the form preview's.
    private const string Ground = "html{color-scheme:light;background:Canvas;color:CanvasText}html." + DarkClass + "{color-scheme:dark}body{margin:0;padding:clamp(16px,4vw,40px);font-family:system-ui,sans-serif}";

    private static readonly Guid FormKey = new("6a3f0e52-91c4-4b7d-8e15-0c2d7f4a9b61");

    /// <summary>The sample page in the base theme and the culture; null for a base theme the site lacks.</summary>
    /// <param name="action">The request the views write for.</param>
    /// <param name="baseTheme">The alias of the theme in code the theme builds on.</param>
    /// <param name="culture">The culture of the sample's texts and of the visitor's words, such as <c>de-DE</c>.</param>
    /// <param name="cancellationToken">Cancels the reads.</param>
    /// <exception cref="InvalidOperationException">The form's view cannot be found.</exception>
    public async Task<string?> RenderAsync(ActionContext action, string baseTheme, string culture, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(baseTheme);
        ArgumentNullException.ThrowIfNull(culture);
        if (registry.Find(baseTheme) is not { } theme)
        {
            return null;
        }

        var http = action.HttpContext;
        var sample = Sample(culture);
        var now = DateTimeOffset.UnixEpoch;
        var form = new FormInfo(FormKey, "theme-sample", sample.Name, null, 1, true, 1, false, null, now, now, null, null);
        var cached = new CachedForm(new PublishedForm(form, sample.Definition, 1), PublicProjection.Create(sample.Definition, types));
        FormPosts.Set(http, FormKey, Instance, sample.Post);
        var shown = await requests.ShowAsync(cached, culture, await pages.PageAsync(null), cancellationToken);
        var drawn = new LibraryTheme(new StoredTheme(0, Guid.Empty, ThemeLibrary.PreviewAlias, sample.Name, theme.Name, "{}", null, 0, now, now), theme);
        var (view, model) = await models.BuildAsync(new FormRender(http, cached, shown, culture, Instance, new Dictionary<string, string?>(), drawn, Preview: true), cancellationToken);
        var found = engine.GetView(null, view, isMainPage: false);
        if (!found.Success)
        {
            throw new InvalidOperationException($"The view '{view}' cannot be found.");
        }

        var cultureInfo = CultureInfo.GetCultureInfo(culture);
        var markup = await RazorViewText.WriteAsync(found.View, action, model, tempData, cultureInfo);
        return Document(markup, sample.Name, assets.Addresses(http.Request.PathBase, theme).Styles, cultureInfo);
    }

    /// <summary>
    /// The sample form in the culture's language, English or German: its second page of two, so both buttons show, with
    /// a text, an email address with help and a placeholder, a number whose answer is out of range, a dropdown, radio
    /// buttons, checkboxes with one ticked, a date, a long text and a single checkbox, and the answers a visitor gave.
    /// </summary>
    /// <param name="culture">The culture, such as <c>de-DE</c>.</param>
    internal static ThemeSample Sample(string culture)
    {
        var german = culture.StartsWith("de", StringComparison.OrdinalIgnoreCase);
        string Say(string english, string deutsch) => german ? deutsch : english;
        LocalizedText Text(string english, string deutsch) => new(Say(english, deutsch));
        FieldOption Option(string value, string english, string deutsch) => new() { Value = value, Label = Text(english, deutsch) };

        var first = new FormPage { Key = Key(1), Title = Text("About you", "Zu Ihnen"), Groups = [new FormGroup { Key = Key(2), Fields = [] }] };
        var name = new FormField { Key = Key(10), Type = "text", Alias = "name", Label = Text("Name", "Name"), Required = true };
        var email = new FormField
        {
            Key = Key(11),
            Type = "email",
            Alias = "email",
            Label = Text("Email address", "E-Mail-Adresse"),
            Help = Text("We send the confirmation here.", "Hierhin senden wir die Best\u00e4tigung."),
            Placeholder = Text("name@example.com", "name@beispiel.de"),
            Required = true,
        };
        var guests = new FormField { Key = Key(12), Type = "number", Alias = "guests", Label = Text("Guests", "G\u00e4ste"), Settings = SettingsObject.Parse("""{"min":"1","max":"4"}""") };
        var session = new FormField
        {
            Key = Key(13),
            Type = "dropdown",
            Alias = "session",
            Label = Text("Session", "Termin"),
            Options = new FieldOptions { Items = [Option("morning", "Morning", "Vormittag"), Option("afternoon", "Afternoon", "Nachmittag")] },
        };
        var heard = new FormField
        {
            Key = Key(14),
            Type = "radio",
            Alias = "heard",
            Label = Text("How did you hear about us?", "Wie haben Sie von uns erfahren?"),
            Options = new FieldOptions { Items = [Option("friends", "From friends", "Von Bekannten"), Option("press", "In the newspaper", "Aus der Zeitung"), Option("online", "Online", "Im Internet")] },
        };
        var wishes = new FormField
        {
            Key = Key(15),
            Type = "checkboxes",
            Alias = "wishes",
            Label = Text("Wishes", "W\u00fcnsche"),
            Options = new FieldOptions { Items = [Option("vegetarian", "Vegetarian menu", "Vegetarisches Men\u00fc"), Option("access", "Wheelchair access needed", "Rollstuhlgerechter Zugang")] },
        };
        var date = new FormField { Key = Key(16), Type = "date", Alias = "date", Label = Text("Date", "Datum") };
        var message = new FormField { Key = Key(17), Type = "textarea", Alias = "message", Label = Text("Message", "Nachricht"), Placeholder = Text("Anything we should know?", "M\u00f6chten Sie uns noch etwas sagen?") };
        var news = new FormField { Key = Key(18), Type = "checkbox", Alias = "news", Label = Text("Send me news about events", "Senden Sie mir Neuigkeiten zu Veranstaltungen") };
        var second = new FormPage
        {
            Key = Key(3),
            Title = Text("Your visit", "Ihr Besuch"),
            Groups = [new FormGroup { Key = Key(4), Fields = [name, email, guests, session, heard, wishes, date, message, news] }],
        };
        var definition = new FormDefinition { Key = Key(0), Pages = [first, second], Outcomes = [new FormOutcome { Key = Key(5) }] };
        var post = new FormPostState
        {
            Answers = new Dictionary<Guid, FieldAnswers>
            {
                [name.Key] = new(["Alex Sample"]),
                [guests.Key] = new(["7"]),
                [session.Key] = new(["afternoon"]),
                [heard.Key] = new(["friends"]),
                [wishes.Key] = new(["vegetarian"]),
            },
            Errors = new Dictionary<Guid, FormPostError> { [guests.Key] = new("max") },
            Page = second.Key,
            ErrorPage = second.Key,
        };
        return new ThemeSample(Say("Sign up for the open day", "Anmeldung zum Tag der offenen T\u00fcr"), definition, post);
    }

    private static Guid Key(int number) => Guid.Parse($"6a3f0e52-0000-4000-8000-{number:D12}");

    // The page: its title, the base rules and the base theme's stylesheet, the layer's empty style element after them,
    // the ground, and the form.
    private static string Document(string markup, string name, IReadOnlyList<string> styles, CultureInfo culture)
    {
        var encoder = HtmlEncoder.Default;
        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"").Append(encoder.Encode(culture.Name)).Append("\" dir=\"").Append(culture.TextInfo.IsRightToLeft ? "rtl" : "ltr").Append("\">");
        html.Append("<head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        html.Append("<title>").Append(encoder.Encode(name)).Append("</title>");
        foreach (var style in styles)
        {
            html.Append("<link rel=\"stylesheet\" href=\"").Append(encoder.Encode(style)).Append("\">");
        }

        html.Append("<style id=\"").Append(LayerId).Append("\"></style>");
        html.Append("<style>").Append(Ground).Append("</style>");
        html.Append("</head><body>").Append(markup.Trim()).Append("</body></html>");
        return html.ToString();
    }
}

/// <summary>The theme editor's sample form.</summary>
/// <param name="Name">Its name, which titles the page.</param>
/// <param name="Definition">Its definition.</param>
/// <param name="Post">The answers a visitor gave, with the error of one.</param>
internal sealed record ThemeSample(string Name, FormDefinition Definition, FormPostState Post)
{
    /// <inheritdoc />
    public override string ToString() => nameof(ThemeSample);
}
