using NPoco;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

/// <summary>A mail whose recipients may name a person, as an erase checks them (ADR-0223).</summary>
/// <param name="Id">The mail.</param>
/// <param name="EntryId">The entry it is about, or none.</param>
/// <param name="Addresses">Its recipients and reply-to address, as stored: lists split on commas.</param>
internal sealed record AddressedMail(long Id, long? EntryId, IReadOnlyList<string> Addresses);

/// <summary>An event whose details may name a person, as an erase checks them (ADR-0223).</summary>
/// <param name="Id">The event.</param>
/// <param name="Details">Its details.</param>
internal sealed record MentioningEvent(long Id, string Details);

/// <summary>How many files and mails an entry has.</summary>
/// <param name="Files">Its uploaded files.</param>
/// <param name="Emails">Its mail, sent or not.</param>
internal readonly record struct EntryCopies(int Files, int Emails);

/// <summary>
/// What the privacy tools read and write beyond an entry's own repository (ADR-0223): the entries that name a person, the
/// mail and events that mention their address, and what anonymising takes from an entry besides its answers and files.
/// </summary>
internal interface IPersonDataRepository
{
    /// <summary>The entries with a value of an email field whose folded text is <paramref name="textKey"/>, newest first.</summary>
    /// <param name="textKey">The address as <see cref="EntryTextKey"/> folds it.</param>
    /// <param name="take">The most ids, 1 to <see cref="SqlArgs.MaxListLength"/>.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<IReadOnlyList<long>> FindByEmailFieldAsync(string textKey, int take, CancellationToken cancellationToken = default);

    /// <summary>The entries that one of these members sent while signed in, newest first.</summary>
    /// <param name="members">The members, at most <see cref="SqlArgs.MaxListLength"/>.</param>
    /// <param name="take">The most ids.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<IReadOnlyList<long>> FindByMembersAsync(IReadOnlyCollection<Guid> members, int take, CancellationToken cancellationToken = default);

    /// <summary>The entries whose mail to their visitor went to the mailbox <paramref name="recipientKey"/>, newest first.</summary>
    /// <param name="recipientKey">The mailbox as the limits count it.</param>
    /// <param name="take">The most ids.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<IReadOnlyList<long>> FindByVisitorMailAsync(string recipientKey, int take, CancellationToken cancellationToken = default);

    /// <summary>How many files and mails each of these entries has; an entry without either is left out.</summary>
    /// <param name="entryIds">The entries, at most <see cref="SqlArgs.MaxListLength"/>.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<IReadOnlyDictionary<long, EntryCopies>> CountCopiesAsync(IReadOnlyCollection<long> entryIds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mail after <paramref name="afterId"/>, by id, to the mailbox <paramref name="recipientKey"/> or whose recipients hold
    /// <paramref name="address"/> in any spelling of its case; the caller checks each address.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <param name="recipientKey">The mailbox as the limits count it.</param>
    /// <param name="afterId">The last mail of the page before.</param>
    /// <param name="take">The most mails.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<IReadOnlyList<AddressedMail>> ListMailToAsync(string address, string recipientKey, long afterId, int take, CancellationToken cancellationToken = default);

    /// <summary>Deletes mail, sent or not.</summary>
    /// <param name="mailIds">The mails, at most <see cref="SqlArgs.MaxListLength"/>.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task<int> DeleteMailAsync(IReadOnlyCollection<long> mailIds, CancellationToken cancellationToken = default);

    /// <summary>Events of a kind after <paramref name="afterId"/>, by id, whose details hold <paramref name="address"/>.</summary>
    /// <param name="kind">The events' kind.</param>
    /// <param name="address">The address.</param>
    /// <param name="afterId">The last event of the page before.</param>
    /// <param name="take">The most events.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    Task<IReadOnlyList<MentioningEvent>> ListEventsMentioningAsync(string kind, string address, long afterId, int take, CancellationToken cancellationToken = default);

    /// <summary>Replaces events' details.</summary>
    /// <param name="eventIds">The events, at most <see cref="SqlArgs.MaxListLength"/>.</param>
    /// <param name="details">What they hold from now on.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task<int> ReplaceEventDetailsAsync(IReadOnlyCollection<long> eventIds, string details, CancellationToken cancellationToken = default);

