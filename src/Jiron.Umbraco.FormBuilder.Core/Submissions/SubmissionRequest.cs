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

    /// <summary>The public revision the visitor's page showed (ADR-0002).</summary>
    public required int Revision { get; init; }

    /// <summary>The visitor's id of this submission, which makes a retry safe (section 4.4); null for the server to make one.</summary>
    public Guid? SubmissionId { get; init; }

    /// <summary>The culture the page was in, as posted; the pipeline holds it to the site's languages.</summary>
    public string? Culture { get; init; }

    /// <summary>The content page the form was on, as posted.</summary>
    public Guid? ContentKey { get; init; }

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
    /// <summary>The key of the file a token names for this form's field, or null when it names none: changed, expired or another field's.</summary>
    /// <param name="token">The token, as a file field's value posts it.</param>
    /// <param name="form">The form's key.</param>
    /// <param name="field">The field's key.</param>
    Guid? FileOf(string token, Guid form, Guid field);
}
