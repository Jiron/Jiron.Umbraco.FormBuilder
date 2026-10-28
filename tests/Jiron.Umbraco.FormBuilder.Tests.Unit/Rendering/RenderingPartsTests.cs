using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Rendering;

/// <summary>
/// The small parts of the Razor rendering (ADR-0093): the model data's escapes, the attributes the views write, the
/// instance ids of a request, the state a post leaves for a form, the package's words by culture, and the form
/// reference a page holds.
/// </summary>
[TestFixture]
public sealed class RenderingPartsTests
{
    private static readonly Guid Form = Guid.Parse("30000000-0000-4000-8000-0000000062c1");

    [Test]
    public void TheModelData_EscapesWhatCouldEndTheScriptElement_AndReadsBackAsTheSameAnswer()
    {
        var json = "{\"text\":\"</script><!-- & \"" + ",\"lines\":\"a" + (char)0x2028 + "b" + (char)0x2029 + "c\",\"plain\":\"" + (char)0xFC + "ber\"}";

        var data = Write(ModelData.Of(Encoding.UTF8.GetBytes(json)));

        Assert.Multiple(() =>
        {
            Assert.That(data, Is.EqualTo("{\"text\":\"" + Escaped(0x3c) + "/script" + Escaped(0x3e) + Escaped(0x3c) + "!-- " + Escaped(0x26) + " \",\"lines\":\"a" + Escaped(0x2028) + "b" + Escaped(0x2029) + "c\",\"plain\":\"" + (char)0xFC + "ber\"}"));
            Assert.That(data, Does.Not.Contain("<").And.Not.Contain(">").And.Not.Contain("&"));
            Assert.That(JsonNode.DeepEquals(JsonNode.Parse(data), JsonNode.Parse(json)), Is.True);
        });
    }

