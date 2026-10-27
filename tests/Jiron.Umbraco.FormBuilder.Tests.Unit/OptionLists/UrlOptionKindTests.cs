using System.Net;
using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Core.Outbound;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Jiron.Umbraco.FormBuilder.Tests.Unit.Outbound;
using Microsoft.Extensions.Logging;
using static Jiron.Umbraco.FormBuilder.Tests.Unit.OptionLists.OptionListTesting;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.OptionLists;

/// <summary>
/// URL lists (ADR-0076): the options at the list path of a JSON answer, each value and label at their paths, at the
/// edges of what an answer holds; the configuration's checks and the site's hosts; the keep time per configuration,
/// with one fetch at a time; and the last copy in use while the address fails, logged without the address.
/// </summary>
[TestFixture]
public sealed class UrlOptionKindTests
{
    private const string Address = "https://options.example.com/v1/list.json";

    private static readonly Guid ListKey = Guid.Parse("73000000-0000-4000-8000-000000000040");

    private static readonly FormBuilderOptionContext English = new("en");

    [Test]
    public async Task TheOptions_AreTheListAtItsPath_EachValueAndLabelAtTheirs()
    {
        var transport = new ScriptedTransport(_ => ScriptedTransport.Json("""
            {"data":{"countries":[
              {"code":"ch","name":{"en":"Switzerland"}},
              {"code":"de","name":{"en":"Germany"}},
              {"code":"fr"}
            ]},"next":null}
            """));
        var site = Site(transport);

        var resolved = await site.Options().GetAsync(Field(OptionSource.Url, Config(listPath: "$.data.countries", valueFrom: "$.code", labelFrom: "$.name.en")), English);

        Assert.Multiple(() =>
        {
            Assert.That(resolved.IsAvailable, Is.True);
            Assert.That(Pairs(resolved), Is.EqualTo(new[] { ("ch", "Switzerland"), ("de", "Germany"), ("fr", (string?)null) }), "the value shows without a label");
            Assert.That(transport.Requests.Select(request => (request.Method, request.Address.AbsoluteUri, request.Accept)), Is.EqualTo(new[] { (HttpMethod.Get, Address, "application/json") }));
        });
    }

    [Test]
    public async Task TheDefaults_ReadAListOfValuesAndLabels()
    {
        var site = Site(new ScriptedTransport(_ => ScriptedTransport.Json("""[{"value":"s","label":"Small"},{"value":"m","label":"Medium","extra":true}]""")));

        var resolved = await site.Options().GetAsync(Field(OptionSource.Url, Config()), English);

        Assert.That(Pairs(resolved), Is.EqualTo(new[] { ("s", "Small"), ("m", "Medium") }));
    }

    [Test]
    public async Task QuotedNamesAndIndexes_LeadToTheirParts()
    {
        var site = Site(new ScriptedTransport(_ => ScriptedTransport.Json("""{"the list":{"it's":[["a","Apple"],["b","Banana"],["c"]]}}""")));

        var resolved = await site.Options().GetAsync(Field(OptionSource.Url, Config(listPath: @"$['the list']['it\'s']", valueFrom: "$[0]", labelFrom: "$[1]")), English);

        Assert.That(Pairs(resolved), Is.EqualTo(new[] { ("a", "Apple"), ("b", "Banana"), ("c", (string?)null) }), "an index past the end is no label");
    }

    [Test]
    public async Task NumbersAndBooleans_ReadAsTheAnswerWritesThem_AndAnythingElseIsLeftOut()
    {
        var site = Site(new ScriptedTransport(_ => ScriptedTransport.Json("""
            [
              {"value":1},{"value":2.50},{"value":-1e3},{"value":true},{"value":false},
              {"value":null},{"value":{}},{"value":[]},{},"text",
              {"value":"x","label":7},{"value":"y","label":{"en":"Y"}},{"value":" z "}
            ]
            """)));

        var resolved = await site.Options().GetAsync(Field(OptionSource.Url, Config()), English);

        Assert.Multiple(() =>
        {
            Assert.That(Pairs(resolved), Is.EqualTo(new[] { ("1", null), ("2.50", null), ("-1e3", null), ("true", null), ("false", null), ("x", "7"), ("y", (string?)null) }));
            Assert.That(site.Logs.Entries.Select(entry => entry.Message), Is.EqualTo(new[] { $"The field {FieldKey} left out 6 options: blank, too long, not clean or repeated values, or more than 10,000" }), "null, an object, a list, a missing value, an element that is no object and a value with spaces around it");
        });
    }

