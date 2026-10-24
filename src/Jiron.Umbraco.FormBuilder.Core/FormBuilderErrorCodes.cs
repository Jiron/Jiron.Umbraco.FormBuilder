using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;

namespace Jiron.Umbraco.FormBuilder.Core;

/// <summary>
/// The one mapping from a refusal to its stable code (ADR-0036, ADR-0044), which the APIs answer with and the tests
/// check. A missing item is no refusal: services return null or false for it.
/// </summary>
internal static class FormBuilderErrorCodes
{
    /// <summary>A change based on a version that changed meanwhile.</summary>
    public const string ConcurrencyConflict = "concurrency_conflict";

    /// <summary>A notification handler cancelled the change.</summary>
    public const string Cancelled = "cancelled";

    /// <summary>
    /// The code of a refusal, or null when <paramref name="exception"/> is none: a validation refusal answers with its
    /// first problem's code, such as <c>limit_exceeded</c>, and keeps every problem.
    /// </summary>
    public static string? Of(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception switch
        {
            FormBuilderConcurrencyException => ConcurrencyConflict,
            FormBuilderValidationException validation => validation.Problems[0].Code,
            FormBuilderConflictException conflict => Of(conflict.Conflict),
            FormBuilderCancelledException => Cancelled,
            _ => null,
        };
    }

    /// <summary>The code of a conflict.</summary>
    public static string Of(FormBuilderConflict conflict) => conflict switch
    {
        FormBuilderConflict.DuplicateAlias => "alias_taken",
        FormBuilderConflict.DuplicateKey => "key_taken",
        FormBuilderConflict.MissingFolder => "folder_missing",
        FormBuilderConflict.FolderIntoItself => "folder_into_itself",
        FormBuilderConflict.FolderTooDeep => "folder_too_deep",
        FormBuilderConflict.FolderNotEmpty => "folder_not_empty",
        FormBuilderConflict.InUse => "in_use",
        FormBuilderConflict.DuplicateSubmission => "duplicate_submission",
        FormBuilderConflict.ChallengeUsed => "challenge_used",
        FormBuilderConflict.MissingFile => "file_missing",
        FormBuilderConflict.MissingField => "field_missing",
        FormBuilderConflict.MissingVersion => "version_missing",
        FormBuilderConflict.Trashed => "trashed",
        _ => throw new ArgumentOutOfRangeException(nameof(conflict), conflict, "A conflict without a code."),
    };
}
