using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags;
using Jiron.Umbraco.FormBuilder.Tests.Unit.OptionLists;
using Microsoft.Extensions.Logging;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Entries;

/// <summary>
/// A submission's choices against the options they offer (ADR-0077), as <see cref="SubmissionReader"/> checks them: a
/// choice from a library list or a code source held to the options it resolves, in the submission's culture and page;
/// a cascading choice held to the options of its parent's answers, a repeatable group's child to its own row's; a source
/// asked once per field and parent answers; and a source that cannot give its options taking the answer as given,
/// logged without it.
/// </summary>
public sealed class SubmissionOptionsTests
{
    private static readonly Guid Size = Key(1);
    private static readonly Guid Country = Key(2);
    private static readonly Guid City = Key(3);
    private static readonly Guid Town = Key(4);
    private static readonly Guid Stops = Key(5);
    private static readonly Guid StopCountry = Key(6);
    private static readonly Guid StopCity = Key(7);
    private static readonly Guid StopTown = Key(8);
    private static readonly Guid Sizes = Key(50);
    private static readonly Guid Page = Key(60);

    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    // The towns of a country, as a site's code source gives them: filtered by the parent's answers itself.
    private static readonly (string Value, string Country)[] Towns = [("zurich", "CH"), ("geneva", "CH"), ("berlin", "DE")];

    // A size from a library list; a country and its city from the field's own options, each city naming its country; a
    // town from a code source that a country filters, shown only for some countries; and a repeatable group of stops,
    // each a country with its city and its town.
    private static FormDefinition Form { get; } = new()
    {
        Key = Key(100),
        Pages =
        [
            new FormPage
            {
                Key = Key(101),
                Groups =
                [
                    new FormGroup
                    {
                        Key = Key(102),
                        Fields =
                        [
                            Field(Size, "dropdown", "size", new FieldOptions { Source = OptionSource.List, OptionList = Sizes }),
                            Field(Country, "radio", "country", Countries()),
                            Field(City, "dropdown", "city", Cities(Country)),
                            Field(Town, "dropdown", "town", TownsOf(Country)) with
                            {
                                Rules = [new FormRule { Key = Key(103), Effect = RuleEffect.Hide, Conditions = [new RuleCondition { Field = Country, Operator = "equals", Value = new ConditionOperand.Text("AT") }] }],
                            },
                            Field(Stops, "repeater", "stops", null, """{"maxRows":5}""") with
                            {
                                Fields =
                                [
                                    Field(StopCountry, "dropdown", "stopCountry", Countries()),
                                    Field(StopCity, "dropdown", "stopCity", Cities(StopCountry)),
                                    Field(StopTown, "dropdown", "stopTown", TownsOf(StopCountry)),
                                ],
                            },
                        ],
                    },
                ],
            },
        ],
    };

    [Test]
    public async Task AChoiceFromAList_RefusesAnAnswerTheListLacks_AndStoresOneItHas()
    {
        var site = Site();
        site.Lists.Repository.Write(Sizes, OptionListKind.Manual, items: """[{"value":"s"},{"value":"m"},{"value":"l"}]""");

        var lacking = await site.ReadAsync(Post(fields: new() { [Size] = Answer("xl") }));
        var having = await site.ReadAsync(Post(fields: new() { [Size] = Answer("m") }));

        Assert.Multiple(() =>
        {
            Assert.That(Errors(lacking), Is.EqualTo(new[] { "size: invalid" }));
            Assert.That(Errors(having), Is.Empty);
            Assert.That(Stored(having), Is.EqualTo(new[] { "size: m" }));
        });
    }

    [Test]
    public async Task ACascadingChoice_TakesTheOptionsOfItsParentsAnswer_AndThoseOfEveryAnswer()
    {
        var site = Site();

        var ofTheAnswer = await site.ReadAsync(Post(fields: new() { [Country] = Answer("CH"), [City] = Answer("zurich") }));
        var ofAnother = await site.ReadAsync(Post(fields: new() { [Country] = Answer("CH"), [City] = Answer("berlin") }));
        var ofEvery = await site.ReadAsync(Post(fields: new() { [Country] = Answer("DE"), [City] = Answer("elsewhere") }));
        var withoutAParent = await site.ReadAsync(Post(fields: new() { [City] = Answer("zurich") }));

        Assert.Multiple(() =>
        {
            Assert.That(Errors(ofTheAnswer), Is.Empty);
            Assert.That(Errors(ofAnother), Is.EqualTo(new[] { "city: invalid" }), "berlin belongs to DE alone");
            Assert.That(Errors(ofEvery), Is.Empty, "an option that names no country belongs to every one");
            Assert.That(Errors(withoutAParent), Is.EqualTo(new[] { "city: invalid" }), "with no country only the options of every answer are offered");
        });
    }

