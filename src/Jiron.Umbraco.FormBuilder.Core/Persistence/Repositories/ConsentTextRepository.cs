using Jiron.Umbraco.FormBuilder.Core.Persistence.Dtos;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

/// <summary>The exact wording of a consent as shown, named by its hash (ADR-0009).</summary>
/// <param name="Hash">The SHA-256 of the culture and the text in lowercase hex.</param>
/// <param name="Culture">The culture, or "" for the invariant culture.</param>
/// <param name="Text">The wording.</param>
/// <param name="FirstSeenUtc">When it was first stored.</param>
internal sealed record StoredConsentText(string Hash, string Culture, string Text, DateTimeOffset FirstSeenUtc);

/// <summary>
/// The consent wordings visitors were shown (<c>jfbConsentText</c>, ADR-0009, ADR-0038): stored once under their hash,
/// which the consent values of every entry name, and never changed.
/// </summary>
internal interface IConsentTextRepository
{
    /// <summary>
    /// Stores a consent's wording once, and returns its hash (<see cref="ConsentTextHash"/>): what the consent value
    /// stores. A wording stored before is left as it is, so the first time it was seen stays.
    /// </summary>
    Task<string> AddAsync(string culture, string text, CancellationToken cancellationToken = default);

    /// <summary>The consent wording with this hash, or null.</summary>
    Task<StoredConsentText?> GetAsync(string hash, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
internal sealed class ConsentTextRepository(FormBuilderDatabase database) : IConsentTextRepository
{
    /// <inheritdoc />
    /// <remarks>
    /// One insert-once on the hash (ADR-0036). The unique key is the backstop for a writer the database let through at
    /// the same moment; the hash names the culture and wording, so that row holds the same wording and the refusal is
    /// the answer.
    /// </remarks>
    public Task<string> AddAsync(string culture, string text, CancellationToken cancellationToken = default)
    {
        var hash = ConsentTextHash.For(culture, text);
        return database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var table = n.T(FormBuilderTables.ConsentText);
                try
                {
                    await db.ExecAsync(
                        $"INSERT INTO {table} ({n.C("textHash")}, {n.C("culture")}, {n.C("text")}, {n.C("firstSeenUtc")}) SELECT @0, @1, @2, @3 WHERE NOT EXISTS (SELECT 1 FROM {table}{n.InsertOnceHint} WHERE {n.C("textHash")} = @0)",
                        hash,
                        culture,
                        text,
                        database.UtcNow);
                }
                catch (Exception exception) when (FormBuilderDatabaseErrors.IsUniqueViolation(exception))
                {
                    // Stored at the same moment by another writer: the same wording, under the same hash.
                }

                return hash;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<StoredConsentText?> GetAsync(string hash, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(hash);
        return database.ReadAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var row = await db.FirstOrNullAsync<ConsentTextDto>($"SELECT * FROM {n.T(FormBuilderTables.ConsentText)} WHERE {n.C("textHash")} = @0", hash);
                return row is null ? null : new StoredConsentText(row.TextHash, row.Culture, row.Text, FormBuilderDbTime.Read(row.FirstSeenUtc));
            },
            cancellationToken);
    }
}
