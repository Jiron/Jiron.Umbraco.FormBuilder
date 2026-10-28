using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.OptionLists;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Jiron.Umbraco.FormBuilder.Tests.Unit.OptionLists;
using static Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags.MergeTagAnswerForms;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags;

/// <summary>
/// Answers to a choice whose options come from a library list or another source in <c>field.*</c> and
/// <c>entry.answers</c> (ADR-0066, ADR-0077): each chosen value shows the label the source resolves in the entry's
/// culture, with its dictionary item's text, as a field's own option shows its own; a value the source no longer offers,
/// and every value of a source that cannot give its options, shows as it is stored. A cascading source resolves for the
/// parent's answers the parent offers, a repeatable group's child row by row, and each source is asked once for a
/// rendering for each field and parent answers; two renderings that read one scope's labels at once both show the
/// options kept first.
/// </summary>
public sealed class ListAnswersTests
{
    private static readonly Guid Sizes = Guid.Parse("76000000-0000-4000-8000-000000000001");
    private static readonly Guid Missing = Guid.Parse("76000000-0000-4000-8000-000000000002");
    private static readonly Guid Page = Guid.Parse("76000000-0000-4000-8000-000000000003");

    // A library list of sizes with labels in English and German, one of them a dictionary item.
    private static OptionListTestSite Library(params IFormBuilderOptionSource[] sources)
    {
        var site = new OptionListTestSite(sources);
        site.Umbraco.Language("en-GB", isDefault: true);
        site.Umbraco.Language("de-DE");
        site.Umbraco.Language("de-CH", fallback: "de-DE");
        site.Umbraco.Item("Sizes.Large", new Dictionary<string, string> { ["en-GB"] = "Large", ["de-DE"] = "Gross" });
        site.Repository.Write(Sizes, OptionListKind.Manual, items: """
            [{"value":"s","label":{"":"Small","de":"Klein"}},{"value":"m","label":{"":"Medium","de":"Mittel"}},{"value":"l","label":{"":"#Sizes.Large"}},{"value":"xs"}]
            """);
        return site;
    }

