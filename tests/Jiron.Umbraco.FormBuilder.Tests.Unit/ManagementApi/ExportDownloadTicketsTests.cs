using System.Buffers.Text;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Jiron.Umbraco.FormBuilder.Web.ManagementApi.Entries;
using Jiron.Umbraco.FormBuilder.Web.PublicApi;
using Microsoft.AspNetCore.DataProtection;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.ManagementApi;

/// <summary>
/// The tickets that download a background export's file (ADR-0210): each names its user, form and export for one minute
/// on the site's clock, and reads as nothing once expired, changed, cut, of another layout, protected for another purpose
/// or by another site, or longer than any is. Neither a ticket nor the answer that holds one prints its value.
/// </summary>
public sealed class ExportDownloadTicketsTests
{
    private static readonly Guid User = Guid.Parse("a0000000-0000-4000-8000-000000000210");
    private static readonly Guid Form = Guid.Parse("f0000000-0000-4000-8000-000000000210");
    private static readonly Guid Export = Guid.Parse("e0000000-0000-4000-8000-000000000210");

    private EphemeralDataProtectionProvider _protection = null!;
    private TestTimeProvider _clock = null!;
    private ExportDownloadTickets _tickets = null!;

    [SetUp]
    public void CreateTheTickets()
    {
        _protection = new EphemeralDataProtectionProvider();
        _clock = new TestTimeProvider();
        _tickets = new ExportDownloadTickets(_protection, _clock);
    }

    [Test]
    public void ATicket_NamesItsUserFormAndExport_ForOneMinute()
    {
        var issued = _tickets.Issue(User, Form, Export);

        var read = _tickets.Read(issued.Value);
        _clock.Advance(ExportDownloadTickets.Lifetime - TimeSpan.FromTicks(1));
        var last = _tickets.Read(issued.Value);
        _clock.Advance(TimeSpan.FromTicks(1));

        Assert.Multiple(() =>
        {
            Assert.That((read?.UserKey, read?.FormKey, read?.ExportKey, read?.ExpiresUtc), Is.EqualTo(((Guid?)User, (Guid?)Form, (Guid?)Export, (DateTimeOffset?)issued.ExpiresUtc)));
            Assert.That(issued.ExpiresUtc, Is.EqualTo(_clock.GetUtcNow()), "a minute after it was issued");
            Assert.That(ExportDownloadTickets.Lifetime, Is.EqualTo(TimeSpan.FromMinutes(1)));
            Assert.That(last, Is.Not.Null, "until the last tick");
            Assert.That(_tickets.Read(issued.Value), Is.Null, "expired");
            Assert.That(issued.Value, Does.Match("^[A-Za-z0-9_-]+$"), "a value an address carries as it is");
            Assert.That(issued.Value, Has.Length.LessThanOrEqualTo(ExportDownloadTickets.MaxLength));
            Assert.That(issued.Value, Does.Not.Contain(Export.ToString("N")));
            Assert.That(issued.ToString(), Is.EqualTo(nameof(ExportDownloadTicket)), "a log never prints the ticket");
            Assert.That(new EntryExportTicketResponseModel { Url = "/file?ticket=" + issued.Value, ExpiresUtc = issued.ExpiresUtc }.ToString(), Does.Not.Contain(issued.Value), "nor the answer that holds it");
        });
    }

    [Test]
    public void ATicket_ReadsAsNothing_ChangedCutOrTooLong_OfAnotherLayout_ProtectedForAnotherPurpose_OrByAnotherSite()
    {
        var value = _tickets.Issue(User, Form, Export).Value;
        var bytes = Base64Url.DecodeFromChars(value);
        bytes[^1] ^= 1;
        var preview = new PreviewTickets(_protection, _clock).Issue(Form, 7).Value;
        var otherSite = new ExportDownloadTickets(new EphemeralDataProtectionProvider(), _clock).Issue(User, Form, Export).Value;
        var protector = _protection.CreateProtector(ExportDownloadTickets.Purpose);

        // A ticket's payload changed and protected again as the site would.
        string Changed(Func<byte[], byte[]> change)
        {
            var payload = protector.Unprotect(Base64Url.DecodeFromChars(_tickets.Issue(User, Form, Export).Value));
            return Base64Url.EncodeToString(protector.Protect(change(payload)));
        }

        Assert.Multiple(() =>
        {
            Assert.That(_tickets.Read(Changed(payload => payload))?.ExportKey, Is.EqualTo(Export), "protected again unchanged, it holds");
            Assert.That(_tickets.Read(Base64Url.EncodeToString(bytes)), Is.Null, "changed");
            Assert.That(_tickets.Read(value[..^4]), Is.Null, "cut");
            Assert.That(_tickets.Read(value + new string('A', ExportDownloadTickets.MaxLength)), Is.Null, "longer than any");
            Assert.That(_tickets.Read(Changed(payload => { payload[0] = 2; return payload; })), Is.Null, "another layout's version");
            Assert.That(_tickets.Read(Changed(payload => payload[..^1])), Is.Null, "a payload of another length");
            Assert.That(_tickets.Read(preview), Is.Null, "another purpose's ticket");
            Assert.That(_tickets.Read(otherSite), Is.Null, "another site's key ring");
            Assert.That(_tickets.Read("not a ticket!"), Is.Null);
            Assert.That(_tickets.Read(string.Empty), Is.Null);
            Assert.That(_tickets.Read(null), Is.Null);
        });
    }
}
