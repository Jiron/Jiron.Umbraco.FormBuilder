using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Dtos;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Queries;
using Umbraco.Cms.Infrastructure.Persistence;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

/// <inheritdoc />
/// <remarks>
/// Every state change is one conditional statement (ADR-0007, ADR-0036): the concurrency version, a free alias and an
/// existing folder are conditions of the write itself, never read first. Only when the write changed nothing does the
/// repository read, to say why.
/// </remarks>
internal sealed partial class FormRepository(FormBuilderDatabase database) : IFormRepository
{
    /// <summary>The longest alias the column holds.</summary>
    public const int MaxAliasLength = 64;

    /// <summary>The longest name the column holds.</summary>
    public const int MaxNameLength = 255;

    /// <inheritdoc />
    public Task<SavedVersion> CreateAsync(NewForm form, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(form);
        CheckAlias(form.Alias);
        CheckName(form.Name);
        CheckDocument(form.Draft);
        CheckComment(form.Comment);
        return database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var table = n.T(FormBuilderTables.Form);
                var now = database.UtcNow;
                var folder = form.FolderId is null ? string.Empty : $" AND EXISTS (SELECT 1 FROM {n.T(FormBuilderTables.Folder)}{n.LockingRead} WHERE {n.C("id")} = @1 AND {n.C("trashedUtc")} IS NULL)";
                int inserted;
                try
                {
                    inserted = await db.ExecAsync(
                        $"""
                        INSERT INTO {table} ({n.C("key")}, {n.C("folderId")}, {n.C("alias")}, {n.C("name")}, {n.C("publicRevision")}, {n.C("concurrencyVersion")}, {n.C("lastEntryNumber")}, {n.C("isRestricted")}, {n.C("createdUtc")}, {n.C("updatedUtc")}, {n.C("createdByUserKey")}, {n.C("updatedByUserKey")})
                        SELECT @0, @1, @2, @3, 0, 1, 0, @4, @5, @5, @6, @6
                        WHERE NOT EXISTS (SELECT 1 FROM {table}{n.InsertOnceHint} WHERE {n.C("alias")} = @2 OR {n.C("key")} = @0){folder}
                        """,
                        form.Key,
                        form.FolderId,
                        form.Alias,
                        form.Name,
                        form.IsRestricted,
                        now,
                        form.UserKey);
                }
                catch (Exception exception) when (FormBuilderDatabaseErrors.IsUniqueViolation(exception))
                {
                    // The backstop for a writer the database let through at the same moment (ADR-0036): say which.
                    var refused = await RefusedCreateAsync(db, n, form);
                    throw new FormBuilderConflictException(refused.Conflict, refused.Message, exception);
                }

                if (inserted == 0)
                {
                    throw await RefusedCreateAsync(db, n, form);
                }

                var formId = await db.ScalarAsync<int>($"SELECT {n.C("id")} FROM {table} WHERE {n.C("key")} = @0", form.Key);
                var version = new FormVersionDto
                {
                    FormId = formId,
                    Number = 1,
                    Kind = nameof(FormVersionKind.Draft),
                    SchemaVersion = form.Draft.SchemaVersion,
                    Definition = form.Draft.Definition,
                    DefinitionHash = form.Draft.DefinitionHash,
                    PublicHash = form.Draft.PublicHash,
                    Comment = form.Comment,
                    CreatedUtc = now,
                    CreatedByUserKey = form.UserKey,
                };
                await db.InsertRowAsync(version);
                await db.ExecAsync($"UPDATE {table} SET {n.C("draftVersionId")} = @0 WHERE {n.C("id")} = @1", version.Id, formId);
                return new SavedVersion((await FindAsync(db, "id", formId))!, VersionFromDto(version));
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<StoredForm?> GetAsync(Guid key, CancellationToken cancellationToken = default) =>
        database.ReadAsync(db => FindAsync(db, "key", key), cancellationToken);

    /// <inheritdoc />
    public Task<StoredForm?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        database.ReadAsync(db => FindAsync(db, "id", id), cancellationToken);

    /// <inheritdoc />
    /// <remarks>
    /// Plain equality: the alias column compares without regard to case on both databases (COLLATE NOCASE on SQLite, a
    /// case-insensitive collation on SQL Server, ADR-0033), and its unique index serves the lookup.
    /// </remarks>
    public Task<StoredForm?> GetByAliasAsync(string alias, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(alias);
        return database.ReadAsync(db => FindAsync(db, "alias", alias), cancellationToken);
    }

    /// <inheritdoc />
    public Task<Page<StoredForm>> ListAsync(FormFilter filter, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return database.ReadAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var args = new List<object?>();
                string Arg(object? value)
                {
                    args.Add(value);
                    return "@" + (args.Count - 1).ToString(CultureInfo.InvariantCulture);
                }

                var where = new List<string>();
                if (!filter.AllFolders)
                {
                    where.Add(filter.FolderId is { } folder ? $"{n.C("folderId")} = {Arg(folder)}" : $"{n.C("folderId")} IS NULL");
                }

                if (filter.Trashed is { } trashed)
                {
                    where.Add($"{n.C("trashedUtc")} {(trashed ? "IS NOT NULL" : "IS NULL")}");
                }

                if (AccessSql.FormClause(filter.Access, n, string.Empty, Arg) is { } access)
                {
                    where.Add(access);
                }

                if (filter.OutsideFolderIds is { Count: > 0 } outside)
                {
                    where.Add($"({n.C("folderId")} IS NULL OR {n.C("folderId")} NOT IN ({Literals(outside)}))");
                }

                if (!string.IsNullOrWhiteSpace(filter.Search))
                {
                    var pattern = Arg("%" + SqlNames.Escape(filter.Search.Trim()) + "%");
                    where.Add($"({n.C("name")} LIKE {pattern} {SqlNames.EscapeClause} OR {n.C("alias")} LIKE {pattern} {SqlNames.EscapeClause})");
                }

                var table = n.T(FormBuilderTables.Form);
                var clause = where.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", where);
                var total = await db.ScalarAsync<long>($"SELECT COUNT(*) FROM {table}{clause}", [.. args]);
                var rows = total <= page.Skip
                    ? new List<FormDto>()
                    : await db.FetchListAsync<FormDto>($"SELECT * FROM {table}{clause} ORDER BY {n.C("name")}, {n.C("id")} {n.Page(page.Skip, page.Take)}", [.. args]);
                return new Page<StoredForm>(rows.Select(FromDto).ToList(), total);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The folder ids go into the text as integer literals, as in the access clause: numbers read from the database,
    /// and a page of the tree holds at most <see cref="PageRequest.MaxTake"/> folders.
    /// </remarks>
    public Task<IReadOnlySet<int>> ListFolderIdsWithFormsAsync(IReadOnlyCollection<int> folderIds, bool trashed, AccessScope? access, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(folderIds);
        if (folderIds.Count == 0)
        {
            return Task.FromResult<IReadOnlySet<int>>(new HashSet<int>());
        }

        return database.ReadAsync<IReadOnlySet<int>>(
            async db =>
            {
                var n = new SqlNames(db);
                var args = new List<object?>();
                string Arg(object? value)
                {
                    args.Add(value);
                    return "@" + (args.Count - 1).ToString(CultureInfo.InvariantCulture);
                }

                var where = new List<string>
                {
                    $"{n.C("folderId")} IN ({Literals(folderIds)})",
                    $"{n.C("trashedUtc")} {(trashed ? "IS NOT NULL" : "IS NULL")}",
                };
                if (AccessSql.FormClause(access, n, string.Empty, Arg) is { } clause)
                {
                    where.Add(clause);
                }

                var ids = await db.FetchListAsync<int>($"SELECT DISTINCT {n.C("folderId")} FROM {n.T(FormBuilderTables.Form)} WHERE {string.Join(" AND ", where)}", [.. args]);
                return ids.ToHashSet();
            },
            cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>The alias column compares without regard to case on both databases, so <c>LIKE</c> does too (ADR-0033).</remarks>
    public Task<IReadOnlyList<string>> ListAliasesAsync(string prefix, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        return database.ReadAsync<IReadOnlyList<string>>(
            async db =>
            {
                var n = new SqlNames(db);
                return await db.FetchListAsync<string>(
                    $"SELECT {n.C("alias")} FROM {n.T(FormBuilderTables.Form)} WHERE {n.C("alias")} LIKE @0 {SqlNames.EscapeClause} ORDER BY {n.C("alias")}",
                    SqlNames.Escape(prefix) + "%");
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<StoredForm?> UpdateAsync(FormUpdate update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        CheckAlias(update.Alias);
        CheckName(update.Name);
        return database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var table = n.T(FormBuilderTables.Form);
                // A form may stay in its folder whatever became of it; a move needs a folder outside the trash.
                var folder = update.FolderId is null ? string.Empty : $" AND ({n.C("folderId")} = @2 OR EXISTS (SELECT 1 FROM {n.T(FormBuilderTables.Folder)}{n.LockingRead} WHERE {n.C("id")} = @2 AND {n.C("trashedUtc")} IS NULL))";
                var updated = await TranslateDuplicatesAsync(() => db.ExecAsync(
                    $"""
                    UPDATE {table} SET {n.C("alias")} = @0, {n.C("name")} = @1, {n.C("folderId")} = @2, {n.C("isRestricted")} = @3, {Raise(n)}, {n.C("updatedUtc")} = @4, {n.C("updatedByUserKey")} = @5
                    WHERE {n.C("id")} = @6 AND {n.C("concurrencyVersion")} = @7 AND {n.C("trashedUtc")} IS NULL
                    AND NOT EXISTS (SELECT 1 FROM {table} o{n.InsertOnceHint} WHERE o.{n.C("alias")} = @0 AND o.{n.C("id")} <> @6){folder}
                    """,
                    update.Alias,
                    update.Name,
                    update.FolderId,
                    update.IsRestricted,
                    database.UtcNow,
                    update.UserKey,
                    update.Id,
                    update.ExpectedConcurrencyVersion));
                var current = await FindAsync(db, "id", update.Id);
                if (updated == 1 || current is null)
                {
                    return current;
                }

                CheckFresh(current, update.ExpectedConcurrencyVersion);
                CheckLive(current);
                var taken = await db.ScalarAsync<int>(
                    $"SELECT COUNT(*) FROM {table} WHERE {n.C("alias")} = @0 AND {n.C("id")} <> @1",
                    update.Alias,
                    update.Id);
                throw taken > 0
                    ? new FormBuilderConflictException(FormBuilderConflict.DuplicateAlias, "Another form already has this alias.")
                    : new FormBuilderConflictException(FormBuilderConflict.MissingFolder, "The folder does not exist or is in the trash.");
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<StoredForm?> UnpublishAsync(int formId, int expectedConcurrencyVersion, Guid? userKey, CancellationToken cancellationToken = default) =>
        database.WriteAsync(
            db =>
            {
                var n = new SqlNames(db);
                return ChangeAsync(
                    db,
                    n,
                    formId,
                    expectedConcurrencyVersion,
                    $"{n.C("publishedVersionId")} = NULL, {n.C("themeAlias")} = NULL",
                    $"{n.C("publishedVersionId")} IS NOT NULL",
                    userKey);
            },
            cancellationToken);

    internal static StoredForm FromDto(FormDto d) => new(
        d.Id,
        d.Key,
        d.FolderId,
        d.Alias,
        d.Name,
        d.DraftVersionId,
        d.PublishedVersionId,
        d.PublicRevision,
        d.ConcurrencyVersion,
        d.LastEntryNumber,
        d.ThemeAlias,
        d.IsRestricted,
        FormBuilderDbTime.Read(d.TrashedUtc),
        d.TrashKey,
        FormBuilderDbTime.Read(d.CreatedUtc),
        FormBuilderDbTime.Read(d.UpdatedUtc),
        d.CreatedByUserKey,
        d.UpdatedByUserKey);

    /// <summary>
    /// The first write of a unit that counts for a form or deletes its daily values: it changes nothing, but holds the
    /// form's row until the unit ends, so units that touch a form's values take turns, and always lock the form first.
    /// False when the form does not exist.
    /// </summary>
    internal static async Task<bool> HoldAsync(IUmbracoDatabase db, SqlNames n, int formId) =>
        await db.ExecAsync(
            $"UPDATE {n.T(FormBuilderTables.Form)} SET {n.C("updatedUtc")} = {n.C("updatedUtc")} WHERE {n.C("id")} = @0",
            formId) == 1;

    private static string Raise(SqlNames n) => $"{n.C("concurrencyVersion")} = {n.C("concurrencyVersion")} + 1";

    // Ids read from the database, as the statement's integer literals.
    private static string Literals(IEnumerable<int> ids) => string.Join(", ", ids.Select(id => id.ToString(CultureInfo.InvariantCulture)));

    private static async Task<StoredForm?> FindAsync(IUmbracoDatabase db, string column, object value)
    {
        var n = new SqlNames(db);
        var row = await db.FirstOrNullAsync<FormDto>($"SELECT * FROM {n.T(FormBuilderTables.Form)} WHERE {n.C(column)} = @0", value);
        return row is null ? null : FromDto(row);
    }

    /// <summary>
    /// Raises the concurrency version and applies <paramref name="set"/> (where <c>@0</c> is the time and <c>@4</c> is
    /// <paramref name="value"/>) when the form is fresh and <paramref name="needed"/> holds. A form for which
    /// <paramref name="needed"/> does not hold needs no change and comes back as it is.
    /// </summary>
    private async Task<StoredForm?> ChangeAsync(IUmbracoDatabase db, SqlNames n, int formId, int expected, string set, string needed, Guid? userKey, object? value = null)
    {
        object?[] args = value is null ? [database.UtcNow, userKey, formId, expected] : [database.UtcNow, userKey, formId, expected, value];
        var changed = await db.ExecAsync(
            $"UPDATE {n.T(FormBuilderTables.Form)} SET {set}, {Raise(n)}, {n.C("updatedUtc")} = @0, {n.C("updatedByUserKey")} = @1 WHERE {n.C("id")} = @2 AND {n.C("concurrencyVersion")} = @3 AND {needed}",
            args);
        var current = await FindAsync(db, "id", formId);
        if (changed == 0 && current is not null)
        {
            CheckFresh(current, expected);
        }

        return current;
    }

    /// <summary>
    /// Raises the concurrency version of a fresh form outside the trash, the first statement of a save or a publish, so
    /// the transaction holds the form's row before it counts versions. False when the form does not exist.
    /// </summary>
    private async Task<bool> RaiseAsync(IUmbracoDatabase db, SqlNames n, int formId, int expected, Guid? userKey)
    {
        var raised = await db.ExecAsync(
            $"UPDATE {n.T(FormBuilderTables.Form)} SET {Raise(n)}, {n.C("updatedUtc")} = @0, {n.C("updatedByUserKey")} = @1 WHERE {n.C("id")} = @2 AND {n.C("concurrencyVersion")} = @3 AND {n.C("trashedUtc")} IS NULL",
            database.UtcNow,
            userKey,
            formId,
            expected);
        if (raised == 1)
        {
            return true;
        }

        var current = await FindAsync(db, "id", formId);
        if (current is null)
        {
            return false;
        }

        CheckFresh(current, expected);
        CheckLive(current);
        return true;
    }

    private static void CheckFresh(StoredForm current, int expected)
    {
        if (current.ConcurrencyVersion != expected)
        {
            throw new FormBuilderConcurrencyException($"The form was changed by someone else meanwhile (version {current.ConcurrencyVersion}, not {expected}).");
        }
    }

    private static void CheckLive(StoredForm current)
    {
        if (current.TrashedUtc is not null)
        {
            throw new FormBuilderConflictException(FormBuilderConflict.Trashed, "The form is in the trash; restore it first.");
        }
    }

    private static async Task<FormBuilderConflictException> RefusedCreateAsync(IUmbracoDatabase db, SqlNames n, NewForm form)
    {
        var table = n.T(FormBuilderTables.Form);
        if (await db.ScalarAsync<int>($"SELECT COUNT(*) FROM {table} WHERE {n.C("alias")} = @0", form.Alias) > 0)
        {
            return new FormBuilderConflictException(FormBuilderConflict.DuplicateAlias, "Another form already has this alias.");
        }

        return await db.ScalarAsync<int>($"SELECT COUNT(*) FROM {table} WHERE {n.C("key")} = @0", form.Key) > 0
            ? new FormBuilderConflictException(FormBuilderConflict.DuplicateKey, "Another form already has this key.")
            : new FormBuilderConflictException(FormBuilderConflict.MissingFolder, "The folder does not exist or is in the trash.");
    }

    /// <summary>
    /// A unique index that refuses an update's alias is a conflict, not a failure: the conditions of the statement keep it
    /// from happening, so this is the backstop for a writer the database let through at the same moment. An update never
    /// changes the key.
    /// </summary>
    private static async Task<int> TranslateDuplicatesAsync(Func<Task<int>> write)
    {
        try
        {
            return await write();
        }
        catch (Exception exception) when (FormBuilderDatabaseErrors.IsUniqueViolation(exception))
        {
            throw new FormBuilderConflictException(FormBuilderConflict.DuplicateAlias, "Another form already has this alias.", exception);
        }
    }

    private static void CheckAlias(string alias)
    {
        ArgumentNullException.ThrowIfNull(alias);
        if (alias.Length is 0 or > MaxAliasLength)
        {
            throw new ArgumentException($"A form alias holds 1 to {MaxAliasLength} characters.", nameof(alias));
        }
    }

    private static void CheckName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Length is 0 or > MaxNameLength)
        {
            throw new ArgumentException($"A form name holds 1 to {MaxNameLength} characters.", nameof(name));
        }
    }
}
