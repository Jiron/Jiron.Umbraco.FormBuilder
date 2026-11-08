using System.Text;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Access;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Testing.Stub;
using Jiron.Umbraco.FormBuilder.Tests.Integration.OptionLists;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Testing;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Cache;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>
/// The library's option lists through the management API (ROADMAP 12.1, ADR-0214): the list with the forms that use
/// each list, which forms those are as a user may see them, the changes, each audited, the delete that waits until no form
/// uses a list, a duplicate's free alias, the preview of contents not saved yet, and files of options read with the line
/// of each problem.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class OptionListLibraryApiTests : ManagementApiTestBase
{
    private const string Topics = """[{"value":"sales","label":{"":"Sales","de-DE":"Verkauf"}},{"value":"support","label":{"":"Support"}}]""";

    [Test]
    public async Task TheList_ShowsEachListByName_WithItsKind_ItsOptions_AndHowManyFormsUseIt_DraftsAndTheRecycleBinIncluded()
    {
        var topics = await ListAsync("topics", "Topics", OptionListKind.Manual, items: Topics);
        var countries = await ListAsync("countries", "Countries we ship to", OptionListKind.Countries, config: """{"countries":["CH","DE"]}""");
        await FormUsingAsync("Contact us", topics, publish: true);
        await FormUsingAsync("Callback", topics, publish: false);
        await TrashFormAsync(await FormUsingAsync("Old enquiry", topics, publish: false));

        var answer = await CallAsync("designer", HttpMethod.Get, "option-list/library");

        Assert.That(answer.Outcome, Is.EqualTo("200"), answer.ToString());
        var rows = answer.Body!["items"]!.AsArray();
        Assert.Multiple(() =>
        {
            Assert.That(answer.Body!["total"]!.GetValue<long>(), Is.EqualTo(2));
            Assert.That(rows.Select(row => row!["name"]!.GetValue<string>()), Is.EqualTo(new[] { "Countries we ship to", "Topics" }), "by name");
            Assert.That(rows[0]!["kind"]!.GetValue<string>(), Is.EqualTo("countries"));
            Assert.That(rows[0]!["count"], Is.Null, "a list that resolves its options has no count of its own");
            Assert.That(rows[0]!["usedBy"]!.GetValue<int>(), Is.Zero);
            Assert.That(rows[1]!["key"]!.GetValue<Guid>(), Is.EqualTo(topics));
            Assert.That(rows[1]!["count"]!.GetValue<int>(), Is.EqualTo(2));
            Assert.That(rows[1]!["usedBy"]!.GetValue<int>(), Is.EqualTo(3), "a live form, a draft and a form in the recycle bin");
            Assert.That(countries, Is.Not.EqualTo(Guid.Empty));
        });
    }

    [Test]
    public async Task UsedBy_NamesTheFormsTheUserMayOpen_WithWhereEachUsesTheList_AndOnlyCountsTheRest()
    {
        var topics = await ListAsync("topics", "Topics", OptionListKind.Manual, items: Topics);
        var live = await FormUsingAsync("Contact us", topics, publish: true);
        var hidden = await FolderAsync("Board");
        await RestrictAsync(hidden, "viewer", FormBuilderPermissions.ViewEntries);
        await FormUsingAsync("Board enquiry", topics, publish: false, folderKey: hidden);

        // The live form's draft stops using the list: its live version still does.
        var forms = GetRequiredService<IFormBuilderFormService>();
        var draft = (await forms.GetDraftAsync(live))!;
        _ = await forms.SaveDraftAsync(live, draft.Form.ConcurrencyVersion, Fillable(draft.Definition!), null, null);

        var designer = await CallAsync("designer", HttpMethod.Get, $"option-list/{topics}/used-by");
        var admin = await CallAsync("admin", HttpMethod.Get, $"option-list/{topics}/used-by");
        var missing = await CallAsync("designer", HttpMethod.Get, $"option-list/{Guid.NewGuid()}/used-by");

        Assert.Multiple(() =>
        {
            Assert.That(designer.Outcome, Is.EqualTo("200"), designer.ToString());
            Assert.That(designer.Body!["forms"]!.AsArray().Select(form => form!["name"]!.GetValue<string>()), Is.EqualTo(new[] { "Contact us" }), "the restricted folder's form is not named");
            Assert.That(designer.Body!["others"]!.GetValue<int>(), Is.EqualTo(1), "but counted");
            Assert.That(designer.Body!["otherUses"]!.GetValue<int>(), Is.EqualTo(1), "and its fields that use the list");
            var contact = designer.Body!["forms"]![0]!;
            Assert.That(contact["key"]!.GetValue<Guid>(), Is.EqualTo(live));
            Assert.That(contact["inDraft"]!.GetValue<bool>(), Is.False);
            Assert.That(contact["inLive"]!.GetValue<bool>(), Is.True);
            Assert.That(contact["isTrashed"]!.GetValue<bool>(), Is.False);
            Assert.That(contact["uses"]!.GetValue<int>(), Is.EqualTo(1));
            Assert.That(admin.Body!["forms"]!.AsArray().Select(form => form!["name"]!.GetValue<string>()), Is.EqualTo(new[] { "Board enquiry", "Contact us" }));
            Assert.That(admin.Body!["others"]!.GetValue<int>(), Is.Zero);
            Assert.That(admin.Body!["otherUses"]!.GetValue<int>(), Is.Zero);
            Assert.That(missing.Outcome, Is.EqualTo("404 option_list_not_found"));
        });
    }

    [Test]
    public async Task ACreate_ASave_AndADelete_AreAudited_AndTheDeleteWaitsUntilNoFormUsesTheList()
    {
        var created = await CallAsync("designer", HttpMethod.Post, "option-list", new { list = new { alias = "topics", name = "Topics", kind = "manual", items = JsonNode.Parse(Topics) } });
        var key = created.Body!["key"]!.GetValue<Guid>();
        var saved = await CallAsync("designer", HttpMethod.Put, $"option-list/{key}", new { concurrencyVersion = 1, list = new { alias = "contactTopics", name = "Contact topics", kind = "manual", items = JsonNode.Parse(Topics) } });
        var stale = await CallAsync("designer", HttpMethod.Put, $"option-list/{key}", new { concurrencyVersion = 1, list = new { alias = "topics", name = "Topics", kind = "manual" } });
        var form = await FormUsingAsync("Contact us", key, publish: false);
        var refused = await CallAsync("designer", HttpMethod.Delete, $"option-list/{key}?concurrencyVersion=2");
        await TrashFormAsync(form);
        var trashed = await CallAsync("designer", HttpMethod.Delete, $"option-list/{key}?concurrencyVersion=2");
        await GetRequiredService<IFormBuilderFormService>().DeleteAsync(form, (await GetRequiredService<IFormBuilderFormService>().GetAsync(form))!.ConcurrencyVersion, null);
        var deleted = await CallAsync("designer", HttpMethod.Delete, $"option-list/{key}?concurrencyVersion=2");
        var gone = await CallAsync("designer", HttpMethod.Get, $"option-list/{key}");

        var audit = GetRequiredService<IAuditRepository>();
        var entries = (await audit.QueryAsync(new AuditQuery { EntityKey = key }, PageRequest.First(10))).Items;
        Assert.Multiple(() =>
        {
            Assert.That(created.Outcome, Is.EqualTo("201"), created.ToString());
            Assert.That(created.Created, Is.EqualTo(key.ToString()));
            Assert.That(created.Body!["concurrencyVersion"]!.GetValue<int>(), Is.EqualTo(1));
            Assert.That(saved.Outcome, Is.EqualTo("200"), saved.ToString());
            Assert.That(saved.Body!["alias"]!.GetValue<string>(), Is.EqualTo("contactTopics"));
            Assert.That(saved.Body!["items"]!.AsArray(), Has.Count.EqualTo(2));
            Assert.That(stale.Outcome, Is.EqualTo("409 concurrency_conflict"));
            Assert.That(refused.Outcome, Is.EqualTo("409 in_use"), "a draft uses it");
            Assert.That(trashed.Outcome, Is.EqualTo("409 in_use"), "a form in the recycle bin can come back");
            Assert.That(deleted.Outcome, Is.EqualTo("204"), deleted.ToString());
            Assert.That(gone.Outcome, Is.EqualTo("404 option_list_not_found"));
            Assert.That(entries.Select(entry => entry.Action), Is.EquivalentTo(new[] { OptionListLibrary.CreatedAction, OptionListLibrary.SavedAction, OptionListLibrary.DeletedAction }));
            Assert.That(entries.Select(entry => entry.EntityType).Distinct(), Is.EqualTo(new[] { "optionList" }));
            Assert.That(entries.Single(entry => entry.Action == OptionListLibrary.SavedAction).Details, Does.Contain("\"alias\":\"contactTopics\"").And.Contain("\"kind\":\"manual\""));
            Assert.That(entries.Select(entry => entry.UserKey).Distinct(), Is.EqualTo(new Guid?[] { GetRequiredService<IUserService>().GetByEmail("api-designer@jfb.test")!.Key }), "the user who signed in");
        });
    }

    [Test]
    public async Task ACreate_ThatItsContentsRefuse_Answers400_WithEachProblemAtItsPath_AndKeepsNothing()
    {
        var answer = await CallAsync("designer", HttpMethod.Post, "option-list", new { list = new { alias = "1topics", name = " ", kind = "code", config = new { source = "planets" } } });
        var kind = await CallAsync("designer", HttpMethod.Post, "option-list", new { list = new { alias = "topics", name = "Topics", kind = "spreadsheet" } });

        Assert.Multiple(() =>
        {
            Assert.That(answer.Outcome, Does.StartWith("400"), answer.ToString());
            Assert.That(answer.Body!["errors"]!.AsArray().Select(error => error!["path"]!.GetValue<string>()), Is.EquivalentTo(new[] { "$.alias", "$.name", "$.config.source" }));
            Assert.That(kind.Outcome, Does.StartWith("400"), kind.ToString());
            Assert.That(kind.Body!["errors"]![0]!["path"]!.GetValue<string>(), Is.EqualTo("$.kind"));
        });
        Assert.That((await GetRequiredService<IOptionListRepository>().ListAsync(null, PageRequest.First(10))).Total, Is.Zero);
    }

    [Test]
    public async Task ADuplicate_TakesTheNameGiven_AndTheFirstFreeAliasAfterItsOwn_WithTheSameContents()
    {
        var topics = await ListAsync("topics", "Topics", OptionListKind.Manual, items: Topics);
        await ListAsync("topics2", "Older topics", OptionListKind.Manual, items: Topics);

        var copy = await CallAsync("designer", HttpMethod.Post, $"option-list/{topics}/duplicate", new { name = "Topics (copy)" });
        var missing = await CallAsync("designer", HttpMethod.Post, $"option-list/{Guid.NewGuid()}/duplicate", new { name = "Copy" });

        var audited = (await GetRequiredService<IAuditRepository>().QueryAsync(new AuditQuery { Action = OptionListLibrary.CreatedAction }, PageRequest.First(5))).Items;
        Assert.Multiple(() =>
        {
            Assert.That(copy.Outcome, Is.EqualTo("201"), copy.ToString());
            Assert.That(copy.Body!["alias"]!.GetValue<string>(), Is.EqualTo("topics3"));
            Assert.That(copy.Body!["name"]!.GetValue<string>(), Is.EqualTo("Topics (copy)"));
            Assert.That(copy.Body!["key"]!.GetValue<Guid>(), Is.Not.EqualTo(topics));
            Assert.That(copy.Body!["items"]!.ToJsonString(), Is.EqualTo(JsonNode.Parse(Topics)!.ToJsonString()));
            Assert.That(missing.Outcome, Is.EqualTo("404 option_list_not_found"));
            Assert.That(audited.Single().Details, Does.Contain(topics.ToString()), "the copy says what it copied");
        });
    }

    [Test]
    public async Task ThePreview_GivesTheOptionsInTheLanguageAsked_OrTheProblemsASaveWouldAnswerWith()
    {
        var german = await CallAsync("designer", HttpMethod.Post, "option-list/preview", new { kind = "manual", items = JsonNode.Parse(Topics), culture = "de-DE" });
        var standard = await CallAsync("designer", HttpMethod.Post, "option-list/preview", new { kind = "code", config = new { source = "agreement" }, culture = "en-US" });
        var broken = await CallAsync("designer", HttpMethod.Post, "option-list/preview", new { kind = "code", config = new { source = "planets" }, culture = "en-US" });

        Assert.Multiple(() =>
        {
            Assert.That(german.Outcome, Is.EqualTo("200"), german.ToString());
            Assert.That(german.Body!["options"]!.AsArray().Select(option => option!["label"]!.GetValue<string>()), Is.EqualTo(new[] { "Verkauf", "Support" }), "a German label where there is one, the default text otherwise");
            Assert.That(german.Body!["isAvailable"]!.GetValue<bool>(), Is.True);
            Assert.That(standard.Body!["options"]!.AsArray().Select(option => option!["value"]!.GetValue<string>()), Is.EqualTo(new[] { "1", "2", "3", "4", "5" }));
            Assert.That(broken.Outcome, Is.EqualTo("200"), "the preview says what is wrong instead of failing");
            Assert.That(broken.Body!["isAvailable"]!.GetValue<bool>(), Is.False);
            Assert.That(broken.Body!["options"]!.AsArray(), Is.Empty);
            Assert.That(broken.Body!["problems"]![0]!["path"]!.GetValue<string>(), Is.EqualTo("$.config.source"));
        });
    }

    [Test]
    public async Task AFileOfOptions_ReadsIntoItems_CsvOrTxt_AndAProblemNamesItsLine()
    {
        var csv = await CallAsync("designer", HttpMethod.Post, "option-list/read-file", new { fileName = "topics.CSV", content = Encoding.UTF8.GetBytes("value;label\nsales;Sales\n\"support\";\"Support; general\"\n") });
        var txt = await CallAsync("designer", HttpMethod.Post, "option-list/read-file", new { fileName = "topics.txt", content = Encoding.UTF8.GetBytes("sales|Sales\nsupport\n") });
        var wrong = await CallAsync("designer", HttpMethod.Post, "option-list/read-file", new { fileName = "topics.csv", content = Encoding.UTF8.GetBytes("sales,Sales\nSALES,Again\n") });
        var other = await CallAsync("designer", HttpMethod.Post, "option-list/read-file", new { fileName = "topics.xlsx", content = Encoding.UTF8.GetBytes("sales") });

        Assert.Multiple(() =>
        {
            Assert.That(csv.Outcome, Is.EqualTo("200"), csv.ToString());
            Assert.That(csv.Body!["count"]!.GetValue<int>(), Is.EqualTo(2));
            Assert.That(csv.Body!["items"]![1]!["label"]![""]!.GetValue<string>(), Is.EqualTo("Support; general"));
            Assert.That(txt.Body!["items"]!.AsArray().Select(item => item!["value"]!.GetValue<string>()), Is.EqualTo(new[] { "sales", "support" }));
            Assert.That(wrong.Outcome, Does.StartWith("400"), wrong.ToString());
            Assert.That(wrong.Body!["errors"]![0]!["path"]!.GetValue<string>(), Is.EqualTo("$.file"));
            Assert.That(wrong.Body!["errors"]![0]!["message"]!.GetValue<string>(), Does.StartWith("Line 2:"));
            Assert.That(other.Outcome, Does.StartWith("400"));
        });
    }

    [Test]
    public async Task TheEditor_IsToldTheSitesLanguages_AndWhatEachKindKeeps()
    {
        var answer = await CallAsync("designer", HttpMethod.Get, "option-list/editor");

        Assert.That(answer.Outcome, Is.EqualTo("200"), answer.ToString());
        var kinds = answer.Body!["kinds"]!.AsArray();
        Assert.Multiple(() =>
        {
            Assert.That(answer.Body!["languages"]!.AsArray().Select(language => language!["isDefault"]!.GetValue<bool>()).First(), Is.True, "the default first");
            Assert.That(kinds.Select(kind => kind!["kind"]!.GetValue<string>()), Is.EqualTo(new[] { "manual", "content", "dataType", "url", "file", "countries", "code" }));
            Assert.That(kinds.Where(kind => kind!["hasItems"]!.GetValue<bool>()).Select(kind => kind!["kind"]!.GetValue<string>()), Is.EqualTo(new[] { "manual", "file" }));
            Assert.That(kinds.Where(kind => kind!["keepsOptions"]!.GetValue<bool>()).Select(kind => kind!["kind"]!.GetValue<string>()), Is.EqualTo(new[] { "url", "code" }));
            Assert.That(kinds.Single(kind => kind!["kind"]!.GetValue<string>() == "url")!["minCacheSeconds"]!.GetValue<int>(), Is.EqualTo(60));
            Assert.That(answer.Body!["maxFileBytes"]!.GetValue<int>(), Is.EqualTo(OptionFileReader.MaxBytes));
        });
    }

    private async Task<Guid> ListAsync(string alias, string name, OptionListKind kind, string config = "{}", string items = "[]")
    {
        var created = await GetRequiredService<OptionListService>().CreateAsync(Guid.NewGuid(), new OptionListInput(alias, name, kind, config, items));
        Assert.That(created.Problems, Is.Empty);
        return created.List!.Key;
    }

    // A form whose first group holds a name and a dropdown that takes its options from the list.
    private async Task<Guid> FormUsingAsync(string name, Guid list, bool publish, Guid? folderKey = null)
    {
        var key = await FormAsync(name, folderKey);
        var forms = GetRequiredService<IFormBuilderFormService>();
        var draft = (await forms.GetDraftAsync(key))!;
        var page = draft.Definition!.Pages[0];
        var definition = draft.Definition with { Pages = [page with { Groups = [page.Groups[0] with { Fields = [NameField, Uses(list)] }] }] };
        var saved = (await forms.SaveDraftAsync(key, draft.Form.ConcurrencyVersion, definition, null, null))!;
        if (publish)
        {
            _ = await forms.PublishAsync(key, saved.Form.ConcurrencyVersion, null, null);
        }

        return key;
    }

    internal static FormField Uses(Guid list) => new()
    {
        Key = Guid.Parse("30000000-0000-4000-8000-0000000000b2"),
        Type = "dropdown",
        Alias = "topic",
        Label = new LocalizedText([new(string.Empty, "Topic")]),
        Options = new FieldOptions { Source = OptionSource.List, OptionList = list },
    };
}

