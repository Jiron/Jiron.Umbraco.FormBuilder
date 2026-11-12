using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Microsoft.Extensions.Options;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Rendering;

/// <summary>
/// What posts without JavaScript hold in this server's temporary files while they are read (16.1's security review): each
/// client its share, all of them what may wait, each passed by one read at most, and every byte given back with its request.
/// </summary>
[TestFixture]
public sealed class FormPostBuffersTests
{
    [Test]
    public void AClient_ReadsOnWhileBelowItsShare_ThenIsRefused_AndAnotherClientIsNot()
    {
        var buffers = Buffers(perClient: 1000, total: 0);
        using var first = buffers.Start("address:192.0.2.1");
        using var other = buffers.Start("address:192.0.2.2");

        Assert.Multiple(() =>
        {
            Assert.That(first.TryCount(600), Is.True);
            Assert.That(first.TryCount(600), Is.True, "below the share when it read, so it passes it by one read");
            Assert.That(first.TryCount(1), Is.False, "at or past its share");
            Assert.That(other.TryCount(900), Is.True, "another client's share is its own");
            Assert.That(buffers.Held, Is.EqualTo(2100));
        });
    }

    [Test]
    public void EveryClientTogether_IsHeldToWhatMayWait()
    {
        var buffers = Buffers(perClient: 0, total: 1000);
        using var first = buffers.Start("address:192.0.2.1");
        using var second = buffers.Start("address:192.0.2.2");

        Assert.Multiple(() =>
        {
            Assert.That(first.TryCount(1200), Is.True);
            Assert.That(second.TryCount(1), Is.False, "the server holds what may wait");
        });
    }

    [Test]
    public void ARequestThatEnds_GivesBackWhatItHeld_AndAClientHoldingNothingIsForgotten()
    {
        var buffers = Buffers(perClient: 1000, total: 0);
        var first = buffers.Start("address:192.0.2.1");
        _ = first.TryCount(1500);
        first.Dispose();
        first.Dispose();
        using var again = buffers.Start("address:192.0.2.1");

        Assert.Multiple(() =>
        {
            Assert.That(buffers.Held, Is.Zero, "given back once, however often it is disposed");
            Assert.That(again.TryCount(900), Is.True, "the client may hold its share again");
            Assert.That(() => first.TryCount(1), Throws.TypeOf<ObjectDisposedException>());
        });
    }

    [Test]
    public void LimitsOfNothing_HoldEverything()
    {
        var buffers = Buffers(perClient: 0, total: 0);
        using var holding = buffers.Start("key:crm");

        Assert.That(holding.TryCount(long.MaxValue / 2), Is.True);
    }

    [Test]
    public async Task ABody_EndsEarly_OnceItsHoldingIsRefused_AndSaysSo()
    {
        var buffers = Buffers(perClient: 10, total: 0);
        using var holding = buffers.Start("address:192.0.2.1");
        using var inner = new MemoryStream(new byte[64]);
        await using var body = new HeldReadStream(inner, holding);
        var chunk = new byte[8];

        var reads = new List<int>();
        int read;
        while ((read = await body.ReadAsync(chunk)) > 0)
        {
            reads.Add(read);
        }

        Assert.Multiple(() =>
        {
            Assert.That(reads, Is.EqualTo(new[] { 8, 8 }), "two reads while below 10 bytes, then the end");
            Assert.That(body.Refused, Is.True);
        });
    }

    private static FormPostBuffers Buffers(long perClient, long total)
    {
        var options = new FormBuilderOptions();
        options.Files.MaxWaitingBytesPerAddress = perClient;
        options.Files.MaxWaitingBytes = total;
        return new FormPostBuffers(Options.Create(options));
    }
}
