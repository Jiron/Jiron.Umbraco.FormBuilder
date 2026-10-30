using System.Text.Encodings.Web;
using Jiron.Umbraco.FormBuilder.Core.Themes;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Extensions.DependencyInjection;

namespace Jiron.Umbraco.FormBuilder.Web.Rendering;

/// <summary>
/// The tags that load the website assets into a page (ADR-0004, ADR-0092, ADR-0105), each written at most once per
/// request: the stylesheets, the functional base rules first and then each theme's that the page's forms use, and the
/// runtime's module script. A <c>&lt;jfb-assets&gt;</c> claims the script as it runs, so it wins wherever it is; every
/// tag is decided when the page is written, in the page's order, so a <c>&lt;jfb-assets&gt;</c> writes the stylesheets
/// of every form rendered by then that the page has not written before it, and a form's own tags write those it still
/// lacks (ADR-0093). Razor runs a view before its layout and writes the layout's head before the view's body, so a
/// <c>&lt;jfb-assets&gt;</c> in the head writes them all, a form the layout renders after it included, and without one
/// they appear once, before the first form that needs them.
/// </summary>
internal sealed class PageAssets(WebsiteAssets assets)
{
    // The key under which a request keeps what its page uses and has written.
    private const string Key = "Jiron.FormBuilder.PageAssets";

    // The path the package's website assets are served below.
    private const string AssetsPrefix = WebsiteAssets.BasePath + "/";

    // The base rules' path, which every page with a form loads before any theme's stylesheet.
    private const string BaseStylesheet = WebsiteAssets.BasePath + "/" + WebsiteAssets.BaseRules;

    /// <summary>Whether the request's module script was claimed: written, or taken by a <c>&lt;jfb-assets&gt;</c> that writes it.</summary>
    /// <param name="httpContext">The request.</param>
    public static bool AreWritten(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        return httpContext.Items.TryGetValue(Key, out var state) && state is PageState { Script: true };
    }

    /// <summary>
    /// The name below <c>/jfb/assets/</c> of a stylesheet that is one of the package's website assets, or null for a
    /// path of the site's own: the first segments match in any letter case, as the assets are served (ADR-0092).
    /// </summary>
    /// <param name="stylesheet">A theme's stylesheet, a local path.</param>
    public static string? AssetName(string stylesheet)
    {
        ArgumentNullException.ThrowIfNull(stylesheet);
        return stylesheet.StartsWith(AssetsPrefix, StringComparison.OrdinalIgnoreCase) ? stylesheet[AssetsPrefix.Length..] : null;
    }

    /// <summary>
    /// Records that a form on the page renders with this theme, whether or not it brings its own tags, so that a
    /// <c>&lt;jfb-assets&gt;</c> anywhere on the page writes the theme's stylesheet.
    /// </summary>
    /// <param name="httpContext">The request.</param>
    /// <param name="theme">The form's theme.</param>
    public static void Use(HttpContext httpContext, IFormBuilderTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        State(httpContext).Themes.Add(theme);
    }

    /// <summary>
    /// The tags of a <c>&lt;jfb-assets&gt;</c>, each with the nonce: the one given, else the site's
    /// <see cref="IFormBuilderCspNonceProvider"/>'s, else none (an empty nonce is none). Null when the page claimed the
    /// module script before; else content that writes, when the page is written, the stylesheets the page has not written
    /// before it (the base rules', then those of every theme its forms use) and the script.
    /// </summary>
    /// <param name="httpContext">The request.</param>
    /// <param name="nonce">The nonce the page gives, or null.</param>
    public IHtmlContent? TagsOnce(HttpContext httpContext, string? nonce)
    {
        var state = State(httpContext);
        if (state.Script)
        {
            return null;
        }

        state.Script = true;
        return new Tags(this, httpContext, state, Nonce(httpContext, nonce));
    }

    /// <summary>
    /// A form's own tags: content that writes, when the page is written and with the site's nonce, the base rules, the
    /// theme's stylesheet and the module script, each unless the page wrote or claimed it before; written twice, it
    /// writes them once.
    /// </summary>
    /// <param name="httpContext">The request.</param>
    /// <param name="theme">The form's theme.</param>
    public IHtmlContent Automatic(HttpContext httpContext, IFormBuilderTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        return new AutomaticTags(this, httpContext, State(httpContext), theme);
    }

