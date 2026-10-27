using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Exchange;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Tests.Unit.Definitions;
using Jiron.Umbraco.FormBuilder.Tests.Unit.OptionLists;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Umbraco.Cms.Core.Models.Membership;
using Umbraco.Cms.Core.Services;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Forms;

/// <summary>
/// The option lists of an import (ADR-0078): a list the file carries planned for a user who may manage the library,
/// with its key, its contents and the first free alias; refused by its kind's checks on this site, logged by code and
/// path only; none for anyone else, who is not even looked up while nothing is to be created; written in the form's unit
/// with a list taken meanwhile refused as taken; and forgotten by every server once it has committed.
/// </summary>
[TestFixture]
public sealed class FormImportListsTests
{
    private const string Rooms = """[{"value":"single","label":{"":"Single","de":"Einzel"}},{"value":"double","label":{"":"Double"}}]""";

    private static readonly Guid Manager = Guid.Parse("78000000-0000-4000-8000-000000000001");
    private static readonly Guid Designer = Guid.Parse("78000000-0000-4000-8000-000000000002");
    private static readonly Guid Stranger = Guid.Parse("78000000-0000-4000-8000-000000000003");
    private static readonly Guid RoomsKey = Guid.Parse("78000000-0000-4000-8000-000000000011");
    private static readonly Guid MealsKey = Guid.Parse("78000000-0000-4000-8000-000000000012");
    private static readonly Guid FeedKey = Guid.Parse("78000000-0000-4000-8000-000000000013");
    private static readonly Guid OtherFeedKey = Guid.Parse("78000000-0000-4000-8000-000000000014");

    [Test]
    public async Task AManager_GetsACarriedList_WithItsKey_ItsContents_AndItsAlias()
    {
        var import = new Import();
        var definition = Naming(RoomsKey);
        var notes = new List<FormImportNote>();

        var plan = await import.Lists.PlanAsync(definition, [Manual(RoomsKey, "rooms", Rooms) with { Name = "Guest rooms" }], Manager, notes, CancellationToken.None);

        Assert.Multiple(async () =>
        {
            Assert.That(plan.Created, Is.EqualTo(new[] { new NewOptionList(RoomsKey, "rooms", "Guest rooms", OptionListKind.Manual, "{}", Rooms, 0) }), "the file's key, name, kind and contents");
            Assert.That(notes, Is.EqualTo(new[] { new FormImportNote(FormImportNotes.OptionListCreated, Path(0), "rooms") }));
            Assert.That(plan.Definition, Is.SameAs(definition), "the fields keep naming the list by its key");
            Assert.That(await import.Site.Repository.GetAsync(RoomsKey), Is.Null, "a plan writes nothing");
        });
    }

