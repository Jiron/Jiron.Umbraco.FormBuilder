using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Settings;

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

    /// <summary>Settings with values that are not allowed.</summary>
    public const string ValidationFailed = "validation_failed";

    /// <summary>A setting that the host configuration locks.</summary>
    public const string SettingLocked = "setting_locked";

    /// <summary>A read that ran past its time budget.</summary>
    public const string QueryTimeout = "query_timeout";

    /// <summary>
    /// The code of a refusal, or null when <paramref name="exception"/> is none: a validation refusal of a definition
    /// answers with its first problem's code, such as <c>limit_exceeded</c>, and keeps every problem; one of settings
    /// with <c>validation_failed</c>.
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
            FormBuilderSettingsValidationException => ValidationFailed,
            FormBuilderSettingLockedException => SettingLocked,
            FormBuilderQueryTimeoutException => QueryTimeout,
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
        FormBuilderConflict.NotTrashed => "not_trashed",
        FormBuilderConflict.LandingChanged => "landing_changed",
        FormBuilderConflict.EntryLimitReached => "entry_limit_reached",
        FormBuilderConflict.AlreadySubmitted => "already_submitted",
        FormBuilderConflict.EntriesHeld => "entries_held",
        _ => throw new ArgumentOutOfRangeException(nameof(conflict), conflict, "A conflict without a code."),
    };
}