    // What the request's page uses and has written, made at first use.
    private static PageState State(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        if (httpContext.Items.TryGetValue(Key, out var found) && found is PageState state)
        {
            return state;
        }

        var made = new PageState();
        httpContext.Items[Key] = made;
        return made;
    }

    private static string? Nonce(HttpContext httpContext, string? nonce) =>
        string.IsNullOrEmpty(nonce) ? httpContext.RequestServices.GetService<IFormBuilderCspNonceProvider>()?.GetNonce(httpContext) : nonce;

    // Writes the stylesheets still to write, the base rules first, then the theme's or every used theme's, and the
    // script when it is this content's to write.
    private void Write(TextWriter writer, HtmlEncoder encoder, HttpContext httpContext, PageState state, string? nonce, IFormBuilderTheme? theme, bool script)
    {
        var pathBase = httpContext.Request.PathBase;
        Stylesheet(writer, encoder, pathBase, state, nonce, BaseStylesheet);
        IReadOnlyList<IFormBuilderTheme> themes = theme is null ? state.Themes : [theme];
        foreach (var used in themes)
        {
            if (used.Stylesheet is { } stylesheet)
            {
                Stylesheet(writer, encoder, pathBase, state, nonce, stylesheet);
            }
        }

        if (script)
        {
            var tag = new TagBuilder("script");
            tag.Attributes["type"] = "module";
            tag.Attributes["src"] = assets.Url(pathBase, WebsiteAssets.Runtime);
            Nonced(tag, nonce).WriteTo(writer, encoder);
        }
    }

    // A stylesheet's tag, once per page: one of the package's under its fingerprint, the site's own below its path base.
    private void Stylesheet(TextWriter writer, HtmlEncoder encoder, PathString pathBase, PageState state, string? nonce, string stylesheet)
    {
        if (!state.Stylesheets.Add(stylesheet))
        {
            return;
        }

        var tag = new TagBuilder("link") { TagRenderMode = TagRenderMode.StartTag };
        tag.Attributes["rel"] = "stylesheet";
        tag.Attributes["href"] = AssetName(stylesheet) is { } name ? assets.Url(pathBase, name) : pathBase.Add(stylesheet).ToString();
        Nonced(tag, nonce).WriteTo(writer, encoder);
    }

    private static TagBuilder Nonced(TagBuilder tag, string? nonce)
    {
        if (!string.IsNullOrEmpty(nonce))
        {
            tag.Attributes["nonce"] = nonce;
        }

        return tag;
    }

    // What a request's page uses and has written: the script claimed, the theme of each form in the order the forms were
    // rendered, and the stylesheets written, by path, which keeps each written once.
    private sealed class PageState
    {
        public bool Script { get; set; }

        public List<IFormBuilderTheme> Themes { get; } = [];

        public HashSet<string> Stylesheets { get; } = new(StringComparer.Ordinal);
    }

    // A <jfb-assets>'s tags: the script it claimed, written the first time the page writes it.
    private sealed class Tags(PageAssets assets, HttpContext httpContext, PageState state, string? nonce) : IHtmlContent
    {
        private bool _script = true;

        public void WriteTo(TextWriter writer, HtmlEncoder encoder)
        {
            var script = _script;
            _script = false;
            assets.Write(writer, encoder, httpContext, state, nonce, theme: null, script);
        }
    }

    // A form's own tags, with the site's nonce, asked for only when the page writes something.
    private sealed class AutomaticTags(PageAssets assets, HttpContext httpContext, PageState state, IFormBuilderTheme theme) : IHtmlContent
    {
        public void WriteTo(TextWriter writer, HtmlEncoder encoder)
        {
            var script = !state.Script;
            state.Script = true;
            string[] stylesheets = theme.Stylesheet is { } own ? [BaseStylesheet, own] : [BaseStylesheet];
            if (!script && stylesheets.All(state.Stylesheets.Contains))
            {
                return;
            }

            assets.Write(writer, encoder, httpContext, state, Nonce(httpContext, null), theme, script);
        }
    }
}
