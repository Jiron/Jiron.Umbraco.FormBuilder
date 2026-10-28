using Jiron.Umbraco.FormBuilder.Core.Configuration;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Configuration;

/// <summary>
/// The rules of the public API's host configuration (ADR-0084): which base paths a host may choose, how an origin is
/// written as browsers send it, and the start-up check that names each key it refuses.
/// </summary>
[TestFixture]
public sealed class PublicApiRulesTests
{
    private const string Api = "JironFormBuilder:PublicApi:";

    [TestCase("/jfb/api")]
    [TestCase("/a")]
    [TestCase("/site/forms-api")]
    [TestCase("/A.b_c~d-e")]
    [TestCase("/umbraco-forms")]
    [TestCase("/jfb/assetsx")]
    [TestCase("/app_pluginsx/api")]
    public void ABasePath_IsSlashAndSegments_OutsideTheSitesOwnPaths(string path)
    {
        Assert.That(PublicApiRules.IsBasePath(path, out var problem), Is.True, problem);
        Assert.That(PublicApiRules.IsBasePath("/" + new string('a', PublicApiRules.MaxBasePathLength - 1), out _), Is.True, "100 characters");
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("/")]
    [TestCase("jfb/api")]
    [TestCase("/jfb/api/")]
    [TestCase("//jfb")]
    [TestCase("/jfb//api")]
    [TestCase("/jfb api")]
    [TestCase("/jfb%20api")]
    [TestCase("/jfb?api")]
    [TestCase("/.")]
    [TestCase("/..")]
    [TestCase("/jfb/../api")]
    [TestCase("/.../api")]
    [TestCase("\\jfb")]
    public void ABasePath_OfAnotherShape_IsRefused(string? path)
    {
        Assert.Multiple(() =>
        {
            Assert.That(PublicApiRules.IsBasePath(path, out var problem), Is.False);
            Assert.That(problem, Does.StartWith("must be '/' and one or more segments"));
            Assert.That(PublicApiRules.IsBasePath("/b" + (char)0xE4 + "r", out _), Is.False, "a letter outside ASCII");
            Assert.That(PublicApiRules.IsBasePath("/" + new string('a', PublicApiRules.MaxBasePathLength), out _), Is.False, "101 characters");
        });
    }

    [TestCase("/umbraco", "/umbraco")]
    [TestCase("/UMBRACO", "/umbraco")]
    [TestCase("/umbraco/jfb", "/umbraco")]
    [TestCase("/App_Plugins", "/App_Plugins")]
    [TestCase("/app_plugins/JironFormBuilder", "/App_Plugins")]
    [TestCase("/jfb/assets", "/jfb/assets")]
    [TestCase("/jfb/assets/api", "/jfb/assets")]
    [TestCase("/jfb", "/jfb/assets")]
    [TestCase("/JFB", "/jfb/assets")]
    public void ABasePath_AtBelowOrAboveTheSitesOwnPaths_IsRefused(string path, string reserved)
    {
        Assert.Multiple(() =>
        {
            Assert.That(PublicApiRules.IsBasePath(path, out var problem), Is.False);
            Assert.That(problem, Is.EqualTo($"must not be '{reserved}', lie below it or hold it, since the site serves its own pages and files there."));
        });
    }

    [TestCase("https://www.example.com", "https://www.example.com")]
    [TestCase("HTTPS://WWW.Example.COM", "https://www.example.com")]
    [TestCase("https://www.example.com:443", "https://www.example.com")]
    [TestCase("http://localhost:80", "http://localhost")]
    [TestCase("http://localhost:8080", "http://localhost:8080")]
    [TestCase("https://shop.example.com:80", "https://shop.example.com:80")]
    [TestCase("http://127.0.0.1:5500", "http://127.0.0.1:5500")]
    [TestCase("http://[::1]:5500", "http://[::1]:5500")]
    [TestCase("https://[2001:DB8::1]", "https://[2001:db8::1]")]
    [TestCase("https://xn--bcher-kva.example", "https://xn--bcher-kva.example")]
    public void AnOrigin_IsWrittenAsBrowsersSendIt(string text, string origin) =>
        Assert.That(PublicApiRules.Origin(text), Is.EqualTo(origin));

