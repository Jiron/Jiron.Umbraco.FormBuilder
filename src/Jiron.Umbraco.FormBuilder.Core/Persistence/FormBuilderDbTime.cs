namespace Jiron.Umbraco.FormBuilder.Core.Persistence;

/// <summary>
/// Times in Jiron Form Builder's tables are UTC (ADR-0003, ADR-0033). Both databases hand them back without a kind, so
/// reads mark them UTC here, and writes convert whatever offset they get. A time is always written as a UTC
/// <see cref="DateTime"/> parameter, never as text, so SQLite stores one format that sorts correctly.
/// </summary>
internal static class FormBuilderDbTime
{
    /// <summary>The earliest time both databases store: SQL Server's <c>DATETIME</c> starts in 1753.</summary>
    public static readonly DateTimeOffset MinStorable = new(1753, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>The latest time both databases store: the last midnight of <c>DATETIME</c>, so rounding cannot carry it past the end.</summary>
    public static readonly DateTimeOffset MaxStorable = new(9999, 12, 31, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Whether both databases can store <paramref name="value"/> and compare a stored time with it.</summary>
    public static bool IsStorable(DateTimeOffset value) => value >= MinStorable && value <= MaxStorable;

    /// <summary>A stored time as a UTC instant.</summary>
    public static DateTimeOffset Read(DateTime stored) => new(DateTime.SpecifyKind(stored, DateTimeKind.Utc));

    /// <summary>A nullable stored time as a UTC instant.</summary>
    public static DateTimeOffset? Read(DateTime? stored) => stored is null ? null : Read(stored.Value);

    /// <summary>An instant as the UTC value to store.</summary>
    public static DateTime Write(DateTimeOffset value) => value.UtcDateTime;

    /// <summary>A nullable instant as the UTC value to store.</summary>
    public static DateTime? Write(DateTimeOffset? value) => value?.UtcDateTime;
}
