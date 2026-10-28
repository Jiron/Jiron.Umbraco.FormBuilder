using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
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
/// the answers the reader makes to be replaced change.
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
}
