using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Tests.Unit.Definitions;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.OptionLists;

/// <summary>
/// A field's options for the answers a request gives up its chain of parents (ADR-0086): each parent's answers cleaned
/// and held to the options the parent offers, its own or those its source resolves for its own held answers, each once
/// and in their order; unknown options up the chain make the field's unknown; a parent outside the form, one without
/// options or one a chain leads back to offers nothing; and the culture and page reach every source asked.
/// </summary>
[TestFixture]
public sealed class CascadingOptionsTests
{
    private static readonly Guid Page = Guid.Parse("86000000-0000-4000-8000-000000000001");

    private static readonly FormField Country = Field("country", new FieldOptions
    {
        Items = [new FieldOption { Value = "ch" }, new FieldOption { Value = "de" }, new FieldOption { Value = "at" }],
    });

    private static readonly FormField Region = Field("region", new FieldOptions { Source = OptionSource.Code, Parent = Country.Key });

    private static readonly FormField City = Field("city", new FieldOptions { Source = OptionSource.Code, Parent = Region.Key });

    [Test]
    public async Task AFieldWithoutAParent_IsAskedForOnce_WithNoParentValues_InTheCultureAndForThePage()
    {
        var service = new RecordingOptions();

        var resolved = await Options(service).GetAsync(Form(Country), Country, Answers(), "de-CH", Page);

        Assert.Multiple(() =>
        {
            Assert.That(resolved.Options.Select(option => option.Value), Is.EqualTo(new[] { "country-option" }));
            Assert.That(service.Calls, Is.EqualTo(new[] { "country: (none), de-CH, page" }));
        });
    }

    [Test]
    public async Task AParentsAnswers_AreCleaned_HeldToItsOwnOptions_AndGivenOnceEachInTheirOrder()
    {
        var service = new RecordingOptions();

        await Options(service).GetAsync(Form(Country, Region), Region, Answers((Country, ["fr", " de", "<b>", "ch", "de", "ch" + (char)0xA0])), "en-US", null);

        Assert.That(service.Calls, Is.EqualTo(new[] { "region: de|ch, en-US, no page" }), "the country's own options are not asked for");
    }

    [Test]
    public async Task AParentFromASource_OffersWhatItResolvesForItsOwnHeldAnswers_UpEveryLevel()
    {
        var service = new RecordingOptions
        {
            [Region.Key] = parents => parents.Contains("ch") ? ["zh", "be"] : ["by"],
        };

        await Options(service).GetAsync(Form(Country, Region, City), City, Answers((Country, ["ch"]), (Region, ["by", "zh", "x"])), "en-US", Page);
        var forSwitzerland = service.Taken();
        await Options(service).GetAsync(Form(Country, Region, City), City, Answers((Country, ["de"]), (Region, ["by", "zh"])), "en-US", Page);

        Assert.Multiple(() =>
        {
            Assert.That(forSwitzerland, Is.EqualTo(new[] { "region: ch, en-US, page", "city: zh, en-US, page" }), "a region its country does not offer is dropped");
            Assert.That(service.Calls, Is.EqualTo(new[] { "region: de, en-US, page", "city: by, en-US, page" }));
        });
    }

    [Test]
    public async Task AParentWithoutAnswers_IsNotAskedFor_AndItsChildGetsNoParentValues()
    {
        var service = new RecordingOptions();

        await Options(service).GetAsync(Form(Country, Region, City), City, Answers((Country, ["ch"]), (Region, [])), "en-US", null);

        Assert.That(service.Calls, Is.EqualTo(new[] { "city: (none), en-US, no page" }));
    }

    [Test]
    public async Task AParentWhoseOptionsAreUnknown_MakesTheFieldsUnknown_WithoutAskingForIt()
    {
        var service = new RecordingOptions { Unavailable = { Region.Key } };

        var resolved = await Options(service).GetAsync(Form(Country, Region, City), City, Answers((Country, ["ch"]), (Region, ["zh"])), "en-US", null);

        Assert.Multiple(() =>
        {
            Assert.That(resolved, Is.SameAs(FormBuilderResolvedOptions.Unavailable));
            Assert.That(service.Calls, Is.EqualTo(new[] { "region: ch, en-US, no page" }));
        });
    }

    [Test]
    public async Task AParentOutsideTheForm_OrWithoutOptions_OffersNothing()
    {
        var name = Field("name", null);
        var belowName = Field("belowName", new FieldOptions { Source = OptionSource.Code, Parent = name.Key });
        var belowNothing = Field("belowNothing", new FieldOptions { Source = OptionSource.Code, Parent = Guid.NewGuid() });
        var service = new RecordingOptions();
        var answers = Answers((name, ["Anna"]));

        await Options(service).GetAsync(Form(name, belowName, belowNothing), belowName, answers, "en-US", null);
        await Options(service).GetAsync(Form(name, belowName, belowNothing), belowNothing, _ => ["x"], "en-US", null);

        Assert.That(service.Calls, Is.EqualTo(new[] { "belowName: (none), en-US, no page", "belowNothing: (none), en-US, no page" }));
    }

