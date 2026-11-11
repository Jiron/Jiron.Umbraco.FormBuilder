using System.Net;
using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Email;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.ContentEditing;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.Changes;
using Umbraco.Cms.Core.Services.Navigation;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Rendering;

/// <summary>
/// Two sites, on <c>site-a.localhost</c> and <c>site-b.localhost</c> (ROADMAP 14.4, ADR-0008, ADR-0043, ADR-0147, ADR-0232):
/// a call that names no page takes the site its host names, in its answer and in the markup the render and element
/// endpoints give, a page it names comes first, and a host of no site gets the shared values; an entry keeps the site it was sent from, so its mail follows that site, also once its page is gone;
/// what protects a form never follows the host; and a root deleted for good takes its settings rows with it, while the
/// Recycle Bin keeps them and the other site's stay.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class MultiSiteTests : RenderingTestBase
{
    private const string Entry = """{"revision":1,"values":{"name":"Ada"}}""";

    private IFormBuilderSettingsService Settings => GetRequiredService<IFormBuilderSettingsService>();

    private Guid FirstRoot => Contents.GetRootContent().Single(content => content.Name == "home").Key;

    [Test]
    public async Task ACallThatNamesNoPage_TakesTheSiteItsHostNames_APageItNamesComesFirst_AndAHostOfNoSiteGetsTheSharedValues()
    {
        var key = await PublishFormAsync("contact");
        var first = await PagesAsync("contact");
        var (other, _) = await SecondSiteAsync("about");
        await Settings.SaveSiteAsync(other, new GeneralSettings { TimeZone = "Pacific/Auckland" }, ["timeZone"], 0, Editor);

        var zones = new Dictionary<string, string>();
        foreach (var (what, address) in new[]
        {
            ("site B's host", $"http://{SecondHost}{Root}/forms/{key}"),
            ("site B's host on a port of its own", $"http://{SecondHost}:5538{Root}/forms/{key}"),
            ("site B's host in capitals", $"http://{SecondHost.ToUpperInvariant()}{Root}/forms/{key}"),
            ("site A's host", $"http://{FirstHost}{Root}/forms/{key}"),
            ("a host of no site", $"http://elsewhere.localhost{Root}/forms/{key}"),
            ("site B's host, naming a page of site A", $"http://{SecondHost}{Root}/forms/{key}?contentKey={first["contact"]}"),
        })
        {
            using var response = await GetAsync(address);
            zones[what] = (await JsonAsync(response))["timeZone"]!.GetValue<string>();
        }

        Assert.That(zones, Is.EqualTo(new Dictionary<string, string>
        {
            ["site B's host"] = "Pacific/Auckland",
            ["site B's host on a port of its own"] = "Pacific/Auckland",
            ["site B's host in capitals"] = "Pacific/Auckland",
            ["site A's host"] = "UTC",
            ["a host of no site"] = "UTC",
            ["site B's host, naming a page of site A"] = "UTC",
        }), string.Join(" | ", zones));
    }

    [Test]
    public async Task TheMarkupOfARenderOrAnElement_ThatNamesNoPage_IsInTheWordsOfTheSiteItsHostNames()
    {
        var key = await PublishFormAsync("contact");
        var first = await PagesAsync("contact");
        var (other, _) = await SecondSiteAsync("about");
        await Settings.SaveSiteAsync(other, new GeneralSettings { SubmitLabel = new Dictionary<string, string> { [""] = "Send to site B" } }, ["submitLabel"], 0, Editor);

        var markup = new Dictionary<string, bool>();
        foreach (var (what, address) in new[]
        {
            ("the render on site B's host", $"http://{SecondHost}{Root}/forms/{key}/render?instance=jfb-1&page=%2F"),
            ("the element on site B's host", $"http://{SecondHost}{Root}/forms/{key}/element?instance=jfb-1"),
            ("the render on site A's host", $"http://{FirstHost}{Root}/forms/{key}/render?instance=jfb-1&page=%2F"),
            ("the render on site B's host, naming a page of site A", $"http://{SecondHost}{Root}/forms/{key}/render?instance=jfb-1&page=%2F&contentKey={first["contact"]}"),
        })
        {
            using var response = await GetAsync(address);
            var html = await response.Content.ReadAsStringAsync();
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), what + ": " + html);
            markup[what] = html.Contains("Send to site B", StringComparison.Ordinal);
        }

        Assert.That(markup, Is.EqualTo(new Dictionary<string, bool>
        {
            ["the render on site B's host"] = true,
            ["the element on site B's host"] = true,
            ["the render on site A's host"] = false,
            ["the render on site B's host, naming a page of site A"] = false,
        }), string.Join(" | ", markup));
    }

    [Test]
    public async Task AHostWhoseDomainsLeadToTwoSites_NamesNeither_AndARootInTheRecycleBin_NoSite()
    {
        _ = await PagesAsync("contact");
        var (other, _) = await SecondSiteAsync("about");
        var hosts = GetRequiredService<SiteHosts>();
        var named = (hosts.SiteOf(SecondHost), hosts.SiteOf(FirstHost + ":443"));

        // Site B also answers on site A's host, under a path, so that host could mean either.
        var domains = GetRequiredService<IDomainService>();
        var culture = await GetRequiredService<ILanguageService>().GetDefaultIsoCodeAsync();
        var shared = await domains.UpdateDomainsAsync(other, new DomainsUpdateModel
        {
            DefaultIsoCode = culture,
            Domains = [new DomainModel { DomainName = SecondHost, IsoCode = culture }, new DomainModel { DomainName = FirstHost + "/b", IsoCode = culture }],
        });
        Assert.That(shared.Success, Is.True, shared.Status.ToString());
        GetRequiredService<IDomainCacheService>().Refresh([new DomainCacheRefresher.JsonPayload(0, DomainChangeTypes.RefreshAll)]);
        var ambiguous = hosts.SiteOf(FirstHost);
        Contents.MoveToRecycleBin(Contents.GetById(other)!);
        await GetRequiredService<IDocumentNavigationManagementService>().RebuildAsync();

        Assert.Multiple(() =>
        {
            Assert.That(named, Is.EqualTo(((Guid?)other, (Guid?)FirstRoot)), "each host names its site, on any port when its domain names none");
            Assert.That(ambiguous, Is.Null, "a host whose domains lead to two sites names neither");
            Assert.That(hosts.SiteOf(SecondHost), Is.Null, "a root in the Recycle Bin is no site");
        });
    }

    [Test]
    public async Task AnEntry_KeepsTheSiteItWasSentFrom_SoItsMailFollowsThatSite_AlsoOnceItsPageIsGone()
    {
        var mail = new FormAction
        {
            Key = Guid.NewGuid(),
            Type = EmailActionType.Alias,
            Name = "Team",
            Settings = SettingsObject.Parse("""{"to":["team@jfb.test"],"subject":{"":"An entry"},"body":{"":"<p><a href=\"/privacy/\">Privacy</a></p>"}}"""),
        };
        var key = await PublishFormAsync("contact", definition => definition with { Actions = [mail] });
        _ = await PagesAsync("contact");
        var (other, pages) = await SecondSiteAsync("about");
        await Settings.SaveSiteAsync(other, new EmailSettings { SenderAddress = "site-b@jfb.test" }, ["senderAddress"], 0, Editor);

        // The test hosts keep their actions paused; this one runs them, as a site does.
        GetRequiredService<IOptionsMonitor<FormBuilderOptions>>().CurrentValue.Actions.Paused = false;
        using var byHost = await PostAsync($"http://{SecondHost}{Root}/forms/{key}/entries", Entry);
        using var byPage = await PostAsync($"http://{FirstHost}{Root}/forms/{key}/entries", $$$"""{"revision":1,"contentKey":"{{{pages["about"]}}}","values":{"name":"Ben"}}""");
        using var elsewhere = await PostAsync($"http://elsewhere.localhost{Root}/forms/{key}/entries", Entry);
        for (var pass = 0; pass < 5; pass++)
        {
            await GetRequiredService<OutboxPass>().RunAsync(CancellationToken.None);
        }

        var entries = GetRequiredService<IEntryRepository>();
        var row = (await GetRequiredService<IFormRepository>().GetAsync(key))!;
        var stored = new List<StoredEntry>();
        foreach (var id in (await entries.ListIdsAsync(row.Id, 10)).Order())
        {
            stored.Add((await entries.GetByIdAsync(id))!);
        }

        var mails = new List<StoredEmail>();
        foreach (var entry in stored)
        {
            mails.Add((await GetRequiredService<IEmailRepository>().ListForEntryAsync(entry.Id)).Single());
        }

        // The page goes; the entry still follows its site.
        Contents.Delete(Contents.GetById(pages["about"])!);
        await GetRequiredService<IDocumentNavigationManagementService>().RebuildAsync();
        var pagesNow = GetRequiredService<FormPages>();
        var afterwards = await pagesNow.EntryPageAsync(stored[1]);

        Assert.Multiple(async () =>
        {
            Assert.That((byHost.StatusCode, byPage.StatusCode, elsewhere.StatusCode), Is.EqualTo((HttpStatusCode.OK, HttpStatusCode.OK, HttpStatusCode.OK)), await byHost.Content.ReadAsStringAsync());
            Assert.That(stored.Select(entry => entry.SiteKey), Is.EqualTo(new Guid?[] { other, other, null }), "site B by its host, site B by its page though sent to site A's host, and none");
            Assert.That((mails[0].From, mails[1].From), Is.EqualTo(("site-b@jfb.test", "site-b@jfb.test")), "site B's sender");
            Assert.That(mails[2].From, Is.Not.EqualTo("site-b@jfb.test"), "a host of no site gets the shared sender");
            Assert.That(mails[0].HtmlBody, Does.Contain("https://site-b.localhost/privacy/"), "a link follows site B's domain");
            Assert.That(mails[2].HtmlBody, Does.Not.Contain("site-b.localhost"));
            Assert.That((afterwards.Content, afterwards.Site), Is.EqualTo(((Guid?)null, (Guid?)other)), "the site it was sent from, once its page is gone");
        });
    }

    [Test]
    public async Task WhatProtectsAForm_NeverFollowsTheHost_SoALaxSitesHostGetsTheStrictestWithoutAPageOfIt()
    {
        var key = await PublishFormAsync("contact");
        _ = await PagesAsync("contact");
        var (other, _) = await SecondSiteAsync("about");
        await Settings.SaveSiteAsync(FirstRoot, new ProtectionSettings { Level = ProtectionLevel.ProofOfWork, ProofBits = 8 }, ["level", "proofBits"], 0, Editor);
        await Settings.SaveSiteAsync(other, new ProtectionSettings { Level = ProtectionLevel.Standard }, ["level"], 0, Editor);

        var puzzles = new Dictionary<string, bool>();
        foreach (var (what, host) in new[] { ("the lax site's host", SecondHost), ("the strict site's host", FirstHost), ("a host of no site", "elsewhere.localhost") })
        {
            using var asked = await SendAsync(HttpMethod.Post, $"http://{host}{Root}/forms/{key}/challenge", request => request.Content = new StringContent("{}", Encoding.UTF8, "application/json"));
            Assert.That(asked.StatusCode, Is.EqualTo(HttpStatusCode.OK), await asked.Content.ReadAsStringAsync());
            puzzles[what] = (await JsonAsync(asked))["proofOfWork"] is not null;
        }

        Assert.That(puzzles.Values, Is.All.True, "a request chooses its host as it chooses a page, so without a page of the form it gets the strictest (ADR-0147): " + string.Join(" | ", puzzles));
    }

    [Test]
    public async Task ARootDeletedForGood_TakesItsSettingsRowsWithIt_WhileTheRecycleBinKeepsThem_AndTheOtherSitesStay()
    {
        _ = await PagesAsync("contact");
        var (other, _) = await SecondSiteAsync("about");
        var third = new Content("third", -1, GetRequiredService<IContentTypeService>().Get(PageType)!);
        Contents.Save(third);
        foreach (var site in new[] { FirstRoot, other, third.Key })
        {
            await Settings.SaveSiteAsync(site, new GeneralSettings { TimeZone = "Pacific/Auckland" }, ["timeZone"], 0, Editor);
            await Settings.SaveSiteAsync(site, new EmailSettings { SenderAddress = "site@jfb.test" }, ["senderAddress"], 0, Editor);
        }

        var cached = (await Settings.GetAsync<GeneralSettings>(other)).Value.TimeZone;
        var stored = GetRequiredService<ISettingsRepository>();
        Contents.MoveToRecycleBin(Contents.GetById(other)!);
        var inTheBin = await stored.ListAsync(SettingsScopes.ForSite(other));
        await Contents.EmptyRecycleBinAsync(Constants.Security.SuperUserKey);
        Contents.Delete(Contents.GetById(third.Key)!);
        var audited = await GetRequiredService<IAuditRepository>().QueryAsync(new AuditQuery { Action = FormBuilderSettingsService.RemovedAction }, new PageRequest(0, 10));

        Assert.Multiple(async () =>
        {
            Assert.That(cached, Is.EqualTo("Pacific/Auckland"));
            Assert.That(inTheBin.Select(row => row.Section), Is.EqualTo(new[] { "email", "general" }), "a root in the Recycle Bin may come back, with its settings");
            Assert.That(await stored.ListAsync(SettingsScopes.ForSite(other)), Is.Empty, "emptied from the Recycle Bin");
            Assert.That(await stored.ListAsync(SettingsScopes.ForSite(third.Key)), Is.Empty, "deleted at once");
            Assert.That((await stored.ListAsync(SettingsScopes.ForSite(FirstRoot))).Select(row => row.Section), Is.EqualTo(new[] { "email", "general" }), "the other site's stay");
            Assert.That((await Settings.GetAsync<GeneralSettings>(other)).Value.TimeZone, Is.EqualTo("UTC"), "and no server keeps the gone site's copy");
            Assert.That(audited.Items.Select(entry => (entry.EntityKey, entry.UserKey)).Order(), Is.EqualTo(new (Guid?, Guid?)[] { (other, null), (other, null), (third.Key, null), (third.Key, null) }.Order()), "a removal audited per section, without a user when Umbraco deleted it");
        });
    }

    private Task<HttpResponseMessage> PostAsync(string address, string body) =>
        SendAsync(HttpMethod.Post, address, request => request.Content = new StringContent(body, Encoding.UTF8, "application/json"));
}
