using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags;
using Jiron.Umbraco.FormBuilder.Tests.Unit.OptionLists;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Entries;

/// <summary>
/// The parent answers a cascading field's source sees in a submission (ADR-0081): only the parent's answers that are
/// options the parent offers, each once and in their order, so a text the parent lacks reaches neither the source nor a
/// library list's cache; and with the parent's options unknown, the field's unknown too, its answer taken as given.
/// </summary>
public sealed class SubmissionParentAnswersTests
{
    private static readonly Guid Country = Key(1);
    private static readonly Guid Town = Key(2);
    private static readonly Guid Region = Key(3);
    private static readonly Guid Stop = Key(4);
    private static readonly Guid Place = Key(5);
    private static readonly Guid Legs = Key(6);
    private static readonly Guid Area = Key(7);
    private static readonly Guid Halt = Key(8);
    private static readonly Guid Countries = Key(50);
    private static readonly Guid Towns = Key(51);

    // The towns of a country, as a site's code source gives them, filtered by the parent's answers itself.
    private static readonly (string Value, string Country)[] KnownTowns = [("zurich", "CH"), ("geneva", "CH"), ("berlin", "DE")];

    [Test]
    public async Task AParentAnswerTheParentLacks_ReachesNeitherTheSourceNorACacheKey()
    {
        var site = new TestSite();
        var post = Post(new() { [Country] = new(["CH", "XX-secret"]), [Town] = new(["zurich"]) });

        var read = await site.ReadAsync(post);

        Assert.Multiple(() =>
        {
            Assert.That(Errors(read), Is.EqualTo(new[] { "country: invalid" }), "the parent itself refuses the text");
            Assert.That(site.Source.Requests.Select(request => string.Join(",", request.ParentValues)), Is.EqualTo(new[] { "CH" }), "only the option the parent offers");
            Assert.That(site.Lists.Caches.RuntimeCache.Get(OptionListCacheKeys.Options(Towns, "de-CH", null, ["CH"])), Is.Not.Null, "kept for the parent's option");
            Assert.That(site.Lists.Caches.RuntimeCache.Get(OptionListCacheKeys.Options(Towns, "de-CH", null, ["CH", "XX-secret"])), Is.Null, "never for the posted text");
        });
    }

    [Test]
    public async Task TheParentsAnswers_ReachTheSourceInTheirOrder_AndARepeatedOneNotAtAll()
    {
        var site = new TestSite();

        var ordered = await site.ReadAsync(Post(new() { [Country] = new(["DE", "CH"]), [Town] = new(["berlin"]) }));
        var repeated = await site.ReadAsync(Post(new() { [Country] = new(["DE", "CH", "DE"]), [Town] = new(["berlin"]) }));

        Assert.Multiple(() =>
        {
            Assert.That(Errors(ordered), Is.Empty);
            Assert.That(Errors(repeated), Is.EqualTo(new[] { "country: invalid", "town: invalid" }), "an answer naming an option twice is none, so no town belongs to it");
            Assert.That(site.Source.Requests.Select(request => string.Join(",", request.ParentValues)), Is.EqualTo(new[] { "DE,CH", string.Empty }), "as answered, not as offered");
        });
    }

    [Test]
    public async Task AParentWhoseOptionsAreUnknown_LeavesItsChildsUnknown_AndTheAnswerTakenAsGiven()
    {
        var site = new TestSite(unknownCountries: true);

        var read = await site.ReadAsync(Post(new() { [Country] = new(["CH"]), [Town] = new(["town-secret"]) }));

        Assert.Multiple(() =>
        {
            Assert.That(read.Errors, Is.Empty, "neither the parent's answer nor the child's can be held to options");
            Assert.That(site.Source.Requests, Is.Empty, "the source never sees an answer nothing held");
            Assert.That(read.Answers.Single(answer => answer.Field == Town).Values.Single().Text, Is.EqualTo("town-secret"));
            Assert.That(site.Logs.Entries.Select(entry => entry.Message), Has.One.Contains("'country'"), "the parent once");
            Assert.That(site.Logs.Entries.Select(entry => entry.Message), Has.One.Contains("'town'"), "the child once");
            Assert.That(site.Logs.Entries.Select(entry => entry.Message), Has.None.Contains("secret"));
        });
    }

    [Test]
    public async Task AParentFromASource_PassesOnlyTheOptionsItResolvedForItsOwnParent()
    {
        var site = new TestSite();

        // The regions' source offers CH and AT in the Alps, so a stop in NL is no option there, and its place's source
        // never sees it.
        var alps = await site.ReadAsync(Post(new() { [Region] = new(["alps"]), [Stop] = new(["CH"]), [Place] = new(["zurich"]) }));
        var elsewhere = await site.ReadAsync(Post(new() { [Region] = new(["alps"]), [Stop] = new(["NL-secret"]), [Place] = new(["zurich"]) }));

        Assert.Multiple(() =>
        {
            Assert.That(Errors(alps), Is.Empty);
            Assert.That(Errors(elsewhere), Is.EqualTo(new[] { "stop: invalid", "place: invalid" }));
            Assert.That(site.Regions.Requests.Select(request => string.Join(",", request.ParentValues)), Is.EqualTo(new[] { "alps", "alps" }));
            Assert.That(site.Source.Requests.Select(request => string.Join(",", request.ParentValues)), Is.EqualTo(new[] { "CH", string.Empty }), "the stop's option, then none");
        });
    }

