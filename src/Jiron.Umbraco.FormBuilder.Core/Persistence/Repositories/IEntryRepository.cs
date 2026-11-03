using Jiron.Umbraco.FormBuilder.Core.Access;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

/// <summary>Where an entry is in the review workflow (section 4.4).</summary>
internal enum EntryState
{
    /// <summary>Saved to resume later; not submitted yet.</summary>
    Draft,

    /// <summary>Waiting for the visitor to verify the email address.</summary>
    Unverified,

    /// <summary>Submitted; the default.</summary>
    Submitted,

    /// <summary>Approved by a reviewer.</summary>
    Approved,

    /// <summary>Rejected by a reviewer.</summary>
    Rejected,

    /// <summary>Spam: stored, but no action runs for it.</summary>
    Spam,
}

/// <summary>
/// One value of an entry to store: an answer, an item of a multi-value answer (<paramref name="ValueIndex"/>) or a value
/// in a row of a repeater (<paramref name="RowIndex"/>). The field's storage kind decides which typed column holds it
/// (<see cref="EntryValueColumns"/>, ADR-0058); <paramref name="Text"/> keeps the value as submitted. A value that is
/// text only gets a key for filters and sorts (<see cref="EntryTextKey"/>), made of <see cref="KeyText"/> when it has one.
/// </summary>
/// <param name="FieldId">The field's row in the catalogue of the entry's form.</param>
/// <param name="Text">The value as submitted, or null.</param>
/// <param name="Number">A number, exact for its at most 15 significant digits (ADR-0003).</param>
/// <param name="Date">A date or time, in UTC.</param>
/// <param name="Bool">A yes or no.</param>
/// <param name="FileId">A file of the entry.</param>
/// <param name="RowIndex">The repeater row, 0 outside a repeater.</param>
/// <param name="ValueIndex">The item of a multi-value answer, 0 for a single value.</param>
internal sealed record NewEntryValue(
    int FieldId,
    string? Text,
    double? Number = null,
    DateTime? Date = null,
    bool? Bool = null,
    int? FileId = null,
    int RowIndex = 0,
    int ValueIndex = 0)
{
    /// <summary>Whether the value is text only, and so filters and sorts by its key.</summary>
    public bool IsText => Number is null && Date is null && Bool is null && FileId is null;

    /// <summary>
    /// The text the key is made of when it is not <see cref="Text"/>: a value with parts stores a JSON object and reads as
    /// its parts' texts (ADR-0058). Only a value that is text only has one.
    /// </summary>
    public string? KeyText { get; init; }
}

/// <summary>A stored value of an entry.</summary>
/// <param name="Id">The row's id.</param>
/// <param name="EntryId">The entry.</param>
/// <param name="FieldId">The field's catalogue row.</param>
/// <param name="RowIndex">The repeater row.</param>
/// <param name="ValueIndex">The item of a multi-value answer.</param>
/// <param name="Text">The value as submitted.</param>
/// <param name="TextKey">The cleaned and folded key of a text value.</param>
/// <param name="Number">A number.</param>
/// <param name="Date">A date or time, in UTC.</param>
/// <param name="Bool">A yes or no.</param>
/// <param name="FileId">A file of the entry.</param>
internal sealed record StoredEntryValue(
    long Id,
    long EntryId,
    int FieldId,
    int RowIndex,
    int ValueIndex,
    string? Text,
    string? TextKey,
    double? Number,
    DateTime? Date,
    bool? Bool,
    int? FileId);

/// <summary>An entry to store with its values, in one unit of work with its entry number (ADR-0003).</summary>
internal sealed record NewEntry
{
    /// <summary>The entry's key.</summary>
    public required Guid Key { get; init; }

    /// <summary>The form.</summary>
    public required int FormId { get; init; }

    /// <summary>The form's version the visitor submitted, a version of the same form.</summary>
    public required int FormVersionId { get; init; }

    /// <summary>The state it starts in.</summary>
    public EntryState State { get; init; } = EntryState.Submitted;

    /// <summary>Whether it came from the preview.</summary>
    public bool IsTest { get; init; }

    /// <summary>The submission id, unique per form, which makes retries safe (section 4.4).</summary>
    public required Guid SubmissionId { get; init; }

