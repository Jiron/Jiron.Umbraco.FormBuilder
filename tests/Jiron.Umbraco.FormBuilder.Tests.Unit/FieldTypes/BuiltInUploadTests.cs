using System.Buffers.Binary;
using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Logic;
using Jiron.Umbraco.FormBuilder.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.FieldTypes;

/// <summary>
/// The checks of an upload (ADR-0071) as the upload endpoint runs them (<see cref="FieldUploads"/>): a field's limit
/// against the host's, a file's size, and its type's check of the file's name and first bytes. A signature is a PNG
/// whose header is well formed, at most three times its pad in pixels and at most a mebibyte; a file field takes the
/// extensions its check of a submission takes; and a site's own type does the same through the contract, which refuses
/// what breaks it.
/// </summary>
public sealed class BuiltInUploadTests
{
    // The host's limit for any file, 50 MB.
    private const long Host = 52_428_800;

    // A 1-by-1 PNG in true colour with alpha, and the most pixels a pad of the default size takes, 3,600 by 1,800, each
    // with the CRC zlib computes for it.
    private const string OnePixel = "89504e470d0a1a0a0000000d49484452000000010000000108060000001f15c489";
    private const string LargestByDefault = "89504e470d0a1a0a0000000d4948445200000e1000000708080600000067a76a1f";

    public static IEnumerable<TestCaseData> Signatures()
    {
        static TestCaseData Case(string name, string? settings, byte[] head, params string[] errors) =>
            new TestCaseData(settings, head, errors).SetName($"ChecksASignature({name})");

        // The bit depths PNG allows for each colour type: grey, true colour, indexed, grey with alpha, true colour with alpha.
        (byte Colour, byte[] Depths)[] allowed = [(0, [1, 2, 4, 8, 16]), (2, [8, 16]), (3, [1, 2, 4, 8]), (4, [8, 16]), (6, [8, 16])];

        yield return Case("one pixel", null, Png(1, 1));
        yield return Case("the most pixels of a pad of the default size", null, Png(3600, 1800));
        yield return Case("a pixel too wide for the default size", null, Png(3601, 1800), "invalid");
        yield return Case("a pixel too tall for the default size", null, Png(3600, 1801), "invalid");
        yield return Case("three times its own pad", """{"width":300,"height":150}""", Png(900, 450));
        yield return Case("a pixel too wide for its own pad", """{"width":300,"height":150}""", Png(901, 450), "invalid");
        yield return Case("a pixel too tall for its own pad", """{"width":300,"height":150}""", Png(900, 451), "invalid");
        yield return Case("its own width and the default height", """{"width":300}""", Png(900, 1800));
        yield return Case("the smallest pad at twice its size", """{"width":200,"height":100}""", Png(400, 200));
        yield return Case("the largest pad at three times its size", """{"width":1200,"height":600}""", Png(3600, 1800));
        yield return Case("interlaced", null, Png(10, 10, interlace: 1));
        foreach (var (colour, depths) in allowed)
        {
            foreach (var depth in depths)
            {
                yield return Case($"colour type {colour} at {depth} bits", null, Png(10, 10, depth, colour));
            }
        }

        yield return Case("grey at 3 bits", null, Png(10, 10, 3, 0), "invalid");
        yield return Case("grey at 32 bits", null, Png(10, 10, 32, 0), "invalid");
        yield return Case("true colour at 4 bits", null, Png(10, 10, 4, 2), "invalid");
        yield return Case("indexed at 16 bits", null, Png(10, 10, 16, 3), "invalid");
        yield return Case("grey with alpha at 4 bits", null, Png(10, 10, 4, 4), "invalid");
        yield return Case("true colour with alpha at 1 bit", null, Png(10, 10, 1, 6), "invalid");
        yield return Case("colour type 1", null, Png(10, 10, 8, 1), "invalid");
        yield return Case("colour type 5", null, Png(10, 10, 8, 5), "invalid");
        yield return Case("colour type 7", null, Png(10, 10, 8, 7), "invalid");
        yield return Case("compression method 1", null, Png(1, 1, compression: 1), "invalid");
        yield return Case("filter method 1", null, Png(1, 1, filter: 1), "invalid");
        yield return Case("interlace method 2", null, Png(1, 1, interlace: 2), "invalid");
        yield return Case("no width", null, Png(0, 1), "invalid");
        yield return Case("no height", null, Png(1, 0), "invalid");
        yield return Case("another chunk first", null, Png(1, 1, type: "gAMA"), "invalid");
        yield return Case("its chunk type in lowercase", null, Png(1, 1, type: "ihdr"), "invalid");
        yield return Case("a chunk of another length", null, Png(1, 1, length: 12), "invalid");
        yield return Case("another CRC", null, Changed(Png(1, 1), 32, 0x8A), "invalid");
        yield return Case("a width its CRC was not computed for", null, Changed(Png(1, 1), 19, 2), "invalid");
        yield return Case("a changed signature byte", null, Changed(Png(1, 1), 1, (byte)'Q'), "invalid");
        yield return Case("its line endings turned around", null, Changed(Changed(Png(1, 1), 4, 10), 5, 13), "invalid");
        yield return Case("only the signature", null, Png(1, 1)[..8], "invalid");
        yield return Case("a byte short of the header", null, Png(1, 1)[..32], "invalid");
        yield return Case("nothing", null, [], "invalid");
        yield return Case("a JPEG", null, Padded([0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, .. "JFIF"u8]), "invalid");
        yield return Case("a GIF", null, Padded("GIF89a"u8), "invalid");
        yield return Case("a PDF", null, Padded("%PDF-1.7"u8), "invalid");
        yield return Case("an SVG", null, Padded("<svg xmlns=\"http://www.w3.org/2000/svg\">"u8), "invalid");
        yield return Case("the first 512 bytes, of which only the header counts", null, [.. Png(10, 10), .. Enumerable.Repeat((byte)0xFF, FieldUploadCheck.HeadLength - PngHeader.Length)]);
    }

