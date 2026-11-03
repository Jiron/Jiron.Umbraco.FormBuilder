using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Testing;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>
/// Where the inspector's option editor takes options from (ADR-0172): the kinds a field may configure with their
/// configurations' schemas and the site's code sources, held to the committed snapshot <c>docs/api/option-sources.json</c>
/// (after a deliberate change, run the fixture with <c>JFB_UPDATE_SNAPSHOTS=1</c>: it writes the snapshot and fails, so
/// the diff gets reviewed); the library's lists for the picker; and a draft's options resolved for the canvas.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class OptionSourceApiTests : ManagementApiTestBase
{
    private static readonly string SnapshotPath = RepositoryRoot.Path(typeof(OptionSourceApiTests).Assembly, "docs", "api", "option-sources.json");

    [Test]
    public async Task TheKindsAFieldMayConfigure_AndTheCodeSources_AreServedToAUserOfTheSectionWithoutGrants_AsTheReviewedSnapshotHolds()
    {
        var answer = await CallAsync("noAccess", HttpMethod.Get, "option-source");
        Assert.That(answer.Outcome, Is.EqualTo("200"), answer.ToString());
        var body = answer.Body!.AsObject();
        Assert.Multiple(() =>
        {
            Assert.That(body["kinds"]!.AsArray().Select(kind => kind!["kind"]!.GetValue<string>()), Is.EqualTo(new[] { "content", "dataType", "url", "countries", "code" }), "no manual list or upload on a field");
            Assert.That(body["sources"]!.AsArray().Select(source => source!["name"]!.GetValue<string>()), Is.EqualTo(new[] { "months", "weekdays", "agreement", "satisfaction", "yesNoNotSure" }), "the standard lists, in the registry's order");
        });

        var served = body.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }).ReplaceLineEndings("\n") + "\n";
        if (Environment.GetEnvironmentVariable("JFB_UPDATE_SNAPSHOTS") == "1")
        {
            await File.WriteAllTextAsync(SnapshotPath, served, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Assert.Fail($"JFB_UPDATE_SNAPSHOTS=1: wrote {SnapshotPath}. Review the diff, commit it, and run again without the variable.");
        }

        Assert.That(File.Exists(SnapshotPath), Is.True, $"the reviewed snapshot {SnapshotPath}; run with JFB_UPDATE_SNAPSHOTS=1");
        Assert.That((await File.ReadAllTextAsync(SnapshotPath)).ReplaceLineEndings("\n"), Is.EqualTo(served), "the kinds or the sources changed: run with JFB_UPDATE_SNAPSHOTS=1 and review the diff");
    }

    [Test]
    public async Task TheLibrarysLists_AreListedByName_WithTheirKinds_AndHowManyOptionsAListThatHoldsThemHas()
    {
        var lists = GetRequiredService<OptionListService>();
        var sizes = await lists.CreateAsync(Guid.NewGuid(), new OptionListInput("sizes", "Sizes", OptionListKind.Manual, Items: """[{"value":"s"},{"value":"m"},{"value":"l"}]"""));
        var months = await lists.CreateAsync(Guid.NewGuid(), new OptionListInput("months", "Months of the year", OptionListKind.Code, Config: """{"source":"months"}"""));
        Assert.That((sizes.Succeeded, months.Succeeded), Is.EqualTo((true, true)));

        var answer = await CallAsync("noAccess", HttpMethod.Get, "option-list");

        Assert.That(answer.Outcome, Is.EqualTo("200"), answer.ToString());
        Assert.That(
            answer.Body!["items"]!.AsArray().Select(item => (item!["name"]!.GetValue<string>(), item["alias"]!.GetValue<string>(), item["kind"]!.GetValue<string>(), item["count"]?.GetValue<int>())),
            Is.EqualTo(new (string, string, string, int?)[] { ("Months of the year", "months", "code", null), ("Sizes", "sizes", "manual", 3) }),
            "by name; a list that resolves its options says no count, as nothing is resolved");
    }

    [Test]
    public async Task ADraftsOptions_AreResolvedInTheCultureAsked_FromItsItems_AList_ACodeSource_OrCountries()
    {
        var form = (await GetRequiredService<IFormBuilderFormService>().CreateAsync(new NewFormRequest("Spring"))).Key;
        var list = await GetRequiredService<OptionListService>().CreateAsync(Guid.NewGuid(), new OptionListInput("rooms", "Rooms", OptionListKind.Manual, Items: """[{"value":"hall","label":{"":"Hall","de-DE":"Saal"}}]"""));
        async Task<JsonNode> ResolveAsync(object options, string culture = "en-US")
        {
            var answer = await CallAsync("designer", HttpMethod.Post, $"form/{form}/options", new { field = Guid.NewGuid(), type = "dropdown", options, culture });
            Assert.That(answer.Outcome, Is.EqualTo("200"), answer.ToString());
            return answer.Body!;
        }

        static IEnumerable<string> Labels(JsonNode body) => body["options"]!.AsArray().Select(option => option!["label"]?.GetValue<string>() ?? option["value"]!.GetValue<string>());

        var parent = Guid.NewGuid();
        var own = await ResolveAsync(new
        {
            source = "items",
            parent,
            items = new object[]
            {
                new { value = "a", label = new Dictionary<string, string> { [string.Empty] = "Apple" }, parentValues = new[] { "fruit" } },
                new { value = "b", label = new Dictionary<string, string> { [string.Empty] = "Bean" }, parentValues = new[] { "vegetable" } },
            },
        });
        var fromList = await ResolveAsync(new { source = "list", list = list.List!.Key }, "de-DE");
        var months = await ResolveAsync(new { source = "code", config = new { source = "months" } }, "de-DE");
        var countries = await ResolveAsync(new { source = "countries", config = new { countries = new[] { "DE", "CH" } } });
        var unknown = await ResolveAsync(new { source = "code", config = new { source = "planets" } });
        var halfTyped = await ResolveAsync(new { source = "countries", config = new { countries = new[] { "DE", "D" } } });
        var nonsense = await ResolveAsync(new { source = "items", items = new[] { new { value = "x" } } }, "<b>");
        var unreadable = await CallAsync("designer", HttpMethod.Post, $"form/{form}/options", new { field = Guid.NewGuid(), type = "dropdown", options = new { source = "everywhere" }, culture = "en-US" });

        Assert.Multiple(() =>
        {
            Assert.That(Labels(own), Is.EqualTo(new[] { "Apple", "Bean" }), "a cascading field shows every option on the canvas");
            Assert.That(own["options"]![0]!["parentValues"]!.AsArray().Select(value => value!.GetValue<string>()), Is.EqualTo(new[] { "fruit" }));
            Assert.That((Labels(fromList).Single(), fromList["isAvailable"]!.GetValue<bool>()), Is.EqualTo(("Saal", true)), "a library list, in German");
            Assert.That(Labels(months).First(), Is.EqualTo("Januar"), "a code source, in German");
            Assert.That(Labels(countries), Is.EqualTo(new[] { "Germany", "Switzerland" }), "the countries named, in the culture's order of their names");
            Assert.That((Labels(unknown).Count(), unknown["isAvailable"]!.GetValue<bool>()), Is.EqualTo((0, false)), "a code source the site lacks is unavailable");
            Assert.That((Labels(halfTyped).Count(), halfTyped["isAvailable"]!.GetValue<bool>()), Is.EqualTo((0, false)), "a configuration its kind refuses, half typed, is unavailable");
            Assert.That(Labels(nonsense), Is.EqualTo(new[] { "x" }), "a culture that is no culture's name asks for the default texts");
            Assert.That(unreadable.Outcome, Is.EqualTo("400 invalid_request"), unreadable.ToString());
        });
    }

    [Test]
    public async Task ADraftsOptions_AreResolvedOnlyForAUserWhoMayDesignTheForm()
    {
        var form = (await GetRequiredService<IFormBuilderFormService>().CreateAsync(new NewFormRequest("Spring"))).Key;
        var request = new { field = Guid.NewGuid(), type = "dropdown", options = new { source = "countries" }, culture = "en-US" };

        var viewer = await CallAsync("viewer", HttpMethod.Post, $"form/{form}/options", request);
        var designer = await CallAsync("designer", HttpMethod.Post, $"form/{form}/options", request);

        Assert.That((viewer.Outcome, designer.Outcome), Is.EqualTo(("403 forbidden", "200")), viewer.ToString());
    }
}
