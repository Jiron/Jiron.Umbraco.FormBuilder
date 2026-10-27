using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Exchange;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Access;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Persistence.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Tests.Common.Testing;
using static Jiron.Umbraco.FormBuilder.Tests.Integration.Forms.FormServiceTesting;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Forms;

/// <summary>
/// The option lists an import creates on a real site (ADR-0078), for the personas of ADR-0013: a library manager gets
/// the lists the file carries with their keys and the first free alias, held to their kinds' checks on this site (a URL
/// list on a host the site does not call is not created), written with the form or not at all; anyone else gets the
/// form and the notes.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest)]
public sealed class FormImportListTests : RepositoryTestBase
{
    private const string Rooms = """[{"value":"single","label":{"":"Single","de":"Einzel"}},{"value":"double","label":{"":"Double"}}]""";
    private const string Meals = """[{"value":"fish"},{"value":"vegan"}]""";
    private const string CalledHost = "lists.jfb.test";

    private static readonly Guid RoomsKey = Guid.Parse("78000000-0000-4000-8000-000000000101");
    private static readonly Guid MealsKey = Guid.Parse("78000000-0000-4000-8000-000000000102");
    private static readonly Guid FeedKey = Guid.Parse("78000000-0000-4000-8000-000000000103");
    private static readonly Guid ElsewhereKey = Guid.Parse("78000000-0000-4000-8000-000000000104");
    private static readonly Guid UploadKey = Guid.Parse("78000000-0000-4000-8000-000000000105");

    private Personas _personas = null!;

    private IFormBuilderFormService Service => GetRequiredService<IFormBuilderFormService>();

    private IFormBuilderFormExchangeService Exchange => GetRequiredService<IFormBuilderFormExchangeService>();

    private IFormBuilderOptionService Options => GetRequiredService<IFormBuilderOptionService>();

    private FormNotificationLog Notifications => GetRequiredService<FormNotificationLog>();

    private RacedForms Raced => GetRequiredService<RacedForms>();

    private LockedLists Lists => GetRequiredService<LockedLists>();

    // The designers' group may manage the library; the entry handlers' may not (ADR-0013).
    private Guid LibraryManager => _personas["designer"].Key;

    private Guid EntryHandler => _personas["entryHandler"].Key;

    [SetUp]
    public async Task CreateThePersonasAsync() => _personas = await ActivatorUtilities.CreateInstance<AccessPersonas>(Services).CreateAsync();

