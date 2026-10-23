using NPoco;
using Umbraco.Cms.Infrastructure.Persistence.DatabaseAnnotations;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;

// Frozen snapshots of the library tables as step 1 creates them. Never edit. Definitions and settings refer to these
// rows by key (themes by alias), never through foreign keys.
internal sealed partial class CreateSchemaV1
{
    // Reusable option lists (section 4.3.2).
    [TableName(FormBuilderTables.OptionList)]
    [PrimaryKey("id", AutoIncrement = true)]
    [ExplicitColumns]
    internal sealed class OptionListDto
    {
        [Column("id")]
        [PrimaryKeyColumn(AutoIncrement = true)]
        public int Id { get; set; }

        [Column("key")]
        [Index(IndexTypes.UniqueNonClustered, Name = "IX_jfbOptionList_key")]
        public Guid Key { get; set; }

        [Column("alias")]
        [Length(64)]
        [Index(IndexTypes.UniqueNonClustered, Name = "IX_jfbOptionList_alias")]
        public string Alias { get; set; } = string.Empty;

        [Column("name")]
        [Length(255)]
        public string Name { get; set; } = string.Empty;

        // Manual, Content, DataType, Url, File, Countries or Code.
        [Column("kind")]
        [Length(16)]
        public string Kind { get; set; } = string.Empty;

        // JSON: the source's configuration, never in the public projection.
        [Column("config")]
        [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
        public string Config { get; set; } = "{}";

        // JSON: the items of a manual or uploaded list.
        [Column("items")]
        [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
        public string Items { get; set; } = "[]";

        [Column("cacheSeconds")]
        public int CacheSeconds { get; set; }

        [Column("concurrencyVersion")]
        public int ConcurrencyVersion { get; set; }

        [Column("createdUtc")]
        public DateTime CreatedUtc { get; set; }

        [Column("updatedUtc")]
        public DateTime UpdatedUtc { get; set; }
    }

    // Themes for the website form (ADR-0015). The default theme is a General setting per site, not a flag here.
    [TableName(FormBuilderTables.Theme)]
    [PrimaryKey("id", AutoIncrement = true)]
    [ExplicitColumns]
    internal sealed class ThemeDto
    {
        [Column("id")]
        [PrimaryKeyColumn(AutoIncrement = true)]
        public int Id { get; set; }

        [Column("key")]
        [Index(IndexTypes.UniqueNonClustered, Name = "IX_jfbTheme_key")]
        public Guid Key { get; set; }

        // Fixed once created: forms refer to it.
        [Column("alias")]
        [Length(64)]
        [Index(IndexTypes.UniqueNonClustered, Name = "IX_jfbTheme_alias")]
        public string Alias { get; set; } = string.Empty;

        [Column("name")]
        [Length(255)]
        public string Name { get; set; } = string.Empty;

        [Column("baseTheme")]
        [Length(64)]
        public string BaseTheme { get; set; } = string.Empty;

        // JSON: the design tokens.
        [Column("tokens")]
        [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
        public string Tokens { get; set; } = "{}";

        [Column("customCss")]
        [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
        [NullSetting(NullSetting = NullSettings.Null)]
        public string? CustomCss { get; set; }

        [Column("concurrencyVersion")]
        public int ConcurrencyVersion { get; set; }

        [Column("createdUtc")]
        public DateTime CreatedUtc { get; set; }

        [Column("updatedUtc")]
        public DateTime UpdatedUtc { get; set; }
    }

    // Email layouts: logo, colours, header and footer per culture. The default is an Email setting per site.
    [TableName(FormBuilderTables.EmailLayout)]
    [PrimaryKey("id", AutoIncrement = true)]
    [ExplicitColumns]
    internal sealed class EmailLayoutDto
    {
        [Column("id")]
        [PrimaryKeyColumn(AutoIncrement = true)]
        public int Id { get; set; }

        [Column("key")]
        [Index(IndexTypes.UniqueNonClustered, Name = "IX_jfbEmailLayout_key")]
        public Guid Key { get; set; }

        [Column("alias")]
        [Length(64)]
        [Index(IndexTypes.UniqueNonClustered, Name = "IX_jfbEmailLayout_alias")]
        public string Alias { get; set; } = string.Empty;

        [Column("name")]
        [Length(255)]
        public string Name { get; set; } = string.Empty;

        // JSON.
        [Column("layout")]
        [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
        public string Layout { get; set; } = "{}";

        // Edited in the library like themes, so stale saves fail the same way (ADR-0033).
        [Column("concurrencyVersion")]
        public int ConcurrencyVersion { get; set; }

        [Column("createdUtc")]
        public DateTime CreatedUtc { get; set; }

        [Column("updatedUtc")]
        public DateTime UpdatedUtc { get; set; }
    }

    // Templates saved by users: whole forms, and groups of fields inserted as copies with new keys.
    [TableName(FormBuilderTables.FormTemplate)]
    [PrimaryKey("id", AutoIncrement = true)]
    [ExplicitColumns]
    internal sealed class FormTemplateDto
    {
        [Column("id")]
        [PrimaryKeyColumn(AutoIncrement = true)]
        public int Id { get; set; }

        [Column("key")]
        [Index(IndexTypes.UniqueNonClustered, Name = "IX_jfbFormTemplate_key")]
        public Guid Key { get; set; }

        // Form or Group.
        [Column("kind")]
        [Length(16)]
        public string Kind { get; set; } = string.Empty;

        [Column("name")]
        [Length(255)]
        public string Name { get; set; } = string.Empty;

        [Column("description")]
        [Length(2000)]
        [NullSetting(NullSetting = NullSettings.Null)]
        public string? Description { get; set; }

        [Column("category")]
        [Length(64)]
        [NullSetting(NullSetting = NullSettings.Null)]
        public string? Category { get; set; }

        [Column("definition")]
        [SpecialDbType(SpecialDbTypes.NVARCHARMAX)]
        public string Definition { get; set; } = string.Empty;

        [Column("createdUtc")]
        public DateTime CreatedUtc { get; set; }

        [Column("createdByUserKey")]
        [NullSetting(NullSetting = NullSettings.Null)]
        public Guid? CreatedByUserKey { get; set; }
    }
}
