using System.Data;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Dtos;
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
    private IFormBuilderDatabaseGate _gate = null!;
    private FormBuilderDatabase _database = null!;

    [SetUp]
    public void SetUp()
    {
        _scopeProvider = Substitute.For<IScopeProvider>();
        _gate = Substitute.For<IFormBuilderDatabaseGate>();
        _database = new FormBuilderDatabase(_scopeProvider, Substitute.For<IScopeAccessor>(), _gate, TimeProvider.System, NullLogger<FormBuilderDatabase>.Instance);
    }

    // Every unit of work passes the gate first, and the substitute gate never runs one: a call that reached it would have
    // reached the database.
    [TearDown]
    public void NothingReachedTheDatabase()
    {
        Assert.That(_gate.ReceivedCalls(), Is.Empty, "a unit of work was started");
        _scopeProvider.DidNotReceiveWithAnyArgs().CreateScope(Arg.Any<IsolationLevel>(), Arg.Any<RepositoryCacheMode>(), Arg.Any<IEventDispatcher?>(), Arg.Any<IScopedNotificationPublisher?>(), Arg.Any<bool?>(), Arg.Any<bool>(), Arg.Any<bool>());
    }

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
            Assert.That(() => repository.RenameAsync(1, string.Empty, null), Throws.ArgumentException);
            Assert.That(() => repository.RenameAsync(1, tooLong, null), Throws.ArgumentException);
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
            Assert.That(() => repository.CommentVersionAsync(1, 1, comment), Throws.ArgumentException);
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
    public void Forms_RefuseVersionReadsBeyondTheirBounds()
    {
        var repository = new FormRepository(_database);

        Assert.Multiple(() =>
        {
            Assert.That(() => repository.GetVersionsAsync(1, null!), Throws.ArgumentNullException);
            Assert.That(() => repository.GetVersionsAsync(1, Enumerable.Range(1, FormRepository.MaxVersionsAtOnce + 1).ToList()), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => repository.ListDraftsToPruneAsync(DateTimeOffset.UnixEpoch, -1, 10), Throws.InstanceOf<ArgumentOutOfRangeException>(), "a negative count to keep");
            Assert.That(() => repository.ListDraftsToPruneAsync(DateTimeOffset.UnixEpoch, 0, 0), Throws.InstanceOf<ArgumentOutOfRangeException>(), "nothing to take");
            Assert.That(() => repository.ListDraftsToPruneAsync(DateTimeOffset.UnixEpoch, 0, PageRequest.MaxTake + 1), Throws.InstanceOf<ArgumentOutOfRangeException>(), "more than a page");
        });
    }

    [Test]
    public void TheCatalogue_RefusesFieldsTheColumnsCannotHold()
    {
        var repository = new FormRepository(_database);
        var field = new CatalogueFieldValues(Guid.NewGuid(), "email", "Email", "email", FieldStorageKind.Text, false, 0, null, null, null);

        Assert.Multiple(() =>
        {
            Assert.That(() => repository.AddFieldAsync(1, field with { Alias = string.Empty }), Throws.ArgumentException);
            Assert.That(() => repository.AddFieldAsync(1, field with { Alias = new string('a', FormRepository.MaxAliasLength + 1) }), Throws.ArgumentException);
            Assert.That(() => repository.AddFieldAsync(1, field with { Label = new string('l', FormRepository.MaxLabelLength + 1) }), Throws.ArgumentException);
            Assert.That(() => repository.AddFieldAsync(1, field with { Type = string.Empty }), Throws.ArgumentException);
            Assert.That(() => repository.UpdateFieldAsync(1, field with { Type = new string('t', FormRepository.MaxTypeLength + 1) }), Throws.ArgumentException);
            Assert.That(() => repository.UpdateFieldAsync(1, field with { StorageKind = (FieldStorageKind)99 }), Throws.InstanceOf<ArgumentOutOfRangeException>());
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

    [TestCase(OptionListKind.Manual, "manual")]
    [TestCase(OptionListKind.Content, "content")]
    [TestCase(OptionListKind.DataType, "dataType")]
    [TestCase(OptionListKind.Url, "url")]
    [TestCase(OptionListKind.File, "file")]
    [TestCase(OptionListKind.Countries, "countries")]
    [TestCase(OptionListKind.Code, "code")]
    public void OptionLists_StoreTheirKindAsItsCamelCaseName(OptionListKind kind, string stored) =>
        Assert.That((StoredNames.Of(kind), StoredNames.Parse<OptionListKind>(stored)), Is.EqualTo((stored, kind)));

    [Test]
    public void TheLibrary_RefusesWhatItsColumnsCannotHold()
    {
        var lists = new OptionListRepository(_database);
        var themes = new ThemeRepository(_database);
        var layouts = new EmailLayoutRepository(_database);
        var alias = new string('a', AliasedLibraryTable<OptionListDto>.MaxAliasLength + 1);
        var name = new string('n', AliasedLibraryTable<OptionListDto>.MaxNameLength + 1);
        var list = new NewOptionList(Guid.NewGuid(), "colours", "Colours", OptionListKind.Manual);
        var theme = new NewTheme(Guid.NewGuid(), "brand", "Brand", "default");

        Assert.Multiple(() =>
        {
            Assert.That(() => lists.CreateAsync(list with { Alias = string.Empty }), Throws.ArgumentException);
            Assert.That(() => lists.CreateAsync(list with { Alias = alias }), Throws.ArgumentException);
            Assert.That(() => lists.CreateAsync(list with { Name = string.Empty }), Throws.ArgumentException);
            Assert.That(() => lists.CreateAsync(list with { Name = name }), Throws.ArgumentException);
            Assert.That(() => lists.CreateAsync(list with { Kind = (OptionListKind)99 }), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => lists.CreateAsync(list with { CacheSeconds = -1 }), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => lists.CreateAsync(list with { Items = null! }), Throws.InstanceOf<ArgumentNullException>());
            Assert.That(() => lists.UpdateAsync(new OptionListUpdate(1, 1, alias, "Colours", OptionListKind.Manual, "{}", "[]", 0)), Throws.ArgumentException);
            Assert.That(() => lists.UpdateAsync(new OptionListUpdate(1, 1, "colours", "Colours", (OptionListKind)99, "{}", "[]", 0)), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => lists.UpdateAsync(new OptionListUpdate(1, 1, null!, "Colours", OptionListKind.Manual, "{}", "[]", 0)), Throws.InstanceOf<ArgumentNullException>(), "an alias is never kept by leaving it out");
            Assert.That(() => themes.CreateAsync(theme with { Alias = alias }), Throws.ArgumentException);
            Assert.That(() => themes.CreateAsync(theme with { BaseTheme = string.Empty }), Throws.ArgumentException);
            Assert.That(() => themes.CreateAsync(theme with { BaseTheme = new string('b', ThemeRepository.MaxBaseThemeLength + 1) }), Throws.ArgumentException);
            Assert.That(() => themes.CreateAsync(theme with { Tokens = null! }), Throws.InstanceOf<ArgumentNullException>());
            Assert.That(() => themes.CreateAsync(theme with { CustomCss = string.Empty }), Throws.ArgumentException, "null stands for none");
            Assert.That(() => themes.UpdateAsync(new ThemeUpdate(1, 1, name, "default", "{}", null)), Throws.ArgumentException);
            Assert.That(() => layouts.CreateAsync(new NewEmailLayout(Guid.NewGuid(), "plain", "Plain", null!)), Throws.InstanceOf<ArgumentNullException>());
            Assert.That(() => layouts.UpdateAsync(new EmailLayoutUpdate(1, 1, string.Empty, "Plain", "{}")), Throws.ArgumentException);
            Assert.That(() => layouts.UpdateAsync(new EmailLayoutUpdate(1, 1, null!, "Plain", "{}")), Throws.InstanceOf<ArgumentNullException>(), "an alias is never kept by leaving it out");
        });
    }

    [Test]
    public void Templates_RefuseWhatTheirColumnsCannotHold()
    {
        var repository = new FormTemplateRepository(_database);
        var template = new NewFormTemplate(Guid.NewGuid(), FormTemplateKind.Form, "Contact", "{}");

        Assert.Multiple(() =>
        {
            Assert.That(() => repository.CreateAsync(template with { Kind = (FormTemplateKind)7 }), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => repository.CreateAsync(template with { Name = string.Empty }), Throws.ArgumentException);
            Assert.That(() => repository.CreateAsync(template with { Name = new string('n', FormTemplateRepository.MaxNameLength + 1) }), Throws.ArgumentException);
            Assert.That(() => repository.CreateAsync(template with { Description = string.Empty }), Throws.ArgumentException, "null stands for none");
            Assert.That(() => repository.CreateAsync(template with { Description = new string('d', FormTemplateRepository.MaxDescriptionLength + 1) }), Throws.ArgumentException);
            Assert.That(() => repository.CreateAsync(template with { Category = string.Empty }), Throws.ArgumentException, "null stands for none");
            Assert.That(() => repository.CreateAsync(template with { Category = new string('c', FormTemplateRepository.MaxCategoryLength + 1) }), Throws.ArgumentException);
            Assert.That(() => repository.CreateAsync(template with { Definition = string.Empty }), Throws.ArgumentException);
            Assert.That(() => repository.UpdateAsync(new FormTemplateUpdate(1, string.Empty, null, null, "{}", null)), Throws.ArgumentException);
            Assert.That(() => repository.UpdateAsync(new FormTemplateUpdate(1, "Contact", null, null, string.Empty, null)), Throws.ArgumentException);
        });
    }

    [Test]
    public void Permissions_RefuseGrantsWithoutAGroup_OrWithATargetThatDoesNotFit()
    {
        var repository = new PermissionRepository(_database);
        var group = Guid.NewGuid();
        var key = Guid.NewGuid();

        Assert.Multiple(() =>
        {
            Assert.That(() => repository.ChangeAsync(new GrantChange(Guid.Empty, PermissionTarget.Global, Guid.Empty, 0, 1)), Throws.ArgumentException);
            Assert.That(() => repository.ChangeAsync(new GrantChange(group, PermissionTarget.Global, key, 0, 1)), Throws.ArgumentException, "a global grant names no target");
            Assert.That(() => repository.ChangeAsync(new GrantChange(group, PermissionTarget.Folder, Guid.Empty, 0, 1)), Throws.ArgumentException);
            Assert.That(() => repository.ChangeAsync(new GrantChange(group, PermissionTarget.Form, Guid.Empty, 0, 0)), Throws.ArgumentException);
            Assert.That(() => repository.ChangeAsync(new GrantChange(group, (PermissionTarget)9, key, 0, 1)), Throws.InstanceOf<ArgumentOutOfRangeException>());
            Assert.That(() => repository.GetAsync(group, PermissionTarget.Form, Guid.Empty), Throws.ArgumentException);
            Assert.That(() => repository.ListForTargetAsync(PermissionTarget.Folder, Guid.Empty), Throws.ArgumentException);
            Assert.That(() => repository.DeleteForTargetAsync(PermissionTarget.Global, key), Throws.ArgumentException);
            Assert.That(() => repository.DeleteForTargetAsync(PermissionTarget.Global, Guid.Empty), Throws.ArgumentException, "never every global grant at once");
            Assert.That(() => repository.DeleteForGroupAsync(Guid.Empty), Throws.ArgumentException);
        });
    }

    [Test]
    public async Task Permissions_ReadNoGroups_WithoutTheDatabase() =>
        Assert.That(await new PermissionRepository(_database).ListForGroupsAsync([]), Is.Empty);

    private static VersionDocument Document() => new(1, "{}", Hash, Hash);

    private static NewForm NewForm() => new(Guid.NewGuid(), null, "contact", "Contact", Document());
}
