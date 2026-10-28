using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.PublicApi;

/// <summary>
/// How the entries endpoint reads, rules and checks the answers (ADR-0030, ADR-0058, ADR-0062, ADR-0088): each error by
/// where it is, with the check or the rule whose message shows; an answer longer than its type takes fails at its field
/// before any rule or pattern runs over it; answers are read by their types before the rules, so a number with an
/// exponent compares as its value and a hidden field's unreadable answer fails nothing; a calculation too long for a
/// number is stored as text; a rule's requirement, the site's today and an upload's name reach the checks; and the site's
/// time zone and clock decide when a form closes.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class PublicFormEntryAnswerTests : PublicApiTestBase
{
    private static readonly GridRow Speed = new() { Key = Guid.Parse("30000000-0000-4000-8000-000000000101"), Label = new LocalizedText("Speed") };
    private static readonly GridRow Price = new() { Key = Guid.Parse("30000000-0000-4000-8000-000000000102"), Label = new LocalizedText("Price") };

    private string _storeRoot = null!;

    private IEntryRepository Entries => GetRequiredService<IEntryRepository>();

    private IFormRepository FormRows => GetRequiredService<IFormRepository>();

    [TearDown]
    public void ResetTheClockAndDeleteTheStore()
    {
        GetRequiredService<TestClock>().Reset();
        if (Directory.Exists(_storeRoot))
        {
            Directory.Delete(_storeRoot, recursive: true);
        }
    }

    [Test]
    public async Task EachError_IsReportedWhereItIs_WithTheCheckOrTheRuleWhoseMessageShows()
    {
        var email = Field("email", "email");
        var phone = Field("phone", "text") with { Validation = [new PatternCheck { Pattern = "[0-9 +]+", Message = new LocalizedText("Digits only") }] };
        var satisfaction = Field("satisfaction", "grid") with { Required = true, Options = Options("good", "bad"), Rows = [Speed, Price] };
        var guestName = Field("guestName", "text") with { Required = true };
        var guests = Field("guests", "repeater") with { Fields = [guestName] };
        var blocked = new FormValidationRule
        {
            Key = Guid.Parse("30000000-0000-4000-8000-000000000103"),
            Field = email.Key,
            Message = new LocalizedText("We cannot take this address."),
            Conditions = [new RuleCondition { Field = email.Key, Operator = "equals", Value = new ConditionOperand.Text("blocked@example.com") }],
        };
        var badPrice = new FormValidationRule
        {
            Key = Guid.Parse("30000000-0000-4000-8000-000000000104"),
            Field = Price.Key,
            Message = new LocalizedText("Tell us more about the price."),
            Conditions = [new RuleCondition { Field = Price.Key, Operator = "equals", Value = new ConditionOperand.Text("bad") }],
        };
        var key = await PublishAsync(change: definition => definition with { ValidationRules = [blocked, badPrice] }, fields: [NameField with { Required = true }, email, phone, satisfaction, guests]);

        using var response = await PostAsync(key, $$$"""
            {"revision":1,"values":{"email":"blocked@example.com","phone":"call me","satisfaction.{{{Price.Key}}}":"bad",
             "guests":[{"guestName":"Ada"},{"guestName":""}]}}
            """);
        var problem = await JsonAsync(response);

        Assert.Multiple(() =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity));
            Assert.That(problem["code"]!.GetValue<string>(), Is.EqualTo("validation_failed"));
            Assert.That(Errors(problem), Is.EquivalentTo(new[]
            {
                "required at name",
                "check 0 at phone",
                "required at satisfaction",
                $"rule {badPrice.Key} at satisfaction.{Price.Key}",
                "required at guests[1].guestName",
                $"rule {blocked.Key} at email",
            }), string.Join(" | ", Errors(problem)));
            Assert.That(problem["errors"]!.AsArray().Select(error => error!["message"]!.GetValue<string>()), Is.All.Not.Contain("blocked@example.com").And.All.Not.Contain("call me"), "the lines for developers hold no answer");
        });
    }

    [Test]
    public async Task AnAnswerLongerThanItsTypeTakes_FailsAtItsField_AndNoRuleOrPatternRunsOverIt()
    {
        var comment = Field("comment", "text", """{"maxLength":4000}""") with { Validation = [new PatternCheck { Pattern = "[a-z]+" }] };
        var anyComment = new FormValidationRule
        {
            Key = Guid.NewGuid(),
            Field = comment.Key,
            Message = new LocalizedText("No comments, please."),
            Conditions = [new RuleCondition { Field = comment.Key, Operator = "isNotEmpty" }],
        };
        var key = await PublishAsync(change: definition => definition with { ValidationRules = [anyComment] }, fields: [NameField, comment]);

        using var tooLong = await PostAsync(key, $$$"""{"revision":1,"values":{"name":"Ada","comment":"{{{new string('x', 4_001)}}}"}}""");
        using var longest = await PostAsync(key, $$$"""{"revision":1,"values":{"name":"Ada","comment":"{{{new string('x', 4_000)}}}"}}""");
        using var unmatched = await PostAsync(key, """{"revision":1,"values":{"name":"Ada","comment":"X1"}}""");

        Assert.Multiple(async () =>
        {
            Assert.That(Errors(await JsonAsync(tooLong)), Is.EqualTo(new[] { "maxLength at comment" }), "the one error, before the rule and the pattern");
            Assert.That(Errors(await JsonAsync(longest)), Is.EqualTo(new[] { $"rule {anyComment.Key} at comment" }), "the longest answer the type takes reaches the rules");
            Assert.That(Errors(await JsonAsync(unmatched)), Is.EquivalentTo(new[] { "check 0 at comment", $"rule {anyComment.Key} at comment" }));
        });
    }

    [Test]
    public async Task ANumberWithAnExponent_IsStoredAndComparedAsItsValue_AndAHiddenFieldsUnreadableAnswer_FailsNothing()
    {
        var amount = Field("amount", "number");
        var reason = Field("reason", "text") with
        {
            Rules = [new FormRule { Key = Guid.NewGuid(), Effect = RuleEffect.Require, Conditions = [new RuleCondition { Field = amount.Key, Operator = "greaterThan", Value = new ConditionOperand.Text("1000") }] }],
        };
        var age = Field("age", "number") with
        {
            Rules = [new FormRule { Key = Guid.NewGuid(), Effect = RuleEffect.Show, Conditions = [new RuleCondition { Field = NameField.Key, Operator = "equals", Value = new ConditionOperand.Text("Grace") }] }],
        };
        var key = await PublishAsync(fields: [NameField, amount, reason, age]);

        using var noReason = await PostAsync(key, """{"revision":1,"values":{"name":"Ada","amount":1.5e3}}""");
        using var stored = await PostAsync(key, """{"revision":1,"values":{"name":"Ada","amount":1.5e3,"reason":"A large order","age":"not a number"}}""");
        using var shown = await PostAsync(key, """{"revision":1,"values":{"name":"Grace","amount":"12","age":"not a number"}}""");
        var form = (await FormRows.GetAsync(key))!;
        var catalogue = (await FormRows.ListFieldsAsync(form.Id)).ToDictionary(field => field.Values.FieldKey, field => field.Id);
        var values = await Entries.ListValuesAsync(await Entries.ListIdsAsync(form.Id, 10));

        Assert.Multiple(async () =>
        {
            Assert.That(Errors(await JsonAsync(noReason)), Is.EqualTo(new[] { "required at reason" }), "1.5e3 is more than 1000");
            Assert.That(stored.StatusCode, Is.EqualTo(HttpStatusCode.OK), await stored.Content.ReadAsStringAsync());
            Assert.That(values.Where(value => value.FieldId == catalogue[amount.Key]).Select(value => (value.Text, value.Number)), Is.EqualTo(new[] { ("1500", (double?)1500) }));
            Assert.That(values.Where(value => value.FieldId == catalogue[age.Key]), Is.Empty, "a hidden field stores nothing");
            Assert.That(Errors(await JsonAsync(shown)), Is.EqualTo(new[] { "invalid at age" }), "once shown, its answer is read");
        });
    }

    [Test]
    public async Task ACalculationTooLongForANumber_IsStoredAsTextOnly()
    {
        var price = Field("price", "number");
        var quantity = Field("quantity", "number");
        var total = Field("total", "calculation", """{"formula":"price * quantity"}""");
        var key = await PublishAsync(fields: [NameField, price, quantity, total]);

        using var response = await PostAsync(key, """{"revision":1,"values":{"name":"Ada","price":123456789,"quantity":123456789,"total":"1"}}""");
        var form = (await FormRows.GetAsync(key))!;
        var catalogue = (await FormRows.ListFieldsAsync(form.Id)).ToDictionary(field => field.Values.FieldKey, field => field.Id);
        var values = (await Entries.ListValuesAsync(await Entries.ListIdsAsync(form.Id, 10))).ToDictionary(value => value.FieldId);

        Assert.Multiple(async () =>
        {
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
            Assert.That((values[catalogue[total.Key]].Text, values[catalogue[total.Key]].Number), Is.EqualTo(("15241578750190521", (double?)null)), "computed by the server, more digits than a double keeps");
            Assert.That(values[catalogue[price.Key]].Number, Is.EqualTo(123456789));
        });
    }

    [Test]
    public async Task ARulesRequirement_TheSitesToday_AndAnUploadsName_ReachTheChecks_AndTheSitesClockClosesTheForm()
    {
        var settings = GetRequiredService<IFormBuilderSettingsService>();
        var general = await settings.GetAsync<GeneralSettings>();
        await settings.SaveAsync(general.Value with { TimeZone = "Pacific/Auckland" }, general.Version, Editor);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Pacific/Auckland");
        DateTimeOffset Local(int month, int day, int hour, int minute) =>
            new(TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, month, day, hour, minute, 0, DateTimeKind.Unspecified), zone), TimeSpan.Zero);

        var newsletter = Field("newsletter", "checkbox");
        var fullName = Field("fullName", "name") with
        {
            Rules = [new FormRule { Key = Guid.NewGuid(), Effect = RuleEffect.Require, Conditions = [new RuleCondition { Field = newsletter.Key, Operator = "isChecked" }] }],
        };
        var visit = Field("visit", "date", """{"min":{"today":""}}""");
        var callBack = Field("callBack", "date", """{"max":{"today":"+1d"}}""");
        var attachment = Field("attachment", "file", """{"types":["pdf","text"]}""");
        var closes = Local(11, 2, 0, 0);
        var key = await PublishAsync(change: definition => definition with
        {
            Settings = definition.Settings with { Availability = definition.Settings.Availability with { ClosesAt = closes.UtcDateTime } },
        }, fields: [NameField, newsletter, fullName, visit, callBack, attachment]);
        var clock = GetRequiredService<TestClock>();

        // Half past midnight on 1 November in Auckland, still 31 October in UTC.
        clock.SetNow(Local(11, 1, 0, 30));
        using var partMissing = await PostAsync(key, """{"revision":1,"values":{"name":"Ada","newsletter":true,"fullName.first":"Ada"}}""");
        using var notAsked = await PostAsync(key, """{"revision":1,"values":{"name":"Ada","fullName.first":"Ada"}}""");
        using var localToday = await PostAsync(key, """{"revision":1,"values":{"name":"Ada","visit":"2026-11-01","callBack":"2026-11-02"}}""");
        using var utcToday = await PostAsync(key, """{"revision":1,"values":{"name":"Ada","visit":"2026-10-31","callBack":"2026-11-03"}}""");

        var token = await UploadAsync(key, attachment.Key, "notes.txt", "Some notes"u8.ToArray());
        await SaveAndPublishAsync(key, definition => WithFields(definition, NameField, newsletter, fullName, visit, callBack, attachment with { Settings = SettingsObject.Parse("""{"types":["pdf"]}""") }));
        using var wrongType = await PostAsync(key, $$$"""{"revision":2,"values":{"name":"Ada","attachment":["{{{token}}}"]}}""");

        clock.SetNow(closes.AddMinutes(-1));
        using var beforeMidnight = await PostAsync(key, """{"revision":2,"values":{"name":"Ada"}}""");
        clock.SetNow(closes.AddMinutes(1));
        using var afterMidnight = await PostAsync(key, """{"revision":2,"values":{"name":"Ada"}}""");

        Assert.Multiple(async () =>
        {
            Assert.That(Errors(await JsonAsync(partMissing)), Is.EqualTo(new[] { "required at fullName" }), "required by the rule, without its last name");
            Assert.That(notAsked.StatusCode, Is.EqualTo(HttpStatusCode.OK), await notAsked.Content.ReadAsStringAsync());
            Assert.That(localToday.StatusCode, Is.EqualTo(HttpStatusCode.OK), await localToday.Content.ReadAsStringAsync());
            Assert.That(Errors(await JsonAsync(utcToday)), Is.EquivalentTo(new[] { "dateWindow at visit", "dateWindow at callBack" }), "today is the site's");
            Assert.That(Errors(await JsonAsync(wrongType)), Is.EqualTo(new[] { "fileType at attachment" }), "by the name of the file its token stands for");
            Assert.That(beforeMidnight.StatusCode, Is.EqualTo(HttpStatusCode.OK), await beforeMidnight.Content.ReadAsStringAsync());
            Assert.That(await RefusalAsync(afterMidnight), Is.EqualTo("403 form_closed"));
        });
    }

    /// <inheritdoc />
    protected override void ConfigureHost(IDictionary<string, string> configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _storeRoot = Path.Combine(Path.GetTempPath(), $"jfb-entries-{Guid.NewGuid():N}");
        configuration[FormBuilderFilesOptions.RootKey] = _storeRoot;
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        var clock = new TestClock();
        builder.Services.AddSingleton(clock);
        builder.Services.AddUnique<TimeProvider>(clock);
    }

    private static FormField Field(string alias, string type, string? settings = null) => new()
    {
        Key = Guid.NewGuid(),
        Type = type,
        Alias = alias,
        Label = new LocalizedText(alias),
        Settings = settings is null ? null : SettingsObject.Parse(settings),
    };

    private static FieldOptions Options(params string[] values) => new() { Items = [.. values.Select(value => new FieldOption { Value = value })] };

    // Each error of a refusal as "required at name", "check 0 at phone" or "rule <key> at email"; none for an entry.
    private static List<string> Errors(JsonNode answer) =>
        answer["errors"] is JsonArray errors
            ? [.. errors.Select(error => error!["code"]!.GetValue<string>() switch
            {
                "check" => $"check {error["check"]} at {error["path"]}",
                "rule" => $"rule {error["rule"]} at {error["path"]}",
                var code => $"{code} at {error["path"]}",
            })]
            : [];

    private async Task<HttpResponseMessage> PostAsync(Guid form, string json) =>
        await SendAsync(HttpMethod.Post, $"{Root}/forms/{form}/entries", request => request.Content = new StringContent(json, Encoding.UTF8, "application/json"));

    // Uploads a file to the field and gives its token.
    private async Task<string> UploadAsync(Guid form, Guid field, string name, byte[] bytes)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(bytes), "file", name);
        using var response = await SendAsync(HttpMethod.Post, $"{Root}/forms/{form}/uploads?fieldKey={field}", request => request.Content = content);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), await response.Content.ReadAsStringAsync());
        return (await JsonAsync(response))["token"]!.GetValue<string>();
    }
}
