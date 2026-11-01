using System.Net;
using System.Net.Http.Headers;
using System.Text;
using AngleSharp.Dom;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Jiron.Umbraco.FormBuilder.Web.Rendering;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Rendering;

// Files in a post without JavaScript (ROADMAP 6.2j, ADR-0006, ADR-0102): the form names itself in its action, its files
// are read before routing up to their fields' limits and held to the upload's rules; a file that passed is kept as a
// ticked box when another field fails and is sent with the entry, one that is too large or of a kind the field does
// not take is an error at its field with every other answer kept, a post past the drain allowance shows a notice, and
// a form that takes no entry now stores none of the post's files.
public sealed partial class FormPostTests
{
    private const string FilesPage = "/post-files/";

    // The form's limit: what it allows for its other answers, the attachment's two files and a CV in each of two rows.
    private const long FormLimit = FormPostFilesMiddleware.OtherBytes + (2048 * 2) + (131_072 * 2);

    private static readonly byte[] Pdf = [.. "%PDF-1.7"u8, 0x0A, .. "1 0 obj"u8];
    private static readonly byte[] Program = [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00];

    private static readonly FormField Topic2 = Field(K(0x80), "subject", "text") with { Required = true };
    private static readonly FormField Remark = Field(K(0x81), "remark", "text");
    private static readonly FormField Attachment = Field(K(0x82), "attachment", "file", """{"types":["pdf"],"maxBytes":2048,"maxFiles":2}""");
    private static readonly FormField Cv = Field(K(0x84), "cv", "file", """{"types":["pdf"],"maxBytes":131072}""");
    private static readonly FormField Crew = Field(K(0x83), "crew", "repeater", """{"minRows":1,"maxRows":2}""") with { Fields = [Cv] };

    [Test]
    public async Task AFileWhileAnotherFieldFails_IsKeptAsATickedBox_AndSentWithTheEntry()
    {
        var key = await PublishFormAsync("postFiles", FilesForm);
        _ = await PagesAsync("post-files");
        var first = FormOf(await DocumentAsync(FilesPage), "jfb-1");
        Fill(first, "remark", "Please print it");

        var kept = await FilesAsync(first, FormPostFields.Submit, ("attachment", "plan.pdf", Bytes(Pdf, 1536)));
        var both = await FilesAsync(first, FormPostFields.Submit, ("attachment", "plan.pdf", Bytes(Pdf, 2049)));
        var doubled = (IElement)kept.Clone();
        var box = doubled.QuerySelector("input[type='checkbox'][name='attachment']")!;
        box.Parent!.AppendChild(box.Clone());
        var twice = await FilesAsync(doubled, "add:crew");
        Fill(kept, "subject", "Room plan");
        using var sent = await SubmitFilesAsync(kept, FormPostFields.Submit);
        var replayed = await FilesAsync(kept, "add:crew");
        var catalogue = await CatalogueAsync(key);
        var stored = (await StoredAsync(key)).Single();
        var entryId = (await Entries.ListIdsAsync((await FormRows.GetAsync(key))!.Id, 10)).Single();
        var files = await Entries.ListFilesAsync(entryId);

        Assert.Multiple(() =>
        {
            Assert.That(first.GetAttribute("action"), Is.EqualTo($"{FilesPage}?jfb-post={key:D}.jfb-1#jfb-1"), "the action names the form and its instance");
            Assert.That(first.GetAttribute("enctype"), Is.EqualTo("multipart/form-data"));
            Assert.That(kept.QuerySelectorAll(".jfb-error-summary a").Select(link => link.GetAttribute("href")), Is.EqualTo(new[] { "#jfb-1-subject" }), "the other field's error alone");
            Assert.That(Kept(kept, "attachment"), Is.EqualTo(new[] { "Keep plan.pdf (1.5 KB)" }), "the file kept as a ticked box with its name and size");
            Assert.That(kept.QuerySelector("input[type='checkbox'][name='attachment']")!.HasAttribute("checked"), Is.True);
            Assert.That(Kept(twice, "attachment"), Is.EqualTo(new[] { "Keep plan.pdf (1.5 KB)" }), "a token posted twice is one file");
            Assert.That(Value(kept, "remark"), Is.EqualTo("Please print it"));
            Assert.That(kept.QuerySelector("input[type='file'][name='attachment']"), Is.Not.Null, "another file can still be chosen");
            Assert.That(both.QuerySelectorAll(".jfb-error-summary a").Select(link => link.GetAttribute("href")), Is.EquivalentTo(new[] { "#jfb-1-subject", "#jfb-1-attachment" }), "a file's error beside the page's other errors");
            Assert.That(sent.StatusCode, Is.EqualTo(HttpStatusCode.Redirect), "sent with the kept file");
            Assert.That(files.Select(file => (file.OriginalName, file.SizeBytes)), Is.EqualTo(new[] { ("plan.pdf", 1536L) }), "the entry holds the file");
            Assert.That(stored.Any(value => value.Field == catalogue[Remark.Key]), Is.True);
            Assert.That(Kept(replayed, "attachment"), Is.Empty, "a token whose file an entry holds is not offered again");
        });
    }

