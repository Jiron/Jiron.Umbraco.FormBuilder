using System.Buffers.Text;
using System.Text;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Microsoft.AspNetCore.DataProtection;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Rendering;

/// <summary>
/// The tokens a post without JavaScript redirects with once it sent the form (ADR-0100): each names the form, the
/// entry's submission id and the instance for 30 minutes on the site's clock, behind the instance it is for, and reads as
/// nothing for another form or instance, once expired, changed, cut, protected for another purpose or by another site, or
/// longer than a page reads.
/// </summary>
public sealed class SentTokensTests
{
    private static readonly Guid Form = Guid.Parse("f0000000-0000-4000-8000-000000000100");
    private static readonly Guid Submission = Guid.Parse("50000000-0000-4000-8000-000000000100");

    private EphemeralDataProtectionProvider _protection = null!;
    private TestTimeProvider _clock = null!;
    private SentTokens _tokens = null!;

    [SetUp]
    public void CreateTheTokens()
    {
        _protection = new EphemeralDataProtectionProvider();
        _clock = new TestTimeProvider();
        _tokens = new SentTokens(_protection, _clock);
    }

    [Test]
    public void AValue_IssuedToExpireAtATime_CountsUntilThen()
    {
        var value = _tokens.Issue(Form, Submission, "jfb-1", _clock.GetUtcNow().AddMinutes(2));
        var expired = _tokens.Issue(Form, Submission, "jfb-1", _clock.GetUtcNow().AddMinutes(-1));
        var counted = _tokens.Read(value, Form, "jfb-1");
        _clock.Advance(TimeSpan.FromMinutes(2));

        Assert.Multiple(() =>
        {
            Assert.That(counted, Is.EqualTo(Submission));
            Assert.That(_tokens.Read(value, Form, "jfb-1"), Is.Null, "at its time");
            Assert.That(_tokens.Read(expired, Form, "jfb-1"), Is.Null, "already expired");
        });
    }

    [Test]
    public void AValue_NamesTheSubmission_ForItsFormAndInstance_BehindTheInstance()
    {
        var value = _tokens.Issue(Form, Submission, "jfb-2");

        Assert.Multiple(() =>
        {
            Assert.That(_tokens.Read(value, Form, "jfb-2"), Is.EqualTo(Submission));
            Assert.That(value, Does.Match("^jfb-2[.][A-Za-z0-9_-]+$"), "the instance, then the token, safe in an address without padding");
            Assert.That(value, Does.Not.Contain(Submission.ToString("N")).And.Not.Contain(Form.ToString("N")), "no key reads from the address");
            Assert.That(SentTokens.Lifetime, Is.EqualTo(TimeSpan.FromMinutes(30)));
            Assert.That(SentTokens.Purpose, Is.EqualTo("Jiron.FormBuilder.Sent.v1"));
            Assert.That(SentTokens.QueryName, Is.EqualTo("jfb-sent"));
            Assert.That(_tokens.Issue(Form, Submission, "jfb-2"), Is.Not.EqualTo(value), "each token is protected anew");
        });
    }

    [Test]
    public void AValue_ReadsAsNothing_ForAnotherFormOrInstance()
    {
        var value = _tokens.Issue(Form, Submission, "jfb-1");
        var token = value["jfb-1.".Length..];

        Assert.Multiple(() =>
        {
            Assert.That(_tokens.Read(value, Guid.NewGuid(), "jfb-1"), Is.Null, "another form");
            Assert.That(_tokens.Read(value, Form, "jfb-2"), Is.Null, "another instance");
            Assert.That(_tokens.Read("jfb-2." + token, Form, "jfb-2"), Is.Null, "the token behind another instance");
            Assert.That(_tokens.Read("jfb-9." + token, Form, "jfb-1"), Is.Null, "another instance in front of it");
            Assert.That(_tokens.Read(value, Form, "jfb-10"), Is.Null, "an instance it begins with");
            Assert.That(_tokens.Read(_tokens.Issue(Form, Submission, "jfb-10"), Form, "jfb-1"), Is.Null, "an instance that begins with it");
            Assert.That(_tokens.Read(token, Form, "jfb-1"), Is.Null, "the token without its instance");
        });
    }

