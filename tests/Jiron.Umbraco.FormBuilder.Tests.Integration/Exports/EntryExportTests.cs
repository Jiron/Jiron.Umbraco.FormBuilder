using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Composing;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.Exports;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Dtos;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Queries;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Actions;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.Navigation;
using Umbraco.Cms.Core.Strings;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Exports;

/// <summary>
/// The exports of a form's entries (ROADMAP 11.4, ADR-0210): each format read back from its file, the columns of grids,
/// rankings and repeatable groups, the formula guard, the sensitive-data rules, the list's filter and the ticked entries,
/// the audit and the announcement, the background exports with their expiry, and the 10,000-entry budget.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class EntryExportTests : ActionTestBase
{
    private static readonly FormField Colours = new()
    {
        Key = Guid.Parse("c4100000-0000-4000-8000-000000000001"),
        Type = "checkboxes",
        Alias = "colours",
        Label = new LocalizedText("Colours"),
        Options = new FieldOptions { Items = [new FieldOption { Value = "red", Label = new LocalizedText("Red") }, new FieldOption { Value = "blue", Label = new LocalizedText("Blue") }] },
    };

    private static readonly GridRow Speed = new() { Key = Guid.Parse("c4100000-0000-4000-8000-000000000002"), Label = new LocalizedText("Speed") };

    private static readonly GridRow Price = new() { Key = Guid.Parse("c4100000-0000-4000-8000-000000000003"), Label = new LocalizedText("Price") };

    private static readonly FormField Rating = new()
    {
        Key = Guid.Parse("c4100000-0000-4000-8000-000000000004"),
        Type = "grid",
        Alias = "rating",
        Label = new LocalizedText("Rating"),
        Options = new FieldOptions { Items = [new FieldOption { Value = "good", Label = new LocalizedText("Good") }, new FieldOption { Value = "bad", Label = new LocalizedText("Bad") }] },
        Rows = [Speed, Price],
    };

    private static readonly FormField Order = new()
    {
        Key = Guid.Parse("c4100000-0000-4000-8000-000000000005"),
        Type = "ranking",
        Alias = "order",
        Label = new LocalizedText("Order"),
        Options = new FieldOptions
        {
            Items = [new FieldOption { Value = "a", Label = new LocalizedText("Apples") }, new FieldOption { Value = "b", Label = new LocalizedText("Bananas") }, new FieldOption { Value = "c", Label = new LocalizedText("Cherries") }],
        },
    };

    private static readonly FormField GuestName = new()
    {
        Key = Guid.Parse("c4100000-0000-4000-8000-000000000006"),
        Type = "text",
        Alias = "guestName",
        Label = new LocalizedText("Guest"),
    };

    private static readonly FormField Guests = new()
    {
        Key = Guid.Parse("c4100000-0000-4000-8000-000000000007"),
        Type = "repeater",
        Alias = "guests",
        Label = new LocalizedText("Guests"),
        Fields = [GuestName],
    };

    private static readonly FormField Salary = new()
    {
        Key = Guid.Parse("c4100000-0000-4000-8000-000000000008"),
        Type = "text",
        Alias = "salary",
        Label = new LocalizedText("Salary"),
        Sensitive = true,
    };

    private static readonly FormField Note = new()
    {
        Key = Guid.Parse("c4100000-0000-4000-8000-000000000009"),
        Type = "text",
        Alias = "note",
        Label = new LocalizedText("Note"),
    };

    private static readonly FormField Upload = new()
    {
        Key = Guid.Parse("c4100000-0000-4000-8000-00000000000a"),
        Type = "file",
        Alias = "upload",
        Label = new LocalizedText("Upload"),
    };

    private static readonly FormField Passport = Upload with { Key = Guid.Parse("c4100000-0000-4000-8000-00000000000b"), Alias = "passport", Label = new LocalizedText("Passport"), Sensitive = true };

    private static readonly FormField Visitor = new()
    {
        Key = Guid.Parse("c4100000-0000-4000-8000-00000000000c"),
        Type = "text",
        Alias = "visitor",
        Label = new LocalizedText("Visitor"),
    };

    private static readonly FormField Pin = new()
    {
        Key = Guid.Parse("c4100000-0000-4000-8000-00000000000d"),
        Type = "text",
        Alias = "pin",
        Label = new LocalizedText("PIN"),
        Sensitive = true,
    };

    private static readonly FormField Badges = new()
    {
        Key = Guid.Parse("c4100000-0000-4000-8000-00000000000e"),
        Type = "repeater",
        Alias = "badges",
        Label = new LocalizedText("Badges"),
        Fields = [Visitor, Pin],
    };

    private static readonly FormField Phone = new()
    {
        Key = Guid.Parse("c4100000-0000-4000-8000-00000000000f"),
        Type = "text",
        Alias = "phone",
        Label = new LocalizedText("Phone"),
    };

    private static readonly FormField Private = new()
    {
        Key = Guid.Parse("c4100000-0000-4000-8000-000000000010"),
        Type = "repeater",
        Alias = "private",
        Label = new LocalizedText("Private"),
        Sensitive = true,
        Fields = [Phone],
    };

    private static readonly EntryExporter Owner = new(Editor, MaySeeSensitive: true, "en-US");

    private static readonly string[] OwnHeaders = ["Entry", "Reference", "State", "Received", "Language"];

    private static readonly string Answers = $$"""{"name":"Ada","colours":["red","blue"],"rating.{{Speed.Key}}":"good","rating.{{Price.Key}}":"bad","order.b":"1","order.a":"2","order.c":"3","guests":[{"guestName":"Ben"},{"guestName":"Cy"}],"salary":"90000","note":"=SUM(A1)"}""";

    private EntryExports Exports => GetRequiredService<EntryExports>();

    private EntryExportJobs Jobs => GetRequiredService<EntryExportJobs>();

    private Heard Announced => GetRequiredService<Heard>();

    private MemoryFileStore Store => GetRequiredService<MemoryFileStore>();

    [Test]
    public async Task ACsvExport_ReadsBackAsItsEntry_WithGridsByRowRankingsByPositionAndGroupsAsLines()
    {
        var form = await FormAsync();
        var entry = await SubmitAsync(form, Answers);

        var bytes = await WriteAsync(form, new EntryExportRequest { Format = "csv" });
        var rows = ExportFiles.Csv(Encoding.UTF8.GetString(bytes.AsSpan(3)));

        Assert.Multiple(() =>
        {
            Assert.That(bytes.Take(3), Is.EqualTo(new byte[] { 0xEF, 0xBB, 0xBF }), "a byte-order mark by default");
            Assert.That(rows[0], Is.EqualTo(OwnHeaders.Concat(["Name", "Colours", "Rating: Speed", "Rating: Price", "Order: 1", "Order: 2", "Order: 3", "Note", "Upload", "Guests"])), "no sensitive field");
            Assert.That(rows, Has.Count.EqualTo(2));
            Assert.That(rows[1].Take(3), Is.EqualTo(new[] { entry.Number.ToString(System.Globalization.CultureInfo.InvariantCulture), $"2026-{entry.Number:00000}", "Approved" }));
            Assert.That(rows[1][3], Does.Match(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$"));
            Assert.That(rows[1].Skip(5), Is.EqualTo(new[] { "Ada", "Red, Blue", "Good", "Bad", "Bananas", "Apples", "Cherries", "'=SUM(A1)", string.Empty, "Guest: Ben\nGuest: Cy" }), "the formula guarded, a group's rows as lines");
        });
    }

    [Test]
    public async Task EachEntrysAnswers_AreWrittenInItsLanguage_WhichItsLanguageColumnNames_AndTheReceivedTimeStaysSortable()
    {
        var amount = new FormField { Key = Guid.Parse("c4100000-0000-4000-8000-0000000000a1"), Type = "number", Alias = "amount", Label = new LocalizedText("Amount") };
        var day = new FormField { Key = Guid.Parse("c4100000-0000-4000-8000-0000000000a2"), Type = "date", Alias = "day", Label = new LocalizedText("Day") };
        var form = await PublishAsync(name: "Languages export", fields: [NameField, amount, day]);
        var row = (await FormRows.GetAsync(form))!;
        var fields = (await FormRows.ListFieldsAsync(row.Id)).ToDictionary(field => field.Values.FieldKey, field => field.Id);
        foreach (var culture in new[] { "en-US", "de-DE" })
        {
            _ = await Entries.CreateAsync(new NewEntry
            {
                Key = Guid.NewGuid(),
                FormId = row.Id,
                FormVersionId = row.PublishedVersionId!.Value,
                State = EntryState.Submitted,
                SubmissionId = Guid.NewGuid(),
                SubmissionHash = new string('a', 64),
                Culture = culture,
                Values = [new NewEntryValue(fields[NameField.Key], "Ada"), new NewEntryValue(fields[amount.Key], "1234.5"), new NewEntryValue(fields[day.Key], "2026-03-01")],
            });
        }

        var rows = ExportFiles.Csv(Encoding.UTF8.GetString((await WriteAsync(form, new EntryExportRequest { Format = "csv" })).AsSpan(3)));
        var json = JsonNode.Parse(await WriteAsync(form, new EntryExportRequest { Format = "json" }))!.AsArray();
        var byLanguage = rows.Skip(1).ToDictionary(each => each[4]);

        Assert.Multiple(() =>
        {
            Assert.That(rows[0].Take(5), Is.EqualTo(OwnHeaders));
            Assert.That(byLanguage.Keys, Is.EquivalentTo(new[] { "en-US", "de-DE" }));
            Assert.That(byLanguage["en-US"].Skip(5), Is.EqualTo(new[] { "Ada", "1,234.5", "3/1/2026" }));
            Assert.That(byLanguage["de-DE"].Skip(5), Is.EqualTo(new[] { "Ada", "1.234,5", "01.03.2026" }));
            Assert.That(byLanguage.Values.Select(each => each[3]), Has.All.Match(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$"), "the same sortable time in every language");
            Assert.That(json.Select(each => each!["language"]!.GetValue<string>()), Is.EquivalentTo(new[] { "en-US", "de-DE" }));
        });
    }

    [Test]
    public async Task AnAnswerKeptOnlyUntilTheActionsRan_IsSaidNotKept_OnceItIsGone_InTheExportersLanguage()
    {
        var kept = Note with { Keep = FieldKeep.UntilActionsRun };
        var form = await PublishAsync(name: "Kept export", fields: [NameField, kept]);
        await SubmitAsync(form, """{"name":"Ada","note":"For the actions"}""");

        var english = ExportFiles.Csv(Encoding.UTF8.GetString((await WriteAsync(form, new EntryExportRequest { Format = "csv" })).AsSpan(3)));
        var german = ExportFiles.Csv(Encoding.UTF8.GetString((await WriteAsync(form, new EntryExportRequest { Format = "csv" }, new EntryExporter(Editor, MaySeeSensitive: false, Culture: "de-DE"))).AsSpan(3)));

        Assert.Multiple(() =>
        {
            Assert.That(english[1].Skip(5), Is.EqualTo(new[] { "Ada", "not kept" }), "no action could use it, so it was never stored");
            Assert.That(german[1].Skip(5), Is.EqualTo(new[] { "Ada", "nicht aufbewahrt" }));
        });
    }

    [Test]
    public async Task ACsvExport_TakesTheDelimiterTheAliasesAndNoByteOrderMark()
    {
        var form = await FormAsync();
        await SubmitAsync(form, """{"name":"Lovelace; Ada","note":"say \"hi\""}""");

        var bytes = await WriteAsync(form, new EntryExportRequest { Format = "csv", Options = new FormBuilderExportOptions { Delimiter = ';', Headers = FormBuilderExportHeaders.Aliases, ByteOrderMark = false } });
        var text = Encoding.UTF8.GetString(bytes);
        var rows = ExportFiles.Csv(text, ';');

        Assert.Multiple(() =>
        {
            Assert.That(bytes[0], Is.EqualTo((byte)'n'), "no byte-order mark");
            Assert.That(rows[0], Is.EqualTo(new[] { "number", "reference", "state", "received", "language", "name", "colours", "rating.1", "rating.2", "order.1", "order.2", "order.3", "note", "upload", "guests" }));
            Assert.That(rows[1][5], Is.EqualTo("Lovelace; Ada"), "a delimiter inside is quoted");
            Assert.That(rows[1][12], Is.EqualTo("say \"hi\""), "quotes are doubled");
        });
    }

    [Test]
    public async Task AnExcelExport_ReadsBack_WithARepeatableGroupOnASheetOfItsOwn()
    {
        var form = await FormAsync();
        var entry = await SubmitAsync(form, Answers);

        var sheets = await ExportFiles.XlsxAsync(await WriteAsync(form, new EntryExportRequest { Format = "xlsx" }));
        var number = entry.Number.ToString(System.Globalization.CultureInfo.InvariantCulture);

        Assert.Multiple(() =>
        {
            Assert.That(sheets.Select(sheet => sheet.Name), Is.EqualTo(new[] { "Export test", "Guests" }));
            Assert.That(sheets[0].Rows[0], Is.EqualTo(OwnHeaders.Concat(["Name", "Colours", "Rating: Speed", "Rating: Price", "Order: 1", "Order: 2", "Order: 3", "Note", "Upload"])));
            Assert.That(sheets[0].Rows[1][0], Is.EqualTo(number), "the number as a number");
            Assert.That(sheets[0].Rows[1].Skip(5), Is.EqualTo(new[] { "Ada", "Red, Blue", "Good", "Bad", "Bananas", "Apples", "Cherries", "=SUM(A1)" }), "an inline string is never a formula");
            Assert.That(sheets[1].Rows, Is.EqualTo(new[] { new[] { "Entry", "#", "Guest" }, new[] { number, "1", "Ben" }, new[] { number, "2", "Cy" } }));
        });
    }

    [Test]
    public async Task AJsonExport_NamesTheAnswersByAlias()
    {
        var form = await FormAsync();
        var entry = await SubmitAsync(form, Answers);

        var json = JsonNode.Parse(await WriteAsync(form, new EntryExportRequest { Format = "json" }))!.AsArray();
        var answers = json[0]!["answers"]!;

        Assert.Multiple(() =>
        {
            Assert.That(json, Has.Count.EqualTo(1));
            Assert.That(json[0]!["number"]!.GetValue<long>(), Is.EqualTo(entry.Number));
            Assert.That(json[0]!["reference"]!.GetValue<string>(), Is.EqualTo($"2026-{entry.Number:00000}"));
            Assert.That(json[0]!["received"]!.GetValue<string>(), Does.Match(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z$"));
            Assert.That(answers.AsObject().Select(pair => pair.Key), Is.EqualTo(new[] { "name", "colours", "rating", "order", "note", "guests" }));
            Assert.That(answers["colours"]!.AsArray().Select(value => value!.GetValue<string>()), Is.EqualTo(new[] { "Red", "Blue" }));
            Assert.That(answers["rating"]!.ToJsonString(), Is.EqualTo("""{"Speed":"Good","Price":"Bad"}"""));
            Assert.That(answers["order"]!.AsArray().Select(value => value!.GetValue<string>()), Is.EqualTo(new[] { "Bananas", "Apples", "Cherries" }));
            Assert.That(answers["note"]!.GetValue<string>(), Is.EqualTo("=SUM(A1)"), "JSON is no spreadsheet, so no guard");
            Assert.That(answers["guests"]!.ToJsonString(), Is.EqualTo("""[{"guestName":"Ben"},{"guestName":"Cy"}]"""));
        });
    }

    [Test]
    public async Task APrintableView_IsOneDocumentWithoutScripts_ItsTextsEscaped()
    {
        var form = await FormAsync();
        await SubmitAsync(form, """{"name":"<script>alert(1)</script>"}""");
        await SubmitAsync(form, """{"name":"Grace"}""");

        var html = Encoding.UTF8.GetString(await WriteAsync(form, new EntryExportRequest { Format = "print" }));

        Assert.Multiple(() =>
        {
            Assert.That(html, Does.StartWith("<!DOCTYPE html><html lang=\"en-US\">"));
            Assert.That(html, Does.Not.Contain("<script"), "the answer is text");
            Assert.That(html, Does.Contain("&lt;script&gt;alert(1)&lt;/script&gt;"));
            Assert.That(html.Split("<section>").Length - 1, Is.EqualTo(2), "a section per entry");
            Assert.That(html, Does.Contain("@media print"));
        });
    }

    [Test]
    public async Task TheZipOfFiles_HasAFolderPerEntry_AndLeavesOutSensitiveAndQuarantinedFiles()
    {
        var form = await FormAsync();
        var row = (await FormRows.GetAsync(form))!;
        var first = await EntryWithFilesAsync(row, ("report.pdf", Upload, FileScanStates.Clean), ("report.pdf", Upload, FileScanStates.Clean), ("passport.pdf", Passport, FileScanStates.Clean));
        var second = await EntryWithFilesAsync(row, ("held.pdf", Upload, FileScanStates.Quarantined), ("photo.png", Upload, FileScanStates.Clean));

        var plain = await ExportFiles.ZipAsync(await WriteAsync(form, new EntryExportRequest { Format = "files" }));
        var all = await ExportFiles.ZipAsync(await WriteAsync(form, new EntryExportRequest { Format = "files", Sensitive = true }));

        Assert.Multiple(() =>
        {
            Assert.That(plain, Is.EqualTo(new[] { $"entry-{second}/photo.png", $"entry-{first}/report.pdf", $"entry-{first}/report (2).pdf" }), "newest first, safe and unique names, no sensitive or quarantined file");
            Assert.That(all, Does.Contain($"entry-{first}/passport.pdf"), "a sensitive file when asked for and allowed");
            Assert.That(all, Does.Not.Contain($"entry-{second}/held.pdf"));
        });
    }

    [Test]
    public async Task SensitiveFields_GoAlong_OnlyWhenAskedForAndAllowed()
    {
        var form = await FormAsync();
        await SubmitAsync(form, Answers);

        var left = ExportFiles.Csv(Encoding.UTF8.GetString(await WriteAsync(form, new EntryExportRequest { Format = "csv", Options = NoMark })))[0];
        var asked = ExportFiles.Csv(Encoding.UTF8.GetString(await WriteAsync(form, new EntryExportRequest { Format = "csv", Sensitive = true, Options = NoMark })));
        var refused = Assert.ThrowsAsync<FormBuilderValidationException>(() => WriteAsync(form, new EntryExportRequest { Format = "csv", Sensitive = true }, Owner with { MaySeeSensitive = false }));

        Assert.Multiple(() =>
        {
            Assert.That(left, Does.Not.Contain("Salary"));
            Assert.That(asked[0], Does.Contain("Salary"));
            Assert.That(asked[1], Does.Contain("90000"));
            Assert.That(refused!.Problems.Select(problem => (problem.Code, problem.Path)), Is.EqualTo(new[] { ("sensitive_not_allowed", "sensitive") }));
        });
    }

    [Test]
    public async Task AnExport_HoldsWhatTheListFilters_OrTheTickedEntries_AndOnlyTheChosenFields()
    {
        var form = await FormAsync();
        var ada = await SubmitAsync(form, """{"name":"Ada"}""");
        var ben = await SubmitAsync(form, """{"name":"Ben"}""");
        var cy = await SubmitAsync(form, """{"name":"Cy"}""");
        _ = await Entries.ChangeStateAsync(ben.Id, EntryState.Approved, EntryState.Rejected, Editor);
        await StoreTestEntryAsync(form);

        var all = await NamesAsync(form, new EntryExportRequest { Format = "csv", Options = NoMark });
        var rejected = await NamesAsync(form, new EntryExportRequest { Format = "csv", Options = NoMark, Filter = new EntryListFilter { States = [EntryState.Rejected] } });
        var searched = await NamesAsync(form, new EntryExportRequest { Format = "csv", Options = NoMark, Filter = new EntryListFilter { Search = "cy" } });
        var ticked = await NamesAsync(form, new EntryExportRequest { Format = "csv", Options = NoMark, Entries = [ada.Key, cy.Key, ada.Key], Filter = new EntryListFilter { States = [EntryState.Rejected] } });
        var chosen = ExportFiles.Csv(Encoding.UTF8.GetString(await WriteAsync(form, new EntryExportRequest { Format = "csv", Options = NoMark, Fields = [NameField.Key] })))[0];

        Assert.Multiple(() =>
        {
            Assert.That(all, Is.EqualTo(new[] { "Cy", "Ben", "Ada" }), "newest first, no test entry");
            Assert.That(rejected, Is.EqualTo(new[] { "Ben" }));
            Assert.That(searched, Is.EqualTo(new[] { "Cy" }));
            Assert.That(ticked, Is.EqualTo(new[] { "Cy", "Ada" }), "the ticked entries, each once, whatever the filter says");
            Assert.That(chosen, Is.EqualTo(OwnHeaders.Append("Name")));
        });
    }

    [Test]
    public async Task AnExportThatChoosesNoState_LeavesOutTheEntriesWaitingForTheirEmailConfirmation_AndItsPlanCountsThem()
    {
        var form = await FormAsync();
        await SubmitAsync(form, """{"name":"Ada"}""");
        var waiting = await StoreEntryAsync(form, EntryState.Unverified);
        var chosen = new EntryExportRequest { Format = "csv", Options = NoMark, Filter = new EntryListFilter { States = [EntryState.Unverified] } };

        var all = await NamesAsync(form, new EntryExportRequest { Format = "csv", Options = NoMark });
        var plan = await Exports.PlanAsync(form, new EntryExportRequest { Format = "csv" }, Owner);
        var unconfirmed = await NamesAsync(form, chosen);
        var chosenPlan = await Exports.PlanAsync(form, chosen, Owner);
        var ticked = await Exports.PlanAsync(form, new EntryExportRequest { Format = "csv", Entries = [waiting] }, Owner);

        Assert.Multiple(() =>
        {
            Assert.That(all, Is.EqualTo(new[] { "Ada" }), "no address nobody has confirmed goes into a file taken for a mailing (ADR-0226)");
            Assert.That(plan, Is.EqualTo(new EntryExportPlan(1, More: false, Background: false, Unconfirmed: 1)), "and the dialog says how many stay out");
            Assert.That(unconfirmed, Is.EqualTo(new[] { "" }), "choosing their state exports them");
            Assert.That(chosenPlan, Is.EqualTo(new EntryExportPlan(1, More: false, Background: false)));
            Assert.That(ticked, Is.EqualTo(new EntryExportPlan(1, More: false, Background: false)), "as does ticking one");
        });
    }

    [Test]
    public async Task TheTickedEntries_AreTheFormsOwnOnly_NeverATestEntryOrADraft()
    {
        var form = await FormAsync();
        var other = await PublishAsync(name: "Another form");
        var ada = await SubmitAsync(form, """{"name":"Ada"}""");
        var theirs = await SubmitAsync(other, """{"name":"Eve"}""");
        var test = await StoreTestEntryAsync(form);
        var draft = await StoreEntryAsync(form, EntryState.Draft);

        var names = await NamesAsync(form, new EntryExportRequest { Format = "csv", Options = NoMark, Entries = [ada.Key, theirs.Key, test, draft] });
        var plan = await Exports.PlanAsync(form, new EntryExportRequest { Format = "csv", Entries = [theirs.Key, test, draft] }, Owner);

        Assert.Multiple(() =>
        {
            Assert.That(names, Is.EqualTo(new[] { "Ada" }), "another form's entry, a test entry and a draft never go along, though ticked");
            Assert.That(plan, Is.EqualTo(new EntryExportPlan(0, More: false, Background: false)));
        });
    }

    [Test]
    public async Task ASensitiveFieldOfARepeatableGroup_AndASensitiveGroup_GoAlong_OnlyWhenAskedForAndAllowed()
    {
        var form = await PublishAsync(name: "Badges", fields: [NameField, Badges, Private]);
        await SubmitAsync(form, """{"name":"Ada","badges":[{"visitor":"Ben","pin":"1234"}],"private":[{"phone":"555"}]}""");

        var left = ExportFiles.Csv(Encoding.UTF8.GetString(await WriteAsync(form, new EntryExportRequest { Format = "csv", Options = NoMark })));
        var asked = ExportFiles.Csv(Encoding.UTF8.GetString(await WriteAsync(form, new EntryExportRequest { Format = "csv", Sensitive = true, Options = NoMark })));
        var json = JsonNode.Parse(await WriteAsync(form, new EntryExportRequest { Format = "json" }))!.AsArray()[0]!["answers"]!;

        Assert.Multiple(() =>
        {
            Assert.That(left[0].Skip(5), Is.EqualTo(new[] { "Name", "Badges" }), "a sensitive group stays out");
            Assert.That(left[1].Skip(5), Is.EqualTo(new[] { "Ada", "Visitor: Ben" }), "and so does a group's sensitive field");
            Assert.That(json.ToJsonString(), Is.EqualTo("""{"name":"Ada","badges":[{"visitor":"Ben"}]}"""));
            Assert.That(asked[0].Skip(5), Is.EqualTo(new[] { "Name", "Badges", "Private" }));
            Assert.That(asked[1].Skip(5), Is.EqualTo(new[] { "Ada", "Visitor: Ben; PIN: 1234", "Phone: 555" }), "both, when asked for and allowed");
        });
    }

    [Test]
    public async Task AFieldTheCatalogueMarksSensitive_StaysOut_AsOneItsVersionMarks_InAGroupToo()
    {
        var plusOne = GuestName with { Key = Guid.Parse("c4100000-0000-4000-8000-000000000011"), Alias = "plusOne", Label = new LocalizedText("Plus one") };
        var form = await PublishAsync(name: "Marked", fields: [NameField, Note, Guests with { Fields = [GuestName, plusOne] }]);
        await SubmitAsync(form, """{"name":"Ada","note":"kept","guests":[{"guestName":"Ben","plusOne":"Cy"}]}""");
        // The catalogue marks the note and a group's field sensitive, though the version they were answered in does not.
        var row = (await FormRows.GetAsync(form))!;
        _ = await GetRequiredService<FormBuilderDatabase>().WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                await db.ExecAsync($"UPDATE {n.T(FormBuilderTables.FormField)} SET {n.C("isSensitive")} = @0 WHERE {n.C("formId")} = @1 AND {n.C("fieldKey")} IN (@2, @3)", true, row.Id, Note.Key, plusOne.Key);
                return true;
            });

        var left = ExportFiles.Csv(Encoding.UTF8.GetString(await WriteAsync(form, new EntryExportRequest { Format = "csv", Options = NoMark })));
        var asked = ExportFiles.Csv(Encoding.UTF8.GetString(await WriteAsync(form, new EntryExportRequest { Format = "csv", Sensitive = true, Options = NoMark })));

        Assert.Multiple(() =>
        {
            Assert.That(left[0].Skip(5), Is.EqualTo(new[] { "Name", "Guests" }), "the note stays out");
            Assert.That(left[1].Skip(5), Is.EqualTo(new[] { "Ada", "Guest: Ben" }), "and so does the group's plus one");
            Assert.That(asked[0].Skip(5), Is.EqualTo(new[] { "Name", "Note", "Guests" }));
            Assert.That(asked[1].Skip(5), Is.EqualTo(new[] { "Ada", "kept", "Guest: Ben; Plus one: Cy" }));
        });
    }

    [Test]
    public async Task AnUnpublishedForm_ExportsAsItWasLastPublished_NotAsItsDraft()
    {
        var form = await PublishAsync(name: "Offline", fields: [NameField, Note, Badges]);
        await SubmitAsync(form, """{"name":"Ada","note":"kept","badges":[{"visitor":"Ben","pin":"1234"}]}""");
        _ = (await Forms.UnpublishAsync(form, (await Forms.GetAsync(form))!.ConcurrencyVersion, Editor))!;
        var draft = (await Forms.GetDraftAsync(form))!;
        // The draft drops the note and no longer marks the PIN sensitive; neither was ever published so.
        var page = draft.Definition!.Pages[0];
        var changed = draft.Definition with { Pages = [page with { Groups = [page.Groups[0] with { Fields = [NameField, Badges with { Fields = [Visitor, Pin with { Sensitive = false }] }] }] }] };
        _ = (await Forms.SaveDraftAsync(form, draft.Form.ConcurrencyVersion, changed, null, Editor))!;

        var rows = ExportFiles.Csv(Encoding.UTF8.GetString(await WriteAsync(form, new EntryExportRequest { Format = "csv", Options = NoMark })));

        Assert.Multiple(() =>
        {
            Assert.That(rows[0].Skip(5), Is.EqualTo(new[] { "Name", "Note", "Badges" }), "the note its entries answered stays");
            Assert.That(rows[1].Skip(5), Is.EqualTo(new[] { "Ada", "kept", "Visitor: Ben" }), "the PIN is as sensitive as when it was answered");
        });
    }

    [Test]
    public async Task TheReference_AndTheTimeReceived_AreTheOnesTheEntrysVersionAndSiteGave()
    {
        var form = await PublishAsync(name: "References");
        var settings = GetRequiredService<IFormBuilderSettingsService>();
        var shared = await settings.GetAsync<GeneralSettings>();
        await settings.SaveAsync(shared.Value with { TimeZone = "Europe/Berlin" }, shared.Version, Editor);
        var (shop, contact) = await SiteAsync();
        await settings.SaveSiteAsync(shop, new GeneralSettings { TimeZone = "Pacific/Auckland" }, ["timeZone"], 0, Editor);
        Clock.SetUtcNow(new DateTimeOffset(2026, 12, 31, 11, 0, 0, TimeSpan.Zero));
        var onTheSite = await StoreEntryAsync(form, contentKey: contact);
        var elsewhere = await StoreEntryAsync(form);
        await SaveAndPublishAsync(form, definition => definition with { Settings = definition.Settings with { Submission = definition.Settings.Submission with { EntryReference = new EntryReferenceFormat { Prefix = "REQ-", Digits = 3 } } } });
        var later = await StoreEntryAsync(form);

        var rows = ExportFiles.Csv(Encoding.UTF8.GetString(await WriteAsync(form, new EntryExportRequest { Format = "csv", Options = NoMark })));
        var numbers = new Dictionary<Guid, long>();
        foreach (var key in new[] { onTheSite, elsewhere, later })
        {
            numbers[key] = (await Entries.GetAsync(key))!.Number;
        }

        Assert.That(rows.Skip(1).Select(row => (row[1], row[3])), Is.EqualTo(new[]
        {
            ($"REQ-2026-{numbers[later]:000}", "2026-12-31 12:00"),
            ($"2026-{numbers[elsewhere]:00000}", "2026-12-31 12:00"),
            ($"2027-{numbers[onTheSite]:00000}", "2027-01-01 00:00"),
        }), "each in its version's format, and its site's year and time, as its receipt gave them");
    }

    [Test]
    public async Task AnExport_IsAuditedAndAnnounced()
    {
        var form = await FormAsync();
        await SubmitAsync(form);
        await SubmitAsync(form);

        _ = await WriteAsync(form, new EntryExportRequest { Format = "json", Fields = [NameField.Key, Note.Key] });
        var audited = (await GetRequiredService<IAuditRepository>().QueryAsync(new AuditQuery { Action = EntryExports.ExportedAction }, PageRequest.First(10))).Items;

        Assert.Multiple(() =>
        {
            Assert.That(audited, Has.Count.EqualTo(1));
            Assert.That(audited[0].EntityKey, Is.EqualTo(form));
            Assert.That(audited[0].UserKey, Is.EqualTo(Editor));
            Assert.That(audited[0].Details, Does.Contain("\"format\":\"json\"").And.Contain("\"entries\":2").And.Contain("\"fields\":2").And.Contain("\"finished\":true").And.Contain("\"sensitive\":false"));
            Assert.That(Announced.Exports.Select(heard => (heard.FormKey, heard.Format, heard.Entries, heard.UserKey)), Is.EqualTo(new[] { (form, "json", 2, Editor) }));
        });
    }

    [Test]
    public async Task WhatAnExportCannotTake_IsRefusedByItsPath()
    {
        var form = await FormAsync();

        var refused = Assert.ThrowsAsync<FormBuilderValidationException>(() => Exports.PlanAsync(form, new EntryExportRequest
        {
            Format = "pdf",
            Fields = [NameField.Key, Guid.NewGuid()],
            Entries = [.. Enumerable.Range(0, EntryExports.MaxSelected + 1).Select(_ => Guid.NewGuid())],
            Options = new FormBuilderExportOptions { Delimiter = '|' },
        }, Owner));

        Assert.Multiple(async () =>
        {
            Assert.That(refused!.Problems.Select(problem => (problem.Code, problem.Path)), Is.EquivalentTo(new[] { ("export_format_unknown", "format"), ("invalid_value", "options.delimiter"), ("entries_invalid", "entries"), ("field_not_found", "fields[1]") }));
            Assert.That(await Exports.PlanAsync(Guid.NewGuid(), new EntryExportRequest { Format = "csv" }, Owner), Is.Null, "a form that does not exist");
        });
    }

    [Test]
    public async Task ALargeExport_RunsInTheBackground_ForItsOwnerOnly_UntilItExpires()
    {
        var form = await FormAsync();
        await SubmitAsync(form, """{"name":"Ada"}""");
        await SubmitAsync(form, """{"name":"Ben"}""");
        var request = new EntryExportRequest { Format = "csv", Options = NoMark };

        var queued = await Jobs.QueueAsync(form, request, Owner, new EntryExportPlan(2, More: false, Background: true));
        await Jobs.PassAsync("test", CancellationToken.None);
        var ready = await Jobs.GetAsync(form, queued.Key, Editor);
        var opened = await Jobs.OpenAsync(form, queued.Key, Editor);
        using var read = new StreamReader(opened!.Content);
        var rows = ExportFiles.Csv(await read.ReadToEndAsync());
        var someoneElse = await Jobs.OpenAsync(form, queued.Key, Guid.NewGuid());
        var otherForm = await Jobs.GetAsync(Guid.NewGuid(), queued.Key, Editor);

        Clock.Advance(EntryExportJobs.KeptFor - TimeSpan.FromMinutes(1));
        var kept = await Jobs.CleanUpAsync();
        Clock.Advance(TimeSpan.FromMinutes(2));
        var removed = await Jobs.CleanUpAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(queued.State, Is.EqualTo(EntryExportState.Queued));
            Assert.That(ready!.State, Is.EqualTo(EntryExportState.Ready));
            Assert.That(ready.Done, Is.EqualTo(2));
            Assert.That(ready.FileName, Does.EndWith("-entries.csv"));
            Assert.That(rows.Skip(1).Select(row => row[5]), Is.EqualTo(new[] { "Ben", "Ada" }));
            Assert.That(someoneElse, Is.Null, "only its owner downloads it");
            Assert.That(otherForm, Is.Null, "only under its own form");
            Assert.That(Announced.Exports, Has.Count.EqualTo(1), "announced once its file is kept");
            Assert.That(kept, Is.Zero, "kept for a day");
            Assert.That(removed, Is.EqualTo(1));
            Assert.That(await Jobs.GetAsync(form, queued.Key, Editor), Is.Null);
            Assert.That(await GetRequiredService<IFormBuilderFileStore>().OpenReadAsync(ready.File!), Is.Null, "its file is gone too");
        });
    }

    [Test]
    public async Task TheExportsOfAFormDeletedForGood_GoAtTheNextCleanUp_WithTheirFiles()
    {
        var form = await FormAsync();
        var kept = await PublishAsync(name: "Kept");
        await SubmitAsync(form);
        await SubmitAsync(kept);
        var request = new EntryExportRequest { Format = "csv" };
        var plan = new EntryExportPlan(1, More: false, Background: true);
        var gone = await Jobs.QueueAsync(form, request, Owner, plan);
        var stays = await Jobs.QueueAsync(kept, request, Owner, plan);
        await Jobs.PassAsync("test", CancellationToken.None);
        var file = (await Jobs.GetAsync(form, gone.Key, Editor))!.File!;
        var trashed = (await Forms.TrashAsync(form, (await Forms.GetAsync(form))!.ConcurrencyVersion, Editor))!;
        var inTrash = await Jobs.CleanUpAsync();
        _ = await Forms.DeleteAsync(form, trashed.ConcurrencyVersion, Editor);

        var removed = await Jobs.CleanUpAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(inTrash, Is.Zero, "a form in the trash keeps them, as it may come back");
            Assert.That(removed, Is.EqualTo(1));
            Assert.That(await GetRequiredService<IKeyValueRepository>().GetAsync(EntryExportJobs.KeyOf(gone.Key)), Is.Null);
            Assert.That(await Store.OpenReadAsync(file), Is.Null, "its file, which holds the entries, goes with it");
            Assert.That((await Jobs.GetAsync(kept, stays.Key, Editor))?.State, Is.EqualTo(EntryExportState.Ready), "another form's stays");
        });
    }

    [Test]
    public async Task AnExportWhoseServerStopped_StartsAgain_OnceItsLeaseRanOut()
    {
        var form = await FormAsync();
        await SubmitAsync(form);
        var key = Guid.NewGuid();
        var running = new EntryExportRecord
        {
            Key = key,
            Owner = Editor,
            Form = form,
            Request = new EntryExportRequest { Format = "csv" },
            Culture = "en-US",
            State = EntryExportState.Running,
            Attempts = 1,
            Claimant = "stopped/server",
            LeaseUtc = Clock.GetUtcNow() + TimeSpan.FromMinutes(1),
            QueuedUtc = Clock.GetUtcNow(),
        };
        await GetRequiredService<IKeyValueRepository>().SetAsync(EntryExportJobs.KeyOf(key), EntryExportJobs.Write(running));

        await Jobs.PassAsync("test", CancellationToken.None);
        var held = await Jobs.GetAsync(form, key, Editor);
        Clock.Advance(TimeSpan.FromMinutes(2));
        await Jobs.PassAsync("test", CancellationToken.None);
        var again = await Jobs.GetAsync(form, key, Editor);

        Assert.Multiple(() =>
        {
            Assert.That(held!.State, Is.EqualTo(EntryExportState.Running), "its lease still holds");
            Assert.That(again!.State, Is.EqualTo(EntryExportState.Ready));
            Assert.That(again.Attempts, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task AnExportThatKeepsFailing_IsTriedThreeTimes_AndThenFails()
    {
        var form = await FormAsync();
        await SubmitAsync(form);
        var queued = await Jobs.QueueAsync(form, new EntryExportRequest { Format = BrokenFormat.FormatName }, Owner, new EntryExportPlan(1, More: false, Background: true));

        var states = new List<(EntryExportState, int)>();
        for (var pass = 0; pass < 4; pass++)
        {
            await Jobs.PassAsync("test", CancellationToken.None);
            var record = (await Jobs.GetAsync(form, queued.Key, Editor))!;
            states.Add((record.State, record.Attempts));
        }

        var audited = (await GetRequiredService<IAuditRepository>().QueryAsync(new AuditQuery { Action = EntryExports.ExportedAction }, PageRequest.First(10))).Items;

        Assert.Multiple(async () =>
        {
            Assert.That(states, Is.EqualTo(new[] { (EntryExportState.Queued, 1), (EntryExportState.Queued, 2), (EntryExportState.Failed, 3), (EntryExportState.Failed, 3) }));
            Assert.That((await Jobs.GetAsync(form, queued.Key, Editor))!.Problem, Is.EqualTo("export_failed"));
            Assert.That(audited, Has.Count.EqualTo(3), "each attempt is audited");
            Assert.That(audited.All(entry => entry.Details.Contains("\"finished\":false", StringComparison.Ordinal)), Is.True);
            Assert.That(Announced.Exports, Is.Empty);
        });
    }

    [Test]
    public async Task AStoreThatTimesOut_CountsAsAFailedAttempt_AndWhatItStoredGoes()
    {
        var form = await FormAsync();
        await SubmitAsync(form);
        Store.AfterSave = () => new TaskCanceledException("The store timed out.");
        var queued = await Jobs.QueueAsync(form, new EntryExportRequest { Format = "csv" }, Owner, new EntryExportPlan(1, More: false, Background: true));

        await Jobs.PassAsync("test", CancellationToken.None);
        var again = (await Jobs.GetAsync(form, queued.Key, Editor))!;

        Assert.Multiple(() =>
        {
            Assert.That((again.State, again.Attempts, again.Claimant), Is.EqualTo((EntryExportState.Queued, 1, (string?)null)), "tried again, as any failure");
            Assert.That(Store.Ids, Is.Empty, "the file the store half kept goes");
        });
    }

    [Test]
    public async Task AHostThatStopsWhileTheFileIsStored_LeavesNoFileBehind_AndTheExportToItsLease()
    {
        var form = await FormAsync();
        await SubmitAsync(form);
        using var stopping = new CancellationTokenSource();
        Store.AfterSave = () =>
        {
            stopping.Cancel();
            return new OperationCanceledException(stopping.Token);
        };
        var queued = await Jobs.QueueAsync(form, new EntryExportRequest { Format = "csv" }, Owner, new EntryExportPlan(1, More: false, Background: true));

        var stopped = Assert.CatchAsync<OperationCanceledException>(() => Jobs.PassAsync("test", stopping.Token));
        var held = (await Jobs.GetAsync(form, queued.Key, Editor))!;

        Assert.Multiple(() =>
        {
            Assert.That(stopped, Is.Not.Null, "the host's stop goes on");
            Assert.That((held.State, held.Attempts, held.Claimant), Is.EqualTo((EntryExportState.Running, 1, "test")), "another server takes it over once the lease ran out");
            Assert.That(Store.Ids, Is.Empty, "no record names the file, so it goes");
        });
    }

    [Test]
    public async Task AWorker_RenewsItsLeaseWhileAPageTakesLong_SoNoOtherServerTakesTheExportOver()
    {
        var form = await FormAsync();
        await SubmitAsync(form);
        var gate = GetRequiredService<SlowGate>();
        var queued = await Jobs.QueueAsync(form, new EntryExportRequest { Format = SlowFormat.FormatName }, Owner, new EntryExportPlan(1, More: false, Background: true));

        var running = Jobs.PassAsync("this", CancellationToken.None);
        var claimed = await LeasedAsync(form, queued.Key, after: DateTimeOffset.MinValue);
        Clock.Advance(EntryExportJobs.Lease + TimeSpan.FromMinutes(1));
        var renewed = await LeasedAsync(form, queued.Key, after: Clock.GetUtcNow());
        await Jobs.PassAsync("other", CancellationToken.None);
        gate.Open.SetResult();
        await running;
        var ready = (await Jobs.GetAsync(form, queued.Key, Editor))!;

        Assert.Multiple(() =>
        {
            Assert.That(claimed.Claimant, Is.EqualTo("this"));
            Assert.That(renewed.Claimant, Is.EqualTo("this"), "past the lease it was claimed with, it still holds");
            Assert.That((ready.State, ready.Attempts), Is.EqualTo((EntryExportState.Ready, 1)), "no other server started it again");
        });
    }

    [Test]
    public async Task AUser_HasAtMostThreeExportsUnderWay()
    {
        var form = await FormAsync();
        var plan = new EntryExportPlan(1, More: false, Background: true);
        for (var export = 0; export < EntryExportJobs.MaxUnderWay; export++)
        {
            _ = await Jobs.QueueAsync(form, new EntryExportRequest { Format = "csv" }, Owner, plan);
        }

        var refused = Assert.ThrowsAsync<FormBuilderValidationException>(() => Jobs.QueueAsync(form, new EntryExportRequest { Format = "csv" }, Owner, plan));
        var someoneElse = await Jobs.QueueAsync(form, new EntryExportRequest { Format = "csv" }, Owner with { UserKey = Guid.NewGuid() }, plan);

        Assert.Multiple(() =>
        {
            Assert.That(refused!.Problems.Single().Code, Is.EqualTo("exports_busy"));
            Assert.That(someoneElse.State, Is.EqualTo(EntryExportState.Queued), "each user's own");
        });
    }

    [Test]
    public async Task TenThousandEntriesOfTwelveValues_ExportAsCsvWithinFiveSeconds_WhileSubmissionsArrive_AndOneMoreGoesToTheBackground()
    {
        const int Count = 10_000;
        const int Bursts = 10;
        const int PerBurst = 5;
        FormField[] fields = [NameField, Note, .. Enumerable.Range(1, 10).Select(index => new FormField
        {
            Key = Guid.Parse($"c4100000-0000-4000-8000-0000000001{index:00}"),
            Type = "text",
            Alias = $"extra{index}",
            Label = new LocalizedText($"Extra {index}"),
        })];
        var form = await PublishAsync(name: "Budget", fields: fields);
        await SeedAsync(form, Count);
        var request = new EntryExportRequest { Format = "csv", Options = NoMark };
        var row = (await FormRows.GetAsync(form))!;
        var name = (await FormRows.ListFieldsAsync(row.Id)).Single(field => field.Values.FieldKey == NameField.Key).Id;

        // As ADR-0003 measured: five submissions every 100 ms while the export runs, each timed.
        var plan = await Exports.PlanAsync(form, request, Owner);
        var times = new ConcurrentBag<TimeSpan>();
        var arriving = Task.Run(async () =>
        {
            for (var burst = 0; burst < Bursts; burst++)
            {
                await Task.WhenAll(Enumerable.Range(0, PerBurst).Select(async arrival =>
                {
                    var took = Stopwatch.StartNew();
                    _ = await Entries.CreateAsync(new NewEntry
                    {
                        Key = Guid.NewGuid(),
                        FormId = row.Id,
                        FormVersionId = row.PublishedVersionId!.Value,
                        SubmissionId = Guid.NewGuid(),
                        SubmissionHash = new string('a', 64),
                        Culture = "en-US",
                        Values = [new NewEntryValue(name, "Arriving")],
                    });
                    times.Add(took.Elapsed);
                }));
                await Task.Delay(100);
            }
        });
        var watch = Stopwatch.StartNew();
        await using var output = new CountingStream();
        var file = await Exports.WriteAsync(form, request, Owner, output);
        var elapsed = watch.Elapsed;
        await arriving;
        var p95 = times.Order().ElementAt((int)Math.Ceiling(times.Count * 0.95) - 1);
        await TestContext.Out.WriteLineAsync($"10,000 entries of 12 values as CSV: {elapsed.TotalMilliseconds:0} ms, {output.Length:N0} bytes; {times.Count} submissions meanwhile, p95 {p95.TotalMilliseconds:0} ms");
        var larger = await Exports.PlanAsync(form, request, Owner);

        Assert.Multiple(() =>
        {
            Assert.That(plan, Is.EqualTo(new EntryExportPlan(Count, More: false, Background: false)));
            Assert.That(file!.Entries, Is.InRange(Count, Count + (Bursts * PerBurst)), "what had arrived before its first page, and nothing twice");
            Assert.That(output.Lines, Is.EqualTo(file.Entries + 1), "a header and a line per entry");
            Assert.That(elapsed, Is.LessThanOrEqualTo(TimeSpan.FromSeconds(5)));
            Assert.That(times, Has.Count.EqualTo(Bursts * PerBurst));
            Assert.That(p95, Is.LessThanOrEqualTo(TimeSpan.FromMilliseconds(150)), "a submission waits behind one page at most");
            Assert.That(larger!.Background, Is.True);
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.Services.RemoveAll(descriptor => descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationType == typeof(EntryExportWorker));
        builder.Services.AddSingleton<Heard>();
        builder.Services.AddSingleton<SlowGate>();
        builder.Services.AddSingleton<MemoryFileStore>();
        builder.Services.AddUnique<IFormBuilderFileStore>(provider => provider.GetRequiredService<MemoryFileStore>());
        builder.Services.AddUnique(new EntryExportTiming(TimeSpan.FromMilliseconds(50)));
        builder.AddNotificationAsyncHandler<ExportCompletedNotification, HeardHandler>();
        builder.JironFormBuilder().ExportFormats().Append<BrokenFormat>().Append<SlowFormat>();
    }

    private static FormBuilderExportOptions NoMark { get; } = new() { ByteOrderMark = false };

    private Task<Guid> FormAsync() => PublishAsync(name: "Export test", fields: [NameField, Colours, Rating, Order, Guests, Salary, Note, Upload, Passport]);

    private async Task<byte[]> WriteAsync(Guid form, EntryExportRequest request, EntryExporter? exporter = null)
    {
        using var output = new MemoryStream();
        _ = await Exports.WriteAsync(form, request, exporter ?? Owner, output);
        return output.ToArray();
    }

    // The names a CSV export holds, in its order.
    private async Task<IReadOnlyList<string>> NamesAsync(Guid form, EntryExportRequest request) =>
        [.. ExportFiles.Csv(Encoding.UTF8.GetString(await WriteAsync(form, request))).Skip(1).Select(row => row[5])];

    private Task<Guid> StoreTestEntryAsync(Guid form) => StoreEntryAsync(form, isTest: true);

    // An entry of the form's live version, as stored; its key.
    private async Task<Guid> StoreEntryAsync(Guid form, EntryState state = EntryState.Submitted, bool isTest = false, Guid? contentKey = null)
    {
        var row = (await FormRows.GetAsync(form))!;
        var key = Guid.NewGuid();
        _ = await Entries.CreateAsync(new NewEntry
        {
            Key = key,
            FormId = row.Id,
            FormVersionId = row.PublishedVersionId!.Value,
            State = state,
            IsTest = isTest,
            SubmissionId = Guid.NewGuid(),
            SubmissionHash = new string('a', 64),
            Culture = "en-US",
            ContentKey = contentKey,
        });
        return key;
    }

    // A site of its own, published: its root and a page on it.
    private async Task<(Guid Root, Guid Page)> SiteAsync()
    {
        var types = GetRequiredService<IContentTypeService>();
        var contents = GetRequiredService<IContentService>();
        var type = new ContentType(GetRequiredService<IShortStringHelper>(), -1) { Alias = "jfbExportPage", Name = "Page", AllowedAsRoot = true };
        Assert.That((await types.CreateAsync(type, Constants.Security.SuperUserKey)).Success, Is.True);
        var root = new Content("Shop", -1, type);
        contents.Save(root);
        contents.Publish(root, ["*"]);
        var page = new Content("Contact", root, type);
        contents.Save(page);
        contents.Publish(page, ["*"]);
        await GetRequiredService<IDocumentNavigationManagementService>().RebuildAsync();
        await GetRequiredService<IDocumentPublishStatusManagementService>().InitializeAsync(CancellationToken.None);
        return (root.Key, page.Key);
    }

    // The export's record once it runs under a lease that ends after the moment, as the worker's renewal leaves it.
    private async Task<EntryExportRecord> LeasedAsync(Guid form, Guid key, DateTimeOffset after)
    {
        var waited = Stopwatch.StartNew();
        while (waited.Elapsed < TimeSpan.FromSeconds(20))
        {
            if (await Jobs.GetAsync(form, key, Editor) is { State: EntryExportState.Running, LeaseUtc: { } lease } record && lease > after)
            {
                return record;
            }

            await Task.Delay(20);
        }

        Assert.Fail($"The export's lease did not reach past {after:O}.");
        return null!;
    }

    // An entry with these files, their bytes in the store; its number.
    private async Task<long> EntryWithFilesAsync(StoredForm form, params (string Name, FormField Field, string State)[] files)
    {
        var store = GetRequiredService<IFormBuilderFileStore>();
        var ids = new List<int>();
        foreach (var (name, field, state) in files)
        {
            var id = FormBuilderFileId.New();
            var bytes = Encoding.UTF8.GetBytes(name);
            using (var content = new MemoryStream(bytes))
            {
                await store.SaveAsync(id, content);
            }

            ids.Add((await Entries.AddFileAsync(new NewEntryFile(Guid.NewGuid(), form.Id, field.Key, name, id, "application/octet-stream", bytes.Length, new string('a', 64), FileScanStates.Clean)))!.Id);
        }

        Clock.Advance(TimeSpan.FromSeconds(1));
        var entry = await Entries.CreateAsync(new NewEntry
        {
            Key = Guid.NewGuid(),
            FormId = form.Id,
            FormVersionId = form.PublishedVersionId!.Value,
            State = EntryState.Submitted,
            SubmissionId = Guid.NewGuid(),
            SubmissionHash = new string('a', 64),
            Culture = "en-US",
            FileIds = ids,
        });

        // A scanner that quarantines a file once its entry holds it, as an entry's files are scanned again.
        for (var index = 0; index < files.Length; index++)
        {
            if (files[index].State != FileScanStates.Clean)
            {
                _ = await Entries.ChangeScanStateAsync(ids[index], FileScanStates.Clean, files[index].State);
            }
        }

        return entry!.Number;
    }

    // The entries as a submission stores them, a second apart, each with its name and a value of every other field.
    private async Task SeedAsync(Guid form, int count)
    {
        var row = (await FormRows.GetAsync(form))!;
        var catalogue = await FormRows.ListFieldsAsync(row.Id);
        var name = catalogue.Single(field => field.Values.FieldKey == NameField.Key).Id;
        var others = catalogue.Where(field => field.Values.FieldKey != NameField.Key).Select(field => field.Id).ToList();
        var start = Clock.GetUtcNow().UtcDateTime;
        await GetRequiredService<FormBuilderDatabase>().WriteAsync(
            async db =>
            {
                for (var i = 1; i <= count; i++)
                {
                    var at = start.AddSeconds(i);
                    var text = $"Person {i}";
                    var entry = new EntryDto
                    {
                        Key = Guid.NewGuid(),
                        FormId = row.Id,
                        FormVersionId = row.PublishedVersionId!.Value,
                        Number = i,
                        State = StoredNames.Of(EntryState.Submitted),
                        SubmissionId = Guid.NewGuid(),
                        SubmissionHash = new string('a', 64),
                        Culture = "en-US",
                        SearchText = EntrySearchText.For([text]),
                        SubmittedUtc = at,
                        UpdatedUtc = at,
                        StateChangedUtc = at,
                    };
                    await db.InsertAsync(entry);
                    await db.InsertAsync(new EntryValueDto { EntryId = entry.Id, FieldId = name, Text = text, TextKey = EntryTextKey.For(text) });
                    foreach (var other in others)
                    {
                        await db.InsertAsync(new EntryValueDto { EntryId = entry.Id, FieldId = other, Text = "A note, \"quoted\"", TextKey = EntryTextKey.For("a note") });
                    }
                }

                var n = new SqlNames(db);
                await db.ExecAsync($"UPDATE {n.T(FormBuilderTables.Form)} SET {n.C("lastEntryNumber")} = @0 WHERE {n.C("id")} = @1", count, row.Id);
                return true;
            });
        Clock.Advance(TimeSpan.FromSeconds(count + 1));
    }

    /// <summary>The finished exports the host announced.</summary>
    internal sealed class Heard
    {
        public ConcurrentQueue<ExportCompletedNotification> Exports { get; } = new();
    }

    /// <summary>Passes what it hears to the fixture's list.</summary>
    internal sealed class HeardHandler(Heard heard) : INotificationAsyncHandler<ExportCompletedNotification>
    {
        public Task HandleAsync(ExportCompletedNotification notification, CancellationToken cancellationToken)
        {
            heard.Exports.Enqueue(notification);
            return Task.CompletedTask;
        }
    }

    /// <summary>A format that fails after it began, as a disk that fills up would.</summary>
    internal sealed class BrokenFormat : IFormBuilderExportFormat
    {
        public const string FormatName = "testing-broken";

        public string Name => FormatName;

        public string DisplayName => "Broken";

        public string FileExtension => ".txt";

        public string ContentType => "text/plain";

        public async Task WriteAsync(FormBuilderExport content, Stream output, CancellationToken cancellationToken)
        {
            await foreach (var _ in content.Entries.WithCancellation(cancellationToken))
            {
                throw new IOException("The disk is full.");
            }
        }
    }

    /// <summary>What a <see cref="SlowFormat"/> waits for.</summary>
    internal sealed class SlowGate
    {
        public TaskCompletionSource Open { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    /// <summary>A format that waits for its gate before it writes, as a page of many files would take long.</summary>
    internal sealed class SlowFormat(SlowGate gate) : IFormBuilderExportFormat
    {
        public const string FormatName = "testing-slow";

        public string Name => FormatName;

        public string DisplayName => "Slow";

        public string FileExtension => ".txt";

        public string ContentType => "text/plain";

        public async Task WriteAsync(FormBuilderExport content, Stream output, CancellationToken cancellationToken)
        {
            await gate.Open.Task.WaitAsync(cancellationToken);
            await foreach (var _ in content.Entries.WithCancellation(cancellationToken))
            {
                output.WriteByte((byte)'.');
            }
        }
    }

    // Counts the bytes and line breaks written, and keeps none of them.
    private sealed class CountingStream : Stream
    {
        private long _length;

        public int Lines { get; private set; }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => _length;

        public override long Position { get => _length; set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            _length += buffer.Length;
            Lines += buffer.Count((byte)'\n');
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Write(buffer.Span);
            return ValueTask.CompletedTask;
        }
    }
}
