using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Microsoft.Extensions.Logging;

namespace Jiron.Umbraco.FormBuilder.Core.Entries;

/// <summary>
/// An entry's title as the lists name it (section 4.3.4): the entry title of the version it was submitted with, written
/// for its answers in its own culture, without sensitive answers. None when the version has no title, the title writes
/// nothing, or it cannot be written, and the entry's number names it.
/// </summary>
/// <param name="tags">The merge tags.</param>
/// <param name="logger">Where a title that cannot be written goes.</param>
internal sealed partial class EntryTitles(IFormBuilderMergeTagService tags, ILogger<EntryTitles> logger)
{
    /// <summary>Whether the version an entry's scope holds has a title to write.</summary>
    /// <param name="scope">The entry's scope.</param>
    public static bool HasTitle(MergeTagScope scope) =>
        scope.Form.Settings.Submission.EntryTitle is { } title && !string.IsNullOrWhiteSpace(title);

    /// <summary>The entry's title, or none.</summary>
    /// <param name="scope">The entry's scope (<see cref="Submissions.SubmissionEntries"/>).</param>
    /// <param name="entryKey">The entry, which a failure names.</param>
    /// <param name="cancellationToken">The request's cancellation.</param>
    public async Task<string?> RenderAsync(MergeTagScope scope, Guid entryKey, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (!HasTitle(scope))
        {
            return null;
        }

        try
        {
            var rendered = (await tags.RenderAsync(scope.Form.Settings.Submission.EntryTitle!, MergeTagPlace.EntryTitle, MergeTagContext.Text, scope, cancellationToken)).Text.Trim();
            return rendered.Length == 0 ? null : rendered;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException && exception is not OperationCanceledException)
        {
            LogUnwritten(logger, LoggedException.Of(exception), entryKey);
            return null;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The title of the entry {Entry} could not be written.")]
    private static partial void LogUnwritten(ILogger logger, LoggedException exception, Guid entry);
}
