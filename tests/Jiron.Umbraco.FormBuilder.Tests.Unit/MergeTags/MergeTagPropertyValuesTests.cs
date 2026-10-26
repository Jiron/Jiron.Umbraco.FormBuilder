using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Logic;
using NSubstitute;
using NUnit.Framework.Constraints;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Strings;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags;

/// <summary>
/// Content and member values as merge tags show them (ADR-0067): each kind of value Umbraco's converters give, as text in
/// the culture with the wire number or date behind it, lists, rich text as its text, and content names per culture.
/// </summary>
public sealed class MergeTagPropertyValuesTests
{
    private static readonly MergeTagCultures Cultures = new();

    private static CultureFormats British => Cultures.For("en-GB");

    [Test]
    public void TextsAndNumbers_ShowAsTheCultureWritesThem_WithTheWireNumberBehind()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Of("Lindenhall"), IsValue(MergeTagValue.Of("Lindenhall")));
            Assert.That(Of(string.Empty), IsValue(MergeTagValue.Empty));
            Assert.That(Of(null), IsValue(MergeTagValue.Empty));
            Assert.That(Of(1200), IsValue(MergeTagValue.Of(new MergeTagItem("1,200", Number: "1200"))));
            Assert.That(Of(-7L), IsValue(MergeTagValue.Of(new MergeTagItem(CultureFormatting.FormatNumber("-7", null, British)!, Number: "-7"))));
            Assert.That(Of(1234.50m), IsValue(MergeTagValue.Of(new MergeTagItem("1,234.5", Number: "1234.5"))), "without the trailing zero");
            Assert.That(Of(0.1d), IsValue(MergeTagValue.Of(new MergeTagItem("0.1", Number: "0.1"))));
            Assert.That(Of(2.5f), IsValue(MergeTagValue.Of(new MergeTagItem("2.5", Number: "2.5"))));
            Assert.That(Of(double.NaN), IsValue(MergeTagValue.Empty), "no number");
            Assert.That(Of(1e30d), IsValue(MergeTagValue.Empty), "beyond what a decimal holds");
        });
    }

    [Test]
    public void DatesAndTimes_ShowInTheCulturesFormats_WithTheWireDateBehind()
    {
        static MergeTagValue Calendar(string wire, string format) => MergeTagValue.Of(new MergeTagItem(CultureFormatting.FormatCalendar(wire, format, British)!, Date: wire));

        Assert.Multiple(() =>
        {
            Assert.That(Of(new DateOnly(2026, 3, 7)), IsValue(Calendar("2026-03-07", "short")));
            Assert.That(Of(new TimeOnly(18, 30)), IsValue(Calendar("18:30", "time")));
            Assert.That(Of(new TimeOnly(18, 30, 15)), IsValue(Calendar("18:30:15", "time")));
            Assert.That(Of(new DateTime(2026, 3, 7, 0, 0, 0, DateTimeKind.Unspecified)), IsValue(Calendar("2026-03-07", "short")), "a date picker without a time");
            Assert.That(Of(new DateTime(2026, 3, 7, 18, 30, 15, DateTimeKind.Unspecified)), IsValue(Calendar("2026-03-07T18:30:15", "dateTime")));
            Assert.That(Of(new DateTimeOffset(2026, 3, 7, 23, 30, 0, TimeSpan.FromHours(-5))), IsValue(Calendar("2026-03-07T23:30:00", "dateTime")), "its own wall clock");
            Assert.That(Calendar("2026-03-07", "short").Items[0].Text, Is.EqualTo("07/03/2026"));
        });
    }

    [Test]
    public void YesAndNo_AreTheCulturesWords()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Of(true), IsValue(MergeTagValue.Of("Yes")));
            Assert.That(MergeTagPropertyValues.Of(false, "de-DE", Cultures.For("de-DE")), IsValue(MergeTagValue.Of("Nein")));
        });
    }

    [Test]
    public void RichText_ShowsAsItsText()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Of(new HtmlEncodedString("<p>Hello <strong>you</strong></p><p>Fish &amp; chips&nbsp;today</p>")), IsValue(MergeTagValue.Of("Hello you Fish & chips today")));
            Assert.That(Of(new HtmlEncodedString("<p> </p>")), IsValue(MergeTagValue.Empty));
            Assert.That(MergeTagPropertyValues.PlainText("he<b>ll</b>o"), Is.EqualTo("hello"), "inline elements join their words");
            Assert.That(MergeTagPropertyValues.PlainText("one<br>two<BR/>three"), Is.EqualTo("one two three"), "a line break in any case");
            Assert.That(MergeTagPropertyValues.PlainText("<ul><li>a</li><li>b</li></ul>"), Is.EqualTo("a b"));
            Assert.That(MergeTagPropertyValues.PlainText("a<script>alert(1)</script>b<style>p{}</style>c"), Is.EqualTo("abc"), "scripts and styles with what they hold");
            Assert.That(MergeTagPropertyValues.PlainText("a<!-- <p>no</p> -->b<!-- open"), Is.EqualTo("ab"), "comments, an open one too");
            Assert.That(MergeTagPropertyValues.PlainText("1 < 2 and 3"), Is.EqualTo("1 < 2 and 3"), "a lone angle bracket is text");
            Assert.That(MergeTagPropertyValues.PlainText("&lt;b&gt;bold&lt;/b&gt;"), Is.EqualTo("<b>bold</b>"), "characters the markup escaped come back as text");
            Assert.That(MergeTagPropertyValues.PlainText("\n  spaced \t out  \n"), Is.EqualTo("spaced out"));
        });
    }

    [Test]
    public void SeveralValues_AreAList_AndValuesOfOtherKinds_ShowAsNothing()
    {
        var picked = Content(new Dictionary<string, string> { ["en-GB"] = "Events", ["de-CH"] = "Anlaesse" });

        Assert.Multiple(() =>
        {
            Assert.That(Of(new[] { "red", string.Empty, "blue" }), IsValue(MergeTagValue.ListOf([new("red"), new("blue")])), "an empty item left out");
            Assert.That(Of(Array.Empty<string>()), IsValue(MergeTagValue.ListOf([])));
            Assert.That(Of(new List<int> { 1, 2 }), IsValue(MergeTagValue.ListOf([new("1", Number: "1"), new("2", Number: "2")])));
            Assert.That(Of(picked), IsValue(MergeTagValue.Of("Events")), "a picked page by its name in the culture");
            Assert.That(MergeTagPropertyValues.Of(new[] { picked }, "de-CH", Cultures.For("de-CH")), IsValue(MergeTagValue.ListOf([new("Anlaesse")])));
            Assert.That(Of(new Link { Name = "Our events", Url = "/events/" }), IsValue(MergeTagValue.Of("Our events")));
            Assert.That(Of(new Link { Url = "https://example.com/" }), IsValue(MergeTagValue.Of("https://example.com/")), "a link without a name by its address");
            Assert.That(Of(new object()), IsValue(MergeTagValue.Empty), "never the name of a type");
            Assert.That(Of(Guid.Parse("10000000-0000-4000-8000-000000000001")), IsValue(MergeTagValue.Empty));
            Assert.That(Of(new Dictionary<string, string> { ["a"] = "b" }), IsValue(MergeTagValue.ListOf([])), "the items of an unknown kind show as nothing");
        });
    }

    [Test]
    public void AContentItemsName_IsTheOneOfTheCulture_OrTheInvariantOne()
    {
        var variant = Content(new Dictionary<string, string> { ["en-US"] = "Events", ["de-CH"] = "Anlaesse" });
        var invariant = Content(new Dictionary<string, string> { [string.Empty] = "Home" });
        var bare = Content([]);
        bare.Name.Returns("Bare");

        Assert.Multiple(() =>
        {
            Assert.That(MergeTagPropertyValues.Name(variant, "de-ch"), Is.EqualTo("Anlaesse"), "in any letter case");
            Assert.That(MergeTagPropertyValues.Name(variant, "fr-FR"), Is.Empty, "a culture the item has no name in");
            Assert.That(MergeTagPropertyValues.Name(invariant, "fr-FR"), Is.EqualTo("Home"));
            Assert.That(MergeTagPropertyValues.Name(bare, "fr-FR"), Is.EqualTo("Bare"), "an item that lists no cultures");
        });
    }

    [Test]
    public void AProperty_HasAValueOnlyWhereUmbracoSaysSo()
    {
        var umbraco = new TestUmbraco();
        var page = umbraco.Page(Guid.NewGuid(), "Events", properties: new Dictionary<string, object?>
        {
            ["title"] = new Dictionary<string, object?> { ["en-GB"] = "Join us", ["de-CH"] = null },
            ["seats"] = 40,
        });

        Assert.Multiple(() =>
        {
            Assert.That(MergeTagPropertyValues.Of(page, "title", "en-GB", British), IsValue(MergeTagValue.Of("Join us")));
            Assert.That(MergeTagPropertyValues.Of(page, "title", "de-CH", British), Is.Null, "no value in the culture");
            Assert.That(MergeTagPropertyValues.Of(page, "seats", "de-CH", British), IsValue(MergeTagValue.Of(new MergeTagItem("40", Number: "40"))), "an invariant value in every culture");
            Assert.That(MergeTagPropertyValues.Of(page, "missing", "en-GB", British), Is.Null, "a property the type lacks");
        });
    }

    private static MergeTagValue Of(object? value) => MergeTagPropertyValues.Of(value, "en-GB", British);

    // Values compare by their items, which compare by their texts and what is behind them.
    private static EqualConstraint IsValue(MergeTagValue expected) =>
        Is.EqualTo(expected).Using<MergeTagValue>((actual, wanted) => actual.IsList == wanted.IsList && actual.Items.SequenceEqual(wanted.Items));

    private static IPublishedContent Content(Dictionary<string, string> names)
    {
        var content = Substitute.For<IPublishedContent>();
        content.Cultures.Returns(names.ToDictionary(name => name.Key, name => new PublishedCultureInfo(name.Key, name.Value, null, DateTime.UnixEpoch)));
        return content;
    }
}