    [Test]
    public void AValue_CountsUntilItExpires_AndNotAfter()
    {
        var value = _tokens.Issue(Form, Submission, "jfb-1");

        _clock.Advance(TimeSpan.FromMinutes(30) - TimeSpan.FromTicks(1));
        var before = _tokens.Read(value, Form, "jfb-1");
        _clock.Advance(TimeSpan.FromTicks(1));
        var at = _tokens.Read(value, Form, "jfb-1");

        Assert.That((before, at), Is.EqualTo(((Guid?)Submission, (Guid?)null)));
    }

    [Test]
    public void AValueChangedCutOrOfAnotherPurposeOrSite_ReadsAsNothing()
    {
        var value = _tokens.Issue(Form, Submission, "jfb-1");
        var bytes = Base64Url.DecodeFromChars(value.AsSpan("jfb-1.".Length));
        bytes[^1] ^= 1;
        var otherPurpose = "jfb-1." + Base64Url.EncodeToString(_protection.CreateProtector("Jiron.FormBuilder.Other.v1").Protect(Payload(1, "jfb-1")));
        var otherSite = new SentTokens(new EphemeralDataProtectionProvider(), _clock).Issue(Form, Submission, "jfb-1");

        Assert.Multiple(() =>
        {
            Assert.That(_tokens.Read("jfb-1." + Base64Url.EncodeToString(bytes), Form, "jfb-1"), Is.Null, "a changed byte");
            Assert.That(_tokens.Read(value.Remove(value.Length - 4), Form, "jfb-1"), Is.Null, "cut short");
            Assert.That(_tokens.Read(otherPurpose, Form, "jfb-1"), Is.Null, "protected for another purpose");
            Assert.That(_tokens.Read(otherSite, Form, "jfb-1"), Is.Null, "another key ring");
            Assert.That(_tokens.Read("jfb-1.not a token!", Form, "jfb-1"), Is.Null, "not base64url");
            Assert.That(_tokens.Read("jfb-1." + new string('A', 600), Form, "jfb-1"), Is.Null, "longer than a page reads");
            Assert.That(_tokens.Read(string.Empty, Form, "jfb-1"), Is.Null);
            Assert.That(_tokens.Read(null, Form, "jfb-1"), Is.Null);
            Assert.That(() => _tokens.Read(value, Form, string.Empty), Throws.ArgumentException);
            Assert.That(() => _tokens.Issue(Form, Submission, string.Empty), Throws.ArgumentException);
        });
    }

    [Test]
    public void APayloadOfAnotherVersionOrWithoutItsInstance_ReadsAsNothing()
    {
        var protector = _protection.CreateProtector(SentTokens.Purpose);

        Assert.Multiple(() =>
        {
            Assert.That(_tokens.Read("jfb-1." + Base64Url.EncodeToString(protector.Protect(Payload(1, "jfb-1"))), Form, "jfb-1"), Is.EqualTo(Submission), "the payload as the tokens write it");
            Assert.That(_tokens.Read("jfb-1." + Base64Url.EncodeToString(protector.Protect(Payload(2, "jfb-1"))), Form, "jfb-1"), Is.Null, "another version");
            Assert.That(_tokens.Read("jfb-1." + Base64Url.EncodeToString(protector.Protect(Payload(1, string.Empty))), Form, "jfb-1"), Is.Null, "no instance");
            Assert.That(_tokens.Read("jfb-1." + Base64Url.EncodeToString(protector.Protect(Payload(1, string.Empty)[..^1])), Form, "jfb-1"), Is.Null, "shorter than the keys and the time");
        });
    }

    // The payload of a token for the form, the submission and an instance, expiring in ten minutes: its version, the two
    // keys, the expiry's ticks big-endian, and the instance in UTF-8.
    private byte[] Payload(byte version, string instance)
    {
        var name = Encoding.UTF8.GetBytes(instance);
        var payload = new byte[1 + 32 + 8 + name.Length];
        payload[0] = version;
        Form.TryWriteBytes(payload.AsSpan(1, 16));
        Submission.TryWriteBytes(payload.AsSpan(17, 16));
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(33), (_clock.GetUtcNow() + TimeSpan.FromMinutes(10)).UtcTicks);
        name.CopyTo(payload.AsSpan(41));
        return payload;
    }
}