    [Test]
    public void TheTestsHeaders_CarryTheCrcZlibComputes() =>
        Assert.Multiple(() =>
        {
            Assert.That(Convert.ToHexStringLower(Png(1, 1)), Is.EqualTo(OnePixel));
            Assert.That(Convert.ToHexStringLower(Png(3600, 1800)), Is.EqualTo(LargestByDefault));
        });

    [TestCaseSource(nameof(Signatures))]
    public void ASignature_IsAPngWithinThreeTimesItsPad(string? settings, byte[] head, string[] errors) =>
        Assert.That(Checked("signature", settings, "signature.png", head), Is.EqualTo(errors));

    [TestCase(1u, 1u, 1, 1)]
    [TestCase(2_147_483_647u, 1u, int.MaxValue, 1)]
    [TestCase(1u, 2_147_483_647u, 1, int.MaxValue)]
    public void APngHeader_GivesItsSizeInPixels(uint width, uint height, int readWidth, int readHeight) =>
        Assert.That(PngHeader.Read(Png(width, height)), Is.EqualTo((readWidth, readHeight)));

    [TestCase(2_147_483_648u, 1u)]
    [TestCase(1u, 2_147_483_648u)]
    [TestCase(uint.MaxValue, uint.MaxValue)]
    public void APngHeader_PastTheLargestSizePngAllows_IsNoHeader(uint width, uint height) =>
        Assert.That(PngHeader.Read(Png(width, height)), Is.Null);

    [TestCase(Host, 1_048_576)]
    [TestCase(1_048_577, 1_048_576)]
    [TestCase(1_048_576, 1_048_576)]
    [TestCase(500_000, 500_000)]
    public void ASignaturesLimit_IsAMebibyte_OrTheHostsWhenItIsSmaller(long host, long limit) =>
        Assert.That(FieldUploads.Limit(Type("signature"), Field("signature"), host), Is.EqualTo(limit));

    [TestCase(0, Host, null)]
    [TestCase(1_048_576, Host, null)]
    [TestCase(1_048_577, Host, "invalid")]
    [TestCase(10_485_760, Host, "invalid")]
    [TestCase(500_000, 500_000, null)]
    [TestCase(500_001, 500_000, "invalid")]
    public void ASignatureTooLarge_IsInvalid(long size, long host, string? error) =>
        Assert.That(SizeChecked("signature", null, size, host), Is.EqualTo(error), "a signature raises no file size error of its own: only a client that skips the runtime sends one too large");

    [TestCase(null, Host, Host)]
    [TestCase("""{"maxBytes":1000}""", Host, 1000)]
    [TestCase("""{"maxBytes":1}""", Host, 1)]
    [TestCase("""{"maxBytes":999999999999999}""", Host, Host)]
    [TestCase("""{"maxBytes":1000}""", 999, 999)]
    public void AFileFieldsLimit_IsItsMostBytes_OrTheHostsWhenItIsSmaller(string? settings, long host, long limit) =>
        Assert.That(FieldUploads.Limit(Type("file"), Field("file", settings), host), Is.EqualTo(limit));

