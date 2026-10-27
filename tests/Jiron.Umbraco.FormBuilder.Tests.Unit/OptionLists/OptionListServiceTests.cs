using System.Globalization;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Core.Persistence;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.OptionLists;

/// <summary>
/// The library's lists (ADR-0073): each change is held to its kind's checks with every problem at its path, the
/// database's refusals become problems, and every change drops what each server kept of the list.
/// </summary>
[TestFixture]
public sealed class OptionListServiceTests
{
    private static readonly Guid ListKey = Guid.Parse("73000000-0000-4000-8000-000000000030");

    [Test]
    public async Task AList_HasAnAliasANameAndATimeWithinTheirBounds()
    {
        var lists = new OptionListTestSite().Lists;

        Assert.Multiple(async () =>
        {
            Assert.That(await CheckAsync(lists, Manual() with { Alias = "Sizes" }), Is.EqualTo(new[] { (DefinitionErrors.AliasInvalid, "$.alias") }));
            Assert.That(await CheckAsync(lists, Manual() with { Alias = null! }), Is.EqualTo(new[] { (DefinitionErrors.AliasInvalid, "$.alias") }));
            Assert.That(await CheckAsync(lists, Manual() with { Name = "  " }), Is.EqualTo(new[] { (DefinitionErrors.InvalidValue, "$.name") }));
            Assert.That(await CheckAsync(lists, Manual() with { Name = new string('n', 256) }), Is.EqualTo(new[] { (DefinitionErrors.LimitExceeded, "$.name") }));
            Assert.That(await CheckAsync(lists, Manual() with { Name = new string('n', 255) }), Is.Empty);
            Assert.That(await CheckAsync(lists, Manual() with { CacheSeconds = -1 }), Is.EqualTo(new[] { (DefinitionErrors.InvalidValue, "$.cacheSeconds") }));
            Assert.That(await CheckAsync(lists, Manual() with { CacheSeconds = OptionListService.MaxCacheSeconds + 1 }), Is.EqualTo(new[] { (DefinitionErrors.InvalidValue, "$.cacheSeconds") }));
            Assert.That(await CheckAsync(lists, Code("""{"source":"months"}""") with { CacheSeconds = OptionListService.MaxCacheSeconds }), Is.Empty, "30 days");
            Assert.That(await CheckAsync(lists, Manual() with { CacheSeconds = 60 }), Is.EqualTo(new[] { (DefinitionErrors.InvalidValue, "$.cacheSeconds") }), "a manual list is kept until it changes");
            Assert.That((await lists.CheckAsync(Countries("{}") with { CacheSeconds = 60 }))[0].Message, Is.EqualTo("A countries list keeps nothing for a time, so its cacheSeconds is 0."));
            Assert.That(OptionListService.MaxCacheSeconds, Is.EqualTo(2_592_000));
        });
    }

    [Test]
    public async Task AListsConfiguration_IsHeldToItsKind_AtItsPath()
    {
        var lists = new OptionListTestSite().Lists;

        Assert.Multiple(async () =>
        {
            Assert.That(await CheckAsync(lists, Manual() with { Kind = OptionListKind.File }), Is.EqualTo(new[] { (DefinitionErrors.OptionsInvalid, "$.kind") }), "a kind of a later step");
            Assert.That((await lists.CheckAsync(Manual() with { Kind = OptionListKind.Url }))[0].Message, Is.EqualTo("The site has no option lists of the kind 'url'."));
            Assert.That(await CheckAsync(lists, Manual() with { Kind = (OptionListKind)99 }), Is.EqualTo(new[] { (DefinitionErrors.OptionsInvalid, "$.kind") }));
            Assert.That(await CheckAsync(lists, Manual() with { Config = "{" }), Is.EqualTo(new[] { (DefinitionErrors.InvalidJson, "$.config") }));
            Assert.That(await CheckAsync(lists, Manual() with { Config = null! }), Is.EqualTo(new[] { (DefinitionErrors.InvalidJson, "$.config") }));
            Assert.That((await CheckAsync(lists, Manual() with { Config = "[]" })).Select(problem => problem.Path), Is.EqualTo(new[] { "$.config" }));
            Assert.That(await CheckAsync(lists, Manual() with { Config = """{"sorted":true}""" }), Is.EqualTo(new[] { (DefinitionErrors.SettingUnknown, "$.config.sorted") }));
            Assert.That(await CheckAsync(lists, Manual() with { Config = """{ }""" }), Is.Empty, "an object with nothing in it is no configuration");
            Assert.That(await CheckAsync(lists, Countries("""{"countries":["CH","XX"]}""")), Is.EqualTo(new[] { (DefinitionErrors.SettingInvalid, "$.config.countries[1]") }));
            Assert.That(await CheckAsync(lists, Countries("{}")), Is.Empty);
            Assert.That(await CheckAsync(lists, Code("{}")), Is.EqualTo(new[] { (DefinitionErrors.SettingMissing, "$.config.source") }));
            Assert.That(await CheckAsync(lists, Code("""{"source":"products"}""")), Is.EqualTo(new[] { (DefinitionErrors.SettingInvalid, "$.config.source") }));
            Assert.That(await CheckAsync(lists, Code("""{"source":"months"}""")), Is.Empty);
        });
    }