    [Test]
    public async Task AnAnswerOfMoreThan10000Options_OffersTheFirst10000()
    {
        var answer = "[" + string.Join(',', Enumerable.Range(1, 10_005).Select(i => $"{{\"value\":\"v{i}\"}}")) + "]";
        var site = Site(new ScriptedTransport(_ => ScriptedTransport.Json(answer)));

        var resolved = await site.Options().GetAsync(Field(OptionSource.Url, Config()), English);

        Assert.Multiple(() =>
        {
            Assert.That((resolved.Options.Count, resolved.Options[^1].Value), Is.EqualTo((OptionListService.MaxOptions, "v10000")));
            Assert.That(site.Logs.Entries.Single().Message, Does.Contain("left out 1 options"), "the first one more is counted");
        });
    }

    [Test]
    public async Task AByteOrderMark_BeforeTheJson_IsSkipped()
    {
        var site = Site(new ScriptedTransport(_ => ScriptedTransport.Bytes([0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("""[{"value":"a"}]""")])));

        var resolved = await site.Options().GetAsync(Field(OptionSource.Url, Config()), English);

        Assert.That(Values(resolved), Is.EqualTo(new[] { "a" }));
    }

    [TestCase("not json", Description = "no JSON")]
    [TestCase("""{"value":"a"}""", Description = "no list at the path")]
    [TestCase("""{"items":[]}""", Description = "the list is elsewhere")]
    [TestCase("[1,2", Description = "JSON cut short")]
    [TestCase("", Description = "an empty answer")]
    public async Task AnAnswerWithNoListAtThePath_IsUnavailable_AndLoggedWithItsReason(string answer)
    {
        var site = Site(new ScriptedTransport(_ => ScriptedTransport.Json(answer)));

        var resolved = await site.Options().GetAsync(Field(OptionSource.Url, Config()), English);

        Assert.Multiple(() =>
        {
            Assert.That(resolved, Is.SameAs(FormBuilderResolvedOptions.Unavailable));
            Assert.That(site.UrlLogs.Entries.Select(entry => (entry.Level, entry.Message)), Is.EqualTo(new[] { (LogLevel.Warning, $"The JSON address of the field {FieldKey} failed (AnswerUnreadable) and has no options fetched before, so it offers none for a minute") }));
        });
    }

    [Test]
    public async Task AnAnswerNestedDeeperThan64_IsUnavailable()
    {
        var nested = new string('[', 64) + new string(']', 64);
        var deeper = new string('[', 63) + new string(']', 63);
        var fits = Site(new ScriptedTransport(_ => ScriptedTransport.Json($"[{{\"value\":\"a\",\"more\":{nested[2..^2]}}}]")));
        var site = Site(new ScriptedTransport(_ => ScriptedTransport.Json($"[{{\"value\":\"a\",\"more\":{deeper}}}]")));

        var kept = await fits.Options().GetAsync(Field(OptionSource.Url, Config()), English);
        var resolved = await site.Options().GetAsync(Field(OptionSource.Url, Config()), English);

        Assert.Multiple(() =>
        {
            Assert.That(Values(kept), Is.EqualTo(new[] { "a" }), "64 levels deep");
            Assert.That(resolved, Is.SameAs(FormBuilderResolvedOptions.Unavailable), "65 levels deep");
        });
    }

