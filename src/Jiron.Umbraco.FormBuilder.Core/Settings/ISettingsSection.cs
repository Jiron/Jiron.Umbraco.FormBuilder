namespace Jiron.Umbraco.FormBuilder.Core.Settings;

/// <summary>
/// A backoffice settings section (ADR-0008, ADR-0043): an immutable record whose property initializers are the defaults,
/// stored as camelCase JSON in <c>jfbSetting</c>, one row per scope. A missing property takes its default and an unknown
/// one is dropped, so every row loads; a stored null means the default.
/// </summary>
internal interface ISettingsSection
{
    /// <summary>The section's alias: its key in storage, errors, locks and the management API.</summary>
    static abstract string Alias { get; }

    /// <summary>The version of the section's JSON shape; an older row is migrated when it is read.</summary>
    static abstract int SchemaVersion { get; }

    /// <summary>True when each site can override the section's fields; false when every site has the global values.</summary>
    static virtual bool PerSite => false;
}

/// <summary>A field of a per-site section that every site shares (ADR-0043): a site cannot override it.</summary>
[AttributeUsage(AttributeTargets.Property)]
internal sealed class GlobalOnlyAttribute : Attribute;
