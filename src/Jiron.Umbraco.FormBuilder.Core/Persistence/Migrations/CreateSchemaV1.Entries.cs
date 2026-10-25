using System.Data;
using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;

// Frozen snapshots of the entry tables as step 1 creates them (ADR-0003). Never edit.
internal sealed partial class CreateSchemaV1
{
    // One submission. The two filtered unique indexes (challengeId, resumeTokenHash) are in FilteredIndexes.
    [TableName(FormBuilderTables.Entry)]
    [PrimaryKey("id", AutoIncrement = true)]
    [ExplicitColumns]
    internal sealed class EntryDto
    {
        [Column("id")]
        [PrimaryKeyColumn(AutoIncrement = true)]
        public long Id { get; set; }

        [Column("key")]
        [Index(IndexTypes.UniqueNonClustered, Name = "IX_jfbEntry_key")]
        public Guid Key { get; set; }

        // No cascade: the form service deletes a form's entries (with their files) before the form.
        [Column("formId")]
        [ForeignKey(typeof(FormDto), Name = "FK_jfbEntry_jfbForm")]
        [Index(IndexTypes.UniqueNonClustered, Name = "IX_jfbEntry_formId_number", ForColumns = "formId,number")]
        [Index(IndexTypes.UniqueNonClustered, Name = "IX_jfbEntry_formId_submissionId", ForColumns = "formId,submissionId")]
        [Index(IndexTypes.NonClustered, Name = "IX_jfbEntry_formId_submittedUtc", ForColumns = "formId,submittedUtc")]
        [Index(IndexTypes.NonClustered, Name = "IX_jfbEntry_formId_state_submittedUtc", ForColumns = "formId,state,submittedUtc")]
        public int FormId { get; set; }

        // The version the entry was submitted with, which is never pruned while an entry points at it. The index
        // serves that check and the foreign key's own check when a draft version is deleted (ADR-0033).
        [Column("formVersionId")]
        [ForeignKey(typeof(FormVersionDto), Name = "FK_jfbEntry_jfbFormVersion")]
        [Index(IndexTypes.NonClustered, Name = "IX_jfbEntry_formVersionId")]
        public int FormVersionId { get; set; }

        [Column("number")]
        public int Number { get; set; }

        // draft, unverified, submitted, approved, rejected or spam (section 4.4).
        [Column("state")]
        [Length(16)]
        public string State { get; set; } = string.Empty;

        [Column("isTest")]
        public bool IsTest { get; set; }

        // Always set; the server makes one for no-JS, server-key and programmatic entries.
        [Column("submissionId")]
        public Guid SubmissionId { get; set; }

        // The submission's SHA-256 in lowercase hex: with the submission id, it tells a retry from a different
        // submission (ADR-0012).
        [Column("submissionHash")]
        [Length(64)]
        public string SubmissionHash { get; set; } = string.Empty;

        // "" for the invariant culture.
        [Column("culture")]
        [Length(32)]
        public string Culture { get; set; } = string.Empty;

        [Column("contentKey")]
        [NullSetting(NullSetting = NullSettings.Null)]
        public Guid? ContentKey { get; set; }

        // The path only, never the query or the referrer (ADR-0009); null without a page (the API, code).
        [Column("pageUrl")]
        [Length(2048)]
        [NullSetting(NullSetting = NullSettings.Null)]
        public string? PageUrl { get; set; }

        [Column("memberKey")]
        [NullSetting(NullSetting = NullSettings.Null)]
        [Index(IndexTypes.NonClustered, Name = "IX_jfbEntry_memberKey")]
        public Guid? MemberKey { get; set; }

        // Not stored by default; anonymised or full only when the form says so (ADR-0009).
        [Column("ipAddress")]
        [Length(64)]
        [NullSetting(NullSetting = NullSettings.Null)]
        public string? IpAddress { get; set; }

        [Column("spamScore")]
        public int SpamScore { get; set; }

