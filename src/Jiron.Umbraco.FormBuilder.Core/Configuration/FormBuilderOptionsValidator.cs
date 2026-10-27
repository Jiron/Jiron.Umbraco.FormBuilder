using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Outbound;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Jiron.Umbraco.FormBuilder.Core.Configuration;

/// <summary>
/// Checks the host configuration when the site starts (ADR-0008, ADR-0042). It collects every problem, and each message
/// names the key, with its list index, and what is allowed, so the site's log says exactly what to fix.
/// </summary>
/// <param name="folders">The site's folders, for the file store's root.</param>
/// <param name="section">The configuration the options are bound from, for keys they do not have.</param>
internal sealed class FormBuilderOptionsValidator(IHostFolders folders, IConfigurationSection section) : IValidateOptions<FormBuilderOptions>
{
    /// <summary>The longest type alias and setting name of a designer lock.</summary>
    public const int MaxNameLength = 64;

    /// <summary>The longest value of a designer lock: the longest text a definition holds (ADR-0002).</summary>
    public const int MaxLockValueLength = 50_000;

    private const string Root = FormBuilderOptions.SectionName;

    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, FormBuilderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (name is not null && name != Options.DefaultName)
        {
            return ValidateOptionsResult.Skip;
        }

        var failures = new List<string>();
        ConfigurationKeys.Check(section, typeof(FormBuilderOptions), failures);
        if (!FileStoreRoot.TryResolve(options.Files.Root, folders, out _, out var problem))
        {
            failures.Add(problem);
        }

        CheckDefinitionLimits(options.Limits.Definitions, failures);
        CheckSettingLocks(options.Designer.SettingLocks, failures);
        CheckLockedSections(options.LockedSections, failures);
        CheckHostGrants(options.Access.Grants, failures);
        CheckAllowedHosts(options.Webhooks.AllowedHosts, failures);
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>Whether <paramref name="type"/> can name a field type or an action type: a letter, then letters, digits, '.', '-' and '_'.</summary>
    internal static bool IsTypeAlias(string? type) =>
        type is { Length: > 0 and <= MaxNameLength } && char.IsAsciiLetter(type[0]) && type.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_');

    /// <summary>Whether <paramref name="setting"/> can name a setting: a lowercase letter, then letters and digits.</summary>
    internal static bool IsSettingName(string? setting) =>
        setting is { Length: > 0 and <= MaxNameLength } && char.IsAsciiLetterLower(setting[0]) && setting.All(char.IsAsciiLetterOrDigit);

    private static void CheckDefinitionLimits(DefinitionLimits limits, List<string> failures)
    {
        var ceiling = DefinitionLimits.Ceiling;
        (string Name, int Value, int Highest)[] checks =
        [
            (nameof(DefinitionLimits.MaxBytes), limits.MaxBytes, ceiling.MaxBytes),
            (nameof(DefinitionLimits.MaxPages), limits.MaxPages, ceiling.MaxPages),
            (nameof(DefinitionLimits.MaxGroups), limits.MaxGroups, ceiling.MaxGroups),
            (nameof(DefinitionLimits.MaxFields), limits.MaxFields, ceiling.MaxFields),
            (nameof(DefinitionLimits.MaxOptionsPerField), limits.MaxOptionsPerField, ceiling.MaxOptionsPerField),
            (nameof(DefinitionLimits.MaxActions), limits.MaxActions, ceiling.MaxActions),
            (nameof(DefinitionLimits.MaxOutcomes), limits.MaxOutcomes, ceiling.MaxOutcomes),
            (nameof(DefinitionLimits.MaxRules), limits.MaxRules, ceiling.MaxRules),
        ];
        foreach (var (name, value, highest) in checks)
        {
            if (value < 1 || value > highest)
            {
                failures.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{Root}:Limits:Definitions:{name} ({value}) must be 1 to {highest}, which is {DefinitionLimits.CeilingFactor} times its default."));
            }
        }
    }

