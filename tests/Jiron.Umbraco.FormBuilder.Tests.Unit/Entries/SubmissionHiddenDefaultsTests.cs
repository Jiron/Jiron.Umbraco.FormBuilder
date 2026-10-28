using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Logic;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags;
using Jiron.Umbraco.FormBuilder.Tests.Unit.OptionLists;
using Microsoft.Extensions.Logging.Abstractions;
using static Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags.MergeTagAnswerForms;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Entries;

/// <summary>
/// The default values of a submission's hidden fields (ADR-0072, ADR-0083), as <see cref="SubmissionReader"/> resolves
/// them on submit: each in the form's order over the answers so far, so a later one shows an earlier one's value, and
/// all of them over one scope, so the labels of a source's options that several of them show are asked for once; only
/// the answers the reader makes to be replaced change. A label's options are asked for the parent's answers that the
/// parent offers, and kept for those answers, so a parent resolved between two defaults gives the later one its own.
/// Each value is checked as it is resolved, over the answers so far, and not again (ADR-0080).
/// </summary>
public sealed class SubmissionHiddenDefaultsTests
{
    [Test]
    public async Task HiddenFieldsDefaults_RenderOverOneScope_ALaterOneShowingAnEarliersValue_AndASourceAskedOnceForItsLabels()
    {
        var towns = new TestOptionSource("towns", (request, _) => ValueTask.FromResult<IReadOnlyList<FormBuilderOption>>(
        [
            .. new[] { ("zurich", "Zurich", "CH"), ("berlin", "Berlin", "DE") }
                .Where(town => request.ParentValues.Contains(town.Item3))
                .Select(town => new FormBuilderOption(town.Item1, town.Item2)),
        ]));
        var country = Field("radio", "country", "Country") with { Options = Options(("CH", "Switzerland"), ("DE", "Germany")) };
        var town = Field("dropdown", "town", "Town") with
        {
            Options = new FieldOptions { Source = OptionSource.Code, Config = SettingsObject.Parse("""{"source":"towns"}"""), Parent = country.Key },
        };
        var where = Field("hidden", "where") with { DefaultValue = FieldDefaultValue.Of("{{ field.town }}") };
        var both = Field("hidden", "both") with { DefaultValue = FieldDefaultValue.Of("{{ field.where }} in {{ field.country }}") };
        var again = Field("hidden", "again") with { DefaultValue = FieldDefaultValue.Of("{{ field.town }}!") };
        var options = new OptionListTestSite(towns).Options();
        var site = MergeTagTestSite.Create(options: options);
        var reader = new SubmissionReader(TestFieldTypes.Registry, site.Service, options, NullLogger<SubmissionReader>.Instance);

        var read = await reader.ReadAsync(new PostedSubmission
        {
            Scope = MergeTagTestSite.Scope(form: Form(country, town, where, both, again)),
            Now = DateTimeOffset.UnixEpoch,
            TimeZone = "Europe/Zurich",
            Fields = new Dictionary<Guid, PostedAnswer> { [country.Key] = new(["CH"]), [town.Key] = new(["zurich"]) },
        });

        Assert.Multiple(() =>
        {
            Assert.That(read.Errors, Is.Empty);
            Assert.That(read.Answers.Select(answer => answer.Values.Single().Text), Is.EqualTo(new[] { "CH", "zurich", "Zurich", "Zurich in Switzerland", "Zurich!" }));
            Assert.That(towns.Requests, Has.Count.EqualTo(2), "one to check the town's answer, one for the label two defaults show");
        });
    }

    [Test]
    public async Task AHiddenDefaultsLabelTag_SendsASourceOnlyTheParentAnswersTheParentOffers()
    {
        var towns = Towns();
        var country = Field("checkboxes", "country", "Country") with { Options = Options(("CH", "Switzerland"), ("DE", "Germany")) };
        var town = TownOf(country);
        var where = Field("hidden", "where") with { DefaultValue = FieldDefaultValue.Of("{{ field.town }}") };
        var options = new OptionListTestSite(towns).Options();
        var reader = new SubmissionReader(TestFieldTypes.Registry, MergeTagTestSite.Create(options: options).Service, options, NullLogger<SubmissionReader>.Instance);

        // The default renders before the answers are checked, so the parent's posted text is still among them.
        var read = await reader.ReadAsync(Post(Form(country, town, where), (country, ["CH", "zz-secret"]), (town, ["zurich"])));

        Assert.Multiple(() =>
        {
            Assert.That(read.Answers.Single(answer => answer.Field == where.Key).Values.Single().Text, Is.EqualTo("Zurich"), "the label for the option the parent offers");
            Assert.That(towns.Requests.Select(request => string.Join(",", request.ParentValues)), Has.All.EqualTo("CH"), "never the posted text, which would be a key of the source's cache");
        });
    }