    /// <summary>The SHA-256 of the submission in lowercase hex.</summary>
    public required string SubmissionHash { get; init; }

    /// <summary>The culture, or "" for the invariant culture.</summary>
    public string Culture { get; init; } = string.Empty;

    /// <summary>The content item the form was placed on.</summary>
    public Guid? ContentKey { get; init; }

    /// <summary>The page's path, never its query or fragment (ADR-0009).</summary>
    public string? PageUrl { get; init; }

    /// <summary>The signed-in member.</summary>
    public Guid? MemberKey { get; init; }

    /// <summary>The address, only when the form keeps it (ADR-0009).</summary>
    public string? IpAddress { get; init; }

    /// <summary>The spam score (ADR-0012).</summary>
    public int SpamScore { get; init; }

    /// <summary>Why it looked like spam, or "".</summary>
    public string SpamReasons { get; init; } = string.Empty;

    /// <summary>The normalised text of its non-sensitive values, for search (ADR-0003).</summary>
    public string SearchText { get; init; } = string.Empty;

    /// <summary>The context values the form lists, as JSON (ADR-0009).</summary>
    public string Context { get; init; } = "{}";

    /// <summary>How long the visitor took.</summary>
    public int? DurationSeconds { get; init; }

    /// <summary>When the visitor first saw the form.</summary>
    public DateTimeOffset? FirstSeenUtc { get; init; }

    /// <summary>When the email address was verified.</summary>
    public DateTimeOffset? VerifiedUtc { get; init; }

    /// <summary>The SHA-256 of a draft's resume token in lowercase hex (ADR-0012).</summary>
    public string? ResumeTokenHash { get; init; }

    /// <summary>When the resume link expires.</summary>
    public DateTimeOffset? ResumeExpiresUtc { get; init; }

    /// <summary>The page a draft resumes on.</summary>
    public Guid? CurrentPageKey { get; init; }

    /// <summary>The challenge that made it: 32 lowercase hex characters, unique per form (ADR-0012).</summary>
    public string? ChallengeId { get; init; }

    /// <summary>When retention deletes it.</summary>
    public DateTimeOffset? PurgeAfterUtc { get; init; }

    /// <summary>
    /// The most entries the form takes (<c>availability.maxEntries</c>, ADR-0088), 1 or more, or null for no limit: the
    /// unit counts the entries that count toward it (<see cref="EntryLimits"/>) right after the form's counter, so two
    /// submissions at once cannot both take the last place, and refuses this one with
    /// <see cref="FormBuilderConflict.EntryLimitReached"/> when the form holds that many already. An entry that does not
    /// count itself is stored whatever the count.
    /// </summary>
    public int? EntryLimit { get; init; }

    /// <summary>The values.</summary>
    public IReadOnlyList<NewEntryValue> Values { get; init; } = [];

    /// <summary>The uploaded files of the form that the entry takes, none of them another entry's yet (ADR-0006).</summary>
    public IReadOnlyList<int> FileIds { get; init; } = [];
}

/// <summary>
/// Which entries count toward a form's entry limit (<c>availability.maxEntries</c>, ADR-0088): every one but test
/// entries, drafts, spam and rejected entries, so neither a preview nor a flood of spam can close a form. The same
/// entries hold places on options (ADR-0090).
/// </summary>
internal static class EntryLimits
{
    /// <summary>The states whose entries count, unless they are test entries.</summary>
    public static readonly IReadOnlyList<EntryState> CountingStates = [EntryState.Unverified, EntryState.Submitted, EntryState.Approved];

    /// <summary>Whether an entry in this state counts toward the limit.</summary>
    /// <param name="state">Its state.</param>
    /// <param name="isTest">Whether it came from the preview.</param>
    public static bool Counts(EntryState state, bool isTest) => !isTest && CountingStates.Contains(state);
}

/// <summary>A stored entry, without its values.</summary>
internal sealed record StoredEntry
{
    /// <summary>The row's id.</summary>
    public required long Id { get; init; }

    /// <summary>The key.</summary>
    public required Guid Key { get; init; }

    /// <summary>The form.</summary>
    public required int FormId { get; init; }

