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

        CheckFileLimits(options.Files, failures);
        CheckDefinitionLimits(options.Limits.Definitions, failures);
        CheckRateLimits(options.Limits, failures);
        CheckSettingLocks(options.Designer.SettingLocks, failures);
        CheckLockedSections(options.LockedSections, failures);
        CheckHostGrants(options.Access.Grants, failures);
        CheckAllowedHosts(options.Webhooks.AllowedHosts, failures);
        CheckPublicApi(options.PublicApi, failures);
        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    /// <summary>Whether <paramref name="type"/> can name a field type or an action type: a letter, then letters, digits, '.', '-' and '_'.</summary>
    internal static bool IsTypeAlias(string? type) =>
        type is { Length: > 0 and <= MaxNameLength } && char.IsAsciiLetter(type[0]) && type.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_');

    /// <summary>Whether <paramref name="setting"/> can name a setting: a lowercase letter, then letters and digits.</summary>
    internal static bool IsSettingName(string? setting) =>
        setting is { Length: > 0 and <= MaxNameLength } && char.IsAsciiLetterLower(setting[0]) && setting.All(char.IsAsciiLetterOrDigit);

    // The most bytes of a file within its bounds, and each blocked extension lowercase letters and digits, once (ADR-0087).
    private static void CheckFileLimits(FormBuilderFilesOptions files, List<string> failures)
    {
        if (files.MaxFileBytes is < 1 or > FormBuilderFilesOptions.HighestMaxFileBytes)
        {
            failures.Add(string.Create(CultureInfo.InvariantCulture, $"{Root}:Files:MaxFileBytes ({files.MaxFileBytes}) must be 1 to {FormBuilderFilesOptions.HighestMaxFileBytes}."));
        }

        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < files.BlockedExtensions.Count; i++)
        {
            var key = string.Create(CultureInfo.InvariantCulture, $"{Root}:Files:BlockedExtensions:{i}");
            var extension = files.BlockedExtensions[i];
            if (extension is not { Length: > 0 and <= FormBuilderFilesOptions.MaxExtensionLength } || !extension.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c)))
            {
                failures.Add(string.Create(CultureInfo.InvariantCulture, $"{key} ('{extension}') must be an extension in lowercase letters and digits without the point, at most {FormBuilderFilesOptions.MaxExtensionLength} of them, such as 'exe'."));
            }
            else if (!seen.TryAdd(extension, i))
            {
                failures.Add(string.Create(CultureInfo.InvariantCulture, $"{key} ('{extension}') names an extension again; {Root}:Files:BlockedExtensions:{seen[extension]} names it already. Keep one."));
            }
        }
    }

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

    // The rate limits within their bounds (ADR-0012, ADR-0137).
    private static void CheckRateLimits(FormBuilderLimitsOptions limits, List<string> failures)
    {
        if (limits.MaxTrackedClients is < FormBuilderLimitsOptions.LeastTrackedClients or > FormBuilderLimitsOptions.HighestRate)
        {
            failures.Add(string.Create(CultureInfo.InvariantCulture, $"{Root}:Limits:MaxTrackedClients ({limits.MaxTrackedClients}) must be {FormBuilderLimitsOptions.LeastTrackedClients} to {FormBuilderLimitsOptions.HighestRate}."));
        }

        if (limits.ServerKeyFactor is < 1 or > FormBuilderLimitsOptions.HighestServerKeyFactor)
        {
            failures.Add(string.Create(CultureInfo.InvariantCulture, $"{Root}:Limits:ServerKeyFactor ({limits.ServerKeyFactor}) must be 1 to {FormBuilderLimitsOptions.HighestServerKeyFactor}."));
        }

        (string Name, FormBuilderRatePolicyOptions Policy)[] policies =
        [
            (nameof(limits.Read), limits.Read),
            (nameof(limits.Challenge), limits.Challenge),
            (nameof(limits.Submit), limits.Submit),
            (nameof(limits.Upload), limits.Upload),
            (nameof(limits.Events), limits.Events),
            (nameof(limits.Verify), limits.Verify),
        ];
        foreach (var (name, policy) in policies)
        {
            (string Name, int Value)[] rates =
            [
                (nameof(policy.PerAddressPerMinute), policy.PerAddressPerMinute),
                (nameof(policy.PerAddressPerHour), policy.PerAddressPerHour),
                (nameof(policy.PerAddressAndFormPerMinute), policy.PerAddressAndFormPerMinute),
                (nameof(policy.PerFormPerMinute), policy.PerFormPerMinute),
                (nameof(policy.SitePerSecond), policy.SitePerSecond),
            ];
            foreach (var (rate, value) in rates)
            {
                if (value is < 0 or > FormBuilderLimitsOptions.HighestRate)
                {
                    failures.Add(string.Create(CultureInfo.InvariantCulture, $"{Root}:Limits:{name}:{rate} ({value}) must be 0 to {FormBuilderLimitsOptions.HighestRate}, where 0 leaves the limit out."));
                }
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

    // Each allowed host is a host name or an IP address, with a port when the host is called on another than its
    // scheme's standard one, each host and port named once (ADR-0076, ADR-0082).
    private static void CheckAllowedHosts(IList<string> hosts, List<string> failures)
    {
        var seen = new Dictionary<OutboundHost, int>();
        for (var i = 0; i < hosts.Count; i++)
        {
            var key = string.Create(CultureInfo.InvariantCulture, $"{Root}:Webhooks:AllowedHosts:{i}");
            if (OutboundHosts.Parse(hosts[i]) is not { } host)
            {
                failures.Add($"{key} ('{hosts[i]}') must be a host name such as 'events.example.com' or an IP address, optionally with a port such as 'events.example.com:8443' (an IPv6 address in brackets: '[2001:db8::1]:8443'), and no scheme, path or wildcard.");
            }
            else if (!seen.TryAdd(host, i))
            {
                failures.Add(string.Create(CultureInfo.InvariantCulture, $"{key} ('{hosts[i]}') names a host and port again; {Root}:Webhooks:AllowedHosts:{seen[host]} names them already. Keep one."));
            }
        }
    }

    // The base path is one the site leaves free, and each allowed origin an origin, named once (ADR-0084).
    private static void CheckPublicApi(FormBuilderPublicApiOptions api, List<string> failures)
    {
        if (!PublicApiRules.IsBasePath(api.BasePath, out var problem))
        {
            failures.Add($"{Root}:PublicApi:BasePath ('{api.BasePath}') {problem}");
        }

        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < api.AllowedOrigins.Count; i++)
        {
            var key = string.Create(CultureInfo.InvariantCulture, $"{Root}:PublicApi:AllowedOrigins:{i}");
            if (PublicApiRules.Origin(api.AllowedOrigins[i]) is not { } origin)
            {
                failures.Add($"{key} ('{api.AllowedOrigins[i]}') must be an origin such as 'https://www.example.com': http or https, a host and a port when it is not the scheme's, and nothing after them, with no '/' at the end.");
            }
            else if (!seen.TryAdd(origin, i))
            {
                failures.Add(string.Create(CultureInfo.InvariantCulture, $"{key} ('{api.AllowedOrigins[i]}') names an origin again; {Root}:PublicApi:AllowedOrigins:{seen[origin]} names it already. Keep one."));
            }
        }

        CheckServerKeys(api.ServerKeys, failures);
    }

    // Each server key a name once and a hash once (ADR-0126); a message names the entry, never its hash.
    private static void CheckServerKeys(IList<ServerKeyOptions> keys, List<string> failures)
    {
        var names = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var hashes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < keys.Count; i++)
        {
            var key = string.Create(CultureInfo.InvariantCulture, $"{Root}:PublicApi:ServerKeys:{i}");
            var entry = keys[i];
            if (!PublicApiRules.IsServerKeyName(entry.Name))
            {
                failures.Add($"{key}:Name must have 1 to 64 letters, digits, hyphens and underscores.");
            }
            else if (!names.TryAdd(entry.Name, i))
            {
                failures.Add(string.Create(CultureInfo.InvariantCulture, $"{key}:Name ('{entry.Name}') names a key again; {Root}:PublicApi:ServerKeys:{names[entry.Name]} has that name already."));
            }

            if (PublicApiRules.ServerKeyHash(entry.Sha256) is null)
            {
                failures.Add($"{key}:Sha256 must be the SHA-256 of the key as 64 hexadecimal digits (scripts/server-key.mjs makes a key and its hash), never the key itself.");
            }
            else if (!hashes.TryAdd(entry.Sha256, i))
            {
                failures.Add(string.Create(CultureInfo.InvariantCulture, $"{key}:Sha256 is the hash of {Root}:PublicApi:ServerKeys:{hashes[entry.Sha256]} again; give each server its own key."));
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