    [Test]
    public async Task AnAddressThatFails_WithNoCopyBefore_IsUnavailable_LoggedWithoutTheAddress()
    {
        var site = Site(new ScriptedTransport(_ => ScriptedTransport.Json("{}", HttpStatusCode.ServiceUnavailable)));
        site.Repository.Write(ListKey, OptionListKind.Url, Config(), cacheSeconds: 300);

        var resolved = await site.Options().GetListAsync(ListKey, English);

        Assert.Multiple(() =>
        {
            Assert.That(resolved, Is.SameAs(FormBuilderResolvedOptions.Unavailable));
            Assert.That(site.UrlLogs.Entries.Select(entry => (entry.Level, entry.Message)), Is.EqualTo(new[] { (LogLevel.Warning, $"The JSON address of the list {ListKey} failed (Refused 503) and has no options fetched before, so it offers none for a minute") }));
            Assert.That(site.Logs.Entries.Single().Message, Does.Contain(typeof(FormBuilderOutboundException).FullName!), "the option service logs the type");
            Assert.That(
                site.UrlLogs.Entries.Concat(site.Logs.Entries).Select(entry => entry.Message + entry.Exception),
                Has.None.Contains("options.example.com").And.None.Contains("list.json"));
        });
    }

    [Test]
    public async Task AnAddressTheSiteDoesNotCall_IsNotFetched()
    {
        var transport = new ScriptedTransport(_ => ScriptedTransport.Json("[]"));
        var site = new OptionListTestSite(new OptionListTestHosts(AllowedHosts: ["other.example.com"], Transport: transport));
        site.Repository.Write(ListKey, OptionListKind.Url, Config(), cacheSeconds: 300);
        var http = Site(transport);

        var resolved = await site.Options().GetListAsync(ListKey, English);
        var overHttp = await http.Options().GetAsync(Field(OptionSource.Url, """{"url":"http://options.example.com/v1/list.json"}"""), English);

        Assert.Multiple(() =>
        {
            Assert.That(new[] { resolved, overHttp }, Has.All.SameAs(FormBuilderResolvedOptions.Unavailable));
            Assert.That(site.UrlLogs.Entries.Single().Message, Does.Contain("(HostNotAllowed)"), "a stored list whose host the host configuration no longer names");
            Assert.That(http.UrlLogs.Entries.Single().Message, Does.Contain("(SchemeNotAllowed)"));
            Assert.That(transport.Requests, Is.Empty);
        });
    }

    [Test]
    public async Task AListsOptions_AreKeptForItsCacheSeconds_OneCopyForEveryCultureAndFieldOfTheConfiguration()
    {
        var time = new TestTimeProvider();
        var transport = Counting();
        var site = Site(transport, time: time);
        site.Repository.Write(ListKey, OptionListKind.Url, Config(), cacheSeconds: 120);
        var service = site.Options(time);

        var first = await service.GetListAsync(ListKey, English);
        time.Advance(TimeSpan.FromSeconds(119));
        var german = await service.GetListAsync(ListKey, new FormBuilderOptionContext("de"));
        var field = await service.GetAsync(Field(OptionSource.Url, Config()), English);
        var otherConfig = await service.GetAsync(Field(OptionSource.Url, Config(labelFrom: "$.name")), English);
        time.Advance(TimeSpan.FromSeconds(2));
        var later = await service.GetListAsync(ListKey, English);

        Assert.Multiple(() =>
        {
            Assert.That(new[] { first, german, field }.Select(Values), Has.All.EqualTo(new[] { "fetch1" }));
            Assert.That(Values(otherConfig), Is.EqualTo(new[] { "fetch2" }), "another configuration is another copy");
            Assert.That(Values(later), Is.EqualTo(new[] { "fetch3" }), "after 120 seconds");
            Assert.That(transport.Requests, Has.Count.EqualTo(3));
        });
    }