    /// <summary>The version it was submitted with.</summary>
    public required int FormVersionId { get; init; }

    /// <summary>Its number within the form, from 1.</summary>
    public required int Number { get; init; }

    /// <summary>Its state.</summary>
    public required EntryState State { get; init; }

    /// <summary>Whether it came from the preview.</summary>
    public required bool IsTest { get; init; }

    /// <summary>The submission id.</summary>
    public required Guid SubmissionId { get; init; }

    /// <summary>The SHA-256 of the submission in lowercase hex.</summary>
    public required string SubmissionHash { get; init; }

    /// <summary>The culture, or "".</summary>
    public required string Culture { get; init; }

    /// <summary>The content item.</summary>
    public required Guid? ContentKey { get; init; }

    /// <summary>The page's path.</summary>
    public required string? PageUrl { get; init; }

    /// <summary>The member.</summary>
    public required Guid? MemberKey { get; init; }

    /// <summary>The address, when kept.</summary>
    public required string? IpAddress { get; init; }

    /// <summary>The spam score.</summary>
    public required int SpamScore { get; init; }

    /// <summary>The spam reasons.</summary>
    public required string SpamReasons { get; init; }

    /// <summary>The search text.</summary>
    public required string SearchText { get; init; }

    /// <summary>The context values as JSON.</summary>
    public required string Context { get; init; }

    /// <summary>How long the visitor took.</summary>
    public required int? DurationSeconds { get; init; }

    /// <summary>When it was submitted.</summary>
    public required DateTimeOffset SubmittedUtc { get; init; }

    /// <summary>When it last changed.</summary>
    public required DateTimeOffset UpdatedUtc { get; init; }

    /// <summary>When its state last changed.</summary>
    public required DateTimeOffset StateChangedUtc { get; init; }

    /// <summary>Who last changed its state, or null for the visitor or the system.</summary>
    public required Guid? StateChangedByUserKey { get; init; }

    /// <summary>When the visitor first saw the form.</summary>
    public required DateTimeOffset? FirstSeenUtc { get; init; }

    /// <summary>When the email address was verified.</summary>
    public required DateTimeOffset? VerifiedUtc { get; init; }

    /// <summary>The hash of a draft's resume token.</summary>
    public required string? ResumeTokenHash { get; init; }

    /// <summary>When the resume link expires.</summary>
    public required DateTimeOffset? ResumeExpiresUtc { get; init; }

    /// <summary>The page a draft resumes on.</summary>
    public required Guid? CurrentPageKey { get; init; }

    /// <summary>The challenge that made it.</summary>
    public required string? ChallengeId { get; init; }

    /// <summary>Until when a legal hold keeps it (13.1).</summary>
    public required DateTimeOffset? HoldUntilUtc { get; init; }

    /// <summary>Why it is held.</summary>
    public required string? HoldReason { get; init; }

    /// <summary>When retention deletes it.</summary>
    public required DateTimeOffset? PurgeAfterUtc { get; init; }
}

/// <summary>An uploaded file to record before its entry exists (ADR-0006).</summary>
/// <param name="Key">The key the upload answers with.</param>
/// <param name="FormId">The form.</param>
/// <param name="FieldKey">The upload field's key.</param>
/// <param name="OriginalName">The name the visitor's file had, for downloads only.</param>
/// <param name="StoredName">The file store's name: 32 lowercase hex characters, never derived from the name.</param>
/// <param name="ContentType">The content type the server detected.</param>
/// <param name="SizeBytes">Its size.</param>
/// <param name="FileHash">The SHA-256 of its bytes in lowercase hex.</param>
/// <param name="ScanState">Where the malware scan is, such as <c>pending</c> (7.3, stored in camel case as every stored name).</param>
internal sealed record NewEntryFile(
    Guid Key,
    int FormId,
    Guid FieldKey,
    string OriginalName,
    string StoredName,
    string ContentType,
    long SizeBytes,
    string FileHash,
    string ScanState);

