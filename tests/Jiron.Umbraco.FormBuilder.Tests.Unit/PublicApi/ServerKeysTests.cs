using System.Net;
using System.Security.Cryptography;
using System.Text;
using Jiron.Umbraco.FormBuilder.Tests.Unit.Configuration;
using Jiron.Umbraco.FormBuilder.Web.PublicApi;
using Microsoft.AspNetCore.Http;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.PublicApi;

/// <summary>
/// The server keys of the public API (ADR-0126): the header's scheme and key read as the ADR says, every configured hash
/// compared in fixed time whichever matched, a hash of another length never matching, the partition a signed request
/// counts in, and the start-up check of the configured names and hashes, which never names a hash.
/// </summary>
public sealed class ServerKeysTests
{
    private const string Api = "JironFormBuilder:PublicApi:";

    // Fake keys (JFB_FAKE), 43 characters as scripts/server-key.mjs makes them.
    private const string Shop = "JFB_FAKE_shop_server_key_0123456789abcdefgh";
    private const string App = "JFB_FAKE_app_server_key_0123456789abcdefghi";

    private static readonly (string Name, byte[] Hash)[] Keys = [("shop", HashOf(Shop)), ("app", HashOf(App))];

    [TestCase(null)]
    [TestCase("Bearer abc")]
    [TestCase("Jfb-Keys " + Shop)]
    [TestCase("Basic " + Shop)]
    public void ARequestWithoutTheScheme_CarriesNoKey(string? header) =>
        Assert.That(ServerKeys.Match(header is null ? [] : [header], Keys, Equal), Is.EqualTo((ServerKeyStatus.None, (string?)null)));

    [TestCase("Jfb-Key")]
    [TestCase("Jfb-Key ")]
    [TestCase("Jfb-Key short")]
    [TestCase("Jfb-Key  " + Shop)]
    [TestCase("Jfb-Key " + Shop + " ")]
    [TestCase("Jfb-Key JFB_FAKE_shop server_key_0123456789abcdefgh")]
    [TestCase("Jfb-Key JFB_FAKE_unknown_key_0123456789abcdefghij")]
    public void TheSchemeWithAKeyThatIsMalformedOrMatchesNone_IsInvalid(string header) =>
        Assert.That(ServerKeys.Match([header], Keys, Equal).Status, Is.EqualTo(ServerKeyStatus.Invalid));

    [Test]
    public void AKeyOfTheLongestLengthPasses_AndOneLongerIsInvalid()
    {
        var longest = new string('k', ServerKeys.MaxKeyLength);
        var keys = new[] { ("long", HashOf(longest)) };

        Assert.Multiple(() =>
        {
            Assert.That(ServerKeys.Match(["Jfb-Key " + longest], keys, Equal), Is.EqualTo((ServerKeyStatus.Valid, (string?)"long")));
            Assert.That(ServerKeys.Match(["Jfb-Key " + longest + "k"], keys, Equal).Status, Is.EqualTo(ServerKeyStatus.Invalid));
            Assert.That(ServerKeys.Match(["Jfb-Key " + Shop[..(ServerKeys.MinKeyLength - 1)]], Keys, Equal).Status, Is.EqualTo(ServerKeyStatus.Invalid));
        });
    }

