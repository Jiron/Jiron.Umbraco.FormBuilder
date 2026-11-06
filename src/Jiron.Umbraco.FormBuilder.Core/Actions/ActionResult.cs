namespace Jiron.Umbraco.FormBuilder.Core.Actions;

/// <summary>What an attempt came to.</summary>
public enum ActionResultKind
{
    /// <summary>Delivered.</summary>
    Succeeded,

    /// <summary>Not delivered, and worth another attempt: it comes after the back-off, up to five attempts in all.</summary>
    TryAgain,

    /// <summary>Not delivered, and another attempt would come to the same: the run fails for good.</summary>
    Failed,

    /// <summary>It may have been delivered: nothing retries it by itself, and only a confirmed retry from the log runs it again.</summary>
    Uncertain,
}

/// <summary>What an attempt came to (ADR-0007), with why it did not succeed.</summary>
public sealed record ActionResult
{
    private ActionResult(ActionResultKind kind, string? code, string? message)
    {
        Kind = kind;
        Code = code;
        Message = message;
    }

    /// <summary>A delivered attempt.</summary>
    public static ActionResult Succeeded { get; } = new(ActionResultKind.Succeeded, null, null);

    /// <summary>What it came to.</summary>
    public ActionResultKind Kind { get; }

    /// <summary>Why it did not succeed: a code of <c>a-z</c>, <c>0-9</c>, <c>_</c>, <c>.</c> and <c>-</c>, at most 64 characters; null for a success.</summary>
    public string? Code { get; }

    /// <summary>
    /// Why, for the deliveries log, which line breaks and control characters leave and which is cut to fit; null for a
    /// success or when the code says it all. It never holds what a visitor typed or a secret.
    /// </summary>
    public string? Message { get; }

    /// <summary>An attempt worth repeating: the receiver is down, or refused it for now.</summary>
    /// <param name="code">Why, as a code.</param>
    /// <param name="message">Why, for the log, without what a visitor typed or a secret.</param>
    /// <exception cref="ArgumentException">The code is not written as a code is.</exception>
    public static ActionResult TryAgain(string code, string? message = null) => new(ActionResultKind.TryAgain, Coded(code), message);

    /// <summary>An attempt that another would not change: the receiver refuses it for good, or the settings cannot work.</summary>
    /// <param name="code">Why, as a code.</param>
    /// <param name="message">Why, for the log, without what a visitor typed or a secret.</param>
    /// <exception cref="ArgumentException">The code is not written as a code is.</exception>
    public static ActionResult Failed(string code, string? message = null) => new(ActionResultKind.Failed, Coded(code), message);

    /// <summary>An attempt that may have been delivered, such as a request whose answer never came.</summary>
    /// <param name="code">Why, as a code.</param>
    /// <param name="message">Why, for the log, without what a visitor typed or a secret.</param>
    /// <exception cref="ArgumentException">The code is not written as a code is.</exception>
    public static ActionResult Uncertain(string code, string? message = null) => new(ActionResultKind.Uncertain, Coded(code), message);

    private static string Coded(string code) =>
        Persistence.Repositories.DeliveryError.IsCode(code) ? code : throw new ArgumentException("A code holds 1 to 64 characters of a-z, 0-9, _, . and -.", nameof(code));
}

/// <summary>What a test run came to.</summary>
public enum ActionTestOutcome
{
    /// <summary>It reached what it was meant to reach.</summary>
    Sent,

    /// <summary>It reached nobody and created nothing: the preview shows what a run would do.</summary>
    DryRun,

    /// <summary>It did not work; the message says why.</summary>
    Failed,
}

/// <summary>What a test run came to, for the designer.</summary>
/// <param name="Outcome">What it came to.</param>
/// <param name="Message">What to tell the editor: a sentence without a secret, or null.</param>
/// <param name="Preview">What a run would do, one line each, such as the content it would create; empty when there is nothing to show.</param>
public sealed record ActionTestResult(ActionTestOutcome Outcome, string? Message = null, IReadOnlyList<string>? Preview = null)
{
    /// <summary>
    /// What it came to as a code the backoffice says in its own words (ADR-0190), such as <c>sent_to_you</c>,
    /// <c>dry_run</c>, <c>no_address</c> or <c>mail_off</c>; null leaves the editor the message.
    /// </summary>
    public string? Code { get; init; }
}

/// <summary>The codes of a test run the backoffice says in its own words (ADR-0190); a type may give others.</summary>
public static class ActionTestCodes
{
    /// <summary>It reached the asking user, such as a test mail to their address.</summary>
    public const string SentToYou = "sent_to_you";

    /// <summary>It reached what the action is set up to reach, marked as a test.</summary>
    public const string Sent = "sent";

    /// <summary>It reached nobody and created nothing: the preview shows what a run would do.</summary>
    public const string DryRun = "dry_run";

    /// <summary>The asking user has no address a test mail can go to.</summary>
    public const string NoAddress = "no_address";

    /// <summary>The site cannot send mail yet: it has no mail server or pickup folder.</summary>
    public const string MailOff = "mail_off";

    /// <summary>
    /// Actions and email are paused on this site (section 4.4), so what the test would reach would not go out; a dry run
    /// still runs.
    /// </summary>
    public const string Paused = "paused";

    /// <summary>The test could not finish; the type's log says why.</summary>
    public const string Failed = "failed";
}
