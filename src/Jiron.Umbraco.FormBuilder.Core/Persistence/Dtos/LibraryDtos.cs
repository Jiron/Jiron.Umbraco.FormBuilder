using NPoco;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Dtos;

// The repositories' rows of the library and permission tables (ADR-0033, ADR-0037): the columns of schema v1, without
// the schema annotations, which belong to the frozen snapshots of the migration steps. ProductDtoTests checks that each
// names exactly the columns of its table with the same types.

/// <summary>The columns every library row named by an alias has (ADR-0037).</summary>
internal interface IAliasedLibraryRow
{
    /// <summary>The row's id.</summary>
    int Id { get; }

    /// <summary>The version every change raises, starting at 1.</summary>
    int ConcurrencyVersion { get; }
}

/// <summary>A row of <c>jfbOptionList</c>.</summary>
[TableName(FormBuilderTables.OptionList)]
[PrimaryKey("id", AutoIncrement = true)]
[ExplicitColumns]
internal sealed class OptionListDto : IAliasedLibraryRow
{
    [Column("id")]
    public int Id { get; set; }

    [Column("key")]
    public Guid Key { get; set; }

    [Column("alias")]
    public string Alias { get; set; } = string.Empty;

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("kind")]
    public string Kind { get; set; } = string.Empty;

    [Column("config")]
    public string Config { get; set; } = "{}";

    [Column("items")]
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

/// <summary>A row of <c>jfbTheme</c>.</summary>
[TableName(FormBuilderTables.Theme)]
[PrimaryKey("id", AutoIncrement = true)]
[ExplicitColumns]
internal sealed class ThemeDto : IAliasedLibraryRow
{
    [Column("id")]
    public int Id { get; set; }

    [Column("key")]
    public Guid Key { get; set; }

    [Column("alias")]
    public string Alias { get; set; } = string.Empty;

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("baseTheme")]
    public string BaseTheme { get; set; } = string.Empty;

    [Column("tokens")]
    public string Tokens { get; set; } = "{}";

    [Column("customCss")]
    public string? CustomCss { get; set; }

    [Column("concurrencyVersion")]
    public int ConcurrencyVersion { get; set; }

    [Column("createdUtc")]
    public DateTime CreatedUtc { get; set; }

    [Column("updatedUtc")]
    public DateTime UpdatedUtc { get; set; }
}

/// <summary>A row of <c>jfbEmailLayout</c>.</summary>
[TableName(FormBuilderTables.EmailLayout)]
[PrimaryKey("id", AutoIncrement = true)]
[ExplicitColumns]
internal sealed class EmailLayoutDto : IAliasedLibraryRow
{
    [Column("id")]
    public int Id { get; set; }

    [Column("key")]
    public Guid Key { get; set; }

    [Column("alias")]
    public string Alias { get; set; } = string.Empty;

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("layout")]
    public string Layout { get; set; } = "{}";

    [Column("concurrencyVersion")]
    public int ConcurrencyVersion { get; set; }

    [Column("createdUtc")]
    public DateTime CreatedUtc { get; set; }

    [Column("updatedUtc")]
    public DateTime UpdatedUtc { get; set; }
}

/// <summary>A row of <c>jfbFormTemplate</c>.</summary>
[TableName(FormBuilderTables.FormTemplate)]
[PrimaryKey("id", AutoIncrement = true)]
[ExplicitColumns]
internal sealed class FormTemplateDto
{
    [Column("id")]
    public int Id { get; set; }

    [Column("key")]
    public Guid Key { get; set; }

    [Column("kind")]
    public string Kind { get; set; } = string.Empty;

    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Column("description")]
    public string? Description { get; set; }

    [Column("category")]
    public string? Category { get; set; }

    [Column("definition")]
    public string Definition { get; set; } = string.Empty;

    [Column("createdUtc")]
    public DateTime CreatedUtc { get; set; }

    [Column("createdByUserKey")]
    public Guid? CreatedByUserKey { get; set; }
}

/// <summary>A row of <c>jfbPermission</c>.</summary>
[TableName(FormBuilderTables.Permission)]
[PrimaryKey("id", AutoIncrement = true)]
[ExplicitColumns]
internal sealed class PermissionDto
{
    [Column("id")]
    public int Id { get; set; }

    [Column("userGroupKey")]
    public Guid UserGroupKey { get; set; }

    [Column("targetType")]
    public string TargetType { get; set; } = string.Empty;

    [Column("targetKey")]
    public Guid TargetKey { get; set; }

    [Column("permissions")]
    public int Permissions { get; set; }
}
