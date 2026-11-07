using System.Collections.Frozen;
using Jiron.Umbraco.FormBuilder.Core.Entries;

namespace Jiron.Umbraco.FormBuilder.Core.Submissions;

/// <summary>
/// A submission as a transport hands it to <see cref="FormSubmissions"/> (ADR-0088): the JSON endpoint reads it from
/// its body, 6.2 from a form post. It names the form by its key and every answer by its field's alias, as the visitor's
/// page knew them, so the pipeline maps them through the revision the page showed.
/// </summary>
internal sealed record SubmissionRequest
{
    private static readonly IReadOnlyDictionary<string, PostedAnswer> NoFields = FrozenDictionary<string, PostedAnswer>.Empty;
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, PostedAnswer>>> NoRows =
        FrozenDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, PostedAnswer>>>.Empty;
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> NoQuery = FrozenDictionary<string, IReadOnlyList<string>>.Empty;
    private static readonly IReadOnlyDictionary<string, string> NoContext = FrozenDictionary<string, string>.Empty;

    /// <summary>The form's key.</summary>
    public required Guid Form { get; init; }

    /// <summary>
    /// The version of the form a preview ticket names (ADR-0203), which the request meets as though it were published,
    /// a form for members as for a test member it admits, with no member; null for a visitor's request.
    /// </summary>
    public int? Preview { get; init; }

    /// <summary>
    /// For a preview's submission, whether its test entry runs its actions as a visitor's entry would (ADR-0204), as its
    /// ticket says; a test entry otherwise sets off nothing.
    /// </summary>
    public bool PreviewRunsActions { get; init; }

    /// <summary>The public revision the visitor's page showed (ADR-0002).</summary>
    public required int Revision { get; init; }

    /// <summary>The visitor's id of this submission, which makes a retry safe (section 4.4); null for the server to make one.</summary>
    public Guid? SubmissionId { get; init; }

    /// <summary>The culture the page was in, as posted; the pipeline holds it to the site's languages.</summary>
    public string? Culture { get; init; }

    /// <summary>The content page the form was on, as posted.</summary>
    public Guid? ContentKey { get; init; }

    /// <summary>
    /// The page a visitor goes to after the send in place of the outcome, which the form's placement names (ADR-0129):
    /// set only from a post's encrypted route token, never from a request's body.
    /// </summary>
    public Guid? Redirect { get; init; }

    /// <summary>
    /// Each answer by its field's alias, for the fields outside repeatable groups: its texts, and its parts' texts by name
    /// (a consent's wording hash as its <c>wording</c> part, ADR-0058).
    /// </summary>
    public IReadOnlyDictionary<string, PostedAnswer> Fields { get; init => field = value ?? NoFields; } = NoFields;

    /// <summary>Each repeatable group's rows by the group's alias, in the order the visitor saw them, every row its children's answers by alias.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, PostedAnswer>>> Rows { get; init => field = value ?? NoRows; } = NoRows;

    /// <summary>The page's query values by name, as posted; only the names the form lists are read.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Query { get; init => field = value ?? NoQuery; } = NoQuery;

    /// <summary>The context values by name, as posted; only the names the form lists are read and stored.</summary>
    public IReadOnlyDictionary<string, string> Context { get; init => field = value ?? NoContext; } = NoContext;

    /// <summary>
    /// The key of the member signed in on the request, as the transport found it, or null: the merge tags' member, whose
    /// values a field prefilled from the member takes (ADR-0124).
    /// </summary>
    public Guid? Member { get; init; }

    /// <summary>
    /// The name of the server key the request carried, as the transport checked it (ADR-0126), or null: the entry records
    /// it as its context value <see cref="ServerKeyContext"/>, and only that key may ask for its receipt.
    /// </summary>
    public string? ServerKey { get; init; }

    /// <summary>The challenge the request posted, read by <see cref="Challenges"/> (ADR-0138).</summary>
    public string? Challenge { get; init; }

    /// <summary>
    /// Reads the request's challenge: the transport's own, since only it knows how challenges are protected. Null for a
    /// transport whose posts carry none, as a post without JavaScript on a cached page cannot (ADR-0012).
    /// </summary>
    public IChallengeReader? Challenges { get; init; }

    /// <summary>What the gate found in the request's challenge; null without one.</summary>
    public ChallengeTicket? Ticket { get; init; }

    /// <summary>The token of the draft this submission finishes, as posted (ADR-0163); null for none.</summary>
    public string? ResumeToken { get; init; }

    /// <summary>The address a draft's link goes to, as posted (ADR-0163); null on anything but a draft.</summary>
    public string? DraftEmail { get; init; }

    /// <summary>The token of the form's captcha, as posted (ADR-0142).</summary>
    public string? CaptchaToken { get; init; }

    /// <summary>The answer to the challenge's puzzle, as posted (ADR-0141).</summary>
    public string? ProofOfWork { get; init; }

    /// <summary>What the page's honeypot was posted with (ADR-0012), which only a bot fills in; null or blank for nothing.</summary>
    public string? Honeypot { get; init; }

    /// <summary>The request's address as the host tells it, which the blocked ranges are matched against and which is not stored from here (section 4.8).</summary>
    public System.Net.IPAddress? Address { get; init; }

    /// <summary>The context name an entry a server key submitted records the key's name under (ADR-0126).</summary>
    public const string ServerKeyContext = "jfbServerKey";

    /// <summary>
    /// Reads the upload tokens that the values of fields storing files post (ADR-0087): the transport's own, since only
    /// it knows how its tokens are protected; null for a transport that posts none, so every such value is invalid.
    /// </summary>
    public IUploadTokenReader? Tokens { get; init; }
}

/// <summary>Reads a transport's upload tokens (ADR-0087, ADR-0088).</summary>
internal interface IUploadTokenReader
{
    /// <summary>
    /// The key of the file a token names for this form's field, or null when it names none: changed, expired, another
    /// field's, or uploaded with another challenge, which only the submission with the same one may claim (ADR-0006).
    /// </summary>
    /// <param name="token">The token, as a file field's value posts it.</param>
    /// <param name="form">The form's key.</param>
    /// <param name="field">The field's key.</param>
    /// <param name="challenge">The id of the request's challenge; null for a request without one.</param>
    Guid? FileOf(string token, Guid form, Guid field, Guid? challenge);
}