    [Test]
    public async Task AnAliasAnotherListHas_GivesWayToTheFirstFreeNumber_LikeOneFromThisImport()
    {
        var import = new Import();
        await import.Site.Repository.CreateAsync(new NewOptionList(Guid.NewGuid(), "rooms", "Rooms", OptionListKind.Manual, Items: """[{"value":"suite"}]"""));
        await import.Site.Repository.CreateAsync(new NewOptionList(Guid.NewGuid(), "ROOMS2", "Rooms", OptionListKind.Manual));
        var longest = "a" + new string('b', DefinitionStructureChecks.MaxAliasLength - 1);
        await import.Site.Repository.CreateAsync(new NewOptionList(Guid.NewGuid(), longest, "Long", OptionListKind.Manual));
        var keys = Enumerable.Range(21, 5).Select(i => Guid.Parse($"78000000-0000-4000-8000-0000000000{i}")).ToArray();
        var carried = new[]
        {
            Manual(keys[0], "rooms", Rooms),
            Manual(keys[1], "rooms", Rooms),
            Manual(keys[2], "Not an alias", Rooms) with { Name = "Guest rooms" },
            Manual(keys[3], longest, Rooms),
            Manual(keys[4], "meals", Rooms),
        };
        var notes = new List<FormImportNote>();

        var plan = await import.Lists.PlanAsync(Naming(keys), carried, Manager, notes, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Created.Select(list => (list.Key, list.Alias)), Is.EqualTo(new[]
            {
                (keys[0], "rooms3"),
                (keys[1], "rooms4"),
                (keys[2], "guestRooms"),
                (keys[3], longest[..^1] + "2"),
                (keys[4], "meals"),
            }), "past the site's aliases in any letter case and those this import takes; one from the name for an alias that is none; cut by one for its number");
            Assert.That(notes.Select(note => (note.Code, note.Alias)), Is.EqualTo(plan.Created.Select(list => (FormImportNotes.OptionListCreated, (string?)list.Alias))), "each note names the alias the list gets");
        });
    }

    [Test]
    public async Task AListItsKindsChecksRefuse_IsMissing_AndLoggedByCodeAndPathOnly()
    {
        var import = new Import();
        var keys = new[] { FeedKey, OtherFeedKey, MealsKey, RoomsKey };
        var carried = new[]
        {
            Url(FeedKey, "feed", "https://lists.example.com/rooms.json"),
            Url(OtherFeedKey, "elsewhere", "https://elsewhere.example.com/rooms.json"),
            Manual(MealsKey, "meals", """[{"value":"vegan"},{"value":"vegan"}]"""),
            Manual(RoomsKey, "rooms", Rooms) with { Kind = "maps" },
        };
        var notes = new List<FormImportNote>();

        var plan = await import.Lists.PlanAsync(Naming(keys), carried, Manager, notes, CancellationToken.None);

        var logged = import.Logs.Entries;
        Assert.Multiple(() =>
        {
            Assert.That(plan.Created.Select(list => list.Key), Is.EqualTo(new[] { FeedKey }), "a URL list on a host the site calls");
            Assert.That(notes, Is.EqualTo(new[]
            {
                new FormImportNote(FormImportNotes.OptionListCreated, Path(0), "feed"),
                new FormImportNote(FormImportNotes.OptionListMissing, Path(1), "elsewhere"),
                new FormImportNote(FormImportNotes.OptionListMissing, Path(2), "meals"),
                new FormImportNote(FormImportNotes.OptionListMissing, Path(3), "rooms"),
            }), "another host, a value given twice, a kind this site does not have");
            Assert.That(logged.Select(entry => entry.Level), Is.EqualTo(new[] { LogLevel.Information, LogLevel.Information, LogLevel.Information }));
            Assert.That(logged[0].Message, Does.Contain(OtherFeedKey.ToString()).And.Contain(DefinitionErrors.SettingInvalid + " at $.config.url"));
            Assert.That(logged[1].Message, Does.Contain(MealsKey.ToString()).And.Contain(" at $.items[1]"));
            Assert.That(logged[2].Message, Does.Contain(RoomsKey.ToString()).And.Contain(DefinitionErrors.OptionsInvalid + " at $.kind"));
            Assert.That(logged.Select(entry => entry.Message), Has.None.Contain("elsewhere.example.com").And.None.Contain("vegan").And.None.Contain("maps"), "never the configuration, an item or the file's kind");
        });
    }

    [Test]
    public async Task AUserWhoMayNotManageTheLibrary_OneUmbracoLacks_OrNone_CreatesNoList()
    {
        var import = new Import();
        var carried = new[] { Manual(RoomsKey, "rooms", Rooms) };

        var plans = new List<(FormImportListPlan Plan, List<FormImportNote> Notes)>();
        foreach (var user in new Guid?[] { Designer, Stranger, null })
        {
            var notes = new List<FormImportNote>();
            plans.Add((await import.Lists.PlanAsync(Naming(RoomsKey), carried, user, notes, CancellationToken.None), notes));
        }

        Assert.Multiple(async () =>
        {
            Assert.That(plans.Select(plan => plan.Plan.Created), Is.All.Empty);
            Assert.That(plans.Select(plan => plan.Notes), Is.All.EqualTo(new[] { new FormImportNote(FormImportNotes.OptionListMissing, Path(0), "rooms") }));
            await import.Users.Received(1).GetAsync(Designer);
            await import.Users.Received(1).GetAsync(Stranger);
            await import.Access.Received(1).GetGlobalAsync(Arg.Is<AccessUser>(user => user.Key == Designer), Arg.Any<CancellationToken>());
            await import.Access.DidNotReceive().GetGlobalAsync(Arg.Is<AccessUser>(user => user.Key == Stranger), Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public async Task WithNothingToCreate_NobodyIsLookedUp_AndTheSitesListsStayFirst()
    {
        var import = new Import();
        await import.Site.Repository.CreateAsync(new NewOptionList(RoomsKey, "rooms", "Rooms", OptionListKind.Manual, Items: Rooms));
        var same = await import.Site.Repository.CreateAsync(new NewOptionList(Guid.NewGuid(), "meals", "Meals", OptionListKind.Manual, Items: Rooms));
        var notes = new List<FormImportNote>();

        var plan = await import.Lists.PlanAsync(Naming(RoomsKey, MealsKey, FeedKey), [Manual(MealsKey, "MEALS", Rooms)], Manager, notes, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That(plan.Created, Is.Empty);
            Assert.That(notes, Is.EqualTo(new[]
            {
                new FormImportNote(FormImportNotes.OptionListReused, Path(1), "meals"),
                new FormImportNote(FormImportNotes.OptionListMissing, Path(2), null),
            }), "a list by key stays, the same list by alias takes the place of the file's, and one the file lacks is missing");
            Assert.That(DefinitionKeys.Fields(plan.Definition).Select(field => field.Options!.OptionList), Is.EqualTo(new Guid?[] { RoomsKey, same.Key, FeedKey }));
            Assert.That(import.Users.ReceivedCalls(), Is.Empty, "who the user is matters only for a list to create");
        });
    }

    [Test]
    public async Task ThePlansLists_AreWrittenInTheFormsUnit_AndForgottenByEveryServerOnceCommitted()
    {
        var import = new Import();
        var plan = await import.Lists.PlanAsync(Naming(RoomsKey), [Manual(RoomsKey, "rooms", Rooms)], Manager, [], CancellationToken.None);
        var before = await import.Site.Lists.GetAsync(RoomsKey);

        await import.Lists.CreateAsync(plan.Created, CancellationToken.None);
        var written = await import.Site.Repository.GetAsync(RoomsKey);
        var kept = await import.Site.Lists.GetAsync(RoomsKey);
        import.Lists.Committed(plan.Created);
        var after = await import.Site.Lists.GetAsync(RoomsKey);

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.Null, "the server keeps the list as missing");
            Assert.That(written, Has.Property(nameof(StoredOptionList.Alias)).EqualTo("rooms"));
            Assert.That(kept, Is.Null, "until the unit has committed");
            Assert.That(after, Has.Property(nameof(OptionListEntry.IsReadable)).True, "then this server reads it at once");
            Assert.That(import.Site.Told, Is.EqualTo(new[] { RoomsKey }), "and the others are told");
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task AListWhoseKeyOrAliasWasTakenSinceThePlan_IsRefusedAsTaken(bool keyTaken)
    {
        var conflict = keyTaken ? FormBuilderConflict.DuplicateKey : FormBuilderConflict.DuplicateAlias;
        var import = new Import();
        var plan = await import.Lists.PlanAsync(Naming(RoomsKey), [Manual(RoomsKey, "rooms", Rooms)], Manager, [], CancellationToken.None);
        import.Site.Repository.Conflict = conflict;

        var taken = Assert.ThrowsAsync<FormImportListTakenException>(async () => await import.Lists.CreateAsync(plan.Created, CancellationToken.None));

        Assert.That(taken!.Conflict.Conflict, Is.EqualTo(conflict), "not the form's alias, which the form's create tries again on its own");
    }

    private static string Path(int field) => $"$.definition.pages[0].groups[0].fields[{field}].options.list";

    // A definition whose first group has a dropdown from each list, in order.
    private static FormDefinition Naming(params Guid[] lists) => new()
    {
        Key = Guid.NewGuid(),
        Pages =
        [
            new FormPage
            {
                Key = Guid.NewGuid(),
                Groups =
                [
                    new FormGroup
                    {
                        Key = Guid.NewGuid(),
                        Fields = [.. lists.Select((list, i) => new FormField { Key = Guid.NewGuid(), Type = "dropdown", Alias = "choice" + i, Label = new LocalizedText("Choice"), Options = new FieldOptions { Source = OptionSource.List, OptionList = list } })],
                    },
                ],
            },
        ],
    };

    private static ExportedOptionList Manual(Guid key, string alias, string items) => new(key, alias, alias, "manual", "{}", items, 0);

    private static ExportedOptionList Url(Guid key, string alias, string url) =>
        new(key, alias, alias, "url", $$"""{"url":"{{url}}","listPath":"$.items","valueFrom":"$.id","labelFrom":"$.name"}""", "[]", 300);

    // A site that calls lists.example.com, with a user who may manage the library, one who may only design, and a
    // key that names no user.
    private sealed class Import
    {
        public Import()
        {
            var manager = User(Manager);
            var designer = User(Designer);
            Users.GetAsync(Manager).Returns(manager);
            Users.GetAsync(Designer).Returns(designer);
            Access.GetGlobalAsync(Arg.Any<AccessUser>(), Arg.Any<CancellationToken>())
                .Returns(call => call.Arg<AccessUser>().Key == Manager ? FormBuilderPermissions.Design | FormBuilderPermissions.ManageLibrary : FormBuilderPermissions.Design);
            Lists = new FormImportLists(Site.Repository, Site.Lists, Access, Users, DefinitionFixtures.Serializer(), Logs);
        }

        public OptionListTestSite Site { get; } = new(new OptionListTestHosts(AllowedHosts: ["lists.example.com"]));

        public IUserService Users { get; } = Substitute.For<IUserService>();

        public IFormBuilderAccessService Access { get; } = Substitute.For<IFormBuilderAccessService>();

        public RecordingLogger<FormBuilderFormExchangeService> Logs { get; } = new();

        public FormImportLists Lists { get; }

        private static IUser User(Guid key)
        {
            var user = Substitute.For<IUser>();
            user.Key.Returns(key);
            user.Groups.Returns([]);
            user.AllowedSections.Returns([]);
            return user;
        }
    }
}
