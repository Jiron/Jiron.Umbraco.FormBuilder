using System.Collections.Frozen;
using System.Collections.Immutable;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Composing;

namespace Jiron.Umbraco.FormBuilder.Core.Actions;

/// <summary>The action types a site registered, in the order the designer shows them (ADR-0158).</summary>
public sealed class FormBuilderActionTypeCollection : BuilderCollectionBase<IFormBuilderActionType>
{
    /// <summary>A collection of the types the builder creates.</summary>
    public FormBuilderActionTypeCollection(Func<IEnumerable<IFormBuilderActionType>> items)
        : base(items)
    {
    }
}

/// <summary>
/// The ordered collection of action types (ADR-0158), reached through <c>builder.JironFormBuilder().ActionTypes()</c>, for
/// example <c>.Append&lt;CrmActionType&gt;()</c>. Each type is a singleton, created by the container.
/// </summary>
public sealed class FormBuilderActionTypeCollectionBuilder
    : OrderedCollectionBuilderBase<FormBuilderActionTypeCollectionBuilder, FormBuilderActionTypeCollection, IFormBuilderActionType>
{
    /// <inheritdoc />
    protected override FormBuilderActionTypeCollectionBuilder This => this;
}

/// <summary>
/// The action types a site has, by alias (ADR-0158): the registered collection, checked once. Start-up refuses a
/// collection with problems (<see cref="ActionTypeStartupCheck"/>), so a running site always has a valid registry.
/// </summary>
internal sealed class ActionTypeRegistry
{
    /// <summary>The longest an attempt of any type takes (ADR-0007).</summary>
    public static readonly TimeSpan LongestAttempt = TimeSpan.FromMinutes(1);

    private readonly FrozenDictionary<string, IFormBuilderActionType> _types;

    /// <summary>A registry of the given types, in the order the designer shows them.</summary>
    /// <exception cref="InvalidOperationException">The types have problems; the message lists every one.</exception>
    public ActionTypeRegistry(IEnumerable<IFormBuilderActionType> types)
    {
        ArgumentNullException.ThrowIfNull(types);
        var list = types.ToList();
        if (Problems(list) is { Count: > 0 } problems)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, problems));
        }

        Types = [.. list];
        _types = list.ToFrozenDictionary(type => type.Declaration.Alias, StringComparer.Ordinal);
    }

    /// <summary>Every type, in the order the designer shows them.</summary>
    public ImmutableArray<IFormBuilderActionType> Types { get; }

    /// <summary>The type with this alias, or null.</summary>
    public IFormBuilderActionType? Find(string alias) => _types.GetValueOrDefault(alias);

    /// <summary>
    /// What keeps the types from being a site's action types, each naming the type: an alias outside the pattern or taken by
    /// another type, a declaration without a name or an icon, a time limit beyond a minute, and a setting visitors would see.
    /// Empty when there is nothing.
    /// </summary>
    public static IReadOnlyList<string> Problems(IReadOnlyList<IFormBuilderActionType> types)
    {
        ArgumentNullException.ThrowIfNull(types);
        var problems = new List<string>();

        // An action names its type exactly, but two aliases that differ only in letter case read as one in the designer
        // and in a definition, so the second is refused as the first's.
        var aliases = new Dictionary<string, IFormBuilderActionType>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in types)
        {
            if (type?.Declaration is not { } declaration)
            {
                problems.Add($"The action type {NameOf(type)} declares nothing: give it a declaration.");
                continue;
            }

            var name = $"The action type '{declaration.Alias}' ({NameOf(type)})";
            if (!FormBuilderOptionsValidator.IsTypeAlias(declaration.Alias))
            {
                problems.Add($"{name} has an alias outside the pattern: a letter, then letters, digits, '.', '-' and '_', at most 64 characters.");
            }
            else if (!aliases.TryAdd(declaration.Alias, type))
            {
                problems.Add($"{name} has the alias of the action type {NameOf(aliases[declaration.Alias])}, in letter case or exactly. Keep one, or replace the first with "
                    + $"builder.JironFormBuilder().ActionTypes().Replace<{aliases[declaration.Alias].GetType().Name}, {type.GetType().Name}>().");
            }

            if (string.IsNullOrWhiteSpace(declaration.Name))
            {
                problems.Add($"{name} has no name for the designer.");
            }

            if (string.IsNullOrWhiteSpace(declaration.Icon))
            {
                problems.Add($"{name} has no icon for the designer.");
            }

            if (declaration.AttemptTimeout is { } limit && (limit <= TimeSpan.Zero || limit > LongestAttempt))
            {
                problems.Add($"{name} gives an attempt {limit}: more than nothing, and at most a minute.");
            }

            if (type.SettingsSchema is not { } schema)
            {
                problems.Add($"{name} has no settings schema; a type without settings has an empty one.");
            }
            else if (schema.Settings.FirstOrDefault(setting => setting.IsPublic) is { } shown)
            {
                problems.Add($"{name} marks its setting '{shown.Name}' as one visitors see, but no visitor sees an action.");
            }
        }

        return problems;
    }

    private static string NameOf(IFormBuilderActionType? type) => type?.GetType().FullName ?? "null";
}

/// <summary>
/// Stops the site when its action types cannot be a registry (ADR-0158), with what keeps them from it. It runs with the
/// other start-up checks, before the site serves anything.
/// </summary>
/// <param name="types">The registered action types, which the check creates.</param>
internal sealed class ActionTypeStartupCheck(FormBuilderActionTypeCollection types) : IValidateOptions<FormBuilderStartupChecks>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, FormBuilderStartupChecks options)
    {
        IReadOnlyList<string> problems;
        try
        {
            problems = ActionTypeRegistry.Problems([.. types]);
        }
        catch (OptionsValidationException)
        {
            // A type that needs the host configuration while it is created meets a refused one: that one's own check names the setting.
            return ValidateOptionsResult.Skip;
        }
        catch (InvalidOperationException exception)
        {
            // A type whose settings class cannot be read fails as it is created, and so does one the container cannot create.
            problems = [$"Jiron Form Builder cannot create its action types: {exception.Message}"];
        }

        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }
}
