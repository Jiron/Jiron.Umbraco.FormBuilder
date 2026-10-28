using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Persistence;

/// <summary>
/// The key of an option's count (ADR-0033, ADR-0090): the SHA-256 of the value's UTF-8 bytes in lowercase hex, exact
/// about every code unit, and refused for a value no option has.
/// </summary>
[TestFixture]
public sealed class OptionValueHashTests
{
    // Computed apart from the code under test: sha256(utf8(value)) in lowercase hex, with Node.
    [TestCase("a", "ca978112ca1bbdcafac231b39a23dc4da786eff8147c4e72b9807785afee48bb")]
    [TestCase("A", "559aead08264d5795d3909718cdd05abd49572e84fe55590eef31a88a08fdffd")]
    [TestCase("Workshop 1", "19ed101e5abbec85a91e8acf9a472541391e7ce8fff0d5b537fb31fb29c00916")]
    public void TheHash_IsTheSha256OfTheValuesBytes(string value, string expected) =>
        Assert.That(OptionValueHash.For(value), Is.EqualTo(expected));

    [Test]
    public void ALetterOutsideAscii_IsHashedAsItsUtf8Bytes() =>
        Assert.That(OptionValueHash.For("x" + (char)0xFC), Is.EqualTo("da289ac898a8d09b562f171f5ef7eb4dde0aca18d37221cbe0a829893c752882"));

    [Test]
    public void AValueNoOptionHas_IsRefused()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => OptionValueHash.For(string.Empty), Throws.ArgumentException);
            Assert.That(() => OptionValueHash.For("a" + (char)0xD800), Throws.ArgumentException.With.InnerException.InstanceOf<System.Text.EncoderFallbackException>(), "a lone surrogate would hash as a replacement character");
            Assert.That(() => OptionValueHash.For(null!), Throws.ArgumentNullException);
        });
    }
}
