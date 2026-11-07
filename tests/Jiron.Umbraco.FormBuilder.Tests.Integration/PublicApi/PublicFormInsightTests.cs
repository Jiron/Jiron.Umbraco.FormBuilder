using System.Net;
using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Insights;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Core.Services.Navigation;
using Umbraco.Cms.Core.Strings;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.PublicApi;

/// <summary>
/// Insights on a real site (ADR-0211): the events endpoint counts a page's beacons once they are written, by what the
/// published version has, and keeps nothing else a beacon holds; a form's markup says where to count only while its
/// insights and the site's are on; and a stored entry adds its page, its hour and its time to complete to the unit that
/// stores it, and fills its duration, while a form whose insights are off counts only its entries.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class PublicFormInsightTests : PublicApiTestBase
{
    private static readonly Guid First = Guid.Parse("30000000-0000-4000-8000-000000002111");
    private static readonly Guid Second = Guid.Parse("30000000-0000-4000-8000-000000002112");
    private static readonly FormField Email = new() { Key = Guid.Parse("30000000-0000-4000-8000-000000002113"), Type = "email", Alias = "email", Label = new LocalizedText("Email") };

    private IDailyStatRepository Stats => GetRequiredService<IDailyStatRepository>();

    private IFormRepository FormRows => GetRequiredService<IFormRepository>();

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    [Test]
    public async Task ThePagesBeacons_CountOnceWritten_ByWhatThePublishedVersionHas()
    {
        var key = await TwoPagesAsync();
        var events = $"{Root}/forms/{key}/events";

        HttpResponseMessage[] answers =
        [
            await BeaconAsync(events, """{"type":"view"}"""),
            await BeaconAsync(events, """{"type":"view"}"""),
            await BeaconAsync(events, """{"type":"start"}""", "application/json"),
            await BeaconAsync(events, $$"""{"type":"page","page":"{{Second}}"}"""),
            await BeaconAsync(events, """{"type":"invalid","field":"email","kind":"invalid"}"""),
            await BeaconAsync(events, $$"""{"type":"page","page":"{{Guid.NewGuid()}}"}"""),
            await BeaconAsync(events, """{"type":"invalid","field":"nobody","kind":"required"}"""),
            await BeaconAsync(events, """{"type":"invalid","field":"email","kind":"Mia"}"""),
            await BeaconAsync(events, "not json"),
            await BeaconAsync(events, string.Empty),
        ];
        var waiting = await StatsAsync(key);
        var written = await GetRequiredService<InsightWriter>().FlushAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(answers.Select(answer => answer.StatusCode), Is.All.EqualTo(HttpStatusCode.NoContent), "every beacon is answered alike");
            Assert.That(await answers[0].Content.ReadAsStringAsync(), Is.Empty);
            Assert.That(waiting, Is.Empty, "nothing is written per beacon");
            Assert.That(written, Is.EqualTo(4));
            Assert.That((await StatsAsync(key)).Select(stat => (stat.Metric, stat.Dimension, stat.Value)), Is.EqualTo(new[]
            {
                ("invalid", Email.Key.ToString("N") + ":invalid", 1L),
                ("page", Second.ToString("N"), 1L),
                ("starts", string.Empty, 1L),
                ("views", string.Empty, 2L),
            }));
        });

        foreach (var answer in answers)
        {
            answer.Dispose();
        }
    }

    [Test]
    public async Task APageRenderedForAPost_CountsNoViewOrStart_AndItsViewAfterANextAsThePageReached()
    {
        var key = await TwoPagesAsync();
        var events = $"{Root}/forms/{key}/events";
        HttpResponseMessage[] answers =
        [
            await BeaconAsync(events + "?continued=true", """{"type":"view"}"""),
            await BeaconAsync(events + "?continued=true", """{"type":"start"}"""),
            await BeaconAsync($"{events}?continued=true&reached={Second}", """{"type":"view"}"""),
            await BeaconAsync($"{events}?continued=true&reached={Second}", """{"type":"start"}"""),
            await BeaconAsync($"{events}?continued=true&reached={Guid.NewGuid()}", """{"type":"view"}"""),
            await BeaconAsync($"{events}?continued=true&reached=page-two", """{"type":"view"}"""),
            await BeaconAsync(events + "?continued=true", """{"type":"invalid","field":"email","kind":"invalid"}"""),
            await BeaconAsync(events, """{"type":"invalid","field":"email","kind":"server"}"""),
        ];
        await GetRequiredService<InsightWriter>().FlushAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(answers.Select(answer => answer.StatusCode), Is.All.EqualTo(HttpStatusCode.NoContent));
            Assert.That((await StatsAsync(key)).Select(stat => (stat.Metric, stat.Dimension, stat.Value)), Is.EqualTo(new[]
            {
                ("invalid", Email.Key.ToString("N") + ":invalid", 1L),
                ("page", Second.ToString("N"), 1L),
            }), "no view or start of the visit again, the page a Next by post reached, and no error kept from a refused post");
        });

        foreach (var answer in answers)
        {
            answer.Dispose();
        }
    }

    [Test]
    public async Task ABeacon_KeepsNothingButItsType_ItsPage_AndItsFieldAndKind()
    {
        var key = await TwoPagesAsync();
        const string Secret = "mia.muster@example.com";
        using var answer = await BeaconAsync($"{Root}/forms/{key}/events", $$$"""{"type":"invalid","field":"email","kind":"invalid","value":"{{{Secret}}}","email":"{{{Secret}}}","answers":{"email":["{{{Secret}}}"]}}""");
        await GetRequiredService<InsightWriter>().FlushAsync();
        var stats = await StatsAsync(key);

        Assert.Multiple(() =>
        {
            Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(stats.Select(stat => (stat.Metric, stat.Dimension)), Is.EqualTo(new[] { ("invalid", Email.Key.ToString("N") + ":invalid") }));
            Assert.That(stats.Any(stat => stat.Metric.Contains("mia", StringComparison.OrdinalIgnoreCase) || stat.Dimension.Contains("mia", StringComparison.OrdinalIgnoreCase)), Is.False, "the value posted along is nowhere");
        });
    }

    [Test]
    public async Task TheEndpoint_AnswersAFormThatIsNotServedWith404_AndABeaconPastItsLimitWith413()
    {
        var key = await TwoPagesAsync();
        using var unknown = await BeaconAsync($"{Root}/forms/{Guid.NewGuid()}/events", """{"type":"view"}""");
        using var notAKey = await BeaconAsync($"{Root}/forms/contact/events", """{"type":"view"}""");
        using var large = await BeaconAsync($"{Root}/forms/{key}/events", "{\"type\":\"view\",\"pad\":\"" + new string('x', FormInsights.MaxBeaconBytes) + "\"}");
        await GetRequiredService<InsightWriter>().FlushAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(await RefusalAsync(unknown), Is.EqualTo("404 form_not_found"));
            Assert.That(await RefusalAsync(notAKey), Is.EqualTo("404 form_not_found"));
            Assert.That(await RefusalAsync(large), Is.EqualTo("413 payload_too_large"));
            Assert.That(await StatsAsync(key), Is.Empty);
        });
    }

    [Test]
    public async Task TheMarkup_SaysWhereToCount_OnlyWhileTheFormsAndTheSitesInsightsAreOn()
    {
        var key = await TwoPagesAsync();
        var on = await MarkupAsync(key);
        var element = await ElementAsync(key);

        await SaveAndPublishAsync(key, definition => definition with { Settings = definition.Settings with { Privacy = definition.Settings.Privacy with { Insights = false } } });
        var formOff = await MarkupAsync(key);
        using var countedOff = await BeaconAsync($"{Root}/forms/{key}/events", """{"type":"view"}""");

        await SaveAndPublishAsync(key, definition => definition with { Settings = definition.Settings with { Privacy = definition.Settings.Privacy with { Insights = true } } });
        var settings = GetRequiredService<IFormBuilderSettingsService>();
        await settings.SaveAsync(new PrivacySettings { Insights = false }, (await settings.GetAsync<PrivacySettings>()).Version, Editor);
        var siteOff = await MarkupAsync(key);
        using var countedSiteOff = await BeaconAsync($"{Root}/forms/{key}/events", """{"type":"view"}""");
        await GetRequiredService<InsightWriter>().FlushAsync();

        Assert.Multiple(async () =>
        {
            Assert.That(on, Does.Contain($"data-jfb-insights=\"{Root}/forms/{key:D}/events\""));
            Assert.That(element, Does.Contain("\"insights\":true"), "the element writes its own address from its API's");
            Assert.That(element, Does.Not.Contain("insightsUrl"));
            Assert.That(formOff, Does.Not.Contain("data-jfb-insights"), "the form's own are off");
            Assert.That(siteOff, Does.Not.Contain("data-jfb-insights"), "the site's are off");
            Assert.That(countedOff.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(countedSiteOff.StatusCode, Is.EqualTo(HttpStatusCode.NoContent));
            Assert.That(await StatsAsync(key), Is.Empty, "a beacon of a form whose insights are off counts nothing");
        });
    }

    [Test]
    public async Task AStoredEntry_AddsItsPageItsHourAndItsTimeToComplete_InTheUnitThatStoresIt()
    {
        var content = await PageAsync();
        var key = await PublishAsync(fields: [NameField]);
        var settings = GetRequiredService<IFormBuilderSettingsService>();
        await settings.SaveSiteAsync(content, new GeneralSettings { TimeZone = "Pacific/Pago_Pago" }, ["timeZone"], 0, Editor);
        var shared = await settings.GetAsync<GeneralSettings>();
        await settings.SaveAsync(shared.Value with { TimeZone = "Pacific/Kiritimati" }, shared.Version, Editor);
        using var sent = await SendAsync(HttpMethod.Post, $"{Root}/forms/{key}/entries", request => request.Content = new StringContent(
            $$$"""{"revision":1,"contentKey":"{{{content}}}","values":{"name":"Ada"}}""", Encoding.UTF8, "application/json"));
        var form = (await FormRows.GetAsync(key))!;
        var entry = (await GetRequiredService<IEntryRepository>().GetByIdAsync((await GetRequiredService<IEntryRepository>().ListIdsAsync(form.Id, 1)).Single()))!;
        var stats = await StatsAsync(key);

        Assert.Multiple(async () =>
        {
            Assert.That(sent.StatusCode, Is.EqualTo(HttpStatusCode.OK), await sent.Content.ReadAsStringAsync());
            Assert.That(entry.DurationSeconds, Is.Not.Null.And.GreaterThanOrEqualTo(0), "from the challenge's first issue");
            Assert.That(stats.Select(stat => stat.Metric), Is.EquivalentTo(new[] { "completion", "hour", "origin", "submissions" }));
            Assert.That(stats.Single(stat => stat.Metric == "completion").Dimension, Is.EqualTo("15"));
            Assert.That(stats.Single(stat => stat.Metric == "origin").Dimension, Is.EqualTo(content.ToString("N")));
            Assert.That(
                stats.Single(stat => stat.Metric == "hour").Dimension,
                Is.EqualTo(TimeZoneInfo.ConvertTime(entry.SubmittedUtc, TimeZoneInfo.FindSystemTimeZoneById("Pacific/Kiritimati")).Hour.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                "by the clock every site shares, which the report names, not the site's own (a day apart)");
        });
    }

    [Test]
    public async Task ASpamEntry_CountsOnlySpam_NotItsPageItsHourOrItsTimeToComplete()
    {
        var content = await PageAsync();
        var key = await PublishAsync(fields: [NameField]);
        using var sent = await SendAsync(HttpMethod.Post, $"{Root}/forms/{key}/entries", request => request.Content = new StringContent(
            $$$"""{"revision":1,"contentKey":"{{{content}}}","values":{"name":"Ada"},"honeypot":"Buy now"}""", Encoding.UTF8, "application/json"));
        var stats = await StatsAsync(key);

        Assert.Multiple(async () =>
        {
            Assert.That(sent.IsSuccessStatusCode, Is.True, await sent.Content.ReadAsStringAsync());
            Assert.That(stats.Select(stat => (stat.Metric, stat.Value)), Is.EqualTo(new[] { ("spam", 1L) }), "its page, hour and time are those of an entry nobody counts");
        });
    }

    [Test]
    public async Task AUnitThatFailsAfterTheEntrysCounts_KeepsNoneOfThem_NorTheEntry()
    {
        var key = await PublishAsync(fields: [NameField]);
        var hooks = Intercepted<IDailyStatRepository>.Of(Stats);
        hooks.On(nameof(IDailyStatRepository.IncrementAsync), (_, call) => FailAfterAsync(call));
        static async Task<bool> FailAfterAsync(Func<object?> call)
        {
            await (Task<bool>)call()!;
            throw new InvalidOperationException("JFB_TEST: the unit fails after the counts");
        }

        HttpResponseMessage sent;
        try
        {
            sent = await SendAsync(HttpMethod.Post, $"{Root}/forms/{key}/entries", request => request.Content = new StringContent(
                """{"revision":1,"values":{"name":"Ada"}}""", Encoding.UTF8, "application/json"));
        }
        finally
        {
            hooks.Off(nameof(IDailyStatRepository.IncrementAsync));
        }

        using (sent)
        {
            var form = (await FormRows.GetAsync(key))!;
            Assert.Multiple(async () =>
            {
                Assert.That(await RefusalAsync(sent), Is.EqualTo("500 server_error"));
                Assert.That(await StatsAsync(key), Is.Empty, "the counts went with the unit");
                Assert.That(await GetRequiredService<IEntryRepository>().ListIdsAsync(form.Id, 10), Is.Empty);
            });
        }
    }

    [Test]
    public async Task AFormWhoseInsightsAreOff_CountsOnlyItsEntries()
    {
        var key = await PublishAsync(change: definition => definition with { Settings = definition.Settings with { Privacy = definition.Settings.Privacy with { Insights = false } } }, fields: [NameField]);
        using var sent = await SendAsync(HttpMethod.Post, $"{Root}/forms/{key}/entries", request => request.Content = new StringContent(
            $$$"""{"revision":1,"contentKey":"{{{Guid.NewGuid()}}}","values":{"name":"Ada"}}""", Encoding.UTF8, "application/json"));

        Assert.Multiple(async () =>
        {
            Assert.That(sent.StatusCode, Is.EqualTo(HttpStatusCode.OK), await sent.Content.ReadAsStringAsync());
            Assert.That((await StatsAsync(key)).Select(stat => (stat.Metric, stat.Value)), Is.EqualTo(new[] { ("submissions", 1L) }));
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);

        // The tests write the beacons themselves, so no period of the flusher writes them in between.
        builder.Services.RemoveAll(descriptor => descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationType == typeof(InsightFlusher));
        builder.Services.AddUnique(provider => Intercepted<IDailyStatRepository>.Over(ActivatorUtilities.CreateInstance<DailyStatRepository>(provider)));
    }

    private Task<Guid> TwoPagesAsync() => PublishAsync(change: definition => definition with
    {
        Pages =
        [
            definition.Pages[0] with { Key = First, Groups = [definition.Pages[0].Groups[0] with { Fields = [NameField] }] },
            new FormPage { Key = Second, Groups = [new FormGroup { Key = Guid.NewGuid(), Fields = [Email] }] },
        ],
    });

    // A published page "Contact" at the root.
    private async Task<Guid> PageAsync()
    {
        var types = GetRequiredService<IContentTypeService>();
        var type = new ContentType(GetRequiredService<IShortStringHelper>(), -1) { Alias = "jfbPage", Name = "Page", AllowedAsRoot = true };
        Assert.That((await types.CreateAsync(type, Constants.Security.SuperUserKey)).Success, Is.True);
        var contents = GetRequiredService<IContentService>();
        var page = new Content("Contact", -1, type);
        contents.Save(page);
        contents.Publish(page, ["*"]);
        await GetRequiredService<IDocumentNavigationManagementService>().RebuildAsync();
        await GetRequiredService<IPublishStatusManagementService>().InitializeAsync(CancellationToken.None);
        return page.Key;
    }

    // A beacon as navigator.sendBeacon sends a string: text/plain, with no challenge.
    private Task<HttpResponseMessage> BeaconAsync(string path, string body, string mediaType = "text/plain") =>
        Client.PostAsync(path, new StringContent(body, Encoding.UTF8, mediaType));

    private async Task<IReadOnlyList<DailyStat>> StatsAsync(Guid key) =>
        await Stats.ListAsync((await FormRows.GetAsync(key))!.Id, Today.AddDays(-1), Today.AddDays(1));

    private async Task<string> MarkupAsync(Guid key)
    {
        using var answer = await Client.GetAsync($"{Root}/forms/{key}/render?instance=jfb-1&page=/contact/");
        Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.OK), await answer.Content.ReadAsStringAsync());
        return await answer.Content.ReadAsStringAsync();
    }

    private async Task<string> ElementAsync(Guid key)
    {
        using var answer = await Client.GetAsync($"{Root}/forms/{key}/element?instance=jfb-1&page=/contact/");
        Assert.That(answer.StatusCode, Is.EqualTo(HttpStatusCode.OK), await answer.Content.ReadAsStringAsync());
        return await answer.Content.ReadAsStringAsync();
    }
}