    /// <summary>
    /// Takes what is left of a person from entries whose answers and files are gone (ADR-0223): their search text, IP
    /// address, context, page, member key and resume token; their notes; the details of their events of these kinds, which
    /// become an empty object; and their action runs' error messages.
    /// </summary>
    /// <param name="entryIds">The entries, at most <see cref="SqlArgs.MaxListLength"/>.</param>
    /// <param name="eventKinds">The kinds of event whose details may name the person.</param>
    /// <param name="cancellationToken">Cancels the write.</param>
    Task<int> ScrubAsync(IReadOnlyCollection<long> entryIds, IReadOnlyCollection<string> eventKinds, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
internal sealed class PersonDataRepository(FormBuilderDatabase database) : IPersonDataRepository
{
    /// <summary>The type name of the email field (ADR-0223), whose answers the tools find a person by.</summary>
    internal const string EmailFieldType = "email";

    // The columns of a mail that hold addresses.
    private static readonly IReadOnlyList<string> RecipientColumns = ["toAddresses", "ccAddresses", "bccAddresses", "replyToAddress"];

    // The character that escapes LIKE's own characters in an address, which an address never holds.
    private const char Escape = '!';

    /// <inheritdoc />
    public Task<IReadOnlyList<long>> FindByEmailFieldAsync(string textKey, int take, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(textKey);
        return IdsAsync(
            (n, a) => $"SELECT DISTINCT v.{n.C("entryId")} AS id FROM {n.T(FormBuilderTables.EntryValue)} v "
                + $"INNER JOIN {n.T(FormBuilderTables.FormField)} f ON f.{n.C("id")} = v.{n.C("fieldId")} "
                + $"WHERE v.{n.C("textKey")} = {a.Add(textKey)} AND f.{n.C("fieldType")} = {a.Add(EmailFieldType)}",
            take,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<long>> FindByMembersAsync(IReadOnlyCollection<Guid> members, int take, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(members);
        if (members.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<long>>([]);
        }

        Check(members.Count);
        return IdsAsync(
            (n, a) => $"SELECT {n.C("id")} AS id FROM {n.T(FormBuilderTables.Entry)} WHERE {n.C("memberKey")} IN ({a.List(members.Cast<object?>())})",
            take,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<long>> FindByVisitorMailAsync(string recipientKey, int take, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(recipientKey);
        return IdsAsync(
            (n, a) => $"SELECT DISTINCT {n.C("entryId")} AS id FROM {n.T(FormBuilderTables.Email)} WHERE {n.C("recipientKey")} = {a.Add(recipientKey)} AND {n.C("entryId")} IS NOT NULL",
            take,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<long, EntryCopies>> CountCopiesAsync(IReadOnlyCollection<long> entryIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entryIds);
        if (entryIds.Count == 0)
        {
            return Task.FromResult<IReadOnlyDictionary<long, EntryCopies>>(new Dictionary<long, EntryCopies>());
        }

        Check(entryIds.Count);
        return database.ReadAsync<IReadOnlyDictionary<long, EntryCopies>>(
            async db =>
            {
                var n = new SqlNames(db);
                var a = new SqlArgs();
                var list = a.List(entryIds.Distinct().Cast<object?>());
                var files = await db.FetchListAsync<EntryCount>(
                    $"SELECT {n.C("entryId")} AS entryId, COUNT(*) AS total FROM {n.T(FormBuilderTables.EntryFile)} WHERE {n.C("entryId")} IN ({list}) GROUP BY {n.C("entryId")}",
                    a.Values);
                var mails = await db.FetchListAsync<EntryCount>(
                    $"SELECT {n.C("entryId")} AS entryId, COUNT(*) AS total FROM {n.T(FormBuilderTables.Email)} WHERE {n.C("entryId")} IN ({list}) GROUP BY {n.C("entryId")}",
                    a.Values);
                var copies = new Dictionary<long, EntryCopies>();
                foreach (var row in files)
                {
                    copies[row.EntryId] = new EntryCopies((int)row.Total, 0);
                }

                foreach (var row in mails)
                {
                    copies[row.EntryId] = copies.GetValueOrDefault(row.EntryId) with { Emails = (int)row.Total };
                }

                return copies;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AddressedMail>> ListMailToAsync(string address, string recipientKey, long afterId, int take, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(address);
        ArgumentException.ThrowIfNullOrEmpty(recipientKey);
        CheckTake(take);
        return database.ReadAsync<IReadOnlyList<AddressedMail>>(
            async db =>
            {
                var n = new SqlNames(db);
                var a = new SqlArgs();
                var like = a.Add(Like(address));
                var holds = string.Join(" OR ", RecipientColumns.Select(column => $"{n.C(column)} LIKE {like} ESCAPE '{Escape}'"));
                var rows = await db.FetchListAsync<MailDto>(
                    $"SELECT {n.C("id")} AS id, {n.C("entryId")} AS entryId, {n.C("toAddresses")} AS toAddresses, {n.C("ccAddresses")} AS ccAddresses, "
                    + $"{n.C("bccAddresses")} AS bccAddresses, {n.C("replyToAddress")} AS replyToAddress FROM {n.T(FormBuilderTables.Email)} "
                    + $"WHERE {n.C("id")} > {a.Add(afterId)} AND ({n.C("recipientKey")} = {a.Add(recipientKey)} OR {holds}) ORDER BY {n.C("id")} {n.Page(0, take)}",
                    a.Values);
                return rows.Select(row => new AddressedMail(
                    row.Id,
                    row.EntryId,
                    [.. new[] { row.ToAddresses, row.CcAddresses, row.BccAddresses, row.ReplyToAddress }
                        .OfType<string>()
                        .SelectMany(list => list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))])).ToList();
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> DeleteMailAsync(IReadOnlyCollection<long> mailIds, CancellationToken cancellationToken = default) =>
        ExecuteForAsync(mailIds, (n, list) => $"DELETE FROM {n.T(FormBuilderTables.Email)} WHERE {n.C("id")} IN ({list})", cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<MentioningEvent>> ListEventsMentioningAsync(string kind, string address, long afterId, int take, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(kind);
        ArgumentException.ThrowIfNullOrEmpty(address);
        CheckTake(take);
        return database.ReadAsync<IReadOnlyList<MentioningEvent>>(
            async db =>
            {
                var n = new SqlNames(db);
                var a = new SqlArgs();
                var rows = await db.FetchListAsync<EventDto>(
                    $"SELECT {n.C("id")} AS id, {n.C("details")} AS details FROM {n.T(FormBuilderTables.EntryEvent)} "
                    + $"WHERE {n.C("id")} > {a.Add(afterId)} AND {n.C("kind")} = {a.Add(kind)} AND {n.C("details")} LIKE {a.Add(Like(address))} ESCAPE '{Escape}' "
                    + $"ORDER BY {n.C("id")} {n.Page(0, take)}",
                    a.Values);
                return rows.Where(row => row.Details is not null).Select(row => new MentioningEvent(row.Id, row.Details!)).ToList();
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> ReplaceEventDetailsAsync(IReadOnlyCollection<long> eventIds, string details, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(eventIds);
        ArgumentNullException.ThrowIfNull(details);
        if (eventIds.Count == 0)
        {
            return Task.FromResult(0);
        }

        Check(eventIds.Count);
        return database.WriteAsync(
            db =>
            {
                var n = new SqlNames(db);
                var a = new SqlArgs();
                var value = a.Add(details);
                return db.ExecAsync($"UPDATE {n.T(FormBuilderTables.EntryEvent)} SET {n.C("details")} = {value} WHERE {n.C("id")} IN ({a.List(eventIds.Distinct().Cast<object?>())})", a.Values);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> ScrubAsync(IReadOnlyCollection<long> entryIds, IReadOnlyCollection<string> eventKinds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entryIds);
        ArgumentNullException.ThrowIfNull(eventKinds);
        if (entryIds.Count == 0)
        {
            return Task.FromResult(0);
        }

        Check(entryIds.Count);
        return database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var a = new SqlArgs();
                var list = a.List(entryIds.Distinct().Cast<object?>());
                var scrubbed = await db.ExecAsync(
                    $"UPDATE {n.T(FormBuilderTables.Entry)} SET {n.C("searchText")} = '', {n.C("ipAddress")} = NULL, {n.C("context")} = '{{}}', {n.C("pageUrl")} = NULL, "
                    + $"{n.C("memberKey")} = NULL, {n.C("resumeTokenHash")} = NULL, {n.C("resumeExpiresUtc")} = NULL WHERE {n.C("id")} IN ({list})",
                    a.Values);
                await db.ExecAsync($"DELETE FROM {n.T(FormBuilderTables.EntryNote)} WHERE {n.C("entryId")} IN ({list})", a.Values);
                await db.ExecAsync($"UPDATE {n.T(FormBuilderTables.ActionRun)} SET {n.C("lastErrorMessage")} = NULL WHERE {n.C("entryId")} IN ({list})", a.Values);
                if (eventKinds.Count > 0)
                {
                    var kinds = a.List(eventKinds.Cast<object?>());
                    await db.ExecAsync($"UPDATE {n.T(FormBuilderTables.EntryEvent)} SET {n.C("details")} = '{{}}' WHERE {n.C("entryId")} IN ({list}) AND {n.C("kind")} IN ({kinds})", a.Values);
                }

                return scrubbed;
            },
            cancellationToken);
    }

    // An address as LIKE finds it anywhere in a text, its own wildcards and the escape taken literally.
    internal static string Like(string address)
    {
        var escaped = address
            .Replace(Escape.ToString(), $"{Escape}{Escape}", StringComparison.Ordinal)
            .Replace("%", $"{Escape}%", StringComparison.Ordinal)
            .Replace("_", $"{Escape}_", StringComparison.Ordinal)
            .Replace("[", $"{Escape}[", StringComparison.Ordinal);
        return $"%{escaped}%";
    }

    private static void Check(int count)
    {
        if (count > SqlArgs.MaxListLength)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, $"At most {SqlArgs.MaxListLength} at a time.");
        }
    }

    private static void CheckTake(int take)
    {
        if (take is < 1 or > PageRequest.MaxTake)
        {
            throw new ArgumentOutOfRangeException(nameof(take), take, $"Take 1 to {PageRequest.MaxTake}.");
        }
    }

    // The ids a query names, newest first.
    private Task<IReadOnlyList<long>> IdsAsync(Func<SqlNames, SqlArgs, string> query, int take, CancellationToken cancellationToken)
    {
        if (take is < 1 or > SqlArgs.MaxListLength)
        {
            throw new ArgumentOutOfRangeException(nameof(take), take, $"Take 1 to {SqlArgs.MaxListLength}.");
        }

        return database.ReadAsync<IReadOnlyList<long>>(
            async db =>
            {
                var n = new SqlNames(db);
                var a = new SqlArgs();
                var rows = await db.FetchListAsync<IdRow>($"SELECT q.id AS id FROM ({query(n, a)}) q ORDER BY q.id DESC {n.Page(0, take)}", a.Values);
                return rows.Select(row => row.Id).ToList();
            },
            cancellationToken);
    }

    private Task<int> ExecuteForAsync(IReadOnlyCollection<long> ids, Func<SqlNames, string, string> statement, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(ids);
        if (ids.Count == 0)
        {
            return Task.FromResult(0);
        }

        Check(ids.Count);
        return database.WriteAsync(
            db =>
            {
                var n = new SqlNames(db);
                var a = new SqlArgs();
                return db.ExecAsync(statement(n, a.List(ids.Distinct().Cast<object?>())), a.Values);
            },
            cancellationToken);
    }

    private sealed class IdRow
    {
        [Column("id")]
        public long Id { get; set; }
    }

    private sealed class EntryCount
    {
        [Column("entryId")]
        public long EntryId { get; set; }

        [Column("total")]
        public long Total { get; set; }
    }

    private sealed class MailDto
    {
        [Column("id")]
        public long Id { get; set; }

        [Column("entryId")]
        public long? EntryId { get; set; }

        [Column("toAddresses")]
        public string? ToAddresses { get; set; }

        [Column("ccAddresses")]
        public string? CcAddresses { get; set; }

        [Column("bccAddresses")]
        public string? BccAddresses { get; set; }

        [Column("replyToAddress")]
        public string? ReplyToAddress { get; set; }
    }

    private sealed class EventDto
    {
        [Column("id")]
        public long Id { get; set; }

        [Column("details")]
        public string? Details { get; set; }
    }
}
