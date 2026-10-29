using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Tests.Integration.PublicApi;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.ContentEditing;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Security;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.Changes;
using Umbraco.Cms.Core.Services.Navigation;
using Umbraco.Cms.Core.Strings;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Web.Common.Controllers;
using Umbraco.Cms.Web.Common.Security;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Rendering;

/// <summary>
/// Forms rendered on real Umbraco pages (ADR-0093): content of the page type <c>jfbRenderingPage</c>, whose
/// controller (<see cref="JfbRenderingPageController"/>) shows the test page named like the content from
/// <c>Views/RenderingTests/</c>, which this assembly compiles. The pages name their forms by alias, so a test publishes
/// the forms its page names. Fixtures boot the runtime (<c>[UmbracoTest(Boot = true)]</c>), so the site routes pages.
/// </summary>
public abstract class RenderingTestBase : PublicApiTestBase
{
    /// <summary>The alias of the page type the tests' pages are of.</summary>
    public const string PageType = "jfbRenderingPage";

    /// <summary>The member a test signs in.</summary>
    private protected const string MemberName = "jfb-rendering-member";

    /// <summary>The content service.</summary>
    private protected IContentService Contents => GetRequiredService<IContentService>();

    /// <summary>
    /// Creates a form with this alias and publishes it with <paramref name="fields"/> in its first group, after
    /// <paramref name="change"/> changed the definition.
    /// </summary>
    private protected async Task<Guid> PublishFormAsync(string alias, Func<FormDefinition, FormDefinition>? change = null, params FormField[] fields)
    {
        var created = await Forms.CreateAsync(new NewFormRequest(alias, Alias: alias, UserKey: Editor));
        await SaveAndPublishAsync(created.Key, definition => (change ?? (d => d))(Headless(WithFields(definition, fields.Length == 0 ? [NameField] : fields))));
        return created.Key;
    }

    /// <summary>
    /// Creates the site's pages: a root and, below it, one published page per name, each showing the test page of its
    /// name. Returns the pages' keys by name.
    /// </summary>
    private protected async Task<IReadOnlyDictionary<string, Guid>> PagesAsync(params string[] names)
    {
        var types = GetRequiredService<IContentTypeService>();
        var type = new ContentType(GetRequiredService<IShortStringHelper>(), -1) { Alias = PageType, Name = "Rendering page", AllowedAsRoot = true };
        Assert.That((await types.CreateAsync(type, Constants.Security.SuperUserKey)).Success, Is.True);
        type.AllowedContentTypes = [new ContentTypeSort(type.Key, 0, type.Alias)];
        Assert.That((await types.UpdateAsync(type, Constants.Security.SuperUserKey)).Success, Is.True);

        var root = new Content("home", -1, type);
        Contents.Save(root);
        Contents.Publish(root, ["*"]);
        var pages = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            var page = new Content(name, root, type);
            Contents.Save(page);
            Contents.Publish(page, ["*"]);
            pages[name] = page.Key;
        }

