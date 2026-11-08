using Jiron.Umbraco.FormBuilder.Core.Themes;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Themes;

/// <summary>
/// A library theme's custom CSS as the website serves it (ROADMAP \u00a74.10, ADR-0215): every selector under the theme's form,
/// the page around it the form itself; the grouping at-rules kept with their rules; imports and other at-rules gone; a
/// declaration with a script or an address on another host gone, a path on the site and a data image kept; a web font
/// from another host gone; nested rules gone; and each removal said by its kind.
/// </summary>
[TestFixture]
public sealed class ThemeCssTests
{
    private const string Scope = ".jfb-form.jfb-theme-autumn";

    private static SanitisedThemeCss Of(string css) => ThemeCss.Sanitise(css, "autumn");

    [Test]
    public void EverySelector_GoesUnderTheThemesForm_AndTheFormItselfIsTheScope()
    {
        var result = Of(".jfb-label, .jfb-help > a:hover { color: red; } .jfb-form { gap: 4px; } .jfb-form.wide .jfb-page { gap: 2px; } .jfb-formless { margin: 0; }");

        Assert.That(result.Removed, Is.Empty);
        // The parser writes a selector its own way: the combinators without spaces.
        Assert.That(result.Css, Does.Contain($"{Scope} .jfb-label, {Scope} .jfb-help>a:hover {{ color: red; }}"));
        Assert.That(result.Css, Does.Contain($"{Scope} {{ gap: 4px; }}"));
        Assert.That(result.Css, Does.Contain($"{Scope}.wide .jfb-page {{"));
        // A class that only starts with the form's name is inside the form, as any other.
        Assert.That(result.Css, Does.Contain($"{Scope} .jfb-formless {{"));
    }

    [TestCase(":root { --jfb-color-accent: #123456; }")]
    [TestCase("html { --jfb-color-accent: #123456; }")]
    [TestCase("body { --jfb-color-accent: #123456; }")]
    public void ThePageAroundTheForm_IsTheFormItself_SoNothingReachesOutside(string css)
    {
        var result = Of(css);

        Assert.That(result.Css.Trim(), Is.EqualTo($"{Scope} {{ --jfb-color-accent: #123456; }}"));
    }

    [Test]
    public void ASelectorBelowThePage_StaysInsideTheForm()
    {
        Assert.That(ThemeCss.Scoped("body .jfb-label", Scope), Is.EqualTo($"{Scope} .jfb-label"));
        Assert.That(ThemeCss.Scoped("html > .jfb-form", Scope), Is.EqualTo(Scope));
    }

    [Test]
    public void SelectorLists_AreSplitOnlyAtTheirOwnCommas()
    {
        Assert.That(
            ThemeCss.SplitSelectors(":is(.a, .b) .c, [data-x=\"1,2\"], .d"),
            Is.EqualTo(new[] { ":is(.a, .b) .c", "[data-x=\"1,2\"]", ".d" }));
    }

    [Test]
    public void MediaSupportsAndKeyframes_KeepTheirRules_EachSelectorScoped()
    {
        var result = Of("@media (min-width: 40em) { .jfb-field { gap: 2px; } } @supports (display: grid) { .jfb-group { display: grid; } } @keyframes pop { from { opacity: 0; } to { opacity: 1; } }");

        Assert.That(result.Removed, Is.Empty);
        Assert.That(result.Css, Does.Contain("@media (min-width: 40em) {"));
        Assert.That(result.Css, Does.Contain($"  {Scope} .jfb-field {{"));
        Assert.That(result.Css, Does.Contain("@supports (display: grid) {"));
        Assert.That(result.Css, Does.Contain($"  {Scope} .jfb-group {{"));
        Assert.That(result.Css, Does.Contain("@keyframes pop {"));
        Assert.That(result.Css, Does.Contain("opacity: 0"));
    }

    [Test]
    public void ImportsAndOtherAtRules_Go_AndAreSaid()
    {
        var result = Of("@import url(\"https://fonts.example.com/a.css\"); @namespace svg url(http://www.w3.org/2000/svg); @page { margin: 1cm; } .jfb-label { color: red; }");

        Assert.That(result.Css, Does.Not.Contain("@import").And.Not.Contain("@namespace").And.Not.Contain("@page"));
        Assert.That(result.Css, Does.Contain($"{Scope} .jfb-label"));
        Assert.That(result.Removed.Select(removal => removal.Code), Is.EqualTo(new[] { "import", "atRule", "atRule" }));
    }