/// <summary>A stored file.</summary>
/// <param name="Id">The row's id, which values name.</param>
/// <param name="Key">Its key.</param>
/// <param name="EntryId">Its entry, or null while it waits for one.</param>
/// <param name="FormId">The form.</param>
/// <param name="FieldKey">The upload field's key.</param>
/// <param name="OriginalName">The visitor's file name.</param>
/// <param name="StoredName">The file store's name.</param>
/// <param name="ContentType">The content type.</param>
/// <param name="SizeBytes">Its size.</param>
/// <param name="FileHash">The SHA-256 of its bytes.</param>
/// <param name="ScanState">Where the malware scan is.</param>
/// <param name="CreatedUtc">When it was uploaded.</param>
internal sealed record StoredEntryFile(
    int Id,
    Guid Key,
    long? EntryId,
    int FormId,
    Guid FieldKey,
    string OriginalName,
    string StoredName,
    string ContentType,
    long SizeBytes,
    string FileHash,
    string ScanState,
    DateTimeOffset CreatedUtc);

/// <summary>
/// An option whose places are counted (section 4.4, ADR-0090): the choice field's key and the option's value.
/// </summary>
/// <param name="FieldKey">The field's key; a repeatable group's child has its own.</param>
/// <param name="Value">The option's value as entries store it: 1 to 200 characters.</param>
internal readonly record struct OptionPlace(Guid FieldKey, string Value);

/// <summary>
/// A value of an answer that must be unique in its form (<c>uniquePerForm</c>, ADR-0091), as entries are compared by it:
/// its number or its date in the column its storage kind fills, or else its text, compared by its key as the entries
/// screen filters (cleaned and folded, ADR-0005) and whole where the key is cut
/// (<see cref="EntryValueColumns.ComparedBy"/>).
/// </summary>
/// <param name="FieldKey">The field's key; a repeatable group's child has its own.</param>
/// <param name="Text">The stored text, for a value its column stores as text only; null otherwise.</param>
/// <param name="Number">The stored number, or null.</param>
/// <param name="Date">The stored date, in UTC, or null.</param>
internal readonly record struct AnswerValue(Guid FieldKey, string? Text, double? Number, DateTime? Date);

/// <summary>What forms hold that a permanent delete of them takes (ADR-0166).</summary>
/// <param name="Entries">Their entries, in any state.</param>
/// <param name="Files">Their uploaded files: those of entries, and those that wait for an entry.</param>
internal readonly record struct EntryHoldings(long Entries, long Files);

/// <summary>
/// The entries of <c>jfbEntry</c> with their values (<c>jfbEntryValue</c>) and files (<c>jfbEntryFile</c>), one aggregate
/// (ADR-0003, ADR-0038). Lists, filters and search are the entries query builder's (4.3d); the consent wordings the
/// values name are <see cref="IConsentTextRepository"/>'s.
/// </summary>
/// <remarks>
/// The places held on options (<c>jfbOptionCapacity</c>, ADR-0090) follow the entries: an option's count is the number
/// of the form's values at the field that equal the option's value and whose entry holds places (one that counts toward
/// the entry limit, <see cref="EntryLimits"/>). Every write here that changes such a value, or whether its entry holds
/// places, changes the counts in its own unit, after it has locked the form's row, so a form's places change one unit
/// at a time. No write refuses a change for an option without a place left: that is the caller's to decide.
/// </remarks>
internal interface IEntryRepository
{
    /// <summary>
    /// Stores an entry with its values and takes its files, and gives it the form's next number; an entry that holds
    /// places adds its values to the counts of the options they match (ADR-0090). The number comes from
    /// one update of the form's counter, the first write of the unit, so two submissions never share one, and a refused
    /// entry gives its number back. Null when the form does not exist. Refused with
    /// <see cref="FormBuilderConflict.EntryLimitReached"/>, <see cref="FormBuilderConflict.DuplicateSubmission"/>,
    /// <see cref="FormBuilderConflict.ChallengeUsed"/>, <see cref="FormBuilderConflict.DuplicateKey"/>,
    /// <see cref="FormBuilderConflict.MissingVersion"/>, <see cref="FormBuilderConflict.MissingField"/> or
    /// <see cref="FormBuilderConflict.MissingFile"/>.
    /// </summary>
    Task<StoredEntry?> CreateAsync(NewEntry entry, CancellationToken cancellationToken = default);

