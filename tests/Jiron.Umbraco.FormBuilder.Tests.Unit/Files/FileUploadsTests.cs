using System.Security.Cryptography;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Testing;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Files;

/// <summary>
/// One upload before its submission (ADR-0087): the name cleaned for downloads, the first bytes checked in their order,
/// and the bytes stored and recorded only once the form, the size, the request's end and the row all passed, so that
/// whatever is not kept leaves nothing in the store, and the file's name is never logged.
/// </summary>
public sealed class FileUploadsTests
{
    private const char Backslash = (char)92;
    private const int FormId = 7;

    private static readonly Guid FormKey = Guid.Parse("f0000000-0000-4000-8000-000000000087");
    private static readonly byte[] Pdf = [.. "%PDF-1.7"u8, 0x0A, .. "1 0 obj"u8];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, .. "JFIF"u8, 0x00];
    private static readonly byte[] Program = [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00];

    // A 1-by-1 PNG in true colour with alpha, with the CRC zlib computes for its header.
    private static readonly byte[] OnePixel = Convert.FromHexString("89504e470d0a1a0a0000000d49484452000000010000000108060000001f15c489");

    private Store _store = null!;
    private IFormRepository _forms = null!;
    private IEntryRepository _entries = null!;
    private RecordingLogger _logger = null!;

    public static IEnumerable<TestCaseData> Names()
    {
        static TestCaseData Case(string name, string? given, string? cleaned) => new TestCaseData(given, cleaned).SetName($"AFilesName_IsCleanedForDownloads({name})");

        var pair = new string([(char)0xD83D, (char)0xDE00]);
        yield return Case("none", null, null);
        yield return Case("empty", "", null);
        yield return Case("only spaces", "   ", null);
        yield return Case("only points", "...", null);
        yield return Case("a plain name", "Report 2026.pdf", "Report 2026.pdf");
        yield return Case("a Windows path", $"C:{Backslash}fakepath{Backslash}report.pdf", "report.pdf");
        yield return Case("a path", "photos/2026/beach.jpg", "beach.jpg");
        yield return Case("a path that ends in a separator", "photos/", null);
        yield return Case("control characters", $"re{(char)0}port{(char)0x1B}{(char)9}.pdf", "report.pdf");
        yield return Case("a decomposed accent", $"Cafe{(char)0x301}.pdf", $"Caf{(char)0xE9}.pdf");
        yield return Case("a lone surrogate", $"a{(char)0xD800}.pdf", $"a{(char)0xFFFD}.pdf");
        yield return Case("spaces around it", $"  report.pdf{(char)0xA0} ", "report.pdf");
        yield return Case("a name at its longest", new string('a', 251) + ".pdf", new string('a', 251) + ".pdf");
        yield return Case("a name too long", new string('a', 300) + ".pdf", new string('a', 251) + ".pdf");
        yield return Case("a name cut before a whole character", new string('a', 250) + pair + "bbb.pdf", new string('a', 250) + ".pdf");
        yield return Case("an extension too long to keep", "a." + new string('x', 300), "a." + new string('x', 253));
        yield return Case("a name of its extension", ".htaccess", ".htaccess");
    }

    [SetUp]
    public void CreateTheSite()
    {
        _store = new Store();
        _forms = Substitute.For<IFormRepository>();
        _forms.GetAsync(FormKey, Arg.Any<CancellationToken>()).Returns(Form());
        _entries = Substitute.For<IEntryRepository>();
        _entries.AddFileAsync(Arg.Any<NewEntryFile>(), Arg.Any<CancellationToken>()).Returns(call => Recorded(call.Arg<NewEntryFile>()));
        _logger = new RecordingLogger();
    }

    [TestCaseSource(nameof(Names))]
    public void AFilesName_IsCleanedForDownloads(string? given, string? cleaned)
    {
        var name = FileUploads.CleanName(given);

        Assert.Multiple(() =>
        {
            Assert.That(name, Is.EqualTo(cleaned));
            Assert.That(name?.Length ?? 0, Is.AtMost(FileUploads.MaxNameLength));
        });
    }

    [Test]
    public void AFile_IsCheckedOnItsFirstBytes_InTheOrderOfTheDecision()
    {
        var uploads = Uploads();
        var anyFile = File("""{"types":["images","pdf"]}""");
        var drawings = File("""{"extensions":["dwg","svg"]}""");
        string? Check(FormField field, string name, byte[] head) =>
            uploads.Check(uploads.TypeOf(field)!, field, name, head) is { } kind ? Name(kind) : null;

        Assert.Multiple(() =>
        {
            Assert.That(Check(anyFile, "photo.jpg", Jpeg), Is.Null);
            Assert.That(Check(anyFile, "report.PDF", Pdf), Is.Null, "the extension in any case");
            Assert.That(Check(anyFile, "photo.jpg", Program), Is.EqualTo("fileType"), "a program, whatever its name");
            Assert.That(Check(drawings, "drawing.svg", "<svg/>"u8.ToArray()), Is.EqualTo("fileType"), "an extension the site blocks, whatever the field lists");
            Assert.That(Check(anyFile, "notes.txt", "Notes"u8.ToArray()), Is.EqualTo("fileType"), "an extension the field does not take");
            Assert.That(Check(anyFile, "photo.png", Pdf), Is.EqualTo("fileType"), "bytes of another kind than the extension names");
            Assert.That(Check(drawings, "plan.dwg", Program[1..]), Is.Null, "an extension outside the table takes any bytes but a program's");
            Assert.That(Check(Signature(), "signature.png", OnePixel), Is.Null);
            Assert.That(Check(Signature(), "signature.png", Jpeg), Is.EqualTo("invalid"), "the type's own check");
            Assert.That(Check(Signature(), "signature.exe", OnePixel), Is.EqualTo("invalid"), "a signature has no message of its own for file types");
            Assert.That(Check(Signature(), "signature.png", Program), Is.EqualTo("invalid"));
        });
    }

    [Test]
    public void ASitesOwnBlockedExtensions_ReplaceTheDefaults()
    {
        var uploads = Uploads(new FieldHostLimits(FieldHostLimits.Default.MaxFileBytes, ["dwg"]));
        var drawings = File("""{"extensions":["dwg","svg"]}""");
        var type = uploads.TypeOf(drawings)!;

        Assert.Multiple(() =>
        {
            Assert.That(uploads.Check(type, drawings, "drawing.svg", "<svg/>"u8.ToArray()), Is.Null);
            Assert.That(uploads.Check(type, drawings, "plan.dwg", Pdf), Is.EqualTo(FieldMessageKinds.FileType));
        });
    }

    [Test]
    public void OnlyAFieldWhoseTypeStoresFiles_TakesUploads_UpToItsLimitOrTheSites()
    {
        var uploads = Uploads(new FieldHostLimits(1000, []));

        Assert.Multiple(() =>
        {
            Assert.That(uploads.TypeOf(File())?.Declaration.Alias, Is.EqualTo("file"));
            Assert.That(uploads.TypeOf(Signature())?.Declaration.Alias, Is.EqualTo("signature"));
            Assert.That(uploads.TypeOf(Field("text", null)), Is.Null);
            Assert.That(uploads.TypeOf(Field("unknown", null)), Is.Null);
            Assert.That(uploads.LimitOf(uploads.TypeOf(File())!, File()), Is.EqualTo(1000));
            Assert.That(uploads.LimitOf(uploads.TypeOf(File())!, File("""{"maxBytes":10}""")), Is.EqualTo(10));
            Assert.That(uploads.RequestLimitOf(uploads.TypeOf(File())!, File("""{"maxBytes":10}""")), Is.EqualTo(10 + 65_536));
            Assert.That(FileUploads.SizeError(uploads.TypeOf(File())!), Is.EqualTo(FieldMessageKinds.FileSize));
            Assert.That(FileUploads.SizeError(uploads.TypeOf(Signature())!), Is.EqualTo(FieldMessageKinds.Invalid));
        });
    }

    [Test]
    public async Task AFileThatPasses_IsStored_AndRecordedWithoutAnEntry_OnceNothingFollowsIt()
    {
        var field = File();
        var bytes = Pdf.Concat(Enumerable.Repeat((byte)'x', 2000)).ToArray();
        var storedWhenAsked = -1;

        var result = await StoreAsync(field, "scans/Report.pdf", bytes, _ =>
        {
            storedWhenAsked = _store.Files.Count;
            return Task.FromResult(true);
        });

        var recorded = (NewEntryFile)_entries.ReceivedCalls().Single(call => call.GetMethodInfo().Name == nameof(IEntryRepository.AddFileAsync)).GetArguments()[0]!;
        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(FileUploadStatus.Stored));
            Assert.That(result.File?.Key, Is.EqualTo(recorded.Key));
            Assert.That(storedWhenAsked, Is.EqualTo(1), "the end is read once the bytes are stored");
            Assert.That(_store.Files.Single().Value, Is.EqualTo(bytes));
            Assert.That(recorded.Key, Is.Not.EqualTo(Guid.Empty));
            Assert.That((recorded.FormId, recorded.FieldKey), Is.EqualTo((FormId, field.Key)));
            Assert.That(recorded.OriginalName, Is.EqualTo("Report.pdf"));
            Assert.That(recorded.StoredName, Is.EqualTo(_store.Files.Single().Key).And.Not.Contains("Report"));
            Assert.That(recorded.ContentType, Is.EqualTo("application/pdf"));
            Assert.That(recorded.SizeBytes, Is.EqualTo(bytes.Length));
            Assert.That(recorded.FileHash, Is.EqualTo(Convert.ToHexStringLower(SHA256.HashData(bytes))));
            Assert.That(recorded.ScanState, Is.EqualTo(FileScanStates.Unscanned));
            Assert.That(_logger.Entries.Select(entry => entry.Level), Is.EqualTo(new[] { LogLevel.Debug }));
            Assert.That(_logger.Entries[0].Message, Does.Contain(recorded.Key.ToString()).And.Not.Contain("Report").And.Not.Contain(recorded.StoredName));
        });
    }

    [Test]
    public async Task AFileOutsideTheTable_IsStoredAsBytes_AndAnEmptyTextAsText()
    {
        var drawing = await StoreAsync(File("""{"extensions":["dwg"]}"""), "plan.dwg", [1, 2, 3]);
        var empty = await StoreAsync(File("""{"types":["text"]}"""), "empty.txt", []);

        Assert.Multiple(() =>
        {
            Assert.That(drawing.File?.ContentType, Is.EqualTo("application/octet-stream"), "never the client's");
            Assert.That((empty.File?.ContentType, empty.File?.SizeBytes), Is.EqualTo(("text/plain", 0L)));
        });
    }

    [Test]
    public async Task WhatIsRefusedBeforeItIsStored_LeavesNothing_AndAsksForNoForm()
    {
        var answers = new Dictionary<string, string>
        {
            ["no name"] = Answer(await StoreAsync(File(), null, Pdf)),
            ["a name that is only a path"] = Answer(await StoreAsync(File(), "scans/", Pdf)),
            ["a program"] = Answer(await StoreAsync(File(), "report.pdf", Program)),
            ["a signature that is no PNG"] = Answer(await StoreAsync(Signature(), "signature.png", Jpeg)),
            ["first bytes past the field's limit"] = Answer(await StoreAsync(File("""{"maxBytes":5}"""), "report.pdf", Pdf)),
        };

        Assert.Multiple(async () =>
        {
            Assert.That(answers, Is.EqualTo(new Dictionary<string, string>
            {
                ["no name"] = "InvalidName",
                ["a name that is only a path"] = "InvalidName",
                ["a program"] = "Refused fileType",
                ["a signature that is no PNG"] = "Refused invalid",
                ["first bytes past the field's limit"] = "TooLarge fileSize",
            }));
            Assert.That(_store.Files, Is.Empty);
            await _forms.DidNotReceiveWithAnyArgs().GetAsync(default, default);
            await _entries.DidNotReceiveWithAnyArgs().AddFileAsync(default!, default);
        });
    }

    [Test]
    public async Task AFilePastItsLimit_IsCutOff_AndLeavesNothing()
    {
        var file = await StoreAsync(File("""{"maxBytes":100}"""), "report.pdf", [.. Pdf, .. new byte[200]]);
        var signature = await StoreAsync(Signature(), "signature.png", [.. OnePixel, .. new byte[1_048_576]]);

        Assert.Multiple(async () =>
        {
            Assert.That(Answer(file), Is.EqualTo("TooLarge fileSize"));
            Assert.That(Answer(signature), Is.EqualTo("TooLarge invalid"), "a signature has no message of its own for sizes");
            Assert.That(_store.Files, Is.Empty);
            Assert.That(_store.Deleted, Is.Empty, "nothing was stored to delete");
            await _entries.DidNotReceiveWithAnyArgs().AddFileAsync(default!, default);
        });
    }

    [TestCase(null, TestName = "AFormNoLongerServed_StoresNothing(gone)")]
    [TestCase("trashed", TestName = "AFormNoLongerServed_StoresNothing(in the trash)")]
    [TestCase("unpublished", TestName = "AFormNoLongerServed_StoresNothing(unpublished)")]
    public async Task AFormNoLongerServed_StoresNothing(string? state)
    {
        _forms.GetAsync(FormKey, Arg.Any<CancellationToken>()).Returns(state switch
        {
            "trashed" => Form() with { TrashedUtc = DateTimeOffset.UnixEpoch },
            "unpublished" => Form() with { PublishedVersionId = null },
            _ => null,
        });

        var result = await StoreAsync(File(), "report.pdf", Pdf);

        Assert.Multiple(async () =>
        {
            Assert.That(Answer(result), Is.EqualTo("FormGone"));
            Assert.That(_store.Files, Is.Empty);
            await _entries.DidNotReceiveWithAnyArgs().AddFileAsync(default!, default);
        });
    }

    [Test]
    public async Task AFormThatGoesWhileTheFileIsRead_LeavesNoBytes()
    {
        _entries.AddFileAsync(Arg.Any<NewEntryFile>(), Arg.Any<CancellationToken>()).Returns((StoredEntryFile?)null);

        var result = await StoreAsync(File(), "report.pdf", Pdf);

        Assert.Multiple(() =>
        {
            Assert.That(Answer(result), Is.EqualTo("FormGone"));
            Assert.That(_store.Files, Is.Empty);
            Assert.That(_store.Deleted, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task SomethingAfterTheFile_LeavesNothing_AndRecordsNoRow()
    {
        var result = await StoreAsync(File(), "report.pdf", Pdf, _ => Task.FromResult(false));

        Assert.Multiple(async () =>
        {
            Assert.That(Answer(result), Is.EqualTo("Malformed"));
            Assert.That(_store.Files, Is.Empty);
            await _entries.DidNotReceiveWithAnyArgs().AddFileAsync(default!, default);
        });
    }

    [Test]
    public async Task ARequestPastItsLimitAfterTheFile_IsTooLarge_AndLeavesNoBytes()
    {
        var result = await StoreAsync(File(), "report.pdf", Pdf, _ => throw new FileTooLargeException(1000));

        Assert.Multiple(async () =>
        {
            Assert.That(Answer(result), Is.EqualTo("TooLarge fileSize"));
            Assert.That(_store.Files, Is.Empty);
            Assert.That(_store.Deleted, Has.Count.EqualTo(1));
            await _entries.DidNotReceiveWithAnyArgs().AddFileAsync(default!, default);
        });
    }

    [Test]
    public void AFailureAfterTheBytesAreStored_ReachesTheCaller_WithTheBytesDeleted()
    {
        var failing = Substitute.For<IEntryRepository>();
        failing.AddFileAsync(Arg.Any<NewEntryFile>(), Arg.Any<CancellationToken>()).Returns<StoredEntryFile?>(_ => throw new InvalidOperationException("The database is gone."));

        Assert.ThrowsAsync<InvalidOperationException>(() => StoreAsync(File(), "report.pdf", Pdf, entries: failing));
        Assert.ThrowsAsync<InvalidDataException>(() => StoreAsync(File(), "report.pdf", Pdf, _ => throw new InvalidDataException("A long epilogue.")));

        Assert.Multiple(() =>
        {
            Assert.That(_store.Files, Is.Empty);
            Assert.That(_store.Deleted, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void AStoreThatStopsBeforeTheEnd_IsAnError_AndItsBytesAreDeleted()
    {
        _store.ReadsOnce = true;

        var error = Assert.ThrowsAsync<InvalidOperationException>(() => StoreAsync(File(), "report.pdf", [.. Pdf, .. new byte[100]]));

        Assert.Multiple(() =>
        {
            Assert.That(error!.Message, Does.Contain("before it read the upload to its end"));
            Assert.That(_store.Files, Is.Empty);
            Assert.That(_store.Deleted, Has.Count.EqualTo(1));
        });
    }

    [TestCase(10, TestName = "ARequestThatCannotBeRead_ArrivesAsAReadException_AndLeavesNothing(in its first bytes)")]
    [TestCase(2000, TestName = "ARequestThatCannotBeRead_ArrivesAsAReadException_AndLeavesNothing(while it is stored)")]
    public void ARequestThatCannotBeRead_ArrivesAsAReadException_AndLeavesNothing(int length)
    {
        var field = File();
        var uploads = Uploads();
        using var cut = new CutStream([.. Pdf, .. new byte[length]]);

        var error = Assert.ThrowsAsync<FileUploadReadException>(() => uploads.StoreAsync(FormKey, uploads.TypeOf(field)!, field, "report.pdf", cut, _ => Task.FromResult(true), CancellationToken.None));

        Assert.Multiple(() =>
        {
            Assert.That(error!.InnerException, Is.InstanceOf<IOException>());
            Assert.That(_store.Files, Is.Empty);
        });
    }

    [Test]
    public async Task BytesThatCannotBeDeletedAgain_AreLogged_WithoutTheFilesName()
    {
        _store.FailDeletes = true;
        _entries.AddFileAsync(Arg.Any<NewEntryFile>(), Arg.Any<CancellationToken>()).Returns((StoredEntryFile?)null);

        var result = await StoreAsync(File(), "Secret plans.pdf", Pdf);

        Assert.Multiple(() =>
        {
            Assert.That(Answer(result), Is.EqualTo("FormGone"));
            Assert.That(_store.Files, Has.Count.EqualTo(1), "kept for retention");
            Assert.That(_logger.Entries.Select(entry => entry.Level), Is.EqualTo(new[] { LogLevel.Warning }));
            Assert.That(_logger.Entries[0].Message, Does.Contain("could not delete").And.Contain("IOException").And.Not.Contain("Secret"));
        });
    }

    private static string Answer(FileUploadResult result) => result.Error is { } kind ? $"{result.Status} {Name(kind)}" : result.Status.ToString();

    private static string Name(FieldMessageKinds kind) => FieldMessageProperties.All.Single(property => property.Kind == kind).Name;

    private static FormField File(string? settings = null) => Field("file", settings);

    private static FormField Signature() => Field("signature", null);

    private static FormField Field(string type, string? settings) => new()
    {
        Key = Guid.Parse("30000000-0000-4000-8000-000000000087"),
        Type = type,
        Alias = "upload",
        Label = new LocalizedText("Upload"),
        Settings = settings is null ? null : SettingsObject.Parse(settings),
    };

    private static StoredForm Form() => new(
        FormId, FormKey, null, "contact", "Contact", 12, 13, 1, 1, 0, null, false, null, null, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, null);

    private static StoredEntryFile Recorded(NewEntryFile file) => new(
        1, file.Key, null, file.FormId, file.FieldKey, file.OriginalName, file.StoredName, file.ContentType, file.SizeBytes, file.FileHash, file.ScanState, DateTimeOffset.UnixEpoch);

    private FileUploads Uploads(FieldHostLimits? host = null, IEntryRepository? entries = null) =>
        new(TestFieldTypes.Registry, host ?? FieldHostLimits.Default, _store, _forms, entries ?? _entries, _logger);

    private async Task<FileUploadResult> StoreAsync(FormField field, string? name, byte[] bytes, Func<CancellationToken, Task<bool>>? endsAfter = null, IEntryRepository? entries = null)
    {
        var uploads = Uploads(entries: entries);
        using var content = new MemoryStream(bytes);
        return await uploads.StoreAsync(FormKey, uploads.TypeOf(field)!, field, name, content, endsAfter ?? (_ => Task.FromResult(true)), CancellationToken.None);
    }

    // A store that keeps all of a file or nothing, as the contract says, and can read only once or refuse its deletes.
    private sealed class Store : IFormBuilderFileStore
    {
        public Dictionary<string, byte[]> Files { get; } = new(StringComparer.Ordinal);

        public List<string> Deleted { get; } = [];

        public bool ReadsOnce { get; set; }

        public bool FailDeletes { get; set; }

        public async Task SaveAsync(string id, Stream content, CancellationToken cancellationToken = default)
        {
            using var copy = new MemoryStream();
            if (ReadsOnce)
            {
                var buffer = new byte[4];
                await copy.WriteAsync(buffer.AsMemory(0, await content.ReadAsync(buffer, cancellationToken)), cancellationToken);
            }
            else
            {
                await content.CopyToAsync(copy, cancellationToken);
            }

            Files.Add(id, copy.ToArray());
        }

        public Task<Stream?> OpenReadAsync(string id, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(Files.TryGetValue(id, out var bytes) ? new MemoryStream(bytes) : null);

        public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
        {
            if (FailDeletes)
            {
                throw new IOException("The disk refuses.");
            }

            Deleted.Add(id);
            Files.Remove(id);
            return Task.CompletedTask;
        }
    }

    // A request whose connection is reset after its first bytes.
    private sealed class CutStream(byte[] first) : MemoryStream(first)
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await base.ReadAsync(buffer, cancellationToken);
            return read > 0 ? read : throw new IOException("The connection was reset.");
        }
    }

    private sealed class RecordingLogger : ILogger<FileUploads>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception)));
    }
}
