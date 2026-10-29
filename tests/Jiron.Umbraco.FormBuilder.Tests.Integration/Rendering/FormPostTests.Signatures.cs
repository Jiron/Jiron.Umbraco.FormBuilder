using System.Net;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Core.Signatures;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Jiron.Umbraco.FormBuilder.Web.Rendering;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Rendering;

// A signature typed without JavaScript (ROADMAP 6.2k, ADR-0097, ADR-0103): the name typed instead is drawn on the
// server as the signature's PNG on its pad, held to the upload's rules and stored with the entry, the name kept exactly
// in the PNG; the page keeps the name, a page button draws it, a row's button does not, a blank name is no signature,
// one longer than the input allows is invalid, and a drawing the upload's rules refuse is an error at its field.
public sealed partial class FormPostTests
{
    private const string SignPage = "/post-signature/";

    private static readonly FormField Signer = Field(K(0x90), "signer", "text") with { Required = true };
    private static readonly FormField Sign = Field(K(0x91), "sign", "signature") with { Required = true };
    private static readonly FormField Witness = Field(K(0x92), "witness", "signature", """{"width":300,"height":150}""");
    private static readonly FormField Witnesses = Field(K(0x93), "witnesses", "repeater", """{"minRows":1,"maxRows":2}""") with { Fields = [Witness] };

    [Test]
    public async Task ANameTypedWithoutJavaScript_IsStoredAsTheSignature_ThePngOnItsPadHoldingTheNameAsTyped()
    {
        var key = await PublishFormAsync("postSign", SignForm);
        _ = await PagesAsync("post-signature");
        var form = FormOf(await DocumentAsync(SignPage), "jfb-1");
        var typed = "  Zo" + (char)0xEB + " M" + "u" + (char)0x0308 + "ller  ";
        var cyrillic = new string([(char)0x0415, (char)0x043B, (char)0x0435, (char)0x043D, (char)0x0430]);
        Fill(form, "signer", "Zoe");
        Fill(form, "sign.typed", typed);
        Fill(form, "witnesses[0].witness.typed", cyrillic);

        // A token posted beside the name, as a site's own view might, gives way to the name drawn.
        var stray = form.Owner!.CreateElement("input");
        stray.SetAttribute("type", "hidden");
        stray.SetAttribute("name", "sign");
        stray.SetAttribute("value", "not-a-token");
        form.AppendChild(stray);
        using var sent = await SubmitFilesAsync(form, FormPostFields.Submit);
        var entryId = (await Entries.ListIdsAsync((await FormRows.GetAsync(key))!.Id, 10)).Single();
        var files = await Entries.ListFilesAsync(entryId);
        var signed = await BytesAsync(files.Single(file => file.FieldKey == Sign.Key).StoredName);
        var witnessed = await BytesAsync(files.Single(file => file.FieldKey == Witness.Key).StoredName);

        Assert.Multiple(() =>
        {
            Assert.That(form.QuerySelector("input[name='sign.typed']")!.GetAttribute("maxlength"), Is.EqualTo("100"), "the input says how long a name may be");
            Assert.That(sent.StatusCode, Is.EqualTo(HttpStatusCode.Redirect), "sent");
            Assert.That(files.Select(file => (file.OriginalName, file.ContentType)), Is.All.EqualTo(("signature.png", "image/png")));
            Assert.That(PngHeader.Read(signed), Is.EqualTo((TypedSignature.DefaultWidth, TypedSignature.DefaultHeight)), "the default pad");
            Assert.That(TypedSignature.NameOf(signed), Is.EqualTo("Zo" + (char)0xEB + " M" + (char)0xFC + "ller"), "the name as typed, cleaned and composed");
            Assert.That(PngHeader.Read(witnessed), Is.EqualTo((300, 150)), "a row's signature on its own pad");
            Assert.That(TypedSignature.NameOf(witnessed), Is.EqualTo(cyrillic), "a name the hand cannot draw, kept exactly");
        });
    }