    [Test]
    public async Task AnImport_ByALibraryManager_CreatesTheCarriedLists_WithTheirKeys_WithTheForm()
    {
        await OptionLists.CreateAsync(new NewOptionList(Guid.NewGuid(), "meals", "Meals", OptionListKind.Manual, Items: """[{"value":"soup"}]"""));
        var file = await FileAsync(
            [RoomsKey, MealsKey, FeedKey],
            ListJson(RoomsKey, "rooms", "Guest rooms", Rooms),
            ListJson(MealsKey, "meals", "Meals", Meals),
            ListJson(FeedKey, "feed", "Feed", "[]", "url", Feed(CalledHost), 300));
        var lists = Count(FormBuilderTables.OptionList);

        var imported = await Exchange.ImportAsync(new ImportFormRequest(file, UserKey: LibraryManager));
        var created = Count(FormBuilderTables.OptionList) - lists;
        var rooms = await OptionLists.GetAsync(RoomsKey);
        var feed = await OptionLists.GetAsync(FeedKey);
        var offered = await Options.GetListAsync(RoomsKey, new FormBuilderOptionContext("de"));
        var fields = DefinitionKeys.Fields((await Service.GetDraftAsync(imported.Form.Key))!.Definition!).Select(field => field.Options?.OptionList).ToList();
        var entry = (await Audit.QueryAsync(new AuditQuery { EntityKey = imported.Form.Key }, PageRequest.First(10))).Items.Single();
        var again = await Exchange.ImportAsync(new ImportFormRequest(file, UserKey: LibraryManager));

        Assert.Multiple(() =>
        {
            Assert.That(imported.Notes, Is.EqualTo(new[]
            {
                new FormImportNote(FormImportNotes.OptionListCreated, Path(0), "rooms"),
                new FormImportNote(FormImportNotes.OptionListCreated, Path(1), "meals2"),
                new FormImportNote(FormImportNotes.OptionListCreated, Path(2), "feed"),
            }), "a carried manual list, one whose alias the site's other list has, and a URL list on a host the site calls");
            Assert.That(created, Is.EqualTo(3));
            Assert.That((rooms!.Alias, rooms.Name, rooms.Kind, rooms.CacheSeconds), Is.EqualTo(("rooms", "Guest rooms", OptionListKind.Manual, 0)));
            Assert.That(FormExportFormat.SameListJson(rooms.Items, Rooms, JsonValueKind.Array), Is.True, "its items as the file carries them");
            Assert.That((feed!.Kind, feed.CacheSeconds, FormExportFormat.SameListJson(feed.Config, Feed(CalledHost), JsonValueKind.Object)), Is.EqualTo((OptionListKind.Url, 300, true)));
            Assert.That(fields, Is.EqualTo(new Guid?[] { RoomsKey, MealsKey, FeedKey }), "the fields keep naming the lists by their keys");
            Assert.That(offered.Options.Select(option => (option.Value, option.Label)), Is.EqualTo(new[] { ("single", "Einzel"), ("double", "Double") }), "the site offers the list's options at once");
            Assert.That(entry.Details, Does.EndWith(""","notes":["option_list_created"],"noteCounts":[3]}"""));
            Assert.That(again.Notes, Is.Empty, "a second import of the file uses the lists by their keys");
            Assert.That(Count(FormBuilderTables.OptionList) - lists, Is.EqualTo(3), "and creates none");
        });
    }

    [Test]
    public async Task AnImport_ByAUserWhoMayNotManageTheLibrary_ReportsTheCarriedList_AndCreatesNone()
    {
        var file = await FileAsync([RoomsKey], ListJson(RoomsKey, "rooms", "Guest rooms", Rooms));
        var (forms, lists) = (Count(FormBuilderTables.Form), Count(FormBuilderTables.OptionList));

        var imports = new List<FormImport>();
        foreach (var user in new Guid?[] { EntryHandler, Editor, null })
        {
            imports.Add(await Exchange.ImportAsync(new ImportFormRequest(file, UserKey: user)));
        }

        Assert.Multiple(async () =>
        {
            Assert.That(imports.Select(import => import.Notes), Is.All.EqualTo(new[] { new FormImportNote(FormImportNotes.OptionListMissing, Path(0), "rooms") }), "a user who may not, a key that names no user, and Jiron Form Builder itself");
            Assert.That(Count(FormBuilderTables.Form) - forms, Is.EqualTo(3), "each gets the form");
            Assert.That(Count(FormBuilderTables.OptionList), Is.EqualTo(lists), "and no list");
            Assert.That(await OptionLists.GetAsync(RoomsKey), Is.Null);
        });
    }

