using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.Exports;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Privacy;
using Jiron.Umbraco.FormBuilder.Core.Retention;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Actions;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Privacy;

/// <summary>
/// The privacy tools (ROADMAP 13.1 part 13.2, ADR-0223): finding a person in every form by an email field in any case, by a
/// member with the address and by the mail sent to them; erasing them so that no Jiron Form Builder table holds the address
/// any more; anonymising, which keeps the entry for the counts and gives its places back (ADR-0090); legal holds, which an
/// erase leaves; and the export, a folder per entry with its answers and files.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class PersonDataTests : ActionTestBase
{
    private const string Ada = "ada.lovelace@jfb.test";

    private const string Grace = "grace@jfb.test";

    private static readonly FormField EmailField = new() { Key = Guid.Parse("e2230000-0000-4000-8000-000000000001"), Type = "email", Alias = "email", Label = new LocalizedText("Email") };

    private static readonly FormField Seat = new()
    {
        Key = Guid.Parse("e2230000-0000-4000-8000-000000000002"),
        Type = "dropdown",
        Alias = "seat",
        Label = new LocalizedText("Seat"),
        Options = new FieldOptions { Items = [new FieldOption { Value = "front", Label = new LocalizedText("Front"), Places = 1 }] },
    };

    private static readonly FormField Upload = new() { Key = Guid.Parse("e2230000-0000-4000-8000-000000000003"), Type = "file", Alias = "cv", Label = new LocalizedText("CV") };

    private static readonly Guid User = Guid.Parse("e2230000-0000-4000-8000-0000000000aa");

    private PersonData People => GetRequiredService<PersonData>();

    private MemoryFileStore Files => GetRequiredService<MemoryFileStore>();

    private IEmailRepository Emails => GetRequiredService<IEmailRepository>();

    private IEntryNoteRepository Notes => GetRequiredService<IEntryNoteRepository>();

    [Test]
    public async Task Find_ByAnEmailFieldInAnyCase_ByTheMemberWithTheAddress_AndByTheMailToThem_AcrossForms_AndAuditsNoAddress()
    {
        var contact = await PublishAsync("Contact", fields: [NameField, EmailField]);
        var survey = await PublishAsync("Survey", fields: [NameField, EmailField]);
        var mine = await SubmitAsync(contact, $$"""{"name":"Ada","email":"{{Ada.ToUpperInvariant()}}"}""");
        var other = await SubmitAsync(contact, $$"""{"name":"Grace","email":"{{Grace}}"}""");
        var elsewhere = await SubmitAsync(survey, $$"""{"name":"Ada","email":"{{Ada}}"}""");
        var asMember = await SubmitAsync(survey, """{"name":"Signed in"}""");
        var draft = await SubmitAsync(contact, """{"name":"Saved for later"}""");
        var member = MemberWith(Ada);
        await ExecAsync("UPDATE {0} SET memberKey = @0 WHERE id = @1", FormBuilderTables.Entry, member, asMember.Id);
        await ExecAsync("UPDATE {0} SET state = 'draft' WHERE id = @0", FormBuilderTables.Entry, draft.Id);
        await MailAsync(Ada, draft.Id, EmailKind.Resume, visitor: true);

        var byAddress = await People.FindAsync(PersonQuery.Of($"  {Ada.ToUpperInvariant()} ", null), User);
        var byMember = await People.FindAsync(PersonQuery.Of(null, member), User);
        var audit = await AuditOfAsync(PersonData.FoundAction);

        Assert.Multiple(() =>
        {
            Assert.That(byAddress.Entries.Select(entry => entry.Key), Is.EqualTo(new[] { draft.Key, asMember.Key, elsewhere.Key, mine.Key }), "newest first, in both forms");
            Assert.That(byAddress.Entries.Select(entry => entry.Key), Does.Not.Contain(other.Key));
            Assert.That(byAddress.Entries[0].State, Is.EqualTo(EntryState.Draft), "the draft its link to resume was mailed for");
            Assert.That(byMember.Entries.Select(entry => entry.Key), Is.EquivalentTo(byAddress.Entries.Select(entry => entry.Key)), "the member's own, and those of their address");
            Assert.That(byAddress.More, Is.False);
            Assert.That(audit, Has.Count.EqualTo(2));
            Assert.That(string.Join(" ", audit), Does.Not.Contain("ada").IgnoreCase, "the audit names the counts, never the person");
            Assert.That(() => PersonQuery.Of(Ada, member), Throws.InstanceOf<Jiron.Umbraco.FormBuilder.Core.Forms.FormBuilderValidationException>(), "one or the other");
            Assert.That(() => PersonQuery.Of("not an address", null), Throws.InstanceOf<Jiron.Umbraco.FormBuilder.Core.Forms.FormBuilderValidationException>());
        });
    }

    [Test]
    public async Task Erase_DeletesEveryCopy_SoNoTableHoldsTheAddress_AndKeepsTheOthers()
    {
        var form = await PublishAsync("Contact", fields: [NameField, EmailField, Seat]);
        var mine = await SubmitAsync(form, $$"""{"name":"Ada","email":"{{Ada}}","seat":"front"}""");
        var draft = await SubmitAsync(form, """{"name":"Ada, later"}""");
        var other = await SubmitAsync(form, $$"""{"name":"Grace","email":"{{Grace}}"}""");
        await ExecAsync("UPDATE {0} SET state = 'draft' WHERE id = @0", FormBuilderTables.Entry, draft.Id);
        await LeaveCopiesAsync(mine, other, draft);
        var preview = await People.PreviewAsync(PersonQuery.Of(Ada, null));
        var copies = await TablesHoldingAsync(Ada);

        var erased = await People.EraseAsync(PersonQuery.Of(Ada, null), PersonEraseMode.Delete, User);

        var row = (await FormRows.GetAsync(form))!;
        var held = await TablesHoldingAsync(Ada);
        Assert.Multiple(async () =>
        {
            Assert.That((preview.Entries, preview.Drafts, preview.Held, preview.Emails), Is.EqualTo((1, 1, 0, 3)), "its own mail, the draft's link and the forward to the address");
            Assert.That((erased.Entries, erased.Drafts, erased.Held, erased.Mode), Is.EqualTo((1, 1, 0, PersonEraseMode.Delete)));
            Assert.That(copies, Is.SupersetOf(new[] { "jfbEntryValue.text", "jfbEntryValue.textKey", "jfbEntry.searchText", "jfbEntryNote.body", "jfbEntryEvent.details", "jfbEmail.toAddresses", "jfbEmail.recipientKey", "jfbEmail.htmlBody" }), "where the address was");
            Assert.That(held, Is.Empty, "no Jiron Form Builder table holds the address");
            Assert.That(await Entries.GetByIdAsync(mine.Id), Is.Null);
            Assert.That(await Entries.GetByIdAsync(draft.Id), Is.Null);
            Assert.That(await Entries.GetByIdAsync(other.Id), Is.Not.Null, "another's entry stays");
            Assert.That(await Notes.ListAsync(other.Id), Has.Count.EqualTo(1), "and its own note");
            Assert.That((await Events.ListForEntryAsync(other.Id)).Single(happened => happened.Kind == EntryForwards.ForwardedEvent).Details, Is.EqualTo("{}"), "the forward to the person stays, to nobody named");
            Assert.That((await Entries.GetPlacesTakenAsync(row.Id, [new OptionPlace(Seat.Key, "front")])).GetValueOrDefault(new OptionPlace(Seat.Key, "front")), Is.Zero, "the seat is free again");
            Assert.That(await AuditOfAsync(PersonData.ErasedAction), Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task Anonymise_KeepsTheEntryForTheCounts_GivesItsPlacesBack_AndKeepsNothingOfThePerson()
    {
        var form = await PublishAsync("Contact", change: definition => definition with { Actions = [Action("notify")] }, fields: [NameField, EmailField, Seat]);
        var mine = await SubmitAsync(form, $$"""{"name":"Ada","email":"{{Ada}}","seat":"front"}""");
        var other = await SubmitAsync(form, $$"""{"name":"Grace","email":"{{Grace}}"}""");
        await LeaveCopiesAsync(mine, other, draft: null);
        await ExecAsync("UPDATE {0} SET lastErrorMessage = @0 WHERE entryId = @1", FormBuilderTables.ActionRun, $"Could not reach {Ada}", mine.Id);
        var row = (await FormRows.GetAsync(form))!;
        var seat = new OptionPlace(Seat.Key, "front");
        var before = (await Entries.GetPlacesTakenAsync(row.Id, [seat]))[seat];
        var copies = await TablesHoldingAsync(Ada);

        var erased = await People.EraseAsync(PersonQuery.Of(Ada, null), PersonEraseMode.Anonymise, User);

        var kept = (await Entries.GetByIdAsync(mine.Id))!;
        var timeline = await Events.ListForEntryAsync(mine.Id);
        Assert.Multiple(async () =>
        {
            Assert.That((erased.Entries, erased.Mode), Is.EqualTo((1, PersonEraseMode.Anonymise)));
            Assert.That((kept.Number, kept.State), Is.EqualTo((mine.Number, mine.State)), "the entry stays for the counts");
            Assert.That(await Entries.ListValuesAsync([mine.Id]), Is.Empty, "with no answer");
            Assert.That((before, (await Entries.GetPlacesTakenAsync(row.Id, [seat])).GetValueOrDefault(seat)), Is.EqualTo((1, 0)), "its place is given back (ADR-0090)");
            Assert.That(await Notes.ListAsync(mine.Id), Is.Empty);
            Assert.That(timeline.Select(happened => happened.Kind), Does.Contain(EntryAnonymiser.AnonymisedEvent));
            Assert.That(timeline.Single(happened => happened.Kind == EntryEdits.EditedEvent).Details, Is.EqualTo("{}"), "an edit's values before and after go");
            Assert.That(copies, Is.SupersetOf(new[] { "jfbEntryValue.text", "jfbEntry.searchText", "jfbEntryNote.body", "jfbEntryEvent.details", "jfbEmail.toAddresses", "jfbActionRun.lastErrorMessage" }), "where the address was");
            Assert.That(await TablesHoldingAsync(Ada), Is.Empty, "no Jiron Form Builder table holds the address");
            Assert.That((await Entries.ListValuesAsync([other.Id])).Select(value => value.Text), Does.Contain(Grace), "another's answers stay");
        });
    }

    [Test]
    public async Task AHold_KeepsThePersonsEntry_FromTheErase_AndTakesOnlyTheirOwn_UntilItIsReleased()
    {
        var form = await PublishAsync("Contact", fields: [NameField, EmailField]);
        var mine = await SubmitAsync(form, $$"""{"name":"Ada","email":"{{Ada}}"}""");
        var later = await SubmitAsync(form, $$"""{"name":"Ada again","email":"{{Ada}}"}""");
        var other = await SubmitAsync(form, $$"""{"name":"Grace","email":"{{Grace}}"}""");
        var query = PersonQuery.Of(Ada, null);

        var tooLong = await People.HoldAsync(query, [mine.Key], Clock.GetUtcNow().AddDays(EntryHolds.MaxDays + 1), "Court case", User);
        var held = await People.HoldAsync(query, [mine.Key, other.Key], Clock.GetUtcNow().AddDays(30), "Court case", User);
        var preview = await People.PreviewAsync(query);
        var erased = await People.EraseAsync(query, PersonEraseMode.Delete, User);
        var released = await People.ReleaseAsync(query, [mine.Key], User);

        Assert.Multiple(async () =>
        {
            Assert.That(tooLong.Refusal, Is.EqualTo(EntryHoldRefusal.End));
            Assert.That(held, Is.EqualTo((EntryHoldRefusal.None, 1)), "only the person's own entry");
            Assert.That((await Entries.GetByIdAsync(other.Id))!.HoldUntilUtc, Is.Null);
            Assert.That((preview.Entries, preview.Held), Is.EqualTo((1, 1)));
            Assert.That((erased.Entries, erased.Held), Is.EqualTo((1, 1)));
            Assert.That(await Entries.GetByIdAsync(later.Id), Is.Null);
            Assert.That(await Entries.GetByIdAsync(mine.Id), Is.Not.Null, "the held entry stays");
            Assert.That(released, Is.EqualTo(1));
            Assert.That((await Entries.GetByIdAsync(mine.Id))!.HoldUntilUtc, Is.Null.Or.LessThanOrEqualTo(Clock.GetUtcNow()));
        });
    }

    [Test]
    public async Task AFormAHoldKeepsEntriesOf_IsNotDeletedForGood_AndStaysWhenTheTrashIsEmptied()
    {
        var form = await PublishAsync("Contact", fields: [NameField, EmailField]);
        var other = await PublishAsync("Survey", fields: [NameField, EmailField]);
        var mine = await SubmitAsync(form, $$"""{"name":"Ada","email":"{{Ada}}"}""");
        await People.HoldAsync(PersonQuery.Of(Ada, null), [mine.Key], Clock.GetUtcNow().AddDays(30), "Court case", User);
        var service = GetRequiredService<IFormBuilderFormService>();
        var trashed = (await service.TrashAsync(form, (await FormRows.GetAsync(form))!.ConcurrencyVersion, User))!;
        await service.TrashAsync(other, (await FormRows.GetAsync(other))!.ConcurrencyVersion, User);

        var refused = Assert.ThrowsAsync<FormBuilderConflictException>(() => service.DeleteAsync(form, trashed.ConcurrencyVersion, User));
        var emptied = await GetRequiredService<IFormBuilderFolderService>().EmptyTrashAsync((_, _) => Task.FromResult(true), User);

        Assert.Multiple(async () =>
        {
            Assert.That(refused!.Conflict, Is.EqualTo(FormBuilderConflict.EntriesHeld));
            Assert.That(emptied.Deleted.Forms.Select(deleted => deleted.Key), Is.EqualTo(new[] { other }), "the other form goes");
            Assert.That(emptied.Left, Is.EqualTo(1), "the held one stays, with what the user may not delete");
            Assert.That(await FormRows.GetAsync(form), Is.Not.Null);
            Assert.That(await Entries.GetByIdAsync(mine.Id), Is.Not.Null);
        });
    }

    [Test]
    public async Task TheExport_HoldsAFolderPerEntry_WithItsAnswersTimelineAndNotes_AndItsFiles()
    {
        var form = await PublishAsync("Contact", fields: [NameField, EmailField, Upload]);
        var mine = await SubmitAsync(form, $$"""{"name":"Ada","email":"{{Ada}}"}""");
        var withFile = await EntryWithFileAsync(form, Ada);
        await Notes.AddAsync(mine.Id, User, "Called back on Monday.");
        var exports = GetRequiredService<PersonExports>();
        var keys = (await People.FindAsync(PersonQuery.Of(Ada, null), User)).Entries.Select(entry => entry.Key).ToList();
        using var zip = new MemoryStream();
        var told = new List<int>();

        var written = await exports.WriteAsync(keys, new EntryExporter(User, true, "en-US"), zip, done =>
        {
            told.Add(done);
            return Task.CompletedTask;
        });

        zip.Position = 0;
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read);
        var alias = (await FormRows.GetAsync(form))!.Alias;
        var names = archive.Entries.Select(entry => entry.FullName).Order(StringComparer.Ordinal).ToList();
        await using var opened = await archive.GetEntry($"{alias}-{mine.Number}/entry.json")!.OpenAsync();
        var json = JsonNode.Parse(await new StreamReader(opened).ReadToEndAsync())!;
        var answers = json["answers"]!.AsArray();
        Assert.Multiple(async () =>
        {
            Assert.That((written.Entries, written.ContentType), Is.EqualTo((2, "application/zip")));
            Assert.That(written.FileName, Does.Match("^personal-data-[0-9]{4}-[0-9]{2}-[0-9]{2}[.]zip$"));
            Assert.That(told, Is.EqualTo(new[] { 1, 2 }));
            Assert.That(names, Is.EqualTo(new[] { $"{alias}-{mine.Number}/entry.json", $"{alias}-{withFile.Number}/entry.json", $"{alias}-{withFile.Number}/report.pdf" }.Order(StringComparer.Ordinal)));
            Assert.That(json["form"]!["alias"]!.GetValue<string>(), Is.EqualTo(alias));
            Assert.That(json["entry"]!["number"]!.GetValue<int>(), Is.EqualTo(mine.Number));
            Assert.That(answers.Select(answer => answer!["label"]!.GetValue<string>()), Does.Contain("Email"));
            Assert.That(answers.First(answer => answer!["label"]!.GetValue<string>() == "Email")!["answer"]![0]!.GetValue<string>(), Is.EqualTo(Ada));
            Assert.That(json["notes"]![0]!["text"]!.GetValue<string>(), Is.EqualTo("Called back on Monday."));
            Assert.That(json["timeline"]!.AsArray().Select(item => item!["kind"]!.GetValue<string>()), Does.Contain(SubmissionEntries.SubmittedEvent));
            Assert.That(await AuditOfAsync(PersonExports.ExportedAction), Has.Count.EqualTo(1));
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.Services.AddSingleton<MemoryFileStore>();
        builder.Services.AddUnique<IFormBuilderFileStore>(provider => provider.GetRequiredService<MemoryFileStore>());
    }

    // Every place the person's address goes besides the entry's answers: a note, an edit's values, mail to them about their
    // entry and their draft, a forward of another's entry to them, and the other's own note.
    private async Task LeaveCopiesAsync(StoredEntry mine, StoredEntry other, StoredEntry? draft)
    {
        await Notes.AddAsync(mine.Id, User, $"Wrote back to {Ada}.");
        await Notes.AddAsync(other.Id, User, "Asked for a call.");
        await Events.AddAsync(new NewEntryEvent(mine.Id, EntryEdits.EditedEvent, User, Details: JsonSerializer.Serialize(new { changes = new[] { new { before = new[] { "old@jfb.test" }, after = new[] { Ada } } } })));
        await Events.AddAsync(new NewEntryEvent(other.Id, EntryForwards.ForwardedEvent, User, Details: JsonSerializer.Serialize(new { to = Ada, sensitive = false })));
        await MailAsync(Ada, mine.Id, EmailKind.Action, visitor: true);
        await MailAsync(Ada, other.Id, EmailKind.Forward, visitor: false);
        if (draft is not null)
        {
            await MailAsync(Ada, draft.Id, EmailKind.Resume, visitor: true);
        }
    }

    private Task<StoredEmail?> MailAsync(string to, long entryId, EmailKind kind, bool visitor) =>
        Emails.AddAsync(new NewEmail(kind, to, "site@jfb.test", $"Hello {to}", $"Dear {to}", "en", HtmlBody: $"<p>Dear {to}</p>", EntryId: entryId, RecipientKey: visitor ? to : null));

    private Guid MemberWith(string email)
    {
        var members = GetRequiredService<IMemberService>();
        var member = members.CreateMemberWithIdentity("jfb-person", email, "Ada Lovelace", Constants.Conventions.MemberTypes.DefaultAlias);
        members.Save(member);
        return member.Key;
    }

    private async Task<StoredEntry> EntryWithFileAsync(Guid form, string email)
    {
        var row = (await FormRows.GetAsync(form))!;
        var id = FormBuilderFileId.New();
        await Files.SaveAsync(id, new MemoryStream("%PDF-bytes"u8.ToArray()));
        var file = (await Entries.AddFileAsync(new NewEntryFile(Guid.NewGuid(), row.Id, Upload.Key, "report.pdf", id, "application/pdf", 9, new string('a', 64), FileScanStates.Clean)))!;
        var fields = await FormRows.ListFieldsAsync(row.Id);
        var upload = fields.Single(item => item.Values.FieldKey == Upload.Key);
        var address = fields.Single(item => item.Values.FieldKey == EmailField.Key);
        return (await Entries.CreateAsync(new NewEntry
        {
            Key = Guid.NewGuid(),
            FormId = row.Id,
            FormVersionId = row.PublishedVersionId!.Value,
            State = EntryState.Submitted,
            SubmissionId = Guid.NewGuid(),
            SubmissionHash = new string('a', 64),
            Culture = "en-US",
            FileIds = [file.Id],
            Values = [new NewEntryValue(upload.Id, "report.pdf", FileId: file.Id), new NewEntryValue(address.Id, email)],
        }))!;
    }

    private Task<int> ExecAsync(string statement, string table, params object?[] args) =>
        GetRequiredService<FormBuilderDatabase>().WriteAsync(db => db.ExecAsync(string.Format(System.Globalization.CultureInfo.InvariantCulture, statement, new SqlNames(db).T(table)), args));

    private async Task<IReadOnlyList<string>> AuditOfAsync(string action) =>
        await GetRequiredService<FormBuilderDatabase>().ReadAsync(async db =>
        {
            var n = new SqlNames(db);
            var rows = await db.FetchListAsync<dynamic>($"SELECT * FROM {n.T(FormBuilderTables.AuditEntry)} WHERE {n.C("action")} = @0", action);
            return (IReadOnlyList<string>)[.. rows.Select(row => string.Join(" ", ((IDictionary<string, object?>)row).Values.Select(value => value?.ToString())))];
        });

    // Every column of every Jiron Form Builder table that holds the text, in any case.
    private async Task<IReadOnlyList<string>> TablesHoldingAsync(string text)
    {
        var found = new List<string>();
        foreach (var table in FormBuilderTables.All)
        {
            var rows = await GetRequiredService<FormBuilderDatabase>().ReadAsync(db => db.FetchListAsync<dynamic>($"SELECT * FROM {new SqlNames(db).T(table)}"));
            foreach (IDictionary<string, object?> row in rows)
            {
                found.AddRange(row.Where(cell => cell.Value is string value && value.Contains(text, StringComparison.OrdinalIgnoreCase)).Select(cell => $"{table}.{cell.Key}"));
            }
        }

        return [.. found.Distinct()];
    }
}