        // "" when nothing looked like spam.
        [Column("spamReasons")]
        [Length(1000)]
        public string SpamReasons { get; set; } = string.Empty;

        // The normalised values of non-sensitive fields, for one-pass search (ADR-0003).
        [Column("searchText")]
        [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
        public string SearchText { get; set; } = string.Empty;

        // JSON: the context values the form lists (ADR-0009).
        [Column("context")]
        [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
        public string Context { get; set; } = "{}";

        [Column("durationSeconds")]
        [NullSetting(NullSetting = NullSettings.Null)]
        public int? DurationSeconds { get; set; }

        [Column("submittedUtc")]
        public DateTime SubmittedUtc { get; set; }

        [Column("updatedUtc")]
        public DateTime UpdatedUtc { get; set; }

        [Column("stateChangedUtc")]
        public DateTime StateChangedUtc { get; set; }

        [Column("stateChangedByUserKey")]
        [NullSetting(NullSetting = NullSettings.Null)]
        public Guid? StateChangedByUserKey { get; set; }

        [Column("firstSeenUtc")]
        [NullSetting(NullSetting = NullSettings.Null)]
        public DateTime? FirstSeenUtc { get; set; }

        [Column("verifiedUtc")]
        [NullSetting(NullSetting = NullSettings.Null)]
        public DateTime? VerifiedUtc { get; set; }

        // SHA-256 of the resume token in lowercase hex; the token itself is never stored (ADR-0012).
        [Column("resumeTokenHash")]
        [Length(64)]
        [NullSetting(NullSetting = NullSettings.Null)]
        public string? ResumeTokenHash { get; set; }

        [Column("resumeExpiresUtc")]
        [NullSetting(NullSetting = NullSettings.Null)]
        public DateTime? ResumeExpiresUtc { get; set; }

        [Column("currentPageKey")]
        [NullSetting(NullSetting = NullSettings.Null)]
        public Guid? CurrentPageKey { get; set; }

        // The challenge that made the entry, unique per form (ADR-0012): 128 random bits as 32 lowercase hex characters,
        // so the database's collation never matters (ADR-0033).
        [Column("challengeId")]
        [Length(64)]
        [NullSetting(NullSetting = NullSettings.Null)]
        public string? ChallengeId { get; set; }

        // A legal hold keeps the entry past its retention (13.1).
        [Column("holdUntilUtc")]
        [NullSetting(NullSetting = NullSettings.Null)]
        public DateTime? HoldUntilUtc { get; set; }

        [Column("holdReason")]
        [Length(500)]
        [NullSetting(NullSetting = NullSettings.Null)]
        public string? HoldReason { get; set; }

        [Column("purgeAfterUtc")]
        [NullSetting(NullSetting = NullSettings.Null)]
        [Index(IndexTypes.NonClustered, Name = "IX_jfbEntry_purgeAfterUtc")]
        public DateTime? PurgeAfterUtc { get; set; }
    }

    // An uploaded file. Files without an entry after 24 hours are orphans and get cleaned up.
    [TableName(FormBuilderTables.EntryFile)]
    [PrimaryKey("id", AutoIncrement = true)]
    [ExplicitColumns]
    internal sealed class EntryFileDto
    {
        [Column("id")]
        [PrimaryKeyColumn(AutoIncrement = true)]
        public int Id { get; set; }

        [Column("key")]
        [Index(IndexTypes.UniqueNonClustered, Name = "IX_jfbEntryFile_key")]
        public Guid Key { get; set; }

        [Column("entryId")]
        [NullSetting(NullSetting = NullSettings.Null)]
        [ForeignKey(typeof(EntryDto), Name = "FK_jfbEntryFile_jfbEntry", OnDelete = Rule.Cascade)]
        [Index(IndexTypes.NonClustered, Name = "IX_jfbEntryFile_entryId_createdUtc", ForColumns = "entryId,createdUtc")]
        public long? EntryId { get; set; }

        // With the entry, the index finds one form's uploads that wait for an entry, for the form's delete (4.8g).
        [Column("formId")]
        [Index(IndexTypes.NonClustered, Name = "IX_jfbEntryFile_formId_entryId", ForColumns = "formId,entryId")]
        public int FormId { get; set; }

        [Column("fieldKey")]
        public Guid FieldKey { get; set; }

        [Column("originalName")]
        [Length(255)]
        public string OriginalName { get; set; } = string.Empty;

        // The store's id: 128 random bits as 32 lowercase hex characters, never derived from the name (ADR-0006).
        [Column("storedName")]
        [Length(64)]
        public string StoredName { get; set; } = string.Empty;

        [Column("contentType")]
        [Length(255)]
        public string ContentType { get; set; } = string.Empty;

        [Column("sizeBytes")]
        public long SizeBytes { get; set; }

        // SHA-256 of the file's bytes in lowercase hex.
        [Column("fileHash")]
        [Length(64)]
        public string FileHash { get; set; } = string.Empty;

        [Column("scanState")]
        [Length(16)]
        public string ScanState { get; set; } = string.Empty;

        // With the entry, the index finds an entry's files and the uploads that wait for one, oldest first.
        [Column("createdUtc")]
        public DateTime CreatedUtc { get; set; }
    }

    // One value of one field of one entry; several for multi-value fields and repeater rows (ADR-0003).
    [TableName(FormBuilderTables.EntryValue)]
    [PrimaryKey("id", AutoIncrement = true)]
    [ExplicitColumns]
    internal sealed class EntryValueDto
    {
        [Column("id")]
        [PrimaryKeyColumn(AutoIncrement = true)]
        public long Id { get; set; }

        [Column("entryId")]
        [ForeignKey(typeof(EntryDto), Name = "FK_jfbEntryValue_jfbEntry", OnDelete = Rule.Cascade)]
        [Index(IndexTypes.NonClustered, Name = "IX_jfbEntryValue_entryId_fieldId", ForColumns = "entryId,fieldId")]
        public long EntryId { get; set; }

        // The field's catalogue row. No cascade: SQL Server refuses a second cascade path, and entries are deleted
        // before their form. The covering indexes serve the entries query builder's shapes (ADR-0003).
        [Column("fieldId")]
        [ForeignKey(typeof(FormFieldDto), Name = "FK_jfbEntryValue_jfbFormField")]
        [Index(IndexTypes.NonClustered, Name = "IX_jfbEntryValue_fieldId_textKey_entryId", ForColumns = "fieldId,textKey,entryId")]
        [Index(IndexTypes.NonClustered, Name = "IX_jfbEntryValue_fieldId_number_entryId", ForColumns = "fieldId,number,entryId")]
        [Index(IndexTypes.NonClustered, Name = "IX_jfbEntryValue_fieldId_date_entryId", ForColumns = "fieldId,date,entryId")]
        public int FieldId { get; set; }

        [Column("rowIndex")]
        public int RowIndex { get; set; }

        [Column("valueIndex")]
        public int ValueIndex { get; set; }

        // The value exactly as submitted, for display and export.
        [Column("text")]
        [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
        [NullSetting(NullSetting = NullSettings.Null)]
        public string? Text { get; set; }

        // Cleaned and folded in C# as rules compare text (ADR-0005), for sort and filter.
        [Column("textKey")]
        [Length(256)]
        [NullSetting(NullSetting = NullSettings.Null)]
        public string? TextKey { get; set; }

        // A double, not a decimal: Umbraco maps decimal to TEXT on SQLite (ADR-0003). Exact for the at most 15
        // significant digits a number field accepts.
        [Column("number")]
        [NullSetting(NullSetting = NullSettings.Null)]
        public double? Number { get; set; }

        // Written only as a UTC DateTime parameter, so SQLite stores one text format that sorts (ADR-0003).
        [Column("date")]
        [NullSetting(NullSetting = NullSettings.Null)]
        public DateTime? Date { get; set; }

        [Column("bool")]
        [NullSetting(NullSetting = NullSettings.Null)]
        public bool? Bool { get; set; }

        // No cascade: the entry's own cascade deletes the value and the file together. The filtered index
        // IX_jfbEntryValue_fileId serves the foreign key's check (FilteredIndexes).
        [Column("fileId")]
        [NullSetting(NullSetting = NullSettings.Null)]
        [ForeignKey(typeof(EntryFileDto), Name = "FK_jfbEntryValue_jfbEntryFile")]
        public int? FileId { get; set; }
    }

    // The exact wording of a consent, stored once; a consent value stores true plus this hash (ADR-0009).
    [TableName(FormBuilderTables.ConsentText)]
    [PrimaryKey("textHash", AutoIncrement = false)]
    [ExplicitColumns]
    internal sealed class ConsentTextDto
    {
        // SHA-256 of the culture and the text, in lowercase hex.
        [Column("textHash")]
        [Length(64)]
        [PrimaryKeyColumn(AutoIncrement = false)]
        public string TextHash { get; set; } = string.Empty;

        [Column("culture")]
        [Length(32)]
        public string Culture { get; set; } = string.Empty;

        [Column("text")]
        [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
        public string Text { get; set; } = string.Empty;

        [Column("firstSeenUtc")]
        public DateTime FirstSeenUtc { get; set; }
    }

    // The entry's timeline. Edits record old and new values only for non-sensitive fields.
    [TableName(FormBuilderTables.EntryEvent)]
    [PrimaryKey("id", AutoIncrement = true)]
    [ExplicitColumns]
    internal sealed class EntryEventDto
    {
        [Column("id")]
        [PrimaryKeyColumn(AutoIncrement = true)]
        public long Id { get; set; }

        [Column("entryId")]
        [ForeignKey(typeof(EntryDto), Name = "FK_jfbEntryEvent_jfbEntry", OnDelete = Rule.Cascade)]
        [Index(IndexTypes.NonClustered, Name = "IX_jfbEntryEvent_entryId_occurredUtc", ForColumns = "entryId,occurredUtc")]
        public long EntryId { get; set; }

        [Column("occurredUtc")]
        public DateTime OccurredUtc { get; set; }

        [Column("kind")]
        [Length(64)]
        public string Kind { get; set; } = string.Empty;

        [Column("userKey")]
        [NullSetting(NullSetting = NullSettings.Null)]
        public Guid? UserKey { get; set; }

        // Ties the events of one state change by a user, a bulk one too, so Undo finds them (ADR-0023).
        [Column("changeKey")]
        [NullSetting(NullSetting = NullSettings.Null)]
        [Index(IndexTypes.NonClustered, Name = "IX_jfbEntryEvent_changeKey")]
        public Guid? ChangeKey { get; set; }

        [Column("details")]
        [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
        public string Details { get; set; } = "{}";
    }

    // Internal notes on an entry, never sent anywhere.
    [TableName(FormBuilderTables.EntryNote)]
    [PrimaryKey("id", AutoIncrement = true)]
    [ExplicitColumns]
    internal sealed class EntryNoteDto
    {
        [Column("id")]
        [PrimaryKeyColumn(AutoIncrement = true)]
        public int Id { get; set; }

        [Column("entryId")]
        [ForeignKey(typeof(EntryDto), Name = "FK_jfbEntryNote_jfbEntry", OnDelete = Rule.Cascade)]
        [Index(IndexTypes.NonClustered, Name = "IX_jfbEntryNote_entryId")]
        public long EntryId { get; set; }

        [Column("userKey")]
        public Guid UserKey { get; set; }

        [Column("body")]
        [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
        public string Body { get; set; } = string.Empty;

        [Column("createdUtc")]
        public DateTime CreatedUtc { get; set; }

        [Column("updatedUtc")]
        public DateTime UpdatedUtc { get; set; }
    }
}
