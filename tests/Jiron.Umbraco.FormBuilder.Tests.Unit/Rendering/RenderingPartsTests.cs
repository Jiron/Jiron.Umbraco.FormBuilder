using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Http;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Rendering;

/// <summary>
/// The small parts of the Razor rendering (ADR-0093): the model data's escapes, the attributes the views write, the
/// instance ids of a request, the state a post leaves for a form, what reading a post's files found and a kept file's
/// size (ADR-0102), the package's words by culture, and the form reference a page holds.
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

    [TestCase("jfb-1", true)]
    [TestCase("jfb-42", true)]
    [TestCase("jfb-123456789012", true)]
    [TestCase("jfb-1234567890123", false)]
    [TestCase("jfb-", false)]
    [TestCase("jfb-one", false)]
    [TestCase("jfb-1a", false)]
    [TestCase("jfb--1", false)]
    [TestCase("form-1", false)]
    [TestCase("", false)]
    public void AnInstanceId_IsThePrefixAndANumber_NoLongerThanAPageReaches(string instance, bool isId) =>
        Assert.That(FormInstances.IsId(instance), Is.EqualTo(isId));

    [Test]
    public void WhatReadingAPostsFilesFound_WaitsForItsFormAndInstance_Only()
    {
        var http = new DefaultHttpContext();
        var read = new FormPostFileRead([new FormPostFileNote("cv", FieldMessageKinds.FileSize)], IsRefused: false);

        FormPostFileNotes.Set(http, Form, "jfb-2", read);

        Assert.Multiple(() =>
        {
            Assert.That(FormPostFileNotes.Get(http, Form, "jfb-2"), Is.SameAs(read));
            Assert.That(FormPostFileNotes.Get(http, Form, "jfb-1"), Is.Null, "another instance of the form");
            Assert.That(FormPostFileNotes.Get(http, Guid.NewGuid(), "jfb-2"), Is.Null, "another form");
            Assert.That(FormPostFileNotes.Get(new DefaultHttpContext(), Form, "jfb-2"), Is.Null, "another request");
            Assert.That(FormPosts.Get(http, Form, "jfb-2"), Is.Null, "apart from the post's state");
        });
    }

    [TestCase(0L, "en", "0 KB")]
    [TestCase(1L, "en", "0.1 KB")]
    [TestCase(100L, "en", "0.1 KB")]
    [TestCase(1536L, "en", "1.5 KB")]
    [TestCase(2048L, "en", "2 KB")]
    [TestCase(40_000L, "en", "39.1 KB")]
    [TestCase(40_000L, "de", "39,1 KB")]
    [TestCase(1_048_524L, "en", "1023.9 KB")]
    [TestCase(1_048_525L, "en", "1 MB")]
    [TestCase(1_048_576L, "en", "1 MB")]
    [TestCase(1_500_000L, "en", "1.4 MB")]
    [TestCase(1_500_000L, "de", "1,4 MB")]
    [TestCase(10_485_760L, "en", "10 MB")]
    public void AKeptFilesSize_IsInKilobytesUntilAMegabyte_WithOnePlaceOnlyWhenItIsNotWhole(long bytes, string culture, string shown) =>
        Assert.That(FormModels.SizeOf(bytes, RenderingWords.Of(culture), CultureInfo.GetCultureInfo(culture)), Is.EqualTo(shown));

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
                words.Back, words.Next, words.Send, words.Optional, words.Asterisk, words.Progress,
                words.ConfirmEmail, words.OpensText, words.ClosedHeading, words.ClosedText, words.FullHeading, words.FullText,
                words.Other, words.OtherText, words.Choose, words.ChooseCountry, words.SeveralHint, words.NoChoices,
                words.ChoicesUnavailable, words.NameTitle, words.NameFirst, words.NameMiddle, words.NameLast, words.Street,
                words.Street2, words.Postcode, words.City, words.Region, words.Country, words.SignatureName,
                words.SignatureTyped, words.TooLargeText,
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
                Assert.That(Format(words.MoveUp, "Seats"), Does.Contain("Seats"), culture);
                Assert.That(Format(words.MoveDown, "Seats"), Does.Contain("Seats").And.Not.EqualTo(Format(words.MoveUp, "Seats")), culture);
                Assert.That(Format(words.Row, 2), Does.Contain("2"), culture);
                Assert.That(Format(words.KeepFile, "cv.pdf", "2 KB"), Does.Contain("cv.pdf (2 KB)"), culture);
                Assert.That((Format(words.Kilobytes, 2), Format(words.Megabytes, 2)), Is.EqualTo(("2 KB", "2 MB")), culture);
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
            Assert.That(words.OpensHeading, Is.EqualTo("Dieses Formular " + (char)0xF6 + "ffnet am {0}"));
            Assert.That(words.Choose, Is.EqualTo("Bitte w" + (char)0xE4 + "hlen"));
            Assert.That(words.SeveralHint, Does.Contain("gedr" + (char)0xFC + "ckt"));
            Assert.That(words.Street, Is.EqualTo("Stra" + (char)0xDF + "e und Hausnummer"));
            Assert.That(words.SignatureName, Is.EqualTo("Ihr vollst" + (char)0xE4 + "ndiger Name"));
            Assert.That(words.TooLargeText, Does.StartWith("Die Dateien waren zu gro" + (char)0xDF + ",").And.Contain("W" + (char)0xE4 + "hlen").And.Contain("f" + (char)0xFC + "llen"));
            Assert.That(words.KeepFile, Is.EqualTo("{0} ({1}) behalten"));
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
