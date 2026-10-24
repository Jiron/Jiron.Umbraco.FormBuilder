using System.Data;
using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;

// Frozen snapshots of the key-value table and the form tables as step 1 creates them. Never edit.
internal sealed partial class CreateSchemaV1
{
    // One-time flags: the section grant, the setup checklist, the file mirror's hashes (section 4.5).
    [TableName(FormBuilderTables.KeyValue)]
    [PrimaryKey("key", AutoIncrement = false)]
    [ExplicitColumns]
    internal sealed class KeyValueDto
    {
        [Column("key")]
        [Length(255)]
        [PrimaryKeyColumn(AutoIncrement = false)]
        public string Key { get; set; } = string.Empty;

        [Column("value")]
        [NullSetting(NullSetting = NullSettings.Null)]
        [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
        public string? Value { get; set; }

        [Column("updatedUtc")]
        public DateTime UpdatedUtc { get; set; }
    }

    // Folders of the form library. Trash keeps the tree; restore returns to the parent or the root.
    [TableName(FormBuilderTables.Folder)]
    [PrimaryKey("id", AutoIncrement = true)]
    [ExplicitColumns]
    internal sealed class FolderDto
    {
        [Column("id")]
        [PrimaryKeyColumn(AutoIncrement = true)]
        public int Id { get; set; }

        [Column("key")]
        [Index(IndexTypes.UniqueNonClustered, Name = "IX_jfbFolder_key")]
        public Guid Key { get; set; }

        // No cascade: SQL Server refuses one on a table that refers to itself, and the form service moves or deletes
        // the contents first.
        [Column("parentId")]
        [NullSetting(NullSetting = NullSettings.Null)]
        [ForeignKey(typeof(FolderDto), Name = "FK_jfbFolder_jfbFolder")]
        [Index(IndexTypes.NonClustered, Name = "IX_jfbFolder_parentId")]
        public int? ParentId { get; set; }

        [Column("name")]
        [Length(255)]
        public string Name { get; set; } = string.Empty;

        [Column("sortOrder")]
        public int SortOrder { get; set; }

        // Below a restricted folder only the grants on it and below count (ADR-0013).
        [Column("isRestricted")]
        public bool IsRestricted { get; set; }

        [Column("trashedUtc")]
        [NullSetting(NullSetting = NullSettings.Null)]
        public DateTime? TrashedUtc { get; set; }

        // The trash operation that put the folder in the trash: the same for everything that went with it (ADR-0047).
        [Column("trashKey")]
        [NullSetting(NullSetting = NullSettings.Null)]
        public Guid? TrashKey { get; set; }

        [Column("createdUtc")]
        public DateTime CreatedUtc { get; set; }

        [Column("updatedUtc")]
        public DateTime UpdatedUtc { get; set; }

        // Null when Jiron Form Builder made the change itself (an import at start-up, a job).
        [Column("createdByUserKey")]
        [NullSetting(NullSetting = NullSettings.Null)]
        public Guid? CreatedByUserKey { get; set; }

        [Column("updatedByUserKey")]
        [NullSetting(NullSetting = NullSettings.Null)]
        public Guid? UpdatedByUserKey { get; set; }
    }

    // A form: its name, alias and folder, and pointers to its draft and published versions (ADR-0002).
    [TableName(FormBuilderTables.Form)]
    [PrimaryKey("id", AutoIncrement = true)]
    [ExplicitColumns]
    internal sealed class FormDto
    {
        [Column("id")]
        [PrimaryKeyColumn(AutoIncrement = true)]
        public int Id { get; set; }

        [Column("key")]
        [Index(IndexTypes.UniqueNonClustered, Name = "IX_jfbForm_key")]
        public Guid Key { get; set; }

        [Column("folderId")]
        [NullSetting(NullSetting = NullSettings.Null)]
        [ForeignKey(typeof(FolderDto), Name = "FK_jfbForm_jfbFolder")]
        [Index(IndexTypes.NonClustered, Name = "IX_jfbForm_folderId")]
        public int? FolderId { get; set; }

        // Unique among all forms, trashed ones included, and without regard to case on both databases: Umbraco makes
        // strings TEXT COLLATE NOCASE on SQLite and requires a case-insensitive collation on SQL Server (ADR-0033).
        [Column("alias")]
        [Length(64)]
        [Index(IndexTypes.UniqueNonClustered, Name = "IX_jfbForm_alias")]
        public string Alias { get; set; } = string.Empty;

        [Column("name")]
        [Length(255)]
        public string Name { get; set; } = string.Empty;

        // Both refer to jfbFormVersion, which is created after this table and refers back to it.
        [Column("draftVersionId")]
        [NullSetting(NullSetting = NullSettings.Null)]
        [ForeignKey(typeof(FormVersionDto), Name = "FK_jfbForm_jfbFormVersion_draftVersionId")]
        public int? DraftVersionId { get; set; }

        [Column("publishedVersionId")]
        [NullSetting(NullSetting = NullSettings.Null)]
        [ForeignKey(typeof(FormVersionDto), Name = "FK_jfbForm_jfbFormVersion_publishedVersionId")]
        public int? PublishedVersionId { get; set; }

        // 0 until the first publish, then up by one whenever the public projection changes (ADR-0002).
        [Column("publicRevision")]
        public int PublicRevision { get; set; }

        // Raised by every change; a stale save gets 409 concurrency_conflict.
        [Column("concurrencyVersion")]
        public int ConcurrencyVersion { get; set; }

        // Entry numbers: UPDATE ... SET lastEntryNumber = lastEntryNumber + 1, read back in the same transaction.
        [Column("lastEntryNumber")]
        public int LastEntryNumber { get; set; }

