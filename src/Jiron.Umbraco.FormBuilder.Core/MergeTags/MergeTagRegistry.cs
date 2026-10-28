using System.Collections.Frozen;
using System.Text.RegularExpressions;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Microsoft.Extensions.Options;
using MergeTagEngine = Jiron.Umbraco.FormBuilder.Logic.MergeTags;

namespace Jiron.Umbraco.FormBuilder.Core.MergeTags;

/// <summary>
/// Every merge-tag source and filter a site has (ADR-0065): the built-in sources first, then the site's own, each by
/// name, as <see cref="SiteMergeTagExtensions"/> read them once, so no rendering asks a site's source for its name or
/// traits again. A site's source or filter with a problem is left out: one whose name is outside the grammar, a
/// built-in one's or an earlier one's, one that fails to say its name or traits, and every one of a collection the
/// container cannot create; the start-up check refuses each, so a running site never has one.
/// </summary>
internal sealed class MergeTagRegistry
{
    /// <summary>The registry of the built-in sources and the site's own.</summary>
    public MergeTagRegistry(IEnumerable<IFormBuilderMergeTagSource> builtIn, SiteMergeTagExtensions site)
    {
        ArgumentNullException.ThrowIfNull(site);
        var sources = new Dictionary<string, IFormBuilderMergeTagSource>(StringComparer.Ordinal);
        foreach (var source in builtIn)
        {
            sources.TryAdd(source.Name, source);
        }

        var traits = new Dictionary<string, MergeTagSourceTraits>(StringComparer.Ordinal);
        foreach (var found in site.Sources)
        {
            if (sources.TryAdd(found.Name, found.Source))
            {
                traits[found.Name] = found.Traits;
            }
        }

        Sources = sources.ToFrozenDictionary(StringComparer.Ordinal);
        Traits = traits.ToFrozenDictionary(StringComparer.Ordinal);
        Filters = site.Filters.ToFrozenDictionary(StringComparer.Ordinal);
        Ordered = [.. sources.Keys];
    }

    /// <summary>Every source by name.</summary>
    public FrozenDictionary<string, IFormBuilderMergeTagSource> Sources { get; }

    /// <summary>The traits of the site's own sources by name, as they were read once; a built-in source's place list decides for it.</summary>
    public FrozenDictionary<string, MergeTagSourceTraits> Traits { get; }

    /// <summary>The site's own filters by name.</summary>
    public FrozenDictionary<string, IFormBuilderMergeTagFilter> Filters { get; }

    /// <summary>Every source's name in the order the picker offers them: the built-in sources first.</summary>
    public IReadOnlyList<string> Ordered { get; }
}

/// <summary>
/// The site's own merge-tag sources and filters (ADR-0065, ADR-0083), each read once, here: a source's name and traits,
/// a filter's name. The registry takes those it may use and the start-up check the problems, so no source is asked
/// twice, and what the check let start is what the registry runs.
/// </summary>
internal sealed partial class SiteMergeTagExtensions
{
    /// <summary>The site's sources and filters, read from their collections, which create them.</summary>
    /// <param name="sources">The site's sources.</param>
    /// <param name="filters">The site's filters.</param>
    public SiteMergeTagExtensions(FormBuilderMergeTagSourceCollection sources, FormBuilderMergeTagFilterCollection filters)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(filters);
        var problems = new List<string>();
        var siteSources = Created(sources, "sources", problems);
        var siteFilters = Created(filters, "filters", problems);

        var named = new List<(string? Name, Type Type)>();
        var usable = new List<UsableMergeTagSource>();
        foreach (var source in siteSources)
        {
            if (!TryRead(source, out var name, out var traits, out var failure))
            {
                problems.Add($"The merge-tag source {source.GetType().FullName} failed with {failure!.GetType().FullName} as its {nameof(IFormBuilderMergeTagSource.Name)}, {nameof(IFormBuilderMergeTagSource.IsPerVisitor)} and {nameof(IFormBuilderMergeTagSource.Places)} were read: {failure.Message}");
                continue;
            }

            named.Add((name, source.GetType()));
            if (Problem("source", name, MergeTagPlaces.BuiltInSources.ContainsKey) is null && usable.TrueForAll(earlier => earlier.Name != name))
            {
                usable.Add(new UsableMergeTagSource(name!, source, traits!));
            }
        }

