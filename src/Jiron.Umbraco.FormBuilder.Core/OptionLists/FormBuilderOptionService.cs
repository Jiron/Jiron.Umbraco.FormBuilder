using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Logic;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Cache;

namespace Jiron.Umbraco.FormBuilder.Core.OptionLists;

/// <summary>
/// Resolves the options of fields and library lists (ADR-0073): a field's own options and a manual list's items in the
/// culture, with their dictionary items; a source's options through its kind, within <see cref="SourceTimeout"/>,
/// cleaned as entries need them; a cascading field's options for the parent's answers; and a library list's source
/// options kept for its <c>cacheSeconds</c>.
/// </summary>
internal sealed partial class FormBuilderOptionService(
    OptionListService lists,
    OptionKindRegistry kinds,
    MergeTagDictionary dictionary,
    AppCaches appCaches,
    TimeProvider time,
    ILogger<FormBuilderOptionService> logger) : IFormBuilderOptionService
{
    /// <summary>The longest a source may take before its field offers no options; 5 seconds unless a test sets less.</summary>
    internal TimeSpan SourceTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <inheritdoc />
    public async Task<FormBuilderResolvedOptions> GetAsync(FormField field, FormBuilderOptionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(field);
        ArgumentNullException.ThrowIfNull(context);
        if (field.Options is not { } options)
        {
            return FormBuilderResolvedOptions.None;
        }

        // Only a field that a parent filters cascades; any other ignores parent answers, and so does its source.
        var parentValues = options.Parent is null ? [] : context.ParentValues;
        var request = new FormBuilderOptionRequest(context.Culture, parentValues, context.Page);
        var resolved = options.Source switch
        {
            OptionSource.Items => new FormBuilderResolvedOptions(Clean(await ItemsAsync(options.Items, context.Culture), "field", field.Key), IsAvailable: true),
            OptionSource.List when options.OptionList is { } list => await ListAsync(list, request, cancellationToken),
            OptionSource.List => FormBuilderResolvedOptions.Unavailable,
            var source => Kind(source) is { OnField: true } kind ? await SourceAsync(kind, options.Config, request, "field", field.Key, cancellationToken) : FormBuilderResolvedOptions.Unavailable,
        };

        return options.Parent is null || !resolved.IsAvailable ? resolved : resolved with { Options = Belonging(resolved.Options, parentValues) };
    }

    /// <inheritdoc />
    public Task<FormBuilderResolvedOptions> GetListAsync(Guid list, FormBuilderOptionContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ListAsync(list, new FormBuilderOptionRequest(context.Culture, context.ParentValues, context.Page), cancellationToken);
    }

    // The options that belong to the parent's answers: those that name one of them, and those that name none.
    private static IReadOnlyList<FormBuilderOption> Belonging(IReadOnlyList<FormBuilderOption> options, IReadOnlyList<string> parentValues)
    {
        var answers = parentValues.ToHashSet(StringComparer.Ordinal);
        return [.. options.Where(option => option.ParentValues.Count == 0 || option.ParentValues.Any(answers.Contains))];
    }

    private OptionKind? Kind(OptionSource source) => source switch
    {
        OptionSource.Content => kinds.Find(OptionListKind.Content),
        OptionSource.DataType => kinds.Find(OptionListKind.DataType),
        OptionSource.Url => kinds.Find(OptionListKind.Url),
        OptionSource.Countries => kinds.Find(OptionListKind.Countries),
        OptionSource.Code => kinds.Find(OptionListKind.Code),
        _ => null,
    };

    private async Task<FormBuilderResolvedOptions> ListAsync(Guid key, FormBuilderOptionRequest request, CancellationToken cancellationToken)
    {
        if (await lists.GetAsync(key, cancellationToken) is not { IsReadable: true, Kind: { } kind } list)
        {
            return FormBuilderResolvedOptions.Unavailable;
        }

        if (kind.HasItems)
        {
            return new FormBuilderResolvedOptions(Clean(await ItemsAsync(list.Items, request.Culture), "list", key), IsAvailable: true);
        }

        if (list.Row.CacheSeconds == 0)
        {
            return await SourceAsync(kind, list.Config, request, "list", key, cancellationToken);
        }

        // Kept for the list's time per culture, page and parent answers, and dropped with the list when it changes. A
        // source that fails is not kept: the load throws, which forgets it, and the next request asks again.
        try
        {
            return await RuntimeCacheLoads.GetAsync(
                appCaches.RuntimeCache,
                OptionListCacheKeys.Options(key, request.Culture, request.Page, request.ParentValues),
                OptionListCacheKeys.Options(key, request.Culture, request.Page, request.ParentValues) + ".loading",
                async () => await SourceAsync(kind, list.Config, request, "list", key, CancellationToken.None) is { IsAvailable: true } found ? found : throw new SourceUnavailableException(),
                TimeSpan.FromSeconds(list.Row.CacheSeconds),
                cancellationToken);
        }
        catch (SourceUnavailableException)
        {
            return FormBuilderResolvedOptions.Unavailable;
        }
    }

    private async Task<IReadOnlyList<FormBuilderOption>> ItemsAsync(IReadOnlyList<FieldOption> items, string culture)
    {
        var keys = OptionItems.DictionaryKeys(items, culture);
        var texts = keys.Count == 0 ? DictionaryTexts.None : await dictionary.TextsAsync(keys, culture);
        return OptionItems.ToOptions(items, texts, culture);
    }

    // A source's options, within the timeout, cleaned; unavailable when it fails or takes too long. The wait ends at the
    // timeout even for a source that does not heed its token.
    private async Task<FormBuilderResolvedOptions> SourceAsync(OptionKind kind, SettingsObject? config, FormBuilderOptionRequest request, string owner, Guid key, CancellationToken cancellationToken)
    {
        using var timeout = new CancellationTokenSource(SourceTimeout, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            var pending = kind.ResolveAsync(new OptionKindRequest(config, [], DictionaryTexts.None, request), linked.Token);
            var options = pending.IsCompletedSuccessfully ? pending.Result : await pending.AsTask().WaitAsync(linked.Token);
            return new FormBuilderResolvedOptions(Clean(options ?? [], owner, key), IsAvailable: true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (timeout.IsCancellationRequested && exception is not OutOfMemoryException)
        {
            LogSourceTimedOut(logger, kind.Name, owner, key, SourceTimeout.TotalSeconds);
            return FormBuilderResolvedOptions.Unavailable;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // The type and the stack, never the message, which may quote what the source read (ADR-0009).
            LogSourceFailed(logger, kind.Name, owner, key, exception.GetType().FullName, exception.StackTrace);
            return FormBuilderResolvedOptions.Unavailable;
        }
    }

    // What entries can store of a source's options: a value that is more than spaces, at most 200 characters, the same
    // once cleaned as answers are (ADR-0005) and not equal to an earlier one once folded; a blank label shows the value;
    // at most MaxOptions. What is left out is logged as a count, never with values.
    private List<FormBuilderOption> Clean(IReadOnlyList<FormBuilderOption> options, string owner, Guid key)
    {
        var kept = new List<FormBuilderOption>(Math.Min(options.Count, OptionListService.MaxOptions));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var leftOut = 0;
        foreach (var option in options)
        {
            if (option?.Value is not { Length: > 0 and <= DefinitionSaveChecks.OptionValueLength } value
                || !string.Equals(TextSemantics.Clean(value), value, StringComparison.Ordinal)
                || kept.Count == OptionListService.MaxOptions
                || !seen.Add(TextSemantics.Fold(value)))
            {
                leftOut++;
                continue;
            }

            kept.Add(string.IsNullOrWhiteSpace(option.Label) && option.Label is not null ? option with { Label = null } : option);
        }

        if (leftOut > 0)
        {
            LogOptionsLeftOut(logger, owner, key, leftOut);
        }

        return kept;
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "The {Kind} source of the {Owner} {Key} failed with {ExceptionType}, so it offers no options. {StackTrace}")]
    private static partial void LogSourceFailed(ILogger logger, string kind, string owner, Guid key, string? exceptionType, string? stackTrace);

    [LoggerMessage(Level = LogLevel.Error, Message = "The {Kind} source of the {Owner} {Key} took longer than {Seconds} seconds, so it offers no options")]
    private static partial void LogSourceTimedOut(ILogger logger, string kind, string owner, Guid key, double seconds);

    [LoggerMessage(Level = LogLevel.Warning, Message = "The {Owner} {Key} left out {Count} options: blank, too long, not clean or repeated values, or more than 10,000")]
    private static partial void LogOptionsLeftOut(ILogger logger, string owner, Guid key, int count);

    // A source that gave no options is not kept.
    private sealed class SourceUnavailableException : Exception;
}
