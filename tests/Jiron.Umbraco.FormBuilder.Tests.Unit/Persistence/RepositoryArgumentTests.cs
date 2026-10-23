using System.Data;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Scoping;
using Umbraco.Cms.Infrastructure.Scoping;
using IScopeProvider = Umbraco.Cms.Infrastructure.Scoping.IScopeProvider;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Persistence;

/// <summary>
/// The repositories refuse what their columns cannot hold, before they reach the database (ADR-0036), so both databases
/// behave alike: SQL Server would refuse an overlong value, SQLite would store it.
/// </summary>
[TestFixture]
public sealed class RepositoryArgumentTests
{
    private static readonly string Hash = new('a', 64);
    private IScopeProvider _scopeProvider = null!;
    private FormBuilderDatabase _database = null!;

    [SetUp]
    public void SetUp()
    {
        _scopeProvider = Substitute.For<IScopeProvider>();
        _database = new FormBuilderDatabase(_scopeProvider, Substitute.For<IScopeAccessor>(), Substitute.For<IFormBuilderDatabaseGate>(), TimeProvider.System, NullLogger<FormBuilderDatabase>.Instance);
    }

    [TearDown]
    public void NothingReachedTheDatabase() =>
        _scopeProvider.DidNotReceiveWithAnyArgs().CreateScope(Arg.Any<IsolationLevel>(), Arg.Any<RepositoryCacheMode>(), Arg.Any<IEventDispatcher?>(), Arg.Any<IScopedNotificationPublisher?>(), Arg.Any<bool?>(), Arg.Any<bool>(), Arg.Any<bool>());

    [TestCase("forms-section-granted", true)]
    [TestCase("mirror:forms/contact.json", true)]
    [TestCase("a", true)]
    [TestCase("checklist.step_1", true)]
    [TestCase("", false)]
    [TestCase("Upper", false)]
    [TestCase("with space", false)]
    [TestCase("semi;colon", false)]
    public void KeyValue_KeysAreLowercaseAndPlain(string key, bool valid) =>
        Assert.That(KeyValueRepository.IsValidKey(key), Is.EqualTo(valid));

    [Test]
    public void KeyValue_RefusesKeysItCannotStore()
    {
        var repository = new KeyValueRepository(_database);
        var tooLong = new string('k', KeyValueRepository.MaxKeyLength + 1);
        var accented = "caf" + (char)0xE9;

        Assert.Multiple(() =>
        {
            Assert.That(KeyValueRepository.IsValidKey(new string('k', KeyValueRepository.MaxKeyLength)), Is.True);
            Assert.That(KeyValueRepository.IsValidKey(tooLong), Is.False);
            Assert.That(KeyValueRepository.IsValidKey(accented), Is.False);
            Assert.That(() => repository.GetAsync("Upper"), Throws.ArgumentException);
            Assert.That(() => repository.ListAsync(string.Empty), Throws.ArgumentException);
            Assert.That(() => repository.SetAsync(tooLong, "v"), Throws.ArgumentException);
            Assert.That(() => repository.TryAddAsync(accented, "v"), Throws.ArgumentException);
            Assert.That(() => repository.DeleteAsync("a b"), Throws.ArgumentException);
        });
    }

    [Test]
    public void Folders_RefuseNamesTheColumnCannotHold()
    {
        var repository = new FolderRepository(_database);
        var tooLong = new string('n', FolderRepository.MaxNameLength + 1);

        Assert.Multiple(() =>
        {
            Assert.That(() => repository.CreateAsync(new NewFolder(Guid.NewGuid(), null, string.Empty)), Throws.ArgumentException);
            Assert.That(() => repository.CreateAsync(new NewFolder(Guid.NewGuid(), null, tooLong)), Throws.ArgumentException);
            Assert.That(() => repository.UpdateAsync(1, string.Empty, 0, false, null), Throws.ArgumentException);
            Assert.That(() => repository.UpdateAsync(1, tooLong, 0, false, null), Throws.ArgumentException);
        });
    }

    [Test]
    public void Forms_RefuseAliasesNamesAndCommentsTheColumnsCannotHold()
    {
        var repository = new FormRepository(_database);
        var alias = new string('a', FormRepository.MaxAliasLength + 1);
        var name = new string('n', FormRepository.MaxNameLength + 1);
        var comment = new string('c', FormRepository.MaxCommentLength + 1);

        Assert.Multiple(() =>
        {
            Assert.That(() => repository.CreateAsync(NewForm() with { Alias = string.Empty }), Throws.ArgumentException);
            Assert.That(() => repository.CreateAsync(NewForm() with { Alias = alias }), Throws.ArgumentException);
            Assert.That(() => repository.CreateAsync(NewForm() with { Name = string.Empty }), Throws.ArgumentException);
            Assert.That(() => repository.CreateAsync(NewForm() with { Name = name }), Throws.ArgumentException);
            Assert.That(() => repository.CreateAsync(NewForm() with { Comment = comment }), Throws.ArgumentException);
            Assert.That(() => repository.UpdateAsync(new FormUpdate(1, 1, alias, "Name", null, false, null)), Throws.ArgumentException);
            Assert.That(() => repository.UpdateAsync(new FormUpdate(1, 1, "contact", name, null, false, null)), Throws.ArgumentException);
            Assert.That(() => repository.SaveDraftAsync(1, 1, Document(), comment, null), Throws.ArgumentException);
            Assert.That(() => repository.UpdateVersionAsync(1, 1, comment, false), Throws.ArgumentException);
            Assert.That(() => repository.PublishAsync(1, 1, Document(), string.Empty, null, null), Throws.ArgumentException);
            Assert.That(() => repository.PublishAsync(1, 1, Document(), alias, null, null), Throws.ArgumentException);
        });
    }

