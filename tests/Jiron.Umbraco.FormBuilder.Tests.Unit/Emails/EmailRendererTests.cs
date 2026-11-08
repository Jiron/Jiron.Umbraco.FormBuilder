using System.Text;
using System.Xml.Linq;
using Jiron.Umbraco.FormBuilder.Core.Email;
using Jiron.Umbraco.FormBuilder.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Emails;

/// <summary>
/// What every mail is (ROADMAP 8.2, section 3.3, ADR-0159): well-formed, with its styles on its elements, with the fixed width
/// for Outlook, nothing on <c>body</c>, and its snapshot. After a deliberate change, run with <c>JFB_UPDATE_SNAPSHOTS=1</c>: it
/// writes the files and fails, so the diff gets reviewed.
/// </summary>
public sealed class EmailRendererTests
{
    private const string Kitchen = """
        <h2>Thank you, Anna</h2>
        <p>We have your <strong>answers</strong>, and a <a href="https://jfb.test/more">link</a> &amp; a <a href="/on-the-site">path</a>.</p>
        <h3>Next</h3><h4>Then</h4>
        <ul><li>One</li><li>Two</li></ul><ol><li>First</li></ol>
        <blockquote>A quote</blockquote><pre>some  code</pre><p><code>inline</code></p><hr>
        <table><caption>Answers</caption><thead><tr><th>Name</th><th>Value</th></tr></thead><tbody><tr><td>Name</td><td>Anna</td></tr></tbody></table>
        """;

    private static readonly EmailRenderer Renderer = new();

    private static readonly EmailLayoutDesign Dressed = new()
    {
        Look = EmailLook.Card,
        LogoUrl = "https://jfb.test/logo.png",
        LogoAlt = new Dictionary<string, string> { [""] = "The logo", ["de"] = "Das Logo" },
        Light = new EmailColours { Accent = "#0a7d5a", Text = "#222222" },
        Dark = new EmailColours { Accent = "#7fe0c3" },
        Header = new Dictionary<string, string> { [""] = "<p>Our team</p>", ["de"] = "<p>Unser Team</p>" },
        Footer = new Dictionary<string, string> { [""] = "<p>Our address</p>", ["de"] = "<p>Unsere Adresse</p>" },
    };

    private static readonly string[] Layouts = ["card", "letter", "dressed-card", "dressed-letter"];

    private static EmailLayoutDesign LayoutOf(string name) => name switch
    {
        "card" => EmailLayouts.BuiltIn[EmailLayouts.CardKey],
        "letter" => EmailLayouts.BuiltIn[EmailLayouts.LetterKey],
        "dressed-card" => Dressed,
        _ => Dressed with { Look = EmailLook.Letter },
    };

    private static string RenderHtml(EmailLayoutDesign layout, string body = Kitchen, string culture = "en", string? baseUrl = "https://www.jfb.test") =>
        Renderer.Render(new EmailPage("Your answers", body, culture, layout, "Jiron", baseUrl)).Html;

    [TestCaseSource(nameof(Layouts))]
    public async Task EachLayout_IsItsSnapshot(string name)
    {
        var layout = LayoutOf(name);
        var path = RepositoryRoot.Path(typeof(EmailRendererTests).Assembly, "tests", "Jiron.Umbraco.FormBuilder.Tests.Unit", "Emails", "Snapshots", $"{name}.html");
        var rendered = Renderer.Render(new EmailPage("Your answers", Kitchen, "en", layout, "Jiron", "https://www.jfb.test"));
        var written = rendered.Html.Replace("><", ">\n<", StringComparison.Ordinal) + "\n\n" + rendered.Text + "\n";
        if (Environment.GetEnvironmentVariable("JFB_UPDATE_SNAPSHOTS") == "1")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, written, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Assert.Fail($"JFB_UPDATE_SNAPSHOTS=1: wrote {path}. Review the diff, and run again without it.");
        }

