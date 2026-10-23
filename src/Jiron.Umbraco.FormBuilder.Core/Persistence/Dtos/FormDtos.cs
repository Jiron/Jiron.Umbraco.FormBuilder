using NPoco;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Dtos;

// The repositories' rows of the key-value, folder and form tables (ADR-0033): the columns of schema v1, without the
// schema annotations, which belong to the frozen snapshots of the migration steps. ProductDtoTests checks that each
// names exactly the columns of its table with the same types.

/// <summary>A row of <c>jfbKeyValue</c>.</summary>
[TableName(FormBuilderTables.KeyValue)]
[PrimaryKey("key", AutoIncrement = false)]
[ExplicitColumns]
internal sealed class KeyValueDto
{
    [Column("key")]
    public string Key { get; set; } = string.Empty;

    [Column("value")]
    public string? Value { get; set; }

    [Column("updatedUtc")]
    public DateTime UpdatedUtc { get; set; }
}

/// <summary>A row of <c>jfbFolder</c>.</summary>
[TableName(FormBuilderTables.Folder)]
[PrimaryKey("id", AutoIncrement = true)]
[ExplicitColumns]
internal sealed class FolderDto
{
    [Column("id")]
    public int Id { get; set; }

    [Column("key")]
    public Guid Key { get; set; }

    [Column("parentId")]
    public int? ParentId { get; set; }

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("sortOrder")]
    public int SortOrder { get; set; }

    [Column("isRestricted")]
    public bool IsRestricted { get; set; }

    [Column("trashedUtc")]
    public DateTime? TrashedUtc { get; set; }

    [Column("createdUtc")]
    public DateTime CreatedUtc { get; set; }

    [Column("updatedUtc")]
    public DateTime UpdatedUtc { get; set; }

    [Column("createdByUserKey")]
    public Guid? CreatedByUserKey { get; set; }

    [Column("updatedByUserKey")]
    public Guid? UpdatedByUserKey { get; set; }
}

/// <summary>A row of <c>jfbForm</c>.</summary>
[TableName(FormBuilderTables.Form)]
[PrimaryKey("id", AutoIncrement = true)]
[ExplicitColumns]
internal sealed class FormDto
{
    [Column("id")]
    public int Id { get; set; }

    [Column("key")]
    public Guid Key { get; set; }

    [Column("folderId")]
    public int? FolderId { get; set; }

    [Column("alias")]
    public string Alias { get; set; } = string.Empty;

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("draftVersionId")]
    public int? DraftVersionId { get; set; }

    [Column("publishedVersionId")]
    public int? PublishedVersionId { get; set; }

    [Column("publicRevision")]
    public int PublicRevision { get; set; }

    [Column("concurrencyVersion")]
    public int ConcurrencyVersion { get; set; }

    [Column("lastEntryNumber")]
    public int LastEntryNumber { get; set; }

    [Column("themeAlias")]
    public string? ThemeAlias { get; set; }

    [Column("isRestricted")]
    public bool IsRestricted { get; set; }

    [Column("trashedUtc")]
    public DateTime? TrashedUtc { get; set; }

    [Column("createdUtc")]
    public DateTime CreatedUtc { get; set; }

    [Column("updatedUtc")]
    public DateTime UpdatedUtc { get; set; }

    [Column("createdByUserKey")]
    public Guid? CreatedByUserKey { get; set; }

    [Column("updatedByUserKey")]
    public Guid? UpdatedByUserKey { get; set; }
}

/// <summary>A row of <c>jfbFormVersion</c>.</summary>
[TableName(FormBuilderTables.FormVersion)]
[PrimaryKey("id", AutoIncrement = true)]
[ExplicitColumns]
internal sealed class FormVersionDto
{
    [Column("id")]
    public int Id { get; set; }

    [Column("formId")]
    public int FormId { get; set; }

    [Column("number")]
    public int Number { get; set; }

    [Column("kind")]
    public string Kind { get; set; } = string.Empty;

    [Column("schemaVersion")]
    public int SchemaVersion { get; set; }

    [Column("definition")]
    public string Definition { get; set; } = string.Empty;

    [Column("definitionHash")]
    public string DefinitionHash { get; set; } = string.Empty;

    [Column("publicHash")]
    public string PublicHash { get; set; } = string.Empty;

    [Column("publicRevision")]
    public int? PublicRevision { get; set; }

    [Column("comment")]
    public string? Comment { get; set; }

    [Column("isPinned")]
    public bool IsPinned { get; set; }

    [Column("createdUtc")]
    public DateTime CreatedUtc { get; set; }

    [Column("createdByUserKey")]
    public Guid? CreatedByUserKey { get; set; }

    [Column("publishedUtc")]
    public DateTime? PublishedUtc { get; set; }

    [Column("publishedByUserKey")]
    public Guid? PublishedByUserKey { get; set; }
}

/// <summary>A row of <c>jfbFormField</c>, the field catalogue.</summary>
[TableName(FormBuilderTables.FormField)]
[PrimaryKey("id", AutoIncrement = true)]
[ExplicitColumns]
internal sealed class FormFieldDto
{
    [Column("id")]
    public int Id { get; set; }

    [Column("formId")]
    public int FormId { get; set; }

    [Column("fieldKey")]
    public Guid FieldKey { get; set; }

    [Column("alias")]
    public string Alias { get; set; } = string.Empty;

    [Column("label")]
    public string Label { get; set; } = string.Empty;

    [Column("type")]
    public string Type { get; set; } = string.Empty;

    [Column("storageKind")]
    public string StorageKind { get; set; } = string.Empty;

    [Column("isSensitive")]
    public bool IsSensitive { get; set; }

    [Column("sortOrder")]
    public int SortOrder { get; set; }

    [Column("firstVersionNumber")]
    public int? FirstVersionNumber { get; set; }

    [Column("lastVersionNumber")]
    public int? LastVersionNumber { get; set; }

    [Column("parentFieldKey")]
    public Guid? ParentFieldKey { get; set; }
}

/// <summary>A row of <c>jfbSetting</c>.</summary>
[TableName(FormBuilderTables.Setting)]
[PrimaryKey("id", AutoIncrement = true)]
[ExplicitColumns]
internal sealed class SettingDto
{
    [Column("id")]
    public int Id { get; set; }

    [Column("scope")]
    public string Scope { get; set; } = string.Empty;

    [Column("section")]
    public string Section { get; set; } = string.Empty;

    [Column("schemaVersion")]
    public int SchemaVersion { get; set; }

    [Column("version")]
    public int Version { get; set; }

    [Column("json")]
    public string Json { get; set; } = "{}";

    [Column("updatedUtc")]
    public DateTime UpdatedUtc { get; set; }

    [Column("updatedByUserKey")]
    public Guid? UpdatedByUserKey { get; set; }
}