    [Test]
    public async Task AnImport_ReportsACarriedUrlListOnAHostTheSiteDoesNotCall_WithoutCreatingIt()
    {
        var file = await FileAsync(
            [FeedKey, ElsewhereKey, UploadKey],
            ListJson(FeedKey, "feed", "Feed", "[]", "url", Feed(CalledHost), 300),
            ListJson(ElsewhereKey, "elsewhere", "Elsewhere", "[]", "url", Feed("elsewhere.jfb.test"), 300),
            ListJson(UploadKey, "upload", "Upload", """[{"value":"fish"},{"value":"fish"}]""", "file"));

        var imported = await Exchange.ImportAsync(new ImportFormRequest(file, UserKey: LibraryManager));
        var fields = DefinitionKeys.Fields((await Service.GetDraftAsync(imported.Form.Key))!.Definition!).Select(field => field.Options?.OptionList).ToList();

        Assert.Multiple(async () =>
        {
            Assert.That(imported.Notes, Is.EqualTo(new[]
            {
                new FormImportNote(FormImportNotes.OptionListCreated, Path(0), "feed"),
                new FormImportNote(FormImportNotes.OptionListMissing, Path(1), "elsewhere"),
                new FormImportNote(FormImportNotes.OptionListMissing, Path(2), "upload"),
            }), "the kinds' checks on this site: the allowed hosts, and an uploaded list's contents");
            Assert.That(await OptionLists.GetAsync(ElsewhereKey), Is.Null, "the URL list on another host is not created");
            Assert.That(await OptionLists.GetAsync(UploadKey), Is.Null, "nor the upload with a value twice");
            Assert.That(await OptionLists.GetAsync(FeedKey), Is.Not.Null);
            Assert.That(fields, Is.EqualTo(new Guid?[] { FeedKey, ElsewhereKey, UploadKey }), "the fields keep the keys of the lists not created");
        });
    }

    [Test]
    public async Task AnImport_WritesItsListsWithTheForm_OrNotAtAll()
    {
        var file = await FileAsync([RoomsKey, MealsKey], ListJson(RoomsKey, "rooms", "Rooms", Rooms), ListJson(MealsKey, "meals", "Meals", Meals));
        var other = await FileAsync([FeedKey], ListJson(FeedKey, "tables", "Tables", Rooms));
        var (forms, lists) = (Count(FormBuilderTables.Form), Count(FormBuilderTables.OptionList));

        Notifications.Cancel = true;
        AssertCode(async () => await Exchange.ImportAsync(new ImportFormRequest(file, UserKey: LibraryManager)), "cancelled");
        Notifications.Cancel = false;
        var afterCancel = (Count(FormBuilderTables.Form) - forms, Count(FormBuilderTables.OptionList) - lists);

        // Another change creates the meals list by its key once the import has planned its lists: the form's unit rolls
        // back with the rooms list it wrote, and the import plans its lists again.
        var raced = 0;
        Notifications.Before = async _ =>
        {
            if (raced++ == 0)
            {
                await OptionLists.CreateAsync(new NewOptionList(MealsKey, "meals", "Meals", OptionListKind.Manual, Items: Meals));
            }
        };
        var imported = await Exchange.ImportAsync(new ImportFormRequest(file, UserKey: LibraryManager));
        var afterRace = (Count(FormBuilderTables.Form) - forms, Count(FormBuilderTables.OptionList) - lists);

        // Another change takes the alias the import chose, three times in a row.
        var taken = new[] { "tables", "tables2", "tables3" };
        var races = 0;
        Notifications.Before = async _ => await OptionLists.CreateAsync(new NewOptionList(Guid.NewGuid(), taken[races++], "Tables", OptionListKind.Manual, Items: Meals));
        var formsBefore = Count(FormBuilderTables.Form);
        var refused = AssertCode(async () => await Exchange.ImportAsync(new ImportFormRequest(other, UserKey: LibraryManager)), "alias_taken");
        Notifications.Before = null;

        Assert.Multiple(async () =>
        {
            Assert.That(afterCancel, Is.EqualTo((0, 0)), "a cancelled import writes neither the form nor its lists");
            Assert.That(raced, Is.EqualTo(2), "announced again");
            Assert.That(imported.Notes, Is.EqualTo(new[] { new FormImportNote(FormImportNotes.OptionListCreated, Path(0), "rooms") }), "the rooms list was rolled back and created again; the meals list is used by its key");
            Assert.That(afterRace, Is.EqualTo((1, 2)), "one form, the rooms list and the other change's meals list");
            Assert.That(races, Is.EqualTo(FormCreation.AliasAttempts));
            Assert.That(refused, Is.InstanceOf<FormBuilderConflictException>().And.Property(nameof(FormBuilderConflictException.Conflict)).EqualTo(FormBuilderConflict.DuplicateAlias));
            Assert.That(Count(FormBuilderTables.Form), Is.EqualTo(formsBefore), "then the import is refused, with no form");
            Assert.That(await OptionLists.GetAsync(FeedKey), Is.Null, "and none of its lists");
        });
    }

