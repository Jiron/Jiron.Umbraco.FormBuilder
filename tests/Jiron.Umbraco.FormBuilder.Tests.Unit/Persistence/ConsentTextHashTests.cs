using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Persistence;

/// <summary>
/// The name of a consent wording (ADR-0009, ADR-0038): the SHA-256 of the culture, a zero byte and the wording, so the
/// form, the server and the stored row agree on it, and no two pairs of culture and wording share one.
/// </summary>
[TestFixture]
public sealed class ConsentTextHashTests
{
    // Computed apart from the code under test: sha256(utf8(culture) + [0] + utf8(text)) in lowercase hex, with Node.
    [TestCase("de-CH", "Ich willige ein.", "742b82cb184d2b77fa400d5042e25af57dca41ed9f2ecf2b623aa23edd99d87b")]
    [TestCase("", "I agree to the privacy policy.", "34311c5cddd49459b8582f6b552f9b10a867c882e705b34c43b335f0b1c14767")]
    [TestCase("en", "I agree to the privacy policy.", "e43eda3926f94229833d847f6969b261e49fbeba7f5ecb8a4e69e44e32c92db2")]
    public void TheHash_IsTheSha256OfTheCultureAZeroByteAndTheWording(string culture, string text, string expected) =>
        Assert.That(ConsentTextHash.For(culture, text), Is.EqualTo(expected));

    [Test]
    public void TheBoundaryBetweenCultureAndWording_IsPartOfTheHash()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ConsentTextHash.For("de", "-CH yes"), Is.Not.EqualTo(ConsentTextHash.For("de-CH", " yes")));
            Assert.That(ConsentTextHash.For(string.Empty, "en yes"), Is.Not.EqualTo(ConsentTextHash.For("en", " yes")));
        });
    }

    [Test]
    public void TheHash_IsExactAboutTheWording()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ConsentTextHash.For("en", "I agree."), Is.Not.EqualTo(ConsentTextHash.For("en", "I agree. ")), "no trimming");
            Assert.That(ConsentTextHash.For("en", "I agree."), Is.Not.EqualTo(ConsentTextHash.For("en", "i agree.")), "no folding");
        });
    }

    [Test]
    public void WhatTheColumnsCannotHold_OrTheHashCannotTellApart_IsRefused()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ConsentTextHash.For(new string('c', ConsentTextHash.MaxCultureLength), "JFB_TEST"), Has.Length.EqualTo(64), "the longest culture");
            Assert.That(() => ConsentTextHash.For(new string('c', ConsentTextHash.MaxCultureLength + 1), "JFB_TEST"), Throws.ArgumentException);
            Assert.That(() => ConsentTextHash.For("e" + (char)0 + "n", "JFB_TEST"), Throws.ArgumentException, "a NUL would move the boundary");
            Assert.That(() => ConsentTextHash.For("en", string.Empty), Throws.ArgumentException);
            Assert.That(() => ConsentTextHash.For("en", "I agree" + (char)0xD800), Throws.InstanceOf<ArgumentException>(), "a lone surrogate");
            Assert.That(() => ConsentTextHash.For(null!, "JFB_TEST"), Throws.ArgumentNullException);
            Assert.That(() => ConsentTextHash.For("en", null!), Throws.ArgumentNullException);
        });
    }
}
