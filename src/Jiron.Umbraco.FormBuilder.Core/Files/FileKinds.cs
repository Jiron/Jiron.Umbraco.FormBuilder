using System.Buffers.Binary;
using System.Collections.Frozen;

namespace Jiron.Umbraco.FormBuilder.Core.Files;

/// <summary>
/// The kinds of file an upload's first bytes show (ADR-0006, ADR-0087), whatever its name says: each extension of the
/// table takes only bytes of its kind, an extension outside it takes any bytes but an executable's, and an executable
/// is refused whatever its name. 6.4's runtime holds files to the same table before it sends them.
/// </summary>
internal static class FileKinds
{
    /// <summary>The content type of a file whose extension is outside the table.</summary>
    public const string UnknownContentType = "application/octet-stream";

    private static ReadOnlySpan<byte> Png => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static ReadOnlySpan<byte> CompoundFile => [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
    private static ReadOnlySpan<byte> SevenZip => [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C];
    private static ReadOnlySpan<byte> Rar => [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07];

    // What a rich text document opens with: a brace, a backslash and rtf.
    private static ReadOnlySpan<byte> Rtf => [0x7B, 0x5C, 0x72, 0x74, 0x66];

    // The brands of an ISO media file (its ftyp box) that hold an AVIF image, and those that hold any HEIF image.
    private static readonly FrozenSet<string> AvifBrands = new[] { "avif", "avis" }.ToFrozenSet(StringComparer.Ordinal);
    private static readonly FrozenSet<string> HeifBrands = new[] { "heic", "heix", "hevc", "hevx", "heim", "heis", "hevm", "hevs", "mif1", "msf1" }.ToFrozenSet(StringComparer.Ordinal);

    // Each extension of the table, in lowercase without the point, with its content type and the bytes of its kind.
    private static readonly FrozenDictionary<string, (string ContentType, HeadMatch Matches)> Table = new Dictionary<string, (string, HeadMatch)>
    {
        ["jpg"] = ("image/jpeg", IsJpeg),
        ["jpeg"] = ("image/jpeg", IsJpeg),
        ["png"] = ("image/png", head => head.StartsWith(Png)),
        ["gif"] = ("image/gif", head => head.StartsWith("GIF87a"u8) || head.StartsWith("GIF89a"u8)),
        ["webp"] = ("image/webp", head => head.StartsWith("RIFF"u8) && head.Length >= 12 && head[8..12].SequenceEqual("WEBP"u8)),
        ["avif"] = ("image/avif", head => HasBrand(head, AvifBrands)),
        ["heic"] = ("image/heic", head => HasBrand(head, HeifBrands)),
        ["heif"] = ("image/heif", head => HasBrand(head, HeifBrands)),
        ["pdf"] = ("application/pdf", head => head.StartsWith("%PDF-"u8)),
        ["doc"] = ("application/msword", head => head.StartsWith(CompoundFile)),
        ["xls"] = ("application/vnd.ms-excel", head => head.StartsWith(CompoundFile)),
        ["ppt"] = ("application/vnd.ms-powerpoint", head => head.StartsWith(CompoundFile)),
        ["docx"] = ("application/vnd.openxmlformats-officedocument.wordprocessingml.document", IsZipEntry),
        ["xlsx"] = ("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", IsZipEntry),
        ["pptx"] = ("application/vnd.openxmlformats-officedocument.presentationml.presentation", IsZipEntry),
        ["odt"] = ("application/vnd.oasis.opendocument.text", IsZipEntry),
        ["ods"] = ("application/vnd.oasis.opendocument.spreadsheet", IsZipEntry),
        ["odp"] = ("application/vnd.oasis.opendocument.presentation", IsZipEntry),
        ["rtf"] = ("application/rtf", head => head.StartsWith(Rtf)),
        ["txt"] = ("text/plain", IsText),
        ["csv"] = ("text/csv", IsText),
        ["zip"] = ("application/zip", IsZip),
        ["7z"] = ("application/x-7z-compressed", head => head.StartsWith(SevenZip)),
        ["rar"] = ("application/vnd.rar", head => head.StartsWith(Rar)),
        ["gz"] = ("application/gzip", head => head.StartsWith((ReadOnlySpan<byte>)[0x1F, 0x8B])),
        ["tar"] = ("application/x-tar", head => head.Length >= 262 && head[257..262].SequenceEqual("ustar"u8)),
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private delegate bool HeadMatch(ReadOnlySpan<byte> head);

    /// <summary>Every extension of the table, in lowercase without the point.</summary>
    public static IEnumerable<string> Extensions => Table.Keys;

    /// <summary>
    /// Whether the bytes are a program or a script: a Windows executable (<c>MZ</c>), an ELF or Mach-O binary, a
    /// universal binary, or a script that names its interpreter (<c>#!</c>).
    /// </summary>
    /// <param name="head">The file's first bytes.</param>
    public static bool IsExecutable(ReadOnlySpan<byte> head) =>
        head.StartsWith("MZ"u8)
        || head.StartsWith((ReadOnlySpan<byte>)[0x7F, 0x45, 0x4C, 0x46])
        || head.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xED, 0xFA, 0xCE]) || head.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xED, 0xFA, 0xCF])
        || head.StartsWith((ReadOnlySpan<byte>)[0xCE, 0xFA, 0xED, 0xFE]) || head.StartsWith((ReadOnlySpan<byte>)[0xCF, 0xFA, 0xED, 0xFE])
        || head.StartsWith((ReadOnlySpan<byte>)[0xCA, 0xFE, 0xBA, 0xBE])
        || head.StartsWith("#!"u8);

    /// <summary>
    /// Whether the bytes are the kind the extension names; true for an extension outside the table, whose bytes no kind
    /// holds to (the check of executables comes first).
    /// </summary>
    /// <param name="extension">The extension in lowercase without the point, empty for a name without one.</param>
    /// <param name="head">The file's first bytes: at least the first 512, or all of a shorter file.</param>
    public static bool Matches(string extension, ReadOnlySpan<byte> head)
    {
        ArgumentNullException.ThrowIfNull(extension);
        return !Table.TryGetValue(extension, out var kind) || kind.Matches(head);
    }

    /// <summary>The content type of a file by its extension: the table's, otherwise <see cref="UnknownContentType"/>.</summary>
    /// <param name="extension">The extension in lowercase without the point.</param>
    public static string ContentType(string extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        return Table.TryGetValue(extension, out var kind) ? kind.ContentType : UnknownContentType;
    }

    private static bool IsJpeg(ReadOnlySpan<byte> head) => head.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]);

    // A ZIP archive whose first entry follows: what office documents are.
    private static bool IsZipEntry(ReadOnlySpan<byte> head) => head.StartsWith((ReadOnlySpan<byte>)[0x50, 0x4B, 0x03, 0x04]);

    // A ZIP archive: one with an entry, an empty one, or the first part of one spread over several files.
    private static bool IsZip(ReadOnlySpan<byte> head) => IsZipEntry(head) || head.StartsWith((ReadOnlySpan<byte>)[0x50, 0x4B, 0x05, 0x06]) || head.StartsWith((ReadOnlySpan<byte>)[0x50, 0x4B, 0x07, 0x08]);

    // Text: no NUL byte, unless the bytes start with a UTF-16 byte-order mark; an empty file is text too.
    private static bool IsText(ReadOnlySpan<byte> head) =>
        head.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]) || head.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]) || !head.Contains((byte)0);

    // An ISO media file whose ftyp box names one of the brands, as its major brand or among its compatible ones.
    private static bool HasBrand(ReadOnlySpan<byte> head, FrozenSet<string> brands)
    {
        if (head.Length < 16 || !head[4..8].SequenceEqual("ftyp"u8))
        {
            return false;
        }

        var size = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(head), (uint)head.Length);
        for (var at = 8; at + 4 <= size; at += at == 8 ? 8 : 4)
        {
            if (brands.Contains(System.Text.Encoding.ASCII.GetString(head[at..(at + 4)])))
            {
                return true;
            }
        }

        return false;
    }
}
