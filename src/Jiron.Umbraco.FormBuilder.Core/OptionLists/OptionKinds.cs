using System.Collections.Frozen;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization.Metadata;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes.BuiltIn;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;

namespace Jiron.Umbraco.FormBuilder.Core.OptionLists;

/// <summary>What a kind's own check of its configuration reports to (ADR-0073), as a field type's own check does (ADR-0056).</summary>
/// <param name="path">The JSON path of the configuration.</param>
/// <param name="problems">Where the problems go.</param>
internal sealed class OptionKindCheck(string path, DefinitionProblems problems)
{
    /// <summary>Refuses a value of the configuration, at its place: a setting's name, then <c>[index]</c> in a list.</summary>
    public void Invalid(string setting, string message) => problems.Add(DefinitionErrors.SettingInvalid, $"{path}.{setting}", message);
}

/// <summary>What a kind resolves its options from.</summary>
/// <param name="Config">The configuration, which keeps the kind's schema and its own check; null for none.</param>
/// <param name="Items">A library list's items, for a kind whose options they are; empty for any other.</param>
/// <param name="Texts">The dictionary items the items' labels and descriptions name, in the request's culture.</param>
/// <param name="Source">The culture, the parent's answers and the current page.</param>
internal sealed record OptionKindRequest(SettingsObject? Config, IReadOnlyList<FieldOption> Items, DictionaryTexts Texts, FormBuilderOptionRequest Source);

/// <summary>
/// A kind of option list (ADR-0073): its configuration's settings class, read into a schema as a field type's settings
/// are (ADR-0055) and checked with the same codes, its own check and the check of what it names on the site
/// (ADR-0075), whether a field may configure it as its source, whether its options are a list's items, whether a list
/// keeps them for its <c>cacheSeconds</c>, and how it resolves its options.
/// </summary>
internal abstract class OptionKind
{
    /// <summary>A kind whose configuration is <paramref name="settingsType"/>.</summary>
    protected OptionKind(OptionListKind kind, Type settingsType, bool onField, bool hasItems, bool usesCacheSeconds)
    {
        Kind = kind;
        Schema = FieldSettingsReader.Read(settingsType);
        OnField = onField;
        HasItems = hasItems;
        UsesCacheSeconds = usesCacheSeconds;
    }

    /// <summary>The kind.</summary>
    public OptionListKind Kind { get; }

    /// <summary>The kind's name as definitions and rows write it, such as <c>dataType</c>.</summary>
    public string Name => EnumNames<OptionListKind>.ByValue[Kind];

    /// <summary>The configuration's settings, as the backoffice's editors read them.</summary>
    public FieldSettingsSchema Schema { get; }

    /// <summary>Whether a field may take its options from this kind, configured on the field.</summary>
    public bool OnField { get; }

    /// <summary>Whether the options are a library list's items, which the list holds.</summary>
    public bool HasItems { get; }

    /// <summary>
    /// Whether a list of the kind keeps its options for its <c>cacheSeconds</c> (ADR-0075): a kind whose options change
    /// without the site saying so. Any other list keeps nothing for a time, and its <c>cacheSeconds</c> is 0.
    /// </summary>
    public bool UsesCacheSeconds { get; }

    /// <summary>
    /// Holds a configuration, or none, to the kind's schema and then to its own check; whether nothing was found, so the
    /// configuration reads as the kind's settings class.
    /// </summary>
    /// <param name="config">The configuration, or null for none.</param>
    /// <param name="path">Its JSON path.</param>
    /// <param name="owner">Who has it, as the start of a sentence: <c>A countries list</c>.</param>
    /// <param name="problems">Where the problems go.</param>
    public bool Check(SettingsObject? config, string path, string owner, DefinitionProblems problems)
    {
        ArgumentNullException.ThrowIfNull(problems);
        if (!FieldSettingsChecks.Check(Schema, config, path, owner, problems))
        {
            return false;
        }

        var found = problems.Found;
        CheckSettings(config, new OptionKindCheck(path, problems));
        return problems.Found == found;
    }

    /// <summary>
    /// <see cref="Check"/>, then what the configuration names on the site (ADR-0075): pages, document types and data
    /// types, which change on their own, so this runs when a list is saved and a form is published, never when a stored
    /// list is read; the site only for a configuration that passed <see cref="Check"/>.
    /// </summary>
    /// <param name="config">The configuration, or null for none.</param>
    /// <param name="path">Its JSON path.</param>
    /// <param name="owner">Who has it, as the start of a sentence: <c>A content list</c>.</param>
    /// <param name="problems">Where the problems go.</param>
    /// <param name="cancellationToken">Cancelled when the save or the publish is.</param>
    public async ValueTask CheckOnSiteAsync(SettingsObject? config, string path, string owner, DefinitionProblems problems, CancellationToken cancellationToken)
    {
        if (Check(config, path, owner, problems))
        {
            await CheckReferencesAsync(config, new OptionKindCheck(path, problems), cancellationToken);
        }
    }