    [Test]
    public async Task ACascadingChoiceFromASource_IsResolvedForItsParentsAnswers_InTheSubmissionsCultureAndPage()
    {
        var site = Site();

        var read = await site.ReadAsync(Post(fields: new() { [Country] = Answer("DE"), [Town] = Answer("zurich") }));
        var request = site.Source.Requests.Single();

        Assert.Multiple(() =>
        {
            Assert.That(Errors(read), Is.EqualTo(new[] { "town: invalid" }), "zurich is a town of CH");
            Assert.That(request.ParentValues, Is.EqualTo(new[] { "DE" }));
            Assert.That((request.Culture, request.Page), Is.EqualTo(("de-CH", (Guid?)Page)));
        });
    }

    [Test]
    public async Task ARepeatableGroupsChild_IsHeldToTheParentInItsOwnRow()
    {
        var site = Site();
        var post = Post(rows: new()
        {
            [Stops] =
            [
                Row((StopCountry, "CH"), (StopCity, "zurich"), (StopTown, "geneva")),
                Row((StopCountry, "DE"), (StopCity, "zurich"), (StopTown, "geneva")),
            ],
        });

        var read = await site.ReadAsync(post);

        Assert.That(Errors(read), Is.EqualTo(new[] { "stopCity@1: invalid", "stopTown@1: invalid" }), "the second stop is in DE");
    }

    [Test]
    public async Task ASource_IsAskedOncePerFieldAndParentAnswers()
    {
        var site = Site();
        var post = Post(rows: new()
        {
            [Stops] =
            [
                Row((StopCountry, "CH"), (StopTown, "zurich")),
                Row((StopCountry, "CH"), (StopTown, "geneva")),
                Row((StopCountry, "DE"), (StopTown, "berlin")),
            ],
        });

        var read = await site.ReadAsync(post);

        Assert.Multiple(() =>
        {
            Assert.That(Errors(read), Is.Empty);
            Assert.That(site.Source.Requests.Select(request => string.Join(",", request.ParentValues)), Is.EqualTo(new[] { "CH", "DE" }));
        });
    }

    [Test]
    public async Task AChoiceWithoutAnAnswer_OrHiddenByTheRules_AsksItsSourceNothing()
    {
        var site = Site();

        var unanswered = await site.ReadAsync(Post(fields: new() { [Country] = Answer("CH") }));
        var hidden = await site.ReadAsync(Post(fields: new() { [Country] = Answer("AT"), [Town] = Answer("vienna") }));

        Assert.Multiple(() =>
        {
            Assert.That(Errors(unanswered), Is.Empty);
            Assert.That(Errors(hidden), Is.Empty);
            Assert.That(Stored(hidden), Is.EqualTo(new[] { "country: AT" }), "what the rules hide is neither checked nor stored");
            Assert.That(site.Source.Requests, Is.Empty);
        });
    }

    [Test]
    public async Task AChoiceWhoseSourceCannotGiveItsOptions_TakesTheAnswerAsGiven_AndLogsTheFieldWithoutIt()
    {
        var missing = Site();
        var failing = Site(fails: true);

        var fromMissing = await missing.ReadAsync(Post(fields: new() { [Size] = Answer("xl-secret") }));
        var fromFailing = await failing.ReadAsync(Post(fields: new() { [Country] = Answer("CH"), [Town] = Answer("town-secret") }));

        Assert.Multiple(() =>
        {
            Assert.That(Errors(fromMissing), Is.Empty, "the library has no such list");
            Assert.That(Stored(fromMissing), Is.EqualTo(new[] { "size: xl-secret" }));
            Assert.That(Errors(fromFailing), Is.Empty, "the source throws");
            Assert.That(Stored(fromFailing), Is.EqualTo(new[] { "country: CH", "town: town-secret" }));
            Assert.That(missing.Logs.Entries.Select(entry => (entry.Level, entry.Message)), Has.One.Matches<(LogLevel Level, string Message)>(entry => entry.Level == LogLevel.Warning && entry.Message.Contains("'size'", StringComparison.Ordinal)));
            Assert.That(failing.Logs.Entries.Select(entry => entry.Message), Has.One.Contains("'town'"));
            Assert.That(missing.Logs.Entries.Concat(failing.Logs.Entries).Select(entry => entry.Message), Has.None.Contains("secret"), "never the answer");
        });
    }

