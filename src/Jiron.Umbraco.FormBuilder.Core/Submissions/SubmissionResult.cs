using System.Collections.Immutable;
using Jiron.Umbraco.FormBuilder.Core.Definitions;

namespace Jiron.Umbraco.FormBuilder.Core.Submissions;

/// <summary>Why a submission, a page's check or a receipt was refused (ADR-0088, ADR-0089); each is one stable code of the public API.</summary>
internal enum SubmissionRefusal
{
    /// <summary>The form does not offer save and resume, or the site cannot say where its link comes back to (<c>resume_unavailable</c>, ADR-0163).</summary>
    ResumeUnavailable,

    /// <summary>The form is missing, unpublished, in the trash or unreadable (<c>form_not_found</c>).</summary>
    FormNotFound,

    /// <summary>The request's challenge is missing, forged, expired, another's or used for another entry (<c>challenge_required</c>).</summary>
    ChallengeRequired,

    /// <summary>The captcha's token does not pass, and the visitor can try again (ADR-0142).</summary>
    CaptchaFailed,

    /// <summary>The form takes entries only from members it admits (<c>member_required</c>).</summary>
    MemberRequired,

    /// <summary>The form has an entry with this submission id and other answers (<c>duplicate_submission</c>).</summary>
    DuplicateSubmission,

    /// <summary>The page showed a revision that cannot be mapped, or a consent wording that changed since (<c>form_changed</c>).</summary>
    FormChanged,

    /// <summary>Before the form's opening time or from its closing time on (<c>form_closed</c>).</summary>
    FormClosed,

    /// <summary>The form holds as many entries as its limit allows (<c>entry_limit_reached</c>).</summary>
    EntryLimitReached,

    /// <summary>The form takes one entry per member, and the member signed in has one (<c>already_submitted</c>, ADR-0231).</summary>
    AlreadySubmitted,

    /// <summary>The request names something the form does not have, or holds a value it cannot take (<c>invalid_request</c>).</summary>
    InvalidRequest,

    /// <summary>The answers fail their checks (<c>validation_failed</c>).</summary>
    ValidationFailed,

    /// <summary>The form has no entry with the submission id a receipt names, or only a draft (<c>not_found</c>, every miss alike).</summary>
    EntryNotFound,
}

/// <summary>
/// One problem of a refused submission (ADR-0088), where the visitor's page shows it: for <c>validation_failed</c>, an
/// error at a field; for <c>invalid_request</c>, a value of the request that cannot be read.
/// </summary>
/// <param name="Code">
/// For a validation error, the field's kind of error as its <c>messages</c> name it (<c>required</c>,
/// <c>maxLength</c>, ...), <see cref="SubmissionProblems.Check"/>, <see cref="SubmissionProblems.Rule"/>,
/// <see cref="SubmissionProblems.Empty"/> or <see cref="SubmissionProblems.Custom"/>; for a request,
/// <see cref="SubmissionProblems.InvalidValue"/>.
/// </param>
/// <param name="Path">
/// The field's alias, <c>&lt;grid&gt;.&lt;rowKey&gt;</c> for a grid row, <c>&lt;group&gt;[&lt;row&gt;].&lt;child&gt;</c>
/// for a repeatable group's child, or <c>""</c> for the form; for a request, the member that cannot be read.
/// </param>
/// <param name="Check">The index of the field's own check that fails, for <see cref="SubmissionProblems.Check"/>.</param>
/// <param name="Rule">The key of the validation rule whose message shows, for <see cref="SubmissionProblems.Rule"/>.</param>
/// <param name="Text">
/// For <see cref="SubmissionProblems.Custom"/>, the words a site's handler gave it, which the page shows as they are; null
/// for none, when the page shows the words of a check without its own message.
/// </param>
internal sealed record SubmissionProblem(string Code, string Path, int? Check = null, Guid? Rule = null, string? Text = null);

/// <summary>The codes of <see cref="SubmissionProblem"/> that are no field's kind of error.</summary>
internal static class SubmissionProblems
{
    /// <summary>One of the field's own checks fails.</summary>
    public const string Check = "check";

    /// <summary>A form-level validation rule shows its message at the field.</summary>
    public const string Rule = "rule";

    /// <summary>The submission holds no answer at all.</summary>
    public const string Empty = "empty";

    /// <summary>A site's handler of <see cref="Entries.EntrySubmittingNotification"/> refused the entry, in its own words (ADR-0233).</summary>
    public const string Custom = "custom";

    /// <summary>A value of the request cannot be read.</summary>
    public const string InvalidValue = "invalid_value";
}

/// <summary>
/// The receipt of an entry (ADR-0012): the submission id that names it, its reference in the form's format, and when it
/// was submitted.
/// </summary>
/// <param name="SubmissionId">The submission id, the visitor's or the one the server made.</param>
/// <param name="Reference">The entry's reference, such as <c>REQ-2026-00042</c>.</param>
/// <param name="Submitted">When it was submitted.</param>
internal sealed record SubmissionReceipt(Guid SubmissionId, string Reference, DateTimeOffset Submitted);

