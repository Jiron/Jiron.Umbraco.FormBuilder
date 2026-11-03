using NPoco;
using Umbraco.Cms.Infrastructure.Persistence;
using UmbracoConstants = Umbraco.Cms.Core.Constants;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

/// <summary>A form a content item names: the form's key, the content's property and the culture (empty for invariant).</summary>
/// <param name="FormKey">The form's key.</param>
/// <param name="PropertyAlias">The content's property that names it, a block list's own alias for a pick inside a block.</param>
/// <param name="Culture">The culture of the value, or empty for an invariant one.</param>
internal sealed record ContentFormReference(Guid FormKey, string PropertyAlias, string Culture);

/// <summary>
/// A document's current version as Umbraco's table holds it: what a save or a publish changes (a save its date, a publish
/// its id), so that the rebuild can tell whether a copy it read is still the document's values (ADR-0131).
/// </summary>
/// <param name="Id">The version's id.</param>
/// <param name="Date">The version's date, as read; only ever compared with another read.</param>
internal sealed record DocumentVersion(int Id, DateTime Date);

/// <summary>
/// The references of content to forms in <c>jfbReference</c>: which property of which content names a form, per culture
/// (ADR-0017). They name the form without a foreign key (ADR-0033): a write skips a form that no longer exists (ADR-0047),
/// and a form's permanent delete removes them.
/// </summary>
internal interface IReferenceRepository
{
    /// <summary>Deletes every reference to a form, in the unit of work that deletes the form (ADR-0047); how many went.</summary>
    Task<int> DeleteFormAsync(int formId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces a content item's references with these, in one unit of work: each once, and none to a form the library
    /// no longer holds, which a locking read of each form checks first. Returns how many were written.
    /// </summary>
    Task<int> ReplaceContentAsync(Guid contentKey, IReadOnlyCollection<ContentFormReference> references, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces a document's references as <see cref="ReplaceContentAsync"/> does, but only while its current version is
    /// still the one read before its values were: a locking read of the version comes first, so a save either shows here
    /// or waits, and its own write comes later. Returns how many were written, or null when the document has changed or
    /// gone and nothing was.
    /// </summary>
    Task<int?> ReplaceUnchangedContentAsync(int documentId, DocumentVersion read, Guid contentKey, IReadOnlyCollection<ContentFormReference> references, CancellationToken cancellationToken = default);

    /// <summary>Deletes a content item's references; how many went.</summary>
    Task<int> DeleteContentAsync(Guid contentKey, CancellationToken cancellationToken = default);

    /// <summary>The keys of the content items that name a form, each once, in key order; none for a form that does not exist.</summary>
    Task<IReadOnlyList<Guid>> ContentOfAsync(Guid formKey, CancellationToken cancellationToken = default);

    /// <summary>Whether any page uses a form.</summary>
    Task<bool> AnyAsync(CancellationToken cancellationToken = default);

    /// <summary>Whether a content item names a form.</summary>
    Task<bool> HoldsAsync(Guid formKey, Guid contentKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// The node ids of up to <paramref name="take"/> of Umbraco's documents after <paramref name="afterId"/>, in id
    /// order, the recycle bin's included and blueprints not: the rebuild's walk (ADR-0131), which a new or deleted
    /// document never makes skip one.
    /// </summary>
    Task<IReadOnlyList<int>> DocumentsAfterAsync(int afterId, int take, CancellationToken cancellationToken = default);

    /// <summary>The current versions of these documents, by node id; a document Umbraco no longer holds is left out.</summary>
    Task<IReadOnlyDictionary<int, DocumentVersion>> VersionsOfAsync(IReadOnlyCollection<int> documentIds, CancellationToken cancellationToken = default);

    /// <summary>Deletes the references of content items Umbraco no longer holds; how many went.</summary>
    Task<int> DeleteOrphansAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc />
internal sealed class ReferenceRepository(FormBuilderDatabase database) : IReferenceRepository
{
    /// <summary>The longest property alias and culture the columns hold.</summary>
    public const int MaxPropertyAliasLength = 255;

    /// <inheritdoc cref="MaxPropertyAliasLength" />
    public const int MaxCultureLength = 32;

    /// <inheritdoc />
    public Task<bool> AnyAsync(CancellationToken cancellationToken = default) =>
        database.ReadAsync(
            async db =>
            {
                var n = new SqlNames(db);
                return await db.ScalarAsync<int>($"SELECT COUNT(*) FROM {n.T(FormBuilderTables.Reference)}") > 0;
            },
            cancellationToken);

    /// <inheritdoc />
    /// <remarks>The primary key starts with the form, so the delete reads only the form's rows.</remarks>
    public Task<int> DeleteFormAsync(int formId, CancellationToken cancellationToken = default) =>
        database.WriteAsync(
            db =>
            {
                var n = new SqlNames(db);
                return db.ExecAsync($"DELETE FROM {n.T(FormBuilderTables.Reference)} WHERE {n.C("formId")} = @0", formId);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<int> ReplaceContentAsync(Guid contentKey, IReadOnlyCollection<ContentFormReference> references, CancellationToken cancellationToken = default)
    {
        var rows = Checked(references);
        return database.WriteAsync(db => ReplaceAsync(db, new SqlNames(db), contentKey, rows), cancellationToken);
    }

    /// <inheritdoc />
    /// <remarks>
    /// It reads <c>umbracoContentVersion</c>, whose id, node id, date and current columns Umbraco has kept since version 8.
    /// The version's row comes before the forms' rows, and nothing that holds a form's row waits for a version's, so this
    /// unit and a save, or a form's delete, wait in one order (ADR-0038).
    /// </remarks>
    public Task<int?> ReplaceUnchangedContentAsync(int documentId, DocumentVersion read, Guid contentKey, IReadOnlyCollection<ContentFormReference> references, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(read);
        var rows = Checked(references);
        return database.WriteAsync<int?>(
            async db =>
            {
                var n = new SqlNames(db);
                var current = await db.FirstOrNullAsync<VersionDto>(
                    $"SELECT {n.C("id")}, {n.C("nodeId")}, {n.C("versionDate")} FROM {n.T(ContentVersionTable)}{n.LockingRead} WHERE {n.C("nodeId")} = @0 AND {n.C("current")} = 1",
                    documentId);
                if (current is null || current.Id != read.Id || current.Date != read.Date)
                {
                    return null;
                }

                return await ReplaceAsync(db, n, contentKey, rows);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> DeleteContentAsync(Guid contentKey, CancellationToken cancellationToken = default) =>
        database.WriteAsync(
            db =>
            {
                var n = new SqlNames(db);
                return db.ExecAsync($"DELETE FROM {n.T(FormBuilderTables.Reference)} WHERE {n.C("contentKey")} = @0", contentKey);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<Guid>> ContentOfAsync(Guid formKey, CancellationToken cancellationToken = default) =>
        database.ReadAsync<IReadOnlyList<Guid>>(
            async db =>
            {
                var n = new SqlNames(db);
                var keys = await db.FetchListAsync<Guid>(
                    $"SELECT DISTINCT r.{n.C("contentKey")} FROM {n.T(FormBuilderTables.Reference)} r INNER JOIN {n.T(FormBuilderTables.Form)} f ON f.{n.C("id")} = r.{n.C("formId")} WHERE f.{n.C("key")} = @0",
                    formKey);
                return [.. keys.Order()];
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<bool> HoldsAsync(Guid formKey, Guid contentKey, CancellationToken cancellationToken = default) =>
        database.ReadAsync(
            async db =>
            {
                var n = new SqlNames(db);
                return await db.ExecuteScalarAsync<int>(
                    $"SELECT COUNT(*) FROM {n.T(FormBuilderTables.Reference)} r INNER JOIN {n.T(FormBuilderTables.Form)} f ON f.{n.C("id")} = r.{n.C("formId")} WHERE f.{n.C("key")} = @0 AND r.{n.C("contentKey")} = @1",
                    formKey,
                    contentKey) > 0;
            },
            cancellationToken);

    /// <inheritdoc />
    /// <remarks>It reads <c>umbracoNode</c>, whose id, key and object type columns Umbraco has kept since version 7.</remarks>
    public Task<IReadOnlyList<int>> DocumentsAfterAsync(int afterId, int take, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(take);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(take, PageRequest.MaxTake);
        return database.ReadAsync<IReadOnlyList<int>>(
            async db =>
            {
                var n = new SqlNames(db);
                return await db.FetchListAsync<int>(
                    $"SELECT {n.C("id")} FROM {n.T(UmbracoNodeTable)} WHERE {n.C("nodeObjectType")} = @0 AND {n.C("id")} > @1 ORDER BY {n.C("id")} {n.Page(0, take)}",
                    UmbracoConstants.ObjectTypes.Document,
                    afterId);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<int, DocumentVersion>> VersionsOfAsync(IReadOnlyCollection<int> documentIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documentIds);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(documentIds.Count, SqlArgs.MaxListLength);
        return database.ReadAsync<IReadOnlyDictionary<int, DocumentVersion>>(
            async db =>
            {
                if (documentIds.Count == 0)
                {
                    return new Dictionary<int, DocumentVersion>();
                }

                var n = new SqlNames(db);
                var args = new SqlArgs();
                var rows = await db.FetchListAsync<VersionDto>(
                    $"SELECT {n.C("id")}, {n.C("nodeId")}, {n.C("versionDate")} FROM {n.T(ContentVersionTable)} WHERE {n.C("current")} = 1 AND {n.C("nodeId")} IN ({args.List(documentIds.Select(id => (object?)id))})",
                    args.Values);
                return rows.ToDictionary(row => row.NodeId, row => new DocumentVersion(row.Id, row.Date));
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<int> DeleteOrphansAsync(CancellationToken cancellationToken = default) =>
        database.WriteAsync(
            db =>
            {
                var n = new SqlNames(db);
                var table = n.T(FormBuilderTables.Reference);
                var nodes = n.T(UmbracoNodeTable);
                return db.ExecAsync(
                    $"DELETE FROM {table} WHERE NOT EXISTS (SELECT 1 FROM {nodes} WHERE {nodes}.{n.C("uniqueId")} = {table}.{n.C("contentKey")} AND {nodes}.{n.C("nodeObjectType")} = @0)",
                    UmbracoConstants.ObjectTypes.Document);
            },
            cancellationToken);

    private const string UmbracoNodeTable = UmbracoConstants.DatabaseSchema.Tables.Node;

    private const string ContentVersionTable = UmbracoConstants.DatabaseSchema.Tables.ContentVersion;

    // The rows, each once, once their aliases and cultures fit the columns.
    private static List<ContentFormReference> Checked(IReadOnlyCollection<ContentFormReference> references)
    {
        ArgumentNullException.ThrowIfNull(references);
        var rows = references.Distinct().ToList();
        if (rows.Any(row => row.PropertyAlias.Length is 0 or > MaxPropertyAliasLength || row.Culture.Length > MaxCultureLength))
        {
            throw new ArgumentException($"A reference's property alias is 1 to {MaxPropertyAliasLength} characters and its culture at most {MaxCultureLength}.", nameof(references));
        }

        return rows;
    }

    private async Task<int> ReplaceAsync(IUmbracoDatabase db, SqlNames n, Guid contentKey, List<ContentFormReference> rows)
    {
        var table = n.T(FormBuilderTables.Reference);

        // The forms' rows first, each with a locking read and in key order (ADR-0038): a form deleted meanwhile gets
        // no reference (ADR-0047), and its delete, which takes the form's row before its references, waits for this
        // unit or this unit for it, never each for the other.
        var ids = new Dictionary<Guid, int>();
        foreach (var formKey in rows.Select(row => row.FormKey).Distinct().Order())
        {
            if (await db.ScalarAsync<int?>($"SELECT {n.C("id")} FROM {n.T(FormBuilderTables.Form)}{n.LockingRead} WHERE {n.C("key")} = @0", formKey) is { } id)
            {
                ids[formKey] = id;
            }
        }

        await db.ExecAsync($"DELETE FROM {table} WHERE {n.C("contentKey")} = @0", contentKey);
        var written = 0;
        foreach (var row in rows.Where(row => ids.ContainsKey(row.FormKey)))
        {
            written += await db.ExecAsync(
                $"INSERT INTO {table} ({n.C("formId")}, {n.C("contentKey")}, {n.C("propertyAlias")}, {n.C("culture")}, {n.C("updatedUtc")}) VALUES (@0, @1, @2, @3, @4)",
                ids[row.FormKey],
                contentKey,
                row.PropertyAlias,
                row.Culture,
                database.UtcNow);
        }

        return written;
    }

    [ExplicitColumns]
    private sealed class VersionDto
    {
        [Column("id")]
        public int Id { get; set; }

        [Column("nodeId")]
        public int NodeId { get; set; }

        [Column("versionDate")]
        public DateTime Date { get; set; }
    }
}
