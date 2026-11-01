using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NSubstitute;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.Navigation;
using Umbraco.Cms.Core.Web;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags;

/// <summary>
/// A small Umbraco for the merge-tag tests (ADR-0067), made of substitutes: published pages in a tree with their names
/// and properties per culture, the addresses Umbraco gives them, domains, languages with their fallbacks, dictionary
/// items, members with their types, and Umbraco's application URL. Nothing is there until a test adds it.
/// </summary>
internal sealed class TestUmbraco
{
    private readonly Dictionary<Guid, IPublishedContent> _pages = [];
    private readonly Dictionary<Guid, Guid> _parents = [];
    private readonly Dictionary<(Guid Page, string Culture), string> _urls = [];
    private readonly List<IDomain> _domains = [];
    private readonly Dictionary<int, Guid> _ids = [];
    private readonly List<ILanguage> _languages = [];
    private readonly Dictionary<string, IDictionaryItem> _items = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, IMember> _members = [];
    private readonly Dictionary<Guid, IMemberType> _memberTypes = [];
    private readonly Dictionary<Guid, IPublishedMember> _publishedMembers = [];
    private readonly List<Uri?> _currents = [];

    /// <summary>Creates an Umbraco with nothing in it.</summary>
    public TestUmbraco()
    {
        Contents.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<bool?>())
            .Returns(call => Task.FromResult(_pages.GetValueOrDefault(call.ArgAt<Guid>(0))));
        Navigation.TryGetAncestorsKeys(Arg.Any<Guid>(), out Arg.Any<IEnumerable<Guid>>()).Returns(call =>
        {
            var key = call.ArgAt<Guid>(0);
            call[1] = AncestorsOf(key);
            return _pages.ContainsKey(key) || _parents.ContainsKey(key);
        });
        Contexts.EnsureUmbracoContext().Returns(_ => new UmbracoContextReference(Substitute.For<IUmbracoContext>(), false, Substitute.For<IUmbracoContextAccessor>()));
        Urls.GetUrl(Arg.Any<IPublishedContent>(), Arg.Any<UrlMode>(), Arg.Any<string?>(), Arg.Any<Uri?>()).Returns(call =>
        {
            var current = call.ArgAt<Uri?>(3);
            _currents.Add(current);
            if (!_urls.TryGetValue((call.ArgAt<IPublishedContent>(0).Key, call.ArgAt<string?>(2) ?? string.Empty), out var url))
            {
                return "#";
            }

            // As Umbraco does: a page under a domain has the domain's address, any other the current address's origin.
            return url.StartsWith('/') && call.ArgAt<UrlMode>(1) == UrlMode.Absolute && current is not null ? current.GetLeftPart(UriPartial.Authority) + url : url;
        });
        Languages.GetAllAsync().Returns(_ => Task.FromResult<IEnumerable<ILanguage>>([.. _languages]));
        Languages.GetDefaultIsoCodeAsync().Returns(_ => Task.FromResult(_languages.FirstOrDefault(language => language.IsDefault)?.IsoCode ?? "en-US"));
        Dictionary.GetAsync(Arg.Any<string>()).Returns(call => Task.FromResult(_items.GetValueOrDefault(call.ArgAt<string>(0))));
        Dictionary.GetManyAsync(Arg.Any<string[]>()).Returns(call =>
        {
            DictionaryReads++;
            return Task.FromResult<IEnumerable<IDictionaryItem>>([.. call.ArgAt<string[]>(0).Select(key => _items.GetValueOrDefault(key)).OfType<IDictionaryItem>()]);
        });
        Members.GetByKeysAsync(Arg.Any<Guid[]>()).Returns(call =>
            Task.FromResult<IEnumerable<IMember>>([.. call.ArgAt<Guid[]>(0).Select(key => _members.GetValueOrDefault(key)).OfType<IMember>()]));
        MemberTypes.GetAsync(Arg.Any<Guid>()).Returns(call => Task.FromResult(_memberTypes.GetValueOrDefault(call.ArgAt<Guid>(0))));
        PublishedMembers.GetAsync(Arg.Any<IMember>()).Returns(call => Task.FromResult(_publishedMembers.GetValueOrDefault(call.ArgAt<IMember>(0).Key)));
        Domains.GetAllAsync(Arg.Any<bool>()).Returns(_ => Task.FromResult<IEnumerable<IDomain>>([.. _domains]));
        Ids.GetKeyForId(Arg.Any<int>(), UmbracoObjectTypes.Document).Returns(call =>
            _ids.TryGetValue(call.ArgAt<int>(0), out var key) ? Attempt<Guid>.Succeed(key) : Attempt<Guid>.Fail());
        Routing.CurrentValue.Returns(_ => RoutingSettings);
    }

    /// <summary>The published content.</summary>
    public IPublishedContentCache Contents { get; } = Substitute.For<IPublishedContentCache>();

    /// <summary>The tree.</summary>
    public IDocumentNavigationQueryService Navigation { get; } = Substitute.For<IDocumentNavigationQueryService>();

    /// <summary>The Umbraco contexts the URL provider reads.</summary>
    public IUmbracoContextFactory Contexts { get; } = Substitute.For<IUmbracoContextFactory>();

    /// <summary>The URL provider.</summary>
    public IPublishedUrlProvider Urls { get; } = Substitute.For<IPublishedUrlProvider>();

    /// <summary>The languages.</summary>
    public ILanguageService Languages { get; } = Substitute.For<ILanguageService>();

    /// <summary>The dictionary.</summary>
    public IDictionaryItemService Dictionary { get; } = Substitute.For<IDictionaryItemService>();

    /// <summary>The members.</summary>
    public IMemberService Members { get; } = Substitute.For<IMemberService>();

    /// <summary>The member types.</summary>
    public IMemberTypeService MemberTypes { get; } = Substitute.For<IMemberTypeService>();

    /// <summary>The members' published values.</summary>
    public IPublishedMemberCache PublishedMembers { get; } = Substitute.For<IPublishedMemberCache>();

    /// <summary>Which pages visitors must sign in to see: none, unless a test says.</summary>
    public IPublicAccessService PublicAccess { get; } = Substitute.For<IPublicAccessService>();

    /// <summary>The domains.</summary>
    public IDomainService Domains { get; } = Substitute.For<IDomainService>();

    /// <summary>The map from content ids to keys.</summary>
    public IIdKeyMap Ids { get; } = Substitute.For<IIdKeyMap>();

    /// <summary>The routing settings, which hold Umbraco's application URL.</summary>
    public IOptionsMonitor<WebRoutingSettings> Routing { get; } = Substitute.For<IOptionsMonitor<WebRoutingSettings>>();

    /// <summary>The routing settings the monitor gives.</summary>
    public WebRoutingSettings RoutingSettings { get; } = new();

    /// <summary>How often the dictionary was asked for several items at once.</summary>
    public int DictionaryReads { get; private set; }

    /// <summary>The current addresses the URL provider was given, in order.</summary>
    public IReadOnlyList<Uri?> Currents => _currents;

    /// <summary>
    /// Adds a published page with its names by culture (<c>""</c> for an invariant page) and its properties, each an
    /// invariant value or a dictionary of values by culture.
    /// </summary>
    public IPublishedContent Page(Guid key, IReadOnlyDictionary<string, string> names, Guid? parent = null, IReadOnlyDictionary<string, object?>? properties = null)
    {
        var page = Substitute.For<IPublishedContent>();
        page.Key.Returns(key);
        page.Name.Returns(names.Values.FirstOrDefault() ?? string.Empty);
        page.Cultures.Returns(names.ToDictionary(name => name.Key, name => new PublishedCultureInfo(name.Key, name.Value, null, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc))));
        page.GetProperty(Arg.Any<string>()).Returns((IPublishedProperty?)null);
        foreach (var (alias, value) in properties ?? new Dictionary<string, object?>())
        {
            var property = Property(alias, value);
            page.GetProperty(alias).Returns(property);
        }

        _pages[key] = page;
        if (parent is { } parentKey)
        {
            _parents[key] = parentKey;
        }

        return page;
    }

    /// <summary>Adds an invariant published page with one name.</summary>
    public IPublishedContent Page(Guid key, string name, Guid? parent = null, IReadOnlyDictionary<string, object?>? properties = null) =>
        Page(key, new Dictionary<string, string> { [string.Empty] = name }, parent, properties);

    /// <summary>
    /// The address Umbraco gives a page in a culture: a path for a page without a domain, which the current address goes
    /// before, or a whole address for one under a domain.
    /// </summary>
    public void Url(Guid page, string culture, string url) => _urls[(page, culture)] = url;

    /// <summary>Adds a domain assigned to a content item, which has the id given.</summary>
    public void Domain(Guid content, int contentId, string name, int sortOrder = 0)
    {
        var domain = Substitute.For<IDomain>();
        domain.DomainName.Returns(name);
        domain.RootContentId.Returns(contentId);
        domain.SortOrder.Returns(sortOrder);
        domain.IsWildcard.Returns(name.StartsWith('*'));
        _domains.Add(domain);
        _ids[contentId] = content;
    }

    /// <summary>Adds a content item to the tree without publishing it, so that it holds domains and pages below it.</summary>
    public void Node(Guid key, Guid? parent = null)
    {
        if (parent is { } parentKey)
        {
            _parents[key] = parentKey;
        }
    }

    /// <summary>Adds a language with the one it falls back to.</summary>
    public void Language(string isoCode, string? fallback = null, bool isDefault = false)
    {
        var language = Substitute.For<ILanguage>();
        language.IsoCode.Returns(isoCode);
        language.FallbackIsoCode.Returns(fallback);
        language.IsDefault.Returns(isDefault);
        _languages.Add(language);
    }

    /// <summary>Adds a dictionary item with its translations by language.</summary>
    public void Item(string key, IReadOnlyDictionary<string, string> translations)
    {
        // Made before the item's Returns, since a substitute made inside one would take its place.
        List<IDictionaryTranslation> made = [];
        foreach (var (language, text) in translations)
        {
            var translation = Substitute.For<IDictionaryTranslation>();
            translation.LanguageIsoCode.Returns(language);
            translation.Value.Returns(text);
            made.Add(translation);
        }

        var item = Substitute.For<IDictionaryItem>();
        item.ItemKey.Returns(key);
        item.Translations.Returns(made);
        _items[key] = item;
    }

    /// <summary>
    /// Adds a member with their published properties, each with whether the member may see it and whether it is
    /// sensitive.
    /// </summary>
    public void Member(Guid key, string name, string email, IReadOnlyDictionary<string, (object? Value, bool CanView, bool Sensitive)>? properties = null)
    {
        var typeKey = Guid.NewGuid();
        var contentType = Substitute.For<ISimpleContentType>();
        contentType.Key.Returns(typeKey);
        var member = Substitute.For<IMember>();
        member.Key.Returns(key);
        member.Name.Returns(name);
        member.Email.Returns(email);
        member.ContentType.Returns(contentType);
        var type = Substitute.For<IMemberType>();
        var published = Substitute.For<IPublishedMember>();
        published.Key.Returns(key);
        published.GetProperty(Arg.Any<string>()).Returns((IPublishedProperty?)null);
        foreach (var (alias, (value, canView, sensitive)) in properties ?? new Dictionary<string, (object?, bool, bool)>())
        {
            var property = Property(alias, value);
            published.GetProperty(alias).Returns(property);
            type.MemberCanViewProperty(alias).Returns(canView);
            type.IsSensitiveProperty(alias).Returns(sensitive);
        }

        _members[key] = member;
        _memberTypes[typeKey] = type;
        _publishedMembers[key] = published;
    }

    /// <summary>Registers every substitute, as the site registers Umbraco's services.</summary>
    public void Register(IServiceCollection services)
    {
        services.AddSingleton(Contents);
        services.AddSingleton(Navigation);
        services.AddSingleton(Contexts);
        services.AddSingleton(Urls);
        services.AddSingleton(Languages);
        services.AddSingleton(Dictionary);
        services.AddSingleton(Members);
        services.AddSingleton(MemberTypes);
        services.AddSingleton(PublishedMembers);
        services.AddSingleton(PublicAccess);
        services.AddSingleton(Domains);
        services.AddSingleton(Ids);
        services.AddSingleton(Routing);
    }

    /// <summary>What the Umbraco sources read.</summary>
    public MergeTagUmbraco Umbraco() => new(Contents, Navigation, Contexts, Urls, Languages, Members, MemberTypes, PublishedMembers, PublicAccess);

    /// <summary>The dictionary as merge tags read it.</summary>
    public MergeTagDictionary DictionaryTexts() => new(Dictionary, Languages);

    /// <summary>The base URLs, over the general settings given.</summary>
    public SiteBaseUrl BaseUrls(IFormBuilderSettingsService settings) => new(settings, Domains, Ids, Navigation, Routing);

    /// <summary>
    /// A property with a value: an invariant one, or a dictionary of values by culture. It has a value where the value is
    /// neither null nor the empty text, as Umbraco's converters say.
    /// </summary>
    private static IPublishedProperty Property(string alias, object? value)
    {
        var property = Substitute.For<IPublishedProperty>();
        property.Alias.Returns(alias);
        object? ValueIn(string? culture) => value is IReadOnlyDictionary<string, object?> byCulture
            ? byCulture.FirstOrDefault(found => string.Equals(found.Key, culture, StringComparison.OrdinalIgnoreCase)).Value
            : value;
        property.GetValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(call => ValueIn(call.ArgAt<string?>(0)));
        property.HasValue(Arg.Any<string?>(), Arg.Any<string?>()).Returns(call => ValueIn(call.ArgAt<string?>(0)) is not (null or ""));
        return property;
    }

    private List<Guid> AncestorsOf(Guid key)
    {
        var ancestors = new List<Guid>();
        while (_parents.TryGetValue(key, out var parent))
        {
            ancestors.Add(parent);
            key = parent;
        }

        return ancestors;
    }
}