/// <summary>What the visitor gets after sending (ADR-0088), rendered for their entry.</summary>
internal sealed record SubmissionOutcome
{
    /// <summary>The message the runtime shows as its own thank-you: no outcome, or none that could be followed.</summary>
    public static readonly SubmissionOutcome Default = new() { Kind = OutcomeKind.Message };

    /// <summary>A message, perhaps followed by a redirect, or a redirect at once.</summary>
    public required OutcomeKind Kind { get; init; }

    /// <summary>The message, sanitised; null for the runtime's own thank-you.</summary>
    public string? Html { get; init; }

    /// <summary>Where the visitor goes, checked by the publish rule; null for nowhere.</summary>
    public string? Url { get; init; }

    /// <summary>How long a message shows before the redirect, in seconds.</summary>
    public int DelaySeconds { get; init; }

    /// <summary>Whether the message shows a copy of the answers (6.5).</summary>
    public bool ShowAnswers { get; init; }
}

/// <summary>
/// What <see cref="FormSubmissions"/> answers (ADR-0088, ADR-0089): an entry's receipt and outcome, a page whose answers
/// pass, or a refusal.
/// </summary>
internal sealed record SubmissionResult
{
    private SubmissionResult()
    {
    }

    /// <summary>Why it was refused; null for an entry and for a page that passes.</summary>
    public SubmissionRefusal? Refusal { get; private init; }

    /// <summary>When the link of a draft this request saved stops working; null for anything else.</summary>
    public DateTimeOffset? DraftExpires { get; private init; }

    /// <summary>The token that names the saved draft to the page that saved it (ADR-0252); one that names nothing where none was stored.</summary>
    public string? DraftToken { get; private init; }

    /// <summary>The entry's receipt; null for a refusal.</summary>
    public SubmissionReceipt? Receipt { get; private init; }

    /// <summary>The entry's outcome; null for a refusal.</summary>
    public SubmissionOutcome? Outcome { get; private init; }

    /// <summary>Whether the entry was there already: a retry of the same answers with the same submission id.</summary>
    public bool Replayed { get; private init; }

    /// <summary>The problems of <see cref="SubmissionRefusal.ValidationFailed"/> and <see cref="SubmissionRefusal.InvalidRequest"/>.</summary>
    public ImmutableArray<SubmissionProblem> Problems { get; private init; } = [];

    /// <summary>An entry, stored now or found for a retry.</summary>
    public static SubmissionResult Accepted(SubmissionReceipt receipt, SubmissionOutcome outcome, bool replayed) => new()
    {
        Receipt = receipt ?? throw new ArgumentNullException(nameof(receipt)),
        Outcome = outcome ?? throw new ArgumentNullException(nameof(outcome)),
        Replayed = replayed,
    };

    /// <summary>A page whose answers pass every check (ADR-0089): neither an entry nor a refusal.</summary>
    public static SubmissionResult Valid() => new();

    /// <summary>A draft that was saved, or one the protection dropped, which is told nothing different (ADR-0163).</summary>
    /// <param name="expires">When its link's time is over.</param>
    /// <param name="token">The draft's token, or one that names nothing.</param>
    public static SubmissionResult Saved(DateTimeOffset expires, string token) => new() { DraftExpires = expires, DraftToken = token ?? throw new ArgumentNullException(nameof(token)) };

    /// <summary>A refusal without problems.</summary>
    /// <exception cref="ArgumentException">The refusal is one that has problems.</exception>
    public static SubmissionResult Refused(SubmissionRefusal refusal) =>
        refusal is SubmissionRefusal.ValidationFailed or SubmissionRefusal.InvalidRequest
            ? throw new ArgumentException("This refusal has problems.", nameof(refusal))
            : new() { Refusal = refusal };

    /// <summary>The answers fail their checks.</summary>
    public static SubmissionResult Invalid(IEnumerable<SubmissionProblem> errors) => WithProblems(SubmissionRefusal.ValidationFailed, errors);

    /// <summary>Values of the request that cannot be read, by where they are.</summary>
    public static SubmissionResult Unreadable(IEnumerable<string> paths) =>
        WithProblems(SubmissionRefusal.InvalidRequest, (paths ?? throw new ArgumentNullException(nameof(paths))).Select(path => new SubmissionProblem(SubmissionProblems.InvalidValue, path)));

    private static SubmissionResult WithProblems(SubmissionRefusal refusal, IEnumerable<SubmissionProblem> problems)
    {
        ImmutableArray<SubmissionProblem> list = [.. problems ?? throw new ArgumentNullException(nameof(problems))];
        return list.IsEmpty ? throw new ArgumentException("A refusal with problems has one at least.", nameof(problems)) : new() { Refusal = refusal, Problems = list };
    }
}
