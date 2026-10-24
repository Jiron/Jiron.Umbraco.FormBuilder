namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

/// <summary>
/// The references of content to forms in <c>jfbReference</c>: which property of which content names a form, per culture.
/// They name the form without a foreign key (ADR-0033). Step 6.7 writes and reads them; a form's permanent delete
/// removes them (ADR-0047).
/// </summary>
internal interface IReferenceRepository
{
    /// <summary>Deletes every reference to a form, in the unit of work that deletes the form (ADR-0047); how many went.</summary>
    Task<int> DeleteFormAsync(int formId, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
internal sealed class ReferenceRepository(FormBuilderDatabase database) : IReferenceRepository
{
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
}
