using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Dtos;
using Umbraco.Cms.Infrastructure.Persistence;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

/// <inheritdoc />
/// <remarks>
/// An entry is stored in one unit of work (ADR-0003, ADR-0038). The form's counter comes first, so the unit holds the
/// form's row before it does anything else and two submissions never share a number. Then the entry, as one insert
/// whose conditions are its version, its submission id, its challenge, its key and its resume token; then its files,
/// the check of its fields and its values. A refusal anywhere rolls the whole unit back, the number included. A service
/// that stores more in the same unit (the timeline event, the action runs, ADR-0007) calls this first, so the counter
/// stays the unit's first write.
/// </remarks>
internal sealed partial class EntryRepository(FormBuilderDatabase database) : IEntryRepository
{
    /// <summary>The most ids one statement names; SQL Server takes at most 2,100 parameters (ADR-0003).</summary>
    public const int IdsPerStatement = 1000;

    /// <summary>The longest culture the column holds.</summary>
    public const int MaxCultureLength = 32;

    /// <summary>The longest page path the column holds.</summary>
    public const int MaxPageUrlLength = 2048;

    /// <summary>The longest address the column holds.</summary>
    public const int MaxIpAddressLength = 64;

    /// <summary>The longest spam reasons the column holds.</summary>
    public const int MaxSpamReasonsLength = 1000;

