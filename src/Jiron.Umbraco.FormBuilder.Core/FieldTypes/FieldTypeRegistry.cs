using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Jiron.Umbraco.FormBuilder.Core.Configuration;

namespace Jiron.Umbraco.FormBuilder.Core.FieldTypes;

/// <summary>
/// The field types a site has, by alias (ADR-0055): the registered collection, checked once, with each type's traits
/// for the structural validation, the public projection and the field catalogue (ADR-0035, ADR-0044). Start-up refuses
/// a collection with problems (<see cref="FieldTypeStartupCheck"/>), so a running site always has a valid registry.
/// </summary>
internal sealed partial class FieldTypeRegistry
{
    private const FieldFeatures WithoutValue = FieldFeatures.VisibilityRules;

    private static readonly FieldFeatures EveryFeature = Enum.GetValues<FieldFeatures>().Aggregate(FieldFeatures.None, (all, feature) => all | feature);

    private static readonly FieldMessageKinds EveryMessage = Enum.GetValues<FieldMessageKinds>().Aggregate(FieldMessageKinds.None, (all, kind) => all | kind);

    private readonly FrozenDictionary<string, (IFormBuilderFieldType Type, FieldTypeTraits Traits)> _types;

    /// <summary>A registry of the given types, in the order the palette shows them.</summary>
    /// <exception cref="InvalidOperationException">The types have problems; the message lists every one.</exception>
    public FieldTypeRegistry(IEnumerable<IFormBuilderFieldType> types)
    {
        ArgumentNullException.ThrowIfNull(types);
        var list = types.ToList();
        if (Problems(list) is { Count: > 0 } problems)
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, problems));
        }

        Types = [.. list];
        All = [.. list.Select(type => new FieldTypeTraits(type))];
        _types = Types.Zip(All).ToFrozenDictionary(pair => pair.First.Declaration.Alias, pair => (pair.First, pair.Second), StringComparer.Ordinal);
    }

    /// <summary>Every type, in the order the palette shows them.</summary>
    public ImmutableArray<IFormBuilderFieldType> Types { get; }

    /// <summary>Every type's traits, in the order the palette shows them.</summary>
    public ImmutableArray<FieldTypeTraits> All { get; }

    /// <summary>The traits of the type with this alias, or null.</summary>
    public FieldTypeTraits? Find(string alias) => _types.TryGetValue(alias, out var type) ? type.Traits : null;

    /// <summary>The type with this alias, or null.</summary>
    public IFormBuilderFieldType? FindType(string alias) => _types.TryGetValue(alias, out var type) ? type.Type : null;

    /// <summary>
    /// What keeps the types from being a site's field types, each naming the type: an alias outside the pattern or taken
    /// by another type, and a declaration that contradicts itself or its settings. Empty when there is nothing.
    /// </summary>
    public static IReadOnlyList<string> Problems(IReadOnlyList<IFormBuilderFieldType> types)
    {
        ArgumentNullException.ThrowIfNull(types);
        var problems = new List<string>();
        // A field names its type exactly, but two aliases that differ only in letter case read as one in the palette and
        // in a definition, so the second is refused as the first's.
        var aliases = new Dictionary<string, IFormBuilderFieldType>(StringComparer.OrdinalIgnoreCase);
        foreach (var type in types)
        {
            if (type?.Declaration is not { } declaration)
            {
                problems.Add($"The field type {NameOf(type)} declares nothing: give it a declaration.");
                continue;
            }

            var name = $"The field type '{declaration.Alias}' ({NameOf(type)})";
            if (!FormBuilderOptionsValidator.IsTypeAlias(declaration.Alias))
            {
                problems.Add($"{name} has an alias outside the pattern: a letter, then letters, digits, '.', '-' and '_', at most 64 characters.");
            }
            else if (!aliases.TryAdd(declaration.Alias, type))
            {
                var first = aliases[declaration.Alias];
                var taken = string.Equals(first.Declaration.Alias, declaration.Alias, StringComparison.Ordinal)
                    ? "the alias"
                    : $"the alias, in another letter case ('{first.Declaration.Alias}'),";
                problems.Add($"{name} has {taken} of the field type {NameOf(first)}. Keep one, or replace the first with "
                    + $"builder.JironFormBuilder().FieldTypes().Replace<{first.GetType().Name}, {type.GetType().Name}>().");
            }

            problems.AddRange(Contradictions(declaration, type.SettingsSchema).Select(problem => $"{name} {problem}"));
        }

        return problems;
    }

    // What a declaration says against itself or its settings, each as the end of a sentence about the type.
    private static IEnumerable<string> Contradictions(FieldTypeDeclaration declaration, FieldSettingsSchema? schema)
    {
        if (schema is null)
        {
            yield return "has no settings schema; a type without settings has an empty one.";
            yield break;
        }

        if (string.IsNullOrWhiteSpace(declaration.Name))
        {
            yield return "has no name for the palette.";
        }

        if (declaration.Icon is null || !IconPattern().IsMatch(declaration.Icon))
        {
            yield return $"has the icon '{declaration.Icon}'; an icon is one of Umbraco's backoffice icons, such as 'icon-message'.";
        }

        if (!Enum.IsDefined(declaration.PaletteGroup) || !Enum.IsDefined(declaration.Storage) || declaration.RuleKind is { } kind && !Enum.IsDefined(kind))
        {
            yield return "names a palette group, a storage kind or a rule kind that does not exist.";
        }

        if ((declaration.Features & ~EveryFeature) != 0 || (declaration.Messages & ~EveryMessage) != 0)
        {
            yield return "names a feature or an error kind that does not exist.";
        }

        var features = declaration.Features;
        if (!declaration.HoldsValue)
        {
            if ((features & ~WithoutValue) != 0 || declaration.Storage != FieldStorageKind.None || declaration.RuleKind is not null
                || declaration.Messages != FieldMessageKinds.None || declaration.MultipleSetting || declaration.ServerOnlyDefault)
            {
                yield return "holds no value, so it has no storage, rule kind, error kinds, multiple setting or server-only default, and supports nothing but show and hide rules.";
            }

            yield break;
        }

        if ((features & FieldFeatures.Label) == 0)
        {
            yield return "holds a value, so it supports a label.";
        }

        var hasParts = (features & (FieldFeatures.Rows | FieldFeatures.ChildFields)) != 0;
        if (hasParts != (declaration.Storage == FieldStorageKind.None))
        {
            yield return "has storage None exactly when its parts hold the values: rows or child fields.";
        }

        if ((features & FieldFeatures.Rows) != 0 && (features & FieldFeatures.ChildFields) != 0)
        {
            yield return "has rows and child fields; a type has one or the other.";
        }

        if ((features & (FieldFeatures.NeedsOptions | FieldFeatures.Places)) != 0 && (features & FieldFeatures.Options) == 0)
        {
            yield return "needs options or has places on them, so it supports options.";
        }

        if (((features & FieldFeatures.Required) != 0) != ((declaration.Messages & FieldMessageKinds.Required) != 0))
        {
            yield return "raises the 'required' error exactly when it supports being required.";
        }

        if ((features & (FieldFeatures.MatchesFieldCheck | FieldFeatures.UniquePerFormCheck)) != 0 && declaration.RuleKind is null)
        {
            yield return "supports a matchesField or uniquePerForm check, which compare answers, so it has a rule kind.";
        }

        if (declaration.ServerOnlyDefault && (features & FieldFeatures.DefaultValue) == 0)
        {
            yield return "keeps its default value on the server, so it supports a default value.";
        }

        if (declaration.MultipleSetting)
        {
            if (declaration.RuleKind != Logic.FieldKind.Choice && (features & FieldFeatures.Rows) == 0)
            {
                yield return "has a multiple setting, which turns one choice into several, so it compares as a choice or has rows.";
            }

            if (schema.Find("multiple") is not { Kind: FieldSettingKind.Boolean })
            {
                yield return "has a multiple setting, so its settings have the boolean 'multiple'.";
            }
        }
    }

    private static string NameOf(IFormBuilderFieldType? type) => type?.GetType().FullName ?? "(null)";

    [GeneratedRegex("^icon-[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex IconPattern();
}
