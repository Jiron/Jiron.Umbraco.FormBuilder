using NPoco;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Dtos;

// The repositories' rows of the entry tables (ADR-0003, ADR-0033, ADR-0038): the columns of schema v1, without the
// schema annotations, which belong to the frozen snapshots of the migration steps. ProductDtoTests checks that each
// names exactly the columns of its table with the same types.

/// <summary>A row of <c>jfbEntry</c>.</summary>
[TableName(FormBuilderTables.Entry)]
[PrimaryKey("id", AutoIncrement = true)]
[ExplicitColumns]
internal sealed class EntryDto
{
    [Column("id")]
    public long Id { get; set; }

    [Column("key")]
    public Guid Key { get; set; }

    [Column("formId")]
    public int FormId { get; set; }

    [Column("formVersionId")]
    public int FormVersionId { get; set; }

    [Column("number")]
    public int Number { get; set; }

    [Column("state")]
    public string State { get; set; } = string.Empty;

    [Column("isTest")]
    public bool IsTest { get; set; }

    [Column("submissionId")]
    public Guid SubmissionId { get; set; }

    [Column("digest")]
    public string Digest { get; set; } = string.Empty;

    [Column("culture")]
    public string Culture { get; set; } = string.Empty;

    [Column("contentKey")]
    public Guid? ContentKey { get; set; }

    [Column("pageUrl")]
    public string? PageUrl { get; set; }

    [Column("memberKey")]
    public Guid? MemberKey { get; set; }

    [Column("ipAddress")]
    public string? IpAddress { get; set; }

    [Column("spamScore")]
    public int SpamScore { get; set; }

    [Column("spamReasons")]
    public string SpamReasons { get; set; } = string.Empty;

    [Column("searchText")]
    public string SearchText { get; set; } = string.Empty;

    [Column("context")]
    public string Context { get; set; } = "{}";

    [Column("durationSeconds")]
    public int? DurationSeconds { get; set; }

    [Column("submittedUtc")]
    public DateTime SubmittedUtc { get; set; }

    [Column("updatedUtc")]
    public DateTime UpdatedUtc { get; set; }

    [Column("stateChangedUtc")]
    public DateTime StateChangedUtc { get; set; }

    [Column("stateChangedByUserKey")]
    public Guid? StateChangedByUserKey { get; set; }

    [Column("firstSeenUtc")]
    public DateTime? FirstSeenUtc { get; set; }

    [Column("verifiedUtc")]
    public DateTime? VerifiedUtc { get; set; }

    [Column("resumeTokenHash")]
    public string? ResumeTokenHash { get; set; }

    [Column("resumeExpiresUtc")]
    public DateTime? ResumeExpiresUtc { get; set; }

    [Column("currentPageKey")]
    public Guid? CurrentPageKey { get; set; }

    [Column("challengeId")]
    public string? ChallengeId { get; set; }

    [Column("holdUntilUtc")]
    public DateTime? HoldUntilUtc { get; set; }

    [Column("holdReason")]
    public string? HoldReason { get; set; }

    [Column("purgeAfterUtc")]
    public DateTime? PurgeAfterUtc { get; set; }
}

/// <summary>A row of <c>jfbEntryFile</c>.</summary>
[TableName(FormBuilderTables.EntryFile)]
[PrimaryKey("id", AutoIncrement = true)]
[ExplicitColumns]
internal sealed class EntryFileDto
{
    [Column("id")]
    public int Id { get; set; }

    [Column("key")]
    public Guid Key { get; set; }

    [Column("entryId")]
    public long? EntryId { get; set; }

    [Column("formId")]
    public int FormId { get; set; }

    [Column("fieldKey")]
    public Guid FieldKey { get; set; }

    [Column("originalName")]
    public string OriginalName { get; set; } = string.Empty;

    [Column("storedName")]
    public string StoredName { get; set; } = string.Empty;

    [Column("contentType")]
    public string ContentType { get; set; } = string.Empty;

    [Column("sizeBytes")]
    public long SizeBytes { get; set; }

    [Column("sha256")]
    public string Sha256 { get; set; } = string.Empty;

    [Column("scanState")]
    public string ScanState { get; set; } = string.Empty;

    [Column("createdUtc")]
    public DateTime CreatedUtc { get; set; }
}

/// <summary>A row of <c>jfbEntryValue</c>.</summary>
[TableName(FormBuilderTables.EntryValue)]
[PrimaryKey("id", AutoIncrement = true)]
[ExplicitColumns]
internal sealed class EntryValueDto
{
    [Column("id")]
    public long Id { get; set; }

    [Column("entryId")]
    public long EntryId { get; set; }

    [Column("fieldId")]
    public int FieldId { get; set; }

    [Column("rowIndex")]
    public int RowIndex { get; set; }

    [Column("valueIndex")]
    public int ValueIndex { get; set; }

    [Column("text")]
    public string? Text { get; set; }

    [Column("textKey")]
    public string? TextKey { get; set; }

    [Column("number")]
    public double? Number { get; set; }

    [Column("date")]
    public DateTime? Date { get; set; }

    [Column("bool")]
    public bool? Bool { get; set; }

    [Column("fileId")]
    public int? FileId { get; set; }
}

/// <summary>A row of <c>jfbConsentText</c>.</summary>
[TableName(FormBuilderTables.ConsentText)]
[PrimaryKey("hash", AutoIncrement = false)]
[ExplicitColumns]
internal sealed class ConsentTextDto
{
    [Column("hash")]
    public string Hash { get; set; } = string.Empty;

    [Column("culture")]
    public string Culture { get; set; } = string.Empty;

    [Column("text")]
    public string Text { get; set; } = string.Empty;

    [Column("firstSeenUtc")]
    public DateTime FirstSeenUtc { get; set; }
}

/// <summary>A row of <c>jfbEntryEvent</c>.</summary>
[TableName(FormBuilderTables.EntryEvent)]
[PrimaryKey("id", AutoIncrement = true)]
[ExplicitColumns]
internal sealed class EntryEventDto
{
    [Column("id")]
    public long Id { get; set; }

    [Column("entryId")]
    public long EntryId { get; set; }

    [Column("occurredUtc")]
    public DateTime OccurredUtc { get; set; }

    [Column("kind")]
    public string Kind { get; set; } = string.Empty;

    [Column("userKey")]
    public Guid? UserKey { get; set; }

    [Column("changeKey")]
    public Guid? ChangeKey { get; set; }

    [Column("details")]
    public string Details { get; set; } = "{}";
}

/// <summary>A row of <c>jfbEntryNote</c>.</summary>
[TableName(FormBuilderTables.EntryNote)]
[PrimaryKey("id", AutoIncrement = true)]
[ExplicitColumns]
internal sealed class EntryNoteDto
{
    [Column("id")]
    public int Id { get; set; }

    [Column("entryId")]
    public long EntryId { get; set; }

    [Column("userKey")]
    public Guid UserKey { get; set; }

    [Column("body")]
    public string Body { get; set; } = string.Empty;

    [Column("createdUtc")]
    public DateTime CreatedUtc { get; set; }

    [Column("updatedUtc")]
    public DateTime UpdatedUtc { get; set; }
}
