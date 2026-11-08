using Jiron.Umbraco.FormBuilder.Core.Forms;
using NPoco;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

/// <summary>A form whose draft or live version uses an item of the library (ADR-0214).</summary>
/// <param name="FormId">The form's row.</param>
/// <param name="FormKey">Its key.</param>
/// <param name="FormName">Its name.</param>
/// <param name="FolderId">Its folder, or null at the root.</param>
/// <param name="IsTrashed">Whether it is in the recycle bin, from which it can come back.</param>
/// <param name="InDraft">Whether its draft uses the item.</param>
/// <param name="InLive">Whether the version the site serves uses it.</param>
/// <param name="Uses">How many of its elements use it in the version that uses it most.</param>
internal sealed record LibraryUser(int FormId, Guid FormKey, string FormName, int? FolderId, bool IsTrashed, bool InDraft, bool InLive, int Uses);

/// <summary>
/// What of the library forms use, as <c>jfbFormLibraryUse</c> holds it (ADR-0214): the form repository writes the rows
/// with every version it writes, and this reads them for the library's "used by" and its delete guards.
/// </summary>
internal interface ILibraryUseRepository
{
    /// <summary>The forms whose draft or live version uses the item, by name.</summary>
    Task<IReadOnlyList<LibraryUser>> UsersAsync(LibraryUseKind kind, Guid key, CancellationToken cancellationToken = default);

    /// <summary>How many forms use each item of a kind that any form uses, by the item's key.</summary>
    Task<IReadOnlyDictionary<Guid, int>> FormCountsAsync(LibraryUseKind kind, CancellationToken cancellationToken = default);

    /// <summary>
    /// How many elements use each item of a kind: per form the more of its draft's and its live version's, summed over the
    /// forms, so an email layout says how many actions name it (ADR-0215).
    /// </summary>
    Task<IReadOnlyDictionary<Guid, int>> UseCountsAsync(LibraryUseKind kind, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
internal sealed class LibraryUseRepository(FormBuilderDatabase database) : ILibraryUseRepository
{
    /// <inheritdoc />
    public Task<IReadOnlyList<LibraryUser>> UsersAsync(LibraryUseKind kind, Guid key, CancellationToken cancellationToken = default) =>
        database.ReadAsync<IReadOnlyList<LibraryUser>>(
            async db =>
            {
                var n = new SqlNames(db);
                var rows = await db.FetchListAsync<UseRow>(
                    $"""
                    SELECT f.{n.C("id")} AS {n.C("formId")}, f.{n.C("key")} AS {n.C("formKey")}, f.{n.C("name")} AS {n.C("formName")}, f.{n.C("folderId")} AS {n.C("folderId")},
                        f.{n.C("trashedUtc")} AS {n.C("trashedUtc")}, u.{n.C("versionKind")} AS {n.C("versionKind")}, u.{n.C("uses")} AS {n.C("uses")}
                    FROM {n.T(FormBuilderTables.FormLibraryUse)} u INNER JOIN {n.T(FormBuilderTables.Form)} f ON f.{n.C("id")} = u.{n.C("formId")}
                    WHERE u.{n.C("itemKind")} = @0 AND u.{n.C("itemKey")} = @1
                    """,
                    StoredNames.Of(kind),
                    key);
                return
                [
                    .. rows
                        .GroupBy(row => row.FormId)
                        .Select(form =>
                        {
                            var first = form.First();
                            var draft = StoredNames.Of(FormVersionKind.Draft);
                            return new LibraryUser(
                                first.FormId,
                                first.FormKey,
                                first.FormName,
                                first.FolderId,
                                first.TrashedUtc is not null,
                                form.Any(row => row.VersionKind == draft),
                                form.Any(row => row.VersionKind != draft),
                                form.Max(row => row.Uses));
                        })
                        .OrderBy(user => user.FormName, StringComparer.CurrentCultureIgnoreCase)
                        .ThenBy(user => user.FormKey),
                ];
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<Guid, int>> FormCountsAsync(LibraryUseKind kind, CancellationToken cancellationToken = default) =>
        database.ReadAsync<IReadOnlyDictionary<Guid, int>>(
            async db =>
            {
                var n = new SqlNames(db);
                var rows = await db.FetchListAsync<CountRow>(
                    $"SELECT {n.C("itemKey")} AS {n.C("itemKey")}, COUNT(DISTINCT {n.C("formId")}) AS {n.C("forms")} FROM {n.T(FormBuilderTables.FormLibraryUse)} WHERE {n.C("itemKind")} = @0 GROUP BY {n.C("itemKey")}",
                    StoredNames.Of(kind));
                return rows.ToDictionary(row => row.ItemKey, row => row.Forms);
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyDictionary<Guid, int>> UseCountsAsync(LibraryUseKind kind, CancellationToken cancellationToken = default) =>
        database.ReadAsync<IReadOnlyDictionary<Guid, int>>(
            async db =>
            {
                var n = new SqlNames(db);
                var rows = await db.FetchListAsync<CountRow>(
                    $"""
                    SELECT m.{n.C("itemKey")} AS {n.C("itemKey")}, SUM(m.{n.C("uses")}) AS {n.C("forms")}
                    FROM (SELECT {n.C("formId")}, {n.C("itemKey")}, MAX({n.C("uses")}) AS {n.C("uses")} FROM {n.T(FormBuilderTables.FormLibraryUse)} WHERE {n.C("itemKind")} = @0 GROUP BY {n.C("formId")}, {n.C("itemKey")}) m
                    GROUP BY m.{n.C("itemKey")}
                    """,
                    StoredNames.Of(kind));
                return rows.ToDictionary(row => row.ItemKey, row => row.Forms);
            },
            cancellationToken);

    // One version's use, with its form.
    [ExplicitColumns]
    private sealed class UseRow
    {
        [Column("formId")]
        public int FormId { get; set; }

        [Column("formKey")]
        public Guid FormKey { get; set; }

        [Column("formName")]
        public string FormName { get; set; } = string.Empty;

        [Column("folderId")]
        public int? FolderId { get; set; }

        [Column("trashedUtc")]
        public DateTime? TrashedUtc { get; set; }

        [Column("versionKind")]
        public string VersionKind { get; set; } = string.Empty;

        [Column("uses")]
        public int Uses { get; set; }
    }

    // The forms that use one item.
    [ExplicitColumns]
    private sealed class CountRow
    {
        [Column("itemKey")]
        public Guid ItemKey { get; set; }

        [Column("forms")]
        public int Forms { get; set; }
    }
}