    [Test]
    public async Task AFileTooLargeOrOfAKindItsFieldDoesNotTake_IsAnErrorAtItsField_EveryOtherAnswerKept_AndNothingIsSent()
    {
        var key = await PublishFormAsync("postFiles", FilesForm);
        _ = await PagesAsync("post-files");
        var first = FormOf(await DocumentAsync(FilesPage), "jfb-1");
        Fill(first, "subject", "Room plan");
        Fill(first, "remark", "Please print it");

        var large = await FilesAsync(first, FormPostFields.Submit, ("attachment", "plan.pdf", Bytes(Pdf, 2049)));
        var drained = await FilesAsync(first, FormPostFields.Submit, ("attachment", "plan.pdf", Bytes(Pdf, 1_500_000)));
        var program = await FilesAsync(first, FormPostFields.Submit, ("attachment", "setup.pdf", Bytes(Program, 600)));
        var three = await FilesAsync(first, FormPostFields.Submit, ("attachment", "a.pdf", Bytes(Pdf, 100)), ("attachment", "b.pdf", Bytes(Pdf, 100)), ("attachment", "c.pdf", Bytes(Pdf, 100)));
        var button = await FilesAsync(first, "add:crew", ("attachment", "plan.pdf", Bytes(Pdf, 2049)));
        var inRow = await FilesAsync(first, FormPostFields.Submit, ("crew[0].cv", "cv.pdf", Bytes(Pdf, 131_073)));
        var exact = await FilesAsync(first, "add:crew", ("attachment", "plan.pdf", Bytes(Pdf, 2048)));
        var empty = await FilesAsync(first, "add:crew", ("attachment", string.Empty, []));

        Assert.Multiple(async () =>
        {
            Assert.That(1_500_000, Is.GreaterThan(FormLimit).And.LessThan(FormLimit * 2), "past the form's limit, within the drain allowance");
            foreach (var (name, form) in new[] { ("too large", large), ("past the form's limit, drained", drained), ("a program", program), ("one too many", three), ("too large, with a row's button", button) })
            {
                Assert.That(form.QuerySelectorAll(".jfb-error-summary a").Select(link => link.GetAttribute("href")), Is.EqualTo(new[] { "#jfb-1-attachment" }), name);
                Assert.That(form.QuerySelector("#jfb-1-attachment-error"), Is.Not.Null, $"{name}: at its field");
                Assert.That((Value(form, "subject"), Value(form, "remark")), Is.EqualTo(("Room plan", "Please print it")), $"{name}: every other answer kept");
            }

            Assert.That(Kept(large, "attachment"), Is.Empty, "the file past its limit is not kept");
            Assert.That(Kept(drained, "attachment"), Is.Empty);
            Assert.That(button.QuerySelectorAll(".jfb-row"), Has.Length.EqualTo(2), "the row's button still makes its change");
            Assert.That(inRow.QuerySelectorAll(".jfb-error-summary a").Select(link => link.GetAttribute("href")), Is.EqualTo(new[] { "#jfb-1-crew-0-cv" }), "a row's file: the error at its field in its row");
            Assert.That(inRow.QuerySelector("#jfb-1-crew-0-cv-error"), Is.Not.Null);
            Assert.That(Kept(program, "attachment"), Is.Empty);
            Assert.That(Kept(three, "attachment"), Is.EqualTo(new[] { "Keep a.pdf (0.1 KB)", "Keep b.pdf (0.1 KB)" }), "the two the field takes are kept");
            Assert.That(Text(large, "#jfb-1-attachment-error"), Does.Contain("2 KB"), "the field's own message names its limit");
            Assert.That(Kept(exact, "attachment"), Is.EqualTo(new[] { "Keep plan.pdf (2 KB)" }), "a file of exactly its limit is kept, and a row's button still makes its change");
            Assert.That(exact.QuerySelectorAll(".jfb-row"), Has.Length.EqualTo(2));
            Assert.That(exact.QuerySelector(".jfb-error-summary"), Is.Null);
            Assert.That(Kept(empty, "attachment"), Is.Empty, "no file chosen is no file");
            Assert.That(empty.QuerySelector(".jfb-error-summary"), Is.Null, "and no error");
            Assert.That(await StoredAsync(key), Is.Empty, "nothing sent");
        });
    }