    [TestCase("""{"maxBytes":1000}""", 1000, Host, null)]
    [TestCase("""{"maxBytes":1000}""", 1001, Host, "fileSize")]
    [TestCase(null, Host, Host, null)]
    [TestCase(null, Host + 1, Host, "fileSize")]
    [TestCase(null, 0, Host, null)]
    public void AFileTooLarge_FailsFileSize(string? settings, long size, long host, string? error) =>
        Assert.That(SizeChecked("file", settings, size, host), Is.EqualTo(error));

    [TestCase(null, "photo.jpg")]
    [TestCase(null, "report.PDF")]
    [TestCase(null, "report.docx", "fileType")]
    [TestCase(null, "photo.jpg.exe", "fileType")]
    [TestCase(null, "photo", "fileType")]
    [TestCase(null, "logo.svg", "fileType")]
    [TestCase("""{"extensions":["dwg"]}""", "plan.DWG")]
    [TestCase("""{"extensions":["dwg"]}""", "photo.jpg", "fileType")]
    [TestCase("""{"types":["office","archives"]}""", "minutes.odt")]
    [TestCase("""{"types":["office","archives"]}""", "backup.tar")]
    public void AFileField_TakesTheExtensionsOfItsSettings_WhenItIsUploaded(string? settings, string name, params string[] errors) =>
        Assert.That(Checked("file", settings, name, "%PDF-1.7"u8.ToArray()), Is.EqualTo(errors));

    [TestCase(null)]
    [TestCase("""{"extensions":["dwg"]}""")]
    [TestCase("""{"types":["text"],"extensions":["md"]}""")]
    public void AFileFieldsUploads_TakeTheExtensionsItsCheckOfASubmissionTakes(string? settings)
    {
        string[] names = ["a.jpg", "a.png", "a.pdf", "a.docx", "a.txt", "a.csv", "a.md", "a.dwg", "a.zip", "a.svg", "a.exe", "a"];

        Assert.Multiple(() =>
        {
            foreach (var name in names)
            {
                var submitted = ValidationCase.Uploads(settings, (name, 1)).Errors().Contains("fileType");
                Assert.That(Checked("file", settings, name, [0]).Contains("fileType"), Is.EqualTo(submitted), name);
            }
        });
    }

    [Test]
    public void AFileFieldsUpload_LeavesTheBytesToTheHostsChecks() =>
        Assert.That(Checked("file", null, "photo.png", "%PDF-1.7"u8.ToArray()), Is.Empty, "whether a file's bytes match its extension is the host's check of every upload (7.3)");

    [Test]
    public void AnUpload_ToAFieldThatStoresNoFiles_IsRefused()
    {
        var text = Type("text");
        var field = Field("text");

        Assert.Multiple(() =>
        {
            Assert.That(() => FieldUploads.Limit(text, field, Host), Throws.ArgumentException.With.Message.Contains("'text' stores no files"));
            Assert.That(() => FieldUploads.CheckSize(text, field, 1, Host), Throws.ArgumentException.With.Message.Contains("'text' stores no files"));
            Assert.That(() => FieldUploads.Check(text, field, "a.txt", new byte[] { 1 }), Throws.ArgumentException.With.Message.Contains("'text' stores no files"));
            Assert.That(() => FieldUploads.Check(Type("repeater"), Field("repeater"), "a.txt", new byte[] { 1 }), Throws.ArgumentException);
        });
    }

    [Test]
    public void AnUpload_IsCheckedOnItsFirstBytesAtMost()
    {
        var signature = Type("signature");
        var field = Field("signature");
        byte[] longest = [.. Png(1, 1), .. new byte[FieldUploadCheck.HeadLength - PngHeader.Length]];

        Assert.Multiple(() =>
        {
            Assert.That(FieldUploads.Check(signature, field, "signature.png", longest), Is.Empty);
            Assert.That(() => FieldUploads.Check(signature, field, "signature.png", longest.Append((byte)0).ToArray()), Throws.ArgumentException.With.Message.Contains("at most the first 512 bytes"));
            Assert.That(() => FieldUploads.Check(signature, field, "", Png(1, 1)), Throws.ArgumentException);
            Assert.That(() => FieldUploads.CheckSize(signature, field, -1, Host), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => FieldUploads.Limit(signature, field, 0), Throws.InstanceOf<ArgumentOutOfRangeException>());
        });
    }

