using Jiron.Umbraco.FormBuilder.Core.MergeTags;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Emails;

/// <summary>What keeps a client from making a link of what a visitor typed (ROADMAP 8.3, section 4.10, ADR-0160).</summary>
public sealed class VisitorLinksTests
{
    private static readonly string Zero = ((char)0x200b).ToString();

    [TestCase("https://evil.test/x", "https:Z//evil.Ztest/x")]
    [TestCase("http://1.2.3.4/x", "http:Z//1.Z2.Z3.Z4/x")]
    [TestCase("1.2.3.4/x", "1.Z2.Z3.Z4/x")]
    [TestCase("evil\u3002test", "evil\u3002Ztest")]
    [TestCase("evil\uff0etest", "evil\uff0eZtest")]
    [TestCase("evil\uff61test", "evil\uff61Ztest")]
    [TestCase("evil.", "evil.Z")]
    [TestCase("www.evil.test", "www.Zevil.Ztest")]
    [TestCase("evil.test", "evil.Ztest")]
    [TestCase("mail@evil.test", "mail@evil.Ztest")]
    public void WhatAClientWouldLink_IsBroken(string text, string broken) =>
        Assert.That(VisitorLinks.Broken(text), Is.EqualTo(broken.Replace("Z", Zero, StringComparison.Ordinal)).Using((IEqualityComparer<string>)StringComparer.Ordinal));

    [TestCase("Just words, then more. And more")]
    [TestCase("A time: 10:30, no slashes")]
    [TestCase("")]
    public void WhatNoClientWouldLink_StaysAsItIs(string text) =>
        Assert.That(VisitorLinks.Broken(text), Is.EqualTo(text).Using((IEqualityComparer<string>)StringComparer.Ordinal));

    [Test]
    public void AValue_IsBroken_InItsItemsAndItsMarkup_AndKeepsWhatIsBehindIt()
    {
        var value = new MergeTagValue([new MergeTagItem("evil.test", Number: "1.5")], false) { Html = "<td>evil.test</td>" };

        var broken = VisitorLinks.Broken(value)!;

        Assert.Multiple(() =>
        {
            Assert.That(broken.Items[0].Text.Contains(Zero, StringComparison.Ordinal), Is.True);
            Assert.That(broken.Items[0].Number, Is.EqualTo("1.5"));
            Assert.That(broken.Html!.Contains(Zero, StringComparison.Ordinal), Is.True);
            Assert.That(VisitorLinks.Broken((MergeTagValue?)null), Is.Null);
        });
    }

    [TestCase("field", "message", true)]
    [TestCase("query", "x", true)]
    [TestCase("context", "x", true)]
    [TestCase("member", "name", true)]
    [TestCase("custom", "x", true)]
    [TestCase("entry", "answers", true)]
    [TestCase("entry", "table", true)]
    [TestCase("entry", "url", false)]
    [TestCase("entry", "number", false)]
    [TestCase("form", "name", false)]
    [TestCase("page", "url", false)]
    [TestCase("site", "name", false)]
    [TestCase("date", "today", false)]
    [TestCase("dictionary", "x", false)]
    public void OnlyWhatCanHoldVisitorText_IsTouched(string source, string path, bool touched) =>
        Assert.That(VisitorLinks.CanHoldVisitorText(source, path), Is.EqualTo(touched));
}
