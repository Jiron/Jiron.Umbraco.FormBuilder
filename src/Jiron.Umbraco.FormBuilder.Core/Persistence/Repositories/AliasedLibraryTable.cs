using Jiron.Umbraco.FormBuilder.Core.Persistence.Dtos;
using Umbraco.Cms.Infrastructure.Persistence;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

/// <summary>A content column of a library row and the value to write to it.</summary>
/// <param name="Name">The column.</param>
/// <param name="Value">Its value.</param>
internal readonly record struct LibraryColumn(string Name, object? Value);

/// <summary>What keeps a library row from being deleted while something uses it.</summary>
/// <param name="Unused">The condition on the row, in SQL, that holds while nothing uses it.</param>
/// <param name="Message">The refusal's message when it does not hold.</param>
internal readonly record struct InUseGuard(Func<SqlNames, string> Unused, string Message);

/// <summary>
/// The statements the library rows named by an alias share (ADR-0037): option lists, themes and email layouts each have a
/// key, an alias unique without regard to case, a name, content columns of their own, and a concurrency version that
/// starts at 1 and that every change raises. Every change is one conditional statement (ADR-0036); only when it changed
/// nothing does the table read, to say why.
/// </summary>
/// <typeparam name="TDto">The table's row.</typeparam>
/// <param name="database">The units of work.</param>
/// <param name="table">The table.</param>
/// <param name="noun">What a row is called in messages, such as "option list".</param>
internal sealed class AliasedLibraryTable<TDto>(FormBuilderDatabase database, string table, string noun)
    where TDto : class, IAliasedLibraryRow
{
    /// <summary>The longest alias the column holds.</summary>
    public const int MaxAliasLength = 64;

    /// <summary>The longest name the column holds.</summary>
    public const int MaxNameLength = 255;

    /// <summary>
    /// Adds a row at concurrency version 1. Refused with <see cref="FormBuilderConflict.DuplicateAlias"/> when another
    /// row has the alias in any letter case, and with <see cref="FormBuilderConflict.DuplicateKey"/> for a taken key.
    /// </summary>
    public Task<TDto> CreateAsync(Guid key, string alias, string name, IReadOnlyList<LibraryColumn> content, CancellationToken cancellationToken)
    {
        CheckAlias(alias);
        CheckName(name);
        return database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var t = n.T(table);
                var args = new SqlArgs();
                var columns = string.Concat(content.Select(column => ", " + n.C(column.Name)));
                var values = string.Concat(content.Select(column => ", " + args.Add(column.Value)));
                var now = args.Add(database.UtcNow);
                var keyArg = args.Add(key);
                var aliasArg = args.Add(alias);
                var nameArg = args.Add(name);

                // One check per unique index, so each is a seek and locks only its own key's range.
                var free = $"NOT EXISTS (SELECT 1 FROM {t}{n.InsertOnceHint} WHERE";
                int inserted;
                try
                {
                    inserted = await db.ExecAsync(
                        $"""
                        INSERT INTO {t} ({n.C("key")}, {n.C("alias")}, {n.C("name")}{columns}, {n.C("concurrencyVersion")}, {n.C("createdUtc")}, {n.C("updatedUtc")})
                        SELECT {keyArg}, {aliasArg}, {nameArg}{values}, 1, {now}, {now}
                        WHERE {free} {n.C("alias")} = {aliasArg}) AND {free} {n.C("key")} = {keyArg})
                        """,
                        args.Values);
                }
                catch (Exception exception) when (FormBuilderDatabaseErrors.IsUniqueViolation(exception))
                {
                    // The backstop for a writer the database let through at the same moment (ADR-0036): say which.
                    var refused = await RefusedCreateAsync(db, n, alias);
                    throw new FormBuilderConflictException(refused.Conflict, refused.Message, exception);
                }

                return inserted == 0
                    ? throw await RefusedCreateAsync(db, n, alias)
                    : (await FindAsync(db, n, "key", key))!;
            },
            cancellationToken);
    }

    /// <summary>
    /// The row whose <paramref name="column"/> equals <paramref name="value"/>, or null. For the alias this is plain
    /// equality: the column compares without regard to case on both databases (ADR-0033), and its unique index serves
    /// the lookup.
    /// </summary>
    public Task<TDto?> GetAsync(string column, object value, CancellationToken cancellationToken) =>
        database.ReadAsync(db => FindAsync(db, new SqlNames(db), column, value), cancellationToken);

    /// <summary>
    /// The aliases that start with <paramref name="prefix"/> in any letter case, in alias order: those to keep clear of
    /// when a row takes the next free number (ADR-0078). The column compares without regard to case on both databases,
    /// so <c>LIKE</c> does too (ADR-0033).
    /// </summary>
    public Task<IReadOnlyList<string>> ListAliasesAsync(string prefix, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        return database.ReadAsync<IReadOnlyList<string>>(
            async db =>
            {
                var n = new SqlNames(db);
                return await db.FetchListAsync<string>(
                    $"SELECT {n.C("alias")} FROM {n.T(table)} WHERE {n.C("alias")} LIKE @0 {SqlNames.EscapeClause} ORDER BY {n.C("alias")}",
                    SqlNames.Escape(prefix) + "%");
            },
            cancellationToken);
    }

    /// <summary>
    /// A page of the rows in name order, optionally only those whose name or alias contains <paramref name="search"/>,
    /// with the number of rows that match.
    /// </summary>
    public Task<Page<TDto>> ListAsync(string? search, PageRequest page, CancellationToken cancellationToken) =>
        database.ReadAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var t = n.T(table);
                var args = new SqlArgs();
                var clause = string.Empty;
                if (!string.IsNullOrWhiteSpace(search))
                {
                    var pattern = args.Add("%" + SqlNames.Escape(search.Trim()) + "%");
                    clause = $" WHERE {n.C("name")} LIKE {pattern} {SqlNames.EscapeClause} OR {n.C("alias")} LIKE {pattern} {SqlNames.EscapeClause}";
                }

                var total = await db.ScalarAsync<long>($"SELECT COUNT(*) FROM {t}{clause}", args.Values);
                var rows = total <= page.Skip
                    ? []
                    : await db.FetchListAsync<TDto>($"SELECT * FROM {t}{clause} ORDER BY {n.C("name")}, {n.C("id")} {n.Page(page.Skip, page.Take)}", args.Values);
                return new Page<TDto>(rows, total);
            },
            cancellationToken);

    /// <summary>
    /// Changes a fresh row and raises its concurrency version; null when the row does not exist. A null
    /// <paramref name="alias"/> keeps the alias: the themes' is fixed, and the other repositories require one. Refused with <see cref="FormBuilderConcurrencyException"/> when the row
    /// has changed since <paramref name="expectedConcurrencyVersion"/>, and with
    /// <see cref="FormBuilderConflict.DuplicateAlias"/> when another row has the new alias in any letter case.
    /// </summary>
    public Task<TDto?> UpdateAsync(int id, int expectedConcurrencyVersion, string? alias, string name, IReadOnlyList<LibraryColumn> content, CancellationToken cancellationToken)
    {
        if (alias is not null)
        {
            CheckAlias(alias);
        }

        CheckName(name);
        return database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var t = n.T(table);
                var args = new SqlArgs();
                var idArg = args.Add(id);
                var set = $"{n.C("name")} = {args.Add(name)}"
                    + string.Concat(content.Select(column => $", {n.C(column.Name)} = {args.Add(column.Value)}"));
                var aliasFree = string.Empty;
                if (alias is not null)
                {
                    var aliasArg = args.Add(alias);
                    set += $", {n.C("alias")} = {aliasArg}";
                    aliasFree = $" AND NOT EXISTS (SELECT 1 FROM {t} o{n.InsertOnceHint} WHERE o.{n.C("alias")} = {aliasArg} AND o.{n.C("id")} <> {idArg})";
                }

                var version = n.C("concurrencyVersion");
                var now = args.Add(database.UtcNow);
                var expected = args.Add(expectedConcurrencyVersion);
                var updated = await TranslateDuplicatesAsync(() => db.ExecAsync(
                    $"UPDATE {t} SET {set}, {version} = {version} + 1, {n.C("updatedUtc")} = {now} WHERE {n.C("id")} = {idArg} AND {version} = {expected}{aliasFree}",
                    args.Values));
                var current = await FindAsync(db, n, "id", id);
                if (updated == 1 || current is null)
                {
                    return current;
                }

                CheckFresh(current, expectedConcurrencyVersion);
                if (alias is null)
                {
                    throw new FormBuilderConcurrencyException($"The {noun} was changed by someone else meanwhile.");
                }

                throw new FormBuilderConflictException(FormBuilderConflict.DuplicateAlias, $"Another {noun} already has this alias.");
            },
            cancellationToken);
    }

    /// <summary>
    /// Deletes a fresh row; false when it does not exist. Refused with <see cref="FormBuilderConcurrencyException"/> when
    /// the row has changed since <paramref name="expectedConcurrencyVersion"/>, and with
    /// <see cref="FormBuilderConflict.InUse"/> when the condition of <paramref name="guard"/> does not hold.
    /// </summary>
    public Task<bool> DeleteAsync(int id, int expectedConcurrencyVersion, InUseGuard? guard, CancellationToken cancellationToken) =>
        database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var condition = guard is { } g ? " AND " + g.Unused(n) : string.Empty;
                var deleted = await db.ExecAsync(
                    $"DELETE FROM {n.T(table)} WHERE {n.C("id")} = @0 AND {n.C("concurrencyVersion")} = @1{condition}",
                    id,
                    expectedConcurrencyVersion);
                if (deleted == 1)
                {
                    return true;
                }

                var current = await FindAsync(db, n, "id", id);
                if (current is null)
                {
                    return false;
                }

                CheckFresh(current, expectedConcurrencyVersion);
                if (guard is not { } used)
                {
                    throw new FormBuilderConcurrencyException($"The {noun} was changed by someone else meanwhile.");
                }

                throw new FormBuilderConflictException(FormBuilderConflict.InUse, used.Message);
            },
            cancellationToken);

    private static void CheckAlias(string alias)
    {
        ArgumentNullException.ThrowIfNull(alias);
        if (alias.Length is 0 or > MaxAliasLength)
        {
            throw new ArgumentException($"An alias holds 1 to {MaxAliasLength} characters.", nameof(alias));
        }
    }

    private static void CheckName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Length is 0 or > MaxNameLength)
        {
            throw new ArgumentException($"A name holds 1 to {MaxNameLength} characters.", nameof(name));
        }
    }

    private Task<TDto?> FindAsync(IUmbracoDatabase db, SqlNames n, string column, object value) =>
        db.FirstOrNullAsync<TDto>($"SELECT * FROM {n.T(table)} WHERE {n.C(column)} = @0", value);

    private void CheckFresh(TDto current, int expected)
    {
        if (current.ConcurrencyVersion != expected)
        {
            throw new FormBuilderConcurrencyException($"The {noun} was changed by someone else meanwhile (version {current.ConcurrencyVersion}, not {expected}).");
        }
    }

    private async Task<FormBuilderConflictException> RefusedCreateAsync(IUmbracoDatabase db, SqlNames n, string alias) =>
        await db.ScalarAsync<int>($"SELECT COUNT(*) FROM {n.T(table)} WHERE {n.C("alias")} = @0", alias) > 0
            ? new FormBuilderConflictException(FormBuilderConflict.DuplicateAlias, $"Another {noun} already has this alias.")
            : new FormBuilderConflictException(FormBuilderConflict.DuplicateKey, $"Another {noun} already has this key.");

    // The condition of an update keeps a taken alias out; the unique index is the backstop for a writer the database let
    // through at the same moment (ADR-0036). An update never changes the key.
    private async Task<int> TranslateDuplicatesAsync(Func<Task<int>> write)
    {
        try
        {
            return await write();
        }
        catch (Exception exception) when (FormBuilderDatabaseErrors.IsUniqueViolation(exception))
        {
            throw new FormBuilderConflictException(FormBuilderConflict.DuplicateAlias, $"Another {noun} already has this alias.", exception);
        }
    }
}
