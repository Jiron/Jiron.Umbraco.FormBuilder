using NPoco;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Dtos;

// The repositories' rows of the outbox tables (ADR-0007, ADR-0040): the columns of schema v1, without the schema
// annotations, which belong to the frozen snapshots of the migration steps. ProductDtoTests checks that each names
// exactly the columns of its table with the same types.

/// <summary>A row of <c>jfbActionRun</c>.</summary>
[TableName(FormBuilderTables.ActionRun)]
[PrimaryKey("id", AutoIncrement = true)]
[ExplicitColumns]
internal sealed class ActionRunDto
{
    [Column("id")]
    public long Id { get; set; }

    [Column("entryId")]
    public long EntryId { get; set; }

    [Column("formVersionId")]
    public int FormVersionId { get; set; }

    [Column("actionKey")]
    public Guid ActionKey { get; set; }

    [Column("actionType")]
    public string ActionType { get; set; } = string.Empty;

    [Column("triggerKind")]
    public string TriggerKind { get; set; } = string.Empty;

    [Column("seq")]
    public int Seq { get; set; }

    [Column("stopOnFailure")]
    public bool StopOnFailure { get; set; }

    [Column("state")]
    public string State { get; set; } = string.Empty;

    [Column("attempts")]
    public int Attempts { get; set; }

    [Column("nextAttemptUtc")]
    public DateTime? NextAttemptUtc { get; set; }

    [Column("changeKey")]
    public Guid? ChangeKey { get; set; }

    [Column("lockedUntilUtc")]
    public DateTime? LockedUntilUtc { get; set; }

    [Column("lockedBy")]
    public string? LockedBy { get; set; }

    [Column("lastErrorCode")]
    public string? LastErrorCode { get; set; }

    [Column("lastErrorMessage")]
    public string? LastErrorMessage { get; set; }

    [Column("createdUtc")]
    public DateTime CreatedUtc { get; set; }

    [Column("startedUtc")]
    public DateTime? StartedUtc { get; set; }

    [Column("finishedUtc")]
    public DateTime? FinishedUtc { get; set; }
}

/// <summary>A row of <c>jfbEmail</c>.</summary>
[TableName(FormBuilderTables.Email)]
[PrimaryKey("id", AutoIncrement = true)]
[ExplicitColumns]
internal sealed class EmailDto
{
    [Column("id")]
    public long Id { get; set; }

    [Column("kind")]
    public string Kind { get; set; } = string.Empty;

    [Column("entryId")]
    public long? EntryId { get; set; }

    [Column("actionRunId")]
    public long? ActionRunId { get; set; }

    [Column("toAddresses")]
    public string ToAddresses { get; set; } = string.Empty;

    [Column("ccAddresses")]
    public string? CcAddresses { get; set; }

    [Column("bccAddresses")]
    public string? BccAddresses { get; set; }

    [Column("replyTo")]
    public string? ReplyTo { get; set; }

    [Column("fromAddress")]
    public string FromAddress { get; set; } = string.Empty;

    [Column("subject")]
    public string Subject { get; set; } = string.Empty;

    [Column("htmlBody")]
    public string? HtmlBody { get; set; }

    [Column("textBody")]
    public string TextBody { get; set; } = string.Empty;

    [Column("attachments")]
    public string Attachments { get; set; } = "[]";

    [Column("culture")]
    public string Culture { get; set; } = string.Empty;

    [Column("recipientKey")]
    public string? RecipientKey { get; set; }

    [Column("state")]
    public string State { get; set; } = string.Empty;

    [Column("attempts")]
    public int Attempts { get; set; }

    [Column("nextAttemptUtc")]
    public DateTime? NextAttemptUtc { get; set; }

    [Column("lockedUntilUtc")]
    public DateTime? LockedUntilUtc { get; set; }

    [Column("lockedBy")]
    public string? LockedBy { get; set; }

    [Column("lastError")]
    public string? LastError { get; set; }

    [Column("createdUtc")]
    public DateTime CreatedUtc { get; set; }

    [Column("sentUtc")]
    public DateTime? SentUtc { get; set; }

    [Column("purgeAfterUtc")]
    public DateTime? PurgeAfterUtc { get; set; }
}