    [Test]
    public async Task AFileForAFormThatFilledUpOrClosedMeanwhile_IsNotStored_AndThePageShowsTheFormsNotice()
    {
        var closes = new DateTimeOffset(2026, 6, 15, 12, 0, 0, TimeSpan.Zero);
        _clock.SetNow(closes.AddHours(-1));
        var key = await PublishFormAsync("postFiles", definition => FilesForm(definition) with
        {
            Settings = definition.Settings with { Availability = definition.Settings.Availability with { ClosesAt = closes.UtcDateTime, MaxEntries = 1 } },
        });
        _ = await PagesAsync("post-files");
        var form = FormOf(await DocumentAsync(FilesPage), "jfb-1");
        Fill(form, "subject", "Room plan");
        using var taken = await SendAsync(HttpMethod.Post, $"{Root}/forms/{key}/entries", request =>
            request.Content = new StringContent("""{"revision":1,"values":{"subject":"Garden plan","crew":[{}]}}""", Encoding.UTF8, "application/json"));
        var store = GetRequiredService<MemoryFileStore>();

        using var fullSend = await SubmitFilesAsync(form, FormPostFields.Submit, ("attachment", "plan.pdf", Bytes(Pdf, 500)));
        using var fullAdd = await SubmitFilesAsync(form, "add:crew", ("crew[0].cv", "cv.pdf", Bytes(Pdf, 500)));
        var whileFull = store.Ids.Count;
        Assert.That(await Entries.DeleteAsync((await Entries.ListIdsAsync((await FormRows.GetAsync(key))!.Id, 10)).Single()), Is.True);
        _clock.SetNow(closes);
        using var closedSend = await SubmitFilesAsync(form, FormPostFields.Submit, ("attachment", "plan.pdf", Bytes(Pdf, 500)));

        Assert.Multiple(async () =>
        {
            Assert.That(taken.StatusCode, Is.EqualTo(HttpStatusCode.OK), await taken.Content.ReadAsStringAsync());
            foreach (var (name, response, heading) in new[] { ("full, on Send", fullSend, "This form is full"), ("full, on a row's button", fullAdd, "This form is full"), ("closed, on Send", closedSend, "This form is closed") })
            {
                var page = await ParseAsync(response);
                Assert.That(page.QuerySelector("#jfb-1 .jfb-notice h2")?.TextContent, Is.EqualTo(heading), name);
                Assert.That(page.QuerySelector("form#jfb-1"), Is.Null, $"{name}: the notice in the form's place, as for any visitor now");
            }

            Assert.That(whileFull, Is.Zero, "no file stored while the form is full");
            Assert.That(store.Ids, Is.Empty, "nor once it closed");
            Assert.That(await StoredAsync(key), Is.Empty, "and no entry");
        });
    }

