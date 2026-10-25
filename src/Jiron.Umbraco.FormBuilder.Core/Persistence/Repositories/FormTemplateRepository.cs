using Jiron.Umbraco.FormBuilder.Core.Persistence.Dtos;
using Umbraco.Cms.Infrastructure.Persistence;

namespace Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;

/// <summary>What a saved template holds.</summary>
internal enum FormTemplateKind
{
    /// <summary>A whole form, to create new forms from.</summary>
    Form,

    /// <summary>A group of fields the designer inserts into a form as a copy with new keys.</summary>
    Group,
}

/// <summary>A template a user saved (12.3). The built-in templates ship inside the package and have no row.</summary>
/// <param name="Id">The row's id.</param>
/// <param name="Key">Its key.</param>
/// <param name="Kind">A form or a group.</param>
/// <param name="Name">The name the gallery shows.</param>
/// <param name="Description">A description, or null.</param>
/// <param name="Category">The gallery's category, or null.</param>
/// <param name="Definition">The definition as JSON.</param>
/// <param name="CreatedUtc">When it was saved.</param>
/// <param name="CreatedByUserKey">Who saved it, or null.</param>
/// <param name="UpdatedUtc">When it was last changed; when it was saved, until a change.</param>
/// <param name="UpdatedByUserKey">Who changed it last, or null.</param>
internal sealed record StoredFormTemplate(
    int Id,
    Guid Key,
    FormTemplateKind Kind,
    string Name,
    string? Description,
    string? Category,
    string Definition,
    DateTimeOffset CreatedUtc,
    Guid? CreatedByUserKey,
    DateTimeOffset UpdatedUtc,
    Guid? UpdatedByUserKey);

/// <summary>A template to save.</summary>
/// <param name="Key">Its key.</param>
/// <param name="Kind">A form or a group.</param>
/// <param name="Name">Its name.</param>
/// <param name="Definition">The definition as JSON.</param>
/// <param name="Description">A description, or null.</param>
/// <param name="Category">The gallery's category, or null.</param>
/// <param name="UserKey">Who saves it, or null.</param>
internal sealed record NewFormTemplate(
    Guid Key,
    FormTemplateKind Kind,
    string Name,
    string Definition,
    string? Description = null,
    string? Category = null,
    Guid? UserKey = null);

/// <summary>A change to a template. Templates carry no concurrency version: the last change wins.</summary>
/// <param name="Id">The template.</param>
/// <param name="Name">The name.</param>
/// <param name="Description">A description, or null.</param>
/// <param name="Category">The gallery's category, or null.</param>
/// <param name="Definition">The definition as JSON.</param>
/// <param name="UserKey">Who changes it, or null.</param>
internal sealed record FormTemplateUpdate(int Id, string Name, string? Description, string? Category, string Definition, Guid? UserKey = null);

/// <summary>Which templates a list holds; every condition left null holds for all.</summary>
/// <param name="Kind">Forms or groups.</param>
/// <param name="Category">One category, in any letter case.</param>
/// <param name="Search">Text the name or the description contains.</param>
internal sealed record FormTemplateFilter(FormTemplateKind? Kind = null, string? Category = null, string? Search = null);

/// <summary>The templates users saved, in <c>jfbFormTemplate</c> (ADR-0037).</summary>
internal interface IFormTemplateRepository
{
    /// <summary>Saves a template. Refused with <see cref="FormBuilderConflict.DuplicateKey"/> for a taken key.</summary>
    Task<StoredFormTemplate> CreateAsync(NewFormTemplate template, CancellationToken cancellationToken = default);

    /// <summary>The template with this key, or null.</summary>
    Task<StoredFormTemplate?> GetAsync(Guid key, CancellationToken cancellationToken = default);

    /// <summary>The template with this id, or null.</summary>
    Task<StoredFormTemplate?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>A page of the templates the filter allows, in name order, with the number that match.</summary>
    Task<Page<StoredFormTemplate>> ListAsync(FormTemplateFilter filter, PageRequest page, CancellationToken cancellationToken = default);

    /// <summary>Changes a template; null when it does not exist.</summary>
    Task<StoredFormTemplate?> UpdateAsync(FormTemplateUpdate update, CancellationToken cancellationToken = default);

    /// <summary>Deletes a template. True when it existed.</summary>
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
internal sealed class FormTemplateRepository(FormBuilderDatabase database) : IFormTemplateRepository
{
    /// <summary>The longest name the column holds.</summary>
    public const int MaxNameLength = 255;

    /// <summary>The longest description the column holds.</summary>
    public const int MaxDescriptionLength = 2000;

    /// <summary>The longest category the column holds.</summary>
    public const int MaxCategoryLength = 64;

    private const string DuplicateKey = "Another template already has this key.";

