using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Tests.Common.Testing;
using static Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories.FormRepositoryTests;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;

/// <summary>
/// Entries with their values and files (ADR-0003, ADR-0038): numbered by the form's counter in the same unit of work,
/// refused by the conditions of one insert, and stored whole or not at all.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class EntryRepositoryTests : RepositoryTestBase
{
    [Test]
    public async Task AnEntry_IsStoredWithEveryColumn_AndFoundEveryWay()
    {
        var form = await CreateTestFormAsync("name", "amount", "arrival", "agree");
        var arrival = new DateTime(2026, 11, 2, 14, 30, 0, DateTimeKind.Utc);
        var entry = NewEntry(
            form.Id,
            form.VersionId,
            new NewEntryValue(form["name"], "  Anna "),
            new NewEntryValue(form["amount"], "12.50", Number: 12.5),
            new NewEntryValue(form["arrival"], "2026-11-02T14:30Z", Date: arrival),
            new NewEntryValue(form["agree"], Hex(32), Bool: true)) with
        {
            State = EntryState.Draft,
            IsTest = true,
            Culture = "de-CH",
            ContentKey = Guid.NewGuid(),
            PageUrl = "/kontakt",
            MemberKey = Guid.NewGuid(),
            IpAddress = "203.0.113.0",
            SpamScore = 3,
            SpamReasons = "fillTime",
            SearchText = "anna",
            Context = """{"utm":"newsletter"}""",
            DurationSeconds = 42,
            FirstSeenUtc = Now.AddMinutes(-3),
            VerifiedUtc = Now.AddMinutes(-1),
            ResumeTokenHash = Hex(32),
            ResumeExpiresUtc = Now.AddDays(7),
            CurrentPageKey = Guid.NewGuid(),
            ChallengeId = Hex(16),
            PurgeAfterUtc = Now.AddDays(365),
        };

        var stored = await Entries.CreateAsync(entry);

        Assert.That(stored, Is.Not.Null);
        var values = await Entries.ListValuesAsync([stored!.Id]);
        Assert.Multiple(async () =>
        {
            Assert.That(stored, Is.EqualTo(Expected(entry, stored.Id, 1)), "every column round-trips, stamped with the clock");
            Assert.That(await Entries.GetAsync(entry.Key), Is.EqualTo(stored));
            Assert.That(await Entries.GetByIdAsync(stored.Id), Is.EqualTo(stored));
            Assert.That(await Entries.GetByNumberAsync(form.Id, 1), Is.EqualTo(stored));
            Assert.That(await Entries.GetBySubmissionAsync(form.Id, entry.SubmissionId), Is.EqualTo(stored));
            Assert.That(await Entries.GetAsync(Guid.NewGuid()), Is.Null);
            Assert.That(await Entries.GetByIdAsync(stored.Id + 1), Is.Null);
            Assert.That(await Entries.GetByNumberAsync(form.Id, 2), Is.Null);
            Assert.That(await Entries.GetBySubmissionAsync(form.Id + 1, entry.SubmissionId), Is.Null, "a submission id is the form's");
            Assert.That(values.Select(Plain), Is.EqualTo(new[]
            {
                (form["name"], 0, 0, "  Anna ", "anna", (double?)null, (DateTime?)null, (bool?)null, (int?)null),
                (form["amount"], 0, 0, "12.50", null, 12.5, null, null, null),
                (form["arrival"], 0, 0, "2026-11-02T14:30Z", null, null, arrival, null, null),
                (form["agree"], 0, 0, entry.Values[3].Text, null, null, null, true, null),
            }));
            Assert.That(values.Select(value => value.EntryId), Is.All.EqualTo(stored.Id));
            Assert.That(values[2].Date?.Kind, Is.EqualTo(DateTimeKind.Utc), "dates are read as UTC");
        });
    }

    [Test]
    public async Task EntryNumbers_CountPerForm_FromTheFormsCounter()
    {
        var form = await CreateTestFormAsync();
        var other = await CreateTestFormAsync();

        var numbers = new List<int>();
        for (var i = 0; i < 3; i++)
        {
            numbers.Add((await Entries.CreateAsync(NewEntry(form.Id, form.VersionId)))!.Number);
        }

        var test = await Entries.CreateAsync(NewEntry(form.Id, form.VersionId) with { IsTest = true, State = EntryState.Draft });
        var elsewhere = await Entries.CreateAsync(NewEntry(other.Id, other.VersionId));

        Assert.Multiple(async () =>
        {
            Assert.That(numbers, Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(test!.Number, Is.EqualTo(4), "test entries and drafts take numbers from the same counter");
            Assert.That(elsewhere!.Number, Is.EqualTo(1), "each form counts on its own");
            Assert.That((await Forms.GetByIdAsync(form.Id))!.LastEntryNumber, Is.EqualTo(4));
        });
    }

    [Test]
    public async Task TheCounter_IsTheUnitsFirstStatement_AndTheInsertTakesItsNumber()
    {
        var form = await CreateTestFormAsync();
        var log = new CommandLog();

        await WatchedAsync(log, () => Entries.CreateAsync(NewEntry(form.Id, form.VersionId)));

        // On SQLite the gate lets one unit through at a time, so parallel inserts alone cannot show this: the form's row
        // is held before anything is read, and the number is the counter's (ADR-0003).
        Assert.Multiple(() =>
        {
            Assert.That(log.Texts[0], Does.StartWith("UPDATE " + T(FormBuilderTables.Form)).And.Contain(Q("lastEntryNumber")));
            Assert.That(log.Texts.Single(text => text.StartsWith("INSERT INTO " + T(FormBuilderTables.Entry), StringComparison.Ordinal)), Does.Contain(Q("lastEntryNumber")));
        });
    }

    [Test]
    public async Task ACounterBehindTheStoredNumbers_CatchesUp()
    {
        var form = await CreateTestFormAsync();
        await Entries.CreateAsync(NewEntry(form.Id, form.VersionId));
        await Entries.CreateAsync(NewEntry(form.Id, form.VersionId));
        Write(db => db.Execute($"UPDATE {T(FormBuilderTables.Form)} SET {Q("lastEntryNumber")} = 0 WHERE {Q("id")} = @0", form.Id));

        var next = await Entries.CreateAsync(NewEntry(form.Id, form.VersionId));

        Assert.Multiple(async () =>
        {
            Assert.That(next!.Number, Is.EqualTo(3), "not 1, which the first entry holds");
            Assert.That((await Forms.GetByIdAsync(form.Id))!.LastEntryNumber, Is.EqualTo(3));
        });
    }

    [Test]
    public async Task ANumber_IsNeverGivenTwice_EvenAfterItsEntryIsDeleted()
    {
        var form = await CreateTestFormAsync();
        await Entries.CreateAsync(NewEntry(form.Id, form.VersionId));
        var second = await Entries.CreateAsync(NewEntry(form.Id, form.VersionId));
        await Entries.DeleteAsync(second!.Id);

        var third = await Entries.CreateAsync(NewEntry(form.Id, form.VersionId));

        Assert.Multiple(async () =>
        {
            Assert.That(third!.Number, Is.EqualTo(3), "#2 named the deleted entry and stays unused");
            Assert.That(await Entries.GetByNumberAsync(form.Id, 2), Is.Null);
        });
    }

    [Test]
    public async Task FiftyParallelInserts_GetTheNumbers1To50_EachOnce()
    {
        var form = await CreateTestFormAsync("name");

        var stored = await Task.WhenAll(Enumerable.Range(0, 50).Select(i =>
            Task.Run(() => Entries.CreateAsync(NewEntry(form.Id, form.VersionId, new NewEntryValue(form["name"], "JFB_TEST " + i))))));

        Assert.Multiple(async () =>
        {
            Assert.That(stored.Select(entry => entry!.Number), Is.EquivalentTo(Enumerable.Range(1, 50)));
            Assert.That((await Forms.GetByIdAsync(form.Id))!.LastEntryNumber, Is.EqualTo(50));
            Assert.That(Count(FormBuilderTables.Entry), Is.EqualTo(50));
            Assert.That(Count(FormBuilderTables.EntryValue), Is.EqualTo(50), "each with its value");
        });
    }

    [Test]
    public async Task ParallelRetriesOfOneSubmission_StoreOneEntry()
    {
        var form = await CreateTestFormAsync();
        var submission = Guid.NewGuid();

        var attempts = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
        {
            try
            {
                return (await Entries.CreateAsync(NewEntry(form.Id, form.VersionId) with { SubmissionId = submission }))?.Number;
            }
            catch (FormBuilderConflictException refused) when (refused.Conflict == FormBuilderConflict.DuplicateSubmission)
            {
                return null;
            }
        })));

        Assert.Multiple(async () =>
        {
            Assert.That(attempts.Where(number => number is not null), Is.EqualTo(new int?[] { 1 }), "one is stored, the others are told");
            Assert.That(Count(FormBuilderTables.Entry), Is.EqualTo(1));
            Assert.That((await Forms.GetByIdAsync(form.Id))!.LastEntryNumber, Is.EqualTo(1), "the refused ones gave their numbers back");
        });
    }

    [Test]
    public async Task ASubmissionId_MakesOneEntryPerForm_AndARefusalGivesItsNumberBack()
    {
        var form = await CreateTestFormAsync();
        var other = await CreateTestFormAsync();
        var first = NewEntry(form.Id, form.VersionId);
        await Entries.CreateAsync(first);

        AssertRefused(() => Entries.CreateAsync(NewEntry(form.Id, form.VersionId) with { SubmissionId = first.SubmissionId }), FormBuilderConflict.DuplicateSubmission);
        var next = await Entries.CreateAsync(NewEntry(form.Id, form.VersionId));
        var elsewhere = await Entries.CreateAsync(NewEntry(other.Id, other.VersionId) with { SubmissionId = first.SubmissionId });

        Assert.Multiple(() =>
        {
            Assert.That(next!.Number, Is.EqualTo(2), "the refused entry took no number");
            Assert.That(elsewhere, Is.Not.Null, "another form has its own submission ids");
            Assert.That(Count(FormBuilderTables.Entry), Is.EqualTo(3));
        });
    }

    [Test]
    public async Task AChallenge_MakesOneEntryPerForm()
    {
        var form = await CreateTestFormAsync();
        var other = await CreateTestFormAsync();
        var challenge = Hex(16);
        await Entries.CreateAsync(NewEntry(form.Id, form.VersionId) with { ChallengeId = challenge });

        AssertRefused(() => Entries.CreateAsync(NewEntry(form.Id, form.VersionId) with { ChallengeId = challenge }), FormBuilderConflict.ChallengeUsed);
        await Entries.CreateAsync(NewEntry(other.Id, other.VersionId) with { ChallengeId = challenge });
        await Entries.CreateAsync(NewEntry(form.Id, form.VersionId));
        await Entries.CreateAsync(NewEntry(form.Id, form.VersionId));

        Assert.Multiple(async () =>
        {
            Assert.That(Count(FormBuilderTables.Entry), Is.EqualTo(4), "per form, and entries without a challenge are many");
            Assert.That((await Forms.GetByIdAsync(form.Id))!.LastEntryNumber, Is.EqualTo(3), "the refused entry gave its number back");
        });
    }

    [Test]
    public async Task AKeyAndAResumeToken_AreOneEntrysOwn()
    {
        var form = await CreateTestFormAsync();
        var other = await CreateTestFormAsync();
        var first = NewEntry(form.Id, form.VersionId) with { ResumeTokenHash = Hex(32) };
        await Entries.CreateAsync(first);

        Assert.Multiple(async () =>
        {
            AssertRefused(() => Entries.CreateAsync(NewEntry(other.Id, other.VersionId) with { Key = first.Key }), FormBuilderConflict.DuplicateKey, "the key");
            AssertRefused(() => Entries.CreateAsync(NewEntry(other.Id, other.VersionId) with { ResumeTokenHash = first.ResumeTokenHash }), FormBuilderConflict.DuplicateKey, "the resume token");
            Assert.That(Count(FormBuilderTables.Entry), Is.EqualTo(1));
            Assert.That((await Forms.GetByIdAsync(other.Id))!.LastEntryNumber, Is.Zero, "the refusals gave their numbers back");
        });
    }

    [Test]
    public async Task AnEntryOfAMissingForm_IsNull_AndAnotherFormsVersionIsRefused()
    {
        var form = await CreateTestFormAsync();
        var other = await CreateTestFormAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(await Entries.CreateAsync(NewEntry(int.MaxValue, form.VersionId)), Is.Null);
            AssertRefused(() => Entries.CreateAsync(NewEntry(form.Id, other.VersionId)), FormBuilderConflict.MissingVersion, "a version of another form");
            AssertRefused(() => Entries.CreateAsync(NewEntry(form.Id, int.MaxValue)), FormBuilderConflict.MissingVersion, "no version");
            Assert.That(Count(FormBuilderTables.Entry), Is.Zero);
            Assert.That((await Forms.GetByIdAsync(form.Id))!.LastEntryNumber, Is.Zero, "the refusals gave their numbers back");
        });
    }

    [Test]
    public async Task AValueOfAnotherFormsField_IsRefused_AndNothingIsWritten()
    {
        var form = await CreateTestFormAsync("name");
        var other = await CreateTestFormAsync("name");

        AssertRefused(
            () => Entries.CreateAsync(NewEntry(form.Id, form.VersionId, new NewEntryValue(form["name"], "kept"), new NewEntryValue(other["name"], "elsewhere"))),
            FormBuilderConflict.MissingField);

        Assert.Multiple(async () =>
        {
            Assert.That(Count(FormBuilderTables.Entry), Is.Zero);
            Assert.That(Count(FormBuilderTables.EntryValue), Is.Zero);
            Assert.That((await Forms.GetByIdAsync(form.Id))!.LastEntryNumber, Is.Zero);
        });
    }

    [Test]
    public async Task AWriterTheDatabaseLetThrough_IsRefusedByTheIndex_ForTheSameReason()
    {
        var form = await CreateTestFormAsync();
        var first = NewEntry(form.Id, form.VersionId) with { ChallengeId = Hex(16), ResumeTokenHash = Hex(32) };
        await Entries.CreateAsync(first);
        var file = await Entries.AddFileAsync(NewFile(form.Id));

        Assert.Multiple(async () =>
        {
            AssertBackstop(() => Entries.CreateAsync(NewEntry(form.Id, form.VersionId) with { SubmissionId = first.SubmissionId }), FormBuilderConflict.DuplicateSubmission);
            AssertBackstop(() => Entries.CreateAsync(NewEntry(form.Id, form.VersionId) with { ChallengeId = first.ChallengeId }), FormBuilderConflict.ChallengeUsed);
            AssertBackstop(() => Entries.CreateAsync(NewEntry(form.Id, form.VersionId) with { Key = first.Key }), FormBuilderConflict.DuplicateKey);
            AssertBackstop(() => Entries.CreateAsync(NewEntry(form.Id, form.VersionId) with { ResumeTokenHash = first.ResumeTokenHash }), FormBuilderConflict.DuplicateKey);
            AssertBackstop(() => Entries.AddFileAsync(NewFile(form.Id) with { Key = file!.Key }), FormBuilderConflict.DuplicateKey);
            Assert.That(Count(FormBuilderTables.Entry), Is.EqualTo(1));
            Assert.That(Count(FormBuilderTables.EntryFile), Is.EqualTo(1));
            Assert.That((await Forms.GetByIdAsync(form.Id))!.LastEntryNumber, Is.EqualTo(1), "the refusals gave their numbers back");
        });
    }

    [Test]
    public async Task AConsentWordingTheDatabaseLetThrough_IsTheAnswer()
    {
        var hash = await Entries.AddConsentTextAsync("en", "JFB_TEST wording");
        var rewriter = new CommandRewriter(" WHERE NOT EXISTS", " WHERE 1 = 1 OR NOT EXISTS");

        var again = await WatchedAsync(rewriter, () => Entries.AddConsentTextAsync("en", "JFB_TEST wording"));

        Assert.Multiple(() =>
        {
            Assert.That(rewriter.Rewritten, Is.EqualTo(1), "the insert's condition was lifted");
            Assert.That(again, Is.EqualTo(hash), "the primary key refused it, and the stored wording is the same");
            Assert.That(Count(FormBuilderTables.ConsentText), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task AnUpdateThatFoundAnotherState_IsRefused_EvenWhenTheStateCameBack()
    {
        var form = await CreateTestFormAsync();
        var entry = await Entries.CreateAsync(NewEntry(form.Id, form.VersionId));
        var file = await Entries.AddFileAsync(NewFile(form.Id));

        // The update matches no row, as when another unit moved the row on in between; the read after it finds the row as
        // before, as when a third unit moved it back.
        var states = new CommandRewriter($"AND {Q("state")} = ", $"AND 1 = 0 AND {Q("state")} = ");
        var scans = new CommandRewriter($"AND {Q("scanState")} = ", $"AND 1 = 0 AND {Q("scanState")} = ");

        Assert.Multiple(async () =>
        {
            Assert.That(async () => await WatchedAsync(states, () => Entries.ChangeStateAsync(entry!.Id, EntryState.Submitted, EntryState.Approved, Editor)), Throws.InstanceOf<FormBuilderConcurrencyException>());
            Assert.That(async () => await WatchedAsync(scans, () => Entries.ChangeScanStateAsync(file!.Id, "Pending", "Clean")), Throws.InstanceOf<FormBuilderConcurrencyException>());
            Assert.That((states.Rewritten, scans.Rewritten), Is.EqualTo((1, 1)));
            Assert.That(await Entries.GetByIdAsync(entry!.Id), Is.EqualTo(entry));
        });
    }

    [Test]
    public async Task AnEntry_TakesItsFiles_WhichAreNoLongerOrphans()
    {
        var form = await CreateTestFormAsync("upload");
        var cv = await Entries.AddFileAsync(NewFile(form.Id, "cv.pdf"));
        var letter = await Entries.AddFileAsync(NewFile(form.Id, "letter.pdf"));
        var waiting = await Entries.AddFileAsync(NewFile(form.Id, "waiting.pdf"));

        var entry = await Entries.CreateAsync(NewEntry(form.Id, form.VersionId, new NewEntryValue(form["upload"], "cv.pdf", FileId: cv!.Id)) with
        {
            FileIds = [cv.Id, letter!.Id],
        });

        Clock.Advance(TimeSpan.FromDays(1));
        var orphans = await Entries.ListOrphanFilesAsync(Clock.GetUtcNow(), 10);
        Assert.Multiple(async () =>
        {
            Assert.That((await Entries.ListFilesAsync(entry!.Id)).Select(file => (file.Key, file.EntryId)), Is.EqualTo(new[] { (cv.Key, (long?)entry.Id), (letter.Key, entry.Id) }));
            Assert.That(orphans.Select(file => file.Key), Is.EqualTo(new[] { waiting!.Key }));
            Assert.That((await Entries.ListValuesAsync([entry.Id])).Single().FileId, Is.EqualTo(cv.Id));
        });
    }

    [Test]
    public async Task AFileOfAnotherForm_OrOfAnotherEntry_IsRefused_AndNothingIsWritten()
    {
        var form = await CreateTestFormAsync();
        var other = await CreateTestFormAsync();
        var elsewhere = await Entries.AddFileAsync(NewFile(other.Id));
        var taken = await Entries.AddFileAsync(NewFile(form.Id));
        var owner = await Entries.CreateAsync(NewEntry(form.Id, form.VersionId) with { FileIds = [taken!.Id] });
        var free = await Entries.AddFileAsync(NewFile(form.Id));

        Assert.Multiple(async () =>
        {
            AssertRefused(() => Entries.CreateAsync(NewEntry(form.Id, form.VersionId) with { FileIds = [free!.Id, elsewhere!.Id] }), FormBuilderConflict.MissingFile, "another form's");
            AssertRefused(() => Entries.CreateAsync(NewEntry(form.Id, form.VersionId) with { FileIds = [taken.Id] }), FormBuilderConflict.MissingFile, "another entry's");
            AssertRefused(() => Entries.CreateAsync(NewEntry(form.Id, form.VersionId) with { FileIds = [int.MaxValue] }), FormBuilderConflict.MissingFile, "none");
            Assert.That(Count(FormBuilderTables.Entry), Is.EqualTo(1));
            Assert.That((await Forms.GetByIdAsync(form.Id))!.LastEntryNumber, Is.EqualTo(1), "the refusals gave their numbers back");
            Assert.That((await Entries.GetFileAsync(free!.Key))!.EntryId, Is.Null, "the file a refused entry named waits on");
            Assert.That((await Entries.GetFileAsync(taken.Key))!.EntryId, Is.EqualTo(owner!.Id));
        });
    }

    [Test]
    public async Task ManyValues_GoInStatementsSqlServerAccepts()
    {
        var form = await CreateTestFormAsync("guests");
        var values = Enumerable.Range(0, (2 * EntryRepository.ValuesPerStatement) + 50)
            .Select(row => new NewEntryValue(form["guests"], "JFB_TEST guest " + row, RowIndex: row))
            .ToArray();

        var statements = new List<int>();
        var entry = await Database.WriteAsync(async db =>
        {
            var counter = new ParameterCounter(statements);
            db.Interceptors.Add(counter);
            try
            {
                return await Entries.CreateAsync(NewEntry(form.Id, form.VersionId, values));
            }
            finally
            {
                db.Interceptors.Remove(counter);
            }
        });

        var stored = await Entries.ListValuesAsync([entry!.Id]);
        Assert.Multiple(() =>
        {
            Assert.That(statements.Where(count => count >= 500), Is.EqualTo(new[] { 1000, 1000, 500 }), "the value inserts, ten parameters a row");
            Assert.That(statements, Is.All.LessThanOrEqualTo(2100));
            Assert.That(stored.Select(value => (value.RowIndex, value.Text)), Is.EqualTo(values.Select(value => (value.RowIndex, value.Text))), "every value, in row order");
        });
    }

    [Test]
    public async Task TheValuesOfManyEntries_AreReadInStatementsOfAtMost1000Ids()
    {
        var form = await CreateTestFormAsync("name");
        var first = await Entries.CreateAsync(NewEntry(form.Id, form.VersionId, new NewEntryValue(form["name"], "first")));
        var second = await Entries.CreateAsync(NewEntry(form.Id, form.VersionId, new NewEntryValue(form["name"], "second"), new NewEntryValue(form["name"], "second, again", ValueIndex: 1)));

        // Ids no entry has go first, so both entries come in the last statement.
        var ids = Enumerable.Range(1, EntryRepository.IdsPerStatement + 500).Select(i => (long)-i).Concat([second!.Id, first!.Id, first.Id]).ToArray();
        var statements = new List<int>();
        var values = await Database.ReadAsync(async db =>
        {
            var counter = new ParameterCounter(statements);
            db.Interceptors.Add(counter);
            try
            {
                return await Entries.ListValuesAsync(ids);
            }
            finally
            {
                db.Interceptors.Remove(counter);
            }
        });

        Assert.Multiple(async () =>
        {
            Assert.That(statements, Is.EqualTo(new[] { 1000, 502 }), "each id once, at most 1,000 to a statement");
            Assert.That(values.Select(value => (value.EntryId, value.Text)), Is.EqualTo(new[] { (first.Id, "first"), (second.Id, "second"), (second.Id, "second, again") }), "by entry, field and item");
            Assert.That(await Entries.ListValuesAsync([]), Is.Empty);
        });
    }

    [Test]
    public async Task TextKeys_AreTheCleanedAndFoldedText_OfTextValuesOnly()
    {
        var form = await CreateTestFormAsync("city", "amount", "agree", "notes");
        var city = " " + (char)0x130 + "STANBUL" + (char)0x2003;
        var notes = new string('X', EntryTextKey.MaxLength + 44);

        var entry = await Entries.CreateAsync(NewEntry(
            form.Id,
            form.VersionId,
            new NewEntryValue(form["city"], city),
            new NewEntryValue(form["amount"], "12.50", Number: 12.5),
            new NewEntryValue(form["agree"], "Yes", Bool: true),
            new NewEntryValue(form["notes"], notes)));

        var keys = (await Entries.ListValuesAsync([entry!.Id])).ToDictionary(value => value.FieldId, value => value.TextKey);
        Assert.Multiple(() =>
        {
            Assert.That(keys[form["city"]], Is.EqualTo("istanbul"), "cleaned (trimmed) and folded as rules compare");
            Assert.That(keys[form["amount"]], Is.Null, "a number filters by its number");
            Assert.That(keys[form["agree"]], Is.Null, "a yes or no filters by its bool");
            Assert.That(keys[form["notes"]], Is.EqualTo(new string('x', EntryTextKey.MaxLength)), "cut to the column");
        });
    }

    [Test]
    public async Task AState_ChangesOnlyFromTheStateTheUserSaw()
    {
        var form = await CreateTestFormAsync();
        var entry = await Entries.CreateAsync(NewEntry(form.Id, form.VersionId));
        Clock.Advance(TimeSpan.FromMinutes(5));

        var approved = await Entries.ChangeStateAsync(entry!.Id, EntryState.Submitted, EntryState.Approved, Editor);
        Clock.Advance(TimeSpan.FromMinutes(5));
        var unchanged = await Entries.ChangeStateAsync(entry.Id, EntryState.Approved, EntryState.Approved, Editor);

        Assert.Multiple(async () =>
        {
            Assert.That(approved, Is.EqualTo(entry with { State = EntryState.Approved, StateChangedUtc = Now.AddMinutes(5), UpdatedUtc = Now.AddMinutes(5), StateChangedByUserKey = Editor }));
            Assert.That(unchanged, Is.EqualTo(approved), "a move to the state it is in changes nothing");
            Assert.That(
                async () => await Entries.ChangeStateAsync(entry.Id, EntryState.Submitted, EntryState.Rejected, Editor),
                Throws.InstanceOf<FormBuilderConcurrencyException>(),
                "approved meanwhile");
            Assert.That(
                async () => await Entries.ChangeStateAsync(entry.Id, EntryState.Spam, EntryState.Spam, Editor),
                Throws.InstanceOf<FormBuilderConcurrencyException>(),
                "not the state the user saw, even when nothing would change");
            Assert.That(await Entries.GetByIdAsync(entry.Id), Is.EqualTo(approved));
            Assert.That(await Entries.ChangeStateAsync(entry.Id + 1, EntryState.Submitted, EntryState.Spam, null), Is.Null);
            Assert.That(await Entries.ChangeStateAsync(entry.Id + 1, EntryState.Spam, EntryState.Spam, null), Is.Null);
        });
    }

    [Test]
    public async Task ReplacingValues_ChangesTheNamedFieldsOnly()
    {
        var form = await CreateTestFormAsync("name", "guests", "upload");
        var cv = await Entries.AddFileAsync(NewFile(form.Id));
        var entry = await Entries.CreateAsync(NewEntry(
            form.Id,
            form.VersionId,
            new NewEntryValue(form["name"], "Anna"),
            new NewEntryValue(form["guests"], "Ben", RowIndex: 0),
            new NewEntryValue(form["guests"], "Cleo", RowIndex: 1),
            new NewEntryValue(form["upload"], "cv.pdf", FileId: cv!.Id)) with
        {
            SearchText = "anna ben cleo",
            FileIds = [cv.Id],
        });
        Clock.Advance(TimeSpan.FromMinutes(5));

        var replaced = await Entries.ReplaceValuesAsync(entry!.Id, [form["guests"], form["upload"]], [new NewEntryValue(form["guests"], "Dana")], "anna dana");

        var values = await Entries.ListValuesAsync([entry.Id]);
        Assert.Multiple(() =>
        {
            Assert.That(replaced, Is.EqualTo(entry with { SearchText = "anna dana", UpdatedUtc = Now.AddMinutes(5) }));
            Assert.That(values.Select(value => (value.FieldId, value.Text)), Is.EqualTo(new[] { (form["name"], "Anna"), (form["guests"], "Dana") }), "the name is kept, the upload field emptied");
            Assert.That(values[1].TextKey, Is.EqualTo("dana"));
        });

        await Entries.ReplaceValuesAsync(entry.Id, [form["upload"]], [new NewEntryValue(form["upload"], "cv.pdf", FileId: cv.Id)], "anna dana");
        Assert.That((await Entries.ListValuesAsync([entry.Id])).Single(value => value.FieldId == form["upload"]).FileId, Is.EqualTo(cv.Id), "a value may name the entry's own file");
    }

    [Test]
    public async Task AReplaceThatIsRefused_ChangesNothing()
    {
        var form = await CreateTestFormAsync("name", "upload");
        var other = await CreateTestFormAsync("name");
        var entry = await Entries.CreateAsync(NewEntry(form.Id, form.VersionId, new NewEntryValue(form["name"], "Anna")) with { SearchText = "anna" });
        var neighbour = await Entries.AddFileAsync(NewFile(form.Id));
        await Entries.CreateAsync(NewEntry(form.Id, form.VersionId) with { FileIds = [neighbour!.Id] });
        var waiting = await Entries.AddFileAsync(NewFile(form.Id));

        Assert.Multiple(async () =>
        {
            AssertRefused(() => Entries.ReplaceValuesAsync(entry!.Id, [other["name"]], [new NewEntryValue(other["name"], "Ben")], "ben"), FormBuilderConflict.MissingField);
            AssertRefused(() => Entries.ReplaceValuesAsync(entry!.Id, [form["upload"]], [new NewEntryValue(form["upload"], "x.pdf", FileId: neighbour.Id)], "ben"), FormBuilderConflict.MissingFile, "another entry's file");
            AssertRefused(() => Entries.ReplaceValuesAsync(entry!.Id, [form["upload"]], [new NewEntryValue(form["upload"], "x.pdf", FileId: waiting!.Id)], "ben"), FormBuilderConflict.MissingFile, "a file no entry took");
            Assert.That(await Entries.GetByIdAsync(entry!.Id), Is.EqualTo(entry), "the search text too");
            Assert.That((await Entries.ListValuesAsync([entry.Id])).Select(value => value.Text), Is.EqualTo(new[] { "Anna" }));
            Assert.That(await Entries.ReplaceValuesAsync(entry.Id + 10, [form["name"]], [], string.Empty), Is.Null);
        });
    }

    [Test]
    public async Task AnEntry_IsDeletedWithItsValuesFilesEventsAndNotes()
    {
        var form = await CreateTestFormAsync("upload");
        var cv = await Entries.AddFileAsync(NewFile(form.Id));
        var entry = await Entries.CreateAsync(NewEntry(form.Id, form.VersionId, new NewEntryValue(form["upload"], "cv.pdf", FileId: cv!.Id)) with { FileIds = [cv.Id] });
        var kept = await Entries.CreateAsync(NewEntry(form.Id, form.VersionId, new NewEntryValue(form["upload"], "JFB_TEST")));
        await EntryEvents.AddAsync(new NewEntryEvent(entry!.Id, "submitted"));
        await EntryNotes.AddAsync(entry.Id, Editor, "Called back.");

        Assert.Multiple(async () =>
        {
            Assert.That(await Entries.DeleteAsync(entry.Id), Is.True);
            Assert.That(await Entries.DeleteAsync(entry.Id), Is.False);
            Assert.That(await Entries.GetByIdAsync(entry.Id), Is.Null);
            Assert.That(Count(FormBuilderTables.EntryValue), Is.EqualTo(1), "the other entry's value stays");
            Assert.That(Count(FormBuilderTables.EntryFile), Is.Zero);
            Assert.That(Count(FormBuilderTables.EntryEvent), Is.Zero);
            Assert.That(Count(FormBuilderTables.EntryNote), Is.Zero);
            Assert.That(await Entries.GetByIdAsync(kept!.Id), Is.EqualTo(kept));
        });
    }

    [Test]
    public async Task AFile_IsRecordedOnce_AndWaitsForItsEntry()
    {
        var form = await CreateTestFormAsync();
        var file = NewFile(form.Id, "Lebenslauf.pdf");

        var stored = await Entries.AddFileAsync(file);

        Assert.Multiple(async () =>
        {
            Assert.That(stored, Is.EqualTo(new StoredEntryFile(stored!.Id, file.Key, null, form.Id, file.FieldKey, file.OriginalName, file.StoredName, file.ContentType, file.SizeBytes, file.Sha256, file.ScanState, Now)));
            Assert.That(await Entries.GetFileAsync(file.Key), Is.EqualTo(stored));
            Assert.That(await Entries.GetFileAsync(Guid.NewGuid()), Is.Null);
            AssertRefused(() => Entries.AddFileAsync(NewFile(form.Id) with { Key = file.Key }), FormBuilderConflict.DuplicateKey);
            Assert.That(await Entries.AddFileAsync(NewFile(int.MaxValue)), Is.Null, "no form");
            Assert.That(Count(FormBuilderTables.EntryFile), Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Orphans_AreListedOldestFirst_BeforeTheCutOff()
    {
        var form = await CreateTestFormAsync();
        var oldest = await Entries.AddFileAsync(NewFile(form.Id));
        Clock.Advance(TimeSpan.FromHours(1));
        var older = await Entries.AddFileAsync(NewFile(form.Id));
        var taken = await Entries.AddFileAsync(NewFile(form.Id));
        await Entries.CreateAsync(NewEntry(form.Id, form.VersionId) with { FileIds = [taken!.Id] });
        Clock.Advance(TimeSpan.FromHours(1));
        await Entries.AddFileAsync(NewFile(form.Id));

        var cutOff = Now.AddHours(2);
        Assert.Multiple(async () =>
        {
            Assert.That((await Entries.ListOrphanFilesAsync(cutOff, 10)).Select(file => file.Id), Is.EqualTo(new[] { oldest!.Id, older!.Id }), "not the taken file, not the one at the cut-off");
            Assert.That((await Entries.ListOrphanFilesAsync(cutOff, 1)).Select(file => file.Id), Is.EqualTo(new[] { oldest.Id }));
        });
    }

    [Test]
    public async Task AScan_MovesOnOnlyFromTheStateItWasSeenIn()
    {
        var form = await CreateTestFormAsync();
        var file = await Entries.AddFileAsync(NewFile(form.Id));

        var clean = await Entries.ChangeScanStateAsync(file!.Id, "Pending", "Clean");

        Assert.Multiple(async () =>
        {
            Assert.That(clean, Is.EqualTo(file with { ScanState = "Clean" }));
            Assert.That(await Entries.ChangeScanStateAsync(file.Id, "Clean", "Clean"), Is.EqualTo(clean));
            Assert.That(async () => await Entries.ChangeScanStateAsync(file.Id, "Pending", "Infected"), Throws.InstanceOf<FormBuilderConcurrencyException>());
            Assert.That(async () => await Entries.ChangeScanStateAsync(file.Id, "Pending", "Pending"), Throws.InstanceOf<FormBuilderConcurrencyException>());
            Assert.That(await Entries.GetFileAsync(file.Key), Is.EqualTo(clean));
            Assert.That(await Entries.ChangeScanStateAsync(file.Id + 1, "Pending", "Clean"), Is.Null);
        });
    }

    [Test]
    public async Task AFile_IsDeletedOnlyWhileNoValueNamesIt()
    {
        var form = await CreateTestFormAsync("upload");
        var cv = await Entries.AddFileAsync(NewFile(form.Id));
        var entry = await Entries.CreateAsync(NewEntry(form.Id, form.VersionId, new NewEntryValue(form["upload"], "cv.pdf", FileId: cv!.Id)) with { FileIds = [cv.Id] });

        AssertRefused(() => Entries.DeleteFileAsync(cv.Id, entry!.Id), FormBuilderConflict.InUse);
        await Entries.ReplaceValuesAsync(entry!.Id, [form["upload"]], [], string.Empty);

        Assert.Multiple(async () =>
        {
            Assert.That(await Entries.DeleteFileAsync(cv.Id, entry.Id), Is.True, "once no value names it");
            Assert.That(await Entries.DeleteFileAsync(cv.Id, entry.Id), Is.False);
            Assert.That(await Entries.ListFilesAsync(entry.Id), Is.Empty);
        });
    }

    [Test]
    public async Task AFile_IsDeletedOnlyFromWhereItWasSeen()
    {
        var form = await CreateTestFormAsync();
        var orphan = await Entries.AddFileAsync(NewFile(form.Id));
        var taken = await Entries.AddFileAsync(NewFile(form.Id));
        var entry = await Entries.CreateAsync(NewEntry(form.Id, form.VersionId) with { FileIds = [taken!.Id] });

        Assert.Multiple(async () =>
        {
            Assert.That(async () => await Entries.DeleteFileAsync(taken.Id, null), Throws.InstanceOf<FormBuilderConcurrencyException>(), "the cleanup listed it, then an entry took it");
            Assert.That(async () => await Entries.DeleteFileAsync(taken.Id, entry!.Id + 1), Throws.InstanceOf<FormBuilderConcurrencyException>(), "another entry's");
            Assert.That(async () => await Entries.DeleteFileAsync(orphan!.Id, entry!.Id), Throws.InstanceOf<FormBuilderConcurrencyException>(), "no entry's yet");
            Assert.That(await Entries.GetFileAsync(taken.Key), Is.Not.Null);
            Assert.That(await Entries.DeleteFileAsync(orphan!.Id, null), Is.True, "an orphan, by the cleanup");
            Assert.That(await Entries.DeleteFileAsync(taken.Id, entry!.Id), Is.True, "the entry's, when no value names it");
            Assert.That(await Entries.DeleteFileAsync(taken.Id, entry.Id), Is.False);
        });
    }

    [Test]
    public async Task AConsentWording_IsStoredOnce_UnderTheHashOfItsCultureAndText()
    {
        const string Text = "I agree to the privacy policy. JFB_TEST";

        var hash = await Entries.AddConsentTextAsync("en", Text);
        Clock.Advance(TimeSpan.FromDays(1));
        var again = await Entries.AddConsentTextAsync("en", Text);
        var invariant = await Entries.AddConsentTextAsync(string.Empty, Text);

        Assert.Multiple(async () =>
        {
            Assert.That(hash, Is.EqualTo(ConsentTextHash.For("en", Text)));
            Assert.That(again, Is.EqualTo(hash));
            Assert.That(invariant, Is.Not.EqualTo(hash), "the culture is part of the name");
            Assert.That(await Entries.GetConsentTextAsync(hash), Is.EqualTo(new StoredConsentText(hash, "en", Text, Now)), "first seen when first stored");
            Assert.That(await Entries.GetConsentTextAsync(invariant), Is.EqualTo(new StoredConsentText(invariant, string.Empty, Text, Now.AddDays(1))));
            Assert.That(await Entries.GetConsentTextAsync(new string('0', 64)), Is.Null);
            Assert.That(Count(FormBuilderTables.ConsentText), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task TheLongestValues_TheColumnsHold_AreStored()
    {
        var form = await CreateTestFormAsync();
        var entry = NewEntry(form.Id, form.VersionId) with
        {
            Culture = new string('c', EntryRepository.MaxCultureLength),
            PageUrl = "/" + new string('p', EntryRepository.MaxPageUrlLength - 1),
            IpAddress = new string('1', EntryRepository.MaxIpAddressLength),
            SpamReasons = new string('r', EntryRepository.MaxSpamReasonsLength),
        };
        var file = NewFile(form.Id) with
        {
            OriginalName = new string('n', EntryRepository.MaxOriginalNameLength),
            ContentType = new string('t', EntryRepository.MaxContentTypeLength),
            ScanState = new string('S', EntryRepository.MaxScanStateLength),
        };

        var stored = await Entries.CreateAsync(entry);
        var storedFile = await Entries.AddFileAsync(file);
        var hash = await Entries.AddConsentTextAsync(new string('c', ConsentTextHash.MaxCultureLength), "JFB_TEST");

        Assert.Multiple(async () =>
        {
            Assert.That(stored, Is.EqualTo(Expected(entry, stored!.Id, 1)));
            Assert.That(storedFile?.OriginalName, Is.EqualTo(file.OriginalName));
            Assert.That(storedFile?.ContentType, Is.EqualTo(file.ContentType));
            Assert.That(storedFile?.ScanState, Is.EqualTo(file.ScanState));
            Assert.That((await Entries.GetConsentTextAsync(hash))?.Culture, Is.EqualTo(new string('c', ConsentTextHash.MaxCultureLength)));
        });
    }

    [Test]
    public async Task AnEntry_ItsEventAndItsConsent_ShareOneUnitOfWork()
    {
        var form = await CreateTestFormAsync();

        var stored = await Database.WriteAsync(async _ =>
        {
            var entry = await Entries.CreateAsync(NewEntry(form.Id, form.VersionId));
            await Entries.AddConsentTextAsync("en", "I agree. JFB_TEST");
            await EntryEvents.AddAsync(new NewEntryEvent(entry!.Id, "submitted"));
            return entry;
        });
        Assert.That(
            async () => await Database.WriteAsync<int>(async _ =>
            {
                var entry = await Entries.CreateAsync(NewEntry(form.Id, form.VersionId));
                await Entries.AddConsentTextAsync("en", "Another wording. JFB_TEST");
                await EntryEvents.AddAsync(new NewEntryEvent(entry!.Id, "submitted"));
                throw new InvalidOperationException("JFB_TEST: a later step of the submission failed");
            }),
            Throws.InvalidOperationException);

        Assert.Multiple(async () =>
        {
            Assert.That(Count(FormBuilderTables.Entry), Is.EqualTo(1));
            Assert.That(Count(FormBuilderTables.ConsentText), Is.EqualTo(1));
            Assert.That((await EntryEvents.ListForEntryAsync(stored!.Id)).Select(entryEvent => entryEvent.Kind), Is.EqualTo(new[] { "submitted" }));
            Assert.That((await Forms.GetByIdAsync(form.Id))!.LastEntryNumber, Is.EqualTo(1), "the failed unit's number went back with it");
        });
    }

    /// <summary>
    /// Asserts that a change whose statement's own conditions are lifted, as for a writer that got past them at the same
    /// moment on SQL Server, is refused by the unique index for <paramref name="conflict"/>, with the index's error inside.
    /// </summary>
    private void AssertBackstop<T>(Func<Task<T>> change, FormBuilderConflict conflict)
    {
        var rewriter = new CommandRewriter((char)10 + "WHERE EXISTS", (char)10 + "WHERE 1 = 1 OR EXISTS");
        Assert.That(
            async () => await WatchedAsync(rewriter, change),
            Throws.InstanceOf<FormBuilderConflictException>()
                .With.Property(nameof(FormBuilderConflictException.Conflict)).EqualTo(conflict)
                .And.Property(nameof(Exception.InnerException)).Not.Null,
            conflict.ToString());
        Assert.That(rewriter.Rewritten, Is.EqualTo(1), "the insert's conditions were lifted");
    }

    internal static NewEntryFile NewFile(int formId, string name = "JFB_TEST.pdf") =>
        new(Guid.NewGuid(), formId, Guid.NewGuid(), name, Hex(16), "application/pdf", 1234, Hex(32), "Pending");

    private static StoredEntry Expected(NewEntry entry, long id, int number) => new()
    {
        Id = id,
        Key = entry.Key,
        FormId = entry.FormId,
        FormVersionId = entry.FormVersionId,
        Number = number,
        State = entry.State,
        IsTest = entry.IsTest,
        SubmissionId = entry.SubmissionId,
        Digest = entry.Digest,
        Culture = entry.Culture,
        ContentKey = entry.ContentKey,
        PageUrl = entry.PageUrl,
        MemberKey = entry.MemberKey,
        IpAddress = entry.IpAddress,
        SpamScore = entry.SpamScore,
        SpamReasons = entry.SpamReasons,
        SearchText = entry.SearchText,
        Context = entry.Context,
        DurationSeconds = entry.DurationSeconds,
        SubmittedUtc = Now,
        UpdatedUtc = Now,
        StateChangedUtc = Now,
        StateChangedByUserKey = null,
        FirstSeenUtc = entry.FirstSeenUtc,
        VerifiedUtc = entry.VerifiedUtc,
        ResumeTokenHash = entry.ResumeTokenHash,
        ResumeExpiresUtc = entry.ResumeExpiresUtc,
        CurrentPageKey = entry.CurrentPageKey,
        ChallengeId = entry.ChallengeId,
        HoldUntilUtc = null,
        HoldReason = null,
        PurgeAfterUtc = entry.PurgeAfterUtc,
    };

    private static (int FieldId, int RowIndex, int ValueIndex, string? Text, string? TextKey, double? Number, DateTime? Date, bool? Bool, int? FileId) Plain(StoredEntryValue value) =>
        (value.FieldId, value.RowIndex, value.ValueIndex, value.Text, value.TextKey, value.Number, value.Date, value.Bool, value.FileId);

    private async Task<TestForm> CreateTestFormAsync(params string[] fields)
    {
        var form = await CreateFormAsync();
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var alias in fields)
        {
            ids[alias] = (await Forms.AddFieldAsync(form.Id, Field(alias)))!.Id;
        }

        return new TestForm(form.Id, form.DraftVersionId!.Value, ids);
    }

    /// <summary>A form for entries: its id, its draft version and the catalogue rows of its fields by alias.</summary>
    private sealed record TestForm(int Id, int VersionId, IReadOnlyDictionary<string, int> Fields)
    {
        public int this[string alias] => Fields[alias];
    }
}