    [Test]
    public async Task AParentOfASitesTypeResolvedBetweenTwoDefaults_GivesTheSecondTheLabelsForItsValue()
    {
        var towns = Towns();
        var types = TestFieldTypes.Build(builder => builder.Append<ServerChoiceFieldType>());
        var before = Field("hidden", "before") with { DefaultValue = FieldDefaultValue.Of("{{ field.town }}") };
        var country = Field("acme.serverChoice", "country", "Country") with { Options = Options(("CH", "Switzerland"), ("DE", "Germany")), DefaultValue = FieldDefaultValue.Of("CH") };
        var after = Field("hidden", "after") with { DefaultValue = FieldDefaultValue.Of("{{ field.town }}") };
        var town = TownOf(country);
        var options = new OptionListTestSite(towns).Options();
        var reader = new SubmissionReader(types, MergeTagTestSite.Create(types: types, options: options).Service, options, NullLogger<SubmissionReader>.Instance);

        var read = await reader.ReadAsync(Post(Form(before, country, after, town), (town, ["zurich"])));

        Assert.Multiple(() =>
        {
            Assert.That(read.Errors, Is.Empty);
            Assert.That(read.Answers.Select(answer => answer.Values.Single().Text), Is.EqualTo(new[] { "zurich", "CH", "Zurich", "zurich" }), "the town as its value while the country has none, then by its label");
            Assert.That(towns.Requests.Select(request => string.Join(",", request.ParentValues)), Is.EqualTo(new[] { string.Empty, "CH", "CH" }), "for the labels once for each of the parent's answers, then to check the town");
        });
    }

    [Test]
    public async Task AHiddenDefaultsLabelsInRows_AskForEachRowsParentAnswer_NeverAPostedText()
    {
        var towns = Towns();
        var country = Field("dropdown", "country", "Country") with { Options = Options(("CH", "Switzerland"), ("DE", "Germany")) };
        var town = TownOf(country);
        var stops = Field("repeater", "stops", "Stops", """{"maxRows":3}""") with { Fields = [country, town] };
        var where = Field("hidden", "where") with { DefaultValue = FieldDefaultValue.Of("{{ field.town }}") };
        var options = new OptionListTestSite(towns).Options();
        var reader = new SubmissionReader(TestFieldTypes.Registry, MergeTagTestSite.Create(options: options).Service, options, NullLogger<SubmissionReader>.Instance);
        static Dictionary<Guid, PostedAnswer> Stop(FormField country, string answer, FormField town, string place) => new() { [country.Key] = new([answer]), [town.Key] = new([place]) };

        var read = await reader.ReadAsync(Post(Form(stops, where)) with
        {
            Rows = new Dictionary<Guid, IReadOnlyList<IReadOnlyDictionary<Guid, PostedAnswer>>>
            {
                [stops.Key] = [Stop(country, "CH", town, "zurich"), Stop(country, "DE", town, "berlin"), Stop(country, "zz-secret", town, "zurich")],
            },
        });

        Assert.Multiple(() =>
        {
            Assert.That(read.Answers.Single(answer => answer.Field == where.Key).Values.Single().Text, Is.EqualTo("Zurich, Berlin, zurich"), "the third row's country is no option, so its town has none, whatever the other rows answered");
            Assert.That(towns.Requests.SelectMany(request => request.ParentValues), Has.None.Contains("secret"), "neither alone nor beside another row's answer");
            Assert.That(towns.Requests.Select(request => string.Join(",", request.ParentValues)), Is.EqualTo(new[] { "CH", "DE", string.Empty, "CH", "DE", string.Empty }), "for the labels, then for the check, row by row both");
        });
    }

    [Test]
    public async Task AValueTheServerResolves_IsCheckedAsItIsResolved_NotAgainOnceALaterOneChangesItsOptions()
    {
        var letters = new TestOptionSource("letters", (request, _) =>
            ValueTask.FromResult<IReadOnlyList<FormBuilderOption>>(request.ParentValues.Contains("b") ? [new FormBuilderOption("y")] : [new FormBuilderOption("x")]));
        var types = TestFieldTypes.Build(builder => builder.Append<ServerChoiceFieldType>());
        var second = Field("acme.serverChoice", "second", "Second") with { Options = Options(("a", "A"), ("b", "B")), DefaultValue = FieldDefaultValue.Of("b") };
        var first = Field("acme.serverChoice", "first", "First") with
        {
            Options = new FieldOptions { Source = OptionSource.Code, Config = SettingsObject.Parse("""{"source":"letters"}"""), Parent = second.Key },
            DefaultValue = FieldDefaultValue.Of("x"),
        };
        var options = new OptionListTestSite(letters).Options();
        var reader = new SubmissionReader(types, MergeTagTestSite.Create(types: types, options: options).Service, options, NullLogger<SubmissionReader>.Instance);

        var read = await reader.ReadAsync(Post(Form(first, second)));

        Assert.Multiple(() =>
        {
            Assert.That(read.Errors, Is.Empty, "the visitor can neither see nor change either");
            Assert.That(read.Answers.Select(answer => answer.Values.Single().Text), Is.EqualTo(new[] { "x", "b" }), "x was an option when it was resolved, before the second had a value");
            Assert.That(letters.Requests.Select(request => string.Join(",", request.ParentValues)), Is.EqualTo(new[] { string.Empty }), "the submission's check asks nothing again");
        });
    }

