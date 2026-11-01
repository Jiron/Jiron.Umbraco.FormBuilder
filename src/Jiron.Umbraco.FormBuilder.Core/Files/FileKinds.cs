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

    /// <summary>How many of a file's last bytes can hold a ZIP archive's closing record: the record and its longest comment.</summary>
    public const int ZipTailLength = ZipClosingLength + ushort.MaxValue;

    private const int ZipClosingLength = 22;

    private static ReadOnlySpan<byte> Png => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static ReadOnlySpan<byte> CompoundFile => [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
    private static ReadOnlySpan<byte> SevenZip => [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C];
    private static ReadOnlySpan<byte> Rar => [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07];
    private static ReadOnlySpan<byte> ZipClosing => [0x50, 0x4B, 0x05, 0x06];

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

    // The extensions that name a page or a drawing that runs script, which a site can only take by leaving them unblocked.
    private static readonly FrozenSet<string> MarkupExtensions = new[] { "html", "htm", "xhtml", "shtml", "xml", "svg" }.ToFrozenSet(StringComparer.Ordinal);

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

    /// <summary>
    /// Whether the bytes hide a page behind another name (ADR-0143): a file whose extension does not name markup and that
    /// opens as a page, a drawing or a server's script, and a binary kind of the table with one inside its first bytes,
    /// where a browser's sniffing or a server's interpreter would find it. Text may mention markup after its start.
    /// </summary>
    /// <param name="extension">The extension in lowercase without the point, empty for a name without one.</param>
    /// <param name="head">The file's first bytes: at least the first 512, or all of a shorter file.</param>
    public static bool HidesMarkup(string extension, ReadOnlySpan<byte> head)
    {
        ArgumentNullException.ThrowIfNull(extension);
        if (MarkupExtensions.Contains(extension))
        {
            return false;
        }

        Span<byte> read = head.Length <= 1_024 ? stackalloc byte[head.Length] : new byte[head.Length];
        read = Readable(head, read);
        return OpensAsMarkup(read.TrimStart(" \t\r\n\f"u8))
            || (extension is not ("txt" or "csv") && Table.ContainsKey(extension) && HoldsMarkup(read));
    }

    /// <summary>
    /// Whether a file of this extension must not also be an archive (ADR-0143): every kind of the table but those that are
    /// one. Only for them does an upload keep its last bytes, for <see cref="EndsAsZip"/>.
    /// </summary>
    /// <param name="extension">The extension in lowercase without the point.</param>
    public static bool MustNotEndAsZip(string extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        return Table.TryGetValue(extension, out var kind) && kind.Matches != IsZipEntry && kind.Matches != IsZip;
    }

    /// <summary>
    /// Whether the bytes end as a ZIP archive does, whatever they start with (ADR-0143): its closing record, which readers
    /// look for from the end, with its comment reaching exactly to the last byte. An image or a PDF that does is both.
    /// </summary>
    /// <param name="tail">The file's last <see cref="ZipTailLength"/> bytes, or all of a shorter file.</param>
    public static bool EndsAsZip(ReadOnlySpan<byte> tail)
    {
        for (var at = tail.Length - ZipClosingLength; at >= 0; at--)
        {
            if (tail[at..].StartsWith(ZipClosing) && at + ZipClosingLength + BinaryPrimitives.ReadUInt16LittleEndian(tail[(at + 20)..]) == tail.Length)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The content type of a file by its extension: the table's, otherwise <see cref="UnknownContentType"/>.</summary>
    /// <param name="extension">The extension in lowercase without the point.</param>
    public static string ContentType(string extension)
    {
        ArgumentNullException.ThrowIfNull(extension);
        return Table.TryGetValue(extension, out var kind) ? kind.ContentType : UnknownContentType;
    }

    // How a page, a drawing or a server's script opens, in lowercase.
    private static bool OpensAsMarkup(ReadOnlySpan<byte> text) =>
        text.StartsWith("<!doctype html"u8) || text.StartsWith("<html"u8) || text.StartsWith("<head"u8) || text.StartsWith("<body"u8)
        || text.StartsWith("<script"u8) || text.StartsWith("<svg"u8) || text.StartsWith("<iframe"u8) || text.StartsWith("<object"u8)
        || text.StartsWith("<embed"u8) || text.StartsWith("<?xml"u8) || text.StartsWith("<?php"u8) || text.StartsWith("<%"u8);

    // What runs when a browser or a server takes a file for a page, wherever it stands, in lowercase: too long to appear by
    // chance in the bytes of an image.
    private static bool HoldsMarkup(ReadOnlySpan<byte> bytes) =>
        bytes.IndexOf("<!doctype html"u8) >= 0 || bytes.IndexOf("<html"u8) >= 0 || bytes.IndexOf("<script"u8) >= 0
        || bytes.IndexOf("<svg"u8) >= 0 || bytes.IndexOf("<iframe"u8) >= 0 || bytes.IndexOf("<?php"u8) >= 0;

    // The bytes as a browser may read them, in lowercase: without a byte-order mark, and without zero bytes, which UTF-16
    // holds with or without its mark and which some readers skip inside a tag.
    private static Span<byte> Readable(ReadOnlySpan<byte> head, Span<byte> read)
    {
        var from = head.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xFE]) || head.StartsWith((ReadOnlySpan<byte>)[0xFE, 0xFF]) ? head[2..]
            : head.StartsWith((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]) ? head[3..]
            : head;
        var length = 0;
        foreach (var unit in from)
        {
            if (unit != 0)
            {
                read[length++] = unit is >= (byte)'A' and <= (byte)'Z' ? (byte)(unit + 32) : unit;
            }
        }

        return read[..length];
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
