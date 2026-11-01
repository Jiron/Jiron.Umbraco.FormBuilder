using System.Buffers.Text;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Jiron.Umbraco.FormBuilder.Web.PublicApi;
using Microsoft.AspNetCore.DataProtection;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.PublicApi;

/// <summary>
/// The tokens an upload answers with (ADR-0087): each names the form, the field and the file's key for 24 hours on the
/// site's clock, and reads as nothing for another form, once expired, changed, cut, protected for another purpose or by
/// another site, or longer than any answer takes.
/// </summary>
public sealed class UploadTokensTests
{
    private static readonly Guid Form = Guid.Parse("f0000000-0000-4000-8000-000000000087");
    private static readonly Guid Field = Guid.Parse("30000000-0000-4000-8000-000000000087");
    private static readonly Guid File = Guid.Parse("50000000-0000-4000-8000-000000000087");

    private EphemeralDataProtectionProvider _protection = null!;
    private TestTimeProvider _clock = null!;
    private UploadTokens _tokens = null!;

    [SetUp]
    public void CreateTheTokens()
    {
        _protection = new EphemeralDataProtectionProvider();
        _clock = new TestTimeProvider();
        _tokens = new UploadTokens(_protection, _clock);
    }

    [Test]
    public void AToken_NamesTheFieldAndTheFile_ForItsForm_For24Hours()
    {
        var token = _tokens.Issue(Form, Field, File);

        var read = _tokens.Read(token, Form);

        Assert.Multiple(() =>
        {
            Assert.That(read, Is.EqualTo(new UploadToken(Field, File, _clock.GetUtcNow() + TimeSpan.FromHours(24))));
            Assert.That(UploadTokens.Lifetime, Is.EqualTo(TimeSpan.FromHours(24)));
            Assert.That(token, Does.Match("^[A-Za-z0-9_-]+$"), "safe in a URL and in JSON, without padding");
            Assert.That(token, Does.Not.Contain(File.ToString("N")).And.Not.Contain(Field.ToString("N")));
            Assert.That(_tokens.Issue(Form, Field, File), Is.Not.EqualTo(token), "each token is protected anew");
        });
    }

    [Test]
    public void AToken_NamesItsFile_OnlyForItsFormAndItsField()
    {
        var token = _tokens.Issue(Form, Field, File);

        Assert.Multiple(() =>
        {
            Assert.That(_tokens.FileOf(token, Form, Field, null), Is.EqualTo(File));
            Assert.That(_tokens.FileOf(token, Form, Guid.NewGuid(), null), Is.Null, "another field");
            Assert.That(_tokens.FileOf(token, Guid.NewGuid(), Field, null), Is.Null, "another form");
            Assert.That(_tokens.FileOf("not a token!", Form, Field, null), Is.Null);
        });
    }

    [Test]
    public void AToken_CountsUntilItExpires_AndNotAfter()
    {
        var token = _tokens.Issue(Form, Field, File);

        _clock.Advance(TimeSpan.FromHours(24) - TimeSpan.FromTicks(1));
        var before = _tokens.Read(token, Form);
        _clock.Advance(TimeSpan.FromTicks(1));
        var at = _tokens.Read(token, Form);

        Assert.That((before is not null, at is not null), Is.EqualTo((true, false)));
    }

    [Test]
    public void ATokenOfAnotherFormOrPurposeOrSite_OrChanged_ReadsAsNothing()
    {
        var token = _tokens.Issue(Form, Field, File);
        var bytes = Base64Url.DecodeFromChars(token);
        bytes[^1] ^= 1;
        var otherPurpose = Base64Url.EncodeToString(_protection.CreateProtector("Jiron.FormBuilder.Other.v1").Protect(Base64Url.DecodeFromChars(Plain())));
        var otherSite = new UploadTokens(new EphemeralDataProtectionProvider(), _clock).Issue(Form, Field, File);

        Assert.Multiple(() =>
        {
            Assert.That(_tokens.Read(token, Guid.NewGuid()), Is.Null, "another form");
            Assert.That(_tokens.Read(Base64Url.EncodeToString(bytes), Form), Is.Null, "a changed byte");
            Assert.That(_tokens.Read(token[..^4], Form), Is.Null, "cut short");
            Assert.That(_tokens.Read(otherPurpose, Form), Is.Null, "protected for another purpose");
            Assert.That(_tokens.Read(otherSite, Form), Is.Null, "another key ring");
            Assert.That(_tokens.Read("not a token!", Form), Is.Null, "not base64url");
            Assert.That(_tokens.Read(new string('A', 1025), Form), Is.Null, "longer than any answer takes");
            Assert.That(_tokens.Read(string.Empty, Form), Is.Null);
            Assert.That(_tokens.Read(null, Form), Is.Null);
        });
    }

    [Test]
    public void APayloadOfAnotherVersionOrLength_ReadsAsNothing()
    {
        var protector = _protection.CreateProtector(UploadTokens.Purpose);
        var payload = Base64Url.DecodeFromChars(Plain());
        var otherVersion = (byte[])payload.Clone();
        otherVersion[0] = 2;

        Assert.Multiple(() =>
        {
            Assert.That(_tokens.Read(Base64Url.EncodeToString(protector.Protect(payload)), Form), Is.Not.Null, "the payload as the tokens write it");
            Assert.That(_tokens.Read(Base64Url.EncodeToString(protector.Protect(otherVersion)), Form), Is.Null);
            Assert.That(_tokens.Read(Base64Url.EncodeToString(protector.Protect(payload[..^1])), Form), Is.Null);
            Assert.That(_tokens.Read(Base64Url.EncodeToString(protector.Protect([.. payload, 0])), Form), Is.Null);
        });
    }

    // The payload of a token for the form, the field and the file, with no challenge, expiring in a day, in base64url: its
    // version, the four keys and the expiry's ticks, big-endian.
    private string Plain()
    {
        var payload = new byte[1 + 64 + 8];
        payload[0] = 1;
        Form.TryWriteBytes(payload.AsSpan(1, 16));
        Field.TryWriteBytes(payload.AsSpan(17, 16));
        File.TryWriteBytes(payload.AsSpan(33, 16));
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(65), (_clock.GetUtcNow() + TimeSpan.FromDays(1)).UtcTicks);
        return Base64Url.EncodeToString(payload);
    }
}