    [Test]
    public async Task ADefaultNotAmongItsOptions_IsNotStored_AndIsNoError()
    {
        var types = TestFieldTypes.Build(builder => builder.Append<ServerChoiceFieldType>());
        var country = Field("acme.serverChoice", "country", "Country") with { Options = Options(("CH", "Switzerland"), ("DE", "Germany")), DefaultValue = FieldDefaultValue.Of("zz-secret") };
        var options = new OptionListTestSite().Options();
        var logger = new RecordingLogger<SubmissionReader>();
        var reader = new SubmissionReader(types, MergeTagTestSite.Create(types: types, options: options).Service, options, logger);

        var read = await reader.ReadAsync(Post(Form(country)));

        Assert.Multiple(() =>
        {
            Assert.That(read.Errors, Is.Empty, "the visitor can neither see nor change the field");
            Assert.That(read.Answers, Is.Empty, "the entry stores none");
            Assert.That(logger.Entries.Select(entry => entry.Message), Has.One.Contains("default value of the field 'country' renders to a value its type cannot take"));
            Assert.That(logger.Entries.Select(entry => entry.Message), Has.None.Contains("secret"), "never the value");
        });
    }

    [Test]
    public void OnlyAnswersMadeToBeReplaced_Change_AndTheirHiddenFieldsStay()
    {
        var shown = Guid.NewGuid();
        var hidden = Guid.NewGuid();
        var replaceable = MergeTagAnswers.Replaceable([new MergeTagAnswer(shown, [new FieldValue("a")])], [hidden]);

        replaceable.Replace([new MergeTagAnswer(shown, [new FieldValue("b")])]);

        Assert.Multiple(() =>
        {
            Assert.That(replaceable.Find(shown)?.Values.Single().Text, Is.EqualTo("b"));
            Assert.That(replaceable.Hidden, Is.EquivalentTo(new[] { hidden }));
            Assert.That(() => MergeTagAnswers.None.Replace([]), Throws.InvalidOperationException, "the answers every scope without any shares");
            Assert.That(() => new MergeTagAnswers([]).Replace([]), Throws.InvalidOperationException);
        });
    }

    // The towns of a country, as a site's code source gives them, filtered by the parent's answers itself.
    private static TestOptionSource Towns() => new("towns", (request, _) => ValueTask.FromResult<IReadOnlyList<FormBuilderOption>>(
    [
        .. new[] { ("zurich", "Zurich", "CH"), ("berlin", "Berlin", "DE") }
            .Where(town => request.ParentValues.Contains(town.Item3))
            .Select(town => new FormBuilderOption(town.Item1, town.Item2)),
    ]));

    private static FormField TownOf(FormField country) => Field("dropdown", "town", "Town") with
    {
        Options = new FieldOptions { Source = OptionSource.Code, Config = SettingsObject.Parse("""{"source":"towns"}"""), Parent = country.Key },
    };

    private static PostedSubmission Post(FormDefinition form, params (FormField Field, string[] Texts)[] answers) => new()
    {
        Scope = MergeTagTestSite.Scope(form: form),
        Now = DateTimeOffset.UnixEpoch,
        TimeZone = "Europe/Zurich",
        Fields = answers.ToDictionary(answer => answer.Field.Key, answer => new PostedAnswer(answer.Texts)),
    };

    /// <summary>
    /// A site's type that keeps its value on the server and has options, as no built-in type does (ADR-0083), and holds
    /// its values to them.
    /// </summary>
    public sealed class ServerChoiceFieldType() : FormBuilderFieldTypeBase<NoFieldSettings>(new FieldTypeDeclaration
    {
        Alias = "acme.serverChoice",
        Name = "Server choice",
        Icon = "icon-list",
        PaletteGroup = FieldPaletteGroup.Choices,
        HoldsValue = true,
        Storage = FieldStorageKind.Text,
        RuleKind = FieldKind.Choice,
        ServerOnlyDefault = true,
        Features = FieldFeatures.Label | FieldFeatures.DefaultValue | FieldFeatures.VisibilityRules | FieldFeatures.Options | FieldFeatures.NeedsOptions,
    })
    {
        /// <inheritdoc />
        protected override void Validate(NoFieldSettings settings, FieldValidation validation)
        {
            if (validation.Options is { } options && !validation.Values.All(value => options.Contains(value.Text)))
            {
                validation.Fail(FieldMessageKinds.Invalid);
            }
        }
    }
}
