using Jiron.Umbraco.FormBuilder.Core.Files;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Files;

/// <summary>A response under a ZIP writer (ROADMAP 7.3c, ADR-0145): what it writes synchronously waits for the next asynchronous write, in its place.</summary>
public sealed class HeldWritesStreamTests
{
#pragma warning disable CA1849 // What is under test is a synchronous write.
    [Test]
    public async Task WhatIsWrittenSynchronously_WaitsForTheNextWrite_InItsPlace()
    {
        using var response = new MemoryStream();
        await using var held = new HeldWritesStream(response);

        held.Write([1, 2]);
        held.Write([9, 3, 9], 1, 1);
        var whileHeld = response.Length;
        await held.WriteAsync(new byte[] { 4 });
        held.WriteByte(5);
        await held.WriteAsync(new byte[] { 9, 6, 9 }.AsMemory(1, 1));
        held.Write([7]);
        await held.FlushAsync();
        await held.FlushAsync();

        Assert.Multiple(() =>
        {
            Assert.That(whileHeld, Is.Zero);
            Assert.That(response.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3, 4, 5, 6, 7 }), "each byte once, in its place");
        });
    }

    [Test]
    public async Task ItNeverWritesToItsResponse_Synchronously()
    {
        await using var held = new HeldWritesStream(new AsyncOnly());

        held.Write([1]);
        held.Flush();
        await held.WriteAsync(new byte[] { 2 });
        await held.FlushAsync();

        Assert.Pass("nothing threw");
    }

#pragma warning restore CA1849

    [Test]
    public async Task ItOnlyGoesForward()
    {
        await using var held = new HeldWritesStream(new MemoryStream());

        Assert.Multiple(() =>
        {
            Assert.That((held.CanRead, held.CanSeek, held.CanWrite), Is.EqualTo((false, false, true)));
            Assert.That(() => held.Length, Throws.InstanceOf<NotSupportedException>());
            Assert.That(() => held.Position, Throws.InstanceOf<NotSupportedException>());
            Assert.That(() => held.Position = 1, Throws.InstanceOf<NotSupportedException>());
            Assert.That(() => held.Read(new byte[1], 0, 1), Throws.InstanceOf<NotSupportedException>());
            Assert.That(() => held.Seek(0, SeekOrigin.Begin), Throws.InstanceOf<NotSupportedException>());
            Assert.That(() => held.SetLength(0), Throws.InstanceOf<NotSupportedException>());
        });
    }

    // As a request's body is: a synchronous write throws.
    private sealed class AsyncOnly : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count) => throw new InvalidOperationException("Synchronous operations are disallowed.");

        public override void Write(ReadOnlySpan<byte> buffer) => throw new InvalidOperationException("Synchronous operations are disallowed.");

        public override void Flush() => throw new InvalidOperationException("Synchronous operations are disallowed.");

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