    [Test]
    public async Task AnImportWhoseUnitTheDatabaseRunsAgain_TellsItsAliasTakenAnew_InEachAttempt()
    {
        var file = await FileAsync([RoomsKey], ListJson(RoomsKey, "rooms", "Guest rooms", Rooms));
        var (forms, lists) = (Count(FormBuilderTables.Form), Count(FormBuilderTables.OptionList));
        var (inserts, announced) = (Raced.Inserts, Notifications.Seen.Count);

        // The first attempt's list insert meets a lock conflict, so the database runs the unit again; in that second
        // attempt another server has just taken the form's alias. The create then takes the next free one.
        Lists.FailNextCreates = 1;
        Raced.TakeTheAliasAtInsert = inserts + 2;
        var imported = await Exchange.ImportAsync(new ImportFormRequest(file, UserKey: LibraryManager));

        Assert.Multiple(async () =>
        {
            Assert.That(Raced.Inserts - inserts, Is.EqualTo(3), "the form, its unit again with the alias taken, then the next alias");
            Assert.That(Notifications.Seen.Count - announced, Is.EqualTo(2), "announced again for the next alias");
            Assert.That((Count(FormBuilderTables.Form) - forms, Count(FormBuilderTables.OptionList) - lists), Is.EqualTo((1, 1)), "one form, one list");
            Assert.That(await OptionLists.GetAsync(RoomsKey), Is.Not.Null);
            Assert.That(imported.Notes.Select(note => note.Code), Does.Contain(FormImportNotes.OptionListCreated));
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        base.CustomTestSetup(builder);
        builder.Services.PostConfigure<FormBuilderOptions>(options => options.Webhooks.AllowedHosts.Add(CalledHost));
        builder.Services.AddSingleton<FormNotificationLog>();
        builder.AddNotificationAsyncHandler<FormSavingNotification, FormNotificationRecorder<FormSavingNotification>>();
        builder.Services.AddSingleton(provider => RacedForms.Over(ActivatorUtilities.CreateInstance<FormRepository>(provider)));
        builder.Services.AddUnique<IFormRepository>(provider => (IFormRepository)provider.GetRequiredService<RacedForms>());
        builder.Services.AddSingleton(provider => new LockedLists(new OptionListRepository(provider.GetRequiredService<FormBuilderDatabase>())));
        builder.Services.AddUnique<IOptionListRepository>(provider => provider.GetRequiredService<LockedLists>());
    }

    private static string Path(int field) => $"$.definition.pages[0].groups[0].fields[{field}].options.list";

    private static string Feed(string host) => $$"""{"url":"https://{{host}}/rooms.json","listPath":"$.items","valueFrom":"$.id","labelFrom":"$.name"}""";

    private static JsonObject ListJson(Guid key, string alias, string name, string items, string kind = "manual", string config = "{}", int cacheSeconds = 0) => new()
    {
        ["key"] = key.ToString("D"),
        ["alias"] = alias,
        ["name"] = name,
        ["kind"] = kind,
        ["config"] = JsonNode.Parse(config),
        ["items"] = JsonNode.Parse(items),
        ["cacheSeconds"] = cacheSeconds,
    };

    // The site's form repository, every call passed on, except the insert a test names: another server took its alias
    // a moment before, as the repository then says. DispatchProxy derives the proxy from it, so it stays unsealed.
#pragma warning disable CA1852
    internal class RacedForms : DispatchProxy
    {
        private IFormRepository _inner = null!;

        /// <summary>How many inserts the repository was asked for.</summary>
        public int Inserts { get; private set; }

        /// <summary>The insert, counted from 1, that finds its alias taken; none by default.</summary>
        public int TakeTheAliasAtInsert { get; set; }

        /// <summary>A repository over <paramref name="inner"/>.</summary>
        public static RacedForms Over(IFormRepository inner)
        {
            var proxy = (RacedForms)(object)Create<IFormRepository, RacedForms>();
            proxy._inner = inner;
            return proxy;
        }

        /// <inheritdoc />
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            if (targetMethod.Name == nameof(IFormRepository.CreateAsync) && ++Inserts == TakeTheAliasAtInsert)
            {
                return Task.FromException<SavedVersion>(new FormBuilderConflictException(FormBuilderConflict.DuplicateAlias, "JFB_TEST: another server took the alias."));
            }

            try
            {
                return targetMethod.Invoke(_inner, args);
            }
            catch (TargetInvocationException exception) when (exception.InnerException is { } thrown)
            {
                ExceptionDispatchInfo.Throw(thrown);
                throw;
            }
        }
    }
#pragma warning restore CA1852

    // The site's option list repository, every call passed on, except the next inserts a test names: they meet SQLite's
    // lock conflict, so the database rolls the unit back and runs it again.
    internal sealed class LockedLists(IOptionListRepository inner) : IOptionListRepository
    {
        /// <summary>How many of the next inserts fail.</summary>
        public int FailNextCreates { get; set; }

        /// <inheritdoc />
        public Task<StoredOptionList> CreateAsync(NewOptionList list, CancellationToken cancellationToken = default)
        {
            if (FailNextCreates > 0)
            {
                FailNextCreates--;
                throw new SqliteException("JFB_TEST: database is locked", 5);
            }

            return inner.CreateAsync(list, cancellationToken);
        }

        /// <inheritdoc />
        public Task<StoredOptionList?> GetAsync(Guid key, CancellationToken cancellationToken = default) => inner.GetAsync(key, cancellationToken);

        /// <inheritdoc />
        public Task<StoredOptionList?> GetByIdAsync(int id, CancellationToken cancellationToken = default) => inner.GetByIdAsync(id, cancellationToken);

        /// <inheritdoc />
        public Task<StoredOptionList?> GetByAliasAsync(string alias, CancellationToken cancellationToken = default) => inner.GetByAliasAsync(alias, cancellationToken);

        /// <inheritdoc />
        public Task<Page<StoredOptionList>> ListAsync(string? search, PageRequest page, CancellationToken cancellationToken = default) => inner.ListAsync(search, page, cancellationToken);

        /// <inheritdoc />
        public Task<IReadOnlyList<string>> ListAliasesAsync(string prefix, CancellationToken cancellationToken = default) => inner.ListAliasesAsync(prefix, cancellationToken);

        /// <inheritdoc />
        public Task<StoredOptionList?> UpdateAsync(OptionListUpdate update, CancellationToken cancellationToken = default) => inner.UpdateAsync(update, cancellationToken);

        /// <inheritdoc />
        public Task<bool> DeleteAsync(int id, int expectedConcurrencyVersion, CancellationToken cancellationToken = default) => inner.DeleteAsync(id, expectedConcurrencyVersion, cancellationToken);
    }

    // The export file of a form whose dropdowns name these lists, which the site lacks, carrying the lists given.
    private async Task<string> FileAsync(Guid[] lists, params JsonObject[] carried)
    {
        var created = await Service.CreateAsync(new NewFormRequest("Booking", UserKey: Editor));
        var draft = (await Service.GetDraftAsync(created.Key))!.Definition!;
        var fields = lists
            .Select((list, i) => new FormField { Key = Guid.NewGuid(), Type = "dropdown", Alias = "choice" + i, Label = new LocalizedText("Choice"), Options = new FieldOptions { Source = OptionSource.List, OptionList = list } })
            .ToArray();
        await Service.SaveDraftAsync(created.Key, created.ConcurrencyVersion, WithFields(draft, fields), null, Editor);
        var envelope = JsonNode.Parse((await Exchange.ExportAsync(created.Key))!.Json)!.AsObject();
        envelope["optionLists"] = new JsonArray(carried);
        return envelope.ToJsonString();
    }
}
