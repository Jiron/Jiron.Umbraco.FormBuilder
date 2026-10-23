using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;

/// <summary>What the shared tests read of a library row, with the repository's own record for equality.</summary>
/// <param name="Id">The row's id.</param>
/// <param name="Key">Its key.</param>
/// <param name="Alias">Its alias.</param>
/// <param name="Name">Its name.</param>
/// <param name="ConcurrencyVersion">Its concurrency version.</param>
/// <param name="CreatedUtc">When it was created.</param>
/// <param name="UpdatedUtc">When it last changed.</param>
/// <param name="Stored">The repository's record.</param>
internal sealed record LibraryRow(int Id, Guid Key, string Alias, string Name, int ConcurrencyVersion, DateTimeOffset CreatedUtc, DateTimeOffset UpdatedUtc, object Stored);

/// <summary>
/// What every library row named by an alias does (ADR-0033, ADR-0037), run once for option lists, themes and email
/// layouts: CRUD, the alias in any letter case, and changes and deletes against the concurrency version. Umbraco reads
/// the database option from the class that declares a test, so the base classes carry it too.
/// </summary>
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public abstract class AliasedLibraryRepositoryTests : RepositoryTestBase
{
    /// <summary>The table the rows live in.</summary>
    private protected abstract string Table { get; }

    [Test]
    public async Task ARow_IsCreatedAtVersion1_AndReadByKeyIdAndAlias()
    {
        var key = Guid.NewGuid();

        var row = await CreateAsync("brand", "Brand", key);

        Assert.Multiple(async () =>
        {
            Assert.That(row.Key, Is.EqualTo(key));
            Assert.That(row.Alias, Is.EqualTo("brand"));
            Assert.That(row.Name, Is.EqualTo("Brand"));
            Assert.That(row.ConcurrencyVersion, Is.EqualTo(1));
            Assert.That(row.CreatedUtc, Is.EqualTo(Now));
            Assert.That(row.UpdatedUtc, Is.EqualTo(Now));
            Assert.That((await GetAsync(key))?.Stored, Is.EqualTo(row.Stored));
            Assert.That((await GetByIdAsync(row.Id))?.Stored, Is.EqualTo(row.Stored));
            Assert.That((await GetByAliasAsync("brand"))?.Stored, Is.EqualTo(row.Stored));
            Assert.That(await GetAsync(Guid.NewGuid()), Is.Null);
            Assert.That(await GetByIdAsync(-1), Is.Null);
        });
    }

    [TestCase("contactUs")]
    [TestCase("CONTACTUS")]
    [TestCase("contactus")]
    [TestCase("ContactUs")]
    public async Task ARow_IsFoundByItsAlias_InAnyLetterCase(string lookup)
    {
        var row = await CreateAsync("contactUs");
        await CreateAsync("contactUsToo");

        var found = await GetByAliasAsync(lookup);

        Assert.Multiple(async () =>
        {
            Assert.That(found?.Id, Is.EqualTo(row.Id));
            Assert.That(found?.Alias, Is.EqualTo("contactUs"), "stored as it was written");
            Assert.That(await GetByAliasAsync("contact"), Is.Null, "no partial match");
        });
    }

    [Test]
    public async Task Creating_IsRefused_ForAnAliasTakenInAnyCase_AndATakenKey()
    {
        var row = await CreateAsync("brand");

        Assert.Multiple(() =>
        {
            AssertRefused(() => CreateAsync("BRAND"), FormBuilderConflict.DuplicateAlias);
            AssertRefused(() => CreateAsync("other", key: row.Key), FormBuilderConflict.DuplicateKey);
            Assert.That(Count(Table), Is.EqualTo(1), "nothing was written");
        });
    }

    [Test]
    public async Task AnUpdate_ChangesTheRow_AndRaisesItsVersion()
    {
        var row = await CreateAsync("brand", "Brand");
        Clock.Advance(TimeSpan.FromMinutes(5));

        var updated = await UpdateAsync(row, row.ConcurrencyVersion, "Brand, renamed");

        Assert.Multiple(async () =>
        {
            Assert.That(updated?.Name, Is.EqualTo("Brand, renamed"));
            Assert.That(updated?.Alias, Is.EqualTo("brand"));
            Assert.That(updated?.ConcurrencyVersion, Is.EqualTo(2));
            Assert.That(updated?.CreatedUtc, Is.EqualTo(Now));
            Assert.That(updated?.UpdatedUtc, Is.EqualTo(Now.AddMinutes(5)));
            Assert.That((await GetByIdAsync(row.Id))?.Stored, Is.EqualTo(updated?.Stored));
            Assert.That(await UpdateAsync(row with { Id = -1 }, 1, "Missing"), Is.Null, "a row that does not exist");
        });
    }

    [Test]
    public async Task AStaleUpdate_IsRefused_AndChangesNothing()
    {
        var row = await CreateAsync("brand");
        var current = await UpdateAsync(row, row.ConcurrencyVersion, "Second");

        Assert.Multiple(async () =>
        {
            Assert.That(
                async () => await UpdateAsync(row, row.ConcurrencyVersion, "Lost"),
                Throws.InstanceOf<FormBuilderConcurrencyException>());
            Assert.That((await GetByIdAsync(row.Id))?.Stored, Is.EqualTo(current?.Stored));
        });
    }

    [Test]
    public async Task ParallelUpdates_FromTheSameVersion_LetExactlyOneThrough()
    {
        var row = await CreateAsync("brand");

        var outcomes = await Task.WhenAll(Enumerable.Range(0, 10).Select(i => Task.Run(async () =>
        {
            try
            {
                await UpdateAsync(row, row.ConcurrencyVersion, $"Name {i}");
                return true;
            }
            catch (FormBuilderConcurrencyException)
            {
                return false;
            }
        })));

        Assert.Multiple(async () =>
        {
            Assert.That(outcomes.Count(updated => updated), Is.EqualTo(1));
            Assert.That((await GetByIdAsync(row.Id))?.ConcurrencyVersion, Is.EqualTo(2));
        });
    }

    [Test]
    public async Task ADelete_IsMadeAgainstTheVersion()
    {
        var row = await CreateAsync("brand");
        var current = await UpdateAsync(row, row.ConcurrencyVersion, "Second");

        Assert.Multiple(async () =>
        {
            Assert.That(
                async () => await DeleteAsync(row, row.ConcurrencyVersion),
                Throws.InstanceOf<FormBuilderConcurrencyException>());
            Assert.That((await GetByIdAsync(row.Id))?.Stored, Is.EqualTo(current?.Stored), "the refused delete changed nothing");
            Assert.That(await DeleteAsync(row, current!.ConcurrencyVersion), Is.True);
            Assert.That(await DeleteAsync(row, current.ConcurrencyVersion), Is.False, "deleted already");
            Assert.That(await GetAsync(row.Key), Is.Null);
            Assert.That(await GetByAliasAsync("brand"), Is.Null);
        });
    }

    [Test]
    public async Task Lists_SearchNameAndAlias_EscapeWildcards_AndPageInTheDatabase()
    {
        await CreateAsync("gamma", "Gamma");
        var alpha = await CreateAsync("alpha", "Alpha 100%");
        var beta = await CreateAsync("beta_2", "Beta");
        await CreateAsync("betaX2", "Delta");
        await CreateAsync("epsilon", "Epsilon 1000");

        var all = await ListAsync(null, PageRequest.First(500));
        var second = await ListAsync(null, new PageRequest(1, 2));
        var byAlias = await ListAsync("BETA_", PageRequest.First(10));
        var byName = await ListAsync(" 100% ", PageRequest.First(10));
        var past = await ListAsync(null, new PageRequest(10, 10));

        Assert.Multiple(() =>
        {
            Assert.That(all.Items.Select(row => row.Name), Is.EqualTo(new[] { "Alpha 100%", "Beta", "Delta", "Epsilon 1000", "Gamma" }), "in name order");
            Assert.That(all.Total, Is.EqualTo(5));
            Assert.That(second.Items.Select(row => row.Name), Is.EqualTo(new[] { "Beta", "Delta" }));
            Assert.That(second.Total, Is.EqualTo(5));
            Assert.That(byAlias.Items.Select(row => row.Id), Is.EqualTo(new[] { beta.Id }), "the underscore is no wildcard, the case does not matter");
            Assert.That(byName.Items.Select(row => row.Id), Is.EqualTo(new[] { alpha.Id }), "the percent sign is no wildcard, or it would find Epsilon 1000");
            Assert.That(past.Items, Is.Empty);
            Assert.That(past.Total, Is.EqualTo(5));
        });
    }

    private protected abstract Task<LibraryRow> CreateAsync(string alias, string name = "Name", Guid? key = null);

    private protected abstract Task<LibraryRow?> GetAsync(Guid key);

    private protected abstract Task<LibraryRow?> GetByIdAsync(int id);

    private protected abstract Task<LibraryRow?> GetByAliasAsync(string alias);

    private protected abstract Task<Page<LibraryRow>> ListAsync(string? search, PageRequest page);

    /// <summary>Changes the row's name (keeping its alias) and its content.</summary>
    private protected abstract Task<LibraryRow?> UpdateAsync(LibraryRow row, int expectedConcurrencyVersion, string name);

    private protected abstract Task<bool> DeleteAsync(LibraryRow row, int expectedConcurrencyVersion);
}

