using System.Data;
using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;

// Frozen snapshots of the action, email and option-place tables as step 1 creates them (ADR-0007). Never edit.
internal sealed partial class CreateSchemaV1
{
    // One run of one action for one entry: the outbox (D6). Every state change names the run, its token and its state.
    [TableName(FormBuilderTables.ActionRun)]
    [PrimaryKey("id", AutoIncrement = true)]
    [ExplicitColumns]
    internal sealed class ActionRunDto
    {
        [Column("id")]
        [PrimaryKeyColumn(AutoIncrement = true)]
        public long Id { get; set; }

        [Column("entryId")]
        [ForeignKey(typeof(EntryDto), Name = "FK_jfbActionRun_jfbEntry", OnDelete = Rule.Cascade)]
        [Index(IndexTypes.NonClustered, Name = "IX_jfbActionRun_entryId_sortOrder", ForColumns = "entryId,sortOrder")]
        public long EntryId { get; set; }

        // The version whose action this runs; no foreign key, the entry already keeps its version.
        [Column("formVersionId")]
        public int FormVersionId { get; set; }

        [Column("actionKey")]
        public Guid ActionKey { get; set; }

        [Column("actionType")]
        [Length(128)]
        public string ActionType { get; set; } = string.Empty;

        // submitted, approved, rejected or verified. Not "trigger", a reserved word in both databases (ADR-0033).
        [Column("triggerKind")]
        [Length(16)]
        public string TriggerKind { get; set; } = string.Empty;

        // The action's position, for the order of section 4.4.
        [Column("sortOrder")]
        public int SortOrder { get; set; }

        [Column("stopOnFailure")]
        public bool StopOnFailure { get; set; }

        // pending, running, succeeded, failed, skipped or uncertain.
        [Column("state")]
        [Length(16)]
        [Index(IndexTypes.NonClustered, Name = "IX_jfbActionRun_state_nextAttemptUtc", ForColumns = "state,nextAttemptUtc")]
        public string State { get; set; } = string.Empty;

        [Column("attempts")]
        public int Attempts { get; set; }

        // When the run may be claimed; null once it is final. A user's state change sets the end of its undo window.
        [Column("nextAttemptUtc")]
        [NullSetting(NullSetting = NullSettings.Null)]
        public DateTime? NextAttemptUtc { get; set; }

        [Column("changeKey")]
        [NullSetting(NullSetting = NullSettings.Null)]
        [Index(IndexTypes.NonClustered, Name = "IX_jfbActionRun_changeKey")]
        public Guid? ChangeKey { get; set; }

        [Column("lockedUntilUtc")]
        [NullSetting(NullSetting = NullSettings.Null)]
        public DateTime? LockedUntilUtc { get; set; }

        // The claim's token: the server's name and 128 random bits.
        [Column("lockedBy")]
        [Length(255)]
        [NullSetting(NullSetting = NullSettings.Null)]
        public string? LockedBy { get; set; }

        [Column("lastErrorCode")]
        [Length(64)]
        [NullSetting(NullSetting = NullSettings.Null)]
        public string? LastErrorCode { get; set; }

        // Sanitised, at most 1,000 characters.
        [Column("lastErrorMessage")]
        [Length(1000)]
        [NullSetting(NullSetting = NullSettings.Null)]
        public string? LastErrorMessage { get; set; }

        [Column("createdUtc")]
        public DateTime CreatedUtc { get; set; }

        [Column("startedUtc")]
        [NullSetting(NullSetting = NullSettings.Null)]
        public DateTime? StartedUtc { get; set; }

        [Column("finishedUtc")]
        [NullSetting(NullSetting = NullSettings.Null)]
        public DateTime? FinishedUtc { get; set; }
    }

    // One outgoing mail: the email outbox (ADR-0007). Its address columns avoid the reserved words FROM and TO.
    [TableName(FormBuilderTables.Email)]
    [PrimaryKey("id", AutoIncrement = true)]
    [ExplicitColumns]
    internal sealed class EmailDto
    {
        [Column("id")]
        [PrimaryKeyColumn(AutoIncrement = true)]
        public long Id { get; set; }

        // action, resume, verification, test, report, digest or forward.
        [Column("kind")]
        [Length(16)]
        public string Kind { get; set; } = string.Empty;

        // Set to null when the entry is deleted; the index serves that (ADR-0033).
        [Column("entryId")]
        [NullSetting(NullSetting = NullSettings.Null)]
        [ForeignKey(typeof(EntryDto), Name = "FK_jfbEmail_jfbEntry", OnDelete = Rule.SetNull)]
        [Index(IndexTypes.NonClustered, Name = "IX_jfbEmail_entryId")]
        public long? EntryId { get; set; }

        // No foreign key: a second path from jfbEntry, which SQL Server refuses.
        [Column("actionRunId")]
        [NullSetting(NullSetting = NullSettings.Null)]
        public long? ActionRunId { get; set; }

        // Comma-separated addresses.
        [Column("toAddresses")]
        [Length(4000)]
        public string ToAddresses { get; set; } = string.Empty;

