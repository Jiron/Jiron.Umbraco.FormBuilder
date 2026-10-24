using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Jiron.Umbraco.FormBuilder.Core.Settings;

namespace Jiron.Umbraco.FormBuilder.Testing.Settings;

/// <summary>
/// A settings section for the tests of the settings framework (ADR-0043): every kind of field, set per site, at schema
/// version 3, with migrations from version 1. Only a test registers it.
/// </summary>
internal sealed record SampleSettings : ISettingsSection
{
    /// <summary>The section's alias.</summary>
    public const string SectionAlias = "sample";

    /// <inheritdoc />
    public static string Alias => SectionAlias;

    /// <inheritdoc />
    public static int SchemaVersion => 3;

    /// <inheritdoc />
    public static bool PerSite => true;

    /// <summary>A text with a default; version 1 called it <c>title</c>.</summary>
    public string Name { get; init; } = "Jiron";

    /// <summary>A number from 0 to 100; version 2 stored it as text.</summary>
    public int Count { get; init; } = 5;

    /// <summary>An enum, stored as camelCase text.</summary>
    public SampleMode Mode { get; init; } = SampleMode.Quiet;

    /// <summary>A nested object, which a site overrides whole.</summary>
    public SampleLimits Limits { get; init; } = new();

    /// <summary>A text whose default is null: the built-in text.</summary>
    public string? Note { get; init; }

    /// <summary>A list, compared whole.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>A field every site shares.</summary>
    [GlobalOnly]
    public int Keep { get; init; } = 50;

    /// <summary>A field stored under a name of its own.</summary>
    [JsonPropertyName("summary")]
    public string? Description { get; init; }

    /// <summary>A value worked out from the others, which is never stored.</summary>
    [JsonIgnore]
    public string Display => Name + " (" + Count.ToString(CultureInfo.InvariantCulture) + ")";

    /// <summary>A tag by its position: an indexer, which is no field.</summary>
    public string this[int index] => Tags[index];
}

/// <summary>The modes of <see cref="SampleSettings.Mode"/>.</summary>
internal enum SampleMode
{
    /// <summary>The default.</summary>
    Quiet,

    /// <summary>A name of two words, stored as <c>loudAndClear</c>.</summary>
    LoudAndClear,
}

/// <summary>The nested object of <see cref="SampleSettings"/>.</summary>
internal sealed record SampleLimits
{
    /// <summary>A number a host can clamp.</summary>
    public int Max { get; init; } = 10;

    /// <summary>An optional text.</summary>
    public string? Label { get; init; }
}

/// <summary>A section every site shares, for the tests of the global scope.</summary>
internal sealed record SampleSharedSettings : ISettingsSection
{
    /// <summary>The section's alias.</summary>
    public const string SectionAlias = "sampleshared";

    /// <inheritdoc />
    public static string Alias => SectionAlias;

    /// <inheritdoc />
    public static int SchemaVersion => 1;

    /// <summary>A switch.</summary>
    public bool Enabled { get; init; } = true;
}

/// <summary>Version 1 to 2 of <see cref="SampleSettings"/>: <c>title</c> became <c>name</c>.</summary>
internal sealed class SampleTitleToName : ISettingsSectionMigration
{
    /// <inheritdoc />
    public string Section => SampleSettings.SectionAlias;

    /// <inheritdoc />
    public int FromVersion => 1;

    /// <inheritdoc />
    public void Migrate(JsonObject json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (json.Remove("title", out var title))
        {
            json["name"] = title;
        }
    }
}

/// <summary>Version 2 to 3 of <see cref="SampleSettings"/>: <c>count</c> was text and became a number.</summary>
internal sealed class SampleCountToNumber : ISettingsSectionMigration
{
    /// <inheritdoc />
    public string Section => SampleSettings.SectionAlias;

    /// <inheritdoc />
    public int FromVersion => 2;

    /// <inheritdoc />
    public void Migrate(JsonObject json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (json["count"] is JsonValue value && value.TryGetValue<string>(out var text) && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var count))
        {
            json["count"] = count;
        }
    }
}

/// <summary>
/// Checks <see cref="SampleSettings"/>: the name is trimmed and required, the count 0 to 100, and the tags no more than
/// <see cref="SampleSettings.Keep"/>, which every site shares.
/// </summary>
internal sealed class SampleSettingsValidator : ISettingsValidator<SampleSettings>
{
    /// <inheritdoc />
    public SampleSettings Normalize(SampleSettings section)
    {
        ArgumentNullException.ThrowIfNull(section);
        return section with { Name = section.Name?.Trim()! };
    }

    /// <inheritdoc />
    public void Validate(SampleSettings section, SettingsErrors errors)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(errors);
        if (string.IsNullOrEmpty(section.Name))
        {
            errors.Add("name", "required", "Is required.");
        }

        if (section.Count is < 0 or > 100)
        {
            errors.Add("count", SettingsErrors.Range, "Must be between 0 and 100.");
        }

        if (section.Tags.Count > section.Keep)
        {
            errors.Add("tags", "maxCount", "Holds more tags than keep allows.");
        }
    }
}

/// <summary>
/// Host rules on <see cref="SampleSettings"/> that a test switches on: the name locked to a value, the mode restricted to
/// <see cref="SampleMode.Quiet"/>, and the nested maximum clamped. Off until a test sets them.
/// </summary>
internal sealed class SampleHostRule : ISettingsHostRule<SampleSettings>
{
    /// <summary>The key of the name's lock.</summary>
    public const string NameKey = "JironFormBuilder:Sample:Name";

    /// <summary>The key of the mode's restriction.</summary>
    public const string ModeKey = "JironFormBuilder:Sample:AllowLoud";

    /// <summary>The key of the maximum's limit.</summary>
    public const string MaxKey = "JironFormBuilder:Sample:MaxLimit";

    /// <summary>The value the host locks the name to, or null for no lock.</summary>
    public string? LockedName { get; set; }

    /// <summary>True when the host refuses <see cref="SampleMode.LoudAndClear"/>.</summary>
    public bool QuietOnly { get; set; }

    /// <summary>The highest maximum the host allows, or null for no limit.</summary>
    public int? MaxLimit { get; set; }

    /// <inheritdoc />
    public SampleSettings Apply(SampleSettings section, SettingsLocks locks)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(locks);
        if (LockedName is { } name)
        {
            locks.Add("name", SettingsLockKind.Locked, NameKey);
            section = section with { Name = name };
        }

        if (QuietOnly)
        {
            locks.Add("mode", SettingsLockKind.Restricted, ModeKey);
            section = section with { Mode = SampleMode.Quiet };
        }

        if (MaxLimit is { } limit)
        {
            locks.Add("limits.max", SettingsLockKind.Clamped, MaxKey);
            section = section with { Limits = section.Limits with { Max = Math.Min(section.Limits.Max, limit) } };
        }

        return section;
    }
}