    [Test]
    public void Forms_RefuseVersionDocumentsThatAreNotWhole()
    {
        var repository = new FormRepository(_database);

        Assert.Multiple(() =>
        {
            Assert.That(() => repository.SaveDraftAsync(1, 1, Document() with { SchemaVersion = 0 }, null, null), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => repository.SaveDraftAsync(1, 1, Document() with { Definition = string.Empty }, null, null), Throws.ArgumentException);
            Assert.That(() => repository.SaveDraftAsync(1, 1, Document() with { DefinitionHash = Hash.ToUpperInvariant() }, null, null), Throws.ArgumentException, "lowercase hex");
            Assert.That(() => repository.SaveDraftAsync(1, 1, Document() with { DefinitionHash = Hash[1..] }, null, null), Throws.ArgumentException, "64 characters");
            Assert.That(() => repository.PublishAsync(1, 1, Document() with { PublicHash = "g" + Hash[1..] }, null, null, null), Throws.ArgumentException, "hex");
            Assert.That(() => repository.CreateAsync(NewForm() with { Draft = Document() with { PublicHash = string.Empty } }), Throws.ArgumentException);
        });
    }

    [Test]
    public void TheCatalogue_RefusesFieldsTheColumnsCannotHold()
    {
        var repository = new FormRepository(_database);
        var field = new CatalogueFieldValues(Guid.NewGuid(), "email", "Email", "email", "Text", false, 0, null, null, null);

        Assert.Multiple(() =>
        {
            Assert.That(() => repository.AddFieldAsync(1, field with { Alias = string.Empty }), Throws.ArgumentException);
            Assert.That(() => repository.AddFieldAsync(1, field with { Alias = new string('a', FormRepository.MaxAliasLength + 1) }), Throws.ArgumentException);
            Assert.That(() => repository.AddFieldAsync(1, field with { Label = new string('l', FormRepository.MaxLabelLength + 1) }), Throws.ArgumentException);
            Assert.That(() => repository.AddFieldAsync(1, field with { Type = string.Empty }), Throws.ArgumentException);
            Assert.That(() => repository.UpdateFieldAsync(1, field with { Type = new string('t', FormRepository.MaxTypeLength + 1) }), Throws.ArgumentException);
            Assert.That(() => repository.UpdateFieldAsync(1, field with { StorageKind = new string('s', FormRepository.MaxStorageKindLength + 1) }), Throws.ArgumentException);
        });
    }

    [TestCase("global", true)]
    [TestCase("0f8fad5b-d9cb-469f-a165-70867728950e", true)]
    [TestCase("Global", false)]
    [TestCase("0F8FAD5B-D9CB-469F-A165-70867728950E", false)]
    [TestCase("{0f8fad5b-d9cb-469f-a165-70867728950e}", false)]
    [TestCase("0f8fad5bd9cb469fa16570867728950e", false)]
    [TestCase("", false)]
    public void Settings_ScopesAreGlobalOrASitesKeyInLowercase(string scope, bool valid) =>
        Assert.That(SettingsScopes.IsValid(scope), Is.EqualTo(valid));

    [TestCase("email", true)]
    [TestCase("integrations", true)]
    [TestCase("a1", true)]
    [TestCase("Email", false)]
    [TestCase("1email", false)]
    [TestCase("e-mail", false)]
    [TestCase("", false)]
    public void Settings_SectionsAreLowercaseWords(string section, bool valid) =>
        Assert.That(SettingsRepository.IsValidSection(section), Is.EqualTo(valid));

    [Test]
    public void Settings_RefuseWhatTheRowsCannotHold()
    {
        var repository = new SettingsRepository(_database);
        var save = new SettingsSave(SettingsScopes.Global, "email", 1, 1, "{}", null);
        var site = SettingsScopes.ForSite(Guid.NewGuid());

        Assert.Multiple(() =>
        {
            Assert.That(SettingsRepository.IsValidSection(new string('s', SettingsRepository.MaxSectionLength)), Is.True);
            Assert.That(SettingsRepository.IsValidSection(new string('s', SettingsRepository.MaxSectionLength + 1)), Is.False);
            Assert.That(SettingsScopes.IsValid(site), Is.True);
            Assert.That(() => repository.GetAsync("Global", "email"), Throws.ArgumentException);
            Assert.That(() => repository.ListAsync("site"), Throws.ArgumentException);
            Assert.That(() => repository.ListSectionAsync("E-mail"), Throws.ArgumentException);
            Assert.That(() => repository.SaveAsync(save with { SchemaVersion = 0 }), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => repository.SaveAsync(save with { ExpectedVersion = -1 }), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => repository.SaveAsync(save with { Json = string.Empty }), Throws.ArgumentException);
            Assert.That(() => repository.DeleteAsync(SettingsScopes.Global, "email", 1), Throws.ArgumentException, "the global rows always stay");
        });
    }

    private static VersionDocument Document() => new(1, "{}", Hash, Hash);

    private static NewForm NewForm() => new(Guid.NewGuid(), null, "contact", "Contact", Document());
}