        await GetRequiredService<IDocumentNavigationManagementService>().RebuildAsync();
        await GetRequiredService<IPublishStatusManagementService>().InitializeAsync(CancellationToken.None);
        await GetRequiredService<IDocumentUrlService>().InitAsync(forceEmpty: false, CancellationToken.None);
        return pages;
    }

    /// <summary>Gets a page and parses it; the page answers <c>200</c>.</summary>
    /// <summary>
    /// The test pages in a site of one culture: the language, the pages, and the culture on the site's root; the test
    /// server sends no cache messages, so the domain cache learns of the culture here.
    /// </summary>
    private protected async Task<IReadOnlyDictionary<string, Guid>> CulturePagesAsync(string culture, string name, params string[] names)
    {
        Assert.That((await GetRequiredService<ILanguageService>().CreateAsync(new Language(culture, name), Constants.Security.SuperUserKey)).Success, Is.True);
        var pages = await PagesAsync(names);
        await DomainsAsync(Contents.GetById(Contents.GetById(pages[names[0]])!.ParentId)!.Key, culture);
        Assert.That(GetRequiredService<IDomainCacheService>().GetAll(true).Select(domain => domain.Culture), Does.Contain(culture));
        return pages;
    }

    /// <summary>
    /// A second site beside the first one's root: a root of its own with the pages, reached at the host <c>second.test</c>,
    /// and the first site's root reached at <c>first.test</c>; the root's key and the pages' keys.
    /// </summary>
    private protected async Task<(Guid Root, IReadOnlyDictionary<string, Guid> Pages)> SecondSiteAsync(params string[] names)
    {
        var type = GetRequiredService<IContentTypeService>().Get(PageType) ?? throw new InvalidOperationException("The first site comes first.");
        var first = Contents.GetRootContent().Single(content => content.Name == "home");
        var root = new Content("other", -1, type);
        Contents.Save(root);
        Contents.Publish(root, ["*"]);
        var pages = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            var page = new Content(name, root, type);
            Contents.Save(page);
            Contents.Publish(page, ["*"]);
            pages[name] = page.Key;
        }

        await GetRequiredService<IDocumentNavigationManagementService>().RebuildAsync();
        await GetRequiredService<IPublishStatusManagementService>().InitializeAsync(CancellationToken.None);
        await GetRequiredService<IDocumentUrlService>().InitAsync(forceEmpty: false, CancellationToken.None);
        var culture = await GetRequiredService<ILanguageService>().GetDefaultIsoCodeAsync();
        await DomainsAsync(first.Key, culture, FirstHost);
        await DomainsAsync(root.Key, culture, SecondHost);
        return (root.Key, pages);
    }

    /// <summary>A test page at a host, such as <see cref="SecondHost"/>, over https when <paramref name="secure"/> says so.</summary>
    private protected async Task<(string Html, IHtmlDocument Document)> PageAtAsync(string host, string name, bool secure = false)
    {
        using var response = await GetAsync((secure ? "https://" : "http://") + host + "/" + name + "/");
        var html = await response.Content.ReadAsStringAsync();
        Assert.That(Succeeded(response), Is.True, $"{host}/{name}/ answers {(int)response.StatusCode}: {html}");
        return (html, await new HtmlParser().ParseDocumentAsync(html));
    }

    private protected async Task<(string Html, IHtmlDocument Document)> PageAsync(string name)
    {
        using var response = await GetAsync("/" + name + "/");
        var html = await response.Content.ReadAsStringAsync();
        Assert.That(Succeeded(response), Is.True, $"/{name}/ answers {(int)response.StatusCode}: {html}");
        return (html, await new HtmlParser().ParseDocumentAsync(html));
    }

    /// <summary>Creates a member and signs it in on the test client, which keeps the cookie.</summary>
    private protected async Task SignInMemberAsync(string email)
    {
        var members = GetRequiredService<IMemberService>();
        var member = members.CreateMemberWithIdentity(MemberName, email, "Rendering member", Constants.Conventions.MemberTypes.DefaultAlias);
        members.Save(member);
        using var response = await GetAsync("/jfb-test/rendering/sign-in?name=" + MemberName);
        Assert.That(Succeeded(response), Is.True, await response.Content.ReadAsStringAsync());
    }

    /// <inheritdoc />
    /// <summary>The host of the first site once there is a second one.</summary>
    private protected const string FirstHost = "first.test";

    /// <summary>The host of the second site.</summary>
    private protected const string SecondHost = "second.test";

    // A root's culture, and the host it is reached at, if any.
    private async Task DomainsAsync(Guid root, string culture, string? host = null)
    {
        var domains = await GetRequiredService<IDomainService>().UpdateDomainsAsync(root, new DomainsUpdateModel
        {
            DefaultIsoCode = culture,
            Domains = host is null ? [] : [new DomainModel { DomainName = host, IsoCode = culture }],
        });
        Assert.That(domains.Success, Is.True, domains.Status.ToString());
        GetRequiredService<IDomainCacheService>().Refresh([new DomainCacheRefresher.JsonPayload(0, DomainChangeTypes.RefreshAll)]);
    }

    /// <summary>
    /// Signs a backoffice user in on the test client, which keeps the backoffice's cookie, as Umbraco's login does; the
    /// cookie is for https only, so the pages that should see it are asked for over https.
    /// </summary>
    private protected async Task SignInBackOfficeAsync()
    {
        using var response = await GetAsync("https://localhost/jfb-test/rendering/back-office-sign-in");
        Assert.That(Succeeded(response), Is.True, await response.Content.ReadAsStringAsync());
    }

    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.Services.AddControllersWithViews().ConfigureApplicationPartManager(manager =>
        {
            var assembly = typeof(RenderingTestBase).Assembly;
            if (!manager.ApplicationParts.OfType<AssemblyPart>().Any(part => part.Assembly == assembly))
            {
                foreach (var part in ApplicationPartFactory.GetApplicationPartFactory(assembly).GetApplicationParts(assembly))
                {
                    manager.ApplicationParts.Add(part);
                }
            }
        });
    }
}

/// <summary>Shows the test page named like the current content, which Umbraco routes here for the page type <see cref="RenderingTestBase.PageType"/>.</summary>
public sealed class JfbRenderingPageController(ILogger<JfbRenderingPageController> logger, ICompositeViewEngine compositeViewEngine, IUmbracoContextAccessor umbracoContextAccessor)
    : RenderController(logger, compositeViewEngine, umbracoContextAccessor)
{
    /// <inheritdoc />
    public override IActionResult Index() => View($"~/Views/RenderingTests/{CurrentPage!.Name}.cshtml");
}

/// <summary>Signs a member in for the rendering tests, as a site's own sign-in page would.</summary>
[Route("jfb-test/rendering")]
public sealed class JfbRenderingSignInController(IMemberManager members, IMemberSignInManager signIn, IUserService accounts, IBackOfficeUserManager users, IBackOfficeSignInManager backOffice) : Controller
{
    /// <summary>Signs Umbraco's first administrator in to the backoffice.</summary>
    [HttpGet("back-office-sign-in")]
    public async Task<IActionResult> BackOfficeSignIn()
    {
        if (await accounts.GetAsync(Constants.Security.SuperUserKey) is not { } admin || await users.FindByNameAsync(admin.Username) is not { } user)
        {
            return NotFound();
        }

        await backOffice.SignInAsync(user, isPersistent: false);
        return Ok();
    }

    /// <summary>Signs the member of this name in.</summary>
    [HttpGet("sign-in")]
    public async Task<IActionResult> SignIn([FromQuery] string name)
    {
        if (await members.FindByNameAsync(name) is not { } member)
        {
            return NotFound();
        }

        await signIn.SignInAsync(member, isPersistent: false);
        return Ok();
    }
}