    private static TestSite Site(bool fails = false)
    {
        var source = new TestOptionSource("towns", (request, _) => fails
            ? throw new InvalidOperationException("The towns are gone.")
            : ValueTask.FromResult<IReadOnlyList<FormBuilderOption>>([.. Towns.Where(town => request.ParentValues.Contains(town.Country)).Select(town => new FormBuilderOption(town.Value))]));
        return new TestSite(new OptionListTestSite(source), source);
    }

    private static PostedSubmission Post(
        Dictionary<Guid, PostedAnswer>? fields = null,
        Dictionary<Guid, IReadOnlyList<IReadOnlyDictionary<Guid, PostedAnswer>>>? rows = null) => new()
        {
            Scope = MergeTagTestSite.Scope(form: Form, culture: "de-CH", page: Page),
            Now = Now,
            TimeZone = "Europe/Zurich",
            Fields = fields!,
            Rows = rows!,
        };

    private static FieldOptions Countries() => new() { Items = [new FieldOption { Value = "CH" }, new FieldOption { Value = "DE" }, new FieldOption { Value = "AT" }] };

    private static FieldOptions Cities(Guid parent) => new()
    {
        Items =
        [
            new FieldOption { Value = "zurich", ParentValues = ["CH"] },
            new FieldOption { Value = "berlin", ParentValues = ["DE"] },
            new FieldOption { Value = "elsewhere" },
        ],
        Parent = parent,
    };

    private static FieldOptions TownsOf(Guid parent) => new() { Source = OptionSource.Code, Config = SettingsObject.Parse("""{"source":"towns"}"""), Parent = parent };

    private static FormField Field(Guid key, string type, string alias, FieldOptions? options, string? settings = null) => new()
    {
        Key = key,
        Type = type,
        Alias = alias,
        Options = options,
        Settings = settings is null ? null : SettingsObject.Parse(settings),
    };

    private static PostedAnswer Answer(params string[] texts) => new(texts);

    private static Dictionary<Guid, PostedAnswer> Row(params (Guid Field, string Text)[] answers) => answers.ToDictionary(answer => answer.Field, answer => Answer(answer.Text));

    // Each stored value as "alias: text", a child's with its row.
    private static string[] Stored(SubmissionAnswers read) =>
        [.. read.Answers.SelectMany(answer => answer.Values.Select(value => $"{AliasOf(answer.Field)}{(IsChild(answer.Field) ? $"@{answer.Row}" : "")}: {value.Text}"))];

    // Each error as "alias: kind", a child's with its row.
    private static string[] Errors(SubmissionAnswers read) =>
        [.. read.Errors.Select(error => $"{AliasOf(error.Field)}{(error.Row is { } row ? $"@{row}" : "")}: {FieldMessageProperties.All.Single(property => property.Kind == error.Kind).Name}")];

    private static string AliasOf(Guid key) => Fields().Single(field => field.Key == key).Alias!;

    private static bool IsChild(Guid key) => Fields().Single(field => field.Key == Stops).Fields.Any(field => field.Key == key);

    private static IEnumerable<FormField> Fields() => Form.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields).SelectMany(field => field.Fields.Prepend(field));

    private static Guid Key(int number) => Guid.Parse($"74000000-0000-4000-8000-{number:D12}");

    // A site whose library lists, code source and logs the tests see, and its reader.
    private sealed class TestSite(OptionListTestSite lists, TestOptionSource source)
    {
        public OptionListTestSite Lists { get; } = lists;

        public TestOptionSource Source { get; } = source;

        public RecordingLogger<SubmissionReader> Logs { get; } = new();

        public Task<SubmissionAnswers> ReadAsync(PostedSubmission post)
        {
            var options = Lists.Options();
            var mergeTags = MergeTagTestSite.Create(options: options);
            return new SubmissionReader(TestFieldTypes.Registry, mergeTags.Service, options, Logs).ReadAsync(post);
        }
    }
}
