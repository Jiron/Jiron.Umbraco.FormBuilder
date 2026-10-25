using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Dtos;
using Umbraco.Cms.Infrastructure.Persistence;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

// The field catalogue (ADR-0003): a row for every field ever published, and for draft-only fields that preview entries
// answered, so entry values can refer to a field by an integer id.
internal sealed partial class FormRepository
{
    /// <summary>The longest label the column holds.</summary>
    public const int MaxLabelLength = 500;

    /// <summary>The longest field type alias the column holds.</summary>
    public const int MaxTypeLength = 128;


    /// <inheritdoc />
    public Task<IReadOnlyList<CatalogueField>> ListFieldsAsync(int formId, CancellationToken cancellationToken = default) =>
        database.ReadAsync<IReadOnlyList<CatalogueField>>(
            async db =>
            {
                var n = new SqlNames(db);
                var rows = await db.FetchListAsync<FormFieldDto>(
                    $"SELECT * FROM {n.T(FormBuilderTables.FormField)} WHERE {n.C("formId")} = @0 ORDER BY {n.C("sortOrder")}, {n.C("id")}",
                    formId);
                return rows.Select(FieldFromDto).ToList();
            },
            cancellationToken);

    /// <inheritdoc />
    public Task<CatalogueField?> AddFieldAsync(int formId, CatalogueFieldValues field, CancellationToken cancellationToken = default)
    {
        CheckField(field);
        return database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var table = n.T(FormBuilderTables.FormField);
                int inserted;
                try
                {
                    inserted = await db.ExecAsync(
                        $"""
                        INSERT INTO {table} ({n.C("formId")}, {n.C("fieldKey")}, {n.C("alias")}, {n.C("label")}, {n.C("fieldType")}, {n.C("storageKind")}, {n.C("isSensitive")}, {n.C("sortOrder")}, {n.C("firstVersionNumber")}, {n.C("lastVersionNumber")}, {n.C("parentFieldKey")})
                        SELECT @0, @1, @2, @3, @4, @5, @6, @7, @8, @9, @10
                        WHERE EXISTS (SELECT 1 FROM {n.T(FormBuilderTables.Form)}{n.LockingRead} WHERE {n.C("id")} = @0)
                        AND NOT EXISTS (SELECT 1 FROM {table}{n.InsertOnceHint} WHERE {n.C("formId")} = @0 AND {n.C("fieldKey")} = @1)
                        """,
                        formId,
                        field.FieldKey,
                        field.Alias,
                        field.Label,
                        field.Type,
                        StoredNames.Of(field.StorageKind),
                        field.IsSensitive,
                        field.SortOrder,
                        field.FirstVersionNumber,
                        field.LastVersionNumber,
                        field.ParentFieldKey);
                }
                catch (Exception exception) when (FormBuilderDatabaseErrors.IsUniqueViolation(exception))
                {
                    throw new FormBuilderConflictException(FormBuilderConflict.DuplicateKey, "The catalogue has this field already.", exception);
                }

                // Nothing inserted: the form does not exist (no field either), or the catalogue has the field.
                var stored = await FindFieldAsync(db, formId, field.FieldKey);
                return inserted == 1 || stored is null
                    ? stored
                    : throw new FormBuilderConflictException(FormBuilderConflict.DuplicateKey, "The catalogue has this field already.");
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<CatalogueField?> UpdateFieldAsync(int formId, CatalogueFieldValues field, CancellationToken cancellationToken = default)
    {
        CheckField(field);
        return database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                await db.ExecAsync(
                    $"""
                    UPDATE {n.T(FormBuilderTables.FormField)} SET {n.C("alias")} = @0, {n.C("label")} = @1, {n.C("fieldType")} = @2, {n.C("storageKind")} = @3, {n.C("isSensitive")} = @4, {n.C("sortOrder")} = @5,
                    {n.C("firstVersionNumber")} = @6, {n.C("lastVersionNumber")} = @7, {n.C("parentFieldKey")} = @8
                    WHERE {n.C("formId")} = @9 AND {n.C("fieldKey")} = @10
                    """,
                    field.Alias,
                    field.Label,
                    field.Type,
                    StoredNames.Of(field.StorageKind),
                    field.IsSensitive,
                    field.SortOrder,
                    field.FirstVersionNumber,
                    field.LastVersionNumber,
                    field.ParentFieldKey,
                    formId,
                    field.FieldKey);
                return await FindFieldAsync(db, formId, field.FieldKey);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> DeleteFieldAsync(int formId, Guid fieldKey, CancellationToken cancellationToken = default) =>
        database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var table = n.T(FormBuilderTables.FormField);
                var deleted = await db.ExecAsync(
                    $"DELETE FROM {table} WHERE {n.C("formId")} = @0 AND {n.C("fieldKey")} = @1 AND NOT EXISTS (SELECT 1 FROM {n.T(FormBuilderTables.EntryValue)} v{n.LockingRead} WHERE v.{n.C("fieldId")} = {table}.{n.C("id")})",
                    formId,
                    fieldKey);
                if (deleted == 1)
                {
                    return true;
                }

                return await FindFieldAsync(db, formId, fieldKey) is null
                    ? false
                    : throw new FormBuilderConflictException(FormBuilderConflict.InUse, "Entry values still refer to this field.");
            },
            cancellationToken);

    internal static CatalogueField FieldFromDto(FormFieldDto d) => new(
        d.Id,
        d.FormId,
        new CatalogueFieldValues(
            d.FieldKey,
            d.Alias,
            d.Label,
            d.FieldType,
            StoredNames.Parse<FieldStorageKind>(d.StorageKind),
            d.IsSensitive,
            d.SortOrder,
            d.FirstVersionNumber,
            d.LastVersionNumber,
            d.ParentFieldKey));

    private static async Task<CatalogueField?> FindFieldAsync(IUmbracoDatabase db, int formId, Guid fieldKey)
    {
        var n = new SqlNames(db);
        var row = await db.FirstOrNullAsync<FormFieldDto>(
            $"SELECT * FROM {n.T(FormBuilderTables.FormField)} WHERE {n.C("formId")} = @0 AND {n.C("fieldKey")} = @1",
            formId,
            fieldKey);
        return row is null ? null : FieldFromDto(row);
    }

    private static void CheckField(CatalogueFieldValues field)
    {
        ArgumentNullException.ThrowIfNull(field);
        Check(field.Alias, 1, MaxAliasLength, "alias");
        Check(field.Label, 0, MaxLabelLength, "label");
        Check(field.Type, 1, MaxTypeLength, "type");
        if (!Enum.IsDefined(field.StorageKind))
        {
            throw new ArgumentOutOfRangeException(nameof(field), StoredNames.Describe(field.StorageKind), "A field's storage kind is one of the kinds of section 4.3.2.");
        }

        static void Check(string value, int min, int max, string what)
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value.Length < min || value.Length > max)
            {
                throw new ArgumentException($"A catalogue field's {what} holds {min} to {max} characters.", nameof(field));
            }
        }
    }
}
