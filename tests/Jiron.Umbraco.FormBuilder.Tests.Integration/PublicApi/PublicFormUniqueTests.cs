using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.PublicApi;

/// <summary>
/// The answers that must be unique in their form (<c>uniquePerForm</c>, ADR-0091) through the entries and validate
/// endpoints: of submissions at once with the same answer exactly one is stored, the others failing the field's check
/// and storing nothing; an answer that differs only in letter case or surrounding spaces is the same, failing each
/// unique check of its field, and a number by its value; an entry that is deleted, rejected or marked as spam gives its answer back, while a retry still gets its
/// receipt; a repeatable group's child fails at its row, the same answer twice in one entry is no clash, and the errors
/// come in the order of the form; a unique answer is checked only once every other answer and place passes, so a post
/// that stores nothing never learns whether an answer is taken (6.8a); and validate passes an answer another entry holds.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class PublicFormUniqueTests : PublicApiTestBase
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    // The email's second check: the first is a pattern, so an error names the unique check by its index.
    private static readonly FormField Email = Field("email", "email") with
    {
        Validation = [new PatternCheck { Pattern = "[A-Za-z@.]+", Message = new LocalizedText("Letters only") }, new UniquePerFormCheck { Message = new LocalizedText("That address is in use.") }],
    };

    private IEntryRepository Entries => GetRequiredService<IEntryRepository>();

    private IFormRepository FormRows => GetRequiredService<IFormRepository>();

    [Test]
    public async Task TenSubmissionsAtOnce_WithTheSameAnswer_StoreExactlyOne_AndTheOthersFailAtTheField()
    {
        var key = await PublishAsync(fields: [NameField, Email]);
        var answers = await AtOnceAsync(key, 10, i => $$$"""{"revision":1,"values":{"name":"Guest {{{i}}}","email":"ada@example.com"}}""");
        var form = (await FormRows.GetAsync(key))!;

        Assert.Multiple(async () =>
        {
            Assert.That(answers.GroupBy(answer => answer).ToDictionary(group => group.Key, group => group.Count()), Is.EquivalentTo(new Dictionary<string, int>
            {
                ["200"] = 1,
                ["422 validation_failed check 1 at email"] = 9,
            }));
            Assert.That((await Entries.ListIdsAsync(form.Id, 100)).Count, Is.EqualTo(1));
            Assert.That(form.LastEntryNumber, Is.EqualTo(1), "a refusal rolled the form's counter back");
        });
    }

    [Test]
    public async Task TwoSubmissionsAtOnce_ThatAlsoTakeTheLastPlace_FailAtThePlace_AndLearnNothingOfTheAnswer()
    {
        var slot = Field("slot", "radio") with
        {
            Options = new FieldOptions { Items = [new FieldOption { Value = "a", Places = 1 }, new FieldOption { Value = "b" }] },
        };
        var key = await PublishAsync(fields: [NameField, Email, slot]);

        var answers = await AtOnceAsync(key, 2, i => $$$"""{"revision":1,"values":{"name":"Guest {{{i}}}","email":"ada@example.com","slot":"a"}}""");

        Assert.That(answers.Order(StringComparer.Ordinal), Is.EqualTo(new[] { "200", "422 validation_failed full at slot" }), "the place first; the answer is checked once the rest passes");
    }

    [Test]
    public async Task AnAnswerThatDiffersOnlyInCaseOrSurroundingSpaces_IsTheSame_FailingEachUniqueCheck_AndANumberComparesByItsValue()
    {
        var code = Field("code", "text") with { Validation = [new UniquePerFormCheck(), new PatternCheck { Pattern = "[A-Z0-9 -]+" }, new UniquePerFormCheck { Message = new LocalizedText("Each code once.") }] };
        var amount = Field("amount", "number") with { Validation = [new UniquePerFormCheck()] };
        var other = Field("other", "text");
        var key = await PublishAsync(fields: [NameField, Email, code, amount, other]);
        var answers = new List<string>();
        async Task PostEntryAsync(string values)
        {
            using var response = await PostAsync(key, $$$"""{"revision":1,"values":{"name":"Ada",{{{values}}}}}""");
            answers.Add(await AnswerAsync(response));
        }

        await PostEntryAsync("\"email\":\"ada@example.com\",\"code\":\"AB-1\",\"amount\":\"12\",\"other\":\"same\"");
        await PostEntryAsync("\"email\":\"ADA@example.COM\",\"code\":\" AB-1  \"");
        await PostEntryAsync("\"code\":\"ab-1\"");
        await PostEntryAsync("\"email\":\"grace@example.com\",\"code\":\"AB-2\",\"amount\":\"12.0\"");
        await PostEntryAsync("\"email\":\"grace@example.com\",\"code\":\"AB-2\",\"amount\":\"13\",\"other\":\"same\"");
        await PostEntryAsync("\"code\":\"AB-3\"");
        await PostEntryAsync("\"code\":\"AB-4\"");

        Assert.That(answers, Is.EqualTo(new[]
        {
            "200",
            "422 validation_failed check 1 at email; check 0 at code; check 2 at code",
            "422 validation_failed check 1 at code",
            "422 validation_failed check 0 at amount",
            "200",
            "200",
            "200",
        }), "an answer that fails its pattern is held against nothing, a field without the check compares nothing, and an unanswered field holds nothing");
    }

    [Test]
    public async Task ARetryGetsItsReceipt_AndAnEntryThatIsDeletedRejectedOrMarkedAsSpam_GivesItsAnswerBack()
    {
        var key = await PublishAsync(fields: [NameField, Email]);
        var form = (await FormRows.GetAsync(key))!;
        var steps = new List<(string Step, string Answer)>();
        async Task<Guid> StepAsync(string step)
        {
            var submission = Guid.NewGuid();
            using var response = await PostAsync(key, Body(submission, "ada@example.com"));
            steps.Add((step, await AnswerAsync(response)));
            return submission;
        }

        async Task<long> IdAsync(Guid submission) => (await Entries.GetBySubmissionAsync(form.Id, submission))!.Id;
        async Task MoveAsync(Guid submission, EntryState from, EntryState to) => Assert.That(await Entries.ChangeStateAsync(await IdAsync(submission), from, to, Editor), Is.Not.Null);

        var ada = Guid.NewGuid();
        using var first = await PostAsync(key, Body(ada, "ada@example.com"));
        using var retry = await PostAsync(key, Body(ada, "ada@example.com"));
        await StepAsync("the answer is taken");
        await MoveAsync(ada, EntryState.Submitted, EntryState.Spam);
        var second = await StepAsync("an entry marked as spam gave it back");
        await MoveAsync(second, EntryState.Submitted, EntryState.Rejected);
        var third = await StepAsync("a rejected entry gave it back");
        Assert.That(await Entries.DeleteAsync(await IdAsync(third)), Is.True);
        await StepAsync("a deleted entry gave it back");
        await StepAsync("the answer is taken again");

        Assert.Multiple(async () =>
        {
            Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK), await first.Content.ReadAsStringAsync());
            Assert.That(await retry.Content.ReadAsStringAsync(), Is.EqualTo(await first.Content.ReadAsStringAsync()), "a retry of the entry that holds the answer gets its receipt");
            Assert.That(steps, Is.EqualTo(new[]
            {
                ("the answer is taken", "422 validation_failed check 1 at email"),
                ("an entry marked as spam gave it back", "200"),
                ("a rejected entry gave it back", "200"),
                ("a deleted entry gave it back", "200"),
                ("the answer is taken again", "422 validation_failed check 1 at email"),
            }));
        });
    }

    [Test]
    public async Task ARepeatableGroupsChild_FailsAtItsRow_InTheOrderOfTheForm_OnceTheRestPasses_AndOneEntryMayRepeatAnAnswer()
    {
        var badge = Field("badge", "text") with { Validation = [new UniquePerFormCheck()] };
        var guests = Field("guests", "repeater") with { Fields = [badge] };
        var email = Email with { Required = true };
        var key = await PublishAsync(fields: [NameField with { Required = true }, guests, email]);

        using var first = await PostAsync(key, """{"revision":1,"values":{"name":"Ada","guests":[{"badge":"A1"}],"email":"ada@example.com"}}""");
        using var many = await PostAsync(key, """{"revision":1,"values":{"name":"","guests":[{"badge":"B2"},{"badge":"a1"}]}}""");
        using var twice = await PostAsync(key, """{"revision":1,"values":{"name":"Grace","guests":[{"badge":"C3"},{"badge":"c3"}],"email":"grace@example.com"}}""");
        using var taken = await PostAsync(key, """{"revision":1,"values":{"name":"Hedy","guests":[{"badge":"D4"},{"badge":"C3"}],"email":"ada@example.com"}}""");

        Assert.Multiple(async () =>
        {
            Assert.That(await AnswerAsync(first), Is.EqualTo("200"));
            Assert.That(await AnswerAsync(many), Is.EqualTo("422 validation_failed required at name; required at email"), "the taken badge is not told while the rest fails");
            Assert.That(await AnswerAsync(twice), Is.EqualTo("200"), "each answer is held against the other entries");
            Assert.That(await AnswerAsync(taken), Is.EqualTo("422 validation_failed check 0 at guests[1].badge; check 1 at email"));
        });
    }

    [Test]
    public async Task AnAnswerAnotherEntryHolds_IsNotToldToAPostThatFailsOtherwise_WhichStoresNothing()
    {
        var key = await PublishAsync(fields: [NameField with { Required = true }, Email]);
        var form = (await FormRows.GetAsync(key))!;
        using var first = await PostAsync(key, Body(Guid.NewGuid(), "ada@example.com"));

        using var probe = await PostAsync(key, """{"revision":1,"values":{"name":"","email":"ada@example.com"}}""");
        using var free = await PostAsync(key, """{"revision":1,"values":{"name":"","email":"grace@example.com"}}""");

        Assert.Multiple(async () =>
        {
            Assert.That(await AnswerAsync(first), Is.EqualTo("200"));
            Assert.That(await AnswerAsync(probe), Is.EqualTo(await AnswerAsync(free)), "a taken address and a free one answer alike while the post fails");
            Assert.That(await AnswerAsync(probe), Is.EqualTo("422 validation_failed required at name"));
            Assert.That((await Entries.ListIdsAsync(form.Id, 100)).Count, Is.EqualTo(1), "and nothing is stored");
        });
    }

    [Test]
    public async Task Validate_PassesAnAnswerAnotherEntryHolds()
    {
        var key = await PublishAsync(fields: [NameField, Email]);
        var page = (await Forms.GetDraftAsync(key))!.Definition!.Pages[0].Key;
        using var stored = await PostAsync(key, """{"revision":1,"values":{"name":"Ada","email":"ada@example.com"}}""");

        using var taken = await ValidateAsync(key, $$$"""{"revision":1,"pageKey":"{{{page}}}","values":{"name":"Grace","email":"ada@example.com"}}""");
        using var wrong = await ValidateAsync(key, $$$"""{"revision":1,"pageKey":"{{{page}}}","values":{"name":"Grace","email":"ada1@example.com"}}""");

        Assert.Multiple(async () =>
        {
            Assert.That(await AnswerAsync(stored), Is.EqualTo("200"));
            Assert.That(await AnswerAsync(taken), Is.EqualTo("204"), "a page's check cannot tell whether an answer is taken");
            Assert.That(await AnswerAsync(wrong), Is.EqualTo("422 validation_failed check 0 at email"), "the other checks still run");
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.Services.AddUnique(provider => Intercepted<IEntryRepository>.Over(ActivatorUtilities.CreateInstance<EntryRepository>(provider)));
    }

    private static FormField Field(string alias, string type) => new()
    {
        Key = Guid.NewGuid(),
        Type = type,
        Alias = alias,
        Label = new LocalizedText(alias),
    };

    private static string Body(Guid submission, string email) =>
        $$$"""{"submissionId":"{{{submission}}}","revision":1,"values":{"name":"Ada","email":"{{{email}}}"}}""";

    // The status, the code and each error with where it is, such as "422 validation_failed check 1 at email"; "200" for
    // an entry and "204" for a page that passes.
    private static async Task<string> AnswerAsync(HttpResponseMessage response)
    {
        if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent)
        {
            return ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var body = await response.Content.ReadAsStringAsync();
        var problem = body.Length > 0 ? JsonNode.Parse(body) as JsonObject : null;
        var errors = problem?["errors"] is JsonArray list
            ? " " + string.Join("; ", list.Select(error => error!["code"]!.GetValue<string>() == "check" ? $"check {error["check"]} at {error["path"]}" : $"{error["code"]} at {error["path"]}"))
            : string.Empty;
        return $"{(int)response.StatusCode} {problem?["code"]?.GetValue<string>() ?? "(no code)"}{errors}";
    }

    // Posts these submissions at once: each reads the answers taken before any of them is stored, as submissions at
    // once do, so only the check inside each one's unit of work can keep an answer unique.
    private async Task<List<string>> AtOnceAsync(Guid key, int count, Func<int, string> body)
    {
        var arrived = 0;
        var all = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Hooks().On(nameof(IEntryRepository.GetTakenAnswersAsync), (_, call) => TogetherAsync(call));
        async Task<IReadOnlySet<AnswerValue>> TogetherAsync(Func<object?> call)
        {
            var taken = await (Task<IReadOnlySet<AnswerValue>>)call()!;
            if (Interlocked.Increment(ref arrived) == count)
            {
                all.SetResult();
            }

            await all.Task.WaitAsync(Patience);
            return taken;
        }

        var responses = await Task.WhenAll(Enumerable.Range(0, count).Select(i => Task.Run(() => PostAsync(key, body(i))))).WaitAsync(Patience);
        Hooks().Off(nameof(IEntryRepository.GetTakenAnswersAsync));
        var answers = new List<string>();
        foreach (var response in responses)
        {
            answers.Add(await AnswerAsync(response));
            response.Dispose();
        }

        Assert.That(arrived, Is.EqualTo(count), "each read the answers taken");
        return answers;
    }

    private Intercepted<IEntryRepository> Hooks() => Intercepted<IEntryRepository>.Of(GetRequiredService<IEntryRepository>());

    private async Task<HttpResponseMessage> PostAsync(Guid form, string json) =>
        await SendAsync(HttpMethod.Post, $"{Root}/forms/{form}/entries", request => request.Content = new StringContent(json, Encoding.UTF8, "application/json"));

    private async Task<HttpResponseMessage> ValidateAsync(Guid form, string json) =>
        await SendAsync(HttpMethod.Post, $"{Root}/forms/{form}/validate", request => request.Content = new StringContent(json, Encoding.UTF8, "application/json"));
}
