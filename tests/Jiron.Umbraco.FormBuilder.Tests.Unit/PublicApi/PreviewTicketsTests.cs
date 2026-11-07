using System.Buffers.Text;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Designer;
using Jiron.Umbraco.FormBuilder.Web.PublicApi;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.PublicApi;

/// <summary>
/// The preview's tickets (ADR-0203): each names the form and the version for ten minutes on the site's clock, and reads
/// as nothing once expired, changed, cut, protected for another purpose or by another site, or longer than any is; the
/// frame sends one after the scheme <c>Jfb-Preview</c> in any letter case, and the scheme alone or twice carries one that
/// does not hold. A ticket never prints its value, nor does a record that holds one: the document and the answers. With
/// 10.3f (ADR-0204) a ticket says whether the test entries sent with it run their actions; one with a flag no ticket sets,
/// or of the first layout, reads as nothing.
/// </summary>
public sealed class PreviewTicketsTests
{
    private static readonly Guid Form = Guid.Parse("f0000000-0000-4000-8000-000000000203");

    private EphemeralDataProtectionProvider _protection = null!;
    private TestTimeProvider _clock = null!;
    private PreviewTickets _tickets = null!;

    [SetUp]
    public void CreateTheTickets()
    {
        _protection = new EphemeralDataProtectionProvider();
        _clock = new TestTimeProvider();
        _tickets = new PreviewTickets(_protection, _clock);
    }

    [Test]
    public void ATicket_NamesTheFormAndTheVersion_ForTenMinutes()
    {
        var issued = _tickets.Issue(Form, 7);

        var read = _tickets.Read(issued.Value);
        _clock.Advance(PreviewTickets.Lifetime - TimeSpan.FromTicks(1));
        var last = _tickets.Read(issued.Value);
        _clock.Advance(TimeSpan.FromTicks(1));

        Assert.Multiple(() =>
        {
            Assert.That((read?.FormKey, read?.Version, read?.ExpiresUtc), Is.EqualTo(((Guid?)Form, (int?)7, (DateTimeOffset?)issued.ExpiresUtc)));
            Assert.That(issued.ExpiresUtc, Is.EqualTo(_clock.GetUtcNow()), "ten minutes after it was issued");
            Assert.That(PreviewTickets.Lifetime, Is.EqualTo(TimeSpan.FromMinutes(10)));
            Assert.That(last, Is.Not.Null, "until the last tick");
            Assert.That(_tickets.Read(issued.Value), Is.Null, "expired");
            Assert.That(issued.Value, Does.Match("^[A-Za-z0-9_-]+$"), "a header's token, without padding");
            Assert.That(issued.Value, Does.Not.Contain(Form.ToString("N")));
            Assert.That(issued.ToString(), Is.EqualTo(nameof(PreviewTicket)), "a log never prints the ticket");
            Assert.That(() => _tickets.Issue(Form, 0), Throws.TypeOf<ArgumentOutOfRangeException>());
        });
    }

    [Test]
    public void TheRecordsThatHoldATicket_PrintTheirNamesAlone()
    {
        var issued = _tickets.Issue(Form, 7);
        var document = new FormPreviewDocument("<meta content=\"" + issued.Value + "\">", DarkScheme: true, issued);
        object[] records =
        [
            document,
            new FormPreviewResult(FormPreviewStatus.Done, document),
            new FormPreviewResponseModel { Document = document.Html, Version = 7, Culture = "en-US", DarkScheme = true, LifetimeSeconds = 600 },
            new FormPreviewTicketResponseModel { Ticket = issued.Value, LifetimeSeconds = 600 },
        ];

        Assert.That(records.Select(record => record.ToString()), Is.EqualTo(records.Select(record => record.GetType().Name)), "a log never prints the ticket");
    }

