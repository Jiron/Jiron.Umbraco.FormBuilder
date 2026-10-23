using System.Security.Cryptography;
using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Schema = Jiron.Umbraco.FormBuilder.Core.Persistence.Migrations.CreateSchemaV1;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;

/// <summary>
/// A migrated database per test with a clock that stands still (ADR-0011), the repositories, and builders for the rows
/// the tests need. Entries and their values are written as raw rows, as their repositories arrive in 4.3c.
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

    /// <summary>An entry of a form, pointing at one of its versions.</summary>
    private protected long AddEntry(int formId, int versionId)
    {
        var now = Now.UtcDateTime;
        var entry = new Schema.EntryDto
        {
            Key = Guid.NewGuid(),
            FormId = formId,
            FormVersionId = versionId,
            Number = Count(FormBuilderTables.Entry, $"{Q("formId")} = @0", formId) + 1,
            State = "New",
            SubmissionId = Guid.NewGuid(),
            Digest = Hex(32),
            Culture = "en-US",
            SpamReasons = "[]",
            SubmittedUtc = now,
            UpdatedUtc = now,
            StateChangedUtc = now,
        };
        Write(db => db.Insert(entry));
        return entry.Id;
    }

    /// <summary>A value of an entry, for a field of the catalogue.</summary>
    private protected void AddValue(long entryId, int fieldId) =>
        Write(db => db.Insert(new Schema.EntryValueDto { EntryId = entryId, FieldId = fieldId, ValueIndex = 0, Text = "JFB_TEST" }));

    private protected static string Sha256(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private protected static string Hex(int bytes) => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(bytes));
}