    [Test]
    public async Task TheName_IsKeptWhileAnotherFieldFails_DrawnOnEachPageButton_AndNotOnARowsButton()
    {
        var key = await PublishFormAsync("postSign", SignForm);
        _ = await PagesAsync("post-signature");
        var first = FormOf(await DocumentAsync(SignPage), "jfb-1");
        Fill(first, "sign.typed", "Ada Lovelace");
        var store = GetRequiredService<MemoryFileStore>();
        var before = store.Ids.Count;

        var failed = await FilesAsync(first, FormPostFields.Submit);
        var drawn = store.Ids.Count;
        var added = await FilesAsync(failed, "add:witnesses");
        var afterRow = store.Ids.Count;
        Fill(added, "signer", "Ada");
        Fill(added, "sign.typed", "Ada King");
        using var sent = await SubmitFilesAsync(added, FormPostFields.Submit);
        var entryId = (await Entries.ListIdsAsync((await FormRows.GetAsync(key))!.Id, 10)).Single();
        var stored = (await Entries.ListFilesAsync(entryId)).Single();

        Assert.Multiple(async () =>
        {
            Assert.That(failed.QuerySelectorAll(".jfb-error-summary a").Select(link => link.GetAttribute("href")), Is.EqualTo(new[] { "#jfb-1-signer" }), "the signature counts as given");
            Assert.That(Value(failed, "sign.typed"), Is.EqualTo("Ada Lovelace"), "the name kept in its input");
            Assert.That(failed.QuerySelectorAll("input[type='checkbox'][name='sign']"), Is.Empty, "no box to keep: the name is drawn again");
            Assert.That(drawn, Is.EqualTo(before + 1), "Send drew it");
            Assert.That(afterRow, Is.EqualTo(drawn), "a row's button drew nothing");
            Assert.That(Value(added, "sign.typed"), Is.EqualTo("Ada Lovelace"));
            Assert.That(added.QuerySelectorAll(".jfb-row"), Has.Length.EqualTo(2));
            Assert.That(sent.StatusCode, Is.EqualTo(HttpStatusCode.Redirect));
            Assert.That(TypedSignature.NameOf(await BytesAsync(stored.StoredName)), Is.EqualTo("Ada King"), "the name as last typed");
        });
    }

    [Test]
    public async Task ABlankName_IsNoSignature_AndANameLongerThanItsInputAllows_IsInvalid()
    {
        var key = await PublishFormAsync("postSign", SignForm);
        _ = await PagesAsync("post-signature");
        var form = FormOf(await DocumentAsync(SignPage), "jfb-1");
        Fill(form, "signer", "Ada");
        var store = GetRequiredService<MemoryFileStore>();
        var before = store.Ids.Count;

        Fill(form, "sign.typed", "  " + (char)0x200B + " ");
        var blank = await FilesAsync(form, FormPostFields.Submit);
        Fill(form, "sign.typed", new string('a', TypedSignature.LongestName + 1));
        var tooLong = await FilesAsync(form, FormPostFields.Submit);
        Fill(form, "sign.typed", new string('a', TypedSignature.LongestName));
        using var longest = await SubmitFilesAsync(form, FormPostFields.Submit);

        Assert.Multiple(async () =>
        {
            Assert.That(Text(blank, "#jfb-1-sign-error"), Is.EqualTo("Add your signature, or type your name instead"), "a blank name is no signature");
            Assert.That(Text(tooLong, "#jfb-1-sign-error"), Is.EqualTo("Sign again, or type your name instead"), "a forged name past the input's length");
            Assert.That(Value(tooLong, "sign.typed"), Has.Length.EqualTo(TypedSignature.LongestName + 1), "kept to correct");
            Assert.That(longest.StatusCode, Is.EqualTo(HttpStatusCode.Redirect), "the longest name sent");
            Assert.That(store.Ids.Count, Is.EqualTo(before + 1), "only the longest name drawn");
            Assert.That(await StoredAsync(key), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task ANameTyped_WhileAnotherServerTrashedTheForm_IsAnErrorAtItsField_AndNothingIsStored()
    {
        var key = await PublishFormAsync("postSign", SignForm);
        _ = await PagesAsync("post-signature");
        var form = FormOf(await DocumentAsync(SignPage), "jfb-1");
        Fill(form, "signer", "Ada");
        Fill(form, "sign.typed", "Ada Lovelace");
        var store = GetRequiredService<MemoryFileStore>();
        var before = store.Ids.Count;

        // The database knows, this server's published forms do not yet: the upload's rules refuse the drawing.
        var stored = (await FormRows.GetAsync(key))!;
        _ = await FormRows.TrashAsync(stored.Id, stored.ConcurrencyVersion, null);
        var refused = await FilesAsync(form, FormPostFields.Submit);

        Assert.Multiple(() =>
        {
            Assert.That(Text(refused, "#jfb-1-sign-error"), Is.EqualTo("Sign again, or type your name instead"));
            Assert.That(Value(refused, "sign.typed"), Is.EqualTo("Ada Lovelace"), "the name kept");
            Assert.That(store.Ids, Has.Count.EqualTo(before), "nothing stored");
        });
    }

    // A form with a required text, a required signature, and a group of one or two rows with a signature on a pad of its
    // own.
    private static FormDefinition SignForm(FormDefinition definition) => WithFields(definition, Signer, Sign, Witnesses);

    // A stored file's bytes.
    private async Task<byte[]> BytesAsync(string storedName)
    {
        await using var content = (await GetRequiredService<MemoryFileStore>().OpenReadAsync(storedName))!;
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy);
        return copy.ToArray();
    }
}
