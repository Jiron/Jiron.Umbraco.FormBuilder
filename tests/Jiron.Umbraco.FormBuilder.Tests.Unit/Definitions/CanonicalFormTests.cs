using System.Security.Cryptography;
using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Definitions;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Definitions;

/// <summary>
/// The canonical form of ADR-0002 and ADR-0034: a fully populated document reads and writes byte for byte, any other
/// spelling of it writes the same bytes, and the hash is the SHA-256 of those bytes.
/// </summary>
public sealed class CanonicalFormTests
{
    [Test]
    public void FullDocument_ReadsAndWrites_ByteForByte()
    {
        var expected = DefinitionFixtures.Minify(DefinitionFixtures.Full);

        var written = FormDefinitionSerializer.WriteString(DefinitionFixtures.Read(DefinitionFixtures.Full));

        Assert.That(written, Is.EqualTo(expected));
    }

    [Test]
    public void FullDocument_WrittenAgain_ReadsToTheSameModel()
    {
        var model = DefinitionFixtures.Read(DefinitionFixtures.Full);

        var again = DefinitionFixtures.Read(FormDefinitionSerializer.WriteString(model));

        Assert.That(DefinitionComparer.Differences(model, again), Is.Empty);
    }

    [Test]
    public void Hash_IsTheSha256OfTheCanonicalBytes_InLowercaseHex()
    {
        var bytes = FormDefinitionSerializer.Write(DefinitionFixtures.Read(DefinitionFixtures.Full));

        var hash = FormDefinitionSerializer.Hash(bytes);

        Assert.Multiple(() =>
        {
            Assert.That(hash, Is.EqualTo(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(DefinitionFixtures.Minify(DefinitionFixtures.Full)))).ToLowerInvariant()));
            Assert.That(hash, Does.Match("^[0-9a-f]{64}$"));
        });
    }
}
