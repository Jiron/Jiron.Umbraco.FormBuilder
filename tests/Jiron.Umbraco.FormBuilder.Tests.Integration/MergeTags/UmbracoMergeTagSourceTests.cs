using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence;
using Microsoft.AspNetCore.Http;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.ContentEditing;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Core.Serialization;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Strings;
using Umbraco.Cms.Core.Sync;
using Umbraco.Cms.Infrastructure.Sync;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.MergeTags;

/// <summary>
/// The Umbraco sources over a real site (ADR-0067): published pages in English and German under domains, a language that
/// falls back, dictionary items and a member, rendered through the service as a site renders them, and the base URL
/// from the general settings as the site stores them.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class UmbracoMergeTagSourceTests : FormBuilderMigrationTestBase
{
    private const string EntryPath = "/umbraco/section/jfb-forms/workspace/jfb-entry/edit/e0000000-0000-4000-8000-000000000001";

    /// <summary>Every test's database has Jiron Form Builder's tables, and Umbraco's URL segments are read.</summary>
    [SetUp]
    public async Task MigrateAsync()
    {
        var result = await RunAsync(new FormBuilderMigrationPlan());
        Assert.That(result.Successful, Is.True, result.Exception?.ToString());

        // A site starts its URL segments when it boots, which the test host leaves to the test.
        await GetRequiredService<IDocumentUrlService>().InitAsync(false, CancellationToken.None);
    }

    /// <summary>The site's composers, and content changes reaching the navigation and the URLs through the cache refreshers, run on this server.</summary>
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        base.CustomTestSetup(builder);
        builder.AddNotificationHandler<ContentTreeChangeNotification, ContentTreeChangeDistributedCacheNotificationHandler>();
        builder.Services.AddUnique<IServerMessenger, LocalServerMessenger>();
    }

    [Test]
    public async Task EverySource_ReadsTheSite_InTheCulture()
    {
        Request(null);
        var site = await LibraryAsync();
        var service = GetRequiredService<IFormBuilderMergeTagService>();
        var scope = Scope(site);

        var rendered = await service.RenderAsync(
            "{{ page.name }}|{{ page.url }}|{{ page.heroTitle }}|{{ page.up.brand }}|{{ site.name }}|{{ site.url }}|{{ dictionary.Forms.Thanks }}|{{ dictionary.Forms.Bye }}|{{ member.name }}|{{ member.email }}|{{ member.company }}|{{ member.staffNote }}|{{ entry.url }}|{{ entry.answers }}",
            MergeTagPlace.Mail,
            MergeTagContext.Text,
            scope);

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Text, Is.EqualTo(string.Join(
                '|',
                "Veranstaltungen",
                "https://lindenhall.example/de/veranstaltungen/",
                "Mach mit",
                "Lindenhall Library",
                "Bibliothek Lindenhall",
                "https://lindenhall.example",
                "Danke",
                "Bye",
                "Anna Muster",
                "anna@example.com",
                "Lindenhall Ltd",
                string.Empty,
                "https://lindenhall.example" + EntryPath,
                "Ihr Name: Anna")));
            Assert.That(rendered.Problems, Is.Empty);
        });
    }

    [Test]
    public async Task TheSitesOwnBaseUrl_AsTheSettingsStoreIt_ComesBeforeItsDomains()
    {
        Request(null);
        var site = await LibraryAsync();
        var settings = GetRequiredService<IFormBuilderSettingsService>();
        await settings.SaveSiteAsync(site.Root, new GeneralSettings { BaseUrl = "HTTPS://www.Lindenhall.example/" }, ["baseUrl"], 0, Constants.Security.SuperUserKey);

        var rendered = await GetRequiredService<IFormBuilderMergeTagService>().RenderAsync("{{ site.url }}|{{ page.url }}|{{ entry.url }}", MergeTagPlace.Mail, MergeTagContext.Text, Scope(site));

        Assert.That(rendered.Text, Is.EqualTo("https://www.lindenhall.example|https://lindenhall.example/de/veranstaltungen/|https://www.lindenhall.example" + EntryPath), "a page under a domain keeps the domain's address");
    }

    [Test]
    public async Task DuringARequest_TheAddressesAreTheSites_NotTheRequestsHost()
    {
        Request("preview.lindenhall.example");
        var site = await LibraryAsync();

        var rendered = await GetRequiredService<IFormBuilderMergeTagService>().RenderAsync("{{ site.url }}|{{ page.url }}", MergeTagPlace.Outcome, MergeTagContext.Html, Scope(site));

        Assert.That(rendered.Text, Is.EqualTo("https://lindenhall.example|https://lindenhall.example/de/veranstaltungen/"));
    }

    /// <summary>
    /// A mail renders in the background without a request (<paramref name="host"/> null), a message after a submission
    /// during the visitor's request to the host. The test host's own stand-in request has no address, which a request to a
    /// site always has, so each test sets the one it renders in.
    /// </summary>
    private void Request(string? host)
    {
        var accessor = GetRequiredService<IHttpContextAccessor>();
        if (host is null)
        {
            accessor.HttpContext = null;
            return;
        }

        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString(host);
        context.Request.Path = "/de/veranstaltungen/";
        accessor.HttpContext = context;
    }

    /// <summary>A mail's scope on the events page in German, for the member, with the entry and an answer whose label names a dictionary item.</summary>
    private static MergeTagScope Scope((Guid Root, Guid Events, Guid Member) site)
    {
        var label = new FormField { Key = Guid.Parse("73000000-0000-4000-8000-000000000001"), Type = "text", Alias = "name", Label = new LocalizedText("#Forms.Name") };
        var form = new FormDefinition
        {
            Key = Guid.Parse("f0000000-0000-4000-8000-000000000001"),
            Pages = [new FormPage { Key = Guid.Parse("73000000-0000-4000-8000-000000000002"), Groups = [new FormGroup { Key = Guid.Parse("73000000-0000-4000-8000-000000000003"), Fields = [label] }] }],
        };
        return new MergeTagScope
        {
            Form = form,
            FormName = "Kontakt",
            FormAlias = "contact",
            Culture = "de-DE",
            Site = site.Root,
            Page = site.Events,
            Member = site.Member,
            Entry = new MergeTagEntry { Key = Guid.Parse("e0000000-0000-4000-8000-000000000001"), Number = 1, State = "submitted", Submitted = DateTimeOffset.UnixEpoch },
            Answers = new MergeTagAnswers([new MergeTagAnswer(label.Key, [new Core.FieldTypes.FieldValue("Anna")])]),
        };
    }

    /// <summary>
    /// English and German (falling back to English); a page type with a German and English hero title and an invariant
    /// brand; the home with the brand under <c>lindenhall.example</c> and <c>lindenhall.example/de</c>, the events page
    /// below it; three dictionary items; and a member whose type lets them see the company but not the staff note.
    /// </summary>
    private async Task<(Guid Root, Guid Events, Guid Member)> LibraryAsync()
    {
        var languages = GetRequiredService<ILanguageService>();
        var english = await languages.GetDefaultLanguageAsync() ?? throw new InvalidOperationException("No default language.");
        var german = new Language("de-DE", "German") { FallbackIsoCode = english.IsoCode };
        await languages.CreateAsync(german, Constants.Security.SuperUserKey);

        var strings = GetRequiredService<IShortStringHelper>();
        var textstring = await GetRequiredService<IDataTypeService>().GetAsync(Constants.DataTypes.Guids.TextstringGuid) ?? throw new InvalidOperationException("No textstring.");
        var pageType = new ContentType(strings, -1) { Alias = "jfbPage", Name = "Page", Variations = ContentVariation.Culture, AllowedAsRoot = true };
        pageType.AddPropertyType(new PropertyType(strings, textstring, "heroTitle") { Name = "Hero title", Variations = ContentVariation.Culture });
        pageType.AddPropertyType(new PropertyType(strings, textstring, "brand") { Name = "Brand", Variations = ContentVariation.Nothing });
        var contentTypes = GetRequiredService<IContentTypeService>();
        Succeeded(await contentTypes.CreateAsync(pageType, Constants.Security.SuperUserKey));
        pageType.AllowedContentTypes = [new ContentTypeSort(pageType.Key, 0, pageType.Alias)];
        Succeeded(await contentTypes.UpdateAsync(pageType, Constants.Security.SuperUserKey));

        var contents = GetRequiredService<IContentService>();
        var root = new Content("Lindenhall", -1, pageType);
        root.SetCultureName("Lindenhall Library", english.IsoCode);
        root.SetCultureName("Bibliothek Lindenhall", german.IsoCode);
        root.SetValue("brand", "Lindenhall Library");
        contents.Save(root);
        contents.Publish(root, ["*"]);
        var events = new Content("Events", root, pageType);
        events.SetCultureName("Events", english.IsoCode);
        events.SetCultureName("Veranstaltungen", german.IsoCode);
        events.SetValue("heroTitle", "Join us", english.IsoCode);
        events.SetValue("heroTitle", "Mach mit", german.IsoCode);
        contents.Save(events);
        contents.Publish(events, ["*"]);

        var domains = await GetRequiredService<IDomainService>().UpdateDomainsAsync(root.Key, new DomainsUpdateModel
        {
            Domains = [new DomainModel { DomainName = "lindenhall.example", IsoCode = english.IsoCode }, new DomainModel { DomainName = "lindenhall.example/de", IsoCode = german.IsoCode }],
        });
        Assert.That(domains.Success, Is.True, domains.Status.ToString());

        var dictionary = GetRequiredService<IDictionaryItemService>();
        await dictionary.CreateAsync(new DictionaryItem("Forms.Thanks") { Translations = [new DictionaryTranslation(english, "Thank you"), new DictionaryTranslation(german, "Danke")] }, Constants.Security.SuperUserKey);
        await dictionary.CreateAsync(new DictionaryItem("Forms.Bye") { Translations = [new DictionaryTranslation(english, "Bye")] }, Constants.Security.SuperUserKey);
        await dictionary.CreateAsync(new DictionaryItem("Forms.Name") { Translations = [new DictionaryTranslation(english, "Your name"), new DictionaryTranslation(german, "Ihr Name")] }, Constants.Security.SuperUserKey);

        var memberTypes = GetRequiredService<IMemberTypeService>();
        var memberType = new MemberType(strings, -1) { Alias = "jfbMember", Name = "Library member" };
        memberType.AddPropertyType(new PropertyType(strings, textstring, "company") { Name = "Company" });
        memberType.AddPropertyType(new PropertyType(strings, textstring, "staffNote") { Name = "Staff note" });
        memberType.SetMemberCanViewProperty("company", true);
        Succeeded(await memberTypes.CreateAsync(memberType, Constants.Security.SuperUserKey));
        var member = new Member("Anna Muster", "anna@example.com", "anna", memberType);
        member.SetValue("company", "Lindenhall Ltd");
        member.SetValue("staffNote", "Owes a book");
        GetRequiredService<IMemberService>().Save(member);

        return (root.Key, events.Key, member.Key);
    }

    private static void Succeeded<TStatus>(Attempt<TStatus> attempt) => Assert.That(attempt.Success, Is.True, attempt.Result?.ToString());

    /// <summary>Delivers every cache refresh on this server at once, as a site on one server does.</summary>
    private sealed class LocalServerMessenger(IJsonSerializer serializer) : ServerMessengerBase(false, serializer)
    {
        public override void SendMessages()
        {
        }

        public override void Sync()
        {
        }

        protected override void DeliverRemote(ICacheRefresher refresher, MessageType messageType, IEnumerable<object>? ids = null, string? json = null)
        {
        }
    }
}
