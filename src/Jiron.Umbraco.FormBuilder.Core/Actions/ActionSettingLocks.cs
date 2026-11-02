using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Serialization;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Microsoft.Extensions.Options;

namespace Jiron.Umbraco.FormBuilder.Core.Actions;

/// <summary>
/// The host's designer locks of action settings (<c>JironFormBuilder:Designer:SettingLocks</c> of kind <c>action</c>,
/// ADR-0060, ADR-0158), resolved against the site's action types, as <see cref="FieldSettingLocks"/> does for fields. The
/// site runs an action with the locks applied, and a draft save refuses a locked setting that an editor gives another
/// value. Start-up refuses locks that do not fit the action types (<see cref="ActionSettingLockStartupCheck"/>).
/// </summary>
internal sealed class ActionSettingLocks
{
    private const string Root = FormBuilderOptions.SectionName + ":Designer:SettingLocks";

    private readonly FrozenDictionary<string, ImmutableArray<FieldSettingLock>> _byType;

    /// <summary>The site's locks of action settings.</summary>
    /// <exception cref="InvalidOperationException">A lock does not fit the action types; the message lists every problem.</exception>
    public ActionSettingLocks(IOptions<FormBuilderOptions> options, ActionTypeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(options);
        var problems = new List<string>();
        var resolved = Resolve(options.Value.Designer.SettingLocks, registry, problems);
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, problems));
        }

        _byType = resolved.GroupBy(each => each.Type, StringComparer.Ordinal).ToFrozenDictionary(group => group.Key, group => group.ToImmutableArray(), StringComparer.Ordinal);
    }

    /// <summary>
    /// What keeps the locks from fitting the site's action types, each naming the lock's key and never its value. Locks of
    /// another shape are the options' own validation's (ADR-0042), and those of kind <c>field</c> have a check of their own.
    /// </summary>
    public static IReadOnlyList<string> Problems(IList<DesignerSettingLock> locks, ActionTypeRegistry registry)
    {
        var problems = new List<string>();
        Resolve(locks, registry, problems);
        return problems;
    }

    /// <summary>The locks of an action type, in the configuration's order; none for a type without any.</summary>
    public ImmutableArray<FieldSettingLock> Of(string type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return _byType.TryGetValue(type, out var locks) ? locks : [];
    }

    /// <summary>The action as the site runs it: with the locked values. The same instance when no lock changes it.</summary>
    public FormAction Apply(FormAction action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var settings = FieldSettingLocks.Applied(action.Settings, Of(action.Type));
        return ReferenceEquals(settings, action.Settings) ? action : action with { Settings = settings };
    }

    /// <summary>The definition with every action as the site runs it. The same instance when no lock changes an action.</summary>
    public FormDefinition Apply(FormDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (_byType.Count == 0)
        {
            return definition;
        }

        var actions = definition.Actions.Select(Apply).ToList();
        return actions.SequenceEqual(definition.Actions, ReferenceEqualityComparer.Instance) ? definition : definition with { Actions = actions };
    }

    /// <summary>
    /// Refuses a draft that gives a locked setting a value of its own: one that is neither the lock's (the setting's
    /// default, for a lock without a value) nor the one the stored draft holds for the action with the same key and type,
    /// which a form keeps from before the lock came. A setting the action leaves out is always fine. The first such setting
    /// in document order is named.
    /// </summary>
    /// <param name="definition">The draft to save.</param>
    /// <param name="stored">The stored draft the save replaces, or null when there is none to read.</param>
    /// <exception cref="FormBuilderSettingLockedException">A locked setting has another value (<c>setting_locked</c>).</exception>
    public void CheckSave(FormDefinition definition, FormDefinition? stored)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (_byType.Count == 0)
        {
            return;
        }

        var before = stored?.Actions.GroupBy(action => action.Key).ToDictionary(group => group.Key, group => group.First()) ?? [];
        var actions = JsonPaths.Property(JsonPaths.Root, "actions");
        for (var i = 0; i < definition.Actions.Count; i++)
        {
            var action = definition.Actions[i];
            var earlier = before.GetValueOrDefault(action.Key);
            var kept = string.Equals(earlier?.Type, action.Type, StringComparison.Ordinal) ? earlier?.Settings : null;
            if (FieldSettingLocks.FirstRefused(action.Settings, kept, Of(action.Type)) is { } settingLock)
            {
                throw new FormBuilderSettingLockedException(JsonPaths.Property(JsonPaths.Property(JsonPaths.Index(actions, i), "settings"), settingLock.Setting.Name), settingLock.ConfigurationKey);
            }
        }
    }

    private static List<FieldSettingLock> Resolve(IList<DesignerSettingLock> locks, ActionTypeRegistry registry, List<string> problems)
    {
        ArgumentNullException.ThrowIfNull(locks);
        ArgumentNullException.ThrowIfNull(registry);
        var resolved = new List<FieldSettingLock>();
        var seen = new HashSet<(string Type, string Setting)>();
        for (var i = 0; i < locks.Count; i++)
        {
            // The options' own validation refuses a lock of another shape and a second lock of one setting (ADR-0042).
            if (locks[i] is not { } settingLock
                || !FieldSettingLocks.IsWellFormed(settingLock, DesignerSettingLock.ActionKind)
                || !seen.Add((settingLock.Type!, settingLock.Setting!)))
            {
                continue;
            }

            var key = string.Create(CultureInfo.InvariantCulture, $"{Root}:{i}");
            var alias = settingLock.Type!;
            if (registry.Find(alias) is not { } type)
            {
                var aliases = registry.Types.Select(each => each.Declaration.Alias).ToList();
                problems.Add(aliases.Find(each => string.Equals(each, alias, StringComparison.OrdinalIgnoreCase)) is { } spelt
                    ? $"{key}:Type ('{alias}') is not an action type of the site; aliases are compared exactly, and the site has '{spelt}'."
                    : aliases.Count == 0
                        ? $"{key}:Type ('{alias}') is not an action type of the site, which has none."
                        : $"{key}:Type ('{alias}') is not an action type of the site; its action types are {string.Join(", ", aliases)}.");
                continue;
            }

            if (FieldSettingLocks.ResolveSetting(settingLock, key, "action type", "an action", type.SettingsSchema, problems) is { } found)
            {
                resolved.Add(found);
            }
        }

        return resolved;
    }
}

/// <summary>
/// Stops the site when a designer lock of an action setting does not fit its action types (ADR-0060, ADR-0158). Each message
/// names the lock's key and never its value. Options that their own validation refuses, and action types that cannot be
/// a registry, are left to those checks.
/// </summary>
/// <param name="host">The host configuration with the locks.</param>
/// <param name="types">The registered action types, which the check creates.</param>
internal sealed class ActionSettingLockStartupCheck(IOptions<FormBuilderOptions> host, FormBuilderActionTypeCollection types) : IValidateOptions<FormBuilderStartupChecks>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, FormBuilderStartupChecks options)
    {
        IList<DesignerSettingLock> locks;
        ActionTypeRegistry registry;
        try
        {
            locks = host.Value.Designer.SettingLocks;
            registry = new ActionTypeRegistry(types);
        }
        catch (Exception exception) when (exception is OptionsValidationException or InvalidOperationException)
        {
            return ValidateOptionsResult.Success;
        }

        var problems = ActionSettingLocks.Problems(locks, registry);
        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }
}
