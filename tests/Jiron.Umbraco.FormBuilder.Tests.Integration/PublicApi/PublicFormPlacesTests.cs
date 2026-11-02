using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.PublicApi;

/// <summary>
/// The places on options (section 4.4, ADR-0090) through the entries and validate endpoints: submissions at once for
/// the last places get exactly those places, the rest failing with <c>full</c> at the field and storing nothing; an entry
/// that leaves the count gives its place back, one moved back takes it again and an edit moves it, while a retry is
/// answered with its receipt once the option is full; each repeater row and each choice of several takes a place, and the
/// choices past the places fail at their paths among the other errors in the order of the form; a library list's places
/// count as a field's own, and places set after entries came in count those entries; and validate answers <c>full</c>
/// and takes nothing.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class PublicFormPlacesTests : PublicApiTestBase
{
    /// <summary>A fixture of bodies and files, for which the challenge is beside the point: PublicFormChallengeTests holds its tests.</summary>
    public PublicFormPlacesTests() => SendsWithServerKey = true;

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);
    private static readonly Guid ListKey = Guid.Parse("74000000-0000-4000-8000-000000000090");

    private static readonly FormField Slot = Field("slot", "radio") with
    {
        Options = new FieldOptions { Items = [new FieldOption { Value = "a", Label = new LocalizedText("Morning"), Places = 1 }, new FieldOption { Value = "b", Label = new LocalizedText("Evening") }] },
    };

    private IEntryRepository Entries => GetRequiredService<IEntryRepository>();

    private IFormRepository FormRows => GetRequiredService<IFormRepository>();

    [Test]
    public async Task FiftySubmissionsAtOnce_ForTheLastTenPlaces_GetExactlyThoseTen_AndTheRestStoreNothing()
    {
        var key = await PublishAsync(fields: [NameField, Places(Slot, 12)]);
        foreach (var name in new[] { "Ada", "Grace" })
        {
            using var earlier = await PostAsync(key, Body(Guid.NewGuid(), name, "a"));
            Assert.That(earlier.StatusCode, Is.EqualTo(HttpStatusCode.OK), await earlier.Content.ReadAsStringAsync());
        }

        // Every submission reads the places left before any of them is stored, as submissions at once do: only the
        // count inside each one's unit of work can keep the places.
        var arrived = 0;
        var all = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Hooks<IEntryRepository>().On(nameof(IEntryRepository.GetPlacesTakenAsync), (_, call) => TogetherAsync(call));
        async Task<IReadOnlyDictionary<OptionPlace, int>> TogetherAsync(Func<object?> call)
        {
            var counts = await (Task<IReadOnlyDictionary<OptionPlace, int>>)call()!;
            if (Interlocked.Increment(ref arrived) == 50)
            {
                all.SetResult();
            }

            await all.Task.WaitAsync(Patience);
            return counts;
        }

        var responses = await Task.WhenAll(Enumerable.Range(0, 50).Select(i => Task.Run(() => PostAsync(key, Body(Guid.NewGuid(), "Guest " + i, "a"))))).WaitAsync(Patience);
        Hooks<IEntryRepository>().Off(nameof(IEntryRepository.GetPlacesTakenAsync));
        var answers = new List<string>();
        foreach (var response in responses)
        {
            answers.Add(await AnswerAsync(response));
            response.Dispose();
        }

        var form = (await FormRows.GetAsync(key))!;

        Assert.Multiple(async () =>
        {
            Assert.That(arrived, Is.EqualTo(50), "each read the places left");
            Assert.That(answers.GroupBy(answer => answer).ToDictionary(group => group.Key, group => group.Count()), Is.EquivalentTo(new Dictionary<string, int>
            {
                ["200"] = 10,
                ["422 validation_failed full at slot"] = 40,
            }));
            Assert.That(await TakenAsync(form.Id, Slot.Key, "a"), Is.EqualTo(12));
            Assert.That(await HeldAsync(form.Id, Slot.Key, "a"), Is.EqualTo(12), "the count is the entries' values");
            Assert.That((await Entries.ListIdsAsync(form.Id, 100)).Count, Is.EqualTo(12));
            Assert.That(form.LastEntryNumber, Is.EqualTo(12), "a refusal rolled the form's counter back");
        });
    }

    [Test]
    public async Task AnEntryThatLeavesTheCount_GivesItsPlaceBack_OneMovedBackTakesItAgain_AndAnEditMovesIt()
    {
        var key = await PublishAsync(fields: [NameField, Slot]);
        var form = (await FormRows.GetAsync(key))!;
        var catalogue = (await FormRows.ListFieldsAsync(form.Id)).ToDictionary(field => field.Values.FieldKey, field => field.Id);
        var answers = new List<(string Step, string Answer, int Taken)>();
        async Task StepAsync(string step, Guid submission, string name)
        {
            using var response = await PostAsync(key, Body(submission, name, "a"));
            answers.Add((step, await AnswerAsync(response), await TakenAsync(form.Id, Slot.Key, "a")));
        }

        async Task<long> IdAsync(Guid submission) => (await Entries.GetBySubmissionAsync(form.Id, submission))!.Id;
        async Task MoveAsync(Guid submission, EntryState from, EntryState to) => Assert.That(await Entries.ChangeStateAsync(await IdAsync(submission), from, to, Editor), Is.Not.Null);
        async Task EditAsync(Guid submission) =>
            await Entries.ReplaceValuesAsync(await IdAsync(submission), [catalogue[Slot.Key]], [new NewEntryValue(catalogue[Slot.Key], "b")], string.Empty);

        var ada = Guid.NewGuid();
        using var first = await PostAsync(key, Body(ada, "Ada", "a"));
        using var retry = await PostAsync(key, Body(ada, "Ada", "a"));
        await StepAsync("the place is taken", Guid.NewGuid(), "Grace");
        await MoveAsync(ada, EntryState.Approved, EntryState.Rejected);
        var grace = Guid.NewGuid();
        await StepAsync("a rejected entry gave it back", grace, "Grace");
        await MoveAsync(grace, EntryState.Approved, EntryState.Spam);
        var hedy = Guid.NewGuid();
        await StepAsync("an entry marked as spam gave it back", hedy, "Hedy");
        Assert.That(await Entries.DeleteAsync(await IdAsync(hedy)), Is.True);
        var ida = Guid.NewGuid();
        await StepAsync("a deleted entry gave it back", ida, "Ida");
        await MoveAsync(ada, EntryState.Rejected, EntryState.Submitted);
        await StepAsync("an entry moved back took it again", Guid.NewGuid(), "Joan");
        await EditAsync(ada);
        await StepAsync("one edit moved one place", Guid.NewGuid(), "Joan");
        await EditAsync(ida);
        await StepAsync("the other edit moved the other", Guid.NewGuid(), "Joan");

        Assert.Multiple(async () =>
        {
            Assert.That(first.StatusCode, Is.EqualTo(HttpStatusCode.OK), await first.Content.ReadAsStringAsync());
            Assert.That(await retry.Content.ReadAsStringAsync(), Is.EqualTo(await first.Content.ReadAsStringAsync()), "a retry gets its receipt once the option is full");
            Assert.That(answers, Is.EqualTo(new[]
            {
                ("the place is taken", "422 validation_failed full at slot", 1),
                ("a rejected entry gave it back", "200", 1),
                ("an entry marked as spam gave it back", "200", 1),
                ("a deleted entry gave it back", "200", 1),
                ("an entry moved back took it again", "422 validation_failed full at slot", 2),
                ("one edit moved one place", "422 validation_failed full at slot", 1),
                ("the other edit moved the other", "200", 1),
            }));
            Assert.That(await HeldAsync(form.Id, Slot.Key, "a"), Is.EqualTo(1));
            Assert.That(await HeldAsync(form.Id, Slot.Key, "b"), Is.EqualTo(2), "the edited entries hold the other option");
        });
    }

    [Test]
    public async Task EachRowAndEachChoice_TakesAPlace_AndTheChoicesPastThePlacesFailAtTheirPaths_InTheOrderOfTheForm()
    {
        var sessions = Field("sessions", "checkboxes") with
        {
            Options = new FieldOptions { Items = [new FieldOption { Value = "morning", Places = 1 }, new FieldOption { Value = "evening", Places = 1 }, new FieldOption { Value = "noon", Places = 1 }] },
        };
        var extras = Field("extras", "checkboxes") with
        {
            Settings = SettingsObject.Parse("""{"maxSelections":1}"""),
            Options = new FieldOptions { Items = [new FieldOption { Value = "lunch", Places = 1 }, new FieldOption { Value = "dinner" }] },
        };
        var seat = Field("seat", "radio") with
        {
            Options = new FieldOptions { Items = [new FieldOption { Value = "x", Places = 2 }, new FieldOption { Value = "y" }] },
        };
        var guestName = Field("guestName", "text") with { Required = true };
        var guests = Field("guests", "repeater") with { Fields = [seat, guestName] };
        var speed = new GridRow { Key = Guid.NewGuid(), Label = new LocalizedText("Speed") };
        var slow = new FormValidationRule
        {
            Key = Guid.NewGuid(),
            Field = speed.Key,
            Message = new LocalizedText("Tell us what was slow."),
            Conditions = [new RuleCondition { Field = speed.Key, Operator = "equals", Value = new ConditionOperand.Text("bad") }],
        };
        var rating = Field("rating", "grid") with { Options = new FieldOptions { Items = [new FieldOption { Value = "good" }, new FieldOption { Value = "bad" }] }, Rows = [speed] };
        var email = Field("email", "email") with { Required = true };
        var key = await PublishAsync(change: definition => definition with { ValidationRules = [slow] }, fields: [NameField with { Required = true }, sessions, rating, guests, extras, email]);
        var form = (await FormRows.GetAsync(key))!;

        using var first = await PostAsync(key, $$$"""{"revision":1,"values":{"name":"Ada","sessions":["morning"],"rating.{{{speed.Key}}}":"good","guests":[{"seat":"x","guestName":"Ada"}],"extras":["lunch"],"email":"ada@example.com"}}""");
        using var many = await PostAsync(key, $$$"""{"revision":1,"values":{"name":"","sessions":["morning","evening"],"rating.{{{speed.Key}}}":"bad","guests":[{"seat":"x","guestName":"Ada"},{"seat":"y"},{"seat":"x","guestName":"Alan"}]}}""");
        using var twoRows = await PostAsync(key, $$$"""{"revision":1,"values":{"name":"Grace","sessions":["evening"],"rating.{{{speed.Key}}}":"good","guests":[{"seat":"x","guestName":"Grace"},{"seat":"x","guestName":"Hedy"}],"email":"grace@example.com"}}""");
        using var last = await PostAsync(key, $$$"""{"revision":1,"values":{"name":"Grace","sessions":["evening"],"rating.{{{speed.Key}}}":"good","guests":[{"seat":"y","guestName":"Grace"},{"seat":"x","guestName":"Hedy"}],"email":"grace@example.com"}}""");
        using var second = await PostAsync(key, """{"revision":1,"values":{"name":"Ida","sessions":["noon","evening"],"email":"ida@example.com"}}""");
        using var both = await PostAsync(key, $$$"""{"revision":1,"values":{"name":"Hedy","sessions":["morning","evening"],"rating.{{{speed.Key}}}":"good","extras":["lunch","dinner"],"email":"hedy@example.com"}}""");

        Assert.Multiple(async () =>
        {
            Assert.That(await AnswerAsync(first), Is.EqualTo("200"));
            Assert.That(await AnswerAsync(many), Is.EqualTo($"422 validation_failed required at name; full at sessions; rule at rating.{speed.Key}; required at guests[1].guestName; full at guests[2].seat; required at email"), "the first row gets the place left, and a group's errors go row by row");
            Assert.That(await AnswerAsync(twoRows), Is.EqualTo("422 validation_failed full at guests[1].seat"));
            Assert.That(await AnswerAsync(last), Is.EqualTo("200"));
            Assert.That(await AnswerAsync(second), Is.EqualTo("422 validation_failed full at sessions"), "each choice is held, the second too");
            Assert.That(await AnswerAsync(both), Is.EqualTo("422 validation_failed full at sessions; selections at extras"), "a field fails once, and an answer that fails takes no place");
            Assert.That(await TakenAsync(form.Id, seat.Key, "x"), Is.EqualTo(2), "a place for each row");
            Assert.That(await TakenAsync(form.Id, sessions.Key, "morning"), Is.EqualTo(1));
            Assert.That(await TakenAsync(form.Id, sessions.Key, "evening"), Is.EqualTo(1), "a place for each choice");
            Assert.That(await TakenAsync(form.Id, extras.Key, "lunch"), Is.EqualTo(1));
            Assert.That(await RowsAsync(form.Id), Is.EqualTo(4), "a count for each option with places an entry took, and none for one without");
        });
    }

    [Test]
    public async Task ALibraryListsPlaces_CountAsAFieldsOwn()
    {
        var lists = GetRequiredService<OptionListService>();
        var created = await lists.CreateAsync(ListKey, new OptionListInput("sizes", "Sizes", OptionListKind.Manual, Items: """[{"value":"s","places":1},{"value":"m"}]"""));
        var size = Field("size", "dropdown") with { Options = new FieldOptions { Source = OptionSource.List, OptionList = ListKey } };
        var key = await PublishAsync(fields: [NameField, size]);
        var form = (await FormRows.GetAsync(key))!;
        var answers = new List<string>();
        async Task PostSizeAsync(string value)
        {
            using var response = await PostAsync(key, $$$"""{"revision":1,"values":{"name":"Ada","size":"{{{value}}}"}}""");
            answers.Add(await AnswerAsync(response));
        }

        await PostSizeAsync("s");
        await PostSizeAsync("s");
        await PostSizeAsync("m");
        var updated = await lists.UpdateAsync(ListKey, created.List!.ConcurrencyVersion, new OptionListInput("sizes", "Sizes", OptionListKind.Manual, Items: """[{"value":"s","places":2},{"value":"m"}]"""));
        await PostSizeAsync("s");
        await PostSizeAsync("s");

        Assert.Multiple(async () =>
        {
            Assert.That((created.Succeeded, updated.Succeeded), Is.EqualTo((true, true)));
            Assert.That(answers, Is.EqualTo(new[] { "200", "422 validation_failed full at size", "200", "200", "422 validation_failed full at size" }), "a place more once the list has one more");
            Assert.That(await TakenAsync(form.Id, size.Key, "s"), Is.EqualTo(2));
        });
    }

    [Test]
    public async Task PlacesSetAfterEntriesCameIn_CountThoseEntries()
    {
        var key = await PublishAsync(fields: [NameField, Places(Slot, null)]);
        var form = (await FormRows.GetAsync(key))!;
        var answers = new List<string>();
        async Task PostSlotAsync(int revision)
        {
            using var response = await PostAsync(key, $$$"""{"revision":{{{revision}}},"values":{"name":"Ada","slot":"a"}}""");
            answers.Add(await AnswerAsync(response));
        }

        for (var i = 0; i < 3; i++)
        {
            await PostSlotAsync(1);
        }

        var before = await RowsAsync(form.Id);
        await SaveAndPublishAsync(key, definition => WithFields(definition, NameField, Places(Slot, 4)));
        using var served = await GetAsync($"{Root}/forms/{key}");
        var revision = (await JsonAsync(served))["revision"]!.GetValue<int>();
        await PostSlotAsync(revision);
        await PostSlotAsync(revision);

        Assert.Multiple(async () =>
        {
            Assert.That(before, Is.Zero, "an option without places has no count");
            Assert.That(answers, Is.EqualTo(new[] { "200", "200", "200", "200", "422 validation_failed full at slot" }), "the three entries before hold three of the four places");
            Assert.That(await TakenAsync(form.Id, Slot.Key, "a"), Is.EqualTo(4));
        });
    }

    [Test]
    public async Task Validate_AnswersFull_AndTakesNothing()
    {
        var key = await PublishAsync(fields: [NameField, Slot]);
        var form = (await FormRows.GetAsync(key))!;
        var page = (await Forms.GetDraftAsync(key))!.Definition!.Pages[0].Key;
        string Page(string slot) => $$$"""{"revision":1,"pageKey":"{{{page}}}","values":{"name":"Ada","slot":"{{{slot}}}"}}""";

        using var free = await ValidateAsync(key, Page("a"));
        using var again = await ValidateAsync(key, Page("a"));
        var rows = await RowsAsync(form.Id);
        using var taking = await PostAsync(key, """{"revision":1,"values":{"name":"Ada","slot":"a"}}""");
        using var full = await ValidateAsync(key, Page("a"));
        using var other = await ValidateAsync(key, Page("b"));

        Assert.Multiple(async () =>
        {
            Assert.That((await AnswerAsync(free), await AnswerAsync(again)), Is.EqualTo(("204", "204")));
            Assert.That(rows, Is.Zero, "validate makes no count");
            Assert.That(await AnswerAsync(taking), Is.EqualTo("200"), "the place validate saw free is still free");
            Assert.That(await AnswerAsync(full), Is.EqualTo("422 validation_failed full at slot"));
            Assert.That(await AnswerAsync(other), Is.EqualTo("204"));
            Assert.That(await TakenAsync(form.Id, Slot.Key, "a"), Is.EqualTo(1));
            Assert.That((await Entries.ListIdsAsync(form.Id, 10)).Count, Is.EqualTo(1));
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

    // The field with its first option's places set.
    private static FormField Places(FormField field, int? places) =>
        field with { Options = field.Options! with { Items = [field.Options!.Items[0] with { Places = places }, .. field.Options.Items.Skip(1)] } };

    private static string Body(Guid submission, string name, string slot) =>
        $$$"""{"submissionId":"{{{submission}}}","revision":1,"values":{"name":"{{{name}}}","slot":"{{{slot}}}"}}""";

    // The status, the code and each error with where it is, such as "422 validation_failed full at slot"; "200" for an
    // entry and "204" for a page that passes.
    private static async Task<string> AnswerAsync(HttpResponseMessage response)
    {
        if (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent)
        {
            return ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var body = await response.Content.ReadAsStringAsync();
        var problem = body.Length > 0 ? JsonNode.Parse(body) as JsonObject : null;
        var errors = problem?["errors"] is JsonArray list ? " " + string.Join("; ", list.Select(error => $"{error!["code"]} at {error["path"]}")) : string.Empty;
        return $"{(int)response.StatusCode} {problem?["code"]?.GetValue<string>() ?? "(no code)"}{errors}";
    }

    private Intercepted<T> Hooks<T>()
        where T : class => Intercepted<T>.Of(GetRequiredService<T>());

    private async Task<HttpResponseMessage> PostAsync(Guid form, string json) =>
        await SendAsync(HttpMethod.Post, $"{Root}/forms/{form}/entries", request => request.Content = new StringContent(json, Encoding.UTF8, "application/json"));

    private async Task<HttpResponseMessage> ValidateAsync(Guid form, string json) =>
        await SendAsync(HttpMethod.Post, $"{Root}/forms/{form}/validate", request => request.Content = new StringContent(json, Encoding.UTF8, "application/json"));

    private async Task<int> TakenAsync(int formId, Guid field, string value)
    {
        var option = new OptionPlace(field, value);
        return (await Entries.GetPlacesTakenAsync(formId, [option]))[option];
    }

    // The places held on an option, counted from the entries and their values, apart from the counts.
    private async Task<int> HeldAsync(int formId, Guid field, string value)
    {
        var fieldId = (await FormRows.ListFieldsAsync(formId)).Single(each => each.Values.FieldKey == field).Id;
        var holding = new List<long>();
        foreach (var id in await Entries.ListIdsAsync(formId, 1000))
        {
            if (await Entries.GetByIdAsync(id) is { } entry && EntryLimits.Counts(entry.State, entry.IsTest))
            {
                holding.Add(id);
            }
        }

        return (await Entries.ListValuesAsync(holding)).Count(each => each.FieldId == fieldId && string.Equals(each.Text, value, StringComparison.Ordinal));
    }

    // The counts made for the form's options.
    private Task<int> RowsAsync(int formId) =>
        GetRequiredService<FormBuilderDatabase>().ReadAsync(db => db.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM {db.SqlContext.SqlSyntax.GetQuotedTableName(FormBuilderTables.OptionCapacity)} WHERE {db.SqlContext.SqlSyntax.GetQuotedColumnName("formId")} = @0",
            formId));
}
