using Jiron.Umbraco.FormBuilder.Core.Persistence.Dtos;
using Umbraco.Cms.Infrastructure.Persistence;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

// The versions of a form (ADR-0002): every save and every publish appends one, numbered per form.
internal sealed partial class FormRepository
{
    /// <summary>The longest comment the column holds.</summary>
    public const int MaxCommentLength = 500;

    /// <summary>The length of a SHA-256 in hex.</summary>
    private const int HashLength = 64;

    // Every column of a version but its definition, for lists.
    private static readonly string[] InfoColumnNames =
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
                return new SavedVersion((await FindAsync(db, "id", formId))!, version);
            },
            cancellationToken);
    }

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
    public Task<Page<FormVersionInfo>> ListVersionsAsync(int formId, PageRequest page, CancellationToken cancellationToken = default) =>
        database.ReadAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var table = n.T(FormBuilderTables.FormVersion);
                var total = await db.ScalarAsync<long>($"SELECT COUNT(*) FROM {table} WHERE {n.C("formId")} = @0", formId);
                var rows = total <= page.Skip
                    ? new List<FormVersionDto>()
                    : await db.FetchListAsync<FormVersionDto>(
                        $"SELECT {InfoColumns(n)} FROM {table} WHERE {n.C("formId")} = @0 ORDER BY {n.C("number")} DESC {n.Page(page.Skip, page.Take)}",
                        formId);
                return new Page<FormVersionInfo>(rows.Select(InfoFromDto).ToList(), total);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<FormVersionInfo?> UpdateVersionAsync(int formId, int number, string? comment, bool isPinned, CancellationToken cancellationToken = default)
    {
        CheckComment(comment);
        return database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var table = n.T(FormBuilderTables.FormVersion);
                await db.ExecAsync(
                    $"UPDATE {table} SET {n.C("comment")} = @0, {n.C("isPinned")} = @1 WHERE {n.C("formId")} = @2 AND {n.C("number")} = @3",
                    comment,
                    isPinned,
                    formId,
                    number);
                var row = await db.FirstOrNullAsync<FormVersionDto>(
                    $"SELECT {InfoColumns(n)} FROM {table} WHERE {n.C("formId")} = @0 AND {n.C("number")} = @1",
                    formId,
                    number);
                return row is null ? null : InfoFromDto(row);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> PruneDraftAsync(int formId, int number, CancellationToken cancellationToken = default) =>
        database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var table = n.T(FormBuilderTables.FormVersion);
                var id = $"{table}.{n.C("id")}";
                var deleted = await db.ExecAsync(
                    $"""
                    DELETE FROM {table} WHERE {n.C("formId")} = @0 AND {n.C("number")} = @1 AND {n.C("kind")} = @2 AND {n.C("isPinned")} = @3
                    AND NOT EXISTS (SELECT 1 FROM {n.T(FormBuilderTables.Form)} f WHERE f.{n.C("draftVersionId")} = {id} OR f.{n.C("publishedVersionId")} = {id})
                    AND NOT EXISTS (SELECT 1 FROM {n.T(FormBuilderTables.Entry)} e WHERE e.{n.C("formVersionId")} = {id})
                    """,
                    formId,
                    number,
                    nameof(FormVersionKind.Draft),
                    false);
                return deleted == 1;
            },
            cancellationToken);

    internal static FormVersionInfo InfoFromDto(FormVersionDto d) => new(
        d.Id,
        d.FormId,
        d.Number,
        Enum.Parse<FormVersionKind>(d.Kind),
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
            kind.ToString(),
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