    /// <summary>
    /// The newest entries of the forms a user may view, outside the trash (ADR-0165): what has arrived, so not a draft, an
    /// entry waiting for its visitor's address, spam or a test.
    /// </summary>
    /// <param name="visibility">The forms the user may view entries of.</param>
    /// <param name="take">How many entries to answer with.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<IReadOnlyList<StoredEntry>> ListRecentAsync(FormVisibility visibility, int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// How many entries each of these forms received since a moment (ADR-0166): those the recent list holds, so no draft,
    /// entry waiting for its visitor's address, spam or test. A form without any is left out.
    /// </summary>
    /// <param name="formIds">The forms, any number.</param>
    /// <param name="since">From when, inclusive.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<IReadOnlyDictionary<int, long>> CountSinceAsync(IReadOnlyCollection<int> formIds, DateTimeOffset since, CancellationToken cancellationToken = default);

    /// <summary>
    /// Which of these forms have an entry nobody has opened yet (ADR-0166): one the recent list holds whose first view is
    /// not on record.
    /// </summary>
    /// <param name="formIds">The forms, any number.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<IReadOnlySet<int>> ListFormIdsWithUnseenAsync(IReadOnlyCollection<int> formIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// How many entries, in any state, and uploaded files, with an entry or waiting for one, these forms hold (ADR-0166): what
    /// a permanent delete of them takes, as its confirmation says.
    /// </summary>
    /// <param name="formIds">The forms, any number.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<EntryHoldings> CountHeldAsync(IReadOnlyCollection<int> formIds, CancellationToken cancellationToken = default);

    /// <summary>How many of a form's entries count toward its entry limit (<see cref="EntryLimits"/>, ADR-0088).</summary>
    /// <param name="formId">The form.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<int> CountTowardsLimitAsync(int formId, CancellationToken cancellationToken = default);

    /// <summary>
    /// How many places are held on each of these options of a form (ADR-0090): the option's count, or, for an option that
    /// has none yet, the number of the form's values at the field that equal its value and whose entry holds places.
    /// Reads only, so an option without a count gets none here.
    /// </summary>
    /// <param name="formId">The form.</param>
    /// <param name="options">The options.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <exception cref="ArgumentException">An option without a field key, or whose value is empty, longer than 200 characters or holds a lone surrogate.</exception>
    Task<IReadOnlyDictionary<OptionPlace, int>> GetPlacesTakenAsync(int formId, IReadOnlyCollection<OptionPlace> options, CancellationToken cancellationToken = default);

    /// <summary>
    /// Makes a count of each of these options of a form that has none yet, holding the places held on it then, and
    /// answers every option's count (ADR-0090); from then on the writes here keep it. Inside a unit that locked the
    /// form's row, a submission's, the counts hold until the unit ends. A form that does not exist has no counts.
    /// </summary>
    /// <param name="formId">The form.</param>
    /// <param name="options">The options.</param>
    /// <param name="cancellationToken">Cancels the unit of work.</param>
    /// <exception cref="ArgumentException">An option without a field key, or whose value is empty, longer than 200 characters or holds a lone surrogate.</exception>
    Task<IReadOnlyDictionary<OptionPlace, int>> CountPlacesAsync(int formId, IReadOnlyCollection<OptionPlace> options, CancellationToken cancellationToken = default);

    /// <summary>
    /// The values of these that an entry of the form holds at their field, of the entries that hold their answers: those
    /// that count toward the entry limit (<see cref="EntryLimits"/>, ADR-0091). Reads only, without locks: what a
    /// submission learns while its answers are read; its unit asks again with <see cref="FindTakenAnswersAsync"/>.
    /// </summary>
    /// <param name="formId">The form.</param>
    /// <param name="values">The values.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <exception cref="ArgumentException">A value without a field key, or with none or more than one of a text, a number and a date.</exception>
    Task<IReadOnlySet<AnswerValue>> GetTakenAnswersAsync(int formId, IReadOnlyCollection<AnswerValue> values, CancellationToken cancellationToken = default);

