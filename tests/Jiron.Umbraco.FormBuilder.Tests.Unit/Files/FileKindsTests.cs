using System.Buffers.Binary;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Core.Files;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Files;

/// <summary>
/// The kinds of file an upload's first bytes show (ADR-0087): each extension of the table takes only bytes of its kind,
/// one outside it takes any bytes, a program or a script is recognised whatever its name, and the table holds exactly
/// the extensions of the file field's type groups.
/// </summary>
public sealed class FileKindsTests
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, .. "JFIF"u8, 0x00];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, .. "IHDR"u8];
    private static readonly byte[] Gif87 = [.. "GIF87a"u8, 0x01, 0x00];
    private static readonly byte[] Gif89 = [.. "GIF89a"u8, 0x01, 0x00];
    private static readonly byte[] WebP = [.. "RIFF"u8, 0x24, 0x00, 0x00, 0x00, .. "WEBPVP8 "u8];
    private static readonly byte[] Wave = [.. "RIFF"u8, 0x24, 0x00, 0x00, 0x00, .. "WAVEfmt "u8];
    private static readonly byte[] Pdf = [.. "%PDF-1.7"u8, 0x0A];
    private static readonly byte[] CompoundFile = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0x00, 0x00];
    private static readonly byte[] ZipEntry = [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x06, 0x00];
    private static readonly byte[] EmptyZip = [0x50, 0x4B, 0x05, 0x06, 0x00, 0x00, 0x00, 0x00];
    private static readonly byte[] SpannedZip = [0x50, 0x4B, 0x07, 0x08, 0x50, 0x4B, 0x03, 0x04];
    private static readonly byte[] Rtf = [0x7B, 0x5C, 0x72, 0x74, 0x66, 0x31, 0x5C, 0x61, 0x6E, 0x73, 0x69];
    private static readonly byte[] Text = "Name;Amount\r\nAda;12\r\n"u8.ToArray();
    private static readonly byte[] Utf16 = [0xFF, 0xFE, (byte)'a', 0x00, (byte)'b', 0x00];
    private static readonly byte[] Utf16BigEndian = [0xFE, 0xFF, 0x00, (byte)'a', 0x00, (byte)'b'];
    private static readonly byte[] WithNul = [(byte)'a', 0x00, (byte)'b'];
    private static readonly byte[] SevenZip = [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C, 0x00, 0x04];
    private static readonly byte[] Rar = [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00];
    private static readonly byte[] Gzip = [0x1F, 0x8B, 0x08, 0x00];

    public static IEnumerable<TestCaseData> Kinds()
    {
        static TestCaseData Case(string name, string extension, byte[] head, bool matches) =>
            new TestCaseData(extension, head, matches).SetName($"AFilesKind_IsItsFirstBytes({name})");

        yield return Case("a JPEG as jpg", "jpg", Jpeg, true);
        yield return Case("a JPEG as jpeg", "jpeg", Jpeg, true);
        yield return Case("a PNG as jpg", "jpg", Png, false);
        yield return Case("a PNG", "png", Png, true);
        yield return Case("a JPEG as png", "png", Jpeg, false);
        yield return Case("a PNG of its signature only", "png", Png[..8], true);
        yield return Case("a PNG a byte short of its signature", "png", Png[..7], false);
        yield return Case("a GIF of 1987", "gif", Gif87, true);
        yield return Case("a GIF of 1989", "gif", Gif89, true);
        yield return Case("a GIF of another year", "gif", [.. "GIF90a"u8], false);
        yield return Case("a WebP", "webp", WebP, true);
        yield return Case("a wave file as webp", "webp", Wave, false);
        yield return Case("a WebP cut before its kind", "webp", WebP[..11], false);
        yield return Case("an AVIF by its major brand", "avif", IsoMedia("avif", "mif1", "miaf"), true);
        yield return Case("an AVIF sequence", "avif", IsoMedia("avis"), true);
        yield return Case("an AVIF among the compatible brands", "avif", IsoMedia("mif1", "miaf", "avif"), true);
        yield return Case("a HEIC as avif", "avif", IsoMedia("heic", "mif1"), false);
        yield return Case("a HEIC", "heic", IsoMedia("heic", "mif1"), true);
        yield return Case("a HEIF image as heif", "heif", IsoMedia("mif1", "heic"), true);
        yield return Case("a HEIC among the compatible brands", "heic", IsoMedia("isom", "iso2", "heic"), true);
        yield return Case("an MP4 video as heic", "heic", IsoMedia("isom", "iso2", "mp41"), false);
        yield return Case("a brand past the box's own size", "heic", [.. IsoMedia("isom", "iso2"), .. "heic"u8], false);
        yield return Case("an ISO media box too short for a brand", "heic", IsoMedia("heic")[..15], false);
        yield return Case("a PDF", "pdf", Pdf, true);
        yield return Case("a PDF without its dash", "pdf", [.. "%PDF1.7"u8], false);
        yield return Case("a Word document of a compound file", "doc", CompoundFile, true);
        yield return Case("a spreadsheet of a compound file", "xls", CompoundFile, true);
        yield return Case("slides of a compound file", "ppt", CompoundFile, true);
        yield return Case("a ZIP as doc", "doc", ZipEntry, false);
        yield return Case("a Word document of a ZIP", "docx", ZipEntry, true);
        yield return Case("a spreadsheet of a ZIP", "xlsx", ZipEntry, true);
        yield return Case("slides of a ZIP", "pptx", ZipEntry, true);
        yield return Case("an open text document", "odt", ZipEntry, true);
        yield return Case("an open spreadsheet", "ods", ZipEntry, true);
        yield return Case("open slides", "odp", ZipEntry, true);
        yield return Case("an empty ZIP as docx", "docx", EmptyZip, false);
        yield return Case("a compound file as docx", "docx", CompoundFile, false);
        yield return Case("rich text", "rtf", Rtf, true);
        yield return Case("plain text as rtf", "rtf", Text, false);
        yield return Case("text", "txt", Text, true);
        yield return Case("comma-separated values", "csv", Text, true);
        yield return Case("an empty text", "txt", [], true);
        yield return Case("text in UTF-16", "txt", Utf16, true);
        yield return Case("values in UTF-16 big-endian", "csv", Utf16BigEndian, true);
        yield return Case("text with a NUL byte", "txt", WithNul, false);
        yield return Case("a PNG as txt", "txt", Png, false);
        yield return Case("a ZIP", "zip", ZipEntry, true);
        yield return Case("an empty ZIP", "zip", EmptyZip, true);
        yield return Case("a ZIP's first part of several", "zip", SpannedZip, true);
        yield return Case("a 7z archive as zip", "zip", SevenZip, false);
        yield return Case("a 7z archive", "7z", SevenZip, true);
        yield return Case("a RAR archive", "rar", Rar, true);
        yield return Case("a gzip archive", "gz", Gzip, true);
        yield return Case("a ZIP as gz", "gz", ZipEntry, false);
        yield return Case("a tar archive", "tar", Tar(257), true);
        yield return Case("a tar mark in the wrong place", "tar", Tar(256), false);
        yield return Case("a tar archive cut before its mark", "tar", Tar(257)[..261], false);
        yield return Case("an empty PNG", "png", [], false);
        yield return Case("an extension outside the table", "dwg", WithNul, true);
        yield return Case("no extension", "", Png, true);
    }

    [TestCaseSource(nameof(Kinds))]
    public void AFilesKind_IsItsFirstBytes(string extension, byte[] head, bool matches) =>
        Assert.That(FileKinds.Matches(extension, head), Is.EqualTo(matches));

    [TestCase(new byte[] { 0x4D, 0x5A, 0x90, 0x00 }, TestName = "AProgram_IsKnownByItsBytes(Windows)")]
    [TestCase(new byte[] { 0x7F, 0x45, 0x4C, 0x46, 0x02 }, TestName = "AProgram_IsKnownByItsBytes(ELF)")]
    [TestCase(new byte[] { 0xFE, 0xED, 0xFA, 0xCE }, TestName = "AProgram_IsKnownByItsBytes(Mach-O 32 big-endian)")]
    [TestCase(new byte[] { 0xFE, 0xED, 0xFA, 0xCF }, TestName = "AProgram_IsKnownByItsBytes(Mach-O 64 big-endian)")]
    [TestCase(new byte[] { 0xCE, 0xFA, 0xED, 0xFE }, TestName = "AProgram_IsKnownByItsBytes(Mach-O 32)")]
    [TestCase(new byte[] { 0xCF, 0xFA, 0xED, 0xFE }, TestName = "AProgram_IsKnownByItsBytes(Mach-O 64)")]
    [TestCase(new byte[] { 0xCA, 0xFE, 0xBA, 0xBE }, TestName = "AProgram_IsKnownByItsBytes(universal)")]
    [TestCase(new byte[] { 0x23, 0x21, 0x2F, 0x62, 0x69, 0x6E, 0x2F, 0x73, 0x68 }, TestName = "AProgram_IsKnownByItsBytes(script)")]
    public void AProgram_IsKnownByItsBytes(byte[] head) => Assert.That(FileKinds.IsExecutable(head), Is.True);

    [Test]
    public void OtherBytes_AreNoProgram()
    {
        byte[][] others = [Jpeg, Png, Pdf, ZipEntry, Text, Utf16, [], [0x4D], [0x23], [.. "#comment"u8], [0x7F, 0x45, 0x4C]];

        Assert.That(others.Where(head => FileKinds.IsExecutable(head)), Is.Empty);
    }

    [Test]
    public void TheTable_HoldsTheExtensionsOfEachTypeGroup_AndNoOther()
    {
        var grouped = Enum.GetValues<FileTypeGroup>().SelectMany(group => FileFieldType.ExtensionsOf(group));

        Assert.That(FileKinds.Extensions, Is.EquivalentTo(grouped));
    }

    [TestCase("jpg", "image/jpeg")]
    [TestCase("jpeg", "image/jpeg")]
    [TestCase("png", "image/png")]
    [TestCase("heif", "image/heif")]
    [TestCase("pdf", "application/pdf")]
    [TestCase("docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [TestCase("csv", "text/csv")]
    [TestCase("7z", "application/x-7z-compressed")]
    [TestCase("dwg", "application/octet-stream")]
    [TestCase("", "application/octet-stream")]
    public void AFilesContentType_IsItsKinds(string extension, string contentType) =>
        Assert.That(FileKinds.ContentType(extension), Is.EqualTo(contentType));

    [Test]
    public void EveryKind_HasAContentTypeOfItsOwn() =>
        Assert.That(FileKinds.Extensions.Select(FileKinds.ContentType), Has.None.EqualTo(FileKinds.UnknownContentType));

    // An ISO media file's ftyp box: its size, its major brand, a minor version and the compatible brands.
    private static byte[] IsoMedia(string major, params string[] compatible)
    {
        var box = new byte[16 + (4 * compatible.Length)];
        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
        "ftyp"u8.CopyTo(box.AsSpan(4));
        System.Text.Encoding.ASCII.GetBytes(major).CopyTo(box, 8);
        for (var i = 0; i < compatible.Length; i++)
        {
            System.Text.Encoding.ASCII.GetBytes(compatible[i]).CopyTo(box, 16 + (4 * i));
        }

        return box;
    }

    // A tar header with its ustar mark at the given byte.
    private static byte[] Tar(int at)
    {
        var header = new byte[512];
        "ustar"u8.CopyTo(header.AsSpan(at));
        return header;
    }
}
