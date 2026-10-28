using System.Collections.Frozen;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

namespace Jiron.Umbraco.FormBuilder.Core.Entries;

/// <summary>
/// One field's answer as it was posted (ADR-0072), before it is cleaned: its texts in order and its named parts' texts,
/// as the visitor's browser or a form without JavaScript sent them. A null or empty text counts as none.
/// </summary>
/// <param name="Values">The field's own texts, in order.</param>
/// <param name="Parts">Each part's texts by its name, such as a name's <c>first</c>; none by default.</param>
internal sealed record PostedAnswer(IReadOnlyList<string?> Values, IReadOnlyDictionary<string, IReadOnlyList<string?>>? Parts = null);

/// <summary>
/// A submission as the pipeline hands it to <see cref="SubmissionReader"/> (ADR-0072): the published version with the
/// scope its merge tags render in, the instant and the site's time zone, the signed-in member's groups, every posted
/// answer by its field's key, each repeatable group's rows, and the uploads the posted tokens stand for.
/// </summary>
internal sealed record PostedSubmission
{
    private static readonly IReadOnlyDictionary<Guid, PostedAnswer> NoFields = FrozenDictionary<Guid, PostedAnswer>.Empty;
    private static readonly IReadOnlyDictionary<Guid, IReadOnlyList<IReadOnlyDictionary<Guid, PostedAnswer>>> NoRows =
        FrozenDictionary<Guid, IReadOnlyList<IReadOnlyDictionary<Guid, PostedAnswer>>>.Empty;
    private static readonly IReadOnlyDictionary<string, StoredEntryFile> NoUploads = FrozenDictionary<string, StoredEntryFile>.Empty;

    /// <summary>
    /// What the form's merge tags render for: its <see cref="MergeTagScope.Form"/> is the version the visitor submits
    /// (as published, with the designer's locks), and it carries the culture, the site, the page, the member, the query
    /// values and the context values. Its answers are the reader's to fill in.
    /// </summary>
    public required MergeTagScope Scope { get; init; }

    /// <summary>The instant of the submission, from the server's clock.</summary>
    public required DateTimeOffset Now { get; init; }

    /// <summary>The site's time zone, an IANA name this server knows; the pipeline falls back to UTC for one it does not (ADR-0043).</summary>
    public required string TimeZone { get; init; }

    /// <summary>The member groups of the signed-in member, which rules may look at; none without a member.</summary>
    public IReadOnlyList<string> MemberGroups { get; init => field = value ?? []; } = [];

    /// <summary>Each field's answer by its key, for the fields outside repeatable groups; a key the version lacks is ignored.</summary>
    public IReadOnlyDictionary<Guid, PostedAnswer> Fields { get; init => field = value ?? NoFields; } = NoFields;

    /// <summary>
    /// Each repeatable group's rows by its key, in the order the visitor saw them, every row its child fields' answers by
    /// their keys; an empty row is a row.
    /// </summary>
    public IReadOnlyDictionary<Guid, IReadOnlyList<IReadOnlyDictionary<Guid, PostedAnswer>>> Rows { get; init => field = value ?? NoRows; } = NoRows;

    /// <summary>
    /// The uploads of the form that the posted upload tokens stand for, by token, as the pipeline found them (6.1). Two
    /// tokens may stand for one upload, which the submission takes once; one an entry holds it takes not at all (ADR-0080).
    /// </summary>
    public IReadOnlyDictionary<string, StoredEntryFile> Uploads { get; init => field = value ?? NoUploads; } = NoUploads;
}
