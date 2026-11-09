using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Actions;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Entries;

/// <summary>
/// The entry panel's server (ROADMAP 11.2, ADR-0208): opening marks an entry seen once, a sensitive answer is masked until
/// a reveal that is audited, a field the live version lost is marked, notes are their author's to change, a move back and
/// an edit ask before they overbook an option or double a unique answer, and an edit keeps the old values on the timeline.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class EntryPanelTests : ActionTestBase
{
    private static readonly Guid Other = Guid.Parse("7d1c3a52-31f4-4e57-9d2e-61b0f6a1c085");

    private static readonly FormField Salary = new()
    {
        Key = Guid.Parse("c4000000-0000-4000-8000-000000000201"),
        Type = "text",
        Alias = "salary",
        Label = new LocalizedText("Salary"),
        Sensitive = true,
    };

    private static readonly FormField Seat = new()
    {
        Key = Guid.Parse("c4000000-0000-4000-8000-000000000202"),
        Type = "dropdown",
        Alias = "seat",
        Label = new LocalizedText("Seat"),
        Options = new FieldOptions { Items = [new FieldOption { Value = "front", Label = new LocalizedText("Front"), Places = 1 }, new FieldOption { Value = "back", Label = new LocalizedText("Back") }] },
    };

    private static readonly FormField Code = new()
    {
        Key = Guid.Parse("c4000000-0000-4000-8000-000000000203"),
        Type = "text",
        Alias = "code",
        Label = new LocalizedText("Code"),
        Validation = [new UniquePerFormCheck()],
    };

    private static readonly FormField Age = new()
    {
        Key = Guid.Parse("c4000000-0000-4000-8000-000000000204"),
        Type = "number",
        Alias = "age",
        Label = new LocalizedText("Age"),
    };

    private EntryPanels Panels => GetRequiredService<EntryPanels>();

    private EntryNotes Notes => GetRequiredService<EntryNotes>();

    private EntryEdits Edits => GetRequiredService<EntryEdits>();

    private EntryTimelines Timelines => GetRequiredService<EntryTimelines>();

    private EntryStateChanges Changes => GetRequiredService<EntryStateChanges>();

    private IAuditRepository Audit => GetRequiredService<IAuditRepository>();

    [Test]
    public async Task Opening_MarksAnEntrySeenOnce_NamingWhoOpenedItFirst()
    {
        var form = await FormAsync();
        var entry = await SubmitAsync(form);

        var first = await Panels.OpenAsync(form, entry.Key, Editor, mayEdit: false);
        await Panels.OpenAsync(form, entry.Key, Other, mayEdit: false);
        var opened = (await Events.ListForEntryAsync(entry.Id)).Where(entryEvent => entryEvent.Kind == EntryPanels.OpenedEvent).ToList();

        Assert.Multiple(async () =>
        {
            Assert.That(first!.Number, Is.EqualTo(entry.Number));
            Assert.That((await Entries.GetAsync(entry.Key))!.FirstSeenUtc, Is.Not.Null);
            Assert.That(opened.Select(entryEvent => entryEvent.UserKey), Is.EqualTo(new Guid?[] { Editor }), "only the first opening is recorded");
            Assert.That(await Panels.OpenAsync(Guid.NewGuid(), entry.Key, Editor, mayEdit: false), Is.Null, "another form's entry is none");
        });
    }

    [Test]
    public async Task ASensitiveAnswer_IsMasked_AndARevealShowsIt_AndIsAudited()
    {
        var form = await FormAsync();
        var entry = await SubmitAsync(form, """{"name":"Ada","salary":"90000"}""");

        var answer = Answer(await Panels.OpenAsync(form, entry.Key, Editor, mayEdit: true), Salary);
        var revealed = await Panels.RevealAsync(form, entry.Key, Salary.Key, Editor, mayEdit: true);
        var plain = await Panels.RevealAsync(form, entry.Key, NameField.Key, Editor, mayEdit: false);
        var audited = (await Audit.QueryAsync(new AuditQuery { Action = EntryPanels.RevealedAction }, PageRequest.First(5))).Items;

        Assert.Multiple(() =>
        {
            Assert.That((answer.Masked, answer.Texts, answer.Edit), Is.EqualTo((true, Array.Empty<string>(), (EntryPanelEdit?)null)), "masked, with no value to edit either");
            Assert.That((revealed!.Masked, revealed.Sensitive, revealed.Texts), Is.EqualTo((false, true, new[] { "90000" })));
            Assert.That(revealed.Edit!.Values, Is.EqualTo(new[] { "90000" }), "a revealed answer the panel edits says how");
            Assert.That(plain!.Texts, Is.EqualTo(new[] { "Ada" }));
            Assert.That(audited.Select(item => (item.EntityKey, item.UserKey)), Is.EqualTo(new[] { ((Guid?)entry.Key, (Guid?)Editor) }), "one entry for the sensitive answer, none for the other");
            Assert.That(audited.Single().Details, Does.Contain(Salary.Key.ToString("D")).And.Not.Contain("90000"));
        });
    }

    [Test]
    public async Task AFieldTheLiveVersionNoLongerHas_IsMarked()
    {
        var form = await FormAsync();
        var entry = await SubmitAsync(form, """{"name":"Ada","age":"36"}""");
        await SaveAndPublishAsync(form, definition => WithFields(definition, NameField, Salary, Seat, Code));

        var panel = await Panels.OpenAsync(form, entry.Key, Editor, mayEdit: false);

        Assert.Multiple(() =>
        {
            Assert.That((Answer(panel, Age).Removed, Answer(panel, Age).Texts), Is.EqualTo((true, new[] { "36" })), "shown in the version it was sent with, and marked");
            Assert.That(Answer(panel, NameField).Removed, Is.False);
        });
    }

    [Test]
    public async Task Notes_AreReadNewestFirst_AndOnlyTheirAuthorChangesThem()
    {
        var form = await FormAsync();
        var entry = await SubmitAsync(form);

        var first = (await Notes.AddAsync(form, entry.Key, Editor, "  Strong portfolio.  "))!;
        Clock.Advance(TimeSpan.FromMinutes(1));
        await Notes.AddAsync(form, entry.Key, Other, "Invite for an interview.");
        var refused = await Notes.ChangeAsync(form, entry.Key, first.Id, Other, "Mine now.");
        Clock.Advance(TimeSpan.FromMinutes(1));
        var changed = await Notes.ChangeAsync(form, entry.Key, first.Id, Editor, "Strong portfolio, and kind.");
        var listed = (await Notes.ListAsync(form, entry.Key))!;

        Assert.Multiple(async () =>
        {
            Assert.That(first.Body, Is.EqualTo("Strong portfolio."), "trimmed");
            Assert.That(refused.Change, Is.EqualTo(EntryNoteChange.NotYours));
            Assert.That(changed.Change, Is.EqualTo(EntryNoteChange.Changed));
            Assert.That(listed.Select(note => note.Body), Is.EqualTo(new[] { "Invite for an interview.", "Strong portfolio, and kind." }), "newest first");
            Assert.That(listed[1].Edited, Is.Not.Null);
            Assert.That((await Panels.OpenAsync(form, entry.Key, Editor, mayEdit: false))!.Notes, Is.EqualTo(2));
            Assert.That(await Notes.ListAsync(form, Guid.NewGuid()), Is.Null);
        });
    }

    [Test]
    public async Task AMoveBack_AsksBeforeItOverbooksOrDoublesAUniqueAnswer_AndMovesOnlyWhenAgreed()
    {
        var form = await FormAsync();
        var first = await SubmitAsync(form, """{"name":"Ada","seat":"front","code":"A1"}""");
        await Changes.ChangeAsync(form, [first.Key], EntryState.Rejected, Editor);
        var second = await SubmitAsync(form, """{"name":"Grace","seat":"front","code":"A1"}""");

        var asked = await Changes.ChangeAsync(form, [first.Key], EntryState.Submitted, Editor);
        var stayed = (await Entries.GetAsync(first.Key))!.State;
        var agreed = await Changes.ChangeAsync(form, [first.Key], EntryState.Submitted, Editor, overbook: true);
        var bySystem = await Changes.ChangeAsync(form, [second.Key], EntryState.Rejected, null);

        Assert.Multiple(() =>
        {
            Assert.That(asked.Moved, Is.Empty, "nothing moves while it asks");
            Assert.That(asked.Asks!.Full, Is.EqualTo(new[] { new EntryFullOption(Seat.Key, "Seat", "front", "Front", 1, 1) }));
            Assert.That(asked.Asks.Held, Is.EqualTo(new[] { new EntryHeldAnswer(Code.Key, "Code") }));
            Assert.That(stayed, Is.EqualTo(EntryState.Rejected));
            Assert.That((agreed.Asks, agreed.Moved.Select(entry => entry.State)), Is.EqualTo(((EntryMoveCheck?)null, new[] { EntryState.Submitted })));
            Assert.That(bySystem.Asks, Is.Null, "a move out of the holding states never asks");
        });
    }

    [Test]
    public async Task TheCheck_ReadsWhatAMoveBackWouldAsk_AndMovesNothing()
    {
        var form = await FormAsync();
        var first = await SubmitAsync(form, """{"name":"Ada","seat":"front","code":"A1"}""");
        await Changes.ChangeAsync(form, [first.Key], EntryState.Rejected, Editor);
        var second = await SubmitAsync(form, """{"name":"Grace","seat":"front","code":"A1"}""");

        var asks = await Changes.AsksAsync(form, [first.Key], EntryState.Submitted);
        var staying = await Changes.AsksAsync(form, [second.Key], EntryState.Approved);
        var holding = await Changes.AsksAsync(form, [first.Key], EntryState.Spam);
        var nowhere = await Changes.AsksAsync(Guid.NewGuid(), [first.Key], EntryState.Submitted);
        var stayed = (await Entries.GetAsync(first.Key))!.State;

        Assert.Multiple(() =>
        {
            Assert.That(asks!.Full, Is.EqualTo(new[] { new EntryFullOption(Seat.Key, "Seat", "front", "Front", 1, 1) }), "what the move would ask");
            Assert.That(asks.Held, Is.EqualTo(new[] { new EntryHeldAnswer(Code.Key, "Code") }));
            Assert.That(staying, Is.Null, "an entry that holds its places already moves without asking");
            Assert.That(holding, Is.Null, "a move into a holding state asks nothing");
            Assert.That(nowhere, Is.Null, "a form that is gone");
            Assert.That(stayed, Is.EqualTo(EntryState.Rejected), "the check moves nothing");
        });
    }

    [Test]
    public async Task TheTimeline_NamesEachRunByItsAction_NewestFirst()
    {
        var form = await ActionFormAsync(Action("Tell the team"));
        var entry = await SubmitAsync(form);
        await DrainAsync(entry.Id);

        var timeline = (await Timelines.ReadAsync(form, entry.Key, maySeeSensitive: false))!;

        Assert.Multiple(() =>
        {
            Assert.That(timeline.Runs.Select(run => (run.Name, run.State, run.Attempts)), Is.EqualTo(new[] { ((string?)"Tell the team", ActionRunState.Succeeded, 1) }));
            Assert.That(timeline.Events.Select(entryEvent => entryEvent.Kind), Does.Contain(SubmissionEntries.SubmittedEvent));
        });
    }

    [Test]
    public async Task AMoveBack_AsksOfAnOptionListsPlacesToo()
    {
        var listKey = Guid.NewGuid();
        await GetRequiredService<OptionListService>().CreateAsync(listKey, new OptionListInput("sizes", "Sizes", OptionListKind.Manual, Items: """[{"value":"s","label":{"":"Small"},"places":1},{"value":"m"}]"""));
        var size = new FormField { Key = Guid.NewGuid(), Type = "radio", Alias = "size", Label = new LocalizedText("Size"), Options = new FieldOptions { Source = OptionSource.List, OptionList = listKey } };
        var form = await PublishAsync(name: $"Shirts {Guid.NewGuid():N}", fields: [NameField, size]);
        var first = await SubmitAsync(form, """{"name":"Ada","size":"s"}""");
        await Changes.ChangeAsync(form, [first.Key], EntryState.Rejected, Editor);
        await SubmitAsync(form, """{"name":"Grace","size":"s"}""");

        var asked = await Changes.ChangeAsync(form, [first.Key], EntryState.Submitted, Editor);

        Assert.That(asked.Asks!.Full, Is.EqualTo(new[] { new EntryFullOption(size.Key, "Size", "s", "Small", 1, 1) }));
    }

    [Test]
    public async Task AnEdit_ChangesAnswers_KeepsTheOldOnTheTimeline_ButNoSensitiveText_AndAuditsTheFields()
    {
        var form = await FormAsync();
        var entry = await SubmitAsync(form, """{"name":"Ada","age":"36","salary":"90000"}""");

        var result = await Edits.EditAsync(form, entry.Key, Editor, [new EntryEditValue(NameField.Key, ["Ada Lovelace"]), new EntryEditValue(Age.Key, ["37"]), new EntryEditValue(Salary.Key, ["95000"])], maySeeSensitive: true, overbook: false);
        var panel = await Panels.OpenAsync(form, entry.Key, Editor, mayEdit: true);
        var hidden = (await Timelines.ReadAsync(form, entry.Key, maySeeSensitive: false))!.Events.First(entryEvent => entryEvent.Kind == EntryEdits.EditedEvent);
        var shown = (await Timelines.ReadAsync(form, entry.Key, maySeeSensitive: true))!.Events.First(entryEvent => entryEvent.Kind == EntryEdits.EditedEvent);
        var audited = (await Audit.QueryAsync(new AuditQuery { Action = EntryEdits.EditedAction }, PageRequest.First(5))).Items;
        var stored = (await GetRequiredService<IEntryEventRepository>().ListForEntryAsync(entry.Id)).Single(entryEvent => entryEvent.Kind == EntryEdits.EditedEvent);

        Assert.Multiple(() =>
        {
            Assert.That((result.Status, result.Changed), Is.EqualTo((EntryEditStatus.Saved, 3)));
            Assert.That(Answer(panel, NameField).Texts, Is.EqualTo(new[] { "Ada Lovelace" }));
            Assert.That(Answer(panel, Age).Texts, Is.EqualTo(new[] { "37" }));
            Assert.That(shown.Changes.Select(change => (change.Label, change.Before, change.After)), Is.EqualTo(new[]
            {
                ("Name", (IReadOnlyList<string>)["Ada"], (IReadOnlyList<string>)["Ada Lovelace"]),
                ("Age", ["36"], ["37"]),
                ("Salary", [], []),
            }), "a sensitive answer's change keeps no texts, even for a reader who may see it (ADR-0225)");
            Assert.That(hidden.Changes[2], Is.EqualTo(new EntryTimelineChange("Salary", [], [], Masked: true)).Using<EntryTimelineChange>((a, b) => a.Label == b.Label && a.Masked == b.Masked && a.Before.Count == 0 && a.After.Count == 0));
            Assert.That(shown.Changes[2].Masked, Is.True);
            Assert.That(stored.Details, Does.Not.Contain("90000").And.Not.Contain("95000"), "the event itself holds neither");
            Assert.That(audited.Single().Details, Does.Contain(Salary.Key.ToString("D")).And.Not.Contain("95000"));
        });
    }

    [Test]
    public async Task AnEdit_IsCheckedByTheFieldType_AndRefusesWhatThePanelDoesNotEdit()
    {
        var form = await FormAsync();
        var entry = await SubmitAsync(form, """{"name":"Ada","salary":"90000"}""");

        var invalid = await Edits.EditAsync(form, entry.Key, Editor, [new EntryEditValue(Age.Key, ["many"])], maySeeSensitive: true, overbook: false);
        var hidden = await Edits.EditAsync(form, entry.Key, Editor, [new EntryEditValue(Salary.Key, ["1"])], maySeeSensitive: false, overbook: false);
        var unknown = await Edits.EditAsync(form, entry.Key, Editor, [new EntryEditValue(Guid.NewGuid(), ["1"])], maySeeSensitive: true, overbook: false);
        var same = await Edits.EditAsync(form, entry.Key, Editor, [new EntryEditValue(NameField.Key, ["Ada"])], maySeeSensitive: true, overbook: false);

        Assert.Multiple(async () =>
        {
            Assert.That(invalid.Problems.Select(problem => (problem.Code, problem.Path)), Is.EqualTo(new[] { ("invalid", "answers[0].values") }));
            Assert.That(hidden.Problems.Select(problem => (problem.Code, problem.Path)), Is.EqualTo(new[] { ("not_editable", "answers[0].field") }), "a sensitive value the user may not see");
            Assert.That(unknown.Status, Is.EqualTo(EntryEditStatus.Invalid));
            Assert.That((same.Status, same.Changed), Is.EqualTo((EntryEditStatus.Saved, 0)), "nothing to change");
            Assert.That((await Events.ListForEntryAsync(entry.Id)).Any(entryEvent => entryEvent.Kind == EntryEdits.EditedEvent), Is.False);
        });
    }

    [Test]
    public async Task AnEdit_FailingOneOfTheFieldsChecks_SaysItsMessage_AndChangesNothing()
    {
        var postcode = new FormField
        {
            Key = Guid.Parse("c4000000-0000-4000-8000-000000000205"),
            Type = "text",
            Alias = "postcode",
            Label = new LocalizedText("Postcode"),
            Validation = [new PatternCheck { Pattern = "[0-9]{4}", Message = new LocalizedText("Four digits, please.") }],
        };
        var form = await PublishAsync(name: "Checked", fields: [NameField, postcode]);
        var entry = await SubmitAsync(form, """{"name":"Ada","postcode":"1234"}""");

        var failed = await Edits.EditAsync(form, entry.Key, Editor, [new EntryEditValue(postcode.Key, ["abcd"])], maySeeSensitive: true, overbook: false);
        var passed = await Edits.EditAsync(form, entry.Key, Editor, [new EntryEditValue(postcode.Key, ["5678"])], maySeeSensitive: true, overbook: false);

        Assert.Multiple(() =>
        {
            Assert.That(failed.Problems.Select(problem => (problem.Code, problem.Path, problem.Message)), Is.EqualTo(new[] { ("check_failed", "answers[0].values", "Four digits, please.") }));
            Assert.That((passed.Status, passed.Changed), Is.EqualTo((EntryEditStatus.Saved, 1)), "an answer that passes it saves");
        });
    }

    [Test]
    public async Task AnEditedChoice_MovesItsPlace_AndAsksBeforeOverbooking()
    {
        var form = await FormAsync();
        var first = await SubmitAsync(form, """{"name":"Ada","seat":"front"}""");
        var second = await SubmitAsync(form, """{"name":"Grace","seat":"back"}""");
        var row = (await FormRows.GetAsync(form))!;
        var front = new OptionPlace(Seat.Key, "front");

        var asked = await Edits.EditAsync(form, second.Key, Editor, [new EntryEditValue(Seat.Key, ["front"])], maySeeSensitive: true, overbook: false);
        var freed = await Edits.EditAsync(form, first.Key, Editor, [new EntryEditValue(Seat.Key, ["back"])], maySeeSensitive: true, overbook: false);
        var taken = (await Entries.GetPlacesTakenAsync(row.Id, [front])).GetValueOrDefault(front);
        var moved = await Edits.EditAsync(form, second.Key, Editor, [new EntryEditValue(Seat.Key, ["front"])], maySeeSensitive: true, overbook: false);

        Assert.Multiple(async () =>
        {
            Assert.That((asked.Status, asked.Asks!.Full.Single().Taken), Is.EqualTo((EntryEditStatus.Asks, 1)));
            Assert.That(freed.Status, Is.EqualTo(EntryEditStatus.Saved));
            Assert.That(taken, Is.Zero, "the changed choice gave its place back");
            Assert.That(moved.Status, Is.EqualTo(EntryEditStatus.Saved), "the place is free now");
            Assert.That((await Entries.GetPlacesTakenAsync(row.Id, [front])).GetValueOrDefault(front), Is.EqualTo(1));
        });
    }

    private static EntryPanelAnswer Answer(EntryPanel? panel, FormField field) =>
        panel!.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Answers).Single(answer => answer.Field == field.Key);

    private Task<Guid> FormAsync() => PublishAsync(
        name: $"Applications {Guid.NewGuid():N}",
        change: definition => definition with { Settings = definition.Settings with { Submission = definition.Settings.Submission with { Approval = ApprovalMode.Manual } } },
        fields: [NameField, Salary, Seat, Code, Age]);
}
