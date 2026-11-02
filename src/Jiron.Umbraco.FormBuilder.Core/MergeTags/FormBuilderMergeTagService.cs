using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Logic;
using Microsoft.Extensions.Logging;
using MergeTagEngine = Jiron.Umbraco.FormBuilder.Logic.MergeTags;

namespace Jiron.Umbraco.FormBuilder.Core.MergeTags;

/// <summary>
/// The merge-tag service (ADR-0065): parses a template with what its place adds, asks each source once per path, and
/// renders with the culture's formats. A site's source or filter that fails gives the empty text, and only the type and
/// the stack of its exception are logged, since the message may hold a visitor's answer (D13).
/// </summary>
/// <param name="registry">The sources and filters.</param>
/// <param name="cultures">The culture formats.</param>
/// <param name="logger">Where failures of a site's sources and filters go.</param>
internal sealed partial class FormBuilderMergeTagService(MergeTagRegistry registry, MergeTagCultures cultures, ILogger<FormBuilderMergeTagService> logger)
    : IFormBuilderMergeTagService
{
    // The source of answer recall in visitor texts.
    private const string FieldSource = "field";

    // The source whose values a page fills in itself in a default value: its address's query values, which a cached page
    // must not keep from the address it was cached for (ADR-0118).
    private const string QuerySource = "query";

    /// <inheritdoc />
    public async Task<MergeTagRendering> RenderAsync(string text, MergeTagPlace place, MergeTagContext context, MergeTagScope scope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(scope);
        var parsed = MergeTagEngine.Parse(text, context, Rules(place, context, scope.Culture, scope.Form, scope.TextOwner));
        var values = new Dictionary<string, MergeTagValue?>(StringComparer.Ordinal);
        foreach (var part in parsed.Parts)
        {
            if (part is TagPart { Path: { } path } && !values.ContainsKey(path))
            {
                values[path] = await ResolveAsync(path, scope, place, context, cancellationToken);
            }
        }

        var rendered = MergeTagEngine.Render(parsed, path => values.GetValueOrDefault(path), cultures.For(scope.Culture));
        return new MergeTagRendering(rendered.Text, rendered.Problems);
    }

    /// <inheritdoc />
    public async Task<MergeTagTemplateValues> ValuesAsync(string text, MergeTagPlace place, MergeTagContext context, MergeTagScope scope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(scope);
        var rules = Rules(place, context, scope.Culture, scope.Form, scope.TextOwner);
        var recalled = new List<Guid>();
        // Each path the place refuses for its source, which the browser cannot tell from an unknown one (ADR-0118).
        var refused = new List<string>();
        var problem = rules.PathProblem;
        rules = rules with
        {
            PathProblem = path =>
            {
                var code = problem(path);
                if (code == MergeTagProblems.SourceNotForPlace && !refused.Contains(path))
                {
                    refused.Add(path);
                }

                return code;
            },
        };
        if (place == MergeTagPlace.VisitorText && registry.Sources.GetValueOrDefault(FieldSource) is FieldMergeTagSource fields)
        {
            // Every field a well-formed tag names, refused or not: the browser refuses one that does not come before the
            // text by the place it has in the form, as the server does, so it needs the field too (ADR-0085).
            var refusal = rules.PathProblem;
            rules = rules with
            {
                PathProblem = path =>
                {
                    if (path.Length > FieldSource.Length + 1 && SourceName(path) == FieldSource
                        && fields.FieldOf(path[(FieldSource.Length + 1)..], scope.Form, place) is { } field && !recalled.Contains(field))
                    {
                        recalled.Add(field);
                    }

                    return refusal(path);
                },
            };
        }

        var parsed = MergeTagEngine.Parse(text, context, rules);
        var values = new Dictionary<string, MergeTagValue>(StringComparer.Ordinal);
        // Its own text and a tag with an error are written as they stand; a tag renders, and a limit ends the output.
        var renders = false;
        foreach (var part in parsed.Parts)
        {
            if (part is StopPart)
            {
                renders = true;
            }

            if (part is not TagPart tag)
            {
                continue;
            }

            renders = true;
            if (tag.Path is not { } path || values.ContainsKey(path))
            {
                continue;
            }

            // The browser fills in answer recall itself, from the answers given so far (ADR-0068).
            if ((place == MergeTagPlace.VisitorText && SourceName(path) == FieldSource)
                || (place == MergeTagPlace.DefaultValue && SourceName(path) == QuerySource))
            {
                continue;
            }

            if (await ResolveAsync(path, scope, place, context, cancellationToken) is { } value)
            {
                values[path] = value;
            }
        }

        return new MergeTagTemplateValues(renders, values, recalled) { Refused = refused };
    }

    /// <inheritdoc />
    public IReadOnlyList<MergeTagProblem> Check(string text, MergeTagPlace place, MergeTagContext context, FormDefinition form, Guid? textOwner = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(form);
        var parsed = MergeTagEngine.Parse(text, context, Rules(place, context, string.Empty, form, textOwner));
        var problems = new List<MergeTagProblem>(parsed.Problems);
        foreach (var part in parsed.Parts)
        {
            if (part is TagPart { Path: { } path } tag && !IsKnown(path, form, place))
            {
                problems.Add(new MergeTagProblem(MergeTagProblems.UnknownPath, tag.PathAt));
            }
        }

        return [.. problems.OrderBy(problem => problem.At)];
    }

    /// <inheritdoc />
    public IReadOnlyList<MergeTagPathInfo> Paths(MergeTagPlace place, FormDefinition form)
    {
        ArgumentNullException.ThrowIfNull(form);
        var rules = MergeTagPlaces.For(place);
        var paths = new List<MergeTagPathInfo>();
        foreach (var name in registry.Ordered)
        {
            if (!Takes(rules, place, name))
            {
                continue;
            }

            try
            {
                paths.AddRange(registry.Sources[name].Paths(form));
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                LogSourceFailed(logger, name, exception.GetType().FullName, exception.StackTrace);
            }
        }

        return paths;
    }

    /// <summary>
    /// What the place adds to the language: the site's filters, whether it takes them, the sources it takes, and in a
    /// visitor text the fields before the text.
    /// </summary>
    private TemplateRules Rules(MergeTagPlace place, MergeTagContext context, string culture, FormDefinition form, Guid? textOwner)
    {
        var rules = MergeTagPlaces.For(place);
        if (!rules.Contexts.Contains(context))
        {
            throw new ArgumentException($"A merge-tag template in the place {place} does not render in the {context} context.", nameof(context));
        }

        return new TemplateRules(
            name => registry.Filters.TryGetValue(name, out var filter) ? Run(filter, name, culture) : null,
            rules.TakesSiteFilters,
            path => PathProblem(path, rules, place, form, textOwner));
    }

    /// <summary>
    /// The problem a path has in a place: a built-in source the place's list lacks, a site's source whose traits the
    /// place does not take, or in a visitor text a field that does not come before the text (ADR-0068). A source no one
    /// has is a warning when the tag renders, not a problem of the place.
    /// </summary>
    private string? PathProblem(string path, PlaceRules rules, MergeTagPlace place, FormDefinition form, Guid? textOwner)
    {
        var name = SourceName(path);
        if (MergeTagPlaces.BuiltInSources.ContainsKey(name))
        {
            if (rules.Sources is not null && !rules.Sources.Contains(name))
            {
                return MergeTagProblems.SourceNotForPlace;
            }

            // A path of the field source alone, with no alias, names no field: it is an unknown path, as any other.
            return place == MergeTagPlace.VisitorText && path.StartsWith("field.", StringComparison.Ordinal)
                && registry.Sources.GetValueOrDefault(name) is FieldMergeTagSource fields
                && fields.FieldOf(path["field.".Length..], form, place) is { } field && !MergeTagRecall.IsBefore(form, field, textOwner)
                ? MergeTagProblems.FieldAfterText
                : null;
        }

        return registry.Traits.TryGetValue(name, out var traits) && !MergeTagPlaces.TakesSiteSource(rules, place, traits) ? MergeTagProblems.SourceNotForPlace : null;
    }

    /// <summary>Whether a place takes a source: a built-in one by the place's list, a site's by the traits it said once.</summary>
    private bool Takes(PlaceRules rules, MergeTagPlace place, string name) =>
        MergeTagPlaces.BuiltInSources.ContainsKey(name)
            ? rules.Sources is null || rules.Sources.Contains(name)
            : registry.Traits.TryGetValue(name, out var traits) && MergeTagPlaces.TakesSiteSource(rules, place, traits);

    /// <summary>Whether a path is known in a place before any value: any path of a source whose paths are not all listed, and a listed one of the others.</summary>
    private bool IsKnown(string path, FormDefinition form, MergeTagPlace place)
    {
        if (!registry.Sources.TryGetValue(SourceName(path), out var source))
        {
            return false;
        }

        return source is not IFiniteMergeTagSource finite || finite.Knows(path, form, place);
    }

    /// <summary>A path's value from its source, null when no source knows it; a failing source gives the empty text.</summary>
    private async Task<MergeTagValue?> ResolveAsync(string path, MergeTagScope scope, MergeTagPlace place, MergeTagContext context, CancellationToken cancellationToken)
    {
        var name = SourceName(path);
        if (!registry.Sources.TryGetValue(name, out var source))
        {
            return null;
        }

        try
        {
            var rest = path.Length > name.Length ? path[(name.Length + 1)..] : string.Empty;
            var value = await source.ResolveAsync(new MergeTagSourceRequest(rest, scope, place, context), cancellationToken);
            return scope.BreaksVisitorLinks && VisitorLinks.CanHoldVisitorText(name, rest) ? VisitorLinks.Broken(value) : value;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LogSourceFailed(logger, name, exception.GetType().FullName, exception.StackTrace);
            return MergeTagValue.Empty;
        }
    }

    /// <summary>A site's filter as the engine runs it: arguments that fail to be checked do not fit, and a failing filter gives the empty text.</summary>
    private SiteFilter Run(IFormBuilderMergeTagFilter filter, string name, string culture) => new(
        arguments =>
        {
            try
            {
                return filter.Accepts([.. arguments.Select(Argument)]);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                LogFilterFailed(logger, name, exception.GetType().FullName, exception.StackTrace);
                return false;
            }
        },
        (value, arguments) =>
        {
            try
            {
                return filter.Apply(new MergeTagFilterRequest(value, [.. arguments.Select(Argument)], culture)) ?? MergeTagValue.Empty;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                LogFilterFailed(logger, name, exception.GetType().FullName, exception.StackTrace);
                return MergeTagValue.Empty;
            }
        });

    private static MergeTagArgument Argument(TemplateArgument argument) => new(argument.Text, argument.IsString);

    /// <summary>The source's name: the path up to its first dot.</summary>
    private static string SourceName(string path)
    {
        var dot = path.IndexOf('.', StringComparison.Ordinal);
        return dot < 0 ? path : path[..dot];
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The merge-tag source '{Source}' failed with {ExceptionType}; its tags render empty. {StackTrace}")]
    private static partial void LogSourceFailed(ILogger logger, string source, string? exceptionType, string? stackTrace);

    [LoggerMessage(Level = LogLevel.Error, Message = "The merge-tag filter '{Filter}' failed with {ExceptionType}; its tags render empty or as they are written. {StackTrace}")]
    private static partial void LogFilterFailed(ILogger logger, string filter, string? exceptionType, string? stackTrace);
}
