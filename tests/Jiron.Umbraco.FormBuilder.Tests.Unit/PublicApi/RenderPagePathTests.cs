using Jiron.Umbraco.FormBuilder.Web.PublicApi;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.PublicApi;

/// <summary>
/// The page path the render endpoint takes (ADR-0104): a local path as the browser's <c>location.pathname</c> gives it,
/// which becomes the form's action; anything that could leave the site, carry a query or a fragment, or is no URL path
/// is refused.
/// </summary>
[TestFixture]
public sealed class RenderPagePathTests
{
    [TestCase("/")]
    [TestCase("/contact/")]
    [TestCase("/en/about-us/contact")]
    [TestCase("/site/kontakt/")]
    [TestCase("/caf%C3%A9/")]
    [TestCase("/a.b_c~d!e$f&g'h(i)j*k+l,m;n=o:p@q/")]
    [TestCase("/%2F/")]
    public void ALocalPath_IsAPagePath(string page) =>
        Assert.That(FormController.IsPagePath(page), Is.True);

    [TestCase(null)]
    [TestCase("")]
    [TestCase("contact/")]
    [TestCase("//evil.example/")]
    [TestCase("/\\evil.example/")]
    [TestCase("https://evil.example/")]
    [TestCase("/contact/?x=1")]
    [TestCase("/contact/#jfb-1")]
    [TestCase("/con tact/")]
    [TestCase("/a\"b/")]
    [TestCase("/a<b/")]
    [TestCase("/a%2/")]
    [TestCase("/a%zz/")]
    [TestCase("/a%")]
    [TestCase("/a%4")]
    [TestCase("/a\tb")]
    public void AnythingElse_IsNoPagePath(string? page) =>
        Assert.That(FormController.IsPagePath(page), Is.False);

    [Test]
    public void ALetterOutsideAscii_IsNoPagePath_ItsEscapeIs() =>
        Assert.Multiple(() =>
        {
            Assert.That(FormController.IsPagePath("/caf" + (char)0xE9 + "/"), Is.False, "the browser escapes it");
            Assert.That(FormController.IsPagePath("/caf%C3%A9/"), Is.True);
        });

    [Test]
    public void APagePath_IsAtMost2048Characters() =>
        Assert.Multiple(() =>
        {
            Assert.That(FormController.IsPagePath("/" + new string('a', FormController.MaxPagePathLength - 1)), Is.True);
            Assert.That(FormController.IsPagePath("/" + new string('a', FormController.MaxPagePathLength)), Is.False);
            Assert.That(FormController.IsPagePath("/" + new string('a', FormController.MaxPagePathLength - 4) + "%41"), Is.True, "an escape counts as its three characters");
        });
}