        Assert.That((await File.ReadAllTextAsync(path)).ReplaceLineEndings("\n"), Is.EqualTo(written), "a layout changed: run with JFB_UPDATE_SNAPSHOTS=1 and review the diff");
    }

    [TestCaseSource(nameof(Layouts))]
    public void EachLayout_IsWellFormed_InEveryDirection(string name)
    {
        var layout = LayoutOf(name);
        Assert.Multiple(() =>
        {
            foreach (var culture in new[] { "en", "de-CH", "ar", "not a culture" })
            {
                Assert.DoesNotThrow(() => XDocument.Parse(RenderHtml(layout, culture: culture)), $"{name} in {culture}");
            }
        });
    }

    [TestCaseSource(nameof(Layouts))]
    public void EachLayout_HasItsStylesInline_AndNothingOnTheBody(string name)
    {
        var layout = LayoutOf(name);
        var document = Parsed(RenderHtml(layout));
        var body = document.Descendants(Xhtml("body")).Single();
        string[] styled = ["table", "td", "th", "p", "a", "h2", "h3", "h4", "ul", "ol", "li", "blockquote", "pre", "code", "hr", "caption"];
        var bare = body.Descendants().Where(element => styled.Contains(element.Name.LocalName) && element.Attribute("style") is null).Select(element => element.Name.LocalName).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(body.Attributes(), Is.Empty, $"{name}: mail clients drop the styles of body");
            Assert.That(bare, Is.Empty, $"{name}: an element whose styles are not on it");
            Assert.That(body.Descendants().Select(element => element.Name.LocalName), Is.SupersetOf(styled), $"{name}: the sample reaches every styled element");
            Assert.That(body.Descendants(Xhtml("table")).All(table => (string?)table.Attribute("style") is { } style && (style.Contains("table-layout:fixed", StringComparison.Ordinal) || style.Contains("width:100%", StringComparison.Ordinal))), $"{name}: every table takes the width it is given");
        });
    }

    [TestCaseSource(nameof(Layouts))]
    public void EachLayout_HasOutlooksWidth_InAConditionalTable(string name)
    {
        var layout = LayoutOf(name);
        var html = RenderHtml(layout);

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("<!--[if mso]><table role=\"presentation\" cellspacing=\"0\" cellpadding=\"0\" border=\"0\" width=\"600\" align=\"center\"><tr><td><![endif]-->"), name);
            Assert.That(html, Does.Contain("<!--[if mso]></td></tr></table><![endif]-->"), name);
        });
    }

    [TestCaseSource(nameof(Layouts))]
    public void TheWordsOfEachLayout_MayBreak(string name)
    {
        var layout = LayoutOf(name);
        var document = Parsed(RenderHtml(layout));
        var cells = document.Descendants(Xhtml("td")).Where(cell => cell.Attribute("class") is { } classes && classes.Value.Contains("jfb-text", StringComparison.Ordinal));
        var content = document.Descendants(Xhtml("table")).First(table => (string?)table.Attribute("class") == "jfb-surface");

        Assert.Multiple(() =>
        {
            Assert.That(cells, Is.Not.Empty, name);
            Assert.That(cells.All(cell => ((string)cell.Attribute("style")!).Contains("overflow-wrap:anywhere", StringComparison.Ordinal)), Is.True, name);
            Assert.That((string)content.Attribute("style")!, Does.Contain("table-layout:fixed").And.Contain("max-width:600px"), name);
        });
    }

    [Test]
    public void DarkMode_MovesEveryColour_OnlyThroughTheStyleSheet()
    {
        var html = RenderHtml(Dressed);
        var sheet = Parsed(html).Descendants(Xhtml("style")).Single().Value;

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("<meta name=\"color-scheme\" content=\"light dark\" />"));
            Assert.That(sheet, Does.Contain("@media (prefers-color-scheme:dark){"));
            Assert.That(sheet, Does.Contain(".jfb-page{background-color:#121417!important}"));
            Assert.That(sheet, Does.Contain(".jfb-surface{background-color:#1c1f24!important;border-color:#363b43!important}"));
            Assert.That(sheet, Does.Contain(".jfb-text{color:#e8eaed!important}"));
            Assert.That(sheet, Does.Contain(".jfb-muted{color:#aab0b9!important}"));
            Assert.That(sheet, Does.Contain(".jfb-link{color:#7fe0c3!important}"), "a layout's own dark colour");
            Assert.That(sheet, Does.Contain(".jfb-rule{border-color:#363b43!important}"));
        });
    }

    [Test]
    public void ALayoutsColours_ReachTheElements_AndBadOnesAreIgnored()
    {
        var html = RenderHtml(Dressed);
        var bad = RenderHtml(new EmailLayoutDesign { Light = new EmailColours { Accent = "red;background:url(https://x.test)", Text = "#12345", Page = "#12345g", Surface = "#ffffff\"" } });

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("color:#0a7d5a;text-decoration:underline"));
            Assert.That(html, Does.Contain("color:#222222"));
            Assert.That(bad, Does.Contain("color:#1f4fbf;text-decoration:underline"));
            Assert.That(bad, Does.Not.Contain("url(").And.Not.Contain("#12345"));
            Assert.DoesNotThrow(() => XDocument.Parse(bad));
        });
    }

    [Test]
    public void ALetter_SitsOnThePageItself_AndACardStandsOnIt()
    {
        var card = Parsed(RenderHtml(EmailLayouts.BuiltIn[EmailLayouts.CardKey]));
        var letter = Parsed(RenderHtml(EmailLayouts.BuiltIn[EmailLayouts.LetterKey]));
        string Style(XDocument document, string name) => (string)document.Descendants(Xhtml("table")).First(table => (string?)table.Attribute("class") == name).Attribute("style")!;

        Assert.Multiple(() =>
        {
            Assert.That(Style(card, "jfb-page"), Does.Contain("background-color:#f2f3f5"));
            Assert.That(Style(card, "jfb-surface"), Does.Contain("border:1px solid").And.Contain("background-color:#ffffff"));
            Assert.That(Style(letter, "jfb-page"), Does.Contain("background-color:#ffffff"));
            Assert.That(Style(letter, "jfb-surface"), Does.Not.Contain("border:"));
        });
    }

    [Test]
    public void TheLayoutsTexts_FollowTheCulture_AndTheLogoSaysWhatItShows()
    {
        var german = RenderHtml(Dressed, culture: "de-CH");
        var french = RenderHtml(Dressed, culture: "fr");
        var noAlt = RenderHtml(Dressed with { LogoAlt = null });

        Assert.Multiple(() =>
        {
            Assert.That(german, Does.Contain("Unser Team").And.Contain("Unsere Adresse").And.Contain("alt=\"Das Logo\""));
            Assert.That(french, Does.Contain("Our team").And.Contain("Our address").And.Contain("alt=\"The logo\""));
            Assert.That(noAlt, Does.Contain("alt=\"Jiron\""));
            Assert.That(german, Does.Contain("src=\"https://jfb.test/logo.png\""));
        });
    }

    [Test]
    public void WhatALogoSays_IsEscaped()
    {
        var html = RenderHtml(Dressed with { LogoAlt = new Dictionary<string, string> { [""] = "\"><script>alert(1)</script>" } });

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Not.Contain("<script"));
            Assert.DoesNotThrow(() => XDocument.Parse(html));
        });
    }

    [TestCase("javascript:alert(1)")]
    [TestCase("//cdn.jfb.test/logo.png")]
    [TestCase("ftp://jfb.test/logo.png")]
    public void ALogoThatIsNoWebAddress_IsLeftOut(string address) =>
        Assert.That(RenderHtml(Dressed with { LogoUrl = address }), Does.Not.Contain("<img"));

    [Test]
    public void ALogoOnTheSite_GoesUnderTheSitesAddress_AndWithoutOneIsLeftOut()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RenderHtml(Dressed with { LogoUrl = "/media/abc/logo.png" }), Does.Contain("<img src=\"https://www.jfb.test/media/abc/logo.png\""));
            Assert.That(RenderHtml(Dressed with { LogoUrl = "/media/abc/logo.png" }, baseUrl: "https://www.jfb.test/de/"), Does.Contain("<img src=\"https://www.jfb.test/media/abc/logo.png\""), "a path is the site's root's");
            Assert.That(RenderHtml(Dressed with { LogoUrl = "/media/abc/logo.png" }, baseUrl: null), Does.Not.Contain("<img"));
            Assert.That(RenderHtml(Dressed with { LogoUrl = "/media/abc/logo.png" }, baseUrl: "ftp://jfb.test"), Does.Not.Contain("<img"));
        });
    }

    [Test]
    public void ALayoutWhoseTextsCannotBeRead_StillGivesTheMail()
    {
        var layout = new EmailLayoutDesign
        {
            Header = new Dictionary<string, string> { ["de"] = "no default text" },
            Footer = new Dictionary<string, string> { [""] = "<p>Fine</p>", ["not a culture name at all"] = "bad" },
        };

        var html = RenderHtml(layout, body: "<p>Still here</p>");

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("Still here"));
            Assert.That(html, Does.Not.Contain("no default text").And.Not.Contain("bad"));
        });
    }

    [Test]
    public void ARightToLeftCulture_TurnsTheDocumentAndItsCells()
    {
        var html = RenderHtml(EmailLayouts.BuiltIn[EmailLayouts.CardKey], culture: "ar");

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("lang=\"ar\" dir=\"rtl\""));
            Assert.That(html, Does.Contain("text-align:right"));
            Assert.That(html, Does.Contain("padding:0 24px 0 0"), "a list's markers are on the right");
            Assert.That(html, Does.Contain("border-right:3px solid"), "a quote's rule too");
            Assert.That(html, Does.Not.Contain("text-align:left"));
        });
    }

    [TestCase("x\" onload=\"alert(1)")]
    [TestCase("not a culture")]
    public void ACultureThatIsNoLanguage_ReadsAsEnglish(string culture) =>
        Assert.That(RenderHtml(EmailLayouts.BuiltIn[EmailLayouts.CardKey], culture: culture), Does.Contain("lang=\"en\" dir=\"ltr\""));

    [TestCase("<script>alert(1)</script><p>Kept</p>", "script")]
    [TestCase("<p onclick=\"alert(1)\">Kept</p>", "onclick")]
    [TestCase("<p style=\"position:fixed\">Kept</p>", "position:fixed")]
    [TestCase("<img src=\"https://x.test/track.png\"><p>Kept</p>", "track.png")]
    [TestCase("<iframe src=\"https://x.test\"></iframe><p>Kept</p>", "iframe")]
    [TestCase("<form action=\"https://x.test\"><input name=\"a\"></form><p>Kept</p>", "<form")]
    [TestCase("<p class=\"evil\">Kept</p>", "evil")]
    [TestCase("<p data-x=\"1\">Kept</p>", "data-x")]
    public void WhatABodyMayNotHold_IsGone(string body, string gone)
    {
        var html = RenderHtml(EmailLayouts.BuiltIn[EmailLayouts.CardKey], body);

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("Kept"));
            Assert.That(html, Does.Not.Contain(gone));
        });
    }

    [Test]
    public void AHarmfulLink_LosesItsAddress()
    {
        var html = RenderHtml(EmailLayouts.BuiltIn[EmailLayouts.CardKey], "<p><a href=\"javascript:alert(1)\">Words stay</a></p>");

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("Words stay"));
            Assert.That(html, Does.Not.Contain("javascript"));
        });
    }

    [Test]
    public void ALinkOnTheSite_StartsWithTheSitesAddress_AndWithoutOneHasNone()
    {
        const string Body = "<p><a href=\"/page?a=1&amp;b=2\">On the site</a><a href=\"mailto:a@jfb.test\">Mail</a><a href=\"tel:+491\">Call</a><a href=\"https://elsewhere.test/x\">Away</a></p>";

        var based = RenderHtml(EmailLayouts.BuiltIn[EmailLayouts.CardKey], Body);
        var unbased = RenderHtml(EmailLayouts.BuiltIn[EmailLayouts.CardKey], Body, baseUrl: null);

        Assert.Multiple(() =>
        {
            Assert.That(based, Does.Contain("href=\"https://www.jfb.test/page?a=1&amp;b=2\""));
            Assert.That(based, Does.Contain("href=\"mailto:a@jfb.test\"").And.Contain("href=\"tel:+491\"").And.Contain("href=\"https://elsewhere.test/x\""));
            Assert.That(unbased, Does.Not.Contain("/page?a=1"));
            Assert.That(unbased, Does.Contain("On the site").And.Contain("href=\"https://elsewhere.test/x\""));
        });
    }

    [TestCase("\\\\evil.test\\share")]
    [TestCase("file:///c:/secret")]
    public void ALinkToAShareOrAFile_LosesItsAddress(string href)
    {
        var html = RenderHtml(EmailLayouts.BuiltIn[EmailLayouts.CardKey], $"<p><a href=\"{href}\">Words stay</a></p>");

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.Contain("Words stay"));
            Assert.That(html, Does.Not.Contain("evil.test").And.Not.Contain("secret"));
        });
    }

    [TestCase("<p>a&#1;b</p>", "ab")]
    [TestCase("<p>a&#xB;b</p>", "ab")]
    [TestCase("<p>a&#xFFFE;b</p>", "ab")]
    [TestCase("<p title=\"a&#1;b\">ab</p>", "ab")]
    public void ANumericReferenceToWhatXmlCannotHold_IsGoneToo(string body, string text)
    {
        var rendered = Renderer.Render(new EmailPage("S", body, "en", EmailLayouts.BuiltIn[EmailLayouts.CardKey]));

        Assert.Multiple(() =>
        {
            Assert.DoesNotThrow(() => XDocument.Parse(rendered.Html));
            Assert.That(rendered.Text, Is.EqualTo(text));
        });
    }

    [Test]
    public void ALinksTarget_IsLeftToTheClient() =>
        Assert.That(RenderHtml(EmailLayouts.BuiltIn[EmailLayouts.CardKey], "<a href=\"https://x.test\" target=\"_blank\">X</a>"), Does.Not.Contain("target="));

    [Test]
    public void WhatXmlCannotHold_IsGone_FromTheBodyAndTheSubject()
    {
        var bad = "a" + (char)0x0001 + "b" + (char)0xd800 + "c" + (char)0x000b + "d";

        var rendered = Renderer.Render(new EmailPage(bad, $"<p>{bad}</p>", "en", EmailLayouts.BuiltIn[EmailLayouts.CardKey]));

        Assert.Multiple(() =>
        {
            Assert.DoesNotThrow(() => XDocument.Parse(rendered.Html));
            Assert.That(rendered.Html, Does.Contain("<title>abcd</title>"));
            Assert.That(rendered.Text, Is.EqualTo("abcd"));
        });
    }

    [Test]
    public void ACharacterBeyondTheBasicPlane_Stays()
    {
        var smile = char.ConvertFromUtf32(0x1f600);

        var rendered = Renderer.Render(new EmailPage(smile, $"<p>{smile}</p>", "en", EmailLayouts.BuiltIn[EmailLayouts.CardKey]));

        Assert.Multiple(() =>
        {
            Assert.DoesNotThrow(() => XDocument.Parse(rendered.Html));
            Assert.That(rendered.Text, Is.EqualTo(smile));
            Assert.That(XDocument.Parse(rendered.Html).Descendants(Xhtml("title")).Single().Value, Is.EqualTo(smile));
        });
    }

    [Test]
    public void TheSubject_IsEscaped_InTheTitle()
    {
        var html = Renderer.Render(new EmailPage("</title><script>alert(1)</script>", "<p>x</p>", "en", EmailLayouts.BuiltIn[EmailLayouts.CardKey])).Html;

        Assert.That(html, Does.Not.Contain("<script"));
    }

    [Test]
    public void AnEmptyBody_StillGivesAWellFormedMail()
    {
        var rendered = Renderer.Render(new EmailPage("Subject", "  ", "en", EmailLayouts.BuiltIn[EmailLayouts.LetterKey]));

        Assert.Multiple(() =>
        {
            Assert.DoesNotThrow(() => XDocument.Parse(rendered.Html));
            Assert.That(rendered.Text, Is.Empty);
        });
    }

    [Test]
    public void TheTextVersion_ReadsLikeTheMail()
    {
        var text = Renderer.Render(new EmailPage("S", Kitchen, "en", Dressed, null, "https://www.jfb.test")).Text;

        Assert.That(text, Is.EqualTo("""
            Our team

            Thank you, Anna

            We have your answers, and a link (https://jfb.test/more) & a path (https://www.jfb.test/on-the-site).

            Next

            Then

            - One
            - Two

            1. First

            A quote

            some  code

            inline

            ---

            Answers

            Name: Value
            Name: Anna

            Our address
            """.ReplaceLineEndings("\n")));
    }

    [Test]
    public void ALinkThatSaysItsAddress_DoesNotSayItTwice_AndMailLinksKeepToTheirWords()
    {
        var text = Renderer.Render(new EmailPage("S", "<p><a href=\"https://jfb.test/a/\">https://jfb.test/a</a> and <a href=\"mailto:a@jfb.test\">Write</a><br>Next line</p>", "en", EmailLayouts.BuiltIn[EmailLayouts.CardKey])).Text;

        Assert.That(text, Is.EqualTo("https://jfb.test/a and Write\nNext line"));
    }

    private static XName Xhtml(string name) => XName.Get(name, "http://www.w3.org/1999/xhtml");

    private static XDocument Parsed(string html) => XDocument.Parse(html);
}
