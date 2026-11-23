using System.Security.Cryptography;
using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Queries;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Testing.Time;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;

/// <summary>
/// A migrated database per test with a clock that stands still (ADR-0011), the repositories, and builders for the rows
/// the tests need.
/// </summary>
public abstract class RepositoryTestBase : FormBuilderMigrationTestBase
{
    /// <summary>A fixed instant whose milliseconds survive SQL Server's <c>datetime</c> (which rounds to 1/300 s).</summary>
    private protected static readonly DateTimeOffset Now = new(2026, 10, 23, 8, 30, 15, 123, TimeSpan.Zero);

    /// <summary>The editor the changes are made by.</summary>
    private protected static readonly Guid Editor = Guid.Parse("7d1c3a52-31f4-4e57-9d2e-61b0f6a1c001");

    private protected TestTimeProvider Clock { get; private set; } = null!;

    private protected FormBuilderDatabase Database => Repo<FormBuilderDatabase>();

    private protected IKeyValueRepository KeyValues => Repo<IKeyValueRepository>();

    private protected IFolderRepository Folders => Repo<IFolderRepository>();

    private protected IFormRepository Forms => Repo<IFormRepository>();

    private protected ISettingsRepository Settings => Repo<ISettingsRepository>();

    private protected IOptionListRepository OptionLists => Repo<IOptionListRepository>();

    private protected IThemeRepository Themes => Repo<IThemeRepository>();

    private protected IEmailLayoutRepository EmailLayouts => Repo<IEmailLayoutRepository>();

    private protected IFormTemplateRepository Templates => Repo<IFormTemplateRepository>();

    private protected IPermissionRepository Permissions => Repo<IPermissionRepository>();

    private protected IEntryRepository Entries => Repo<IEntryRepository>();

    private protected IConsentTextRepository ConsentTexts => Repo<IConsentTextRepository>();

    private protected IEntryEventRepository EntryEvents => Repo<IEntryEventRepository>();

    private protected IEntryNoteRepository EntryNotes => Repo<IEntryNoteRepository>();

    private protected IEntryQueryRepository EntryQueries => Repo<IEntryQueryRepository>();

    private protected IActionRunRepository ActionRuns => Repo<IActionRunRepository>();

    private protected IEmailRepository Emails => Repo<IEmailRepository>();

    private protected IAuditRepository Audit => Repo<IAuditRepository>();

    private protected IDailyStatRepository Stats => Repo<IDailyStatRepository>();

    [SetUp]
    public async Task MigrateAsync()
    {
        var result = await RunAsync(new FormBuilderMigrationPlan());
        Assert.That(result.Successful, Is.True, result.Exception?.ToString());
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        base.CustomTestSetup(builder);
        Clock = new TestTimeProvider(Now);
        builder.Services.AddUnique<TimeProvider>(Clock);
    }

    private protected T Repo<T>()
        where T : notnull => GetRequiredService<T>();

    /// <summary>A version document whose hashes are those of its texts.</summary>
    private protected static VersionDocument Document(string definition = """{"pages":[]}""", string? publicProjection = null) =>
        new(1, definition, Sha256(definition), Sha256(publicProjection ?? definition));

    private protected static NewForm NewForm(string? alias = null, int? folderId = null, string name = "Contact") =>
        new(Guid.NewGuid(), folderId, alias ?? "f" + Hex(6), name, Document(), Comment: "Created", UserKey: Editor);

    private protected async Task<StoredForm> CreateFormAsync(string? alias = null, int? folderId = null, string name = "Contact") =>
        (await Forms.CreateAsync(NewForm(alias, folderId, name))).Form;

    private protected async Task<StoredFolder> CreateFolderAsync(string name = "Folder", int? parentId = null, int sortOrder = 0) =>
        await Folders.CreateAsync(new NewFolder(Guid.NewGuid(), parentId, name, sortOrder, UserKey: Editor));

    /// <summary>A form with text fields of these aliases in its catalogue.</summary>
    private protected async Task<TestForm> CreateTestFormAsync(params string[] fields)
    {
        var form = await CreateFormAsync();
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var alias in fields)
        {
            ids[alias] = (await Forms.AddFieldAsync(form.Id, FormRepositoryTests.Field(alias)))!.Id;
        }

