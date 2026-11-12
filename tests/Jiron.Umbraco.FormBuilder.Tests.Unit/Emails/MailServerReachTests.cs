using System.Net.Sockets;
using Jiron.Umbraco.FormBuilder.Core.Email;
using Jiron.Umbraco.FormBuilder.Testing.Time;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Emails;

/// <summary>
/// When this server finds no mail server, it waits before it tries again, longer each time up to five minutes, and the first
/// mail that leaves ends it (ROADMAP 16.4, ADR-0237).
/// </summary>
[TestFixture]
public sealed class MailServerReachTests
{
    [Test]
    public void EachRefusalInARow_PausesLonger_UpToFiveMinutes_AndAMailThatLeavesEndsIt()
    {
        var clock = new TestTimeProvider();
        var reach = new MailServerReach(clock);
        var pauses = new List<TimeSpan>();

        Assert.That((reach.Paused, reach.Unreached), Is.EqualTo((false, false)), "nothing is known yet");
        for (var refusal = 0; refusal < 7; refusal++)
        {
            var pause = reach.Refusal();
            pauses.Add(pause);
            Assert.That(reach.Paused, Is.True);
            clock.Advance(pause - TimeSpan.FromSeconds(1));
            Assert.That(reach.Paused, Is.True, "not before its time");
            clock.Advance(TimeSpan.FromSeconds(1));
            Assert.That(reach.Paused, Is.False);
        }

        Assert.That(
            pauses.Select(pause => pause.TotalSeconds),
            Is.EqualTo(new double[] { 30, 60, 120, 240, 300, 300, 300 }));
        Assert.That(reach.Unreached, Is.True, "it is still not known to be there");

        reach.Reached();

        Assert.Multiple(() =>
        {
            Assert.That((reach.Paused, reach.Unreached), Is.EqualTo((false, false)));
            Assert.That(reach.Refusal(), Is.EqualTo(TimeSpan.FromSeconds(30)), "a new outage starts short again");
        });
    }

    [Test]
    public void AttemptsThatWereUnderWayTogether_EndAsOneRefusal()
    {
        var clock = new TestTimeProvider();
        var reach = new MailServerReach(clock);

        var first = reach.Refusal();
        clock.Advance(TimeSpan.FromSeconds(10));
        var second = reach.Refusal();
        var third = reach.Refusal();
        clock.Advance(TimeSpan.FromSeconds(20));
        var next = reach.Refusal();

        Assert.Multiple(() =>
        {
            Assert.That(first, Is.EqualTo(TimeSpan.FromSeconds(30)));
            Assert.That((second, third), Is.EqualTo((TimeSpan.FromSeconds(20), TimeSpan.FromSeconds(20))), "what is left of the same pause");
            Assert.That(next, Is.EqualTo(TimeSpan.FromMinutes(1)), "the next refusal after it is the second in a row");
        });
    }

    [Test]
    public void ARefusal_IsASocketsError_HoweverDeepItLies()
    {
        var refused = new SocketException((int)SocketError.ConnectionRefused);

        Assert.Multiple(() =>
        {
            Assert.That(MailServerReach.Refused(refused), Is.True);
            Assert.That(MailServerReach.Refused(new SocketException((int)SocketError.HostNotFound)), Is.True, "no such host");
            Assert.That(MailServerReach.Refused(new IOException("Unable to connect", refused)), Is.True, "wrapped");
            Assert.That(MailServerReach.Refused(new AggregateException(new InvalidOperationException("other"), new IOException("x", refused))), Is.True, "among others");
            Assert.That(MailServerReach.Refused(new InvalidOperationException("No mailbox for anna@jfb.test")), Is.False, "the mail server answered and refused the mail");
            Assert.That(MailServerReach.Refused(new IOException("The stream ended")), Is.False);
            Assert.That(MailServerReach.Refused(new TimeoutException()), Is.False, "a cut-off may have sent the mail");
        });
    }
}
