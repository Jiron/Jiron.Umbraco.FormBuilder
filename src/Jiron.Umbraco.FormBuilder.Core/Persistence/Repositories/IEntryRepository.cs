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
/// (5.1); <paramref name="Text"/> keeps the value as submitted. A value that is text only gets a key for filters and
/// sorts (<see cref="EntryTextKey"/>).
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
    public required string Digest { get; init; }

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

    /// <summary>The values.</summary>
    public IReadOnlyList<NewEntryValue> Values { get; init; } = [];

    /// <summary>The uploaded files of the form that the entry takes, none of them another entry's yet (ADR-0006).</summary>
    public IReadOnlyList<int> FileIds { get; init; } = [];
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

    /// <summary>The submission's digest.</summary>
    public required string Digest { get; init; }

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
/// <param name="Sha256">The SHA-256 of its bytes in lowercase hex.</param>
/// <param name="ScanState">Where the malware scan is, such as <c>Pending</c> (7.3).</param>
internal sealed record NewEntryFile(
    Guid Key,
    int FormId,
    Guid FieldKey,
    string OriginalName,
    string StoredName,
    string ContentType,
    long SizeBytes,
    string Sha256,
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
/// <param name="Sha256">The SHA-256 of its bytes.</param>
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
    string Sha256,
    string ScanState,
    DateTimeOffset CreatedUtc);

/// <summary>The exact wording of a consent as shown, named by its hash (ADR-0009).</summary>
/// <param name="Hash">The SHA-256 of the culture and the text in lowercase hex.</param>
/// <param name="Culture">The culture, or "" for the invariant culture.</param>
/// <param name="Text">The wording.</param>
/// <param name="FirstSeenUtc">When it was first stored.</param>
internal sealed record StoredConsentText(string Hash, string Culture, string Text, DateTimeOffset FirstSeenUtc);

/// <summary>
/// The entries of <c>jfbEntry</c> with their values (<c>jfbEntryValue</c>), files (<c>jfbEntryFile</c>) and the consent
/// wordings they gave (<c>jfbConsentText</c>), one aggregate (ADR-0003, ADR-0038). Lists, filters and search are the
/// entries query builder's (4.3d).
/// </summary>
internal interface IEntryRepository
{
    /// <summary>
    /// Stores an entry with its values and takes its files, and gives it the form's next number. The number comes from
    /// one update of the form's counter, the first write of the unit, so two submissions never share one, and a refused
    /// entry gives its number back. Null when the form does not exist. Refused with
    /// <see cref="FormBuilderConflict.DuplicateSubmission"/>, <see cref="FormBuilderConflict.ChallengeUsed"/>,
    /// <see cref="FormBuilderConflict.DuplicateKey"/>, <see cref="FormBuilderConflict.MissingVersion"/>,
    /// <see cref="FormBuilderConflict.MissingField"/> or <see cref="FormBuilderConflict.MissingFile"/>.
    /// </summary>
    Task<StoredEntry?> CreateAsync(NewEntry entry, CancellationToken cancellationToken = default);

    /// <summary>The entry with this key, or null.</summary>
    Task<StoredEntry?> GetAsync(Guid key, CancellationToken cancellationToken = default);

    /// <summary>The entry with this id, or null.</summary>
    Task<StoredEntry?> GetByIdAsync(long id, CancellationToken cancellationToken = default);

    /// <summary>The form's entry with this number, or null.</summary>
    Task<StoredEntry?> GetByNumberAsync(int formId, int number, CancellationToken cancellationToken = default);

    /// <summary>The form's entry with this submission id, or null: what answers a retry (section 4.4).</summary>
    Task<StoredEntry?> GetBySubmissionAsync(int formId, Guid submissionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The values of these entries, by entry, field, row and item. The ids go into statements of at most
    /// <see cref="EntryRepository.IdsPerStatement"/> (ADR-0003).
    /// </summary>
    Task<IReadOnlyList<StoredEntryValue>> ListValuesAsync(IReadOnlyCollection<long> entryIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves an entry from the state the user saw to another; null when it does not exist. Refused with
    /// <see cref="FormBuilderConcurrencyException"/> when it is no longer in <paramref name="expected"/>. A move to the
    /// state it is in changes nothing.
    /// </summary>
    Task<StoredEntry?> ChangeStateAsync(long entryId, EntryState expected, EntryState state, Guid? userKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces the values of some fields of an entry, and its search text; null when it does not exist. Every value
    /// names one of <paramref name="fieldIds"/>, and a file value one of the entry's files. Refused with
    /// <see cref="FormBuilderConflict.MissingField"/> or <see cref="FormBuilderConflict.MissingFile"/>.
    /// </summary>
    Task<StoredEntry?> ReplaceValuesAsync(long entryId, IReadOnlyCollection<int> fieldIds, IReadOnlyList<NewEntryValue> values, string searchText, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an entry with its values, files, events and notes. True when it existed. The file store's bytes are the
    /// service's to delete (<see cref="ListFilesAsync(long, CancellationToken)"/> first, ADR-0006).
    /// </summary>
    Task<bool> DeleteAsync(long entryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The ids of up to <paramref name="take"/> (at most <see cref="EntryRepository.IdsPerStatement"/>) entries of a form,
    /// lowest number first: a batch of the form's permanent delete (ADR-0047).
    /// </summary>
    Task<IReadOnlyList<long>> ListIdsAsync(int formId, int take, CancellationToken cancellationToken = default);

    /// <summary>The files of these entries, by entry and in the order they were uploaded.</summary>
    Task<IReadOnlyList<StoredEntryFile>> ListFilesAsync(IReadOnlyCollection<long> entryIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes these entries with their values, files, events, notes and action runs; how many existed. The file store's
    /// bytes are the service's to delete (<see cref="ListFilesAsync(IReadOnlyCollection{long}, CancellationToken)"/> first).
    /// </summary>
    Task<int> DeleteAsync(IReadOnlyCollection<long> entryIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Up to <paramref name="take"/> (at most <see cref="EntryRepository.IdsPerStatement"/>) uploads of a form that wait
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

    /// <summary>
    /// Stores a consent's wording once, and returns its hash (<see cref="ConsentTextHash"/>): what the consent value
    /// stores. A wording stored before is left as it is, so the first time it was seen stays.
    /// </summary>
    Task<string> AddConsentTextAsync(string culture, string text, CancellationToken cancellationToken = default);

    /// <summary>The consent wording with this hash, or null.</summary>
    Task<StoredConsentText?> GetConsentTextAsync(string hash, CancellationToken cancellationToken = default);
}
