using Jiron.Umbraco.FormBuilder.Core.Forms;
using NSubstitute;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.PublishedCache;
using Umbraco.Cms.Core.Routing;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.Navigation;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Forms;

/// <summary>
/// The site a request's host names (ROADMAP 14.4, ADR-0008, ADR-0232): only a domain with a host counts, on its own port
/// when it names one; a host of two sites names neither; a domain on content in the Recycle Bin or gone names nothing.
/// </summary>
[TestFixture]
public sealed class SiteHostsTests
{
    private static readonly Guid SiteA = Guid.Parse("a0000000-0000-4000-8000-00000000000a");
    private static readonly Guid SiteB = Guid.Parse("b0000000-0000-4000-8000-00000000000b");
    private static readonly Guid PageOfB = Guid.Parse("b0000000-0000-4000-8000-0000000000b1");
    private static readonly Guid Trashed = Guid.Parse("c0000000-0000-4000-8000-00000000000c");

    [TestCase("site-a.localhost", "site-a.localhost", null)]
    [TestCase("Site-A.LocalHost:5538", "site-a.localhost", 5538)]
    [TestCase("site-a.localhost:80", "site-a.localhost", null)]
    [TestCase("[::1]:5520", "::1", 5520)]
    [TestCase("b\u00fccher.example", "xn--bcher-kva.example", null)]
    public void ARequestsHost_IsReadAsItsAsciiHostAndPort(string host, string expected, int? port) =>
        Assert.That(SiteHosts.AddressOf(host), Is.EqualTo(new HostAddress(expected, port)));

    [TestCase(null)]
    [TestCase("")]
    [TestCase("  ")]
    [TestCase("site-a.localhost/path")]
    [TestCase("user@site-a.localhost")]
    [TestCase("site-a.localhost?x=1")]
    [TestCase("site a.localhost")]
    [TestCase("site-a.localhost:notaport")]
    public void WhatIsNoHost_IsNone(string? host) => Assert.That(SiteHosts.AddressOf(host), Is.Null);

    [TestCase("site-b.localhost", "site-b.localhost", null)]
    [TestCase("https://site-b.localhost", "site-b.localhost", null)]
    [TestCase("https://site-b.localhost:8443/en", "site-b.localhost", 8443)]
    [TestCase("site-b.localhost:5538/de", "site-b.localhost", 5538)]
    public void ADomainsHostAndPort_AreItsOwn_ItsPathAside(string name, string host, int? port) =>
        Assert.That(SiteHosts.DomainAddress(name), Is.EqualTo(new HostAddress(host, port)));

    [TestCase("/en")]
    [TestCase("/")]
    [TestCase("")]
    [TestCase("ftp://site-b.localhost")]
    public void ADomainOfNoHost_NamesNone(string name) => Assert.That(SiteHosts.DomainAddress(name), Is.Null);

    [TestCase("site-b.localhost", "site-b.localhost:5538", true, TestName = "A domain without a port names its host on any port")]
    [TestCase("site-b.localhost:5538", "site-b.localhost:5538", true, TestName = "A domain with a port names that port")]
    [TestCase("site-b.localhost:5538", "site-b.localhost:5539", false, TestName = "A domain with a port names no other")]
    [TestCase("site-b.localhost:5538", "site-b.localhost", false, TestName = "A domain with a port names not the scheme's own")]
    [TestCase("shop.site-b.localhost", "site-b.localhost", false, TestName = "A subdomain is another host")]
    public void ADomain_NamesTheHostItSays(string domain, string host, bool matches) =>
        Assert.That(SiteHosts.Matches(SiteHosts.DomainAddress(domain)!, SiteHosts.AddressOf(host)!), Is.EqualTo(matches));

    [Test]
    public void TheSiteOfAHost_IsTheRootOfTheContentItsDomainIsOn()
    {
        var hosts = Hosts(("site-a.localhost", 1), ("/en", 1), ("site-b.localhost/de", 3), ("*4", 4));

        Assert.Multiple(() =>
        {
            Assert.That(hosts.SiteOf("site-a.localhost:5520"), Is.EqualTo(SiteA));
            Assert.That(hosts.SiteOf("site-b.localhost"), Is.EqualTo(SiteB), "a domain on a page of the site names its root");
            Assert.That(hosts.SiteOf("elsewhere.localhost"), Is.Null);
            Assert.That(hosts.SiteOf(null), Is.Null);
        });
    }

    [Test]
    public void AHostOfTwoSites_NamesNeither_ButTwoDomainsOfOneSiteNameIt()
    {
        var twoSites = Hosts(("site-a.localhost", 1), ("site-a.localhost/b", 2));
        var oneSite = Hosts(("site-b.localhost/en", 2), ("site-b.localhost/de", 3));

        Assert.Multiple(() =>
        {
            Assert.That(twoSites.SiteOf("site-a.localhost"), Is.Null);
            Assert.That(oneSite.SiteOf("site-b.localhost"), Is.EqualTo(SiteB));
        });
    }

    [Test]
    public void ADomainOnContentOutsideTheTree_NamesNothing()
    {
        var hosts = Hosts(("trashed.localhost", 5), ("gone.localhost", 6));

        Assert.Multiple(() =>
        {
            Assert.That(hosts.SiteOf("trashed.localhost"), Is.Null, "in the Recycle Bin");
            Assert.That(hosts.SiteOf("gone.localhost"), Is.Null, "no key for its id");
        });
    }

    // Content 1 is site A's root, 2 site B's, 3 a page of B, 5 in the Recycle Bin; 6 has no key.
    private static SiteHosts Hosts(params (string Name, int Content)[] domains)
    {
        var cache = Substitute.For<IDomainCacheService>();
        cache.GetAll(false).Returns(domains.Select((domain, index) => new Domain(index + 1, domain.Name, domain.Content, "en-US", domain.Name.StartsWith('*'), index)).ToList());
        var ids = Substitute.For<IIdKeyMap>();
        foreach (var (id, key) in new[] { (1, SiteA), (2, SiteB), (3, PageOfB), (5, Trashed) })
        {
            ids.GetKeyForId(id, UmbracoObjectTypes.Document).Returns(Attempt.Succeed(key));
        }

        ids.GetKeyForId(6, UmbracoObjectTypes.Document).Returns(Attempt<Guid>.Fail());
        var navigation = Substitute.For<IDocumentNavigationQueryService>();
        navigation.TryGetAncestorsKeys(Arg.Any<Guid>(), out Arg.Any<IEnumerable<Guid>>()).Returns(call =>
        {
            var key = (Guid)call[0];
            call[1] = key == PageOfB ? new[] { SiteB } : Array.Empty<Guid>();
            return key != Trashed;
        });
        return new SiteHosts(cache, ids, navigation);
    }
}