    [Test]
    public async Task AFieldsOwnUrlSource_KeepsItsOptionsFor10Minutes()
    {
        var time = new TestTimeProvider();
        var transport = Counting();
        var site = Site(transport, time: time);
        var service = site.Options(time);

        var first = await service.GetAsync(Field(OptionSource.Url, Config()), English);
        time.Advance(TimeSpan.FromMinutes(10) - TimeSpan.FromSeconds(1));
        var kept = await service.GetAsync(Field(OptionSource.Url, Config()), English);
        time.Advance(TimeSpan.FromSeconds(2));
        var later = await service.GetAsync(Field(OptionSource.Url, Config()), English);

        Assert.Multiple(() =>
        {
            Assert.That(new[] { first, kept }.Select(Values), Has.All.EqualTo(new[] { "fetch1" }));
            Assert.That(Values(later), Is.EqualTo(new[] { "fetch2" }));
            Assert.That(UrlOptionFetcher.FieldKeep, Is.EqualTo(TimeSpan.FromMinutes(10)));
        });
    }

    [Test]
    public async Task AStoredListWithLessThanAMinute_KeepsItsOptionsForAMinute()
    {
        var time = new TestTimeProvider();
        var transport = Counting();
        var site = Site(transport, time: time);
        site.Repository.Write(ListKey, OptionListKind.Url, Config(), cacheSeconds: 0);
        var service = site.Options(time);

        var first = await service.GetListAsync(ListKey, English);
        time.Advance(TimeSpan.FromSeconds(59));
        var kept = await service.GetListAsync(ListKey, English);
        time.Advance(TimeSpan.FromSeconds(2));
        var later = await service.GetListAsync(ListKey, English);

        Assert.Multiple(() =>
        {
            Assert.That(new[] { first, kept }.Select(Values), Has.All.EqualTo(new[] { "fetch1" }));
            Assert.That(Values(later), Is.EqualTo(new[] { "fetch2" }));
        });
    }

    [Test]
    public async Task WhileTheAddressFails_TheLastCopyIsUsed_AndTriedAgainAfterAMinute()
    {
        var time = new TestTimeProvider(new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero));
        var failing = false;
        var count = 0;
        var transport = new ScriptedTransport(_ =>
        {
            var number = Interlocked.Increment(ref count);
            return failing ? ScriptedTransport.Json("{}", HttpStatusCode.ServiceUnavailable) : ScriptedTransport.Json($$"""[{"value":"fetch{{number}}"}]""");
        });
        var site = Site(transport, time: time);
        site.Repository.Write(ListKey, OptionListKind.Url, Config(), cacheSeconds: 120);
        var service = site.Options(time);

        var first = await service.GetListAsync(ListKey, English);
        time.Advance(TimeSpan.FromSeconds(121));
        failing = true;
        var failed = await service.GetListAsync(ListKey, English);
        time.Advance(TimeSpan.FromSeconds(59));
        var waiting = await service.GetListAsync(ListKey, English);
        var requestsWhileWaiting = transport.Requests.Count;
        time.Advance(TimeSpan.FromSeconds(2));
        var failedAgain = await service.GetListAsync(ListKey, English);
        failing = false;
        time.Advance(TimeSpan.FromSeconds(61));
        var back = await service.GetListAsync(ListKey, English);