    [Test]
    public void AConfiguredKey_IsValid_ByItsName_InAnyLetterCaseOfTheScheme()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ServerKeys.Match(["Jfb-Key " + Shop], Keys, Equal), Is.EqualTo((ServerKeyStatus.Valid, (string?)"shop")));
            Assert.That(ServerKeys.Match(["jfb-key " + App], Keys, Equal), Is.EqualTo((ServerKeyStatus.Valid, (string?)"app")));
            Assert.That(ServerKeys.Match(["Jfb-Key " + Shop, "Jfb-Key " + App], Keys, Equal).Status, Is.EqualTo(ServerKeyStatus.Invalid), "two keys at once are none");
        });
    }

    [Test]
    public void EveryConfiguredHash_IsCompared_WhicheverMatched()
    {
        var compared = new List<byte[]>();
        bool Counting(byte[] a, byte[] b)
        {
            compared.Add(b);
            return CryptographicOperations.FixedTimeEquals(a, b);
        }

        var first = ServerKeys.Match(["Jfb-Key " + Shop], Keys, Counting);
        var all = compared.Count;
        compared.Clear();
        _ = ServerKeys.Match(["Jfb-Key JFB_FAKE_unknown_key_0123456789abcdefghij"], Keys, Counting);

        Assert.Multiple(() =>
        {
            Assert.That(first.Name, Is.EqualTo("shop"));
            Assert.That(all, Is.EqualTo(Keys.Length), "the match on the first key compares the last too");
            Assert.That(compared, Has.Count.EqualTo(Keys.Length), "and so does a key that matches none");
        });
    }

    [Test]
    public void AHashOfAnotherLength_NeverMatches()
    {
        var hash = HashOf(Shop);

        Assert.Multiple(() =>
        {
            Assert.That(ServerKeys.Match(["Jfb-Key " + Shop], [("short", hash[..31]), ("long", [.. hash, 0])], Equal).Status, Is.EqualTo(ServerKeyStatus.Invalid));
            Assert.That(Core.Configuration.PublicApiRules.ServerKeyHash(Convert.ToHexString(hash)[..62]), Is.Null, "the configuration never holds one");
        });
    }

    [Test]
    public void ASignedRequest_CountsInItsKeysPartition_AnyOtherInItsAddresss()
    {
        var signed = new DefaultHttpContext();
        signed.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");
        signed.Features.Set(new ServerKeyRequest("shop"));
        var anonymous = new DefaultHttpContext();
        anonymous.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.7");

        Assert.Multiple(() =>
        {
            Assert.That(FormBuilderRateLimitPolicies.ClientOf(signed), Is.EqualTo("key:shop"));
            Assert.That(FormBuilderRateLimitPolicies.ClientOf(anonymous), Is.EqualTo("address:203.0.113.7"));
        });
    }

    [Test]
    public void TheStartUpCheck_RefusesABadNameOrHash_AndOneGivenTwice_NeverNamingAHash()
    {
        var shop = Convert.ToHexStringLower(HashOf(Shop));
        var failures = HostOptions.Failures(new Dictionary<string, string?>
        {
            [Api + "ServerKeys:0:Name"] = "shop",
            [Api + "ServerKeys:0:Sha256"] = shop,
            [Api + "ServerKeys:1:Name"] = "SHOP",
            [Api + "ServerKeys:1:Sha256"] = shop.ToUpperInvariant(),
            [Api + "ServerKeys:2:Name"] = "a shop",
            [Api + "ServerKeys:2:Sha256"] = Shop,
            [Api + "ServerKeys:3:Name"] = "app",
            [Api + "ServerKeys:3:Sha256"] = Convert.ToHexStringLower(HashOf(App)),
        });

        Assert.Multiple(() =>
        {
            Assert.That(failures.Select(failure => failure[..failure.IndexOf(' ', StringComparison.Ordinal)]), Is.EquivalentTo(new[]
            {
                Api + "ServerKeys:1:Name",
                Api + "ServerKeys:1:Sha256",
                Api + "ServerKeys:2:Name",
                Api + "ServerKeys:2:Sha256",
            }));
            Assert.That(string.Join("\n", failures), Does.Not.Contain(shop).IgnoreCase.And.Not.Contain("JFB_FAKE"), "a message names the entry, never its hash or a key");
        });
    }

    private static byte[] HashOf(string key) => SHA256.HashData(Encoding.UTF8.GetBytes(key));

    private static bool Equal(byte[] a, byte[] b) => CryptographicOperations.FixedTimeEquals(a, b);
}
