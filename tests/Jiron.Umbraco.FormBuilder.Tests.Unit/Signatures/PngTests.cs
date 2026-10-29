using System.Buffers.Binary;
using System.Text;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Core.Signatures;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Signatures;

/// <summary>
/// The PNG files signatures need (ADR-0071, ADR-0103): a chunk's CRC-32, an image written in true colour with alpha
/// with one text chunk, the text read back only from a well-formed uncompressed chunk with its keyword, and the pen's
/// canvas that draws the paths.
/// </summary>
public sealed class PngTests
{
    private const string Keyword = "jfb:test";

    [Test]
    public void TheCrc_IsTheOnePngChunksCarry()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Png.Crc32("IEND"u8), Is.EqualTo(0xAE426082u), "the CRC every PNG's end carries");
            Assert.That(Png.Crc32([]), Is.Zero);
        });
    }

    [Test]
    public void AnImage_IsWrittenWithItsPixelsAndItsText_AndReadBack()
    {
        byte[] pixels = [1, 2, 3, 0, 4, 5, 6, 128, 7, 8, 9, 255, 10, 11, 12, 1, 13, 14, 15, 2, 16, 17, 18, 254];
        var text = "Zo" + (char)0xEB + " " + (char)0x65E5 + (char)0x672C;

        var png = Png.Write(3, 2, pixels, Keyword, text);

        Assert.Multiple(() =>
        {
            Assert.That(PngHeader.Read(png), Is.EqualTo((3, 2)), "a header the signature's check takes");
            Assert.That(PngPixels.Decode(png), Is.EqualTo((3, 2, pixels)));
            Assert.That(Png.Text(png, Keyword), Is.EqualTo(text), "the text exactly, as UTF-8");
            Assert.That(Png.Text(png, "jfb:other"), Is.Null, "another keyword's text is none");
            Assert.That(Encoding.ASCII.GetString(png, 37, 4), Is.EqualTo("iTXt"), "the text right after the header");
        });
    }

    [Test]
    public void TheText_IsReadOnlyFromAWellFormedUncompressedChunkWithItsKeyword()
    {
        var whole = Chunks(Text(Keyword, "Ada"));
        var cut = whole[..(8 + 25 + 10)];
        var badCrc = (byte[])whole.Clone();
        badCrc[^13] ^= 1;
        var badHeader = (byte[])whole.Clone();
        badHeader[20] ^= 1;
        // The text chunk says it runs one byte past the file's end.
        var longer = (byte[])whole.Clone();
        BinaryPrimitives.WriteInt32BigEndian(longer.AsSpan(33), whole.Length - 33 - 12 + 1);

        Assert.Multiple(() =>
        {
            Assert.That(Png.Text(whole, Keyword), Is.EqualTo("Ada"));
            Assert.That(Png.Text(Chunks(Text("jfb:other", "Bob"), Text(Keyword, "Ada")), Keyword), Is.EqualTo("Ada"), "the chunk with its keyword, after another");
            Assert.That(Png.Text("GIF89a"u8.ToArray(), Keyword), Is.Null, "no PNG");
            Assert.That(Png.Text(cut, Keyword), Is.Null, "a chunk cut short");
            Assert.That(Png.Text(longer, Keyword), Is.Null, "a chunk longer than the file");
            Assert.That(Png.Text(badCrc, Keyword), Is.Null, "a text whose CRC is wrong");
            Assert.That(Png.Text(badHeader, Keyword), Is.Null, "a header whose CRC is wrong, before the text");
            Assert.That(Png.Text(Chunks(), Keyword), Is.Null, "no text before the end");
            Assert.That(Png.Text([.. Chunks(), .. Chunk("iTXt", Text(Keyword, "late"))], Keyword), Is.Null, "nothing after the end");
            Assert.That(Png.Text(Chunks([.. Encoding.ASCII.GetBytes(Keyword), 0, 1, 0, 0, 0, .. "Ada"u8]), Keyword), Is.Null, "a compressed text");
            Assert.That(Png.Text(Chunks([.. Encoding.ASCII.GetBytes(Keyword)]), Keyword), Is.Null, "a keyword without its end");
            Assert.That(Png.Text(Chunks([.. Encoding.ASCII.GetBytes(Keyword), 0, 0]), Keyword), Is.Null, "without its compression method");
            Assert.That(Png.Text(Chunks([.. Encoding.ASCII.GetBytes(Keyword), 0, 0, 0, .. "en"u8]), Keyword), Is.Null, "a language tag without its end");
            Assert.That(Png.Text(Chunks([.. Encoding.ASCII.GetBytes(Keyword), 0, 0, 0, .. "en"u8, 0, .. "Name"u8]), Keyword), Is.Null, "a translated keyword without its end");
            Assert.That(Png.Text(Chunks([.. Encoding.ASCII.GetBytes(Keyword), 0, 0, 0, .. "en"u8, 0, .. "Name"u8, 0, .. "Ada"u8]), Keyword), Is.EqualTo("Ada"), "a language tag and a translated keyword skipped");
        });
    }

    [Test]
    public void WritingAnImpossibleImage_Throws()
    {
        Assert.Multiple(() =>
        {
            Assert.That(() => Png.Write(0, 1, [], Keyword, "a"), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => Png.Write(1, 0, [], Keyword, "a"), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => Png.Write(1, 1, [0, 0, 0], Keyword, "a"), Throws.InstanceOf<ArgumentOutOfRangeException>(), "pixels of another size");
            Assert.That(() => Png.Write(1, 1, [0, 0, 0, 0], string.Empty, "a"), Throws.ArgumentException, "no keyword");
            Assert.That(() => Png.Write(1, 1, [0, 0, 0, 0], new string('k', 80), "a"), Throws.ArgumentException, "a keyword too long");
            Assert.That(() => Png.Write(1, 1, [0, 0, 0, 0], "a" + (char)9 + "b", "a"), Throws.ArgumentException, "a keyword with a tab");
            Assert.That(() => Png.Write(1, 1, [0, 0, 0, 0], "a" + (char)0xE9, "a"), Throws.ArgumentException, "a keyword past ASCII");
            Assert.That(() => Png.Write(1, 1, [0, 0, 0, 0], null!, "a"), Throws.ArgumentNullException);
            Assert.That(() => Png.Write(1, 1, [0, 0, 0, 0], Keyword, null!), Throws.ArgumentNullException);
            Assert.That(Png.Write(1, 1, [0, 0, 0, 0], new string('k', 79), "a"), Is.Not.Empty, "the longest keyword");
        });
    }

    [Test]
    public void ThePen_DrawsLinesCurvesAndClosedShapes_SmoothAtTheEdges_AndRefusesUnknownCommands()
    {
        // Half a pixel along, so the line runs through the pixels' middles: a pixel two away lies on the pen's edge.
        var canvas = new PenCanvas(40, 20);
        canvas.Draw("M 2 10 L 38 10", (x, y) => (x + 0.5, y + 0.5), 4);
        var line = canvas.Rgba(1, 2, 3);
        var curve = new PenCanvas(40, 20);
        curve.Draw("M 2 2 Q 20 30 38 2 M 2 18 C 10 0 30 0 38 18 Z", (x, y) => (x, y), 2);
        var twice = new PenCanvas(40, 20);
        twice.Draw("M 2 10 L 38 10 M 2 10 L 38 10", (x, y) => (x + 0.5, y + 0.5), 4);

        // A quadratic through (10, 2.5) a quarter of the way along: cut into a few pieces, it would pass three away.
        var round = new PenCanvas(40, 12);
        round.Draw("M 0 10 Q 20 -10 40 10", (x, y) => (x, y), 2);

        Assert.Multiple(() =>
        {
            Assert.That(Pixel(line, 40, 20, 10), Is.EqualTo(new byte[] { 1, 2, 3, 255 }), "inside the pen: fully inked, in the ink's colour");
            Assert.That(Pixel(line, 40, 20, 12)[3], Is.EqualTo(128), "the pen's edge half covered");
            Assert.That(Pixel(line, 40, 20, 14)[3], Is.Zero, "past the pen: no ink");
            Assert.That(Pixel(line, 40, 0, 10)[3], Is.EqualTo(128), "the round end reaches as far as the pen's half width");
            Assert.That(curve.Rgba(0, 0, 0).Where((_, at) => at % 4 == 3).Count(alpha => alpha > 0), Is.GreaterThan(60), "the curves drawn");
            Assert.That(Pixel(twice.Rgba(1, 2, 3), 40, 20, 12)[3], Is.EqualTo(128), "a stroke drawn over another is no darker");
            Assert.That(Pixel(round.Rgba(0, 0, 0), 40, 9, 2)[3], Is.GreaterThan(200), "a curve cut fine enough to stay on it");
            Assert.That(new PenCanvas(3, 3).Rgba(9, 9, 9).Where((_, at) => at % 4 == 3), Is.All.Zero, "nothing drawn, nothing inked");
            Assert.That(() => new PenCanvas(4, 4).Draw("M 0 0 X 1 1", (x, y) => (x, y), 1), Throws.InstanceOf<FormatException>());
            Assert.That(() => new PenCanvas(4, 4).Draw(null!, (x, y) => (x, y), 1), Throws.ArgumentNullException);
            Assert.That(() => new PenCanvas(4, 4).Draw("M 0 0", null!, 1), Throws.ArgumentNullException);
        });
    }

    // A pixel's four bytes.
    private static byte[] Pixel(byte[] rgba, int width, int x, int y) => rgba[(((y * width) + x) * 4)..((((y * width) + x) * 4) + 4)];

    // An iTXt chunk's data without a language tag or translated keyword.
    private static byte[] Text(string keyword, string text) => [.. Encoding.ASCII.GetBytes(keyword), 0, 0, 0, 0, 0, .. Encoding.UTF8.GetBytes(text)];

    // A PNG of one pixel's header, the given iTXt chunks' data and the end, without pixels.
    private static byte[] Chunks(params byte[][] texts)
    {
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, 1);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), 1);
        header[8] = 8;
        header[9] = 6;
        return [.. Png.Signature, .. Chunk("IHDR", header), .. texts.SelectMany(text => Chunk("iTXt", text)), .. Chunk("IEND", [])];
    }

    private static byte[] Chunk(string type, byte[] data)
    {
        byte[] typed = [.. Encoding.ASCII.GetBytes(type), .. data];
        var bytes = new byte[12 + data.Length];
        BinaryPrimitives.WriteInt32BigEndian(bytes, data.Length);
        typed.CopyTo(bytes, 4);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8 + data.Length), Png.Crc32(typed));
        return bytes;
    }
}