    [Test]
    public void ASitesTypeWithoutChecksOfItsOwn_TakesAnyFileUpToTheHostsLimit()
    {
        IFormBuilderFieldType plain = new PlainFileType();
        var field = Field("acme.plain");

        Assert.Multiple(() =>
        {
            Assert.That(FieldUploads.Limit(plain, field, Host), Is.EqualTo(Host));
            Assert.That(FieldUploads.CheckSize(plain, field, Host + 1, Host), Is.EqualTo(FieldMessageKinds.Invalid), "a type without the file size kind fails invalid");
            Assert.That(FieldUploads.Check(plain, field, "anything", Png(1, 1)), Is.Empty);
        });
    }

    [Test]
    public void ASitesType_ChecksItsUploads_ThroughItsSettings()
    {
        var stamp = new StampFieldType();
        var field = Field("acme.stamp", """{"magic":"JFB","maxBytes":2048}""");

        Assert.Multiple(() =>
        {
            Assert.That(FieldUploads.Limit(stamp, field, Host), Is.EqualTo(2048));
            Assert.That(FieldUploads.CheckSize(stamp, field, 2049, Host), Is.EqualTo(FieldMessageKinds.FileSize));
            Assert.That(FieldUploads.Check(stamp, field, "a.stamp", "JFB-1"u8.ToArray()), Is.Empty);
            Assert.That(FieldUploads.Check(stamp, field, "a.stamp", "STAMP"u8.ToArray()), Is.EqualTo(new[] { FieldMessageKinds.Invalid }));
            Assert.That(FieldUploads.Check(stamp, field, "a.txt", "STAMP"u8.ToArray()), Is.EqualTo(new[] { FieldMessageKinds.Invalid, FieldMessageKinds.FileType }), "in the order reported");
            Assert.That(FieldUploads.Check(stamp, Field("acme.stamp"), "a.stamp", "STAMP-1"u8.ToArray()), Is.Empty, "its default settings");
        });
    }

    [Test]
    public void ATypesLimitBelowOneByte_IsRefused_NamingTheType() =>
        Assert.That(
            () => FieldUploads.Limit(new StampFieldType(), Field("acme.stamp", """{"maxBytes":0}"""), Host),
            Throws.InvalidOperationException.With.Message.Contains("'acme.stamp'").And.Message.Contains("below 1 byte"));

    [Test]
    public void ATypesCheckOfAnUpload_ReportsEachKindOnce_AndOnlyKindsItDeclares()
    {
        var field = Field("acme.reporter");

        Assert.Multiple(() =>
        {
            byte[] head = [1];
            Assert.That(FieldUploads.Check(new ReportingFileType(FieldMessageKinds.Invalid, FieldMessageKinds.FileType, FieldMessageKinds.Invalid), field, "a", head), Is.EqualTo(new[] { FieldMessageKinds.Invalid, FieldMessageKinds.FileType }));
            Assert.That(() => FieldUploads.Check(new ReportingFileType(FieldMessageKinds.FileSize), field, "a", head), Throws.ArgumentException.With.Message.Contains("does not declare the error kind 'FileSize'"));
            Assert.That(() => FieldUploads.Check(new ReportingFileType(FieldMessageKinds.None), field, "a", head), Throws.ArgumentException.With.Message.Contains("not exactly one error kind"));
            Assert.That(() => FieldUploads.Check(new ReportingFileType(FieldMessageKinds.Invalid | FieldMessageKinds.FileType), field, "a", head), Throws.ArgumentException.With.Message.Contains("not exactly one error kind"));
        });
    }

    [Test]
    public void TheBuiltInTypesThatStoreFiles_AreTheFileAndTheSignature() =>
        Assert.That(TestFieldTypes.Registry.All.Where(type => type.Storage == FieldStorageKind.File).Select(type => type.Alias), Is.EquivalentTo(new[] { "file", "signature" }), "each has its check of an upload here");