/// <summary>
/// Fetch now on a site in Development with the Testing assembly's stub (ADR-0214): what this server keeps of a URL list's
/// address, and a fetch that drops it and asks the address again at once.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class OptionListFetchApiTests : ManagementApiTestBase
{
    private int _port;

    [Test]
    public async Task FetchNow_AsksTheAddressAgain_AndSaysWhenAndHowManyOptions_WhileAnotherKindHasNothingToFetch()
    {
        var stub = Services.GetRequiredService<StubHostedService>().Server!;
        stub.Answer(new StubAnswer("GET", "/options.json", Body: """{"items":[{"id":"ch","name":"Switzerland"},{"id":"li","name":"Liechtenstein"}]}"""));
        var lists = Services.GetRequiredService<OptionListService>();
        _ = await lists.CreateAsync(UrlOptionListHost.ListKey, UrlOptionListHost.List(_port, "/options.json"));
        var manual = (await lists.CreateAsync(Guid.NewGuid(), new OptionListInput("topics", "Topics", OptionListKind.Manual, Items: """[{"value":"a"}]"""))).List!.Key;

        var before = await CallAsync("designer", HttpMethod.Get, $"option-list/{UrlOptionListHost.ListKey}/fetched");
        _ = await Services.GetRequiredService<IFormBuilderOptionService>().GetListAsync(UrlOptionListHost.ListKey, new FormBuilderOptionContext("en"));
        var kept = await CallAsync("designer", HttpMethod.Get, $"option-list/{UrlOptionListHost.ListKey}/fetched");
        stub.Answer(new StubAnswer("GET", "/options.json", Body: """{"items":[{"id":"ch","name":"Switzerland"},{"id":"li","name":"Liechtenstein"},{"id":"at","name":"Austria"}]}"""));
        var fetched = await CallAsync("designer", HttpMethod.Post, $"option-list/{UrlOptionListHost.ListKey}/fetch");
        var resolved = await Services.GetRequiredService<IFormBuilderOptionService>().GetListAsync(UrlOptionListHost.ListKey, new FormBuilderOptionContext("en"));
        var nothing = await CallAsync("designer", HttpMethod.Post, $"option-list/{manual}/fetch");

        var audited = (await GetRequiredService<IAuditRepository>().QueryAsync(new AuditQuery { Action = OptionListLibrary.FetchedAction }, PageRequest.First(5))).Items;
        Assert.Multiple(() =>
        {
            Assert.That(before.Outcome, Is.EqualTo("200"), before.ToString());
            Assert.That(before.Body!["fetchedUtc"], Is.Null, "nothing asked for the options yet");
            Assert.That(kept.Body!["count"]!.GetValue<int>(), Is.EqualTo(2));
            Assert.That(kept.Body!["fetchedUtc"], Is.Not.Null);
            Assert.That(fetched.Outcome, Is.EqualTo("200"), fetched.ToString());
            Assert.That(fetched.Body!["count"]!.GetValue<int>(), Is.EqualTo(3), "fetched again at once, inside the list's 300 seconds");
            Assert.That(fetched.Body!["failing"]!.GetValue<bool>(), Is.False);
            Assert.That(stub.CallsTo("/options.json"), Has.Count.EqualTo(2));
            Assert.That(resolved.Options.Select(option => option.Value), Is.EqualTo(new[] { "ch", "li", "at" }), "forms get the new copy");
            Assert.That(nothing.Outcome, Is.EqualTo("404 option_list_not_found"), "a manual list fetches nothing");
            Assert.That(audited, Has.Count.EqualTo(1));
        });
    }

    protected override void ConfigureHost(IDictionary<string, string> configuration)
    {
        _port = UrlOptionListHost.FreePort();
        UrlOptionListHost.Configure(configuration, _port);
    }

    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        base.CustomTestSetup(builder);
        TestEndpointsHost.UseDevelopment(builder);
        builder.Services.AddUnique(new AppCaches(new ObjectCacheAppCache(), NoAppCache.Instance, new IsolatedCaches(_ => NoAppCache.Instance)));
    }
}