    /// <inheritdoc />
    public Task<StoredFormTemplate> CreateAsync(NewFormTemplate template, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (!Enum.IsDefined(template.Kind))
        {
            throw new ArgumentOutOfRangeException(nameof(template), template.Kind, "A template holds a form or a group.");
        }

        Check(template.Name, template.Description, template.Category, template.Definition);
        return database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var table = n.T(FormBuilderTables.FormTemplate);
                int inserted;
                try
                {
                    inserted = await db.ExecAsync(
                        $"""
                        INSERT INTO {table} ({n.C("key")}, {n.C("kind")}, {n.C("name")}, {n.C("description")}, {n.C("category")}, {n.C("definition")}, {n.C("createdUtc")}, {n.C("createdByUserKey")}, {n.C("updatedUtc")}, {n.C("updatedByUserKey")})
                        SELECT @0, @1, @2, @3, @4, @5, @6, @7, @6, @7
                        WHERE NOT EXISTS (SELECT 1 FROM {table}{n.InsertOnceHint} WHERE {n.C("key")} = @0)
                        """,
                        template.Key,
                        StoredNames.Of(template.Kind),
                        template.Name,
                        template.Description,
                        template.Category,
                        template.Definition,
                        database.UtcNow,
                        template.UserKey);
                }
                catch (Exception exception) when (FormBuilderDatabaseErrors.IsUniqueViolation(exception))
                {
                    throw new FormBuilderConflictException(FormBuilderConflict.DuplicateKey, DuplicateKey, exception);
                }

                return inserted == 0
                    ? throw new FormBuilderConflictException(FormBuilderConflict.DuplicateKey, DuplicateKey)
                    : (await FindAsync(db, n, "key", template.Key))!;
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<StoredFormTemplate?> GetAsync(Guid key, CancellationToken cancellationToken = default) =>
        database.ReadAsync(db => FindAsync(db, new SqlNames(db), "key", key), cancellationToken);

    /// <inheritdoc />
    public Task<StoredFormTemplate?> GetByIdAsync(int id, CancellationToken cancellationToken = default) =>
        database.ReadAsync(db => FindAsync(db, new SqlNames(db), "id", id), cancellationToken);

    /// <inheritdoc />
    public Task<Page<StoredFormTemplate>> ListAsync(FormTemplateFilter filter, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        return database.ReadAsync(
            async db =>
            {
                var n = new SqlNames(db);
                var args = new SqlArgs();

                var where = new List<string>();
                if (filter.Kind is { } kind)
                {
                    where.Add($"{n.C("kind")} = {args.Add(StoredNames.Of(kind))}");
                }

                if (filter.Category is { } category)
                {
                    where.Add($"{n.C("category")} = {args.Add(category)}");
                }

                if (!string.IsNullOrWhiteSpace(filter.Search))
                {
                    var pattern = args.Add("%" + SqlNames.Escape(filter.Search.Trim()) + "%");
                    where.Add($"({n.C("name")} LIKE {pattern} {SqlNames.EscapeClause} OR {n.C("description")} LIKE {pattern} {SqlNames.EscapeClause})");
                }

                var table = n.T(FormBuilderTables.FormTemplate);
                var clause = where.Count == 0 ? string.Empty : " WHERE " + string.Join(" AND ", where);
                var total = await db.ScalarAsync<long>($"SELECT COUNT(*) FROM {table}{clause}", args.Values);
                var rows = total <= page.Skip
                    ? []
                    : await db.FetchListAsync<FormTemplateDto>($"SELECT * FROM {table}{clause} ORDER BY {n.C("name")}, {n.C("id")} {n.Page(page.Skip, page.Take)}", args.Values);
                return new Page<StoredFormTemplate>(rows.Select(FromDto).ToList(), total);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<StoredFormTemplate?> UpdateAsync(FormTemplateUpdate update, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);
        Check(update.Name, update.Description, update.Category, update.Definition);
        return database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                await db.ExecAsync(
                    $"UPDATE {n.T(FormBuilderTables.FormTemplate)} SET {n.C("name")} = @0, {n.C("description")} = @1, {n.C("category")} = @2, {n.C("definition")} = @3, {n.C("updatedUtc")} = @5, {n.C("updatedByUserKey")} = @6 WHERE {n.C("id")} = @4",
                    update.Name,
                    update.Description,
                    update.Category,
                    update.Definition,
                    update.Id,
                    database.UtcNow,
                    update.UserKey);
                return await FindAsync(db, n, "id", update.Id);
            },
            cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default) =>
        database.WriteAsync(
            async db =>
            {
                var n = new SqlNames(db);
                return await db.ExecAsync($"DELETE FROM {n.T(FormBuilderTables.FormTemplate)} WHERE {n.C("id")} = @0", id) == 1;
            },
            cancellationToken);

    private static void Check(string name, string? description, string? category, string definition)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (name.Length is 0 or > MaxNameLength)
        {
            throw new ArgumentException($"A template's name holds 1 to {MaxNameLength} characters.", nameof(name));
        }

        // Null stands for none, so an empty text would be a second way to say it.
        if (description is { Length: 0 or > MaxDescriptionLength })
        {
            throw new ArgumentException($"A template's description is null or 1 to {MaxDescriptionLength} characters.", nameof(description));
        }

        if (category is { Length: 0 or > MaxCategoryLength })
        {
            throw new ArgumentException($"A template's category is null or 1 to {MaxCategoryLength} characters.", nameof(category));
        }

        ArgumentException.ThrowIfNullOrEmpty(definition);
    }

    private static async Task<StoredFormTemplate?> FindAsync(IUmbracoDatabase db, SqlNames n, string column, object value)
    {
        var row = await db.FirstOrNullAsync<FormTemplateDto>($"SELECT * FROM {n.T(FormBuilderTables.FormTemplate)} WHERE {n.C(column)} = @0", value);
        return row is null ? null : FromDto(row);
    }

    private static StoredFormTemplate FromDto(FormTemplateDto d) => new(
        d.Id,
        d.Key,
        StoredNames.Parse<FormTemplateKind>(d.Kind),
        d.Name,
        d.Description,
        d.Category,
        d.Definition,
        FormBuilderDbTime.Read(d.CreatedUtc),
        d.CreatedByUserKey,
        FormBuilderDbTime.Read(d.UpdatedUtc),
        d.UpdatedByUserKey);
}