    // The header of a PNG image: its signature and an IHDR chunk with the given values and the CRC over its type and
    // data, computed with a table as the PNG specification gives it, apart from the product's own bitwise CRC.
    private static byte[] Png(uint width, uint height, byte depth = 8, byte colour = 6, byte compression = 0, byte filter = 0, byte interlace = 0, string type = "IHDR", uint length = 13)
    {
        var header = new byte[PngHeader.Length];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(header, 0);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(8), length);
        Encoding.ASCII.GetBytes(type).CopyTo(header, 12);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(16), width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(20), height);
        header[24] = depth;
        header[25] = colour;
        header[26] = compression;
        header[27] = filter;
        header[28] = interlace;
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(29), Crc(header.AsSpan(12, 17)));
        return header;
    }

    private static uint Crc(ReadOnlySpan<byte> bytes)
    {
        var table = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        var crc = 0xFFFFFFFFu;
        foreach (var value in bytes)
        {
            crc = table[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private static byte[] Changed(byte[] bytes, int at, byte value)
    {
        var changed = bytes.ToArray();
        changed[at] = value;
        return changed;
    }

    // A file's first bytes, filled up to a PNG header's length so that only what they are decides.
    private static byte[] Padded(ReadOnlySpan<byte> start) => [.. start, .. new byte[Math.Max(0, PngHeader.Length - start.Length)]];

    private static string[] Checked(string type, string? settings, string name, byte[] head) =>
        [.. FieldUploads.Check(Type(type), Field(type, settings), name, head).Select(ValidationCase.Name)];

    private static string? SizeChecked(string type, string? settings, long size, long host) =>
        FieldUploads.CheckSize(Type(type), Field(type, settings), size, host) is { } kind ? ValidationCase.Name(kind) : null;

    private static IFormBuilderFieldType Type(string alias) => TestFieldTypes.Registry.FindType(alias)!;

    private static FormField Field(string type, string? settings = null) =>
        new() { Key = Guid.NewGuid(), Type = type, Alias = "upload", Settings = settings is null ? null : SettingsObject.Parse(settings) };

    private static FieldTypeDeclaration Declare(string alias, FieldMessageKinds messages) => new()
    {
        Alias = alias,
        Name = alias,
        Icon = "icon-attachment",
        PaletteGroup = FieldPaletteGroup.Files,
        HoldsValue = true,
        Storage = FieldStorageKind.File,
        RuleKind = FieldKind.Files,
        Features = FieldFeatures.Label | FieldFeatures.Required,
        Messages = messages,
    };

    // A site's type that stores files, written against the interface alone: every check is the interface's default.
    private sealed class PlainFileType : IFormBuilderFieldType
    {
        public FieldTypeDeclaration Declaration { get; } = Declare("acme.plain", FieldMessageKinds.Required);

        public FieldSettingsSchema SettingsSchema { get; } = FieldSettingsReader.Read(typeof(NoFieldSettings));
    }

    // A site's type whose files start with the stamp its settings name and end in ".stamp", up to the bytes its settings
    // allow.
    private sealed class StampFieldType() : FormBuilderFieldTypeBase<StampSettings>(Declare("acme.stamp", FieldMessageKinds.Required | FieldMessageKinds.FileType | FieldMessageKinds.FileSize))
    {
        protected override long? UploadLimit(StampSettings settings, FormField field) => settings.MaxBytes;

        protected override void CheckUpload(StampSettings settings, FieldUploadCheck check)
        {
            if (!check.Head.Span.StartsWith(Encoding.ASCII.GetBytes(settings.Magic)))
            {
                check.Fail(FieldMessageKinds.Invalid);
            }

            if (!check.Name.EndsWith(".stamp", StringComparison.Ordinal))
            {
                check.Fail(FieldMessageKinds.FileType);
            }
        }
    }

    public sealed class StampSettings
    {
        [FormBuilderSetting("Magic")]
        public string Magic { get; init; } = "STAMP";

        [FormBuilderSetting("Most bytes")]
        public long? MaxBytes { get; init; }
    }

    // A site's type that reports the given kinds, in order, for every upload.
    private sealed class ReportingFileType(params FieldMessageKinds[] kinds) : IFormBuilderFieldType
    {
        public FieldTypeDeclaration Declaration { get; } = Declare("acme.reporter", FieldMessageKinds.Required | FieldMessageKinds.FileType);

        public FieldSettingsSchema SettingsSchema { get; } = FieldSettingsReader.Read(typeof(NoFieldSettings));

        public void CheckUpload(FieldUploadCheck check)
        {
            foreach (var kind in kinds)
            {
                check.Fail(kind);
            }
        }
    }
}
