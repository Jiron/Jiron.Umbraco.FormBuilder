using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Configuration;

namespace Jiron.Umbraco.FormBuilder.Core.Settings;

/// <summary>How the host configuration rules a backoffice field (ADR-0008).</summary>
internal enum SettingsLockKind
{
    /// <summary>The field has the value the host sets, or a locked section's stored value: read-only.</summary>
    Locked,

    /// <summary>Some values are refused, and a refused one that is stored reads as the nearest allowed one.</summary>
    Restricted,

    /// <summary>Values above a host limit are lowered to it.</summary>
    Clamped,
}

/// <summary>A host rule on a field: what it does, and the configuration key behind it.</summary>
/// <param name="Kind">What the rule does.</param>
/// <param name="ConfigurationKey">The key, for example <c>JironFormBuilder:LockedSections:0</c>.</param>
internal sealed record SettingsLock(SettingsLockKind Kind, string ConfigurationKey);

/// <summary>Collects the locks the host rules put on a section's fields, by dotted path.</summary>
internal sealed class SettingsLocks
{
    /// <summary>The path of a lock on the whole section.</summary>
    public const string WholeSection = "*";

    private readonly Dictionary<string, SettingsLock> _locks = new(StringComparer.Ordinal);

    /// <summary>Every lock so far.</summary>
    public IReadOnlyDictionary<string, SettingsLock> All => _locks;

    /// <summary>Adds a lock on a field, given by its dotted path within the section; a later lock on the path wins.</summary>
    public void Add(string field, SettingsLockKind kind, string configurationKey)
    {
        ArgumentException.ThrowIfNullOrEmpty(field);
        ArgumentException.ThrowIfNullOrEmpty(configurationKey);
        _locks[field] = new SettingsLock(kind, configurationKey);
    }
}

/// <summary>
/// The host's rules on one section (ADR-0008, ADR-0043), registered with the host keys behind them. The reader applies
/// them where the value is used, so a lock added later takes effect at once and no row is rewritten; a save applies them
/// to what the editor sent, and refuses a field they change.
/// </summary>
/// <typeparam name="T">The section.</typeparam>
internal interface ISettingsHostRule<T>
    where T : ISettingsSection
{
    /// <summary>
    /// The section with the rules applied: a <see cref="SettingsLockKind.Locked"/> field set to the host's value, a
    /// <see cref="SettingsLockKind.Restricted"/> value the host refuses replaced by the nearest allowed one, a
    /// <see cref="SettingsLockKind.Clamped"/> value lowered to the host's limit. Every field it rules is added to
    /// <paramref name="locks"/>, whether its value changed or not.
    /// </summary>
    T Apply(T section, SettingsLocks locks);
}

/// <summary>Applies the host configuration to sections (ADR-0008, ADR-0043): locked sections, then each section's rules.</summary>
internal static class SettingsHostRules
{
    /// <summary>The configuration key of the locked sections.</summary>
    public const string LockedSectionsKey = FormBuilderOptions.SectionName + ":LockedSections";

    /// <summary>The section with the host's rules applied, and the locks by field path.</summary>
    public static (T Value, IReadOnlyDictionary<string, SettingsLock> Locks) Apply<T>(T section, FormBuilderOptions host, IEnumerable<ISettingsHostRule<T>> rules)
        where T : ISettingsSection
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(rules);
        var locks = new SettingsLocks();
        for (var i = 0; i < host.LockedSections.Count; i++)
        {
            if (string.Equals(host.LockedSections[i], T.Alias, StringComparison.OrdinalIgnoreCase))
            {
                locks.Add(SettingsLocks.WholeSection, SettingsLockKind.Locked, $"{LockedSectionsKey}:{i}");
                break;
            }
        }

        foreach (var rule in rules)
        {
            section = rule.Apply(section, locks);
        }

        return (section, locks.All);
    }

    /// <summary>
    /// Checks what a save writes against the host's rules: a locked section, or a field in <paramref name="written"/>
    /// whose value a locked or restricted rule changes, throws; a value a clamping rule lowers adds a
    /// <see cref="SettingsErrors.Range"/> error.
    /// </summary>
    /// <param name="proposed">The values the save would write, before the rules.</param>
    /// <param name="written">The top-level fields the save writes; a field outside them is not the save's to check.</param>
    /// <param name="host">The host configuration.</param>
    /// <param name="rules">The section's rules.</param>
    /// <param name="errors">Where a clamped value's error goes.</param>
    /// <exception cref="FormBuilderSettingLockedException">The save changes what the host locks.</exception>
    public static void CheckSave<T>(T proposed, IReadOnlySet<string> written, FormBuilderOptions host, IEnumerable<ISettingsHostRule<T>> rules, SettingsErrors errors)
        where T : ISettingsSection
    {
        ArgumentNullException.ThrowIfNull(written);
        ArgumentNullException.ThrowIfNull(errors);
        var (applied, locks) = Apply(proposed, host, rules);
        if (locks.TryGetValue(SettingsLocks.WholeSection, out var section))
        {
            throw new FormBuilderSettingLockedException(T.Alias, section.ConfigurationKey);
        }

        var before = SettingsSectionJson.ToNode(proposed);
        var after = SettingsSectionJson.ToNode(applied);
        foreach (var (field, rule) in locks.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (!written.Contains(SettingsSectionJson.TopField(field))
                || JsonNode.DeepEquals(SettingsSectionJson.At(before, field), SettingsSectionJson.At(after, field)))
            {
                continue;
            }

            if (rule.Kind == SettingsLockKind.Clamped)
            {
                errors.Add(field, SettingsErrors.Range, $"Is above the limit that {rule.ConfigurationKey} sets.");
            }
            else
            {
                throw new FormBuilderSettingLockedException($"{T.Alias}.{field}", rule.ConfigurationKey);
            }
        }
    }
}