    [Test]
    public async Task AChainThatLeadsBack_EndsWhereItWouldAskAgain()
    {
        var firstKey = Guid.NewGuid();
        var secondKey = Guid.NewGuid();
        var first = Field("first", new FieldOptions { Source = OptionSource.Code, Parent = secondKey }) with { Key = firstKey };
        var second = Field("second", new FieldOptions { Source = OptionSource.Code, Parent = firstKey }) with { Key = secondKey };
        var self = Field("self", new FieldOptions { Source = OptionSource.Code });
        self = self with { Options = self.Options! with { Parent = self.Key } };
        var service = new RecordingOptions
        {
            [secondKey] = _ => ["b"],
        };

        await Options(service).GetAsync(Form(first, second, self), first, Answers((first, ["a"]), (second, ["b", "c"])), "en-US", null);
        var cycle = service.Taken();
        await Options(service).GetAsync(Form(first, second, self), self, Answers((self, ["self-option"])), "en-US", null);

        Assert.Multiple(() =>
        {
            Assert.That(cycle, Is.EqualTo(new[] { "second: (none), en-US, no page", "first: b, en-US, no page" }), "the first's answers are not read for the second, which it asks for");
            Assert.That(service.Calls, Is.EqualTo(new[] { "self: (none), en-US, no page" }));
        });
    }

    [Test]
    public async Task ARepeatableGroupsField_IsFound_WithItsParentOutsideTheGroup()
    {
        var meal = Field("meal", new FieldOptions { Source = OptionSource.Code, Parent = Country.Key });
        var guests = Field("guests", null) with { Type = "repeater", Fields = [meal] };
        var service = new RecordingOptions();

        await Options(service).GetAsync(Form(Country, guests), meal, Answers((Country, ["at"])), "en-US", null);

        Assert.That(service.Calls, Is.EqualTo(new[] { "meal: at, en-US, no page" }));
    }

    [Test]
    public void ItsArguments_AreRequired()
    {
        var options = Options(new RecordingOptions());
        var form = Form(Country);

        Assert.Multiple(() =>
        {
            Assert.That(() => options.GetAsync(null!, Country, Answers(), "en-US", null), Throws.ArgumentNullException);
            Assert.That(() => options.GetAsync(form, null!, Answers(), "en-US", null), Throws.ArgumentNullException);
            Assert.That(() => options.GetAsync(form, Country, null!, "en-US", null), Throws.ArgumentNullException);
            Assert.That(() => options.GetAsync(form, Country, Answers(), null!, null), Throws.ArgumentNullException);
        });
    }

    private static CascadingOptions Options(RecordingOptions service) => new(service);

    private static FormField Field(string alias, FieldOptions? options) => new()
    {
        Key = Guid.NewGuid(),
        Type = options is null ? "text" : "dropdown",
        Alias = alias,
        Options = options,
    };

    // The full fixture's definition with these fields in its first group.
    private static FormDefinition Form(params FormField[] fields)
    {
        var definition = DefinitionFixtures.Read(DefinitionFixtures.Full);
        return definition with { Pages = [definition.Pages[0] with { Groups = [definition.Pages[0].Groups[0] with { Fields = fields }] }] };
    }

    private static Func<Guid, IReadOnlyList<string>> Answers(params (FormField Field, string[] Values)[] answers) =>
        key => answers.FirstOrDefault(answer => answer.Field.Key == key).Values ?? [];

    // An option service that notes each field it is asked for, as "alias: parent values, culture, page", and answers with
    // the options a field's function gives for the parent values, by default one named after the field.
    private sealed class RecordingOptions : IFormBuilderOptionService
    {
        private readonly Dictionary<Guid, Func<IReadOnlyList<string>, string[]>> _options = [];

        public List<string> Calls { get; } = [];

        public HashSet<Guid> Unavailable { get; } = [];

        public Func<IReadOnlyList<string>, string[]> this[Guid field]
        {
            set => _options[field] = value;
        }

        public string[] Taken()
        {
            string[] taken = [.. Calls];
            Calls.Clear();
            return taken;
        }

        public Task<FormBuilderResolvedOptions> GetAsync(FormField field, FormBuilderOptionContext context, CancellationToken cancellationToken = default)
        {
            Calls.Add($"{field.Alias}: {(context.ParentValues.Count == 0 ? "(none)" : string.Join("|", context.ParentValues))}, {context.Culture}, {(context.Page is null ? "no page" : "page")}");
            if (Unavailable.Contains(field.Key))
            {
                return Task.FromResult(FormBuilderResolvedOptions.Unavailable);
            }

            var values = _options.TryGetValue(field.Key, out var options) ? options(context.ParentValues) : [$"{field.Alias}-option"];
            return Task.FromResult(new FormBuilderResolvedOptions([.. values.Select(value => new FormBuilderOption(value))], IsAvailable: true));
        }

        public Task<FormBuilderResolvedOptions> GetListAsync(Guid list, FormBuilderOptionContext context, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The walk asks for fields.");
    }
}