    [Test]
    public void AnOriginWithAHostOutsideAscii_IsWrittenInItsAsciiForm() =>
        Assert.That(PublicApiRules.Origin("https://b" + (char)0xFC + "cher.example"), Is.EqualTo("https://xn--bcher-kva.example"));

    [TestCase(null)]
    [TestCase("")]
    [TestCase(" ")]
    [TestCase("null")]
    [TestCase("*")]
    [TestCase(" https://www.example.com")]
    [TestCase("https://www.example.com ")]
    [TestCase("https://www.example.com/")]
    [TestCase("https://www.example.com/shop")]
    [TestCase("https://www.example.com?a=1")]
    [TestCase("https://www.example.com#top")]
    [TestCase("https://user@www.example.com")]
    [TestCase("https://user:JFB_FAKE@www.example.com")]
    [TestCase("ftp://www.example.com")]
    [TestCase("file:///c:/site")]
    [TestCase("www.example.com")]
    [TestCase("//www.example.com")]
    [TestCase("https://")]
    public void AnythingElse_IsNoOrigin(string? text)
    {
        Assert.That(PublicApiRules.Origin(text), Is.Null);
        Assert.That(PublicApiRules.Origin("https://" + new string('a', 2041) + ".example"), Is.Null, "longer than 2048 characters");
    }

    [Test]
    public void TheDefaults_PassTheStartUpCheck()
    {
        var options = HostOptions.Bind(new Dictionary<string, string?>());
        Assert.Multiple(() =>
        {
            Assert.That(options.PublicApi.BasePath, Is.EqualTo(FormBuilderPublicApiOptions.DefaultBasePath));
            Assert.That(options.PublicApi.AllowedOrigins, Is.Empty);
            Assert.That(HostOptions.Failures(options), Is.Empty);
        });
    }

    [Test]
    public void TheStartUpCheck_NamesEachKeyItRefuses()
    {
        var failures = HostOptions.Failures(new Dictionary<string, string?>
        {
            [Api + "BasePath"] = "/umbraco/forms",
            [Api + "AllowedOrigins:0"] = "https://shop.example.com",
            [Api + "AllowedOrigins:1"] = "https://shop.example.com/",
            [Api + "AllowedOrigins:2"] = "HTTPS://Shop.Example.com:443",
            [Api + "AllowedOrigins:3"] = "http://shop.example.com",
        });

        Assert.That(failures, Is.EqualTo(new[]
        {
            "JironFormBuilder:PublicApi:BasePath ('/umbraco/forms') must not be '/umbraco', lie below it or hold it, since the site serves its own pages and files there.",
            "JironFormBuilder:PublicApi:AllowedOrigins:1 ('https://shop.example.com/') must be an origin such as 'https://www.example.com': http or https, a host and a port when it is not the scheme's, and nothing after them, with no '/' at the end.",
            "JironFormBuilder:PublicApi:AllowedOrigins:2 ('HTTPS://Shop.Example.com:443') names an origin again; JironFormBuilder:PublicApi:AllowedOrigins:0 names it already. Keep one.",
        }));
    }

    [Test]
    public void ABasePathOfAnotherShape_IsNamedWithAnExample() =>
        Assert.That(
            HostOptions.Failures(new Dictionary<string, string?> { [Api + "BasePath"] = "forms/" }),
            Is.EqualTo(new[] { "JironFormBuilder:PublicApi:BasePath ('forms/') must be '/' and one or more segments of letters, digits, '.', '-', '_' and '~', not only dots, such as '/jfb/api', at most 100 characters, without a '/' at the end." }));
}