    /// <summary>The options, in order, before the service cleans them.</summary>
    /// <param name="request">The configuration, which the kind's check accepted, the items and the request.</param>
    /// <param name="cancellationToken">Cancelled when the page or the submission is.</param>
    public abstract ValueTask<IReadOnlyList<FormBuilderOption>> ResolveAsync(OptionKindRequest request, CancellationToken cancellationToken);

    /// <summary>The kind's own check of a configuration that keeps its schema.</summary>
    protected abstract void CheckSettings(SettingsObject? config, OptionKindCheck check);

    /// <summary>The check of what a configuration that the kind's own check accepted names on the site.</summary>
    protected abstract ValueTask CheckReferencesAsync(SettingsObject? config, OptionKindCheck check, CancellationToken cancellationToken);
}

/// <summary>A kind whose configuration reads as <typeparamref name="TSettings"/>.</summary>
/// <typeparam name="TSettings">The settings class.</typeparam>
internal abstract class OptionKind<TSettings> : OptionKind
    where TSettings : class, new()
{
    private readonly JsonTypeInfo<TSettings> _info;

    /// <summary>A kind of the settings class.</summary>
    protected OptionKind(OptionListKind kind, bool onField, bool hasItems = false, bool usesCacheSeconds = false)
        : base(kind, typeof(TSettings), onField, hasItems, usesCacheSeconds) =>
        _info = (JsonTypeInfo<TSettings>)FieldSettingsJson.Options.GetTypeInfo(typeof(TSettings));

    /// <summary>A configuration as the settings class; a new instance, with its defaults, for none.</summary>
    public TSettings Read(SettingsObject? config) => config?.Bind(_info) ?? new TSettings();

    /// <inheritdoc />
    public sealed override ValueTask<IReadOnlyList<FormBuilderOption>> ResolveAsync(OptionKindRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return ResolveAsync(Read(request.Config), request, cancellationToken);
    }

    /// <summary>The options of a configuration read as the settings class.</summary>
    protected abstract ValueTask<IReadOnlyList<FormBuilderOption>> ResolveAsync(TSettings settings, OptionKindRequest request, CancellationToken cancellationToken);

    /// <inheritdoc />
    protected sealed override void CheckSettings(SettingsObject? config, OptionKindCheck check) => CheckSettings(Read(config), check);

    /// <inheritdoc />
    protected sealed override ValueTask CheckReferencesAsync(SettingsObject? config, OptionKindCheck check, CancellationToken cancellationToken) =>
        CheckReferencesAsync(Read(config), check, cancellationToken);

    /// <summary>The kind's own check of the settings; it finds nothing unless a kind overrides it.</summary>
    protected virtual void CheckSettings(TSettings settings, OptionKindCheck check)
    {
    }

    /// <summary>The check of what the settings name on the site; it finds nothing unless a kind overrides it.</summary>
    protected virtual ValueTask CheckReferencesAsync(TSettings settings, OptionKindCheck check, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}

/// <summary>The kinds this version has, by kind (ADR-0073, ADR-0075); 5.5c adds the URL and file kinds.</summary>
internal sealed class OptionKindRegistry
{
    private readonly FrozenDictionary<OptionListKind, OptionKind> _kinds;

    /// <summary>The kinds over the site's option sources, content and data types.</summary>
    public OptionKindRegistry(OptionSourceRegistry sources, OptionListUmbraco umbraco)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(umbraco);
        OptionKind[] kinds = [new ManualOptionKind(), new ContentOptionKind(umbraco), new DataTypeOptionKind(umbraco), new CountriesOptionKind(), new CodeOptionKind(sources)];
        _kinds = kinds.ToFrozenDictionary(kind => kind.Kind);
    }

    /// <summary>Every kind, in the order of the enum.</summary>
    public IEnumerable<OptionKind> All => _kinds.Values.OrderBy(kind => kind.Kind);

    /// <summary>The kind, or null when this version does not have it yet.</summary>
    public OptionKind? Find(OptionListKind kind) => _kinds.GetValueOrDefault(kind);
}

/// <summary>A manual list (ADR-0073): options an editor types or pastes, held as the list's items, with no configuration.</summary>
internal sealed class ManualOptionKind() : OptionKind<ManualOptionListSettings>(OptionListKind.Manual, onField: false, hasItems: true)
{
    /// <inheritdoc />
    protected override ValueTask<IReadOnlyList<FormBuilderOption>> ResolveAsync(ManualOptionListSettings settings, OptionKindRequest request, CancellationToken cancellationToken) =>
        ValueTask.FromResult(OptionItems.ToOptions(request.Items, request.Texts, request.Source.Culture));
}