    [TestCase(".a { width: expression(alert(1)); }")]
    [TestCase(".a { behavior: url(/x.htc); }")]
    [TestCase(".a { -moz-binding: url(/x.xml#y); }")]
    [TestCase(".a { background: url(\"javascript:alert(1)\"); }")]
    [TestCase(".a { background: url(vbscript:x); }")]
    public void AScriptInADeclaration_Goes(string css)
    {
        var result = Of(css);

        Assert.That(result.Css, Is.Empty);
        Assert.That(result.Removed.Select(removal => removal.Code), Is.EqualTo(new[] { "script" }));
    }

    [TestCase(".a { background-image: url(https://cdn.example.com/x.png); }")]
    [TestCase(".a { background-image: url('//cdn.example.com/x.png'); }")]
    [TestCase(".a { background-image: url(\"http://cdn.example.com/x.png\"); }")]
    [TestCase(".a { background-image: image-set(\"https://cdn.example.com/x.png\" 1x); }")]
    [TestCase(".a { background-image: url(data:text/html;base64,PHNjcmlwdD4=); }")]
    [TestCase(".a { list-style-image: url(ftp://example.com/x.png); }")]
    [TestCase(".a { cursor: url(\\\\server\\share\\x.cur), auto; }")]
    public void AnAddressOnAnotherHost_TakesItsDeclaration(string css)
    {
        var result = Of(css);

        Assert.That(result.Css, Is.Empty);
        Assert.That(result.Removed.Select(removal => removal.Code), Is.EqualTo(new[] { "address" }));
    }

    [TestCase(".a { background-image: url(/media/x.png); }")]
    [TestCase(".a { background-image: url(\"images/x.png\"); }")]
    [TestCase(".a { background-image: url(data:image/png;base64,iVBORw0KGgo=); }")]
    public void APathOnTheSiteOrADataImage_Stays(string css)
    {
        var result = Of(css);

        Assert.That(result.Removed, Is.Empty);
        Assert.That(result.Css, Does.Contain("background-image"));
    }

    [Test]
    public void AWebFontFromAnotherHost_Goes_OneOfTheSitesStays()
    {
        var result = Of("@font-face { font-family: Far; src: url(https://fonts.example.com/far.woff2) format(\"woff2\"); } @font-face { font-family: Near; src: url(/fonts/near.woff2) format(\"woff2\"); }");

        Assert.That(result.Css, Does.Not.Contain("Far").And.Contain("Near").And.Contain("/fonts/near.woff2"));
        Assert.That(result.Removed.Select(removal => removal.Code), Is.EqualTo(new[] { "fontFace" }));
    }

    [Test]
    public void NestedRules_Go_TheirParentsOwnDeclarationsStay()
    {
        var result = Of(".jfb-field { gap: 2px; & .jfb-label { color: red; } }");

        Assert.That(result.Css, Does.Contain($"{Scope} .jfb-field {{ gap: 2px; }}"));
        Assert.That(result.Css, Does.Not.Contain("color"));
        Assert.That(result.Removed.Select(removal => removal.Code), Is.EqualTo(new[] { "nested" }));
    }

    [Test]
    public void ImportantAndCustomProperties_AreKept()
    {
        var result = Of(".jfb-label { color: red !important; --jfb-color-accent: #0f6e5c; }");

        Assert.That(result.Css, Does.Contain("!important"));
        Assert.That(result.Css, Does.Contain("--jfb-color-accent"));
    }

    [Test]
    public void NothingTyped_IsNothingServed()
    {
        Assert.That(Of("   "), Is.EqualTo(new SanitisedThemeCss(string.Empty, [])));
        Assert.That(Of("/* only a comment */").Css, Is.Empty);
    }

    [Test]
    public void ARemovalQuotesAShortenedText()
    {
        var result = Of($".a {{ background: url(https://example.com/{new string('x', 200)}.png); }}");

        Assert.That(result.Removed.Single().Text.Length, Is.LessThanOrEqualTo(80));
        Assert.That(result.Removed.Single().Text, Does.EndWith("\u2026"));
    }

    [Test]
    public void TextThatBreaksOutOfARule_NeverReachesTheOutput()
    {
        var result = Of(".a { color: red; } </style><script>alert(1)</script> .b { color: blue; }");

        Assert.That(result.Css, Does.Not.Contain("<").And.Not.Contain("script"));
    }
}
