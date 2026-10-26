using System.Collections.Frozen;
using System.Text.RegularExpressions;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Microsoft.Extensions.Options;
using MergeTagEngine = Jiron.Umbraco.FormBuilder.Logic.MergeTags;

namespace Jiron.Umbraco.FormBuilder.Core.MergeTags;

/// <summary>
/// Every merge-tag source and filter a site has (ADR-0065): the built-in sources first, then the site's own, each by
/// name. A site's source or filter whose name is taken is left out here; the start-up check refuses it, so a running
/// site never has one.
/// </summary>
internal sealed partial class MergeTagRegistry
{
    /// <summary>A source's or filter's name, as the grammar reads it (ADR-0014): a lowercase letter, then letters and digits.</summary>
    [GeneratedRegex("^[a-z][A-Za-z0-9]*$", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();

    /// <summary>The registry of the built-in sources and the site's own.</summary>
    public MergeTagRegistry(IEnumerable<IFormBuilderMergeTagSource> builtIn, FormBuilderMergeTagSourceCollection siteSources, FormBuilderMergeTagFilterCollection siteFilters)
    {
        var sources = new Dictionary<string, IFormBuilderMergeTagSource>(StringComparer.Ordinal);
        foreach (var source in builtIn)
        {
            sources.TryAdd(source.Name, source);
        }

        foreach (var source in siteSources)
        {
            if (Problem("source", source.Name, MergeTagPlaces.BuiltInSources.ContainsKey) is null)
            {
                sources.TryAdd(source.Name, source);
            }
        }

        var filters = new Dictionary<string, IFormBuilderMergeTagFilter>(StringComparer.Ordinal);
        foreach (var filter in siteFilters)
        {
            if (Problem("filter", filter.Name, MergeTagEngine.IsBuiltInFilter) is null)
            {
                filters.TryAdd(filter.Name, filter);
            }
        }

        Sources = sources.ToFrozenDictionary(StringComparer.Ordinal);
        Filters = filters.ToFrozenDictionary(StringComparer.Ordinal);
        Ordered = [.. sources.Values];
    }

    /// <summary>Every source by name.</summary>
    public FrozenDictionary<string, IFormBuilderMergeTagSource> Sources { get; }

    /// <summary>The site's own filters by name.</summary>
    public FrozenDictionary<string, IFormBuilderMergeTagFilter> Filters { get; }

    /// <summary>Every source in the order the picker offers them: the built-in sources first.</summary>
    public IReadOnlyList<IFormBuilderMergeTagSource> Ordered { get; }

    /// <summary>
    /// The problems of a site's sources and filters, which stop the site (ADR-0042): a name outside the grammar, a
    /// built-in one's name, or a name two of them share; each names the type.
    /// </summary>
    public static IReadOnlyList<string> Problems(IEnumerable<IFormBuilderMergeTagSource> sources, IEnumerable<IFormBuilderMergeTagFilter> filters)
    {
        var problems = new List<string>();
        Collect("source", sources.Select(source => ((string?)source.Name, source.GetType())), MergeTagPlaces.BuiltInSources.ContainsKey, problems);
        Collect("filter", filters.Select(filter => ((string?)filter.Name, filter.GetType())), MergeTagEngine.IsBuiltInFilter, problems);
        return problems;
    }

    private static void Collect(string kind, IEnumerable<(string? Name, Type Type)> items, Func<string, bool> builtIn, List<string> problems)
    {
        var seen = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var (name, type) in items)
        {
            if (Problem(kind, name, builtIn) is { } problem)
            {
                problems.Add($"The merge-tag {kind} {type.FullName} {problem}.");
            }
            else if (!seen.TryAdd(name!, type))
            {
                problems.Add($"The merge-tag {kind}s {seen[name!].FullName} and {type.FullName} are both named '{name}'; each needs a name of its own.");
            }
        }
    }

    /// <summary>What is wrong with a site's source's or filter's name, or null when nothing is.</summary>
    private static string? Problem(string kind, string? name, Func<string, bool> builtIn)
    {
        if (name is null || !NamePattern().IsMatch(name))
        {
            return $"is named '{name}', which is not a name merge tags can use: a lowercase letter, then letters and digits";
        }

        return builtIn(name) ? $"is named '{name}', which a built-in {kind} has" : null;
    }
}

/// <summary>
/// Stops the site when a site's merge-tag source or filter cannot be used (ADR-0042, ADR-0065): a name outside the
/// grammar, a built-in one's name, or a name two of them share. It runs with the other start-up checks and names every
/// problem at once.
/// </summary>
/// <param name="sources">The site's sources, which the check creates.</param>
/// <param name="filters">The site's filters, which the check creates.</param>
internal sealed class MergeTagStartupCheck(FormBuilderMergeTagSourceCollection sources, FormBuilderMergeTagFilterCollection filters) : IValidateOptions<FormBuilderStartupChecks>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, FormBuilderStartupChecks options)
    {
        IReadOnlyList<string> problems;
        try
        {
            problems = MergeTagRegistry.Problems([.. sources], [.. filters]);
        }
        catch (InvalidOperationException exception)
        {
            // A source or filter the container cannot create fails as the collection is read.
            problems = [$"Jiron Form Builder cannot create the site's merge-tag sources and filters: {exception.Message}"];
        }

        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }
}