        Assert.Multiple(() =>
        {
            Assert.That(new[] { first, failed, waiting, failedAgain }.Select(Values), Has.All.EqualTo(new[] { "fetch1" }));
            Assert.That(requestsWhileWaiting, Is.EqualTo(2), "no fetch in the minute after a failure");
            Assert.That(Values(back), Is.EqualTo(new[] { "fetch4" }));
            Assert.That(transport.Requests, Has.Count.EqualTo(4));
            Assert.That(site.UrlLogs.Entries.Select(entry => entry.Level), Is.EqualTo(new[] { LogLevel.Warning, LogLevel.Warning }));
            Assert.That(
                site.UrlLogs.Entries.Select(entry => entry.Message),
                Has.All.StartWith($"The JSON address of the list {ListKey} failed (Refused 503), so it offers the options fetched at ").And.All.EndWith(" for now").And.All.Contain("2026"));
            Assert.That(UrlOptionFetcher.RetryAfter, Is.EqualTo(TimeSpan.FromMinutes(1)));
        });
    }

    [Test]
    public async Task ConfigurationsAskedForAtOnce_AreFetchedOnce_AndACallerThatStopsWaitingStopsNoOtherOne()
    {
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new ScriptedTransport((_, _) => release.Task);
        var site = Site(transport);
        site.Repository.Write(ListKey, OptionListKind.Url, Config(), cacheSeconds: 300);
        var service = site.Options();
        using var stop = new CancellationTokenSource();

        var waiting = Enumerable.Range(0, 8).Select(_ => service.GetListAsync(ListKey, English)).Append(service.GetAsync(Field(OptionSource.Url, Config()), English)).ToList();
        var stopping = service.GetListAsync(ListKey, English, stop.Token);
        await stop.CancelAsync();
        release.SetResult(ScriptedTransport.Json("""[{"value":"once"}]"""));
        var resolved = await Task.WhenAll(waiting);

        Assert.Multiple(() =>
        {
            Assert.That(resolved.Select(Values), Has.All.EqualTo(new[] { "once" }));
            Assert.That(transport.Requests, Has.Count.EqualTo(1));
            Assert.That(() => stopping, Throws.InstanceOf<OperationCanceledException>());
        });
    }

    [Test]
    public async Task TheConfiguration_IsCheckedSettingBySetting_AtItsPath()
    {
        var lists = Site(new ScriptedTransport(_ => ScriptedTransport.Json("[]"))).Lists;
        var longest = Address + "?q=" + new string('a', UrlOptionKind.MaxUrlLength - Address.Length - 3);

        Assert.Multiple(async () =>
        {
            Assert.That(await CheckAsync(lists, "{}"), Is.EqualTo(new[] { (DefinitionErrors.SettingMissing, "$.config.url", "The setting 'url' needs a value.") }));
            Assert.That(await CheckAsync(lists, Config()), Is.Empty);
            Assert.That(await CheckAsync(lists, $$"""{"url":"{{longest}}"}"""), Is.Empty, "2048 characters");
            Assert.That(await CheckAsync(lists, $$"""{"url":"{{longest}}a"}"""), Is.EqualTo(new[] { (DefinitionErrors.SettingInvalid, "$.config.url", "The setting 'url' has at most 2048 characters, not 2049.") }));
            foreach (var url in new[] { "ftp://options.example.com/list", "options.example.com/list", "/list.json", "https://user:JFB_FAKE@options.example.com/list", "https://options.example.com/list#top", "https://options.example.com/a b", "file:///c:/list.json" })
            {
                Assert.That(await CheckAsync(lists, $$"""{"url":"{{url}}"}"""), Is.EqualTo(new[] { (DefinitionErrors.SettingInvalid, "$.config.url", "An address is absolute, http or https, with no user name, password or '#' part.") }), url);
            }

            Assert.That(await CheckAsync(lists, Config(listPath: "$.", valueFrom: "value", labelFrom: "$[01]")), Is.EqualTo(new[]
            {
                (DefinitionErrors.SettingInvalid, "$.config.listPath", "A path is $, then parts such as .name, ['a name'] or [0], not '$.'."),
                (DefinitionErrors.SettingInvalid, "$.config.valueFrom", "A path is $, then parts such as .name, ['a name'] or [0], not 'value'."),
                (DefinitionErrors.SettingInvalid, "$.config.labelFrom", "A path is $, then parts such as .name, ['a name'] or [0], not '$[01]'."),
            }));
            Assert.That(await CheckAsync(lists, Config(listPath: "$" + string.Concat(Enumerable.Repeat(".a", 128)))), Is.EqualTo(new[] { (DefinitionErrors.SettingInvalid, "$.config.listPath", "The setting 'listPath' has at most 256 characters, not 257.") }));
        });
    }

    [Test]
    public async Task TheSite_CallsOnlyItsHosts_OverHttpsUnlessItAllowsHttp()
    {
        var https = Site(new ScriptedTransport(_ => ScriptedTransport.Json("[]"))).Lists;
        var http = new OptionListTestSite(new OptionListTestHosts(AllowedHosts: ["options.example.com"], AllowHttp: true)).Lists;

        Assert.Multiple(async () =>
        {
            Assert.That(await CheckAsync(https, """{"url":"http://options.example.com/list"}"""), Is.EqualTo(new[] { (DefinitionErrors.SettingInvalid, "$.config.url", "The site calls http addresses only when JironFormBuilder:Webhooks:AllowHttp is on; use https.") }));
            Assert.That(await CheckAsync(https, """{"url":"https://Other.Example.com/list"}"""), Is.EqualTo(new[] { (DefinitionErrors.SettingInvalid, "$.config.url", "The site does not call the host 'other.example.com': its host configuration names the hosts it calls in JironFormBuilder:Webhooks:AllowedHosts.") }));
            Assert.That(await CheckAsync(https, """{"url":"https://OPTIONS.example.com/list"}"""), Is.Empty);
            Assert.That(await CheckAsync(http, """{"url":"http://options.example.com/list"}"""), Is.Empty);
        });
    }

    [Test]
    public async Task AUrlList_KeepsWhatItFetches_ForAMinuteToThirtyDays()
    {
        var lists = Site(new ScriptedTransport(_ => ScriptedTransport.Json("[]"))).Lists;

        Assert.Multiple(async () =>
        {
            Assert.That(await lists.CheckAsync(Url(cacheSeconds: 59)), Is.EqualTo(new[] { new DefinitionProblem(DefinitionErrors.InvalidValue, "$.cacheSeconds", "A url list keeps what it fetches for 60 seconds at least.") }));
            Assert.That(await lists.CheckAsync(Url(cacheSeconds: 0)), Has.Count.EqualTo(1), "a URL list always keeps what it fetches");
            Assert.That(await lists.CheckAsync(Url(cacheSeconds: 60)), Is.Empty);
            Assert.That(await lists.CheckAsync(Url(cacheSeconds: OptionListService.MaxCacheSeconds)), Is.Empty);
            Assert.That((await lists.CheckAsync(Url(cacheSeconds: OptionListService.MaxCacheSeconds + 1))).Select(problem => problem.Path), Is.EqualTo(new[] { "$.cacheSeconds" }));
        });
    }

    private static OptionListTestSite Site(ScriptedTransport transport, TimeProvider? time = null) =>
        new(new OptionListTestHosts(AllowedHosts: ["options.example.com"], Transport: transport, Time: time));

    // Answers each request with one option named after the request's number.
    private static ScriptedTransport Counting()
    {
        var count = 0;
        return new ScriptedTransport(_ => ScriptedTransport.Json($$"""[{"value":"fetch{{Interlocked.Increment(ref count)}}"}]"""));
    }

    private static string Config(string? listPath = null, string? valueFrom = null, string? labelFrom = null)
    {
        var settings = new Dictionary<string, string> { ["url"] = Address };
        if (listPath is not null)
        {
            settings["listPath"] = listPath;
        }

        if (valueFrom is not null)
        {
            settings["valueFrom"] = valueFrom;
        }

        if (labelFrom is not null)
        {
            settings["labelFrom"] = labelFrom;
        }

        return System.Text.Json.JsonSerializer.Serialize(settings);
    }

    private static OptionListInput Url(int cacheSeconds) => new("events", "Events", OptionListKind.Url, Config: Config(), CacheSeconds: cacheSeconds);

    private static async Task<List<(string Code, string Path, string Message)>> CheckAsync(OptionListService lists, string config) =>
        [.. (await lists.CheckAsync(new OptionListInput("events", "Events", OptionListKind.Url, Config: config, CacheSeconds: 300))).Select(problem => (problem.Code, problem.Path, problem.Message))];
}
