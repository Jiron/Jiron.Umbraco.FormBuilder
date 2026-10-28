using System.Collections.Frozen;
using System.Text.RegularExpressions;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Microsoft.Extensions.Options;
using MergeTagEngine = Jiron.Umbraco.FormBuilder.Logic.MergeTags;

namespace Jiron.Umbraco.FormBuilder.Core.MergeTags;

/// <summary>
/// Every merge-tag source and filter a site has (ADR-0065): the built-in sources first, then the site's own, each by
/// name. A site's source's name and traits and a filter's name are read once, here, so no rendering asks them again. A
/// site's source or filter whose name is taken, or that fails to say its name or traits, is left out here; the start-up
/// check refuses it, so a running site never has one.
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

        var traits = new Dictionary<string, MergeTagSourceTraits>(StringComparer.Ordinal);
        foreach (var source in siteSources)
        {
            if (TryRead(source, out var name, out var read, out _) && Problem("source", name, MergeTagPlaces.BuiltInSources.ContainsKey) is null && sources.TryAdd(name!, source))
            {
                traits[name!] = read!;
            }
        }

        var filters = new Dictionary<string, IFormBuilderMergeTagFilter>(StringComparer.Ordinal);
        foreach (var filter in siteFilters)
        {
            if (TryName(filter, out var name, out _) && Problem("filter", name, MergeTagEngine.IsBuiltInFilter) is null)
            {
                filters.TryAdd(name!, filter);
            }
        }

        Sources = sources.ToFrozenDictionary(StringComparer.Ordinal);
        Traits = traits.ToFrozenDictionary(StringComparer.Ordinal);
        Filters = filters.ToFrozenDictionary(StringComparer.Ordinal);
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

    /// <summary>
    /// The problems of a site's sources and filters, which stop the site (ADR-0042): one that fails to say its name or
    /// traits, a name outside the grammar, a built-in one's name, or a name two of them share; each names the type.
    /// </summary>
    public static IReadOnlyList<string> Problems(IEnumerable<IFormBuilderMergeTagSource> sources, IEnumerable<IFormBuilderMergeTagFilter> filters)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(filters);
        var problems = new List<string>();
        var named = new List<(string? Name, Type Type)>();
        foreach (var source in sources)
        {
            if (TryRead(source, out var name, out _, out var failure))
            {
                named.Add((name, source.GetType()));
            }
            else
            {
                problems.Add($"The merge-tag source {source.GetType().FullName} failed with {failure!.GetType().FullName} as its {nameof(IFormBuilderMergeTagSource.Name)}, {nameof(IFormBuilderMergeTagSource.IsPerVisitor)} and {nameof(IFormBuilderMergeTagSource.Places)} were read: {failure.Message}");
            }
        }

        Collect("source", named, MergeTagPlaces.BuiltInSources.ContainsKey, problems);
        named.Clear();
        foreach (var filter in filters)
        {
            if (TryName(filter, out var name, out var failure))
            {
                named.Add((name, filter.GetType()));
            }
            else
            {
                problems.Add($"The merge-tag filter {filter.GetType().FullName} failed with {failure!.GetType().FullName} as its {nameof(IFormBuilderMergeTagFilter.Name)} was read: {failure.Message}");
            }
        }

        Collect("filter", named, MergeTagEngine.IsBuiltInFilter, problems);
        return problems;
    }

    // A site's source's name and traits, read once; false, with the exception, when the source fails to say them.
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

    // A site's filter's name, read once; false, with the exception, when the filter fails to say it.
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

/// <summary>
/// A site's source's traits as the registry read them once (ADR-0065): whether its values differ per visitor, and the
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
/// Stops the site when a site's merge-tag source or filter cannot be used (ADR-0042, ADR-0065): one that fails to say
/// its name or traits, a name outside the grammar, a built-in one's name, or a name two of them share. It runs with the
/// other start-up checks and names every problem at once.
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
