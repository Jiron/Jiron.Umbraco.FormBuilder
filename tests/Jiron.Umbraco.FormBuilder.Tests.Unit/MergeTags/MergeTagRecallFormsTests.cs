using System.Buffers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Projection;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Testing;
using NSubstitute;
using static Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags.MergeTagAnswerForms;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags;

/// <summary>
/// The form answer recall reads in the browser (ADR-0068, ADR-0085), cut down to what the texts need: the elements that
/// own a text or are recalled, with what holds them, in the form's order; a repeatable group whole with any of its
/// fields; and for each recalled field what its answers show as. The vectors' runner holds the traits to the rules both
/// runtimes keep; these tests hold the parts only the answer has.
/// </summary>
public sealed class MergeTagRecallFormsTests
{
    private static readonly Guid List = Guid.Parse("70000000-0000-4000-8000-000000000001");

    [Test]
    public async Task OnlyTheOwnersAndTheRecalledFields_AreGiven_TheOwnersWithTheirPlaceAlone()
    {
        var name = Field("text", "name", "Name");
        var email = Field("email", "email", "Email");
        var notes = Field("textarea", "notes", "Notes");
        var later = Field("text", "later", "Later");
        var form = MergeTagTestSite.Form() with
        {
            Pages =
            [
                Page(Group(name, email), Group(Field("text", "skipped", "Skipped"))),
                Page(Group(notes)),
                Page(Group(later)),
            ],
        };

        var recall = await WriteAsync(form, owners: [notes.Key], recalled: [name.Key]);

        Assert.Multiple(() =>
        {
            Assert.That(Ids(recall), Is.EqualTo(new[] { $"page {form.Pages[0].Key}", $"group {form.Pages[0].Groups[0].Key}", $"field {name.Key}", $"page {form.Pages[1].Key}", $"group {form.Pages[1].Groups[0].Key}", $"field {notes.Key}" }));
            Assert.That(FieldOf(recall, notes.Key).Select(property => property.Key), Is.EqualTo(new[] { "id", "type", "holdsValue" }));
            Assert.That(FieldOf(recall, name.Key)["alias"]!.GetValue<string>(), Is.EqualTo("name"));
            Assert.That(FieldOf(recall, name.Key)["label"]!.GetValue<string>(), Is.EqualTo("Name"));
        });
    }

    [Test]
    public async Task ARepeatableGroupComesWhole_WithAnyOfItsFields_AndASensitiveOneMasksThemAll()
    {
        var who = Field("text", "who", "Who");
        var age = Field("number", "age", "Age");
        var party = Field("repeater", "party", "Party") with { Fields = [who, age], Sensitive = true };
        var owner = Field("textarea", "notes", "Notes");

        var recall = await WriteAsync(Form(party, owner), owners: [owner.Key], recalled: [who.Key]);

        var group = FieldOf(recall, party.Key);
        Assert.Multiple(() =>
        {
            Assert.That(group["alias"]!.GetValue<string>(), Is.EqualTo("party"), "a tag names its child through it");
            Assert.That(group["fields"]!.AsArray().Select(child => child!["alias"]!.GetValue<string>()), Is.EqualTo(new[] { "who", "age" }), "its rows show all its children");
            Assert.That(group["recall"]!.GetValue<string>(), Is.EqualTo("masked"));
            Assert.That(group["fields"]!.AsArray().Select(child => child!["recall"]!.GetValue<string>()), Is.EqualTo(new[] { "masked", "masked" }));
        });
    }