    /// <summary>
    /// The values of these that another entry of this entry's form holds at their field, of the entries that hold their
    /// answers (ADR-0091), in a unit that has locked the form's row first and reads with locks: inside a submission's
    /// unit, which stored the entry, the answer holds until the unit ends. None for an entry that does not exist.
    /// </summary>
    /// <param name="entryId">The entry the values are of, which is left out.</param>
    /// <param name="values">The values.</param>
    /// <param name="cancellationToken">Cancels the unit of work.</param>
    /// <exception cref="ArgumentException">A value without a field key, or with none or more than one of a text, a number and a date.</exception>
    Task<IReadOnlySet<AnswerValue>> FindTakenAnswersAsync(long entryId, IReadOnlyCollection<AnswerValue> values, CancellationToken cancellationToken = default);

    /// <summary>The entry with this key, or null.</summary>
    Task<StoredEntry?> GetAsync(Guid key, CancellationToken cancellationToken = default);

    /// <summary>The entry with this id, or null.</summary>
    Task<StoredEntry?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>The form's entry with this number, or null.</summary>
    Task<StoredEntry?> GetByNumberAsync(int formId, int number, CancellationToken cancellationToken = default);

    /// <summary>The form's entry with this submission id, or null: what answers a retry (section 4.4).</summary>
    Task<StoredEntry?> GetBySubmissionAsync(int formId, Guid submissionId, CancellationToken cancellationToken = default);

    /// <summary>The draft with this hash of its resume token, or null.</summary>
    /// <param name="tokenHash">The SHA-256 of the token in lowercase hex.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<StoredEntry?> GetByResumeTokenAsync(string tokenHash, CancellationToken cancellationToken = default);