        // The published version's appearance.theme, for the library's "used by" list (ADR-0015).
        [Column("themeAlias")]
        [Length(64)]
        [NullSetting(NullSetting = NullSettings.Null)]
        public string? ThemeAlias { get; set; }

        [Column("isRestricted")]
        public bool IsRestricted { get; set; }

        [Column("trashedUtc")]
        [NullSetting(NullSetting = NullSettings.Null)]
        [Index(IndexTypes.NonClustered, Name = "IX_jfbForm_trashedUtc")]
        public DateTime? TrashedUtc { get; set; }

        // The trash operation that put the form in the trash: its own, or its folder's (ADR-0047).
        [Column("trashKey")]
        [NullSetting(NullSetting = NullSettings.Null)]
        public Guid? TrashKey { get; set; }

        [Column("createdUtc")]
        public DateTime CreatedUtc { get; set; }

        [Column("updatedUtc")]
        public DateTime UpdatedUtc { get; set; }

        [Column("createdByUserKey")]
        [NullSetting(NullSetting = NullSettings.Null)]
        public Guid? CreatedByUserKey { get; set; }

        [Column("updatedByUserKey")]
        [NullSetting(NullSetting = NullSettings.Null)]
        public Guid? UpdatedByUserKey { get; set; }
    }

    // Every save of a form appends a row; published rows are never pruned (ADR-0002).
    [TableName(FormBuilderTables.FormVersion)]
    [PrimaryKey("id", AutoIncrement = true)]
    [ExplicitColumns]
    internal sealed class FormVersionDto
    {
        [Column("id")]
        [PrimaryKeyColumn(AutoIncrement = true)]
        public int Id { get; set; }

        [Column("formId")]
        [ForeignKey(typeof(FormDto), Name = "FK_jfbFormVersion_jfbForm", OnDelete = Rule.Cascade)]
        [Index(IndexTypes.UniqueNonClustered, Name = "IX_jfbFormVersion_formId_number", ForColumns = "formId,number")]
        public int FormId { get; set; }

        [Column("number")]
        public int Number { get; set; }

        // Draft or Published.
        [Column("kind")]
        [Length(16)]
        public string Kind { get; set; } = string.Empty;

        [Column("schemaVersion")]
        public int SchemaVersion { get; set; }

        [Column("definition")]
        [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
        public string Definition { get; set; } = string.Empty;

        // SHA-256 of the definition's UTF-8 bytes, and of the canonical public projection, in lowercase hex.
        [Column("definitionHash")]
        [Length(64)]
        public string DefinitionHash { get; set; } = string.Empty;

        [Column("publicHash")]
        [Length(64)]
        public string PublicHash { get; set; } = string.Empty;

        // Set on published rows only.
        [Column("publicRevision")]
        [NullSetting(NullSetting = NullSettings.Null)]
        public int? PublicRevision { get; set; }

        [Column("comment")]
        [Length(500)]
        [NullSetting(NullSetting = NullSettings.Null)]
        public string? Comment { get; set; }

        [Column("isPinned")]
        public bool IsPinned { get; set; }

        [Column("createdUtc")]
        public DateTime CreatedUtc { get; set; }

        [Column("createdByUserKey")]
        [NullSetting(NullSetting = NullSettings.Null)]
        public Guid? CreatedByUserKey { get; set; }

        [Column("publishedUtc")]
        [NullSetting(NullSetting = NullSettings.Null)]
        public DateTime? PublishedUtc { get; set; }

        [Column("publishedByUserKey")]
        [NullSetting(NullSetting = NullSettings.Null)]
        public Guid? PublishedByUserKey { get; set; }
    }

    // The field catalogue: every field ever published, plus draft-only fields that preview entries answered (ADR-0003).
    [TableName(FormBuilderTables.FormField)]
    [PrimaryKey("id", AutoIncrement = true)]
    [ExplicitColumns]
    internal sealed class FormFieldDto
    {
        [Column("id")]
        [PrimaryKeyColumn(AutoIncrement = true)]
        public int Id { get; set; }

        [Column("formId")]
        [ForeignKey(typeof(FormDto), Name = "FK_jfbFormField_jfbForm", OnDelete = Rule.Cascade)]
        [Index(IndexTypes.UniqueNonClustered, Name = "IX_jfbFormField_formId_fieldKey", ForColumns = "formId,fieldKey")]
        public int FormId { get; set; }

        [Column("fieldKey")]
        public Guid FieldKey { get; set; }

        [Column("alias")]
        [Length(64)]
        public string Alias { get; set; } = string.Empty;

        // The label in the default culture, for entry columns and exports.
        [Column("label")]
        [Length(500)]
        public string Label { get; set; } = string.Empty;

        // The field type's alias, built in or custom.
        [Column("type")]
        [Length(128)]
        public string Type { get; set; } = string.Empty;

        // Which typed column of jfbEntryValue holds its values.
        [Column("storageKind")]
        [Length(16)]
        public string StorageKind { get; set; } = string.Empty;

        [Column("isSensitive")]
        public bool IsSensitive { get; set; }

        [Column("sortOrder")]
        public int SortOrder { get; set; }

        // Null: only in a draft so far, shown with test entries only. Set by the first publish that has the field.
        [Column("firstVersionNumber")]
        [NullSetting(NullSetting = NullSettings.Null)]
        public int? FirstVersionNumber { get; set; }

        // Null while the live version has the field (once FirstVersionNumber is set).
        [Column("lastVersionNumber")]
        [NullSetting(NullSetting = NullSettings.Null)]
        public int? LastVersionNumber { get; set; }

        // The repeater a child field belongs to, or the grid a row belongs to.
        [Column("parentFieldKey")]
        [NullSetting(NullSetting = NullSettings.Null)]
        public Guid? ParentFieldKey { get; set; }
    }
}