/// <summary>
/// The countries (ADR-0073): every country of ISO 3166, or those the configuration names, each by its two-letter code
/// with its name in the culture's language from .NET's culture data, in the order of the names in that culture.
/// </summary>
internal sealed class CountriesOptionKind() : OptionKind<CountriesOptionListSettings>(OptionListKind.Countries, onField: true)
{
    /// <inheritdoc />
    /// <remarks>A country named twice is the settings checks' to refuse, as every list setting holds each value once.</remarks>
    protected override void CheckSettings(CountriesOptionListSettings settings, OptionKindCheck check)
    {
        for (var i = 0; i < settings.Countries.Count; i++)
        {
            var code = settings.Countries[i];
            if (!BuiltInChecks.CountryCodes.Contains(code))
            {
                check.Invalid(BuiltInChecks.At("countries", i), $"A country is its two-letter code in capitals, such as CH, not '{code}'.");
            }
        }
    }

    /// <inheritdoc />
    protected override ValueTask<IReadOnlyList<FormBuilderOption>> ResolveAsync(CountriesOptionListSettings settings, OptionKindRequest request, CancellationToken cancellationToken)
    {
        var culture = request.Source.Culture;
        var names = StringComparer.Create(MergeTagCultures.Culture(culture), ignoreCase: false);
        IEnumerable<string> codes = settings.Countries.Count > 0 ? settings.Countries : BuiltInChecks.CountryCodes;
        IReadOnlyList<FormBuilderOption> options =
        [
            .. codes
                .Select(code => new FormBuilderOption(code, CountryNames.Name(code, culture)))
                .OrderBy(option => option.Label, names)
                .ThenBy(option => option.Value, StringComparer.Ordinal),
        ];
        return ValueTask.FromResult(options);
    }
}

/// <summary>Options from a source registered in code (ADR-0073), the standard lists among them, named by its name.</summary>
/// <param name="sources">The site's option sources.</param>
internal sealed class CodeOptionKind(OptionSourceRegistry sources) : OptionKind<CodeOptionListSettings>(OptionListKind.Code, onField: true, usesCacheSeconds: true)
{
    /// <inheritdoc />
    protected override void CheckSettings(CodeOptionListSettings settings, OptionKindCheck check)
    {
        if (settings.Source is { } source && sources.Find(source) is null)
        {
            check.Invalid("source", $"The site has no option source '{source}'.");
        }
    }

    /// <inheritdoc />
    protected override ValueTask<IReadOnlyList<FormBuilderOption>> ResolveAsync(CodeOptionListSettings settings, OptionKindRequest request, CancellationToken cancellationToken) =>
        settings.Source is { } name && sources.Find(name) is { } source
            ? source.GetOptionsAsync(request.Source, cancellationToken)
            : throw new InvalidOperationException("The source is gone.");
}

/// <summary>A manual list's configuration (ADR-0073): none, as its options are its items.</summary>
public sealed class ManualOptionListSettings;

/// <summary>The configuration of the countries (ADR-0073), each country as its ISO 3166 alpha-2 code.</summary>
public sealed class CountriesOptionListSettings
{
    /// <summary>The countries offered; every country by default.</summary>
    [FormBuilderSetting("#jfbOptionListSettings_countries", Description = "#jfbOptionListSettings_countriesHelp")]
    public IReadOnlyList<string> Countries { get; init; } = [];
}

/// <summary>The configuration of options from code (ADR-0073).</summary>
public sealed class CodeOptionListSettings
{
    /// <summary>The name of the option source, such as <c>months</c>.</summary>
    [Required]
    [FormBuilderSetting("#jfbOptionListSettings_source", Description = "#jfbOptionListSettings_sourceHelp")]
    public string? Source { get; init; }
}

/// <summary>Options written as a field's own (ADR-0002), turned into the options of one culture.</summary>
internal static class OptionItems
{
    /// <summary>The items' options, each label and description in the culture with its dictionary item's text.</summary>
    public static IReadOnlyList<FormBuilderOption> ToOptions(IReadOnlyList<FieldOption> items, DictionaryTexts texts, string culture) =>
    [
        .. items.Select(item => new FormBuilderOption(item.Value, item.Label is null ? null : texts.Resolve(item.Label, culture))
        {
            Description = item.Description is null ? null : texts.Resolve(item.Description, culture),
            Image = item.Image,
            Places = item.Places,
            ParentValues = item.ParentValues,
        }),
    ];

    /// <summary>The dictionary keys the items' labels and descriptions name in a culture, for one read of the dictionary.</summary>
    public static IReadOnlyCollection<string> DictionaryKeys(IReadOnlyList<FieldOption> items, string culture)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var text in items.SelectMany(item => new LocalizedString?[] { item.Label, item.Description }).OfType<LocalizedString>())
        {
            if (LocalizedString.TryGetDictionaryKey(text.Resolve(culture), out var key))
            {
                keys.Add(key);
            }
        }

        return keys;
    }
}