        Collect("source", named, MergeTagPlaces.BuiltInSources.ContainsKey, problems);
        named.Clear();
        var usableFilters = new Dictionary<string, IFormBuilderMergeTagFilter>(StringComparer.Ordinal);
        foreach (var filter in siteFilters)
        {
            if (!TryName(filter, out var name, out var failure))
            {
                problems.Add($"The merge-tag filter {filter.GetType().FullName} failed with {failure!.GetType().FullName} as its {nameof(IFormBuilderMergeTagFilter.Name)} was read: {failure.Message}");
                continue;
            }

            named.Add((name, filter.GetType()));
            if (Problem("filter", name, MergeTagEngine.IsBuiltInFilter) is null)
            {
                usableFilters.TryAdd(name!, filter);
            }
        }

        Collect("filter", named, MergeTagEngine.IsBuiltInFilter, problems);
        Sources = usable;
        Filters = usableFilters;
        Problems = problems;
    }

    /// <summary>The site's sources the registry may use, in their order, each with its name and traits as they were read.</summary>
    public IReadOnlyList<UsableMergeTagSource> Sources { get; }

    /// <summary>The site's filters the registry may use, by name.</summary>
    public IReadOnlyDictionary<string, IFormBuilderMergeTagFilter> Filters { get; }

    /// <summary>
    /// The problems of the site's sources and filters, which stop the site (ADR-0042): a collection the container cannot
    /// create one of, with what the container said; and one that fails to say its name or traits, a name outside the
    /// grammar, a built-in one's name, or a name two of them share, each naming the type.
    /// </summary>
    public IReadOnlyList<string> Problems { get; }

    /// <summary>A source's or filter's name, as the grammar reads it (ADR-0014): a lowercase letter, then letters and digits.</summary>
    [GeneratedRegex("^[a-z][A-Za-z0-9]*$", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();

    // A collection's items, which the container creates as it is read; none, with the problem, when it cannot create one
    // of them, so the other collection's items stay usable.
    private static T[] Created<T>(IEnumerable<T> collection, string kind, List<string> problems)
    {
        try
        {
            return [.. collection];
        }
        catch (InvalidOperationException exception)
        {
            problems.Add($"Jiron Form Builder cannot create the site's merge-tag {kind}: {exception.Message}");
            return [];
        }
    }

    // A site's source's name and traits; false, with the exception, when the source fails to say them.
    private static bool TryRead(IFormBuilderMergeTagSource source, out string? name, out MergeTagSourceTraits? traits, out Exception? failure)
    {
        try
        {
            (name, traits, failure) = (source.Name, MergeTagSourceTraits.Of(source), null);
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            (name, traits, failure) = (null, null, exception);
            return false;
        }
    }

    // A site's filter's name; false, with the exception, when the filter fails to say it.
    private static bool TryName(IFormBuilderMergeTagFilter filter, out string? name, out Exception? failure)
    {
        try
        {
            (name, failure) = (filter.Name, null);
            return true;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            (name, failure) = (null, exception);
            return false;
        }
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

/// <summary>A site's source the registry may use, with its name and traits as they were read once.</summary>
/// <param name="Name">Its name.</param>
/// <param name="Source">The source.</param>
/// <param name="Traits">Its traits.</param>
internal sealed record UsableMergeTagSource(string Name, IFormBuilderMergeTagSource Source, MergeTagSourceTraits Traits);

/// <summary>
/// A site's source's traits as they were read once (ADR-0065, ADR-0083): whether its values differ per visitor, and the
/// places it may be used in, copied so that the source's own set cannot change them later.
/// </summary>
/// <param name="IsPerVisitor">Whether the values differ per visitor.</param>
/// <param name="Places">The places, within those that take a site's sources; null for all of them.</param>
internal sealed record MergeTagSourceTraits(bool IsPerVisitor, FrozenSet<MergeTagPlace>? Places)
{
    /// <summary>The traits <paramref name="source"/> says now; what it throws passes on.</summary>
    public static MergeTagSourceTraits Of(IFormBuilderMergeTagSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new MergeTagSourceTraits(source.IsPerVisitor, source.Places?.ToFrozenSet());
    }
}

/// <summary>
/// Stops the site when a site's merge-tag source or filter cannot be used (ADR-0042, ADR-0065): one the container cannot
/// create, one that fails to say its name or traits, a name outside the grammar, a built-in one's name, or a name two
/// of them share, as <see cref="SiteMergeTagExtensions"/> read them. It runs with the other start-up checks and names
/// every problem at once.
/// </summary>
/// <param name="site">The site's sources and filters, as they were read once.</param>
internal sealed class MergeTagStartupCheck(SiteMergeTagExtensions site) : IValidateOptions<FormBuilderStartupChecks>
{
    /// <inheritdoc />
    public ValidateOptionsResult Validate(string? name, FormBuilderStartupChecks options) =>
        site.Problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(site.Problems);
}