    [Test]
    public void AFlag_IsTheBareName_WhenOn_AndNothingWhenOff()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Write(FormBuilderHtml.Flag("data-jfb-hidden", on: true)), Is.EqualTo("data-jfb-hidden"));
            Assert.That(Write(FormBuilderHtml.Flag("data-jfb-hidden", on: false)), Is.Empty);
        });
    }

    [Test]
    public void AnAttribute_HoldsItsValueEncoded_AndIsNothingWithoutAValue()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Write(FormBuilderHtml.Attribute("data-jfb-alias", "a\"<b>&c")), Is.EqualTo("data-jfb-alias=\"a&quot;&lt;b&gt;&amp;c\""));
            Assert.That(Write(FormBuilderHtml.Attribute("data-jfb-alias", string.Empty)), Is.EqualTo("data-jfb-alias=\"\""), "an empty value is a value");
            Assert.That(Write(FormBuilderHtml.Attribute("data-jfb-alias", null)), Is.Empty);
        });
    }

    [TestCase("")]
    [TestCase(" ")]
    public void AnAttributeWithoutAName_IsRefused(string name)
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => FormBuilderHtml.Flag(name, on: true), Throws.InstanceOf<ArgumentException>());
            Assert.That(() => FormBuilderHtml.Attribute(name, "value"), Throws.InstanceOf<ArgumentException>());
        });
    }

    [Test]
    public void TheInstanceIds_CountTheFormsOfEachRequest()
    {
        var one = new DefaultHttpContext();
        var other = new DefaultHttpContext();

        Assert.Multiple(() =>
        {
            Assert.That(FormInstances.Next(one), Is.EqualTo("jfb-1"));
            Assert.That(FormInstances.Next(one), Is.EqualTo("jfb-2"));
            Assert.That(FormInstances.Next(other), Is.EqualTo("jfb-1"), "each request counts its own");
            Assert.That(FormInstances.Next(one), Is.EqualTo("jfb-3"));
        });
    }

    [Test]
    public void APostsState_WaitsForItsFormAndInstance_Only()
    {
        var http = new DefaultHttpContext();
        var state = new FormPostState { Page = Form, Notice = new FormPostNotice(null, "Check your answers.") };

        FormPosts.Set(http, Form, "jfb-2", state);

        Assert.Multiple(() =>
        {
            Assert.That(FormPosts.Get(http, Form, "jfb-2"), Is.SameAs(state));
            Assert.That(FormPosts.Get(http, Form, "jfb-1"), Is.Null, "another instance of the form");
            Assert.That(FormPosts.Get(http, Guid.NewGuid(), "jfb-2"), Is.Null, "another form");
            Assert.That(FormPosts.Get(new DefaultHttpContext(), Form, "jfb-2"), Is.Null, "another request");
        });
    }

    [TestCase("de", "Senden")]
    [TestCase("de-CH", "Senden")]
    [TestCase("de-DE", "Senden")]
    [TestCase("en-GB", "Send")]
    [TestCase("fr-FR", "Send")]
    [TestCase("", "Send")]
    [TestCase(null, "Send")]
    public void TheWords_OfACulture_AreItsOwn_ElseItsMoreGeneralCulturesOrEnglish(string? culture, string send) =>
        Assert.That(RenderingWords.Of(culture).Send, Is.EqualTo(send));

    [Test]
    public void TheWordsOfEachCulture_AreAllThere_AndTheirPlaceholdersFormat()
    {
        Assert.That(RenderingWords.Cultures, Is.EquivalentTo(new[] { "en", "de" }));
        foreach (var culture in RenderingWords.Cultures)
        {
            var words = RenderingWords.Of(culture);
            var texts = new[]
            {
                words.Back, words.Next, words.Send, words.Optional, words.Asterisk, words.ErrorSummary, words.Progress,
                words.ConfirmEmail, words.OpensText, words.ClosedHeading, words.ClosedText, words.FullHeading, words.FullText,
            };
            Assert.Multiple(() =>
            {
                Assert.That(texts, Has.All.Not.Empty, culture);
                Assert.That(Format(words.RequiredNote, "*"), Does.Contain("*"), culture);
                Assert.That(Format(words.Step, 2, 3), Does.Contain("2").And.Contain("3"), culture);
                Assert.That(Format(words.Page, 4), Does.Contain("4"), culture);
                Assert.That(Format(words.Counter, 40), Does.Contain("40"), culture);
                Assert.That(Format(words.Reference, "REQ-1"), Does.Contain("REQ-1"), culture);
                Assert.That(Format(words.OpensHeading, "1 May"), Does.Contain("1 May"), culture);
            });
        }
    }

    [Test]
    public void TheGermanWords_HoldTheirUmlauts()
    {
        var words = RenderingWords.Of("de");

        Assert.Multiple(() =>
        {
            Assert.That(words.Back, Is.EqualTo("Zur" + (char)0xFC + "ck"));
            Assert.That(words.ErrorSummary, Is.EqualTo("Diese Antworten brauchen eine " + (char)0xC4 + "nderung"));
            Assert.That(words.OpensHeading, Is.EqualTo("Dieses Formular " + (char)0xF6 + "ffnet am {0}"));
        });
    }

    [Test]
    public void AFormReference_ReadsAsItsKey()
    {
        var reference = new FormBuilderFormReference(Form);

        Assert.Multiple(() =>
        {
            Assert.That(reference.ToString(), Is.EqualTo("30000000-0000-4000-8000-0000000062c1"));
            Assert.That(reference, Is.EqualTo(new FormBuilderFormReference(Form)));
        });
    }

    // The escape JSON reads back as the character.
    private static string Escaped(int code) => (char)92 + "u" + code.ToString("x4", CultureInfo.InvariantCulture);

    private static string Format(string text, params object[] values) => string.Format(CultureInfo.InvariantCulture, text, values);

    private static string Write(IHtmlContent content)
    {
        using var writer = new StringWriter();
        content.WriteTo(writer, HtmlEncoder.Default);
        return writer.ToString();
    }
}
