using System.Collections.Immutable;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Dtos;
using Umbraco.Cms.Infrastructure.Persistence;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

// The versions of a form (ADR-0002): every save and every publish appends one, numbered per form.
internal sealed partial class FormRepository
{
    /// <summary>The longest comment the column holds.</summary>
    public const int MaxCommentLength = 500;

    /// <summary>The most versions <see cref="GetVersionsAsync"/> reads at once.</summary>
    public const int MaxVersionsAtOnce = 200;

    /// <summary>The length of a SHA-256 in hex.</summary>
    private const int HashLength = 64;

    // Every column of a version but its definition, for lists.
    private static readonly ImmutableArray<string> InfoColumnNames =
    [
        "id", "formId", "number", "kind", "schemaVersion", "definitionHash", "publicHash", "publicRevision", "comment", "isPinned",
        "createdUtc", "createdByUserKey", "publishedUtc", "publishedByUserKey",
    ];

    /// <inheritdoc />
    public Task<SavedVersion?> SaveDraftAsync(int formId, int expectedConcurrencyVersion, VersionDocument draft, string? comment, Guid? userKey, CancellationToken cancellationToken = default)
    {
        CheckDocument(draft);
        CheckComment(comment);
        return database.WriteAsync<SavedVersion?>(
            async db =>
            {
                var n = new SqlNames(db);
                if (!await RaiseAsync(db, n, formId, expectedConcurrencyVersion, userKey))
                {
                    return null;
                }

                var version = await AppendAsync(db, n, formId, FormVersionKind.Draft, draft, comment, userKey);
                await db.ExecAsync($"UPDATE {n.T(FormBuilderTables.Form)} SET {n.C("draftVersionId")} = @0 WHERE {n.C("id")} = @1", version.Info.Id, formId);
                await WriteUsesAsync(db, n, formId, FormVersionKind.Draft, draft.Uses);
                return new SavedVersion((await FindAsync(db, "id", formId))!, version);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<SavedVersion?> PublishAsync(int formId, int expectedConcurrencyVersion, VersionDocument document, string? themeAlias, string? comment, Guid? userKey, CancellationToken cancellationToken = default)
    {
        CheckDocument(document);
        CheckComment(comment);
        if (themeAlias is { Length: 0 or > MaxAliasLength })
        {
            throw new ArgumentException($"A theme alias holds 1 to {MaxAliasLength} characters.", nameof(themeAlias));
        }

        return database.WriteAsync<SavedVersion?>(
            async db =>
            {
                var n = new SqlNames(db);
                if (!await RaiseAsync(db, n, formId, expectedConcurrencyVersion, userKey))
                {
                    return null;
                }

                var version = await AppendAsync(db, n, formId, FormVersionKind.Published, document, comment, userKey);
                await db.ExecAsync(
                    $"UPDATE {n.T(FormBuilderTables.Form)} SET {n.C("publishedVersionId")} = @0, {n.C("publicRevision")} = @1, {n.C("themeAlias")} = @2 WHERE {n.C("id")} = @3",
                    version.Info.Id,
                    version.Info.PublicRevision,
                    themeAlias,
                    formId);
                await WriteUsesAsync(db, n, formId, FormVersionKind.Published, document.Uses);
                return new SavedVersion((await FindAsync(db, "id", formId))!, version);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<int?> GetLastPublishedNumberAsync(int formId, int beforeNumber, CancellationToken cancellationToken = default) =>
        database.ReadAsync(
            db =>
            {
                var n = new SqlNames(db);
                return db.ScalarAsync<int?>(
                    $"SELECT MAX({n.C("number")}) FROM {n.T(FormBuilderTables.FormVersion)} WHERE {n.C("formId")} = @0 AND {n.C("kind")} = @1 AND {n.C("number")} < @2",
                    formId,
                    StoredNames.Of(FormVersionKind.Published),
                    beforeNumber);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<StoredFormVersion?> GetVersionAsync(int formId, int number, CancellationToken cancellationToken = default) =>
        database.ReadAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var row = await db.FirstOrNullAsync<FormVersionDto>(
                    $"SELECT * FROM {n.T(FormBuilderTables.FormVersion)} WHERE {n.C("formId")} = @0 AND {n.C("number")} = @1",
                    formId,
                    number);
                return row is null ? null : VersionFromDto(row);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<StoredFormVersion?> GetVersionByIdAsync(int versionId, CancellationToken cancellationToken = default) =>
        database.ReadAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var row = await db.FirstOrNullAsync<FormVersionDto>($"SELECT * FROM {n.T(FormBuilderTables.FormVersion)} WHERE {n.C("id")} = @0", versionId);
                return row is null ? null : VersionFromDto(row);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<StoredFormVersion?> GetRevisionAsync(int formId, int publicRevision, CancellationToken cancellationToken = default) =>
        database.ReadAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var row = await db.FirstOrNullAsync<FormVersionDto>(
                    $"SELECT * FROM {n.T(FormBuilderTables.FormVersion)} WHERE {n.C("formId")} = @0 AND {n.C("kind")} = @1 AND {n.C("publicRevision")} = @2 ORDER BY {n.C("number")} DESC {n.Page(0, 1)}",
                    formId,
                    StoredNames.Of(FormVersionKind.Published),
                    publicRevision);
                return row is null ? null : VersionFromDto(row);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<StoredFormVersion>> GetVersionsAsync(int formId, IReadOnlyCollection<int> numbers, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(numbers);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(numbers.Count, MaxVersionsAtOnce, nameof(numbers));
        if (numbers.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<StoredFormVersion>>([]);
        }

        var wanted = numbers.ToArray();
        return database.ReadAsync<IReadOnlyList<StoredFormVersion>>(
            async db =>
            {
                var n = new SqlNames(db);
                var rows = await db.FetchListAsync<FormVersionDto>(
                    $"SELECT * FROM {n.T(FormBuilderTables.FormVersion)} WHERE {n.C("formId")} = @0 AND {n.C("number")} IN ({SqlArgs.Placeholders(1, wanted.Length)}) ORDER BY {n.C("number")} DESC",
                    [formId, .. wanted.Cast<object>()]);
                return rows.Select(VersionFromDto).ToList();
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<Page<FormVersionInfo>> ListVersionsAsync(int formId, FormVersionKind? kind, PageRequest page, CancellationToken cancellationToken = default) =>
        database.ReadAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var table = n.T(FormBuilderTables.FormVersion);
                var where = kind is null ? $"{n.C("formId")} = @0" : $"{n.C("formId")} = @0 AND {n.C("kind")} = @1";
                var kindName = kind is { } named ? StoredNames.Of(named) : null;
                var total = await db.ScalarAsync<long>($"SELECT COUNT(*) FROM {table} WHERE {where}", formId, kindName);
                var rows = total <= page.Skip
                    ? new List<FormVersionDto>()
                    : await db.FetchListAsync<FormVersionDto>(
                        $"SELECT {InfoColumns(n)} FROM {table} WHERE {where} ORDER BY {n.C("number")} DESC {n.Page(page.Skip, page.Take)}",
                        formId,
                        kindName);
                return new Page<FormVersionInfo>(rows.Select(InfoFromDto).ToList(), total);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<FormVersionInfo?> PinVersionAsync(int formId, int number, bool isPinned, CancellationToken cancellationToken = default) =>
        UpdateVersionAsync(formId, number, "isPinned", isPinned, cancellationToken);

    /// <inheritdoc />
    public Task<FormVersionInfo?> CommentVersionAsync(int formId, int number, string? comment, CancellationToken cancellationToken = default)
    {
        CheckComment(comment);
        return UpdateVersionAsync(formId, number, "comment", comment, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> PruneDraftAsync(int formId, int number, CancellationToken cancellationToken = default) =>
        database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var table = n.T(FormBuilderTables.FormVersion);
                var id = $"{table}.{n.C("id")}";

                // The form is found by its id, so the locking read holds its row only: the version columns have no index.
                var deleted = await db.ExecAsync(
                    $"""
                    DELETE FROM {table} WHERE {n.C("formId")} = @0 AND {n.C("number")} = @1 AND {n.C("kind")} = @2 AND {n.C("isPinned")} = @3
                    AND NOT EXISTS (SELECT 1 FROM {n.T(FormBuilderTables.Form)} f{n.LockingRead} WHERE f.{n.C("id")} = @0 AND (f.{n.C("draftVersionId")} = {id} OR f.{n.C("publishedVersionId")} = {id}))
                    AND NOT EXISTS (SELECT 1 FROM {n.T(FormBuilderTables.Entry)} e{n.LockingRead} WHERE e.{n.C("formVersionId")} = {id})
                    """,
                    formId,
                    number,
                    StoredNames.Of(FormVersionKind.Draft),
                    false);
                return deleted == 1;
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<DraftToPrune>> ListDraftsToPruneAsync(DateTimeOffset createdBefore, int keepNewest, int take, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(keepNewest);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(take, PageRequest.MaxTake);
        return database.ReadAsync<IReadOnlyList<DraftToPrune>>(
            async db =>
            {
                var n = new SqlNames(db);
                var versions = n.T(FormBuilderTables.FormVersion);
                var form = n.T(FormBuilderTables.Form);

                // A nomination only: every delete checks the pin, the form's versions and the entries again with a locking
                // read (ADR-0045), so this read takes no locks.
                var rows = await db.FetchListAsync<FormVersionDto>(
                    $"""
                    SELECT d.{n.C("formId")}, d.{n.C("number")} FROM (
                    SELECT v.{n.C("id")}, v.{n.C("formId")}, v.{n.C("number")}, v.{n.C("isPinned")}, v.{n.C("createdUtc")}, ROW_NUMBER() OVER (PARTITION BY v.{n.C("formId")} ORDER BY v.{n.C("number")} DESC) AS {n.C("place")}
                    FROM {versions} v WHERE v.{n.C("kind")} = @0) d
                    INNER JOIN {form} f ON f.{n.C("id")} = d.{n.C("formId")}
                    WHERE d.{n.C("place")} > @1 AND d.{n.C("isPinned")} = @2 AND d.{n.C("createdUtc")} < @3
                    AND (f.{n.C("draftVersionId")} IS NULL OR f.{n.C("draftVersionId")} <> d.{n.C("id")})
                    AND NOT EXISTS (SELECT 1 FROM {n.T(FormBuilderTables.Entry)} e WHERE e.{n.C("formVersionId")} = d.{n.C("id")})
                    ORDER BY d.{n.C("formId")}, d.{n.C("number")} {n.Page(0, take)}
                    """,
                    StoredNames.Of(FormVersionKind.Draft),
                    keepNewest,
                    false,
                    FormBuilderDbTime.Write(createdBefore));
                return rows.Select(row => new DraftToPrune(row.FormId, row.Number)).ToList();
            },
            cancellationToken);
    }

    internal static FormVersionInfo InfoFromDto(FormVersionDto d) => new(
        d.Id,
        d.FormId,
        d.Number,
        StoredNames.Parse<FormVersionKind>(d.Kind),
        d.SchemaVersion,
        d.DefinitionHash,
        d.PublicHash,
        d.PublicRevision,
        d.Comment,
        d.IsPinned,
        FormBuilderDbTime.Read(d.CreatedUtc),
        d.CreatedByUserKey,
        FormBuilderDbTime.Read(d.PublishedUtc),
        d.PublishedByUserKey);

    internal static StoredFormVersion VersionFromDto(FormVersionDto d) => new(InfoFromDto(d), d.Definition);

    /// <summary>
    /// Appends a version with the next number. A publish keeps the public revision of the last publish when its public
    /// hash is the same, and takes the next one otherwise (ADR-0002). One statement, run after the form's row is
    /// locked by <see cref="RaiseAsync"/>, so two writers of one form never count the same number.
    /// </summary>
    private async Task<StoredFormVersion> AppendAsync(IUmbracoDatabase db, SqlNames n, int formId, FormVersionKind kind, VersionDocument document, string? comment, Guid? userKey)
    {
        var table = n.T(FormBuilderTables.FormVersion);
        var next = $"(SELECT COALESCE(MAX({n.C("number")}), 0) + 1 FROM {table} WHERE {n.C("formId")} = @0)";
        string revision = "NULL", publishedUtc = "NULL", publishedBy = "NULL";
        if (kind == FormVersionKind.Published)
        {
            var publishes = $"{n.C("formId")} = @0 AND {n.C("kind")} = @1";
            var lastHash = $"(SELECT l.{n.C("publicHash")} FROM {table} l WHERE l.{n.C("formId")} = @0 AND l.{n.C("kind")} = @1 AND l.{n.C("number")} = (SELECT MAX({n.C("number")}) FROM {table} WHERE {publishes}))";
            revision = $"(SELECT COALESCE(MAX({n.C("publicRevision")}), 0) FROM {table} WHERE {publishes}) + CASE WHEN {lastHash} = @5 THEN 0 ELSE 1 END";
            publishedUtc = "@8";
            publishedBy = "@9";
        }

        await db.ExecAsync(
            $"""
            INSERT INTO {table} ({n.C("formId")}, {n.C("number")}, {n.C("kind")}, {n.C("schemaVersion")}, {n.C("definition")}, {n.C("definitionHash")}, {n.C("publicHash")}, {n.C("publicRevision")}, {n.C("comment")}, {n.C("isPinned")}, {n.C("createdUtc")}, {n.C("createdByUserKey")}, {n.C("publishedUtc")}, {n.C("publishedByUserKey")})
            SELECT @0, {next}, @1, @2, @3, @4, @5, {revision}, @6, @7, @8, @9, {publishedUtc}, {publishedBy}
            """,
            formId,
            StoredNames.Of(kind),
            document.SchemaVersion,
            document.Definition,
            document.DefinitionHash,
            document.PublicHash,
            comment,
            false,
            database.UtcNow,
            userKey);
        var row = await db.FirstOrNullAsync<FormVersionDto>(
            $"SELECT * FROM {table} WHERE {n.C("formId")} = @0 AND {n.C("number")} = (SELECT MAX({n.C("number")}) FROM {table} WHERE {n.C("formId")} = @0)",
            formId);
        return VersionFromDto(row!);
    }

    private static string InfoColumns(SqlNames n) => string.Join(", ", InfoColumnNames.Select(column => n.C(column)));

    /// <summary>
    /// Sets one column of a version: one statement, which a form in the trash refuses (ADR-0045). The form's row stays as
    /// it is, so the change never makes another editor's save stale. Only when it changed nothing does it read, to say why.
    /// </summary>
    private Task<FormVersionInfo?> UpdateVersionAsync(int formId, int number, string column, object? value, CancellationToken cancellationToken) =>
        database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var table = n.T(FormBuilderTables.FormVersion);
                var form = n.T(FormBuilderTables.Form);
                var updated = await db.ExecAsync(
                    $"UPDATE {table} SET {n.C(column)} = @0 WHERE {n.C("formId")} = @1 AND {n.C("number")} = @2 AND EXISTS (SELECT 1 FROM {form} f{n.LockingRead} WHERE f.{n.C("id")} = @1 AND f.{n.C("trashedUtc")} IS NULL)",
                    value,
                    formId,
                    number);
                var row = await db.FirstOrNullAsync<FormVersionDto>(
                    $"SELECT {InfoColumns(n)} FROM {table} WHERE {n.C("formId")} = @0 AND {n.C("number")} = @1",
                    formId,
                    number);
                if (updated == 0 && row is not null)
                {
                    throw LibraryTrash.Trashed("form");
                }

                return row is null ? null : InfoFromDto(row);
            },
            cancellationToken);

    private static void CheckDocument(VersionDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(document.SchemaVersion, nameof(document));
        if (string.IsNullOrEmpty(document.Definition))
        {
            throw new ArgumentException("A version needs its definition.", nameof(document));
        }

        if (!IsHash(document.DefinitionHash) || !IsHash(document.PublicHash))
        {
            throw new ArgumentException("A version's hashes are SHA-256 values in lowercase hex.", nameof(document));
        }
    }

    private static bool IsHash(string? hash) =>
        hash is { Length: HashLength } && hash.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f'));

    private static void CheckComment(string? comment)
    {
        if (comment is { Length: > MaxCommentLength })
        {
            throw new ArgumentException($"A version comment holds at most {MaxCommentLength} characters.", nameof(comment));
        }
    }
}
