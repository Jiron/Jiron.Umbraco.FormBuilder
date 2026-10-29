using System.Buffers.Binary;
using Jiron.Umbraco.FormBuilder.Core.Signatures;

namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;

/// <summary>
/// The header of a PNG image (ADR-0071), which a signature's upload must start with: the eight signature bytes and the
/// <c>IHDR</c> chunk, which every PNG has first.
/// </summary>
internal static class PngHeader
{
    /// <summary>The bytes the header takes: the signature, and the chunk's length, type, 13 bytes of data and CRC.</summary>
    public const int Length = 33;

    /// <summary>
    /// The image's width and height in pixels when the bytes start with a PNG's signature and a well-formed <c>IHDR</c>
    /// chunk: the length 13, a width and a height from 1 to 2,147,483,647, a bit depth its colour type allows,
    /// compression and filter method 0, interlace 0 or 1, and the chunk's CRC; otherwise null.
    /// </summary>
    /// <param name="head">A file's first bytes.</param>
    public static (int Width, int Height)? Read(ReadOnlySpan<byte> head)
    {
        if (head.Length < Length || !head.StartsWith(Png.Signature) || BinaryPrimitives.ReadUInt32BigEndian(head[8..]) != 13 || !head[12..16].SequenceEqual("IHDR"u8))
        {
            return null;
        }

        var width = BinaryPrimitives.ReadUInt32BigEndian(head[16..]);
        var height = BinaryPrimitives.ReadUInt32BigEndian(head[20..]);
        var takes = width is >= 1 and <= int.MaxValue
            && height is >= 1 and <= int.MaxValue
            && Allows(colourType: head[25], bitDepth: head[24])
            && head[26] == 0
            && head[27] == 0
            && head[28] <= 1
            && Png.Crc32(head[12..29]) == BinaryPrimitives.ReadUInt32BigEndian(head[29..]);
        return takes ? ((int)width, (int)height) : null;
    }

    // The bit depths each colour type allows: grey, true colour, indexed, grey with alpha and true colour with alpha.
    private static bool Allows(byte colourType, byte bitDepth) => colourType switch
    {
        0 => bitDepth is 1 or 2 or 4 or 8 or 16,
        2 or 4 or 6 => bitDepth is 8 or 16,
        3 => bitDepth is 1 or 2 or 4 or 8,
        _ => false,
    };
}
