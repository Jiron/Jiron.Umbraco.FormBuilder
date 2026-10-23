using System.Security.Cryptography;
using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Umbraco.Cms.Infrastructure.Persistence;
using Umbraco.Cms.Tests.Common.Testing;
using Schema = Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations.CreateSchemaV1;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence;

/// <summary>
/// The constraints of schema v1 do what the design says (section 4.5, ADR-0003, ADR-0033): storage types, the filtered unique
/// indexes, uniqueness, cascades, set-null and the deletes that are refused. SQLite here; the <c>sqlserver</c> job in
/// ci.yml runs the same tests against SQL Server.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class SchemaConstraintTests : FormBuilderMigrationTestBase
{
    [SetUp]
    public async Task MigrateAsync()
    {
        var result = await RunAsync(new FormBuilderMigrationPlan());
        Assert.That(result.Successful, Is.True, result.Exception?.ToString());
    }

    [Test]
    public void Values_StoreNumbersAsDoublesAndDatesAsText_OnSqlite_AndAsFloatAndDatetime_OnSqlServer()
    {
        var form = AddPublishedForm();
        var entry = AddEntry(form);
        var date = new DateTime(2026, 10, 23, 8, 30, 15, DateTimeKind.Utc);
        long fractional = 0, whole = 0;
        Write(db =>
        {
            fractional = AddValue(db, entry, form.FieldId, valueIndex: 0, number: 1234567890.12345, date: date);
            whole = AddValue(db, entry, form.FieldId, valueIndex: 1, number: 3);
        });

        var stored = Query(db => db.Fetch<Schema.EntryValueDto>($"SELECT * FROM {T(FormBuilderTables.EntryValue)} ORDER BY {Q("id")}"));
        Assert.Multiple(() =>
        {
            if (IsSqlite)
            {
                // Umbraco maps decimal to TEXT on SQLite, which sorts numbers as text (ADR-0003); a double is REAL.
                var types = Query(db => db.Fetch<StorageTypes>(
                    $"SELECT typeof({Q("number")}) AS NumberType, typeof({Q("date")}) AS DateType FROM {T(FormBuilderTables.EntryValue)} WHERE {Q("id")} IN (@0, @1) ORDER BY {Q("id")}",
                    fractional,
                    whole));
                Assert.That(types.Select(t => t.NumberType), Is.EqualTo(new[] { "real", "real" }), "a whole number is a real too");
                Assert.That(types[0].DateType, Is.EqualTo("text"));
            }
            else
            {
                var types = Query(db => db.Fetch<string>(
                    "SELECT DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = @0 AND COLUMN_NAME IN (@1, @2) ORDER BY COLUMN_NAME",
                    FormBuilderTables.EntryValue,
                    "date",
                    "number"));
                Assert.That(types, Is.EqualTo(new[] { "datetime", "float" }));
            }

            Assert.That(stored.Select(value => value.Number), Is.EqualTo(new double?[] { 1234567890.12345, 3 }), "15 significant digits come back exactly");
            Assert.That(stored[0].Date, Is.EqualTo(date));
        });
    }

    [Test]
    public void Entries_WithoutAChallenge_AreStored_ARepeatedChallengeIsRefused_AndAnotherFormMayUseIt()
    {
        var form = AddPublishedForm();
        var other = AddPublishedForm();
        var challenge = Hex(16);

        AddEntry(form);
        AddEntry(form);
        AddEntry(form, challengeId: challenge);

        Assert.Multiple(() =>
        {
            Assert.That(Count(FormBuilderTables.Entry, $"{Q("challengeId")} IS NULL"), Is.EqualTo(2), "two entries without a challenge");
            Assert.That(() => AddEntry(form, challengeId: challenge), Throws.Exception, "one entry per challenge and form");
            Assert.That(() => AddEntry(other, challengeId: challenge), Throws.Nothing, "the index is per form");
            Assert.That(Count(FormBuilderTables.Entry), Is.EqualTo(4));
        });
    }

    [Test]
    public void Entries_WithoutAResumeToken_AreStored_AndARepeatedTokenHashIsRefused()
    {
        var form = AddPublishedForm();
        var token = Hex(32);

        AddEntry(form);
        AddEntry(form);
        AddEntry(form, resumeTokenHash: token);

        Assert.Multiple(() =>
        {
            Assert.That(() => AddEntry(form, resumeTokenHash: token), Throws.Exception, "within the form");
            Assert.That(() => AddEntry(AddPublishedForm(), resumeTokenHash: token), Throws.Exception, "and across forms");
            Assert.That(Count(FormBuilderTables.Entry), Is.EqualTo(3));
        });
    }

    [Test]
    public void Entries_KeysNumbersAndSubmissionIds_AreUnique_NumbersAndSubmissionsWithinTheirForm()
    {
        var form = AddPublishedForm();
        var other = AddPublishedForm();
        var first = Query(db => db.Single<Schema.EntryDto>($"SELECT * FROM {T(FormBuilderTables.Entry)} WHERE {Q("id")} = @0", AddEntry(form, number: 1)));

        Assert.Multiple(() =>
        {
            Assert.That(() => AddEntry(form, number: 1), Throws.Exception, "number");
            Assert.That(() => AddEntry(form, number: 2, submissionId: first.SubmissionId), Throws.Exception, "submission id");
            Assert.That(() => AddEntry(form, number: 3, key: first.Key), Throws.Exception, "key");
            Assert.That(() => AddEntry(other, number: 1, submissionId: first.SubmissionId), Throws.Nothing, "another form counts and dedupes on its own");
            Assert.That(Count(FormBuilderTables.Entry), Is.EqualTo(2));
        });
    }

    [Test]
    public void DeletingAnEntry_DeletesItsValuesFilesEventsNotesAndActionRuns_AndKeepsItsEmailsWithoutTheLink()
    {
        var form = AddPublishedForm();
        var entry = AddEntry(form);
        var kept = AddEntry(form);
        var now = DateTime.UtcNow;
        Write(db =>
        {
            var file = AddFile(db, form.FormId, entry);
            AddValue(db, entry, form.FieldId, valueIndex: 0, fileId: file);
            AddValue(db, kept, form.FieldId, valueIndex: 0, text: "kept");
            db.Insert(new Schema.EntryEventDto { EntryId = entry, OccurredUtc = now, Kind = "submitted" });
            db.Insert(new Schema.EntryNoteDto { EntryId = entry, UserKey = Guid.NewGuid(), Body = "Called back.", CreatedUtc = now, UpdatedUtc = now });
            var run = new Schema.ActionRunDto { EntryId = entry, FormVersionId = form.VersionId, ActionKey = Guid.NewGuid(), ActionType = "email", TriggerKind = "submitted", State = "Succeeded", CreatedUtc = now };
            db.Insert(run);
            db.Insert(new Schema.EmailDto { Kind = "Action", EntryId = entry, ActionRunId = run.Id, ToAddresses = "team@jfb.test", FromAddress = "site@jfb.test", Subject = "New entry", TextBody = "t", Culture = "en-US", State = "Sent", CreatedUtc = now });
        });

        Write(db => db.Execute($"DELETE FROM {T(FormBuilderTables.Entry)} WHERE {Q("id")} = @0", entry));

        Assert.Multiple(() =>
        {
            foreach (var table in new[] { FormBuilderTables.EntryFile, FormBuilderTables.EntryEvent, FormBuilderTables.EntryNote, FormBuilderTables.ActionRun })
            {
                Assert.That(Count(table), Is.Zero, table);
            }

            Assert.That(Count(FormBuilderTables.EntryValue), Is.EqualTo(1), "only the other entry's value is left");
            Assert.That(Count(FormBuilderTables.EntryValue, $"{Q("entryId")} = @0", kept), Is.EqualTo(1));
            Assert.That(Count(FormBuilderTables.Email, $"{Q("entryId")} IS NULL"), Is.EqualTo(1), "the mail stays until its purge, unlinked");
        });
    }

    [Test]
    public void DeletingAForm_WithEntries_IsRefused_AndWithoutThem_DeletesItsVersionsFieldsAndCapacities()
    {
        var form = AddPublishedForm();
        var entry = AddEntry(form);
        var other = AddPublishedForm();
        Write(db => db.Insert(new Schema.OptionCapacityDto { FormId = form.FormId, FieldKey = Guid.NewGuid(), ValueHash = Sha256("Tuesday"), OptionValue = "Tuesday", Taken = 3, UpdatedUtc = DateTime.UtcNow }));

        Assert.That(() => DeleteForm(form.FormId), Throws.Exception, "entries are deleted before their form");

        Write(db => db.Execute($"DELETE FROM {T(FormBuilderTables.Entry)} WHERE {Q("id")} = @0", entry));
        DeleteForm(form.FormId);

        Assert.Multiple(() =>
        {
            Assert.That(Count(FormBuilderTables.Form), Is.EqualTo(1));
            Assert.That(Count(FormBuilderTables.FormVersion, $"{Q("formId")} = @0", form.FormId), Is.Zero, "the draft and published versions it pointed at");
            Assert.That(Count(FormBuilderTables.FormField, $"{Q("formId")} = @0", form.FormId), Is.Zero);
            Assert.That(Count(FormBuilderTables.OptionCapacity), Is.Zero);
            Assert.That(Count(FormBuilderTables.FormVersion, $"{Q("formId")} = @0", other.FormId), Is.EqualTo(1), "another form keeps its own");
        });
    }

    [Test]
    public void FormVersion_ThatTheFormOrAnEntryPointsAt_CannotBeDeleted_AndAnUnusedOneCan()
    {
        var form = AddPublishedForm();
        var used = AddVersion(form.FormId, number: 2);
        var unused = AddVersion(form.FormId, number: 3);
        AddEntry(form, versionId: used);

        Assert.Multiple(() =>
        {
            Assert.That(() => DeleteVersion(form.VersionId), Throws.Exception, "the form's draft and published version");
            Assert.That(() => DeleteVersion(used), Throws.Exception, "a version an entry was submitted with");
            Assert.That(() => DeleteVersion(unused), Throws.Nothing, "a pruned draft save");
            Assert.That(Count(FormBuilderTables.FormVersion), Is.EqualTo(2));
            Assert.That(() => AddVersion(form.FormId, number: 2), Throws.Exception, "numbers are unique per form");
        });
    }

    [Test]
    public void FormField_ThatHasValues_CannotBeDeleted()
    {
        var form = AddPublishedForm();
        var entry = AddEntry(form);
        Write(db => AddValue(db, entry, form.FieldId, valueIndex: 0, text: "anna@jfb.test"));

        Assert.Multiple(() =>
        {
            Assert.That(() => Write(db => db.Execute($"DELETE FROM {T(FormBuilderTables.FormField)} WHERE {Q("id")} = @0", form.FieldId)), Throws.Exception);
            Assert.That(() => AddField(form.FormId, form.FieldKey), Throws.Exception, "a field key is unique per form");
            Assert.That(() => AddField(AddPublishedForm().FormId, form.FieldKey), Throws.Nothing, "a copied form keeps the keys");
        });
    }

    [Test]
    public void EntryFile_ThatAValueNames_CannotBeDeletedAlone_AndAnOrphanCan()
    {
        var form = AddPublishedForm();
        var entry = AddEntry(form);
        int named = 0, orphan = 0;
        Write(db =>
        {
            named = AddFile(db, form.FormId, entry);
            orphan = AddFile(db, form.FormId, entryId: null);
            AddValue(db, entry, form.FieldId, valueIndex: 0, fileId: named);
        });

        Assert.Multiple(() =>
        {
            Assert.That(() => DeleteFile(named), Throws.Exception);
            Assert.That(() => DeleteFile(orphan), Throws.Nothing, "the upload cleanup");
            Assert.That(Count(FormBuilderTables.EntryFile), Is.EqualTo(1));
        });
    }

    [Test]
    public void Folder_WithASubfolderOrAForm_CannotBeDeleted()
    {
        var parent = AddFolder(parentId: null);
        var child = AddFolder(parent);
        var withForm = AddFolder(parentId: null);
        var now = DateTime.UtcNow;
        Write(db => db.Insert(new Schema.FormDto { Key = Guid.NewGuid(), FolderId = withForm, Alias = "inFolder", Name = "In a folder", CreatedUtc = now, UpdatedUtc = now }));

        Assert.Multiple(() =>
        {
            Assert.That(() => DeleteFolder(parent), Throws.Exception, "a folder with a subfolder");
            Assert.That(() => DeleteFolder(withForm), Throws.Exception, "a folder with a form");
            Assert.That(() => DeleteFolder(child), Throws.Nothing);
            Assert.That(() => DeleteFolder(parent), Throws.Nothing, "empty now");
        });
    }

    [Test]
    public void Aliases_AreUnique_WithoutRegardToCase_OnBothDatabases()
    {
        var now = DateTime.UtcNow;
        Write(db =>
        {
            db.Insert(new Schema.FormDto { Key = Guid.NewGuid(), Alias = "contactForm", Name = "Contact", CreatedUtc = now, UpdatedUtc = now });
            db.Insert(new Schema.OptionListDto { Key = Guid.NewGuid(), Alias = "countries", Name = "Countries", Kind = "Countries", CreatedUtc = now, UpdatedUtc = now });
            db.Insert(new Schema.ThemeDto { Key = Guid.NewGuid(), Alias = "brand", Name = "Brand", BaseTheme = "default", CreatedUtc = now, UpdatedUtc = now });
            db.Insert(new Schema.EmailLayoutDto { Key = Guid.NewGuid(), Alias = "newsletter", Name = "Newsletter", CreatedUtc = now, UpdatedUtc = now });
        });

        Assert.Multiple(() =>
        {
            foreach (var alias in new[] { "contactForm", "contactform", "ContactForm" })
            {
                Assert.That(() => Write(db => db.Insert(new Schema.FormDto { Key = Guid.NewGuid(), Alias = alias, Name = "Again", CreatedUtc = now, UpdatedUtc = now })), Throws.Exception, $"form {alias}");
            }

            Assert.That(() => Write(db => db.Insert(new Schema.OptionListDto { Key = Guid.NewGuid(), Alias = "Countries", Name = "Again", Kind = "Manual", CreatedUtc = now, UpdatedUtc = now })), Throws.Exception, "option list");
            Assert.That(() => Write(db => db.Insert(new Schema.ThemeDto { Key = Guid.NewGuid(), Alias = "BRAND", Name = "Again", BaseTheme = "default", CreatedUtc = now, UpdatedUtc = now })), Throws.Exception, "theme");
            Assert.That(() => Write(db => db.Insert(new Schema.EmailLayoutDto { Key = Guid.NewGuid(), Alias = "Newsletter", Name = "Again", CreatedUtc = now, UpdatedUtc = now })), Throws.Exception, "email layout");
            Assert.That(() => Write(db => db.Insert(new Schema.FormDto { Key = Guid.NewGuid(), Alias = "contactForm2", Name = "Another", CreatedUtc = now, UpdatedUtc = now })), Throws.Nothing);
        });
    }

    [Test]
    public void CompositeKeys_RefuseASecondRow()
    {
        var form = AddPublishedForm();
        var group = Guid.NewGuid();
        var user = Guid.NewGuid();
        var content = Guid.NewGuid();
        var day = new DateTime(2026, 10, 23, 0, 0, 0, DateTimeKind.Utc);
        var now = DateTime.UtcNow;
        var hash = Sha256("Tuesday");
        Write(db =>
        {
            db.Insert(new Schema.PermissionDto { UserGroupKey = group, TargetType = "Global", TargetKey = Guid.Empty, Permissions = 1 });
            db.Insert(new Schema.OptionCapacityDto { FormId = form.FormId, FieldKey = form.FieldKey, ValueHash = hash, OptionValue = "Tuesday", UpdatedUtc = now });
            db.Insert(new Schema.UserSettingDto { UserKey = user, Name = "columns", UpdatedUtc = now });
            db.Insert(new Schema.ReferenceDto { FormId = form.FormId, ContentKey = content, PropertyAlias = "form", Culture = string.Empty, UpdatedUtc = now });
            db.Insert(new Schema.DailyStatDto { Day = day, FormId = form.FormId, Metric = "views", Dimension = string.Empty, Value = 1 });
        });

        Assert.Multiple(() =>
        {
            Assert.That(() => Write(db => db.Insert(new Schema.SettingDto { Scope = CreateDefaultSettingsV1.GlobalScope, Section = "general", SchemaVersion = 1, Version = 1, UpdatedUtc = now })), Throws.Exception, "setting");
            Assert.That(() => Write(db => db.Insert(new Schema.PermissionDto { UserGroupKey = group, TargetType = "Global", TargetKey = Guid.Empty, Permissions = 2 })), Throws.Exception, "permission");
            Assert.That(() => Write(db => db.Insert(new Schema.OptionCapacityDto { FormId = form.FormId, FieldKey = form.FieldKey, ValueHash = hash, OptionValue = "Tuesday", UpdatedUtc = now })), Throws.Exception, "capacity");
            Assert.That(() => Write(db => db.Insert(new Schema.UserSettingDto { UserKey = user, Name = "columns", UpdatedUtc = now })), Throws.Exception, "user setting");
            Assert.That(() => Write(db => db.Insert(new Schema.ReferenceDto { FormId = form.FormId, ContentKey = content, PropertyAlias = "form", Culture = string.Empty, UpdatedUtc = now })), Throws.Exception, "reference");
            Assert.That(() => Write(db => db.Insert(new Schema.DailyStatDto { Day = day, FormId = form.FormId, Metric = "views", Dimension = string.Empty, Value = 2 })), Throws.Exception, "daily stat");

            // Values that SQL Server's collation may fold together count apart, because the key is their hash.
            Assert.That(() => Write(db => db.Insert(new Schema.OptionCapacityDto { FormId = form.FormId, FieldKey = form.FieldKey, ValueHash = Sha256("tuesday"), OptionValue = "tuesday", UpdatedUtc = now })), Throws.Nothing, "another value");
            Assert.That(() => Write(db => db.Insert(new Schema.SettingDto { Scope = Guid.NewGuid().ToString("D"), Section = "general", SchemaVersion = 1, Version = 1, UpdatedUtc = now })), Throws.Nothing, "a site's own row");
        });
    }

    private void DeleteForm(int id) => Write(db => db.Execute($"DELETE FROM {T(FormBuilderTables.Form)} WHERE {Q("id")} = @0", id));

    private void DeleteVersion(int id) => Write(db => db.Execute($"DELETE FROM {T(FormBuilderTables.FormVersion)} WHERE {Q("id")} = @0", id));

    private void DeleteFile(int id) => Write(db => db.Execute($"DELETE FROM {T(FormBuilderTables.EntryFile)} WHERE {Q("id")} = @0", id));

    private void DeleteFolder(int id) => Write(db => db.Execute($"DELETE FROM {T(FormBuilderTables.Folder)} WHERE {Q("id")} = @0", id));

    /// <summary>A form with one version that is both its draft and its published version, and one field.</summary>
    private PublishedForm AddPublishedForm()
    {
        var now = DateTime.UtcNow;
        var form = new Schema.FormDto { Key = Guid.NewGuid(), Alias = "f" + Hex(8), Name = "Contact", CreatedUtc = now, UpdatedUtc = now };
        Write(db => db.Insert(form));
        var version = AddVersion(form.Id, number: 1);
        Write(db => db.Execute(
            $"UPDATE {T(FormBuilderTables.Form)} SET {Q("draftVersionId")} = @0, {Q("publishedVersionId")} = @0, {Q("publicRevision")} = 1 WHERE {Q("id")} = @1",
            version,
            form.Id));
        var fieldKey = Guid.NewGuid();
        return new PublishedForm(form.Id, version, AddField(form.Id, fieldKey), fieldKey);
    }

    private int AddVersion(int formId, int number)
    {
        var version = new Schema.FormVersionDto
        {
            FormId = formId,
            Number = number,
            Kind = "Published",
            SchemaVersion = 1,
            Definition = "{}",
            DefinitionHash = Sha256("{}"),
            PublicHash = Sha256("{}"),
            PublicRevision = 1,
            CreatedUtc = DateTime.UtcNow,
        };
        Write(db => db.Insert(version));
        return version.Id;
    }

    private int AddField(int formId, Guid fieldKey)
    {
        var field = new Schema.FormFieldDto { FormId = formId, FieldKey = fieldKey, Alias = "email", Label = "Email", Type = "email", StorageKind = "text", FirstVersionNumber = 1 };
        Write(db => db.Insert(field));
        return field.Id;
    }

    private long AddEntry(PublishedForm form, int? number = null, int? versionId = null, string? challengeId = null, string? resumeTokenHash = null, Guid? submissionId = null, Guid? key = null)
    {
        var now = DateTime.UtcNow;
        var entry = new Schema.EntryDto
        {
            Key = key ?? Guid.NewGuid(),
            FormId = form.FormId,
            FormVersionId = versionId ?? form.VersionId,
            Number = number ?? Count(FormBuilderTables.Entry) + 1,
            State = "New",
            SubmissionId = submissionId ?? Guid.NewGuid(),
            Digest = Hex(32),
            Culture = "en-US",
            SpamReasons = "[]",
            ChallengeId = challengeId,
            ResumeTokenHash = resumeTokenHash,
            SubmittedUtc = now,
            UpdatedUtc = now,
            StateChangedUtc = now,
        };
        Write(db => db.Insert(entry));
        return entry.Id;
    }

    private static long AddValue(IUmbracoDatabase db, long entryId, int fieldId, int valueIndex, string? text = null, double? number = null, DateTime? date = null, int? fileId = null)
    {
        var value = new Schema.EntryValueDto { EntryId = entryId, FieldId = fieldId, ValueIndex = valueIndex, Text = text, Number = number, Date = date, FileId = fileId };
        db.Insert(value);
        return value.Id;
    }

    private static int AddFile(IUmbracoDatabase db, int formId, long? entryId)
    {
        var file = new Schema.EntryFileDto
        {
            Key = Guid.NewGuid(),
            EntryId = entryId,
            FormId = formId,
            FieldKey = Guid.NewGuid(),
            OriginalName = "cv.pdf",
            StoredName = Hex(16),
            ContentType = "application/pdf",
            SizeBytes = 1024,
            Sha256 = Hex(32),
            ScanState = "Clean",
            CreatedUtc = DateTime.UtcNow,
        };
        db.Insert(file);
        return file.Id;
    }

    private int AddFolder(int? parentId)
    {
        var now = DateTime.UtcNow;
        var folder = new Schema.FolderDto { Key = Guid.NewGuid(), ParentId = parentId, Name = "Folder", CreatedUtc = now, UpdatedUtc = now };
        Write(db => db.Insert(folder));
        return folder.Id;
    }

    private static string Hex(int bytes) => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(bytes));

    private static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private sealed record PublishedForm(int FormId, int VersionId, int FieldId, Guid FieldKey);

    private sealed class StorageTypes
    {
        public string NumberType { get; set; } = string.Empty;

        public string DateType { get; set; } = string.Empty;
    }
}
