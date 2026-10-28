using Jiron.Umbraco.FormBuilder.Core.Definitions.Projection;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Definitions;

/// <summary>
/// The one sanitiser of editors' rich text (section 4.10, ADR-0084): what it keeps, what goes with its content, which
/// links survive, how a link to a new tab is guarded, and that one instance serves many threads.
/// </summary>
[TestFixture]
public sealed class EditorHtmlTests
{
    private readonly EditorHtml _html = new();

    [TestCase("<p>Plain <strong>strong</strong> <b>b</b> <em>em</em> <i>i</i> <u>u</u> <s>s</s> <mark>m</mark> <small>sm</small> <sub>1</sub> <sup>2</sup> <code>c</code> <abbr title=\"Form\">F</abbr> <span lang=\"de\" dir=\"ltr\">Hallo</span></p>")]
    [TestCase("<h2>Two</h2><h3>Three</h3><h4>Four</h4><h5>Five</h5><h6>Six</h6>")]
    [TestCase("<ul><li>One</li></ul><ol><li>Two</li></ol>")]
    [TestCase("<blockquote><p>Said</p></blockquote><pre>  kept  </pre><hr><p>a<br>b</p>")]
    [TestCase("<p><a href=\"https://www.example.com/terms\" title=\"Terms\">terms</a> <a href=\"/privacy\">privacy</a> <a href=\"mailto:office@example.com\">mail</a> <a href=\"tel:+4930123\">call</a></p>")]
    [TestCase("<p>a &lt;b&gt; &amp; \"q\"</p>")]
    public void WhatItKeeps_PassesUnchanged(string html) =>
        Assert.That(_html.Sanitise(html), Is.EqualTo(html));

    [TestCase("<p class=\"x\" style=\"color:red\" id=\"top\" onclick=\"steal()\" data-x=\"1\">Text</p>", "<p>Text</p>")]
    [TestCase("<h1>Big</h1><p>Text</p>", "<p>Text</p>")]
    [TestCase("<p>Text</p><script>steal()</script>", "<p>Text</p>")]
    [TestCase("<style>p{color:red}</style><p>Text</p>", "<p>Text</p>")]
    [TestCase("<p>Text<img src=\"x.png\" onerror=\"steal()\"></p>", "<p>Text</p>")]
    [TestCase("<iframe src=\"https://evil.example\"></iframe><p>Text</p>", "<p>Text</p>")]
    [TestCase("<form action=\"https://evil.example\"><input name=\"card\"><button>Pay</button></form><p>Text</p>", "<p>Text</p>")]
    [TestCase("<svg><script>steal()</script></svg><p>Text</p>", "<p>Text</p>")]
    [TestCase("<div><p>Inside</p></div><p>Text</p>", "<p>Text</p>")]
    [TestCase("<!-- note --><p>Text</p>", "<p>Text</p>")]
    [TestCase("<p>Text", "<p>Text</p>")]
    public void WhatItDoesNotKeep_GoesWithItsContent(string html, string sanitised) =>
        Assert.That(_html.Sanitise(html), Is.EqualTo(sanitised));

    [TestCase("javascript:steal()")]
    [TestCase("JavaScript:steal()")]
    [TestCase(" javascript:steal()")]
    [TestCase("data:text/html,<script>steal()</script>")]
    [TestCase("vbscript:steal()")]
    [TestCase("file:///c:/secrets")]
    [TestCase("ftp://files.example.com")]
    public void ALinkToAnotherScheme_LosesItsAddress(string href) =>
        Assert.That(_html.Sanitise($"<p><a href=\"{href}\">link</a></p>"), Is.EqualTo("<p><a>link</a></p>"));

    [TestCase("_blank")]
    [TestCase("_BLANK")]
    public void ALinkToANewTab_HasNoHoldOnThePageThatOpenedIt(string target) =>
        Assert.That(
            _html.Sanitise($"<p><a href=\"/terms\" target=\"{target}\" rel=\"opener\">terms</a></p>"),
            Is.EqualTo("<p><a href=\"/terms\" target=\"_blank\" rel=\"noopener noreferrer\">terms</a></p>"));

    [TestCase("_self")]
    [TestCase("_top")]
    [TestCase("_parent")]
    [TestCase("checkout")]
    public void AnyOtherTarget_Goes(string target) =>
        Assert.That(_html.Sanitise($"<p><a href=\"/terms\" target=\"{target}\" rel=\"opener\">terms</a></p>"), Is.EqualTo("<p><a href=\"/terms\">terms</a></p>"));

    [Test]
    public void AnEmptyText_StaysEmpty_AndNullIsRefused()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_html.Sanitise(string.Empty), Is.Empty);
            Assert.That(() => _html.Sanitise(null!), Throws.ArgumentNullException);
        });
    }

    [Test]
    public void OneInstance_SanitisesOnManyThreadsAtOnce()
    {
        const string Rich = "<p class=\"x\">Hello <a href=\"/a\" target=\"_blank\">a</a><script>steal()</script></p>";
        var expected = _html.Sanitise(Rich);
        var results = new string[200];

        Parallel.For(0, results.Length, i => results[i] = _html.Sanitise(Rich));

        Assert.That(results, Is.All.EqualTo(expected));
    }

    [Test]
    public void TheAllowlist_IsWhatTheDocsName()
    {
        Assert.Multiple(() =>
        {
            Assert.That(EditorHtml.Tags, Does.Not.Contain("h1").And.Not.Contain("img").And.Not.Contain("div").And.Not.Contain("script"));
            Assert.That(EditorHtml.Attributes, Is.EqualTo(new[] { "dir", "href", "lang", "target", "title" }));
            Assert.That(EditorHtml.Schemes, Is.EqualTo(new[] { "http", "https", "mailto", "tel" }));
        });
    }
}