    /// <summary>The form's entry a challenge made, or null (ADR-0012).</summary>
    /// <param name="formId">The form.</param>
    /// <param name="challengeId">The challenge's id as an entry stores it.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<StoredEntry?> GetByChallengeAsync(int formId, string challengeId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The values of these entries, by entry, field, row and item. The ids go into statements of at most
    /// <see cref="SqlArgs.MaxListLength"/> (ADR-0003).
    /// </summary>
    Task<IReadOnlyList<StoredEntryValue>> ListValuesAsync(IReadOnlyCollection<long> entryIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves an entry from the state the user saw to another; null when it does not exist. Refused with
    /// <see cref="FormBuilderConcurrencyException"/> when it is no longer in <paramref name="expected"/>. A move to the
    /// state it is in changes nothing. A move out of the states that hold places takes the entry's values from the
    /// counts of the options they match, and a move back adds them again, also to an option without a place left
    /// (ADR-0090).
    /// </summary>
    Task<StoredEntry?> ChangeStateAsync(long entryId, EntryState expected, EntryState state, Guid? userKey, CancellationToken cancellationToken = default);

    /// <summary>Records that an entry's address is confirmed, once; false when it was confirmed already (ADR-0162).</summary>
    Task<bool> MarkVerifiedAsync(long entryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the values of some fields of an entry, and its search text; null when it does not exist. Every value
    /// names one of <paramref name="fieldIds"/>, and a file value one of the entry's files. Refused with
    /// <see cref="FormBuilderConflict.MissingField"/> or <see cref="FormBuilderConflict.MissingFile"/>. For an entry that
    /// holds places, the counts of the options follow its values (ADR-0090).
    /// </summary>
    Task<StoredEntry?> ReplaceValuesAsync(long entryId, IReadOnlyCollection<int> fieldIds, IReadOnlyList<NewEntryValue> values, string searchText, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an entry with its values, files, events and notes, and gives back the places it held (ADR-0090). True when
    /// it existed. The file store's bytes are the service's to delete (<see cref="ListFilesAsync(long, CancellationToken)"/>
    /// first, ADR-0006).
    /// </summary>
    Task<bool> DeleteAsync(long entryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The ids of up to <paramref name="take"/> (at most <see cref="SqlArgs.MaxListLength"/>) entries of a form,
    /// lowest number first: a batch of the form's permanent delete (ADR-0047).
    /// </summary>
    Task<IReadOnlyList<long>> ListIdsAsync(int formId, int take, CancellationToken cancellationToken = default);

    /// <summary>The files of these entries, by entry and in the order they were uploaded.</summary>
    Task<IReadOnlyList<StoredEntryFile>> ListFilesAsync(IReadOnlyCollection<long> entryIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes these entries with their values, files, events, notes and action runs, and gives back the places they held
    /// (ADR-0090); how many existed. The file store's bytes are the service's to delete
    /// (<see cref="ListFilesAsync(IReadOnlyCollection{long}, CancellationToken)"/> first).
    /// </summary>
    Task<int> DeleteAsync(IReadOnlyCollection<long> entryIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Up to <paramref name="take"/> (at most <see cref="SqlArgs.MaxListLength"/>) uploads of a form that wait
    /// for an entry, oldest first: the next ones the form's permanent delete removes (ADR-0047).
    /// </summary>
    Task<IReadOnlyList<StoredEntryFile>> ListUploadsAsync(int formId, int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records an upload that waits for its entry; null when the form does not exist. Refused with
    /// <see cref="FormBuilderConflict.DuplicateKey"/>.
    /// </summary>
    Task<StoredEntryFile?> AddFileAsync(NewEntryFile file, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a file's malware scan from the state it was seen in to another (7.3); null when the file does not exist.
    /// Refused with <see cref="FormBuilderConcurrencyException"/> when the scan is no longer in <paramref name="expected"/>.
    /// </summary>
    Task<StoredEntryFile?> ChangeScanStateAsync(int fileId, string expected, string scanState, CancellationToken cancellationToken = default);

    /// <summary>The file with this key, or null.</summary>
    Task<StoredEntryFile?> GetFileAsync(Guid key, CancellationToken cancellationToken = default);

    /// <summary>The files of an entry, in the order they were uploaded.</summary>
    Task<IReadOnlyList<StoredEntryFile>> ListFilesAsync(long entryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// A form's files for an archive after the file <paramref name="afterId"/>, in the order they came (ADR-0145): those of its
    /// entries that count (<see cref="EntryLimits.CountingStates"/>), not Spam, drafts or test entries.
    /// </summary>
    /// <param name="formId">The form.</param>
    /// <param name="afterId">The id to go on after; 0 from the first.</param>
    /// <param name="take">How many, 1 to <see cref="EntryRepository.MaxOrphansPerList"/>.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<IReadOnlyList<StoredEntryFile>> ListFilesToArchiveAsync(int formId, int afterId, int take, CancellationToken cancellationToken = default);

    /// <summary>The files an entry holds that wait for a scan after the file <paramref name="afterId"/>, in the order they came (ADR-0144, ADR-0148).</summary>
    /// <param name="afterId">The id to go on after; 0 from the first.</param>
    /// <param name="take">How many, 1 to <see cref="EntryRepository.MaxOrphansPerList"/>.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<IReadOnlyList<StoredEntryFile>> ListEntryFilesToScanAsync(int afterId, int take, CancellationToken cancellationToken = default);

    /// <summary>The uploads no entry took yet that wait for a scan after the file <paramref name="afterId"/>, in the order they came (ADR-0144, ADR-0148).</summary>
    /// <param name="afterId">The id to go on after; 0 from the first.</param>
    /// <param name="take">How many, 1 to <see cref="EntryRepository.MaxOrphansPerList"/>.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<IReadOnlyList<StoredEntryFile>> ListUploadsToScanAsync(int afterId, int take, CancellationToken cancellationToken = default);

    /// <summary>The bytes of all the uploads no entry took yet (ADR-0148).</summary>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<long> SumWaitingBytesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Files that no entry took and that were uploaded before <paramref name="createdBefore"/>, oldest first: what the
    /// cleanup removes (ADR-0006).
    /// </summary>
    Task<IReadOnlyList<StoredEntryFile>> ListOrphanFilesAsync(DateTimeOffset createdBefore, int take, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a file's row while it still belongs to <paramref name="entryId"/>, or to no entry when that is null (the
    /// orphan cleanup). True when it existed. Refused with <see cref="FormBuilderConcurrencyException"/> when it belongs
    /// to another entry by now, and with <see cref="FormBuilderConflict.InUse"/> while a value names it.
    /// </summary>
    Task<bool> DeleteFileAsync(int fileId, long? entryId, CancellationToken cancellationToken = default);
}