    [TestCase("en-GB", "Medium|Small, Large|m|Size: Medium")]
    [TestCase("de-DE", "Mittel|Klein, Gross|m|Size: Mittel")]
    [TestCase("de-CH", "Mittel|Klein, Gross|m|Size: Mittel")]
    public async Task AChoiceFromAList_ShowsTheListsLabels_InTheEntrysCulture(string culture, string expected)
    {
        var size = Field("dropdown", "size", "Size") with { Options = FromList(Sizes) };
        var extras = Field("checkboxes", "extras", "Extras") with { Options = FromList(Sizes), IncludeInEmails = false };
        var form = Form(size, extras);
        var site = MergeTagTestSite.Create(options: Library().Options());

        var rendered = await site.RenderAsync("{{ field.size }}|{{ field.extras }}|{{ field.size.value }}|{{ entry.answers }}", Scope(form, Answers(Answer(size, "m"), Answer(extras, "s", "l")), culture));

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Text, Is.EqualTo(expected), "de-CH falls back to de; a label that names a dictionary item shows its text; the value stays the value");
            Assert.That(rendered.Problems, Is.Empty);
        });
    }

    [Test]
    public async Task AValueTheListNoLongerOffers_AndAListTheSiteLacks_ShowTheStoredValues()
    {
        var size = Field("dropdown", "size", "Size") with { Options = FromList(Sizes) };
        var gone = Field("dropdown", "gone", "Gone") with { Options = FromList(Missing) };
        var form = Form(size, gone);
        var site = MergeTagTestSite.Create(options: Library().Options());

        var rendered = await site.RenderAsync("{{ field.size }}|{{ field.gone }}|{{ entry.answers }}", Scope(form, Answers(Answer(size, "xxl"), Answer(gone, "s")), "de-DE"));

        Assert.That(rendered.Text, Is.EqualTo("xxl|s|Size: xxl\nGone: s"));
    }

    [Test]
    public async Task AnOptionWithoutALabel_ShowsItsValue()
    {
        var size = Field("dropdown", "size", "Size") with { Options = FromList(Sizes) };
        var site = MergeTagTestSite.Create(options: Library().Options());

        var rendered = await site.RenderAsync("{{ field.size }}", Scope(Form(size), Answers(Answer(size, "xs")), "en-GB"));

        Assert.That(rendered.Text, Is.EqualTo("xs"));
    }

    [Test]
    public async Task ACascadingSource_ResolvesForEachRowsParentAnswer_OnTheScopesPage_OnceForEachInARendering()
    {
        var towns = new TestOptionSource("towns", (request, _) => ValueTask.FromResult<IReadOnlyList<FormBuilderOption>>(
        [
            .. new[] { ("zurich", "Zurich", "CH"), ("berlin", "Berlin", "DE") }
                .Where(town => request.ParentValues.Contains(town.Item3))
                .Select(town => new FormBuilderOption(town.Item1, town.Item2)),
        ]));
        var country = Field("dropdown", "country", "Country") with { Options = Options(("CH", "Switzerland"), ("DE", "Germany")) };
        var town = Field("dropdown", "town", "Town") with
        {
            Options = new FieldOptions { Source = OptionSource.Code, Config = SettingsObject.Parse("""{"source":"towns"}"""), Parent = country.Key },
        };
        var stops = Field("repeater", "stops", "Stops") with { Fields = [country, town] };
        var form = Form(stops);
        var answers = Answers(InRow(country, 0, "CH"), InRow(town, 0, "zurich"), InRow(country, 1, "DE"), InRow(town, 1, "berlin"));
        var site = MergeTagTestSite.Create(options: Library(towns).Options());

        var rendered = await site.RenderAsync(
            "{{ field.stops.town }}|{{ field.town }}|{{ entry.answers }}",
            MergeTagTestSite.Scope(form: form, culture: "de-DE", answers: answers, page: Page));

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Text, Is.EqualTo("Zurich, Berlin|Zurich, Berlin|Stops:\n1. Country: Switzerland; Town: Zurich\n2. Country: Germany; Town: Berlin"));
            Assert.That(towns.Requests.Select(request => (string.Join(",", request.ParentValues), request.Culture, request.Page)), Is.EqualTo(new[] { ("CH", "de-DE", (Guid?)Page), ("DE", "de-DE", (Guid?)Page) }), "one request for each row's answer, for every tag");
        });
    }

    [Test]
    public async Task AChoiceTheRulesHid_OrWithoutAnAnswer_AsksItsSourceNothing()
    {
        var towns = new TestOptionSource("towns", new FormBuilderOption("zurich", "Zurich"));
        var hidden = Field("dropdown", "hidden", "Hidden") with { Options = new FieldOptions { Source = OptionSource.Code, Config = SettingsObject.Parse("""{"source":"towns"}""") } };
        var unanswered = Field("dropdown", "unanswered", "Unanswered") with { Options = new FieldOptions { Source = OptionSource.Code, Config = SettingsObject.Parse("""{"source":"towns"}""") } };
        var name = Field("text", "name", "Name");
        var site = MergeTagTestSite.Create(options: Library(towns).Options());

        var rendered = await site.RenderAsync(
            "{{ field.hidden }}|{{ field.unanswered }}|{{ entry.answers }}",
            Scope(Form(hidden, unanswered, name), new MergeTagAnswers([Answer(hidden, "zurich"), Answer(name, "Ada")], [hidden.Key])));

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Text, Is.EqualTo("||Name: Ada"));
            Assert.That(towns.Requests, Is.Empty);
        });
    }

    [Test]
    public async Task ASourcesLabels_AreReadOnlyForTheTagsThatShowItsAnswer_OnceForEveryTagOfARendering()
    {
        var towns = new TestOptionSource("towns", new FormBuilderOption("zurich", "Zurich"));
        var sizes = new TestOptionSource("sizes", new FormBuilderOption("m", "Medium"));
        var town = Field("dropdown", "town", "Town") with { Options = FromCode("towns") };
        var size = Field("dropdown", "size", "Size") with { Options = FromCode("sizes") };
        var form = Form(town, size);
        var answers = Answers(Answer(town, "zurich"), Answer(size, "m"));
        var site = MergeTagTestSite.Create(options: Library(towns, sizes).Options());
        (int Towns, int Sizes) Asked() => (towns.Requests.Count, sizes.Requests.Count);

        var values = await site.RenderAsync("{{ field.town.value }}|{{ field.town.label }}|{{ field.size.value }}", Scope(form, answers));
        var afterValues = Asked();
        var town3 = await site.RenderAsync("{{ field.town }}|{{ field.town }}|{{ field.town | upper }}", Scope(form, answers));
        var afterTowns = Asked();
        var all = await site.RenderAsync("{{ entry.answers }}|{{ field.size }}", Scope(form, answers));

        Assert.Multiple(() =>
        {
            Assert.That((values.Text, town3.Text, all.Text), Is.EqualTo(("zurich|Town|m", "Zurich|Zurich|ZURICH", "Town: Zurich\nSize: Medium|Medium")));
            Assert.That(afterValues, Is.EqualTo((0, 0)), "a value or a label shows no option's label");
            Assert.That(afterTowns, Is.EqualTo((1, 0)), "the town's three tags in one rendering, one request; the size, which none of them shows, none");
            Assert.That(Asked(), Is.EqualTo((2, 1)), "every answer once, and the size's tag after them nothing more");
        });
    }

    [Test]
    public async Task TwoRenderingsReadingOneScopesLabelsAtOnce_BothShowTheOptionsKeptFirst()
    {
        var calls = 0;
        var bothAsked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource[] answered = [new(TaskCreationOptions.RunContinuationsAsynchronously), new(TaskCreationOptions.RunContinuationsAsynchronously)];
        var towns = new TestOptionSource("towns", async (_, cancellationToken) =>
        {
            var call = Interlocked.Increment(ref calls);
            if (call == 2)
            {
                bothAsked.SetResult();
            }

            await answered[call - 1].Task.WaitAsync(cancellationToken);
            return [new FormBuilderOption("zurich", $"Zurich {call}")];
        });
        var town = Field("dropdown", "town", "Town") with { Options = FromCode("towns") };
        var scope = Scope(Form(town), Answers(Answer(town, "zurich")));
        // Timers that never fire on their own, so a slow run waits for the source rather than timing it out.
        var site = MergeTagTestSite.Create(options: Library(towns).Options(new ManualTimers()));

        var renderings = new[] { site.RenderAsync("{{ field.town }}", scope), site.RenderAsync("{{ field.town }}", scope) };
        await bothAsked.Task.WaitAsync(TimeSpan.FromSeconds(10));
        answered[1].SetResult();
        await Task.WhenAny(renderings).WaitAsync(TimeSpan.FromSeconds(10));
        answered[0].SetResult();
        var shown = await Task.WhenAll(renderings).WaitAsync(TimeSpan.FromSeconds(10));
        var later = await site.RenderAsync("{{ field.town }}", scope);

        Assert.Multiple(() =>
        {
            Assert.That(shown.Select(rendering => rendering.Text), Is.EqualTo(new[] { "Zurich 2", "Zurich 2" }), "the second answer came first, so it stands, for the slower rendering too");
            Assert.That((later.Text, towns.Requests.Count), Is.EqualTo(("Zurich 2", 2)), "and later tags of the scope read it without asking again");
        });
    }

    private static FieldOptions FromList(Guid list) => new() { Source = OptionSource.List, OptionList = list };

    private static FieldOptions FromCode(string source) => new() { Source = OptionSource.Code, Config = SettingsObject.Parse($$"""{"source":"{{source}}"}""") };
}
