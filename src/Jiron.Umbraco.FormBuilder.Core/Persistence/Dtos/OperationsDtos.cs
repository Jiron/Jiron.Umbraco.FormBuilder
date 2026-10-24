using NPoco;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Dtos;

// The repositories' rows of the audit log and the daily statistics (ADR-0041): the columns of schema v1, without the
// schema annotations, which belong to the frozen snapshots of the migration steps. ProductDtoTests checks that each names
// exactly the columns of its table with the same types.

/// <summary>A row of <c>jfbAuditEntry</c>.</summary>
[TableName(FormBuilderTables.AuditEntry)]
[PrimaryKey("id", AutoIncrement = true)]
[ExplicitColumns]
internal sealed class AuditEntryDto
{
    [Column("id")]
    public long Id { get; set; }

    [Column("occurredUtc")]
    public DateTime OccurredUtc { get; set; }

    [Column("userKey")]
    public Guid? UserKey { get; set; }

    [Column("action")]
    public string Action { get; set; } = string.Empty;

    [Column("entityType")]
    public string EntityType { get; set; } = string.Empty;

    [Column("entityKey")]
    public Guid? EntityKey { get; set; }

    [Column("details")]
    public string Details { get; set; } = "{}";
}

/// <summary>A row of <c>jfbDailyStat</c>.</summary>
[TableName(FormBuilderTables.DailyStat)]
[PrimaryKey(["day", "formId", "metric", "dimension"], AutoIncrement = false)]
[ExplicitColumns]
internal sealed class DailyStatDto
{
    [Column("day")]
    public DateTime Day { get; set; }

    [Column("formId")]
    public int FormId { get; set; }

    [Column("metric")]
    public string Metric { get; set; } = string.Empty;

    [Column("dimension")]
    public string Dimension { get; set; } = string.Empty;

    [Column("value")]
    public long Value { get; set; }
}
