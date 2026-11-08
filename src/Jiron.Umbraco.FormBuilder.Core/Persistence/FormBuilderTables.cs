namespace Jiron.Umbraco.FormBuilder.Core.Persistence;

/// <summary>
/// Jiron Form Builder's table names (section 4.5, ADR-0033): <c>jfbPascalCase</c>, with columns in camelCase like Umbraco's
/// own tables.
/// </summary>
internal static class FormBuilderTables
{
    public const string KeyValue = "jfbKeyValue";
    public const string Folder = "jfbFolder";
    public const string Form = "jfbForm";
    public const string FormVersion = "jfbFormVersion";
    public const string FormField = "jfbFormField";
    public const string Entry = "jfbEntry";
    public const string EntryFile = "jfbEntryFile";
    public const string EntryValue = "jfbEntryValue";
    public const string ConsentText = "jfbConsentText";
    public const string EntryEvent = "jfbEntryEvent";
    public const string EntryNote = "jfbEntryNote";
    public const string ActionRun = "jfbActionRun";
    public const string Email = "jfbEmail";
    public const string OptionCapacity = "jfbOptionCapacity";
    public const string OptionList = "jfbOptionList";
    public const string Theme = "jfbTheme";
    public const string EmailLayout = "jfbEmailLayout";
    public const string FormTemplate = "jfbFormTemplate";
    public const string UserSetting = "jfbUserSetting";
    public const string Permission = "jfbPermission";
    public const string Setting = "jfbSetting";
    public const string Reference = "jfbReference";
    public const string AuditEntry = "jfbAuditEntry";
    public const string DailyStat = "jfbDailyStat";
    public const string FormLibraryUse = "jfbFormLibraryUse";

    /// <summary>
    /// The tables of schema v1, in the order it creates them: a table comes after the tables it refers to, except
    /// <see cref="Form"/>, whose draft and published versions live in <see cref="FormVersion"/>, which refers back to it.
    /// </summary>
    public static readonly IReadOnlyList<string> SchemaV1 =
    [
        KeyValue, Folder, Form, FormVersion, FormField, Entry, EntryFile, EntryValue, ConsentText, EntryEvent, EntryNote,
        ActionRun, Email, OptionCapacity, OptionList, Theme, EmailLayout, FormTemplate, UserSetting, Permission, Setting,
        Reference, AuditEntry, DailyStat,
    ];

    /// <summary>Every table: those of schema v1, then those of later steps in their order (ADR-0214).</summary>
    public static readonly IReadOnlyList<string> All = [.. SchemaV1, FormLibraryUse];
}