/// <summary>The library rows whose alias may change, because what uses them names them by key.</summary>
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public abstract class RenamableLibraryRepositoryTests : AliasedLibraryRepositoryTests
{
    [Test]
    public async Task TheAlias_MayChange_ButNotToOneTakenInAnyCase()
    {
        var row = await CreateAsync("brand");
        await CreateAsync("other");

        var renamed = await ChangeAliasAsync(row, row.ConcurrencyVersion, "Brand2");

        Assert.Multiple(async () =>
        {
            Assert.That(renamed?.Alias, Is.EqualTo("Brand2"));
            Assert.That((await GetByAliasAsync("brand2"))?.Id, Is.EqualTo(row.Id));
            Assert.That(await GetByAliasAsync("brand"), Is.Null);
            Assert.That((await ChangeAliasAsync(renamed!, renamed!.ConcurrencyVersion, "BRAND2"))?.Alias, Is.EqualTo("BRAND2"), "its own alias in another case");
            AssertRefused(() => ChangeAliasAsync(renamed, renamed.ConcurrencyVersion + 1, "OTHER"), FormBuilderConflict.DuplicateAlias);
            Assert.That(
                async () => await ChangeAliasAsync(renamed, renamed.ConcurrencyVersion, "OTHER"),
                Throws.InstanceOf<FormBuilderConcurrencyException>(),
                "a stale change says so first, whatever its alias");
            Assert.That((await GetByIdAsync(row.Id))?.Alias, Is.EqualTo("BRAND2"));
        });
    }

    private protected abstract Task<LibraryRow?> ChangeAliasAsync(LibraryRow row, int expectedConcurrencyVersion, string alias);
}