    [Test]
    public async Task TheLabelsOfTheAnswers_AreTheOwnOptions_TheListsForNoParent_AndEveryCountry()
    {
        var own = Field("dropdown", "own", "Own") with { Options = Options(("a", "Alpha"), ("b", string.Empty)) };
        var listed = Field("dropdown", "listed", "Listed") with { Options = new FieldOptions { Source = OptionSource.List, OptionList = List } };
        var missing = Field("dropdown", "missing", "Missing") with { Options = new FieldOptions { Source = OptionSource.List, OptionList = Guid.NewGuid() } };
        var cascading = Field("dropdown", "cascading", "Cascading") with { Options = new FieldOptions { Source = OptionSource.List, OptionList = List, Parent = listed.Key } };
        var country = Field("country", "country", "Country");
        var options = Substitute.For<IFormBuilderOptionService>();
        options.GetAsync(Arg.Is<FormField>(field => field.Key == listed.Key), Arg.Any<FormBuilderOptionContext>(), Arg.Any<CancellationToken>())
            .Returns(new FormBuilderResolvedOptions([new FormBuilderOption("x", "Ex"), new FormBuilderOption("y", null)], IsAvailable: true));
        options.GetAsync(Arg.Is<FormField>(field => field.Key == missing.Key), Arg.Any<FormBuilderOptionContext>(), Arg.Any<CancellationToken>())
            .Returns(FormBuilderResolvedOptions.Unavailable);
        var page = Guid.NewGuid();

        var recall = await WriteAsync(Form(own, listed, missing, cascading, country), owners: [], recalled: [own.Key, listed.Key, missing.Key, cascading.Key, country.Key], options: options, page: page, culture: "de-DE");

        Assert.Multiple(() =>
        {
            Assert.That(FieldOf(recall, own.Key)["options"]!.ToJsonString(), Is.EqualTo("""[{"value":"a","label":"Alpha"},{"value":"b","label":""}]"""), "an empty label shows the value in both runtimes");
            Assert.That(FieldOf(recall, listed.Key)["options"]!.ToJsonString(), Is.EqualTo("""[{"value":"x","label":"Ex"},{"value":"y","label":""}]"""));
            Assert.That(FieldOf(recall, missing.Key).ContainsKey("options"), Is.False, "a list the site cannot give shows the values");
            Assert.That(FieldOf(recall, cascading.Key).ContainsKey("options"), Is.False, "its options depend on its parent's answers (6.1c)");
            var countries = FieldOf(recall, country.Key)["options"]!.AsArray();
            Assert.That(countries.Select(each => each!["value"]!.GetValue<string>()), Is.Ordered.Using((IComparer<string>)StringComparer.Ordinal));
            Assert.That(countries.Single(each => each!["value"]!.GetValue<string>() == "DE")!["label"]!.GetValue<string>(), Is.EqualTo("Deutschland"));
        });
        await options.Received(1).GetAsync(Arg.Is<FormField>(field => field.Key == listed.Key), Arg.Is<FormBuilderOptionContext>(context => context.Culture == "de-DE" && context.Page == page), Arg.Any<CancellationToken>());
        await options.DidNotReceive().GetAsync(Arg.Is<FormField>(field => field.Key == cascading.Key), Arg.Any<FormBuilderOptionContext>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ARecalledField_GetsItsRows_ItsParts_AndShowsNothingWhenItsValueStaysOnTheServer()
    {
        var grid = Field("grid", "grid", "Grid") with
        {
            Options = Options(("1", "One")),
            Rows = [new GridRow { Key = Guid.NewGuid(), Label = new LocalizedText("Speed") }, new GridRow { Key = Guid.NewGuid() }],
        };
        var name = Field("name", "fullName", "Full name");
        var hidden = Field("hidden", "secret", "Secret");
        var heading = Field("heading", "title") with { Settings = SettingsObject.Parse("""{"text":{"":"Hi"},"level":2}""") };

        var recall = await WriteAsync(Form(grid, name, hidden, heading), owners: [], recalled: [grid.Key, name.Key, hidden.Key, heading.Key]);

        Assert.Multiple(() =>
        {
            Assert.That(FieldOf(recall, grid.Key)["rows"]!.ToJsonString(), Is.EqualTo($$"""[{"id":"{{grid.Rows[0].Key}}","label":"Speed"},{"id":"{{grid.Rows[1].Key}}","label":""}]"""));
            Assert.That(FieldOf(recall, name.Key)["parts"]!.GetValue<bool>(), Is.True);
            Assert.That(FieldOf(recall, grid.Key).ContainsKey("parts"), Is.False);
            Assert.That(FieldOf(recall, hidden.Key)["recall"]!.GetValue<string>(), Is.EqualTo("none"));
            Assert.That(FieldOf(recall, heading.Key)["holdsValue"]!.GetValue<bool>(), Is.False);
            Assert.That(FieldOf(recall, heading.Key).TryGetPropertyValue("kind", out var kind) && kind is null, Is.True, "a field that holds no value has a kind of null");
            Assert.That(FieldOf(recall, heading.Key).ContainsKey("recall"), Is.False);
        });
    }

    private static async Task<JsonNode> WriteAsync(FormDefinition form, Guid[] owners, Guid[] recalled, IFormBuilderOptionService? options = null, Guid? page = null, string culture = "en-GB")
    {
        var shown = VisitorForm.Map(PublicProjection.Create(form, TestFieldTypes.Registry), text => text.Default, TestFieldTypes.Registry);
        var buffer = new ArrayBufferWriter<byte>();
        await using (var writer = new Utf8JsonWriter(buffer))
        {
            await new MergeTagRecallForms(TestFieldTypes.Registry, options ?? Substitute.For<IFormBuilderOptionService>(), new MergeTagCultures())
                .WriteAsync(writer, shown, owners.ToHashSet(), recalled, MergeTagTestSite.Scope(form, culture: culture, page: page));
        }

        return JsonNode.Parse(buffer.WrittenSpan)!;
    }

    // Each element of the recall form in order, as its kind and key.
    private static IEnumerable<string> Ids(JsonNode recall)
    {
        foreach (var page in recall["pages"]!.AsArray())
        {
            yield return $"page {page!["id"]}";
            foreach (var group in page["groups"]!.AsArray())
            {
                yield return $"group {group!["id"]}";
                foreach (var field in group["fields"]!.AsArray())
                {
                    yield return $"field {field!["id"]}";
                }
            }
        }
    }

    private static JsonObject FieldOf(JsonNode recall, Guid key) =>
        recall["pages"]!.AsArray().SelectMany(page => page!["groups"]!.AsArray()).SelectMany(group => group!["fields"]!.AsArray())
            .Single(field => field!["id"]!.GetValue<string>() == key.ToString())!.AsObject();

    private static FormPage Page(params FormGroup[] groups) => new() { Key = NewKey(), Groups = groups };

    private static FormGroup Group(params FormField[] fields) => new() { Key = NewKey(), Fields = fields };
}
