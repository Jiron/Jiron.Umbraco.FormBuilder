using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>
/// Insights through the management API (ROADMAP 11.4, ADR-0211): the site's switch on Privacy, Data collection, which
/// needs <c>manageSettings</c>; a form's report over a span, compared with another, by day, week or month, which needs
/// <c>viewEntries</c> on the form and refuses a span it cannot read; the site's table of the forms the user may view
/// entries of; and the boxes a user shows as tables, kept for them.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class InsightApiTests : ManagementApiTestBase
{
    private const string Admin = "admin";

    private static readonly Guid Second = Guid.Parse("30000000-0000-4000-8000-0000000021a1");

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private IDailyStatRepository Stats => GetRequiredService<IDailyStatRepository>();

    [Test]
    public async Task ThePrivacySettings_SayWhetherTheFormsCount_AndASaveChangesIt()
    {
        var read = await Api.CallAsync(Admin, HttpMethod.Get, "settings/privacy");
        var saved = await Api.CallAsync(Admin, HttpMethod.Put, "settings/privacy", new { version = read.Body!["version"]!.GetValue<int>(), insights = false });
        var kept = await Api.CallAsync(Admin, HttpMethod.Put, "settings/privacy", new { version = saved.Body!["version"]!.GetValue<int>() });
        var designer = await Api.CallAsync("designer", HttpMethod.Get, "settings/privacy");
        var designerSave = await Api.CallAsync("designer", HttpMethod.Put, "settings/privacy", new { version = kept.Body!["version"]!.GetValue<int>(), insights = true });

        Assert.Multiple(async () =>
        {
            Assert.That(read.Outcome, Is.EqualTo("200"), read.ToString());
            Assert.That(read.Body["insights"]!.GetValue<bool>(), Is.True, "on by default");
            Assert.That(read.Body["locks"]!.AsArray(), Is.Empty);
            Assert.That(saved.Outcome, Is.EqualTo("200"), saved.ToString());
            Assert.That(saved.Body["insights"]!.GetValue<bool>(), Is.False);
            Assert.That(kept.Body["insights"]!.GetValue<bool>(), Is.False, "a save without it leaves it as it was");
            Assert.That((designer.Outcome, designerSave.Outcome), Is.EqualTo(("403 forbidden", "403 forbidden")));
            Assert.That((await GetRequiredService<IFormBuilderSettingsService>().GetAsync<PrivacySettings>()).Value.Insights, Is.False);
        });
    }

    [Test]
    public async Task AFormsReport_AddsUpItsSpan_ComparesItWithAnother_AndNamesItsPagesAndFields()
    {
        var key = await PublishedAsync("Insights");
        var id = (await GetRequiredService<IFormRepository>().GetAsync(key))!.Id;
        var name = NameField.Key.ToString("N");
        await Stats.IncrementOnAsync(id, Today, [new("views", By: 50), new("starts", By: 20), new("submissions", By: 5), new("spam", By: 1), new("page", Second.ToString("N"), 8), new("invalid", name + ":required", 4), new("hour", "10", 5), new("completion", "60", 5)]);
        await Stats.IncrementOnAsync(id, Today.AddDays(-1), [new("views", By: 30), new("submissions", By: 1)]);
        await Stats.IncrementOnAsync(id, Today.AddDays(-3), [new("views", By: 40), new("submissions", By: 2)]);

        var report = await Api.CallAsync("viewer", HttpMethod.Get, $"form/{key}/insights?from={Day(-1)}&to={Day(0)}&compareFrom={Day(-3)}&compareTo={Day(-2)}");
        var body = report.Body!;

        Assert.Multiple(() =>
        {
            Assert.That(report.Outcome, Is.EqualTo("200"), report.ToString());
            Assert.That((body["siteOn"]!.GetValue<bool>(), body["formOn"]!.GetValue<bool>()), Is.EqualTo((true, true)));
            Assert.That(JsonNode.DeepEquals(body["totals"], JsonNode.Parse("""{"views":80,"starts":20,"entries":6,"spam":1,"conversion":0.075}""")), Is.True, body["totals"]!.ToJsonString());
            Assert.That(JsonNode.DeepEquals(body["before"], JsonNode.Parse("""{"views":40,"starts":0,"entries":2,"spam":0,"conversion":0.05}""")), Is.True, body["before"]!.ToJsonString());
            Assert.That(body["periods"]!.ToJsonString(), Is.EqualTo($$"""[{"start":"{{Day(-1)}}","entries":1,"before":2},{"start":"{{Day(0)}}","entries":5,"before":0}]"""));
            Assert.That(body["pages"]!.ToJsonString(), Is.EqualTo($$"""[{"key":"{{Second}}","name":"Details","reached":8}]"""));
            Assert.That(body["medianCompletion"]!.GetValue<string>(), Is.EqualTo("60"));
            Assert.That(body["fields"]!.ToJsonString(), Is.EqualTo($$"""[{"key":"{{NameField.Key}}","label":"Name","stops":4,"kind":"required"}]"""));
            Assert.That(body["origins"]!.AsArray(), Is.Empty);
            Assert.That(body["hours"]!.AsArray().Select(hour => hour!.GetValue<long>()), Is.EqualTo(Enumerable.Range(0, 24).Select(hour => hour == 10 ? 5L : 0L)));
            Assert.That((body["members"]!.GetValue<long>(), body["memberEntries"]!.GetValue<long>(), body["membersBefore"]!.GetValue<long>()), Is.EqualTo((0L, 0L, 0L)));
            Assert.That(body["timeZone"]!.GetValue<string>(), Is.EqualTo(TimeZoneInfo.Utc.Id).Or.EqualTo("UTC").Or.EqualTo("Etc/UTC"), "the site's, in which the hours are counted");
            Assert.That(body["tables"]!.AsArray(), Is.Empty);
        });
    }

    [Test]
    public async Task AReport_NeedsTheRightToViewTheFormsEntries_AndASpanItCanRead()
    {
        var key = await PublishedAsync("Guarded");
        var span = $"from={Day(-6)}&to={Day(0)}";
        var designer = await Api.CallAsync("designer", HttpMethod.Get, $"form/{key}/insights?{span}");
        var missing = await Api.CallAsync(Admin, HttpMethod.Get, $"form/{Guid.NewGuid()}/insights?{span}");
        var noDays = await Api.CallAsync(Admin, HttpMethod.Get, $"form/{key}/insights");
        var backwards = await Api.CallAsync(Admin, HttpMethod.Get, $"form/{key}/insights?from={Day(0)}&to={Day(-1)}");
        var tooLong = await Api.CallAsync(Admin, HttpMethod.Get, $"form/{key}/insights?from={Day(-366)}&to={Day(0)}");
        var yearly = await Api.CallAsync(Admin, HttpMethod.Get, $"form/{key}/insights?{span}&groupBy=year");
        var halfCompare = await Api.CallAsync(Admin, HttpMethod.Get, $"form/{key}/insights?{span}&compareFrom={Day(-13)}");
        var weekly = await Api.CallAsync(Admin, HttpMethod.Get, $"form/{key}/insights?from={Day(-30)}&to={Day(0)}&groupBy=week");

        Assert.Multiple(() =>
        {
            Assert.That(designer.Outcome, Is.EqualTo("403 forbidden"), designer.ToString());
            Assert.That(missing.Outcome, Is.EqualTo("404 form_not_found"), missing.ToString());
            Assert.That(noDays.Body!["periods"]!.AsArray().Select(period => period!["start"]!.GetValue<string>()), Is.EqualTo(Enumerable.Range(-29, 30).Select(Day)), "without days, the last 30 to today");
            Assert.That(Paths(backwards), Is.EqualTo(new[] { "to" }), backwards.ToString());
            Assert.That(Paths(tooLong), Is.EqualTo(new[] { "to" }), tooLong.ToString());
            Assert.That(Paths(yearly), Is.EqualTo(new[] { "groupBy" }), yearly.ToString());
            Assert.That(Paths(halfCompare), Is.EqualTo(new[] { "compareTo" }), halfCompare.ToString());
            Assert.That(weekly.Outcome, Is.EqualTo("200"), weekly.ToString());
            Assert.That(weekly.Body!["periods"]!.AsArray().Select(period => DateOnly.Parse(period!["start"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture).DayOfWeek), Is.All.EqualTo(DayOfWeek.Monday));
        });
    }

    [Test]
    public async Task TheSitesTable_ListsTheFormsTheUserMayViewEntriesOf_WithTheMostEntriesFirst()
    {
        var quiet = await PublishedAsync("Quiet");
        var busy = await PublishedAsync("Busy");
        _ = await PublishedAsync("Untouched");
        var forms = GetRequiredService<IFormRepository>();
        await Stats.IncrementOnAsync((await forms.GetAsync(quiet))!.Id, Today, [new("views", By: 10), new("submissions", By: 1)]);
        await Stats.IncrementOnAsync((await forms.GetAsync(busy))!.Id, Today, [new("views", By: 10), new("starts", By: 6), new("submissions", By: 4)]);
        await Stats.IncrementOnAsync((await forms.GetAsync(busy))!.Id, Today.AddDays(-10), [new("submissions", By: 2)]);
        var path = $"insights/glance?from={Day(-6)}&to={Day(0)}&compareFrom={Day(-13)}&compareTo={Day(-7)}";

        var viewer = await Api.CallAsync("viewer", HttpMethod.Get, path);
        var designer = await Api.CallAsync("designer", HttpMethod.Get, path);
        var noCompare = await Api.CallAsync("viewer", HttpMethod.Get, $"insights/glance?from={Day(-6)}&to={Day(0)}");

        Assert.Multiple(() =>
        {
            Assert.That(viewer.Outcome, Is.EqualTo("200"), viewer.ToString());
            Assert.That(viewer.Body!["items"]!.AsArray().Select(item => item!["name"]!.GetValue<string>()), Is.EqualTo(new[] { "Busy", "Quiet" }), "a form with nothing in either span is left out");
            Assert.That(JsonNode.DeepEquals(viewer.Body["items"]![0]!["totals"], JsonNode.Parse("""{"views":10,"starts":6,"entries":4,"spam":0,"conversion":0.4}""")), Is.True, viewer.Text);
            Assert.That(viewer.Body["items"]![0]!["before"]!["entries"]!.GetValue<long>(), Is.EqualTo(2));
            Assert.That(designer.Body!["items"]!.AsArray(), Is.Empty, "a user who may view no form's entries");
            Assert.That(noCompare.Outcome, Is.EqualTo("200"), noCompare.ToString());
            Assert.That(noCompare.Body!["items"]![0]!["before"]!["entries"]!.GetValue<long>(), Is.EqualTo(2), "without a span to compare with, as many days just before");
        });
    }

    [Test]
    public async Task TheBoxesShownAsTables_AreKeptForTheUser_AndAnUnknownBoxIsRefused()
    {
        var key = await PublishedAsync("Tables");
        var saved = await Api.CallAsync("viewer", HttpMethod.Put, "insights/tables", new { tables = new[] { "hours", "fields", "hours" } });
        var report = await Api.CallAsync("viewer", HttpMethod.Get, $"form/{key}/insights?from={Day(0)}&to={Day(0)}");
        var other = await Api.CallAsync(Admin, HttpMethod.Get, $"form/{key}/insights?from={Day(0)}&to={Day(0)}");
        var unknown = await Api.CallAsync("viewer", HttpMethod.Put, "insights/tables", new { tables = new[] { "mood" } });
        var cleared = await Api.CallAsync("viewer", HttpMethod.Put, "insights/tables", new { tables = Array.Empty<string>() });
        var after = await Api.CallAsync("viewer", HttpMethod.Get, $"form/{key}/insights?from={Day(0)}&to={Day(0)}");

        Assert.Multiple(() =>
        {
            Assert.That(saved.Outcome, Is.EqualTo("200"), saved.ToString());
            Assert.That(saved.Body!["tables"]!.AsArray().Select(box => box!.GetValue<string>()), Is.EqualTo(new[] { "fields", "hours" }), "each once, in order");
            Assert.That(report.Body!["tables"]!.AsArray().Select(box => box!.GetValue<string>()), Is.EqualTo(new[] { "fields", "hours" }));
            Assert.That(other.Body!["tables"]!.AsArray(), Is.Empty, "another user's choice is theirs");
            Assert.That(Paths(unknown), Is.EqualTo(new[] { "tables" }), unknown.ToString());
            Assert.That(cleared.Outcome, Is.EqualTo("200"));
            Assert.That(after.Body!["tables"]!.AsArray(), Is.Empty);
        });
    }

    private static string Day(int offset) => Today.AddDays(offset).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    private static string[] Paths(ApiAnswer answer)
    {
        Assert.That(answer.Status, Is.EqualTo(System.Net.HttpStatusCode.BadRequest), answer.ToString());
        return [.. answer.Body!["errors"]!.AsArray().Select(error => error!["path"]!.GetValue<string>())];
    }

    // A published form of two pages, the second titled "Details".
    private async Task<Guid> PublishedAsync(string name)
    {
        var key = await FormAsync(name);
        var forms = GetRequiredService<IFormBuilderFormService>();
        var draft = (await forms.GetDraftAsync(key))!;
        var definition = Fillable(draft.Definition!);
        definition = definition with { Pages = [definition.Pages[0], new FormPage { Key = Second, Title = new LocalizedText("Details"), Groups = [new FormGroup { Key = Guid.NewGuid(), Fields = [new FormField { Key = Guid.NewGuid(), Type = "text", Alias = "phone", Label = new LocalizedText("Phone") }] }] }] };
        var saved = (await forms.SaveDraftAsync(key, draft.Form.ConcurrencyVersion, definition, null, null))!;
        _ = (await forms.PublishAsync(key, saved.Form.ConcurrencyVersion, null, null))!;
        return key;
    }
}