    [Test]
    public async Task AKeptFileUnticked_IsLetGo_AndAFileInARow_IsKeptInItsRowAndMovesUpWithIt()
    {
        var key = await PublishFormAsync("postFiles", FilesForm);
        _ = await PagesAsync("post-files");
        var first = FormOf(await DocumentAsync(FilesPage), "jfb-1");
        Fill(first, "subject", "Room plan");

        var added = await FilesAsync(first, "add:crew", ("attachment", "plan.pdf", Bytes(Pdf, 500)), ("crew[0].cv", "ada.pdf", Bytes(Pdf, 300)));

        // Sent without the subject, the post only brings the new files.
        Fill(added, "subject", string.Empty);
        var replaced = await FilesAsync(added, FormPostFields.Submit, ("attachment", "notes.pdf", Bytes(Pdf, 200)), ("crew[0].cv", "ada-2024.pdf", Bytes(Pdf, 350)));
        Fill(replaced, "subject", "Room plan");
        var second = await FilesAsync(replaced, "remove:crew:0", ("crew[1].cv", "lin.pdf", Bytes(Pdf, 100_000)));
        foreach (var box in second.QuerySelectorAll<AngleSharp.Html.Dom.IHtmlInputElement>("input[type='checkbox'][name='attachment']"))
        {
            box.IsChecked = false;
        }

        using var sent = await SubmitFilesAsync(second, FormPostFields.Submit);
        var entryId = (await Entries.ListIdsAsync((await FormRows.GetAsync(key))!.Id, 10)).Single();
        var files = await Entries.ListFilesAsync(entryId);

        Assert.Multiple(() =>
        {
            Assert.That(Kept(added, "crew[0].cv"), Is.EqualTo(new[] { "Keep ada.pdf (0.3 KB)" }), "kept in its row");
            Assert.That(Kept(added, "attachment"), Is.EqualTo(new[] { "Keep plan.pdf (0.5 KB)" }));
            Assert.That(Kept(replaced, "attachment"), Is.EqualTo(new[] { "Keep plan.pdf (0.5 KB)", "Keep notes.pdf (0.2 KB)" }), "a field of two files takes the new one beside the kept one");
            Assert.That(Kept(replaced, "crew[0].cv"), Is.EqualTo(new[] { "Keep ada-2024.pdf (0.3 KB)" }), "a field of one file takes the new one in the kept one's place");
            Assert.That(Kept(second, "crew[0].cv"), Is.EqualTo(new[] { "Keep lin.pdf (97.7 KB)" }), "the second row's file moved up with it, the first row's gone with its row");
            Assert.That(second.QuerySelectorAll(".jfb-row"), Has.Length.EqualTo(1));
            Assert.That(sent.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(files.Select(file => (file.OriginalName, file.SizeBytes)), Is.EqualTo(new[] { ("lin.pdf", 100_000L) }), "the unticked files let go; a file past what is held in memory stored whole");
        });
    }

    [Test]
    public async Task APostPastTheDrainAllowance_ShowsTheFormWithTheNotice_AndAPostWithoutTheMarker_HoldsItsFilesToTheSameRules()
    {
        var key = await PublishFormAsync("postFiles", FilesForm);
        _ = await PagesAsync("post-files", "plain");
        var first = FormOf(await DocumentAsync(FilesPage), "jfb-1");
        Fill(first, "subject", "Room plan");

        using var past = await SubmitFilesAsync(first, FormPostFields.Submit, ("attachment", "huge.pdf", Bytes(Pdf, 3_000_000)));
        var readOfPast = GetRequiredService<BodyCap>().LastRead;
        var refused = FormOf(await ParseAsync(past), "jfb-1");
        using var elsewhere = await SubmitFilesAsync(first, FormPostFields.Submit, "/plain/?" + FormPostFields.PostMarker + "=" + key.ToString("D") + ".jfb-1", ("attachment", "huge.pdf", Bytes(Pdf, 3_000_000)));
        using var unmarked = await SubmitFilesAsync(first, FormPostFields.Submit, FilesPage, ("attachment", "plan.pdf", Bytes(Pdf, 500)));
        var entryId = (await Entries.ListIdsAsync((await FormRows.GetAsync(key))!.Id, 10)).Single();
        var files = await Entries.ListFilesAsync(entryId);

        Assert.Multiple(async () =>
        {
            Assert.That(3_000_000, Is.GreaterThan(FormLimit * 2), "past the drain allowance");
            Assert.That(readOfPast, Is.Zero, "a declared length past the allowance is not read at all");
            Assert.That(Header(past, "Cache-Control"), Is.EqualTo("no-store"));
            Assert.That((elsewhere.StatusCode, Header(elsewhere, "Cache-Control")), Is.EqualTo((HttpStatusCode.OK, "no-store")), "the reader marks a post it refused, also on a page without the form");
            Assert.That(refused.QuerySelector("[data-jfb-notice='too_large'] h2")!.TextContent, Is.EqualTo("Your answers weren't sent"));
            Assert.That(refused.QuerySelector("[data-jfb-notice='too_large']")!.GetAttribute("class"), Is.EqualTo("jfb-notice jfb-notice-error"));
            Assert.That(Value(refused, "subject"), Is.Null, "nothing of the post could be kept");
            Assert.That(unmarked.StatusCode, Is.EqualTo(HttpStatusCode.Redirect), "a post without the marker (a site's own view) is read as Umbraco reads it, under the site's limits");
            Assert.That(files.Select(file => file.OriginalName), Is.EqualTo(new[] { "plan.pdf" }), "and its files go through the same rules");
        });
    }

    [Test]
    public async Task APostWithoutTheMarker_IsHeldToTheFieldsCount_AndStoresNothingPastIt()
    {
        _ = await PublishFormAsync("postFiles", FilesForm);
        _ = await PagesAsync("post-files");
        var form = FormOf(await DocumentAsync(FilesPage), "jfb-1");
        Fill(form, "subject", "Room plan");

        using var response = await SubmitFilesAsync(
            form,
            FormPostFields.Submit,
            FilesPage,
            ("attachment", "a.pdf", Bytes(Pdf, 100)),
            ("attachment", "b.pdf", Bytes(Pdf, 100)),
            ("attachment", "c.pdf", Bytes(Pdf, 100)),
            ("attachment", "d.pdf", Bytes(Pdf, 100)));
        var answered = FormOf(await ParseAsync(response), "jfb-1");
        var waiting = await Entries.ListOrphanFilesAsync(DateTimeOffset.UtcNow.AddHours(1), 50);

        Assert.Multiple(() =>
        {
            Assert.That(answered.QuerySelector("#jfb-1-attachment-error"), Is.Not.Null, "one too many is an error at its field");
            Assert.That(answered.QuerySelectorAll(".jfb-error-summary a").Select(link => link.GetAttribute("href")), Is.EqualTo(new[] { "#jfb-1-attachment" }), "said once");
            Assert.That(waiting.Select(file => file.OriginalName), Is.EquivalentTo(new[] { "a.pdf", "b.pdf" }), "nothing past its count stored");
        });
    }

    [Test]
    public async Task TheFilesReader_LeavesOtherPostsAlone_SkipsPartsOfNoField_AndRefusesAPostThatBreaksItsLimits()
    {
        var key = await PublishFormAsync("postFiles", FilesForm);
        _ = await PagesAsync("post-files");
        var first = FormOf(await DocumentAsync(FilesPage), "jfb-1");
        Fill(first, "subject", "Room plan");
        var pairs = Posted(first, FormPostFields.Submit);
        var marked = first.GetAttribute("action")!.Split('#')[0];
        string Marked(string marker) => FilesPage + "?jfb-post=" + marker;
        const string Boundary = "jfb-test";

        // Markers the reader leaves alone: Umbraco reads the post, and the handler holds it as before.
        var unknown = await RawAsync(Marked(Guid.NewGuid().ToString("D") + ".jfb-1"), Multipart(Boundary, pairs));
        var instance = await RawAsync(Marked(key.ToString("D") + ".one"), Multipart(Boundary, pairs));
        var noDot = await RawAsync(Marked(key.ToString("N")), Multipart(Boundary, pairs));
        var encoded = await RawAsync(marked, new FormUrlEncodedContent(pairs));

        // Parts it skips: a file of no field, a part without a name, a part that is no form data.
        List<HttpContent> extra =
        [
            Part("form-data; name=\"nowhere\"; filename=\"x.pdf\"", Bytes(Pdf, 100)),
            Part("form-data; filename=\"y.pdf\"", Bytes(Pdf, 100)),
            Part("attachment; name=\"attachment\"; filename=\"z.pdf\"", Bytes(Pdf, 100)),
        ];
        var skipped = await RawAsync(marked, Multipart(Boundary, pairs, extra));
        var exactValue = await RawAsync(marked, Multipart(Boundary, [.. pairs, new("nowhere", new string('a', FormPostFilesMiddleware.MostValueBytes))]));

        // Posts that break its limits: too many parts, a part with too many headers or too long ones, a value too long, a
        // body cut short, and a body of no declared length past the drain allowance, which the cap stops as it is read.
        var many = await RawAsync(marked, Multipart(Boundary, [.. pairs, .. Enumerable.Range(0, FormPostFilesMiddleware.MostParts).Select(at => new KeyValuePair<string, string>("x" + at, "y"))]));
        var headed = Part("form-data; name=\"remark\"", "a"u8.ToArray());
        foreach (var at in Enumerable.Range(0, FormPostFilesMiddleware.MostHeaders))
        {
            headed.Headers.TryAddWithoutValidation("X-Jfb-" + at, "1");
        }

        var manyHeaders = await RawAsync(marked, Multipart(Boundary, pairs, [headed]));
        var longHeaders = await RawAsync(marked, Multipart(Boundary, pairs, [Part("form-data; name=\"remark\"; note=\"" + new string('a', FormPostFilesMiddleware.MostHeaderBytes) + "\"", "a"u8.ToArray())]));
        var longValue = await RawAsync(marked, Multipart(Boundary, [.. pairs, new("remark", new string('a', FormPostFilesMiddleware.MostValueBytes + 1))]));
        var longValues = await RawAsync(marked, Multipart(Boundary, [.. pairs, new("one", new string('a', 700_000)), new("two", new string('a', 700_000))]));
        var cut = await RawAsync(marked, new ByteArrayContent(System.Text.Encoding.ASCII.GetBytes("--" + Boundary + Crlf + "Content-Disposition: form-data; name=\"subject\"" + Crlf + Crlf + "Room"))
        {
            Headers = { ContentType = MediaTypeHeaderValue.Parse("multipart/form-data; boundary=" + Boundary) },
        });
        var undeclared = await RawAsync(marked, await UndeclaredAsync(Multipart(Boundary, pairs, [Part("form-data; name=\"nowhere\"; filename=\"huge.pdf\"", Bytes(Pdf, 3_000_000))])));

        Assert.Multiple(async () =>
        {
            foreach (var (name, response) in new[] { ("an unknown form", unknown), ("an instance id the render never gives", instance), ("a marker without its instance", noDot), ("a post that is no multipart", encoded) })
            {
                Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Redirect), name + ": read by Umbraco and sent as before");
            }

            Assert.That(skipped.StatusCode, Is.EqualTo(HttpStatusCode.Redirect), "the parts of no field skipped, the rest sent");
            Assert.That(exactValue.StatusCode, Is.EqualTo(HttpStatusCode.Redirect), "a value of its most bytes read");
            foreach (var (name, response) in new[] { ("too many parts", many), ("too many headers", manyHeaders), ("headers too long", longHeaders), ("a value too long", longValue), ("values too long together", longValues), ("a body cut short", cut), ("a body of no declared length past the allowance", undeclared) })
            {
                var form = FormOf(await ParseAsync(response), "jfb-1");
                Assert.That(form.QuerySelector("[data-jfb-notice='too_large']"), Is.Not.Null, name + ": nothing kept, and the notice says so");
            }

            Assert.That(GetRequiredService<BodyCap>().LastSet, Is.EqualTo(FormLimit * 2), "the body capped at the drain allowance, twice the form's limit");
            Assert.That(await StoredAsync(key), Has.Count.EqualTo(6), "the posts the reader left alone or read were sent; those it refused were not");
            var entryIds = await Entries.ListIdsAsync((await FormRows.GetAsync(key))!.Id, 10);
            Assert.That(await Entries.ListFilesAsync(entryIds), Is.Empty, "no part of no field became a file");
        });
    }

    // A form with a required text, a text, a file field of up to two PDFs of 2 KB, and a group of one or two rows with a
    // PDF of 2 KB each.
    private static FormDefinition FilesForm(FormDefinition definition) => WithFields(definition, Topic2, Remark, Attachment, Crew);

    // Bytes that start as the given ones, padded to a length.
    private static byte[] Bytes(byte[] head, int length)
    {
        var bytes = new byte[length];
        head.AsSpan(0, Math.Min(head.Length, length)).CopyTo(bytes);
        bytes.AsSpan(Math.Min(head.Length, length)).Fill((byte)'x');
        return bytes;
    }

    // The labels of a field's kept files.
    private static string[] Kept(IElement form, string name) =>
        [.. form.QuerySelectorAll($"input[type='checkbox'][name='{name}']").Select(box => form.QuerySelector($"label[for='{box.Id}']")!.TextContent)];

    private static string? Text(IElement form, string selector) => form.QuerySelector(selector)?.TextContent;

    // A multipart body of the pairs, then any other parts, with the given boundary.
    private static MultipartFormDataContent Multipart(string boundary, IEnumerable<KeyValuePair<string, string>> pairs, IEnumerable<HttpContent>? parts = null)
    {
        var content = new MultipartFormDataContent(boundary);
        foreach (var (name, value) in pairs)
        {
            content.Add(new StringContent(value), name);
        }

        foreach (var part in parts ?? [])
        {
            content.Add(part);
        }

        return content;
    }

    // A part with the given disposition.
    private static ByteArrayContent Part(string disposition, byte[] bytes)
    {
        var part = new ByteArrayContent(bytes);
        part.Headers.TryAddWithoutValidation("Content-Disposition", disposition);
        return part;
    }

    // The content as a body of no declared length, sent as a browser streams one.
    private static async Task<HttpContent> UndeclaredAsync(HttpContent content)
    {
        var undeclared = new Undeclared(await content.ReadAsByteArrayAsync());
        undeclared.Headers.ContentType = content.Headers.ContentType;
        return undeclared;
    }

    // Posts a body to an address of the page, from the site's own origin.
    private async Task<HttpResponseMessage> RawAsync(string address, HttpContent content) =>
        await SendAsync(HttpMethod.Post, address, request =>
        {
            request.Content = content;
            request.Headers.TryAddWithoutValidation("Origin", OwnOrigin);
            request.Headers.Referrer = new Uri(Client.BaseAddress!, FilesPage);
        });

    // Posts the form with files and shows the page it answers.
    private async Task<IElement> FilesAsync(IElement form, string nav, params (string Name, string FileName, byte[] Bytes)[] files)
    {
        using var response = await SubmitFilesAsync(form, nav, files);
        return FormOf(await ParseAsync(response), "jfb-1");
    }

    private Task<HttpResponseMessage> SubmitFilesAsync(IElement form, string? nav, params (string Name, string FileName, byte[] Bytes)[] files) =>
        SubmitFilesAsync(form, nav, form.GetAttribute("action")!.Split('#')[0], files);

    // Posts the form as a browser without JavaScript does with files: multipart, its values, then each file as a part.
    private async Task<HttpResponseMessage> SubmitFilesAsync(IElement form, string? nav, string address, params (string Name, string FileName, byte[] Bytes)[] files)
    {
        var content = new MultipartFormDataContent();
        foreach (var (name, value) in Posted(form, nav))
        {
            content.Add(new StringContent(value), name);
        }

        foreach (var (name, fileName, bytes) in files)
        {
            var part = new ByteArrayContent(bytes);
            part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            // A file input left empty posts a part with an empty name, which HttpClient will not write itself.
            part.Headers.ContentDisposition = new ContentDispositionHeaderValue("form-data") { Name = "\"" + name + "\"", FileName = "\"" + fileName + "\"" };
            content.Add(part);
        }

        return await SendAsync(HttpMethod.Post, address, request =>
        {
            request.Content = content;
            request.Headers.TryAddWithoutValidation("Origin", OwnOrigin);
            request.Headers.Referrer = new Uri(Client.BaseAddress!, FilesPage);
        });
    }

    // Bytes sent without a length.
    private sealed class Undeclared(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => stream.WriteAsync(bytes).AsTask();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