        [Column("ccAddresses")]
        [Length(4000)]
        [NullSetting(NullSetting = NullSettings.Null)]
        public string? CcAddresses { get; set; }

        [Column("bccAddresses")]
        [Length(4000)]
        [NullSetting(NullSetting = NullSettings.Null)]
        public string? BccAddresses { get; set; }

        [Column("replyToAddress")]
        [Length(500)]
        [NullSetting(NullSetting = NullSettings.Null)]
        public string? ReplyToAddress { get; set; }

        // The sender's name and address; never taken from a visitor (section 4.10).
        [Column("fromAddress")]
        [Length(500)]
        public string FromAddress { get; set; } = string.Empty;

        [Column("subject")]
        [Length(1000)]
        public string Subject { get; set; } = string.Empty;

        // One body per mail (ADR-0007): null for a plain-text mail; the text version is always there.
        [Column("htmlBody")]
        [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
        [NullSetting(NullSetting = NullSettings.Null)]
        public string? HtmlBody { get; set; }

        [Column("textBody")]
        [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
        public string TextBody { get; set; } = string.Empty;

        // JSON: the keys of the attached files.
        [Column("attachments")]
        [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
        public string Attachments { get; set; } = "[]";

        [Column("culture")]
        [Length(32)]
        public string Culture { get; set; } = string.Empty;

        // The visitor-typed address, trimmed, case-folded in C# and without a +tag, for the per-address limits.
        [Column("recipientKey")]
        [Length(320)]
        [NullSetting(NullSetting = NullSettings.Null)]
        [Index(IndexTypes.NonClustered, Name = "IX_jfbEmail_recipientKey_createdUtc", ForColumns = "recipientKey,createdUtc")]
        public string? RecipientKey { get; set; }

        // queued, sending, sent, failed, uncertain or limited (final, never claimed).
        [Column("state")]
        [Length(16)]
        [Index(IndexTypes.NonClustered, Name = "IX_jfbEmail_state_nextAttemptUtc", ForColumns = "state,nextAttemptUtc")]
        public string State { get; set; } = string.Empty;

        [Column("attempts")]
        public int Attempts { get; set; }

        [Column("nextAttemptUtc")]
        [NullSetting(NullSetting = NullSettings.Null)]
        public DateTime? NextAttemptUtc { get; set; }

        [Column("lockedUntilUtc")]
        [NullSetting(NullSetting = NullSettings.Null)]
        public DateTime? LockedUntilUtc { get; set; }

        [Column("lockedBy")]
        [Length(255)]
        [NullSetting(NullSetting = NullSettings.Null)]
        public string? LockedBy { get; set; }

        [Column("lastErrorCode")]
        [Length(64)]
        [NullSetting(NullSetting = NullSettings.Null)]
        public string? LastErrorCode { get; set; }

        // Sanitised, at most 1,000 characters.
        [Column("lastErrorMessage")]
        [Length(1000)]
        [NullSetting(NullSetting = NullSettings.Null)]
        public string? LastErrorMessage { get; set; }

        [Column("createdUtc")]
        public DateTime CreatedUtc { get; set; }

        [Column("sentUtc")]
        [NullSetting(NullSetting = NullSettings.Null)]
        public DateTime? SentUtc { get; set; }

        // Set when the mail reaches a final state.
        [Column("purgeAfterUtc")]
        [NullSetting(NullSetting = NullSettings.Null)]
        [Index(IndexTypes.NonClustered, Name = "IX_jfbEmail_purgeAfterUtc")]
        public DateTime? PurgeAfterUtc { get; set; }
    }

    // The places taken on options with a limit; the limit itself lives in the definition (section 4.4).
    [TableName(FormBuilderTables.OptionCapacity)]
    [PrimaryKey(["formId", "fieldKey", "valueHash"], AutoIncrement = false)]
    [ExplicitColumns]
    internal sealed class OptionCapacityDto
    {
        [Column("formId")]
        [PrimaryKeyColumn(AutoIncrement = false, Name = "PK_jfbOptionCapacity", OnColumns = "formId, fieldKey, valueHash")]
        [ForeignKey(typeof(FormDto), Name = "FK_jfbOptionCapacity_jfbForm", OnDelete = Rule.Cascade)]
        public int FormId { get; set; }

        [Column("fieldKey")]
        public Guid FieldKey { get; set; }

        // SHA-256 of the option value's UTF-8 bytes in lowercase hex. The key compares exactly on both databases, which
        // the value itself would not: SQL Server's collation folds more than case (ADR-0033).
        [Column("valueHash")]
        [Length(64)]
        public string ValueHash { get; set; } = string.Empty;

        // The value as the definition has it (1 to 200 characters), for reading the table.
        [Column("optionValue")]
        [Length(200)]
        public string OptionValue { get; set; } = string.Empty;

        [Column("taken")]
        public int Taken { get; set; }

        [Column("updatedUtc")]
        public DateTime UpdatedUtc { get; set; }
    }
}
