using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Files;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.PublicApi;

/// <summary>
/// The validate endpoint of the public API (ADR-0089) on a real site: a page whose answers pass answers <c>204</c> and
/// stores, counts and claims nothing, so its upload is still the submission's; a page's errors are those of its own
/// fields only, by alias, with its repeatable groups' children and grid rows, ruled with every answer given so far, the
/// earlier pages' and a later page's among them; a page key that names no page is an invalid request for the current
/// revision and a change for an older one, whose page is checked against the live version; and a closed, full, trashed or
/// members-only form, a newer revision, a changed consent wording and an alias the form lacks are refused as a
/// submission is, while a page without an answer is none.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class PublicFormValidateTests : PublicApiTestBase
{
    private static readonly Guid First = Guid.Parse("30000000-0000-4000-8000-000000000201");
    private static readonly Guid Second = Guid.Parse("30000000-0000-4000-8000-000000000202");
    private static readonly Guid Third = Guid.Parse("30000000-0000-4000-8000-000000000203");
    private static readonly GridRow Speed = new() { Key = Guid.Parse("30000000-0000-4000-8000-000000000204"), Label = new LocalizedText("Speed") };
    private static readonly GridRow Price = new() { Key = Guid.Parse("30000000-0000-4000-8000-000000000205"), Label = new LocalizedText("Price") };

    private string _storeRoot = null!;

    private IEntryRepository Entries => GetRequiredService<IEntryRepository>();

    private IFormRepository FormRows => GetRequiredService<IFormRepository>();

    [TearDown]
    public void DeleteTheStore()
    {
        if (Directory.Exists(_storeRoot))
        {
            Directory.Delete(_storeRoot, recursive: true);
        }
    }

    [Test]
    public async Task APageWhoseAnswersPass_Answers204_AndStoresCountsAndClaimsNothing()
    {
        var attachment = Field("attachment", "file", """{"types":["text"]}""");
        var phone = Field("phone", "text") with { Required = true };
        var company = Field("company", "text") with { Required = true };
        var key = await PublishAsync(change: definition => OnPages(definition, (First, [NameField with { Required = true }, attachment]), (Second, [phone]), (Third, [company])));
        var token = await UploadAsync(key, attachment.Key, "notes.txt", "Some notes"u8.ToArray());

        using var first = await ValidateAsync(key, $$$"""{"revision":1,"pageKey":"{{{First}}}","values":{"name":"Ada","attachment":["{{{token}}}"]}}""");
        using var second = await ValidateAsync(key, $$$"""{"revision":1,"pageKey":"{{{Second}}}","values":{"name":"Ada","attachment":["{{{token}}}"],"phone":"+44 20 7946 0000"}}""");
        var form = (await FormRows.GetAsync(key))!;
        var storedBefore = await Entries.ListIdsAsync(form.Id, 10);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var countedBefore = await GetRequiredService<IDailyStatRepository>().ListAsync(form.Id, today.AddDays(-1), today.AddDays(1));
        var waiting = await Entries.ListUploadsAsync(form.Id, 10);

        using var submitted = await SendAsync(HttpMethod.Post, $"{Root}/forms/{key}/entries", request => request.Content = Json($$$"""
            {"revision":1,"values":{"name":"Ada","attachment":["{{{token}}}"],"phone":"+44 20 7946 0000","company":"Analytical Engines"}}
            """));
        var entry = (await Entries.ListIdsAsync(form.Id, 10)).Single();
        var files = await Entries.ListFilesAsync(entry);

        Assert.Multiple(async () =>
        {
            Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), await first.Content.ReadAsStringAsync());
            Assert.That(await first.Content.ReadAsStringAsync(), Is.Empty);
            Assert.That(Header(first, "Cache-Control"), Is.EqualTo("no-store"));
            Assert.That(second.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), await second.Content.ReadAsStringAsync());
            Assert.That(storedBefore, Is.Empty, "no entry");
            Assert.That(countedBefore, Is.Empty, "no count");
            Assert.That(waiting.Select(file => file.EntryId), Is.EqualTo(new long?[] { null }), "the upload still waits for its entry");
            Assert.That(submitted.StatusCode, Is.EqualTo(HttpStatusCode.OK), await submitted.Content.ReadAsStringAsync());
            Assert.That(files.Select(file => file.OriginalName), Is.EqualTo(new[] { "notes.txt" }), "the upload the pages were checked with is still the submission's to claim");
        });
    }

    [Test]
    public async Task APage_AnswersTheErrorsOfItsOwnFieldsOnly_ByAlias_RuledWithEveryAnswerGivenSoFar()
    {
        var email = Field("email", "email");
        var kind = Field("kind", "text");
        var country = Field("country", "text");
        var phone = Field("phone", "text") with { Required = true };
        var company = Field("company", "text") with
        {
            Rules = [new FormRule { Key = Guid.NewGuid(), Effect = RuleEffect.Require, Conditions = [new RuleCondition { Field = kind.Key, Operator = "equals", Value = new ConditionOperand.Text("business") }] }],
        };
        var vat = Field("vat", "text") with
        {
            Rules = [new FormRule { Key = Guid.NewGuid(), Effect = RuleEffect.Require, Conditions = [new RuleCondition { Field = country.Key, Operator = "equals", Value = new ConditionOperand.Text("DE") }] }],
        };
        var satisfaction = Field("satisfaction", "grid") with { Required = true, Options = Options("good", "bad"), Rows = [Speed, Price] };
        var guests = Field("guests", "repeater") with { Fields = [Field("guestName", "text") with { Required = true }] };
        var notes = Field("notes", "text") with { Required = true };
        var badPrice = new FormValidationRule
        {
            Key = Guid.Parse("30000000-0000-4000-8000-000000000206"),
            Field = Price.Key,
            Message = new LocalizedText("Tell us more about the price."),
            Conditions = [new RuleCondition { Field = Price.Key, Operator = "equals", Value = new ConditionOperand.Text("bad") }],
        };
        var key = await PublishAsync(change: definition => OnPages(definition with { ValidationRules = [badPrice] }, (First, [NameField with { Required = true }, email, kind]), (Second, [phone, company, vat, satisfaction, guests]), (Third, [country, notes])));
        var wrong = $$$"""
            "email":"not an address","kind":"business","country":"DE","satisfaction.{{{Price.Key}}}":"bad","guests":[{"guestName":"Grace"},{"guestName":""}]
            """;

        using var second = await ValidateAsync(key, $$$"""{"revision":1,"pageKey":"{{{Second}}}","values":{{{{wrong}}}}}""");
        using var first = await ValidateAsync(key, $$$"""{"revision":1,"pageKey":"{{{First}}}","values":{{{{wrong}}}}}""");
        using var third = await ValidateAsync(key, $$$"""{"revision":1,"pageKey":"{{{Third}}}","values":{{{{wrong}}}}}""");
        using var passing = await ValidateAsync(key, $$$"""
            {"revision":1,"pageKey":"{{{Second}}}","values":{"kind":"private","phone":"+44 20 7946 0000",
             "satisfaction.{{{Speed.Key}}}":"good","satisfaction.{{{Price.Key}}}":"good","guests":[{"guestName":"Grace"}]}}
            """);

        Assert.Multiple(async () =>
        {
            Assert.That(await ErrorsAsync(second), Is.EquivalentTo(new[]
            {
                "required at phone",
                "required at company",
                "required at vat",
                "required at satisfaction",
                $"rule {badPrice.Key} at satisfaction.{Price.Key}",
                "required at guests[1].guestName",
            }), "the page's own fields, required by an earlier page's answer and by a later one's");
            Assert.That(await ErrorsAsync(first), Is.EquivalentTo(new[] { "required at name", "invalid at email" }));
            Assert.That(await ErrorsAsync(third), Is.EqualTo(new[] { "required at notes" }));
            Assert.That(passing.StatusCode, Is.EqualTo(HttpStatusCode.NoContent), await passing.Content.ReadAsStringAsync());
        });
    }

    [Test]
    public async Task APageKeyThatNamesNoPage_IsAnInvalidRequest_AndAnOlderRevisionsPage_IsCheckedAgainstTheLiveVersion()
    {
        var phone = Field("phone", "text");
        var email = Field("email", "email") with { Required = true };
        var key = await PublishAsync(change: definition => OnPages(definition, (First, [NameField]), (Second, [phone])));
        using var unknownNow = await ValidateAsync(key, $$$"""{"revision":1,"pageKey":"{{{Guid.NewGuid()}}}","values":{"name":"Ada"}}""");
        await SaveAndPublishAsync(key, definition => OnPages(definition, (First, [NameField, email]), (Third, [phone])));

        var answers = new Dictionary<string, string>();
        foreach (var (what, body) in new[]
        {
            ("the current revision's first page", $$$"""{"revision":2,"pageKey":"{{{First}}}","values":{"name":"Ada","email":"ada@example.com"}}"""),
            ("the older revision's first page, which now asks for more", $$$"""{"revision":1,"pageKey":"{{{First}}}","values":{"name":"Ada"}}"""),
            ("the older revision's second page, gone since", $$$"""{"revision":1,"pageKey":"{{{Second}}}","values":{"name":"Ada"}}"""),
            ("a page neither revision has, for the older one", $$$"""{"revision":1,"pageKey":"{{{Guid.NewGuid()}}}","values":{"name":"Ada"}}"""),
            ("a page the older revision lacks, for the current one", $$$"""{"revision":2,"pageKey":"{{{Second}}}","values":{"name":"Ada"}}"""),
            ("the current revision's page the older one lacks", $$$"""{"revision":2,"pageKey":"{{{Third}}}","values":{"name":"Ada"}}"""),
        })
        {
            using var response = await ValidateAsync(key, body);
            answers[what] = await AnswerAsync(response);
        }

        Assert.Multiple(async () =>
        {
            Assert.That(await AnswerAsync(unknownNow), Is.EqualTo("400 invalid_request invalid_value at pageKey"));
            Assert.That(answers, Is.EqualTo(new Dictionary<string, string>
            {
                ["the current revision's first page"] = "204",
                ["the older revision's first page, which now asks for more"] = "409 form_changed",
                ["the older revision's second page, gone since"] = "204",
                ["a page neither revision has, for the older one"] = "409 form_changed",
                ["a page the older revision lacks, for the current one"] = "400 invalid_request invalid_value at pageKey",
                ["the current revision's page the older one lacks"] = "204",
            }), string.Join(" | ", answers));
        });
    }

    [Test]
    public async Task TheRefusalsOfASubmission_ComeOnThePage_AndAPageWithoutAnAnswerIsNoError()
    {
        var notYet = await PublishAsync("Not yet", change: definition => OnFirstPage(definition with
        {
            Settings = definition.Settings with { Availability = definition.Settings.Availability with { OpensAt = Instant(DateTime.UtcNow.AddDays(1)) } },
        }));
        var full = await PublishAsync("Full", change: definition => OnFirstPage(definition with
        {
            Settings = definition.Settings with { Availability = definition.Settings.Availability with { MaxEntries = 1 } },
        }));
        using (var entry = await SendAsync(HttpMethod.Post, $"{Root}/forms/{full}/entries", request => request.Content = Json("""{"revision":1,"values":{"name":"Ada"}}""")))
        {
            Assert.That(entry.StatusCode, Is.EqualTo(HttpStatusCode.OK), await entry.Content.ReadAsStringAsync());
        }

        var members = await PublishAsync("Members", change: definition => OnFirstPage(definition with
        {
            Settings = definition.Settings with { Availability = definition.Settings.Availability with { Access = MemberAccess.Members } },
        }));
        var trashed = await PublishAsync("Trashed", change: OnFirstPage);
        await Forms.TrashAsync(trashed, (await Forms.GetAsync(trashed))!.ConcurrencyVersion, Editor);

        // A form the site still serves when the page is checked: it read the form before the trash, which went past the
        // form service and its cache.
        var pastTheCache = await PublishAsync("Past the cache", change: OnFirstPage);
        using (var served = await GetAsync($"{Root}/forms/{pastTheCache}"))
        {
            Assert.That(served.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        }

        var row = (await FormRows.GetAsync(pastTheCache))!;
        _ = await FormRows.TrashAsync(row.Id, row.ConcurrencyVersion, Editor);
        var consent = Field("privacy", "consent", """{"text":{"":"I agree to the notice."}}""") with { Required = true };
        var open = await PublishAsync("Open", change: definition => OnPages(definition, (First, [NameField, consent]), (Second, [Field("company", "text")])));
        var hash = await ConsentHashAsync(open, consent.Key);

        var answers = new Dictionary<string, string>();
        foreach (var (what, form, body) in new[]
        {
            ("a form before its opening time", notYet, $$$"""{"revision":1,"pageKey":"{{{First}}}","values":{"name":"Ada"}}"""),
            ("a full form", full, $$$"""{"revision":1,"pageKey":"{{{First}}}","values":{"name":"Ada"}}"""),
            ("a form for members", members, $$$"""{"revision":1,"pageKey":"{{{First}}}","values":{"name":"Ada"}}"""),
            ("a form in the trash", trashed, $$$"""{"revision":1,"pageKey":"{{{First}}}","values":{"name":"Ada"}}"""),
            ("a form trashed past the cache", pastTheCache, $$$"""{"revision":1,"pageKey":"{{{First}}}","values":{"name":"Ada"}}"""),
            ("a form that does not exist", Guid.NewGuid(), $$$"""{"revision":1,"pageKey":"{{{First}}}","values":{"name":"Ada"}}"""),
            ("a newer revision", open, $$$"""{"revision":2,"pageKey":"{{{First}}}","values":{"name":"Ada"}}"""),
            ("a consent wording other than the one it shows", open, $$$"""{"revision":1,"pageKey":"{{{First}}}","values":{"name":"Ada","privacy":true},"consents":{"privacy":"0123abcd"}}"""),
            ("the consent wording it shows", open, $$$"""{"revision":1,"pageKey":"{{{First}}}","values":{"name":"Ada","privacy":true},"consents":{"privacy":"{{{hash}}}"}}"""),
            ("an alias the form lacks", open, $$$"""{"revision":1,"pageKey":"{{{First}}}","values":{"name":"Ada","nope":"x"}}"""),
            ("a page without an answer", open, $$$"""{"revision":1,"pageKey":"{{{Second}}}"}"""),
        })
        {
            using var response = await ValidateAsync(form, body);
            answers[what] = await AnswerAsync(response);
        }

        var stored = 0;
        foreach (var form in new[] { notYet, members, trashed, open })
        {
            stored += (await Entries.ListIdsAsync((await FormRows.GetAsync(form))!.Id, 10)).Count;
        }

        Assert.Multiple(() =>
        {
            Assert.That(answers, Is.EqualTo(new Dictionary<string, string>
            {
                ["a form before its opening time"] = "403 form_closed",
                ["a full form"] = "403 entry_limit_reached",
                ["a form for members"] = "403 member_required",
                ["a form in the trash"] = "404 form_not_found",
                ["a form trashed past the cache"] = "404 form_not_found",
                ["a form that does not exist"] = "404 form_not_found",
                ["a newer revision"] = "409 form_changed",
                ["a consent wording other than the one it shows"] = "409 form_changed",
                ["the consent wording it shows"] = "204",
                ["an alias the form lacks"] = "400 invalid_request invalid_value at values.nope",
                ["a page without an answer"] = "204",
            }), string.Join(" | ", answers));
            Assert.That(stored, Is.Zero);
        });
    }

    /// <inheritdoc />
    protected override void ConfigureHost(IDictionary<string, string> configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _storeRoot = Path.Combine(Path.GetTempPath(), $"jfb-validate-{Guid.NewGuid():N}");
        configuration[FormBuilderFilesOptions.RootKey] = _storeRoot;
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

    // The definition with each page's fields in a group of their own, in this order.
    private static FormDefinition OnPages(FormDefinition definition, params (Guid Key, FormField[] Fields)[] pages) =>
        definition with { Pages = [.. pages.Select(page => new FormPage { Key = page.Key, Groups = [new FormGroup { Key = Guid.NewGuid(), Fields = page.Fields }] })] };

    // The definition with its fields on the first page.
    private static FormDefinition OnFirstPage(FormDefinition definition) => OnPages(definition, (First, [NameField]));

    // An instant as definitions hold it: in UTC, in whole seconds.
    private static DateTime Instant(DateTime utc) => new(utc.Ticks - (utc.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);

    private static StringContent Json(string json) => new(json, Encoding.UTF8, "application/json");

    // The status, the code and each error with where it is, such as "422 validation_failed required at name"; "204" for a
    // page that passes.
    private static async Task<string> AnswerAsync(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.NoContent)
        {
            return "204";
        }

        var body = await response.Content.ReadAsStringAsync();
        var problem = body.Length > 0 ? JsonNode.Parse(body) as JsonObject : null;
        var errors = problem?["errors"] is JsonArray list ? " " + string.Join("; ", list.Select(error => $"{error!["code"]} at {error["path"]}")) : string.Empty;
        return $"{(int)response.StatusCode} {problem?["code"]?.GetValue<string>() ?? "(no code)"}{errors}";
    }

    // Each error of a 422 as "required at name" or "rule <key> at satisfaction.<row>".
    private static async Task<List<string>> ErrorsAsync(HttpResponseMessage response)
    {
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.UnprocessableEntity), await response.Content.ReadAsStringAsync());
        return [.. (await JsonAsync(response))["errors"]!.AsArray().Select(error => error!["code"]!.GetValue<string>() == "rule"
            ? $"rule {error["rule"]} at {error["path"]}"
            : $"{error["code"]} at {error["path"]}")];
    }

    private async Task<HttpResponseMessage> ValidateAsync(Guid form, string json) =>
        await SendAsync(HttpMethod.Post, $"{Root}/forms/{form}/validate", request => request.Content = Json(json));

    private async Task<string> ConsentHashAsync(Guid form, Guid field)
    {
        using var response = await GetAsync($"{Root}/forms/{form}?culture=en-US");
        return (await JsonAsync(response))["consents"]![field.ToString()]!.GetValue<string>();
    }

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
