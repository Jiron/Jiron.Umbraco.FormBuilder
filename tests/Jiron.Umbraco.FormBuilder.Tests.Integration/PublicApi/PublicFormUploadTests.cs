using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Web.PublicApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.PublicApi;

/// <summary>
/// The upload endpoint of the public API (ADR-0087) on a real site whose limit for any file is 60 MiB and which blocks
/// its own extensions: a file is stored for its field, recorded without an entry and answered with a token that names
/// its row; a 60 MiB file to a 50 MiB field is cut off at its limit without being buffered, one of exactly 50 MiB is
/// stored, and a declared length past a limit is refused before a byte is read; a renamed program is refused on its
/// first bytes, and every file a field does not take with the field's kind of error; every refusal leaves nothing in
/// the store, the tables or ASP.NET Core's buffer folder, and closes the connection; publishing holds file fields to
/// the site's limits. A middleware of the test site counts the bytes the server reads of each request.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class PublicFormUploadTests : PublicApiTestBase
{
    // The site's limit for any file, above the default so the setting shows, and the limit of the large field.
    private const long SiteLimit = 62_914_560;
    private const long FieldLimit = 52_428_800;
    private const string Boundary = "jfb-upload-boundary";

    private static readonly string Crlf = new([(char)13, (char)10]);
    private static readonly char Backslash = (char)92;

    private static readonly FormField Attachment = Field("attachment", "file", null);
    private static readonly FormField Large = Field("large", "file", """{"types":["pdf"],"maxBytes":52428800}""");
    private static readonly FormField Small = Field("small", "file", """{"types":["pdf","text"],"maxBytes":1048576}""");
    private static readonly FormField Drawing = Field("drawing", "file", """{"extensions":["svg"]}""");
    private static readonly FormField Archive = Field("archive", "file", """{"types":["archives"]}""");
    private static readonly FormField Sign = Field("sign", "signature", null);
    private static readonly FormField Ticket = Field("ticket", "file", """{"types":["pdf"]}""");
    private static readonly FormField Guests = Field("guests", "repeater", null) with { Fields = [Ticket] };
    private static readonly FormField[] All = [NameField, Attachment, Large, Small, Drawing, Archive, Sign, Guests];

    private static readonly byte[] Pdf = [.. "%PDF-1.7"u8, 0x0A, .. "1 0 obj"u8];
    private static readonly byte[] Program = [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00];
    private static readonly byte[] Zip = [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00];
    private static readonly byte[] Rar = [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x01, 0x00];

    // A 1-by-1 PNG in true colour with alpha, with the CRC zlib computes for its header.
    private static readonly byte[] OnePixel = Convert.FromHexString("89504e470d0a1a0a0000000d49484452000000010000000108060000001f15c489");

    private string _storeRoot = null!;
    private long _sent;

    private RequestBodies Bodies => GetRequiredService<RequestBodies>();

    [TearDown]
    public void DeleteTheStore()
    {
        if (Directory.Exists(_storeRoot))
        {
            Directory.Delete(_storeRoot, recursive: true);
        }
    }

    [Test]
    public async Task AFile_IsStoredForItsField_AndAnsweredWithAToken_ThatNamesItsRowButNotItsBytes()
    {
        var key = await PublishAsync(fields: All);
        var bytes = Bytes(Pdf, 100_000);

        using var response = await UploadAsync(key, Attachment.Key, $"C:{Backslash}fakepath{Backslash}Report 2026.pdf", bytes);
        var read = Bodies.Read;
        var answer = await JsonAsync(response);
        var token = answer["token"]!.GetValue<string>();
        var named = GetRequiredService<UploadTokens>().Read(token, key);
        var row = (await GetRequiredService<IEntryRepository>().GetFileAsync(named!.FileKey))!;
        var form = (await GetRequiredService<IFormRepository>().GetAsync(key))!;
        var stored = await StoredBytesAsync(row.StoredName);

        Assert.Multiple(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
            Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/json"));
            Assert.That(Header(response, "Cache-Control"), Is.EqualTo("no-store"));
            Assert.That(response.Headers.ConnectionClose, Is.Not.True);
            Assert.That(answer.AsObject().Select(property => property.Key), Is.EqualTo(new[] { "token", "name", "size", "contentType" }));
            Assert.That(answer["name"]!.GetValue<string>(), Is.EqualTo("Report 2026.pdf"), "the last segment of the device's path");
            Assert.That(answer["size"]!.GetValue<long>(), Is.EqualTo(bytes.Length));
            Assert.That(answer["contentType"]!.GetValue<string>(), Is.EqualTo("application/pdf"));
            Assert.That(named.FieldKey, Is.EqualTo(Attachment.Key));
            Assert.That((row.EntryId, row.FormId, row.FieldKey), Is.EqualTo(((long?)null, form.Id, Attachment.Key)), "a row that waits for its entry");
            Assert.That((row.OriginalName, row.ContentType, row.SizeBytes), Is.EqualTo(("Report 2026.pdf", "application/pdf", (long)bytes.Length)));
            Assert.That(row.FileHash, Is.EqualTo(Convert.ToHexStringLower(SHA256.HashData(bytes))));
            Assert.That(row.ScanState, Is.EqualTo("unscanned"));
            Assert.That(FormBuilderFileId.IsValid(row.StoredName), Is.True);
            Assert.That(StoredFiles(), Is.EqualTo(new[] { Path.Combine(_storeRoot, row.StoredName[..2], row.StoredName) }), "under its random id in its folder, without the name's extension");
            Assert.That(stored, Is.EqualTo(bytes));
            Assert.That(token, Does.Not.Contain(row.StoredName).And.Not.Contain(row.Key.ToString("N")));
            Assert.That(read, Is.EqualTo(_sent), "the request read to its end");
        });
    }

    [Test]
    public async Task AChallengeFirst_ARepeatableGroupsField_AndASignature_AreTaken()
    {
        var key = await PublishAsync(fields: All);

        using var ticket = await UploadAsync(key, Ticket.Key, "ticket.pdf", Pdf, challenge: "a challenge 7.1 verifies");
        using var signature = await UploadAsync(key, Sign.Key, "signature.png", OnePixel);
        using var archive = await UploadAsync(key, Archive.Key, "backup.zip", Zip);
        var tokens = GetRequiredService<UploadTokens>();

        Assert.Multiple(async () =>
        {
            Assert.That(ticket.StatusCode, Is.EqualTo(HttpStatusCode.OK), await ticket.Content.ReadAsStringAsync());
            Assert.That(tokens.Read((await JsonAsync(ticket))["token"]!.GetValue<string>(), key)?.FieldKey, Is.EqualTo(Ticket.Key));
            Assert.That(signature.StatusCode, Is.EqualTo(HttpStatusCode.OK), await signature.Content.ReadAsStringAsync());
            Assert.That((await JsonAsync(signature))["contentType"]!.GetValue<string>(), Is.EqualTo("image/png"));
            Assert.That(archive.StatusCode, Is.EqualTo(HttpStatusCode.OK), await archive.Content.ReadAsStringAsync());
            Assert.That(tokens.Read((await JsonAsync(signature))["token"]!.GetValue<string>(), Guid.NewGuid()), Is.Null, "a token counts for its own form");
        });
    }

    [Test]
    public async Task AnUploadToAFormThatTakesNoEntry_IsRefusedWithTheSubmissionsCode_BeforeAByteIsRead()
    {
        var now = DateTime.UtcNow;
        var closed = await PublishAsync("Closed", change: definition => definition with
        {
            Settings = definition.Settings with { Availability = definition.Settings.Availability with { ClosesAt = new DateTime(now.Ticks - (now.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc).AddMinutes(-1) } },
        }, fields: All);
        var full = await PublishAsync("Full", change: definition => definition with
        {
            Settings = definition.Settings with { Availability = definition.Settings.Availability with { MaxEntries = 1 } },
        }, fields: All);
        using (var entry = await SendAsync(HttpMethod.Post, $"{Root}/forms/{full}/entries", request => request.Content = new StringContent("""{"revision":1,"values":{"name":"Ada"}}""", Encoding.UTF8, "application/json")))
        {
            Assert.That(entry.StatusCode, Is.EqualTo(HttpStatusCode.OK), await entry.Content.ReadAsStringAsync());
        }

        var answers = new Dictionary<string, string>();
        var read = new Dictionary<string, long>();
        foreach (var (what, form) in new[] { ("a closed form", closed), ("a full form", full) })
        {
            using var content = new StreamedUpload("report.pdf", Pdf, 1_048_576, declared: true);
            using var response = await SendAsync(HttpMethod.Post, UploadPath(form, Attachment.Key), request => request.Content = content);
            answers[what] = await AnswerAsync(response);
            read[what] = Bodies.Read;
        }

        Assert.Multiple(async () =>
        {
            Assert.That(answers, Is.EqualTo(new Dictionary<string, string>
            {
                ["a closed form"] = "403 form_closed, close",
                ["a full form"] = "403 entry_limit_reached, close",
            }), string.Join(" | ", answers));
            Assert.That(read.Values, Is.All.Zero, "refused before a byte of the file is read");
            Assert.That(StoredFiles(), Is.Empty);
            Assert.That(await OrphansAsync(), Is.Empty);
        });
    }

    [Test]
    public async Task A60MiBFileToA50MiBField_IsCutOffAtItsLimit_WithoutBuffering_AndLeavesNothingInTheStoreOrTheTables()
    {
        var key = await PublishAsync(fields: All);
        using var watch = new BufferWatch(IntegrationSetup.BufferFolder);
        var watched = await watch.ControlAsync();

        using var content = new StreamedUpload("big.pdf", Pdf, 62_914_560, declared: false);
        var allocated = GC.GetTotalAllocatedBytes(precise: true);
        using var response = await SendAsync(HttpMethod.Post, UploadPath(key, Large.Key), request => request.Content = content);
        allocated = GC.GetTotalAllocatedBytes(precise: true) - allocated;
        var read = Bodies.Read;
        var written = content.Written;
        var buffered = await watch.SeenAsync();
        await TestContext.Out.WriteLineAsync($"read {read:N0} bytes, {allocated:N0} allocated in the process, {written:N0} of the file written by the answer");

        Assert.Multiple(async () =>
        {
            Assert.That(watched, Is.True, "the watch sees what ASP.NET Core buffers to the folder");
            Assert.That(await AnswerAsync(response), Is.EqualTo("413 payload_too_large fileSize at file, close"));
            Assert.That(read, Is.InRange(FieldLimit, FieldLimit + FileUploads.OverheadBytes), "the server stopped reading at the field's limit");
            Assert.That(written, Is.LessThan(62_914_560), "the transfer was cut off before the client had sent the whole file");
            Assert.That(allocated, Is.LessThan(read / 4), "streamed: buffering in memory would allocate at least what it read");
            Assert.That(buffered, Is.Empty, "nothing buffered to disk");
            Assert.That(StoredFiles(), Is.Empty, "no file, and no partial one");
            Assert.That(await OrphansAsync(), Is.Empty);
        });
    }

    [Test]
    public async Task AFileOfExactlyItsFieldsLimit_IsStored_WithoutBuffering()
    {
        var key = await PublishAsync(fields: All);
        using var watch = new BufferWatch(IntegrationSetup.BufferFolder);

        using var content = new StreamedUpload("exact.pdf", Pdf, FieldLimit, declared: true);
        using var response = await SendAsync(HttpMethod.Post, UploadPath(key, Large.Key), request => request.Content = content);
        var buffered = await watch.SeenAsync();
        var body = await response.Content.ReadAsStringAsync();

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), body);
            Assert.That(JsonNode.Parse(body)?["size"]?.GetValue<long>(), Is.EqualTo(FieldLimit));
            Assert.That(StoredFiles().Select(file => new FileInfo(file).Length), Is.EqualTo(new[] { FieldLimit }));
            Assert.That(buffered, Is.Empty);
        });
    }

    [Test]
    public async Task ADeclaredLengthPastTheLimit_IsRefusedBeforeAByteIsRead()
    {
        var key = await PublishAsync(fields: All);

        using var toSmall = new StreamedUpload("report.pdf", Pdf, 1_048_576 + FileUploads.OverheadBytes, declared: true);
        using var small = await SendAsync(HttpMethod.Post, UploadPath(key, Small.Key), request => request.Content = toSmall);
        var smallRead = Bodies.Read;
        using var toLarge = new StreamedUpload("report.pdf", Pdf, FieldLimit + FileUploads.OverheadBytes, declared: true);
        using var large = await SendAsync(HttpMethod.Post, UploadPath(key, Large.Key), request => request.Content = toLarge);
        var largeRead = Bodies.Read;
        using var toAny = new StreamedUpload("report.pdf", Pdf, SiteLimit + FileUploads.OverheadBytes, declared: true);
        using var any = await SendAsync(HttpMethod.Post, UploadPath(key, Attachment.Key), request => request.Content = toAny);
        var anyRead = Bodies.Read;

        Assert.Multiple(async () =>
        {
            Assert.That(await AnswerAsync(small), Is.EqualTo("413 payload_too_large fileSize at file, close"), "past the field's limit");
            Assert.That(smallRead, Is.Zero);
            Assert.That(await AnswerAsync(large), Is.EqualTo("413 payload_too_large fileSize at file, close"), "past a 50 MiB field's limit");
            Assert.That(largeRead, Is.Zero);
            Assert.That(await AnswerAsync(any), Is.EqualTo("413 payload_too_large, close"), "past the largest upload the site takes, refused by the frame");
            Assert.That(anyRead, Is.Zero);
            Assert.That(StoredFiles(), Is.Empty);
        });
    }

    [Test]
    public async Task AFileItsFieldDoesNotTake_IsRefusedOnItsFirstBytes_WithTheFieldsKindOfError()
    {
        var key = await PublishAsync(fields: All);
        using var watch = new BufferWatch(IntegrationSetup.BufferFolder);
        var answers = new Dictionary<string, string>();
        var reads = new Dictionary<string, long>();
        foreach (var declared in new[] { true, false })
        {
            var what = declared ? "a renamed 20 MiB program with its length" : "a renamed 20 MiB program, chunked";
            using var program = new StreamedUpload("setup.pdf", Program, 20_971_520, declared);
            using var response = await SendAsync(HttpMethod.Post, UploadPath(key, Attachment.Key), request => request.Content = program);
            answers[what] = await AnswerAsync(response);
            reads[what] = Bodies.Read;
        }

        foreach (var (what, field, name, bytes) in new (string, FormField, string, byte[])[]
        {
            ("a renamed program", Attachment, "invoice.pdf", Bytes(Program, 2_000_000)),
            ("a PDF named as an image", Attachment, "photo.png", Pdf),
            ("an extension the field does not take", Attachment, "notes.txt", "Notes"u8.ToArray()),
            ("an extension the site blocks", Small, "page.html", "<p>Hello</p>"u8.ToArray()),
            ("a type group's extension the site blocks", Archive, "backup.rar", Rar),
            ("a script by its first bytes", Small, "notes.txt", "#!/bin/sh"u8.ToArray()),
            ("a program named as a drawing", Drawing, "drawing.svg", Program),
            ("a signature that is no PNG", Sign, "signature.png", Pdf),
            ("a signature past its limit", Sign, "signature.png", Bytes(OnePixel, 1_048_577)),
        })
        {
            using var response = await UploadAsync(key, field.Key, name, bytes);
            answers[what] = await AnswerAsync(response);
            reads[what] = Bodies.Read;
        }

        var buffered = await watch.SeenAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(answers, Is.EqualTo(new Dictionary<string, string>
            {
                ["a renamed 20 MiB program with its length"] = "415 unsupported_media_type fileType at file, close",
                ["a renamed 20 MiB program, chunked"] = "415 unsupported_media_type fileType at file, close",
                ["a renamed program"] = "415 unsupported_media_type fileType at file, close",
                ["a PDF named as an image"] = "415 unsupported_media_type fileType at file, close",
                ["an extension the field does not take"] = "415 unsupported_media_type fileType at file, close",
                ["an extension the site blocks"] = "415 unsupported_media_type fileType at file, close",
                ["a type group's extension the site blocks"] = "415 unsupported_media_type fileType at file, close",
                ["a script by its first bytes"] = "415 unsupported_media_type fileType at file, close",
                ["a program named as a drawing"] = "415 unsupported_media_type fileType at file, close",
                ["a signature that is no PNG"] = "415 unsupported_media_type invalid at file, close",
                ["a signature past its limit"] = "413 payload_too_large invalid at file, close",
            }));
            Assert.That(reads.Where(read => read.Key.Contains("program", StringComparison.Ordinal)).Select(read => read.Value), Is.All.LessThan(65_536), "refused on its first bytes, not after its megabytes");
            Assert.That(buffered, Is.Empty);
            Assert.That(StoredFiles(), Is.Empty);
            Assert.That(await OrphansAsync(), Is.Empty);
        });
    }

    [Test]
    public async Task WhatItCannotTake_IsRefused_ClosingTheConnection_AndStoringNothing()
    {
        var key = await PublishAsync(fields: All);
        var unpublished = (await Forms.CreateAsync(new NewFormRequest("Draft only", UserKey: Editor))).Key;
        var trashed = await PublishAsync("Trashed", fields: [Attachment]);
        await Forms.TrashAsync(trashed, (await Forms.GetAsync(trashed))!.ConcurrencyVersion, Editor);
        var file = FilePart("report.pdf", Pdf);
        var challenge = ("Content-Disposition: form-data; name=challenge", "challenge"u8.ToArray());

        var answers = new Dictionary<string, string>();
        foreach (var (what, path, body) in new (string, string, Func<HttpContent>)[]
        {
            ("a key of no form", UploadPath(Guid.NewGuid(), Attachment.Key), () => Multipart(file)),
            ("a key without hyphens", $"{Root}/forms/{key:N}/uploads?fieldKey={Attachment.Key}", () => Multipart(file)),
            ("an unpublished form", UploadPath(unpublished, Attachment.Key), () => Multipart(file)),
            ("a form in the trash", UploadPath(trashed, Attachment.Key), () => Multipart(file)),
            ("no field key", $"{Root}/forms/{key}/uploads", () => Multipart(file)),
            ("a field key that is no key", $"{Root}/forms/{key}/uploads?fieldKey=attachment", () => Multipart(file)),
            ("a key of no field", UploadPath(key, Guid.NewGuid()), () => Multipart(file)),
            ("a field that takes no files", UploadPath(key, NameField.Key), () => Multipart(file)),
            ("a repeatable group", UploadPath(key, Guests.Key), () => Multipart(file)),
            ("a body that is no multipart", UploadPath(key, Attachment.Key), () => new StringContent("""{"file":"x"}""", Encoding.UTF8, "application/json")),
            ("no boundary", UploadPath(key, Attachment.Key), () => Multipart([file], contentType: "multipart/form-data")),
            ("a boundary too long", UploadPath(key, Attachment.Key), () => Multipart([file], contentType: "multipart/form-data; boundary=" + new string('b', 71))),
            ("no file", UploadPath(key, Attachment.Key), () => Multipart(challenge)),
            ("a file part without a file name", UploadPath(key, Attachment.Key), () => Multipart(("Content-Disposition: form-data; name=file", Pdf))),
            ("a file name that is only points", UploadPath(key, Attachment.Key), () => Multipart(FilePart("...", Pdf))),
            ("two challenges", UploadPath(key, Attachment.Key), () => Multipart(challenge, challenge, file)),
            ("a challenge too long", UploadPath(key, Attachment.Key), () => Multipart(("Content-Disposition: form-data; name=challenge", new byte[4097]), file)),
            ("a challenge sent as a file", UploadPath(key, Attachment.Key), () => Multipart(("Content-Disposition: form-data; name=challenge; filename=c.txt", "c"u8.ToArray()), file)),
            ("a part before the file", UploadPath(key, Attachment.Key), () => Multipart(("Content-Disposition: form-data; name=notes", "n"u8.ToArray()), file)),
            ("a part that is no form data", UploadPath(key, Attachment.Key), () => Multipart(("Content-Disposition: attachment; name=file; filename=report.pdf", Pdf))),
            ("a part with too many headers", UploadPath(key, Attachment.Key), () => Multipart((string.Join(Crlf, Enumerable.Range(1, 8).Select(i => $"X-Part-{i}: {i}").Append(file.Headers)), Pdf))),
            ("a part after the file", UploadPath(key, Attachment.Key), () => Multipart(file, challenge)),
            ("a body cut before its end", UploadPath(key, Attachment.Key), () => Multipart([file], close: false)),
        })
        {
            using var content = body();
            using var response = await SendAsync(HttpMethod.Post, path, request => request.Content = content);
            answers[what] = $"{await AnswerAsync(response)}, {Header(response, "Cache-Control")}";
        }

        Assert.Multiple(async () =>
        {
            Assert.That(answers, Is.EqualTo(new Dictionary<string, string>
            {
                ["a key of no form"] = "404 form_not_found, close, no-store",
                ["a key without hyphens"] = "404 form_not_found, close, no-store",
                ["an unpublished form"] = "404 form_not_found, close, no-store",
                ["a form in the trash"] = "404 form_not_found, close, no-store",
                ["no field key"] = "400 invalid_request invalid_value at fieldKey, close, no-store",
                ["a field key that is no key"] = "400 invalid_request invalid_value at fieldKey, close, no-store",
                ["a key of no field"] = "404 not_found, close, no-store",
                ["a field that takes no files"] = "404 not_found, close, no-store",
                ["a repeatable group"] = "404 not_found, close, no-store",
                ["a body that is no multipart"] = "415 unsupported_media_type, close, no-store",
                ["no boundary"] = "400 invalid_request invalid_value at Content-Type, close, no-store",
                ["a boundary too long"] = "400 invalid_request invalid_value at Content-Type, close, no-store",
                ["no file"] = "400 invalid_request invalid_value at file, close, no-store",
                ["a file part without a file name"] = "400 invalid_request invalid_value at file, close, no-store",
                ["a file name that is only points"] = "400 invalid_request invalid_value at file, close, no-store",
                ["two challenges"] = "400 invalid_request invalid_value at file, close, no-store",
                ["a challenge too long"] = "400 invalid_request invalid_value at challenge, close, no-store",
                ["a challenge sent as a file"] = "400 invalid_request invalid_value at file, close, no-store",
                ["a part before the file"] = "400 invalid_request invalid_value at file, close, no-store",
                ["a part that is no form data"] = "400 invalid_request invalid_value at file, close, no-store",
                ["a part with too many headers"] = "400 invalid_request invalid_value at file, close, no-store",
                ["a part after the file"] = "400 invalid_request invalid_value at file, close, no-store",
                ["a body cut before its end"] = "400 invalid_request invalid_value at file, close, no-store",
            }));
            Assert.That(StoredFiles(), Is.Empty, "a file followed by another part is deleted again");
            Assert.That(await OrphansAsync(), Is.Empty);
        });
    }

    [Test]
    public async Task Publishing_HoldsAFileField_ToTheSitesLimits()
    {
        var key = (await Forms.CreateAsync(new NewFormRequest("Limits", UserKey: Editor))).Key;
        async Task<string> PublishWithAsync(string settings)
        {
            try
            {
                await SaveAndPublishAsync(key, definition => WithFields(definition, Field("upload", "file", settings)));
                return "published";
            }
            catch (FormBuilderValidationException refused)
            {
                return string.Join(", ", refused.Problems.Select(problem => $"{problem.Code} at {problem.Path}"));
            }
        }

        var above = await PublishWithAsync("""{"maxBytes":62914561}""");
        var blocked = await PublishWithAsync("""{"extensions":["svg","dwg"]}""");
        var within = await PublishWithAsync("""{"extensions":["svg"],"maxBytes":62914560}""");
        var pastTheDefault = await PublishWithAsync("""{"maxBytes":52428801}""");

        Assert.Multiple(() =>
        {
            Assert.That(above, Is.EqualTo($"{DefinitionErrors.SettingInvalid} at $.pages[0].groups[0].fields[0].settings.maxBytes"));
            Assert.That(blocked, Is.EqualTo($"{DefinitionErrors.SettingInvalid} at $.pages[0].groups[0].fields[0].settings.extensions[1]"));
            Assert.That(within, Is.EqualTo("published"), "the site's own list replaces the defaults, so it takes svg");
            Assert.That(pastTheDefault, Is.EqualTo("published"), "the site's own limit, not the default's");
        });
    }

    /// <inheritdoc />
    protected override void ConfigureHost(IDictionary<string, string> configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _storeRoot = Path.Combine(Path.GetTempPath(), $"jfb-uploads-{Guid.NewGuid():N}");
        configuration[FormBuilderFilesOptions.RootKey] = _storeRoot;
        configuration[FormBuilderFilesOptions.SectionName + ":MaxFileBytes"] = SiteLimit.ToString(System.Globalization.CultureInfo.InvariantCulture);
        string[] blocked = ["exe", "html", "dwg", "rar"];
        for (var i = 0; i < blocked.Length; i++)
        {
            configuration[$"{FormBuilderFilesOptions.SectionName}:BlockedExtensions:{i}"] = blocked[i];
        }
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.Services.AddSingleton<RequestBodies>();
        builder.Services.AddSingleton<IStartupFilter, CountRequestBodies>();
    }

    private static FormField Field(string alias, string type, string? settings) => new()
    {
        Key = Guid.NewGuid(),
        Type = type,
        Alias = alias,
        Label = new LocalizedText(alias),
        Settings = settings is null ? null : SettingsObject.Parse(settings),
    };

    // The bytes followed by zeros up to the length.
    private static byte[] Bytes(byte[] head, int length)
    {
        var bytes = new byte[length];
        head.CopyTo(bytes, 0);
        return bytes;
    }

    private static string UploadPath(Guid form, Guid field) => $"{Root}/forms/{form}/uploads?fieldKey={field}";

    private static (string Headers, byte[] Bytes) FilePart(string name, byte[] bytes) => ($"Content-Disposition: form-data; name=file; filename={name}", bytes);

    private static ByteArrayContent Multipart(params (string Headers, byte[] Bytes)[] parts) => Multipart(parts, close: true);

    // A multipart body of these parts as written, closed by its boundary unless told otherwise.
    private static ByteArrayContent Multipart((string Headers, byte[] Bytes)[] parts, bool close = true, string? contentType = null)
    {
        using var body = new MemoryStream();
        foreach (var (headers, bytes) in parts)
        {
            body.Write(Encoding.ASCII.GetBytes($"--{Boundary}{Crlf}{headers}{Crlf}{Crlf}"));
            body.Write(bytes);
            body.Write(Encoding.ASCII.GetBytes(Crlf));
        }

        if (close)
        {
            body.Write(Encoding.ASCII.GetBytes($"--{Boundary}--{Crlf}"));
        }

        var content = new ByteArrayContent(body.ToArray());
        content.Headers.TryAddWithoutValidation("Content-Type", contentType ?? $"multipart/form-data; boundary={Boundary}");
        return content;
    }

    // The status, the code, the errors and whether the connection closes, such as "413 payload_too_large fileSize at file, close".
    private static async Task<string> AnswerAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        var problem = body.Length > 0 ? JsonNode.Parse(body) as JsonObject : null;
        var code = problem?["code"]?.GetValue<string>() ?? "(no code)";
        var errors = problem?["errors"] is JsonArray list ? " " + string.Join("; ", list.Select(error => $"{error!["code"]} at {error["path"]}")) : string.Empty;
        var close = response.Headers.ConnectionClose == true ? ", close" : string.Empty;
        return $"{(int)response.StatusCode} {code}{errors}{close}";
    }

    // Uploads the bytes as a browser does: the challenge first when there is one, then the file with its name.
    private async Task<HttpResponseMessage> UploadAsync(Guid form, Guid field, string name, byte[] bytes, string? challenge = null)
    {
        using var content = new MultipartFormDataContent(Boundary);
        if (challenge is not null)
        {
            content.Add(new StringContent(challenge), "challenge");
        }

        content.Add(new ByteArrayContent(bytes), "file", name);
        _sent = content.Headers.ContentLength ?? -1;
        return await SendAsync(HttpMethod.Post, UploadPath(form, field), request => request.Content = content);
    }

    private async Task<byte[]> StoredBytesAsync(string id)
    {
        await using var stored = (await GetRequiredService<IFormBuilderFileStore>().OpenReadAsync(id))!;
        using var copy = new MemoryStream();
        await stored.CopyToAsync(copy);
        return copy.ToArray();
    }

    // Every file under the store's folder, partial ones among them.
    private List<string> StoredFiles() =>
        Directory.Exists(_storeRoot) ? [.. Directory.EnumerateFiles(_storeRoot, "*", SearchOption.AllDirectories)] : [];

    private async Task<IReadOnlyList<StoredEntryFile>> OrphansAsync() =>
        await GetRequiredService<IEntryRepository>().ListOrphanFilesAsync(DateTimeOffset.UtcNow.AddDays(1), 100);

    // An upload of one file of the given size, its body written in chunks as it is sent: chunked, or with its length.
    private sealed class StreamedUpload : HttpContent
    {
        private readonly byte[] _start;
        private readonly byte[] _head;
        private readonly long _size;
        private readonly byte[] _end = Encoding.ASCII.GetBytes($"{Crlf}--{Boundary}--{Crlf}");
        private readonly bool _declared;
        private long _written;

        public StreamedUpload(string name, byte[] head, long size, bool declared)
        {
            _start = Encoding.ASCII.GetBytes($"--{Boundary}{Crlf}Content-Disposition: form-data; name=file; filename={name}{Crlf}{Crlf}");
            _head = head;
            _size = size;
            _declared = declared;
            Headers.ContentType = MediaTypeHeaderValue.Parse($"multipart/form-data; boundary={Boundary}");
        }

        // How many bytes of the file it has written.
        public long Written => Interlocked.Read(ref _written);

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            await stream.WriteAsync(_start);
            await stream.WriteAsync(_head);
            var chunk = new byte[65_536];
            for (var written = (long)_head.Length; written < _size;)
            {
                var length = (int)Math.Min(chunk.Length, _size - written);
                await stream.WriteAsync(chunk.AsMemory(0, length));
                written += length;
                Interlocked.Exchange(ref _written, written);
            }

            await stream.WriteAsync(_end);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _start.Length + _size + _end.Length;
            return _declared;
        }
    }

    // Watches ASP.NET Core's buffer folder for files created in it.
    private sealed class BufferWatch : IDisposable
    {
        private readonly string _folder;
        private readonly FileSystemWatcher _watcher;
        private readonly ConcurrentQueue<string> _created = new();

        public BufferWatch(string folder)
        {
            _folder = folder;
            _watcher = new FileSystemWatcher(folder) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName, InternalBufferSize = 65_536 };
            _watcher.Created += (_, created) => _created.Enqueue(created.Name ?? created.FullPath);
            _watcher.EnableRaisingEvents = true;
        }

        // Buffers a body through ASP.NET Core's own buffering, which writes it to the folder; true once the watch saw it.
        public async Task<bool> ControlAsync()
        {
            using var inner = new MemoryStream(new byte[1_048_576]);
            await using (var buffering = new FileBufferingReadStream(inner, memoryThreshold: 1024))
            {
                await buffering.DrainAsync(CancellationToken.None);
            }

            return (await SeenAsync()).Any(name => name.StartsWith("ASPNETCORE_", StringComparison.Ordinal));
        }

        // What the folder got since the last look: a marker goes in last, and the watch reports what came before it first.
        public async Task<List<string>> SeenAsync()
        {
            var marker = $"jfb-marker-{Guid.NewGuid():N}";
            var path = Path.Combine(_folder, marker);
            await File.WriteAllBytesAsync(path, []);
            var seen = new List<string>();
            var waited = Stopwatch.StartNew();
            while (waited.Elapsed < TimeSpan.FromSeconds(10))
            {
                while (_created.TryDequeue(out var name))
                {
                    if (name == marker)
                    {
                        File.Delete(path);
                        return seen;
                    }

                    seen.Add(name);
                }

                await Task.Delay(20);
            }

            throw new TimeoutException("The watch did not see its marker within 10 seconds.");
        }

        public void Dispose() => _watcher.Dispose();
    }
}

/// <summary>How many bytes the test site's server read of the request bodies since the last request began.</summary>
internal sealed class RequestBodies
{
    private long _read;

    /// <summary>The bytes read of the last request's body.</summary>
    public long Read => Interlocked.Read(ref _read);

    /// <summary>Starts counting a request.</summary>
    public void Start() => Interlocked.Exchange(ref _read, 0);

    /// <summary>Counts bytes read.</summary>
    public void Add(int read) => Interlocked.Add(ref _read, read);
}

/// <summary>Puts a counting stream around every request's body, before anything of the site sees it.</summary>
/// <param name="bodies">The count.</param>
internal sealed class CountRequestBodies(RequestBodies bodies) : IStartupFilter
{
    /// <inheritdoc />
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, following) =>
        {
            bodies.Start();
            context.Request.Body = new CountingStream(context.Request.Body, bodies);
            await following(context);
        });
        next(app);
    };

    private sealed class CountingStream(Stream inner, RequestBodies bodies) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken);
            bodies.Add(read);
            return read;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException("The site reads bodies asynchronously.");

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