    [Test]
    public void ATicket_ReadsAsNothing_ChangedCutOrTooLong_ProtectedForAnotherPurpose_OrByAnotherSite()
    {
        var value = _tickets.Issue(Form, 7).Value;
        var bytes = Base64Url.DecodeFromChars(value);
        bytes[^1] ^= 1;
        var upload = new UploadTokens(_protection, _clock).Issue(Form, Guid.NewGuid(), Guid.NewGuid());
        var otherSite = new PreviewTickets(new EphemeralDataProtectionProvider(), _clock).Issue(Form, 7).Value;

        Assert.Multiple(() =>
        {
            Assert.That(_tickets.Read(Base64Url.EncodeToString(bytes)), Is.Null, "changed");
            Assert.That(_tickets.Read(value[..^4]), Is.Null, "cut");
            Assert.That(_tickets.Read(value + new string('A', PreviewTickets.MaxLength)), Is.Null, "longer than any");
            Assert.That(_tickets.Read(upload), Is.Null, "another purpose's token");
            Assert.That(_tickets.Read(otherSite), Is.Null, "another site's key ring");
            Assert.That(_tickets.Read("not a ticket!"), Is.Null);
            Assert.That(_tickets.Read(string.Empty), Is.Null);
            Assert.That(_tickets.Read(null), Is.Null);
        });
    }

    [Test]
    public void ATicket_SaysWhetherItsTestEntriesRunTheirActions_AndAnUnknownFlagOrTheFirstLayout_ReadsAsNothing()
    {
        var protector = _protection.CreateProtector(PreviewTickets.Purpose);

        // A ticket that runs actions, its payload changed and protected again as the site would.
        string Changed(Func<byte[], byte[]> change)
        {
            var payload = protector.Unprotect(Base64Url.DecodeFromChars(_tickets.Issue(Form, 7, runsActions: true).Value));
            return Base64Url.EncodeToString(protector.Protect(change(payload)));
        }

        Assert.Multiple(() =>
        {
            Assert.That(_tickets.Read(_tickets.Issue(Form, 7, runsActions: true).Value)?.RunsActions, Is.True);
            Assert.That(_tickets.Issue(Form, 7, runsActions: true).RunsActions, Is.True);
            Assert.That(_tickets.Read(_tickets.Issue(Form, 7).Value)?.RunsActions, Is.False, "running nothing unless asked");
            Assert.That(_tickets.Read(Changed(payload => payload))?.RunsActions, Is.True, "protected again unchanged, it holds");
            Assert.That(_tickets.Read(Changed(payload => { payload[^1] = 2; return payload; })), Is.Null, "a flag no ticket sets");
            Assert.That(_tickets.Read(Changed(payload => { payload[^1] = 3; return payload; })), Is.Null);
            Assert.That(_tickets.Read(Changed(payload => { payload[0] = 1; return payload; })), Is.Null, "the first layout's version");
            Assert.That(_tickets.Read(Changed(payload => { payload[0] = 1; return payload[..^1]; })), Is.Null, "a ticket of the first layout");
        });
    }

    [TestCase(null, false, null)]
    [TestCase("Jfb-Key some-key", false, null)]
    [TestCase("Bearer abc", false, null)]
    [TestCase("Jfb-PreviewX abc", false, null)]
    [TestCase("Jfb-Preview abc", true, "abc")]
    [TestCase("jfb-preview abc", true, "abc")]
    [TestCase("Jfb-Preview", true, null)]
    [TestCase("Jfb-Preview ", true, null)]
    public void TheHeader_CarriesTheSchemeInAnyLetterCase_AndTheTicketAfterOneSpace(string? header, bool carries, string? ticket)
    {
        var request = new DefaultHttpContext().Request;
        if (header is not null)
        {
            request.Headers.Authorization = header;
        }

        Assert.That((PreviewTickets.Carries(request, out var sent), sent), Is.EqualTo((carries, ticket)));
    }

    [Test]
    public void TwoHeaders_CarryATicketThatDoesNotHold()
    {
        var request = new DefaultHttpContext().Request;
        request.Headers.Authorization = new Microsoft.Extensions.Primitives.StringValues(["Jfb-Preview abc", "Jfb-Preview def"]);

        Assert.That((PreviewTickets.Carries(request, out var sent), sent), Is.EqualTo((true, (string?)null)));
    }
}
