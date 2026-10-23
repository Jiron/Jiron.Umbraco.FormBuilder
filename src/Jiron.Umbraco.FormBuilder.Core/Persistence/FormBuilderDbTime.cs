namespace Jiron.Umbraco.FormBuilder.Core.Persistence;

/// <summary>
/// Times in Jiron Form Builder's tables are UTC (ADR-0003, ADR-0033). Both databases hand them back without a kind, so
/// reads mark them UTC here, and writes convert whatever offset they get. A time is always written as a UTC
/// <see cref="DateTime"/> parameter, never as text, so SQLite stores one format that sorts correctly.
/// </summary>
internal static class FormBuilderDbTime
{
    /// <summary>A stored time as a UTC instant.</summary>
    public static DateTimeOffset Read(DateTime stored) => new(DateTime.SpecifyKind(stored, DateTimeKind.Utc));

    /// <summary>A nullable stored time as a UTC instant.</summary>
    public static DateTimeOffset? Read(DateTime? stored) => stored is null ? null : Read(stored.Value);

    /// <summary>An instant as the UTC value to store.</summary>
    public static DateTime Write(DateTimeOffset value) => value.UtcDateTime;

    /// <summary>A nullable instant as the UTC value to store.</summary>
    public static DateTime? Write(DateTimeOffset? value) => value?.UtcDateTime;
}