    private static void CheckSettingLocks(IList<DesignerSettingLock> locks, List<string> failures)
    {
        var seen = new Dictionary<(string Kind, string Type, string Setting), int>();
        for (var i = 0; i < locks.Count; i++)
        {
            var key = string.Create(CultureInfo.InvariantCulture, $"{Root}:Designer:SettingLocks:{i}");
            if (locks[i] is not { } settingLock)
            {
                failures.Add($"{key} is empty: give its Kind, Type and Setting.");
                continue;
            }

            var kind = settingLock.Kind?.ToLowerInvariant();
            var valid = true;
            if (kind is not (DesignerSettingLock.FieldKind or DesignerSettingLock.ActionKind))
            {
                failures.Add($"{key}:Kind ('{settingLock.Kind}') must be '{DesignerSettingLock.FieldKind}' or '{DesignerSettingLock.ActionKind}'.");
                valid = false;
            }

            if (!IsTypeAlias(settingLock.Type))
            {
                failures.Add($"{key}:Type ('{settingLock.Type}') must be a type's alias, such as 'email': a letter, then letters, digits, '.', '-' and '_', at most {MaxNameLength} characters.");
                valid = false;
            }

            if (!IsSettingName(settingLock.Setting))
            {
                failures.Add($"{key}:Setting ('{settingLock.Setting}') must be a setting's name, such as 'from': a lowercase letter, then letters and digits, at most {MaxNameLength} characters.");
                valid = false;
            }

            // The value is never repeated: it may be an address or a reference that belongs in the configuration only.
            if (settingLock.Value is { Length: > MaxLockValueLength } value)
            {
                failures.Add(string.Create(CultureInfo.InvariantCulture, $"{key}:Value has {value.Length} characters; a lock's value has at most {MaxLockValueLength}."));
            }

            if (valid && !seen.TryAdd((kind!, settingLock.Type!, settingLock.Setting!), i))
            {
                failures.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{key} locks the setting '{settingLock.Setting}' of the {kind} type '{settingLock.Type}' again; {Root}:Designer:SettingLocks:{seen[(kind!, settingLock.Type!, settingLock.Setting!)]} locks it already. Keep one."));
            }
        }
    }

    // Each host grant names a group once and grants known permissions with the ones they need (ADR-0048).
    private static void CheckHostGrants(IList<HostGrant> grants, List<string> failures)
    {
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < grants.Count; i++)
        {
            var key = string.Create(CultureInfo.InvariantCulture, $"{Root}:Access:Grants:{i}");
            if (grants[i] is not { } grant)
            {
                failures.Add($"{key} is empty: give its Group and Permissions.");
                continue;
            }

            if (string.IsNullOrWhiteSpace(grant.Group))
            {
                failures.Add($"{key}:Group is missing: name a user group by its alias, such as 'editor'.");
            }
            else if (!seen.TryAdd(grant.Group, i))
            {
                failures.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{key}:Group ('{grant.Group}') is granted again; {Root}:Access:Grants:{seen[grant.Group]} grants it already. Keep one."));
            }

            if (grant.Permissions.Count == 0)
            {
                failures.Add($"{key}:Permissions is empty: list at least one permission, or remove the grant.");
            }

            var permissions = FormBuilderPermissions.None;
            for (var j = 0; j < grant.Permissions.Count; j++)
            {
                if (PermissionRules.TryParse(grant.Permissions[j], out var permission))
                {
                    permissions |= permission;
                }
                else
                {
                    failures.Add(string.Create(
                        CultureInfo.InvariantCulture,
                        $"{key}:Permissions:{j} ('{grant.Permissions[j]}') is not a permission; use one of {string.Join(", ", PermissionRules.NamesOf(PermissionRules.All))}."));
                }
            }

            foreach (var missing in PermissionRules.MissingDependencies(permissions))
            {
                failures.Add($"{key}:Permissions grants '{PermissionRules.NameOf(missing.Permission)}' without '{PermissionRules.NameOf(missing.Needs)}', which it needs.");
            }
        }
    }

    // Each allowed host is a host name or an IP address, named once (ADR-0076).
    private static void CheckAllowedHosts(IList<string> hosts, List<string> failures)
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < hosts.Count; i++)
        {
            var key = string.Create(CultureInfo.InvariantCulture, $"{Root}:Webhooks:AllowedHosts:{i}");
            if (OutboundHosts.Normalize(hosts[i]) is not { } host)
            {
                failures.Add($"{key} ('{hosts[i]}') must be a host name such as 'events.example.com' or an IP address, with no scheme, port, path or wildcard.");
            }
            else if (!seen.TryAdd(host, i))
            {
                failures.Add(string.Create(CultureInfo.InvariantCulture, $"{key} ('{hosts[i]}') names a host again; {Root}:Webhooks:AllowedHosts:{seen[host]} names it already. Keep one."));
            }
        }
    }

    private static void CheckLockedSections(IList<string> sections, List<string> failures)
    {
        for (var i = 0; i < sections.Count; i++)
        {
            if (!FormBuilderSettingsSections.Lockable.Contains(sections[i], StringComparer.OrdinalIgnoreCase))
            {
                failures.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{Root}:LockedSections:{i} ('{sections[i]}') is not a settings section; use one of {string.Join(", ", FormBuilderSettingsSections.Lockable)}."));
            }
        }
    }
}