    [Test]
    public async Task AParentInARepeatableGroup_GivesEachRowsChildTheOptionsForThatRowsAnswer()
    {
        var site = new TestSite();
        static Dictionary<Guid, PostedAnswer> Leg(string area, string halt) => new() { [Area] = new([area]), [Halt] = new([halt]) };

        var read = await site.ReadAsync(Post([]) with { Rows = new Dictionary<Guid, IReadOnlyList<IReadOnlyDictionary<Guid, PostedAnswer>>> { [Legs] = [Leg("alps", "AT"), Leg("coast", "NL"), Leg("coast", "CH")] } });

        Assert.Multiple(() =>
        {
            Assert.That(Errors(read), Is.EqualTo(new[] { "halt@2: invalid" }), "CH is an option in the Alps, not on the coast");
            Assert.That(site.Regions.Requests.Select(request => string.Join(",", request.ParentValues)), Is.EqualTo(new[] { "alps", "coast" }), "each row's own answer, asked once");
        });
    }

    // Each error as "alias: kind", a child's with its row.
    private static string[] Errors(SubmissionAnswers read) =>
        [.. read.Errors.Select(error => $"{AliasOf(error.Field)}{(error.Row is { } row ? $"@{row}" : "")}: {FieldMessageProperties.All.Single(property => property.Kind == error.Kind).Name}")];

    private static string AliasOf(Guid key) =>
        Form.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields).SelectMany(field => field.Fields.Prepend(field)).Single(field => field.Key == key).Alias!;

    private static FormField Field(Guid key, string type, string alias, FieldOptions options) => new() { Key = key, Type = type, Alias = alias, Options = options };

    // A post of the form in Swiss German.
    private static PostedSubmission Post(Dictionary<Guid, PostedAnswer> fields) => new()
    {
        Scope = MergeTagTestSite.Scope(form: Form, culture: "de-CH"),
        Now = new DateTimeOffset(2026, 10, 27, 12, 0, 0, TimeSpan.Zero),
        TimeZone = "Europe/Zurich",
        Fields = fields,
    };

    private static Guid Key(int number) => Guid.Parse($"81000000-0000-4000-8000-{number:D12}");

    // A country from a library list (unknown when the site lacks it) or the field's own options, its towns from a code
    // source in a library list that keeps them, a region whose countries come from a code source in turn, and a
    // repeatable group of legs, each with an area and a halt whose countries come from that source for the leg's area.
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
                            Field(Country, "checkboxes", "country", new FieldOptions { Source = OptionSource.List, OptionList = Countries }),
                            Field(Town, "dropdown", "town", new FieldOptions { Source = OptionSource.List, OptionList = Towns, Parent = Country }),
                            Field(Region, "radio", "region", new FieldOptions { Items = [new FieldOption { Value = "alps" }, new FieldOption { Value = "coast" }] }),
                            Field(Stop, "dropdown", "stop", new FieldOptions { Source = OptionSource.Code, Config = SettingsObject.Parse("""{"source":"regions"}"""), Parent = Region }),
                            Field(Place, "dropdown", "place", new FieldOptions { Source = OptionSource.Code, Config = SettingsObject.Parse("""{"source":"towns"}"""), Parent = Stop }),
                            new FormField
                            {
                                Key = Legs,
                                Type = "repeater",
                                Alias = "legs",
                                Settings = SettingsObject.Parse("""{"maxRows":3}"""),
                                Fields =
                                [
                                    Field(Area, "radio", "area", new FieldOptions { Items = [new FieldOption { Value = "alps" }, new FieldOption { Value = "coast" }] }),
                                    Field(Halt, "dropdown", "halt", new FieldOptions { Source = OptionSource.Code, Config = SettingsObject.Parse("""{"source":"regions"}"""), Parent = Area }),
                                ],
                            },
                        ],
                    },
                ],
            },
        ],
    };

    // A site with the countries' and the towns' library lists, unless the countries' is missing, and the code sources.
    private sealed class TestSite
    {
        public TestSite(bool unknownCountries = false)
        {
            Source = new TestOptionSource("towns", (request, _) =>
                ValueTask.FromResult<IReadOnlyList<FormBuilderOption>>([.. KnownTowns.Where(town => request.ParentValues.Contains(town.Country)).Select(town => new FormBuilderOption(town.Value))]));
            Regions = new TestOptionSource("regions", (request, _) =>
                ValueTask.FromResult<IReadOnlyList<FormBuilderOption>>(request.ParentValues.Contains("alps") ? [new FormBuilderOption("CH"), new FormBuilderOption("AT")] : [new FormBuilderOption("NL")]));
            Lists = new OptionListTestSite(Source, Regions);
            if (!unknownCountries)
            {
                Lists.Repository.Write(Countries, OptionListKind.Manual, items: """[{"value":"CH"},{"value":"DE"},{"value":"AT"}]""");
            }

            Lists.Repository.Write(Towns, OptionListKind.Code, config: """{"source":"towns"}""", cacheSeconds: 300);
        }

        public OptionListTestSite Lists { get; }

        public TestOptionSource Source { get; }

        public TestOptionSource Regions { get; }

        public RecordingLogger<SubmissionReader> Logs { get; } = new();

        public Task<SubmissionAnswers> ReadAsync(PostedSubmission post)
        {
            var options = Lists.Options();
            return new SubmissionReader(TestFieldTypes.Registry, MergeTagTestSite.Create(options: options).Service, options, Logs).ReadAsync(post);
        }
    }
}
