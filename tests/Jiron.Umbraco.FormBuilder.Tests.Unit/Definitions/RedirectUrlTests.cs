using Jiron.Umbraco.FormBuilder.Core.Definitions;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Definitions;

/// <summary>
/// An outcome's redirect is a path on the site or an https address, and merge tags never decide the host (ADR-0002,
/// ADR-0035), so no visitor can steer a redirect to another site.
/// </summary>
public sealed class RedirectUrlTests
{
    private static readonly string Backslash = ((char)92).ToString();

    public static IEnumerable<TestCaseData> Refused()
    {
        TestCaseData Case(string name, string url) => new TestCaseData(url).SetName($"Refuses({name})");

        yield return Case("nothing", string.Empty);
        yield return Case("a relative path", "thanks");
        yield return Case("a query alone", "?sent=1");
        yield return Case("http", "http://example.com/thanks");
        yield return Case("a script", "javascript:alert(1)");
        yield return Case("data", "data:text/html,hello");
        yield return Case("two slashes", "//elsewhere.example/thanks");
        yield return Case("a slash and a backslash", "/" + Backslash + "elsewhere.example");
        yield return Case("a backslash in the path", "/a" + Backslash + "b");
        yield return Case("an encoded slash first", "/%2F/elsewhere.example");
        yield return Case("an encoded backslash first, in small letters", "/%5celsewhere.example");
        yield return Case("a broken escape", "/a%2");
        yield return Case("an escape that is not hexadecimal", "/a%zz");
        yield return Case("a space", "/thank you");
        yield return Case("a tab", "/thanks" + (char)9);
        yield return Case("a non-breaking space", "/thanks" + (char)0xA0 + "x");
        yield return Case("a merge tag first", "{{ field.page }}");
        yield return Case("a merge tag right after the first slash", "/{{ field.slug }}");
        yield return Case("merge tags that render two slashes when the first is empty", "/{{ field.a }}/{{ field.b }}");
        yield return Case("a merge tag whose value may start with a slash", "/{{ field.path }}thanks");
        yield return Case("a merge tag as the host", "https://{{ field.host }}/thanks");
        yield return Case("a merge tag in the host", "https://www.{{ field.domain }}.example/thanks");
        yield return Case("a merge tag in the port", "https://example.com:{{ field.port }}/");
        yield return Case("an unclosed merge tag", "/thanks?n={{ field.name");
        yield return Case("no host", "https://");
        yield return Case("no host before the path", "https:///thanks");
        yield return Case("a user", "https://user@example.com/");
        yield return Case("a user and a password", "https://user:JFB_FAKE@example.com/");
        yield return Case("an IPv6 literal", "https://[::1]/");
        yield return Case("a host with a space", "https://exa mple.com/");
        yield return Case("a label that starts with a hyphen", "https://-example.com/");
        yield return Case("a label that ends with a hyphen", "https://example-.com/");
        yield return Case("an empty label", "https://example..com/");
        yield return Case("a label of 64 characters", "https://" + new string('a', 64) + ".com/");
        yield return Case("a host of 254 characters", "https://" + string.Join('.', Enumerable.Repeat(new string('a', 62), 4)) + ".ab/");
        yield return Case("an underscore in the host", "https://my_site.example/");
        yield return Case("port 0", "https://example.com:0/");
        yield return Case("port 65536", "https://example.com:65536/");
        yield return Case("a port with a leading zero", "https://example.com:0443/");
        yield return Case("an empty port", "https://example.com:/");
        yield return Case("a port that is a word", "https://example.com:https/");
    }

    public static IEnumerable<TestCaseData> Accepted()
    {
        TestCaseData Case(string name, string url) => new TestCaseData(url).SetName($"Accepts({name})");

        yield return Case("the site's root", "/");
        yield return Case("a path", "/thanks");
        yield return Case("a path with a query and a fragment", "/events/thanks?ref=a%20b#top");
        yield return Case("merge tags in the path and the query", "/events/{{ field.city | lower }}/thanks?n={{ field.name }}");
        yield return Case("a merge tag after text of the first segment", "/thanks-{{ field.slug }}");
        yield return Case("an encoded slash later in the path", "/a/%2F");
        yield return Case("an https address", "https://example.com");
        yield return Case("an https address with a path", "https://www.example.co.uk/thanks/");
        yield return Case("an https address in capitals", "HTTPS://Example.COM/Thanks");
        yield return Case("a port", "https://example.com:8443/thanks");
        yield return Case("the highest port", "https://example.com:65535/");
        yield return Case("an IPv4 address", "https://192.0.2.10/thanks");
        yield return Case("merge tags after the host", "https://example.com/{{ field.lang }}/thanks?n={{ field.name | url }}");
        yield return Case("a merge tag in the query right after the host", "https://example.com?ref={{ query.ref }}");
        yield return Case("a merge tag in the fragment right after the host", "https://example.com#{{ field.anchor }}");
        yield return Case("spaces inside a merge tag", "/thanks?n={{  field.name  |  default: \"you\"  }}");
        yield return Case("a label of 63 characters", "https://" + new string('a', 63) + ".com/");
        yield return Case("letters outside ASCII in the path", "/caf" + (char)0xE9);
    }

    [TestCaseSource(nameof(Refused))]
    public void ARedirect_ThatCouldLeaveTheEditorsSite_IsRefused(string url) =>
        Assert.That(RedirectUrls.Problem(url), Is.Not.Null.And.Not.Empty);

    [TestCaseSource(nameof(Accepted))]
    public void ARedirect_OnTheEditorsSite_IsAccepted(string url) =>
        Assert.That(RedirectUrls.Problem(url), Is.Null);

    // The host check alone would refuse these too, but would only say the host is not a name.
    [TestCase("{{ field.page }}")]
    [TestCase("https://{{ field.host }}/thanks")]
    [TestCase("https://www.{{ field.domain }}.example/thanks")]
    [TestCase("https://example.com:{{ field.port }}/")]
    [TestCase("https://example.com{{ field.path }}")]
    public void AMergeTag_BeforeThePath_IsRefusedForWhereItStands(string url) =>
        Assert.That(RedirectUrls.Problem(url), Does.Contain("after the host"));
}
