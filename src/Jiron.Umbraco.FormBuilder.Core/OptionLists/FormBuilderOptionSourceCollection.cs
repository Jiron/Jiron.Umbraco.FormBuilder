using System.Collections.Frozen;
using System.Text.RegularExpressions;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Composing;

namespace Jiron.Umbraco.FormBuilder.Core.OptionLists;

/// <summary>The option sources a site has (ADR-0073), in the order the backoffice offers them.</summary>
public sealed class FormBuilderOptionSourceCollection : BuilderCollectionBase<IFormBuilderOptionSource>
{
    /// <summary>A collection of the sources the builder creates.</summary>
    public FormBuilderOptionSourceCollection(Func<IEnumerable<IFormBuilderOptionSource>> items)
        : base(items)
    {
    }
}

/// <summary>
/// The ordered collection of option sources (ADR-0073), reached through <c>builder.JironFormBuilder().OptionSources()</c>.
/// It holds the standard lists from the moment it is made, so a composer adds, replaces or removes a source whether it
/// runs before Jiron Form Builder's own composer or after it, for example <c>.Append&lt;ProductSource&gt;()</c> or
/// <c>.Remove&lt;SatisfactionOptionSource&gt;()</c>. Each source is a singleton, created by the container.
/// </summary>
public sealed class FormBuilderOptionSourceCollectionBuilder
    : OrderedCollectionBuilderBase<FormBuilderOptionSourceCollectionBuilder, FormBuilderOptionSourceCollection, IFormBuilderOptionSource>
{
    /// <summary>A builder that holds the standard lists.</summary>
    public FormBuilderOptionSourceCollectionBuilder() => Append(StandardOptionSources.All);

    /// <inheritdoc />
    protected override FormBuilderOptionSourceCollectionBuilder This => this;
}

/// <summary>
/// The site's option sources by name (ADR-0073). A source whose name is taken or outside the grammar is left out
/// here; the start-up check refuses it, so a running site never has one.
/// </summary>
internal sealed partial class OptionSourceRegistry
{
    private readonly FrozenDictionary<string, IFormBuilderOptionSource> _sources;

    /// <summary>The registry of the site's sources.</summary>
    public OptionSourceRegistry(FormBuilderOptionSourceCollection sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var byName = new Dictionary<string, IFormBuilderOptionSource>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            if (IsName(source.Name))
            {
                byName.TryAdd(source.Name, source);
            }
        }

        _sources = byName.ToFrozenDictionary(StringComparer.Ordinal);
        Ordered = [.. sources.Where(source => _sources.TryGetValue(source.Name ?? string.Empty, out var kept) && ReferenceEquals(kept, source))];
    }

    /// <summary>Every source, in the order the collection has them.</summary>
    public IReadOnlyList<IFormBuilderOptionSource> Ordered { get; }

    /// <summary>The source with this name, exactly as written, or null.</summary>
    public IFormBuilderOptionSource? Find(string name) => name is null ? null : _sources.GetValueOrDefault(name);

    /// <summary>
    /// The problems of a site's sources, which stop the site (ADR-0042): a name outside the grammar, or one that two
    /// sources share; each names the source's type.
    /// </summary>
    public static IReadOnlyList<string> Problems(IEnumerable<IFormBuilderOptionSource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var problems = new List<string>();
        var seen = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            var type = source.GetType();
            var name = source.Name;
            if (!IsName(name))
            {
                problems.Add($"The option source {type.FullName} has the name '{name}': a name is a lowercase ASCII letter, then up to 63 ASCII letters and digits.");
            }
            else if (!seen.TryAdd(name, type))
            {
                problems.Add($"The option source {type.FullName} has the name '{name}', which {seen[name].FullName} has already.");
            }
        }

        return problems;
    }

    private static bool IsName(string? name) => name is not null && NamePattern().IsMatch(name);

    [GeneratedRegex("^[a-z][A-Za-z0-9]{0,63}$", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();
}

/// <summary>
/// Stops the site when its option sources cannot be told apart (ADR-0042, ADR-0073): a name outside the grammar or
/// one that two sources share. It runs with the other start-up checks and names every problem at once.
/// </summary>
/// <param name="sources">The registered sources, which the check creates.</param>
internal sealed class OptionSourceStartupCheck(FormBuilderOptionSourceCollection sources) : IValidateOptions<FormBuilderStartupChecks>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, FormBuilderStartupChecks options)
    {
        var problems = OptionSourceRegistry.Problems(sources);
        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }
}