    [Test]
    public async Task AListsItems_AreAFieldsOwnOptions_ForAManualListOnly()
    {
        var lists = new OptionListTestSite().Lists;
        var tooMany = "[" + string.Join(",", Enumerable.Range(0, OptionListService.MaxOptions + 1).Select(i => string.Create(CultureInfo.InvariantCulture, $$"""{"value":"v{{i}}"}"""))) + "]";
        var most = "[" + string.Join(",", Enumerable.Range(0, OptionListService.MaxOptions).Select(i => string.Create(CultureInfo.InvariantCulture, $$"""{"value":"v{{i}}"}"""))) + "]";

        Assert.Multiple(async () =>
        {
            Assert.That(await CheckAsync(lists, Manual("""[{"value":"s","label":{"":"Small","de":"Klein"},"places":4,"parentValues":["x"]}]""")), Is.Empty);
            Assert.That(await CheckAsync(lists, Manual("""[{"value":1}]""")), Is.EqualTo(new[] { (DefinitionErrors.InvalidType, "$.items[0].value") }), "the shape checks of a field's own options");
            Assert.That((await CheckAsync(lists, Manual("""[{"value":"s","colour":"red"}]"""))).Select(problem => problem.Path), Is.EqualTo(new[] { "$.items[0].colour" }));
            Assert.That(await CheckAsync(lists, Manual("[]")), Is.Empty, "no items");
            Assert.That(await CheckAsync(lists, Manual("[")), Is.EqualTo(new[] { (DefinitionErrors.InvalidJson, "$.items") }));
            Assert.That(await CheckAsync(lists, Manual("""{"value":"s"}""")), Is.EqualTo(new[] { (DefinitionErrors.InvalidJson, "$.items") }));
            Assert.That(await CheckAsync(lists, Manual("null")), Is.EqualTo(new[] { (DefinitionErrors.InvalidJson, "$.items") }));
            Assert.That(await CheckAsync(lists, Manual("[null]")), Is.EqualTo(new[] { (DefinitionErrors.InvalidType, "$.items[0]") }));
            Assert.That(await CheckAsync(lists, Manual("""[{"value":"s"},{"value":"S"}]""")), Is.EqualTo(new[] { (DefinitionErrors.OptionValueDuplicate, "$.items[1].value") }), "unique once folded");
            Assert.That(await CheckAsync(lists, Manual("""[{"value":" s"}]""")), Is.EqualTo(new[] { (DefinitionErrors.OptionValueInvalid, "$.items[0].value") }), "clean as answers are");
            Assert.That((await CheckAsync(lists, Manual("""[{"value":"s","places":-1}]"""))).Select(problem => problem.Path), Is.EqualTo(new[] { "$.items[0].places" }));
            Assert.That(await CheckAsync(lists, Manual(tooMany)), Is.EqualTo(new[] { (DefinitionErrors.LimitExceeded, "$.items") }));
            Assert.That(await CheckAsync(lists, Manual(most)), Is.Empty, "10,000 options");
            Assert.That(await CheckAsync(lists, Countries("{}") with { Items = """[{"value":"s"}]""" }), Is.EqualTo(new[] { (DefinitionErrors.OptionsInvalid, "$.items") }), "a source's list has no items of its own");
            Assert.That((await lists.CheckAsync(Countries("{}") with { Items = """[{"value":"s"}]""" }))[0].Message, Is.EqualTo("A countries list takes its options from its source and has no items of its own."));
        });
    }

    [Test]
    public async Task ACreate_StoresTheList_AndTellsEveryServer_UnlessItsContentsOrTheDatabaseRefuseIt()
    {
        var site = new OptionListTestSite();

        var refused = await site.Lists.CreateAsync(ListKey, Manual() with { Alias = "Bad" });
        var created = await site.Lists.CreateAsync(ListKey, Manual());
        site.Repository.Conflict = FormBuilderConflict.DuplicateAlias;
        var taken = await site.Lists.CreateAsync(Guid.NewGuid(), Manual());
        site.Repository.Conflict = FormBuilderConflict.DuplicateKey;
        var sameKey = await site.Lists.CreateAsync(ListKey, Manual() with { Alias = "other" });
        var entry = await site.Lists.GetAsync(ListKey);

        Assert.Multiple(() =>
        {
            Assert.That(refused.Succeeded, Is.False);
            Assert.That(refused.Problems.Select(problem => problem.Code), Is.EqualTo(new[] { DefinitionErrors.AliasInvalid }));
            Assert.That(created.Succeeded, Is.True);
            Assert.That((created.List!.Key, created.List.Alias, created.List.Kind, created.List.Items), Is.EqualTo((ListKey, "sizes", OptionListKind.Manual, """[{"value":"s"}]""")));
            Assert.That(taken.Problems, Is.EqualTo(new[] { new DefinitionProblem(DefinitionErrors.AliasDuplicate, "$.alias", "Another option list has this alias (letter case does not count).") }));
            Assert.That(sameKey.Problems, Is.EqualTo(new[] { new DefinitionProblem(DefinitionErrors.InvalidValue, "$.key", "Another option list has this key.") }));
            Assert.That(site.Told, Is.EqualTo(new[] { ListKey }), "only what was stored");
            Assert.That(entry, Is.Not.Null);
            Assert.That((entry!.IsReadable, entry.Kind?.Kind, entry.Config), Is.EqualTo((true, (OptionListKind?)OptionListKind.Manual, (SettingsObject?)null)));
            Assert.That(entry.Items.Select(item => item.Value), Is.EqualTo(new[] { "s" }));
        });
    }