    /// <inheritdoc />
    public Task<StoredEntry?> CreateAsync(NewEntry entry, CancellationToken cancellationToken = default)
    {
        CheckEntry(entry);
        return database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var forms = n.T(FormBuilderTables.Form);
                var table = n.T(FormBuilderTables.Entry);

                // The first write of the unit: the form's next number, which no other unit can take until this one ends. It
                // never falls behind the numbers the form's entries hold, even when rows came in another way (ADR-0038).
                var highest = $"COALESCE((SELECT MAX(e.{n.C("number")}) FROM {table} e WHERE e.{n.C("formId")} = @0), 0)";
                var counted = await db.ExecAsync(
                    $"UPDATE {forms} SET {n.C("lastEntryNumber")} = CASE WHEN {highest} > {n.C("lastEntryNumber")} THEN {highest} + 1 ELSE {n.C("lastEntryNumber")} + 1 END WHERE {n.C("id")} = @0",
                    entry.FormId);
                if (counted == 0)
                {
                    return null;
                }

                // One check per unique index, so each is a seek. IS NOT NULL lets SQL Server use the filtered indexes.
                var free = $"NOT EXISTS (SELECT 1 FROM {table}{n.InsertOnceHint} WHERE";
                var challenge = entry.ChallengeId is null ? string.Empty : $" AND {free} {n.C("formId")} = @1 AND {n.C("challengeId")} = @23 AND {n.C("challengeId")} IS NOT NULL)";
                var resume = entry.ResumeTokenHash is null ? string.Empty : $" AND {free} {n.C("resumeTokenHash")} = @20 AND {n.C("resumeTokenHash")} IS NOT NULL)";
                int inserted;
                try
                {
                    inserted = await db.ExecAsync(
                        $"""
                        INSERT INTO {table} ({n.C("key")}, {n.C("formId")}, {n.C("formVersionId")}, {n.C("number")}, {n.C("state")}, {n.C("isTest")}, {n.C("submissionId")}, {n.C("digest")}, {n.C("culture")}, {n.C("contentKey")}, {n.C("pageUrl")}, {n.C("memberKey")}, {n.C("ipAddress")}, {n.C("spamScore")}, {n.C("spamReasons")},
                        {n.C("searchText")}, {n.C("context")}, {n.C("durationSeconds")}, {n.C("submittedUtc")}, {n.C("updatedUtc")}, {n.C("stateChangedUtc")}, {n.C("stateChangedByUserKey")}, {n.C("firstSeenUtc")}, {n.C("verifiedUtc")}, {n.C("resumeTokenHash")}, {n.C("resumeExpiresUtc")}, {n.C("currentPageKey")}, {n.C("challengeId")}, {n.C("holdUntilUtc")}, {n.C("holdReason")}, {n.C("purgeAfterUtc")})
                        SELECT @0, @1, @2, (SELECT f.{n.C("lastEntryNumber")} FROM {forms} f WHERE f.{n.C("id")} = @1), @3, @4, @5, @6, @7, @8, @9, @10, @11, @12, @13, @14, @15, @16, @17, @17, @17, NULL, @18, @19, @20, @21, @22, @23, NULL, NULL, @24
                        WHERE EXISTS (SELECT 1 FROM {n.T(FormBuilderTables.FormVersion)} v{n.LockingRead} WHERE v.{n.C("id")} = @2 AND v.{n.C("formId")} = @1)
                        AND {free} {n.C("formId")} = @1 AND {n.C("submissionId")} = @5)
                        AND {free} {n.C("key")} = @0){challenge}{resume}
                        """,
                        entry.Key,
                        entry.FormId,
                        entry.FormVersionId,
                        entry.State.ToString(),
                        entry.IsTest,
                        entry.SubmissionId,
                        entry.Digest,
                        entry.Culture,
                        entry.ContentKey,
                        entry.PageUrl,
                        entry.MemberKey,
                        entry.IpAddress,
                        entry.SpamScore,
                        entry.SpamReasons,
                        entry.SearchText,
                        entry.Context,
                        entry.DurationSeconds,
                        database.UtcNow,
                        FormBuilderDbTime.Write(entry.FirstSeenUtc),
                        FormBuilderDbTime.Write(entry.VerifiedUtc),
                        entry.ResumeTokenHash,
                        FormBuilderDbTime.Write(entry.ResumeExpiresUtc),
                        entry.CurrentPageKey,
                        entry.ChallengeId,
                        FormBuilderDbTime.Write(entry.PurgeAfterUtc));
                }
                catch (Exception exception) when (FormBuilderDatabaseErrors.IsUniqueViolation(exception))
                {
                    // The backstop for a writer the database let through at the same moment (ADR-0036): say which.
                    var refused = await RefusedCreateAsync(db, n, entry);
                    if (refused is null)
                    {
                        throw;
                    }

                    throw new FormBuilderConflictException(refused.Conflict, refused.Message, exception);
                }

                if (inserted == 0)
                {
                    throw (Exception?)await RefusedCreateAsync(db, n, entry)
                        ?? new InvalidOperationException("The entry was not stored, and no condition of the insert explains it.");
                }

                var entryId = await db.ScalarAsync<long>($"SELECT {n.C("id")} FROM {table} WHERE {n.C("key")} = @0", entry.Key);
                await TakeFilesAsync(db, n, entryId, entry.FormId, entry.FileIds);
                await CheckFieldsAsync(db, n, entry.FormId, entry.Values.Select(value => value.FieldId).Distinct().ToArray());
                await InsertValuesAsync(db, n, entryId, entry.Values);
                return await FindAsync(db, n, $"{n.C("id")} = @0", entryId);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<StoredEntry?> GetAsync(Guid key, CancellationToken cancellationToken = default) =>
        database.ReadAsync(
            db =>
            {
                var n = new SqlNames(db);
                return FindAsync(db, n, $"{n.C("key")} = @0", key);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<StoredEntry?> GetByIdAsync(long id, CancellationToken cancellationToken = default) =>
        database.ReadAsync(
            db =>
            {
                var n = new SqlNames(db);
                return FindAsync(db, n, $"{n.C("id")} = @0", id);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<StoredEntry?> GetByNumberAsync(int formId, int number, CancellationToken cancellationToken = default) =>
        database.ReadAsync(
            db =>
            {
                var n = new SqlNames(db);
                return FindAsync(db, n, $"{n.C("formId")} = @0 AND {n.C("number")} = @1", formId, number);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<StoredEntry?> GetBySubmissionAsync(int formId, Guid submissionId, CancellationToken cancellationToken = default) =>
        database.ReadAsync(
            db =>
            {
                var n = new SqlNames(db);
                return FindAsync(db, n, $"{n.C("formId")} = @0 AND {n.C("submissionId")} = @1", formId, submissionId);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<StoredEntry?> ChangeStateAsync(long entryId, EntryState expected, EntryState state, Guid? userKey, CancellationToken cancellationToken = default)
    {
        CheckState(expected, nameof(expected));
        CheckState(state, nameof(state));
        if (expected == state)
        {
            // Nothing to change, so nothing to write: the entry as it is, while it is still in the state the user saw.
            return database.ReadAsync(
                async db =>
                {
                    var n = new SqlNames(db);
                    var current = await FindAsync(db, n, $"{n.C("id")} = @0", entryId);
                    CheckState(current, expected);
                    return current;
                },
                cancellationToken);
        }

        return database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var changed = await db.ExecAsync(
                    $"UPDATE {n.T(FormBuilderTables.Entry)} SET {n.C("state")} = @0, {n.C("stateChangedUtc")} = @1, {n.C("updatedUtc")} = @1, {n.C("stateChangedByUserKey")} = @2 WHERE {n.C("id")} = @3 AND {n.C("state")} = @4",
                    state.ToString(),
                    database.UtcNow,
                    userKey,
                    entryId,
                    expected.ToString());
                var current = await FindAsync(db, n, $"{n.C("id")} = @0", entryId);
                if (changed == 0 && current is not null)
                {
                    // Not in the state the user saw when the update ran, whatever it is in now: it may have come back since.
                    throw new FormBuilderConcurrencyException($"The entry was changed by someone else meanwhile (it was no longer {expected}).");
                }

                return current;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The database's cascades remove the values, files, events, notes and action runs; queued emails keep their row
    /// without the link (<c>FK_jfbEmail_jfbEntry</c> sets it to null), so the service deletes those first (section 4.4).
    /// </remarks>
    public Task<bool> DeleteAsync(long entryId, CancellationToken cancellationToken = default) =>
        database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                return await db.ExecAsync($"DELETE FROM {n.T(FormBuilderTables.Entry)} WHERE {n.C("id")} = @0", entryId) == 1;
            },
            cancellationToken);

    internal static StoredEntry FromDto(EntryDto d) => new()
    {
        Id = d.Id,
        Key = d.Key,
        FormId = d.FormId,
        FormVersionId = d.FormVersionId,
        Number = d.Number,
        State = Enum.Parse<EntryState>(d.State),
        IsTest = d.IsTest,
        SubmissionId = d.SubmissionId,
        Digest = d.Digest,
        Culture = d.Culture,
        ContentKey = d.ContentKey,
        PageUrl = d.PageUrl,
        MemberKey = d.MemberKey,
        IpAddress = d.IpAddress,
        SpamScore = d.SpamScore,
        SpamReasons = d.SpamReasons,
        SearchText = d.SearchText,
        Context = d.Context,
        DurationSeconds = d.DurationSeconds,
        SubmittedUtc = FormBuilderDbTime.Read(d.SubmittedUtc),
        UpdatedUtc = FormBuilderDbTime.Read(d.UpdatedUtc),
        StateChangedUtc = FormBuilderDbTime.Read(d.StateChangedUtc),
        StateChangedByUserKey = d.StateChangedByUserKey,
        FirstSeenUtc = FormBuilderDbTime.Read(d.FirstSeenUtc),
        VerifiedUtc = FormBuilderDbTime.Read(d.VerifiedUtc),
        ResumeTokenHash = d.ResumeTokenHash,
        ResumeExpiresUtc = FormBuilderDbTime.Read(d.ResumeExpiresUtc),
        CurrentPageKey = d.CurrentPageKey,
        ChallengeId = d.ChallengeId,
        HoldUntilUtc = FormBuilderDbTime.Read(d.HoldUntilUtc),
        HoldReason = d.HoldReason,
        PurgeAfterUtc = FormBuilderDbTime.Read(d.PurgeAfterUtc),
    };

    /// <summary>
    /// The first write of a unit that adds an event or a note to an entry: it changes nothing, but holds the entry's row until the unit ends,
    /// so the entry cannot go away before the new row's foreign key is checked. False when the entry does not exist.
    /// </summary>
    internal static async Task<bool> HoldAsync(IUmbracoDatabase db, SqlNames n, long entryId) =>
        await db.ExecAsync(
            $"UPDATE {n.T(FormBuilderTables.Entry)} SET {n.C("updatedUtc")} = {n.C("updatedUtc")} WHERE {n.C("id")} = @0",
            entryId) == 1;

    /// <summary>
    /// Whether <paramref name="url"/> is a site path only, as ADR-0009 keeps it: one slash first (two, or a slash and a
    /// backslash, name another host to a browser), and no query or fragment, which may carry personal data.
    /// </summary>
    internal static bool IsPath(string url) =>
        url.StartsWith('/') && !(url.Length > 1 && url[1] is '/' or '\\') && url.AsSpan().IndexOfAny('?', '#') < 0;

    /// <summary>Whether <paramref name="value"/> is <paramref name="length"/> lowercase hex characters.</summary>
    internal static bool IsLowerHex(string value, int length) =>
        value.Length == length && value.All(c => char.IsAsciiDigit(c) || c is >= 'a' and <= 'f');

    /// <summary>The parameter list <c>@first, @first+1, ...</c> of <paramref name="count"/> parameters.</summary>
    private static string Parameters(int first, int count) =>
        string.Join(", ", Enumerable.Range(first, count).Select(i => "@" + i.ToString(CultureInfo.InvariantCulture)));

    private static async Task<StoredEntry?> FindAsync(IUmbracoDatabase db, SqlNames n, string where, params object?[] args)
    {
        var row = await db.FirstOrNullAsync<EntryDto>($"SELECT * FROM {n.T(FormBuilderTables.Entry)} WHERE {where}", args);
        return row is null ? null : FromDto(row);
    }

    /// <summary>Why the insert of an entry was refused, or null when none of its conditions explains it.</summary>
    private static async Task<FormBuilderConflictException?> RefusedCreateAsync(IUmbracoDatabase db, SqlNames n, NewEntry entry)
    {
        var table = n.T(FormBuilderTables.Entry);
        async Task<bool> AnyAsync(string from, string where, params object?[] args) =>
            await db.ScalarAsync<int>($"SELECT COUNT(*) FROM {from} WHERE {where}", args) > 0;

        if (!await AnyAsync(n.T(FormBuilderTables.FormVersion), $"{n.C("id")} = @0 AND {n.C("formId")} = @1", entry.FormVersionId, entry.FormId))
        {
            return new FormBuilderConflictException(FormBuilderConflict.MissingVersion, "The form version does not exist or belongs to another form.");
        }

        if (await AnyAsync(table, $"{n.C("formId")} = @0 AND {n.C("submissionId")} = @1", entry.FormId, entry.SubmissionId))
        {
            return new FormBuilderConflictException(FormBuilderConflict.DuplicateSubmission, "The form already has an entry with this submission id.");
        }

        if (entry.ChallengeId is not null && await AnyAsync(table, $"{n.C("formId")} = @0 AND {n.C("challengeId")} = @1", entry.FormId, entry.ChallengeId))
        {
            return new FormBuilderConflictException(FormBuilderConflict.ChallengeUsed, "The challenge already made an entry of this form.");
        }

        if (await AnyAsync(table, $"{n.C("key")} = @0", entry.Key))
        {
            return new FormBuilderConflictException(FormBuilderConflict.DuplicateKey, "Another entry already has this key.");
        }

        return entry.ResumeTokenHash is not null && await AnyAsync(table, $"{n.C("resumeTokenHash")} = @0", entry.ResumeTokenHash)
            ? new FormBuilderConflictException(FormBuilderConflict.DuplicateKey, "Another entry already has this resume token.")
            : null;
    }

    private static void CheckState(StoredEntry? current, EntryState expected)
    {
        if (current is not null && current.State != expected)
        {
            throw new FormBuilderConcurrencyException($"The entry was changed by someone else meanwhile (it is {current.State}, not {expected}).");
        }
    }

    private static void CheckState(EntryState state, string parameter)
    {
        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(parameter, state, "Not an entry state.");
        }
    }

    private static void CheckEntry(NewEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        CheckState(entry.State, nameof(entry));
        Require(entry.Digest is not null && IsLowerHex(entry.Digest, 64), "An entry's digest is 64 lowercase hex characters.");
        Require(entry.Culture is not null && entry.Culture.Length <= MaxCultureLength, $"An entry's culture holds at most {MaxCultureLength} characters.");
        Require(
            entry.PageUrl is null || (entry.PageUrl.Length <= MaxPageUrlLength && IsPath(entry.PageUrl)),
            $"An entry's page is a site path of 1 to {MaxPageUrlLength} characters, without a host, a query or a fragment.");
        Require(entry.IpAddress is null || entry.IpAddress.Length is > 0 and <= MaxIpAddressLength, $"An entry's address holds 1 to {MaxIpAddressLength} characters.");
        Require(entry.SpamScore >= 0, "An entry's spam score is 0 or more.");
        Require(entry.SpamReasons is not null && entry.SpamReasons.Length <= MaxSpamReasonsLength, $"An entry's spam reasons hold at most {MaxSpamReasonsLength} characters.");
        Require(entry.SearchText is not null, "An entry's search text is not null.");
        Require(entry.Context is not null && JsonObjectText.IsObject(entry.Context), "An entry's context is a JSON object.");
        Require(entry.DurationSeconds is null or >= 0, "An entry's duration is 0 seconds or more.");
        Require(entry.ResumeTokenHash is null || IsLowerHex(entry.ResumeTokenHash, 64), "An entry's resume token hash is 64 lowercase hex characters.");
        Require(entry.ChallengeId is null || IsLowerHex(entry.ChallengeId, 32), "An entry's challenge id is 32 lowercase hex characters.");
        if (entry.Values is null || entry.FileIds is null)
        {
            throw new ArgumentException("An entry's values and files are not null.", nameof(entry));
        }

        Require(entry.FileIds.All(id => id > 0) && entry.FileIds.Distinct().Count() == entry.FileIds.Count, "An entry's files are distinct file ids.");
        CheckValues(entry.Values, nameof(entry));
        Require(entry.Values.All(value => value.FileId is not { } file || entry.FileIds.Contains(file)), "A file value names one of the entry's files.");

        static void Require(bool condition, string message)
        {
            if (!condition)
            {
                throw new ArgumentException(message, nameof(entry));
            }
        }
    }

}
