using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Jiron.Umbraco.FormBuilder.Core.Signatures;

/// <summary>
/// PNG files as signatures need them (ADR-0071, ADR-0103): a chunk's CRC-32, an image in true colour with alpha written
/// with one text chunk, and that chunk read back.
/// </summary>
internal static class Png
{
    /// <summary>The eight bytes every PNG starts with.</summary>
    public static ReadOnlySpan<byte> Signature => [137, 80, 78, 71, 13, 10, 26, 10];

    /// <summary>
    /// Writes an image of 8-bit red, green, blue and alpha: the header, one international text chunk (<c>iTXt</c>,
    /// uncompressed, without a language tag), the rows unfiltered in one zlib stream, and the end.
    /// </summary>
    /// <param name="width">The width in pixels.</param>
    /// <param name="height">The height in pixels.</param>
    /// <param name="rgba">The pixels, row by row, four bytes each.</param>
    /// <param name="keyword">The text chunk's keyword: 1 to 79 printable ASCII characters.</param>
    /// <param name="text">The text, written as UTF-8.</param>
    public static byte[] Write(int width, int height, ReadOnlySpan<byte> rgba, string keyword, string text)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfNotEqual(rgba.Length, width * height * 4);
        ArgumentNullException.ThrowIfNull(keyword);
        ArgumentNullException.ThrowIfNull(text);
        if (keyword.Length is < 1 or > 79 || keyword.Any(character => character is < ' ' or > '~'))
        {
            throw new ArgumentException("A keyword is 1 to 79 printable ASCII characters.", nameof(keyword));
        }

        using var output = new MemoryStream();
        output.Write(Signature);
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = 6;
        Chunk(output, "IHDR"u8, header);
        Chunk(output, "iTXt"u8, [.. Encoding.ASCII.GetBytes(keyword), 0, 0, 0, 0, 0, .. Encoding.UTF8.GetBytes(text)]);
        Chunk(output, "IDAT"u8, Compressed(width, height, rgba));
        Chunk(output, "IEND"u8, []);
        return output.ToArray();
    }

    /// <summary>
    /// The text of the first uncompressed <c>iTXt</c> chunk with the keyword; null when the bytes are no PNG, a chunk is
    /// cut short or its CRC is wrong before one is found, or there is none.
    /// </summary>
    /// <param name="png">The file's bytes.</param>
    /// <param name="keyword">The keyword.</param>
    public static string? Text(ReadOnlySpan<byte> png, string keyword)
    {
        ArgumentNullException.ThrowIfNull(keyword);
        if (!png.StartsWith(Signature))
        {
            return null;
        }

        var at = Signature.Length;
        while (png.Length - at >= 12)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(png[at..]);
            if (length > png.Length - at - 12)
            {
                return null;
            }

            var typed = png.Slice(at + 4, 4 + (int)length);
            if (Crc32(typed) != BinaryPrimitives.ReadUInt32BigEndian(png[(at + 8 + (int)length)..]))
            {
                return null;
            }

            if (typed.StartsWith("IEND"u8))
            {
                return null;
            }

            if (typed.StartsWith("iTXt"u8) && TextOf(typed[4..], keyword) is { } text)
            {
                return text;
            }

            at += 12 + (int)length;
        }

        return null;
    }

    /// <summary>
    /// The CRC-32 a PNG chunk carries over its type and data: reflected, with the polynomial <c>0xEDB88320</c>, starting
    /// from all ones and inverted at the end.
    /// </summary>
    /// <param name="bytes">The chunk's type and data.</param>
    public static uint Crc32(ReadOnlySpan<byte> bytes)
    {
        var crc = uint.MaxValue;
        foreach (var value in bytes)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }

    // A chunk: its data's length, its type, its data and the CRC of its type and data.
    private static void Chunk(MemoryStream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        var word = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(word, data.Length);
        output.Write(word);
        var typed = new byte[4 + data.Length];
        type.CopyTo(typed);
        data.CopyTo(typed.AsSpan(4));
        output.Write(typed);
        BinaryPrimitives.WriteUInt32BigEndian(word, Crc32(typed));
        output.Write(word);
    }

    // The rows, each after its filter byte (none), as one zlib stream.
    private static byte[] Compressed(int width, int height, ReadOnlySpan<byte> rgba)
    {
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            var row = width * 4;
            for (var y = 0; y < height; y++)
            {
                zlib.WriteByte(0);
                zlib.Write(rgba.Slice(y * row, row));
            }
        }

        return compressed.ToArray();
    }

    // An iTXt chunk's text when its keyword is the one asked for and it is not compressed: the keyword, a zero, the
    // compression flag and method, the language tag and the translated keyword each ended by a zero, then UTF-8.
    private static string? TextOf(ReadOnlySpan<byte> data, string keyword)
    {
        var end = data.IndexOf((byte)0);
        if (end < 0 || !data[..end].SequenceEqual(Encoding.ASCII.GetBytes(keyword)) || data.Length < end + 3 || data[end + 1] != 0)
        {
            return null;
        }

        var rest = data[(end + 3)..];
        var language = rest.IndexOf((byte)0);
        if (language < 0)
        {
            return null;
        }

        rest = rest[(language + 1)..];
        var translated = rest.IndexOf((byte)0);
        return translated < 0 ? null : Encoding.UTF8.GetString(rest[(translated + 1)..]);
    }
}