    [Test]
    public async Task AnUpdate_ChangesTheList_AndDropsWhatWasKept_EvenWhenItChangedMeanwhile()
    {
        var site = new OptionListTestSite();
        var created = (await site.Lists.CreateAsync(ListKey, Manual())).List!;
        await site.Lists.GetAsync(ListKey);

        var missing = await site.Lists.UpdateAsync(Guid.NewGuid(), 1, Manual());
        var refused = await site.Lists.UpdateAsync(ListKey, created.ConcurrencyVersion, Manual("[]") with { Name = string.Empty });
        site.Repository.Conflict = FormBuilderConflict.DuplicateAlias;
        var taken = await site.Lists.UpdateAsync(ListKey, created.ConcurrencyVersion, Manual() with { Alias = "colours" });
        var changed = await site.Lists.UpdateAsync(ListKey, created.ConcurrencyVersion, Manual("""[{"value":"m"}]"""));
        var stale = await site.Lists.GetAsync(ListKey);
        var readsBefore = site.Repository.Reads;
        var meanwhile = Assert.CatchAsync(async () => await site.Lists.UpdateAsync(ListKey, created.ConcurrencyVersion, Manual()));
        var reread = await site.Lists.GetAsync(ListKey);
        var readsAfter = site.Repository.Reads;

        Assert.Multiple(() =>
        {
            Assert.That(missing.List, Is.Null, "no list");
            Assert.That(missing.Problems, Is.Empty, "and no problems");
            Assert.That(refused.Problems.Select(problem => problem.Path), Is.EqualTo(new[] { "$.name" }));
            Assert.That(taken.Problems.Single().Code, Is.EqualTo(DefinitionErrors.AliasDuplicate));
            Assert.That(changed.List!.ConcurrencyVersion, Is.EqualTo(created.ConcurrencyVersion + 1));
            Assert.That(stale!.Items.Select(item => item.Value), Is.EqualTo(new[] { "m" }), "the change shows at once");
            Assert.That(meanwhile, Is.InstanceOf<FormBuilderConcurrencyException>());
            Assert.That(reread, Is.Not.Null);
            Assert.That(readsAfter, Is.EqualTo(readsBefore + 2), "the change's own read, then the copy it dropped, which may be older than the row");
            Assert.That(site.Told, Is.EqualTo(new[] { ListKey, ListKey, ListKey }), "the create, the change and the refused change");
        });
    }

    [Test]
    public async Task ADelete_RemovesTheList_AndDropsWhatWasKept()
    {
        var site = new OptionListTestSite();
        var created = (await site.Lists.CreateAsync(ListKey, Manual())).List!;
        var kept = await site.Lists.GetAsync(ListKey);

        var missing = await site.Lists.DeleteAsync(Guid.NewGuid(), 1);
        var deleted = await site.Lists.DeleteAsync(ListKey, created.ConcurrencyVersion);
        var after = await site.Lists.GetAsync(ListKey);

        Assert.Multiple(() =>
        {
            Assert.That(kept, Is.Not.Null);
            Assert.That((missing, deleted), Is.EqualTo((false, true)));
            Assert.That(after, Is.Null, "gone at once");
            Assert.That(site.Told, Is.EqualTo(new[] { ListKey, ListKey }));
        });
    }

    [Test]
    public async Task AListTheSiteLacks_IsKeptAsMissing_SoAPageThatNamesItDoesNotReadTheDatabaseEachTime()
    {
        var site = new OptionListTestSite();
        var key = Guid.NewGuid();

        await site.Lists.GetAsync(key);
        await site.Lists.GetAsync(key);
        var created = await site.Lists.CreateAsync(key, Manual());
        var found = await site.Lists.GetAsync(key);

        Assert.Multiple(() =>
        {
            Assert.That(site.Repository.Reads, Is.EqualTo(2), "once while missing, once after the create");
            Assert.That(created.Succeeded, Is.True);
            Assert.That(found, Is.Not.Null, "a create drops the missing mark");
        });
    }

    private static OptionListInput Manual(string items = """[{"value":"s"}]""") => new("sizes", "Sizes", OptionListKind.Manual, Items: items);

    private static OptionListInput Countries(string config) => new("countries", "Countries", OptionListKind.Countries, Config: config);

    private static OptionListInput Code(string config) => new("products", "Products", OptionListKind.Code, Config: config);

    private static async Task<List<(string Code, string Path)>> CheckAsync(OptionListService lists, OptionListInput input) =>
        [.. (await lists.CheckAsync(input)).Select(problem => (problem.Code, problem.Path))];
}