        return new TestForm(form.Id, form.DraftVersionId!.Value, ids);
    }

    /// <summary>An entry to store for a form, pointing at one of its versions, with a fresh key and submission id.</summary>
    private protected static NewEntry NewEntry(int formId, int versionId, params NewEntryValue[] values) => new()
    {
        Key = Guid.NewGuid(),
        FormId = formId,
        FormVersionId = versionId,
        SubmissionId = Guid.NewGuid(),
        SubmissionHash = Hex(32),
        Culture = "en-US",
        Values = values,
    };

    /// <summary>An entry of a form, pointing at one of its versions; its id.</summary>
    private protected async Task<long> AddEntryAsync(int formId, int versionId) =>
        (await Entries.CreateAsync(NewEntry(formId, versionId)))!.Id;

    /// <summary>A value of an entry, for a field of the catalogue.</summary>
    private protected Task AddValueAsync(long entryId, int fieldId) =>
        Entries.ReplaceValuesAsync(entryId, [fieldId], [new NewEntryValue(fieldId, "JFB_TEST")], string.Empty);

    /// <summary>
    /// A condition that holds and takes far longer than any budget to find so, put in front of a statement's own
    /// conditions to make it slow. On SQL Server it counts the rows of a cross join that pass a test of all its sides at
    /// once: a plain count of a cross join need not be slow there, as the optimizer may count each side and multiply, and
    /// the statement then ends at once (ROADMAP 26.1). <see cref="AuditRepositoryTests"/> checks it is slow on each database.
    /// </summary>
    private protected string SlowCondition => IsSqlite
        ? "(WITH RECURSIVE c(x) AS (SELECT 1 UNION ALL SELECT x + 1 FROM c WHERE x < 100000000) SELECT MAX(x) FROM c) > 0 AND "
        : "(SELECT COUNT_BIG(*) FROM sys.all_objects a CROSS JOIN sys.all_objects b CROSS JOIN sys.all_objects c WHERE CHECKSUM(a.object_id, b.object_id, c.object_id) <> 0) > 0 AND ";

    /// <summary>Runs <paramref name="work"/> inside one read with <paramref name="interceptor"/> watching its statements.</summary>
    private protected Task<T> WatchedReadAsync<T>(NPoco.IInterceptor interceptor, Func<Task<T>> work) =>
        Database.ReadAsync(async db =>
        {
            db.Interceptors.Add(interceptor);
            try
            {
                return await work();
            }
            finally
            {
                db.Interceptors.Remove(interceptor);
            }
        });

    /// <summary>Runs <paramref name="work"/> as one unit of work with <paramref name="interceptor"/> watching its statements.</summary>
    private protected Task<T> WatchedAsync<T>(NPoco.IInterceptor interceptor, Func<Task<T>> work) =>
        Database.WriteAsync(async db =>
        {
            db.Interceptors.Add(interceptor);
            try
            {
                return await work();
            }
            finally
            {
                db.Interceptors.Remove(interceptor);
            }
        });

    /// <summary>
    /// Asserts that a change is refused with <paramref name="conflict"/> by the condition of its own statement, not by a
    /// unique index: a refusal the index made carries the database's error inside (ADR-0036).
    /// </summary>
    private protected static void AssertRefused<T>(Func<Task<T>> change, FormBuilderConflict conflict, string? why = null) =>
        Assert.That(
            async () => await change(),
            Throws.InstanceOf<FormBuilderConflictException>()
                .With.Property(nameof(FormBuilderConflictException.Conflict)).EqualTo(conflict)
                .And.Property(nameof(Exception.InnerException)).Null,
            why ?? conflict.ToString());

    private protected static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private protected static string Hex(int bytes) => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(bytes));

    /// <summary>A form for entries: its id, its draft version and the catalogue rows of its fields by alias.</summary>
    private protected sealed record TestForm(int Id, int VersionId, IReadOnlyDictionary<string, int> Fields)
    {
        public int this[string alias] => Fields[alias];
    }
}
