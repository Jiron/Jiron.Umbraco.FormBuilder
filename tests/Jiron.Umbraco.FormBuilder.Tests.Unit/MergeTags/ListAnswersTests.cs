using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
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
/// and every value of a source that cannot give its options, shows as it is stored; a grid's columns from a list show
/// their labels too. A cascading source resolves for the parent's answers the parent offers, a repeatable group's child
/// row by row (for every answer of a parent outside its group), and each source is asked once for a rendering for each
/// field and parent answers; two renderings that read one scope's labels at once both show the options kept first.
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

    [Test]
    public async Task TwoRowsWhoseParentsSourcesShareAValue_EachShowTheirOwnLabel()
    {
        var codes = new TestOptionSource("codes", (request, _) => ValueTask.FromResult<IReadOnlyList<FormBuilderOption>>(
        [
            .. new[] { ("1000", "Lausanne", "CH"), ("1000", "Brussels", "BE") }
                .Where(code => request.ParentValues.Contains(code.Item3))
                .Select(code => new FormBuilderOption(code.Item1, code.Item2)),
        ]));
        var country = Field("dropdown", "country", "Country") with { Options = Options(("CH", "Switzerland"), ("BE", "Belgium")) };
        var code = Field("dropdown", "code", "Postal code") with { Options = FromCode("codes", parent: country) };
        var stops = Field("repeater", "stops", "Stops") with { Fields = [country, code] };
        var answers = Answers(InRow(country, 0, "CH"), InRow(code, 0, "1000"), InRow(country, 1, "BE"), InRow(code, 1, "1000"));
        var site = MergeTagTestSite.Create(options: Library(codes).Options());

        var rendered = await site.RenderAsync("{{ field.code }}", Scope(Form(stops), answers));

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Text, Is.EqualTo("Lausanne, Brussels"), "a source's options keep a value once, so both rows' parents at once would give one label");
            Assert.That(codes.Requests.Select(request => string.Join(",", request.ParentValues)), Is.EqualTo(new[] { "CH", "BE" }), "each row's own answer, as the check asks, which the lists' cache can keep");
        });
    }

    [TestCase(false, Description = "by its own rule")]
    [TestCase(true, Description = "by its repeatable group's")]
    public async Task AParentTheRulesHid_GivesItsChildNoParentAnswers(bool byItsGroup)
    {
        // Answers as any caller of the public answers may give them: the submission's reader leaves out a hidden group's
        // children, but the hidden keys alone decide here.
        var towns = Towns();
        var country = Field("dropdown", "country", "Country") with { Options = Options(("CH", "Switzerland"), ("DE", "Germany")) };
        var stops = Field("repeater", "stops", "Stops") with { Fields = [country] };
        var town = Field("dropdown", "town", "Town") with { Options = FromCode("towns", parent: country) };
        var answers = new MergeTagAnswers([InRow(country, 0, "CH"), Answer(town, "zurich")], [byItsGroup ? stops.Key : country.Key]);
        var site = MergeTagTestSite.Create(options: Library(towns).Options());

        var rendered = await site.RenderAsync("{{ field.town }}", Scope(Form(stops, town), answers));

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Text, Is.EqualTo("zurich"), "no town is an option without a country");
            Assert.That(towns.Requests.Select(request => string.Join(",", request.ParentValues)), Is.EqualTo(new[] { string.Empty }), "a hidden answer counts for nothing");
        });
    }

    [Test]
    public async Task AParentTheRulesHidInOneRow_HasNoAnswerThere_SoThatRowsChildHasNoParentAnswer()
    {
        // The submission's reader leaves out what the rules hid in a row, so that row has no answer of the parent at all.
        var towns = Towns();
        var country = Field("dropdown", "country", "Country") with { Options = Options(("CH", "Switzerland"), ("DE", "Germany")) };
        var town = Field("dropdown", "town", "Town") with { Options = FromCode("towns", parent: country) };
        var stops = Field("repeater", "stops", "Stops") with { Fields = [country, town] };
        var site = MergeTagTestSite.Create(options: Library(towns).Options());

        var rendered = await site.RenderAsync("{{ field.town }}", Scope(Form(stops), Answers(InRow(country, 0, "CH"), InRow(town, 0, "zurich"), InRow(town, 1, "zurich"))));

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Text, Is.EqualTo("Zurich, zurich"), "no town is an option without a country");
            Assert.That(towns.Requests.Select(request => string.Join(",", request.ParentValues)), Is.EqualTo(new[] { "CH", string.Empty }));
        });
    }

    [TestCase(false, "Zurich, Zurich", "CH", Description = "a parent outside any repeatable group")]
    [TestCase(true, "Zurich, Berlin", "CH,DE", Description = "a parent in another repeatable group")]
    public async Task ARowsChildWhoseParentIsOutsideItsGroup_IsAskedForEveryAnswerOfTheParent_OnceForAllItsRows(bool inAnotherGroup, string expected, string asked)
    {
        var towns = Towns();
        var country = Field("dropdown", "country", "Country") with { Options = Options(("CH", "Switzerland"), ("DE", "Germany")) };
        var town = Field("dropdown", "town", "Town") with { Options = FromCode("towns", parent: country) };
        var stops = Field("repeater", "stops", "Stops") with { Fields = [town] };
        var people = Field("repeater", "people", "People") with { Fields = [country] };
        MergeTagAnswer[] countries = inAnotherGroup ? [InRow(country, 0, "CH"), InRow(country, 1, "DE")] : [Answer(country, "CH")];
        var answers = Answers([.. countries, InRow(town, 0, "zurich"), InRow(town, 1, inAnotherGroup ? "berlin" : "zurich")]);
        var site = MergeTagTestSite.Create(options: Library(towns).Options());

        var rendered = await site.RenderAsync("{{ field.town }}", Scope(inAnotherGroup ? Form(people, stops) : Form(country, stops), answers));

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Text, Is.EqualTo(expected));
            Assert.That(towns.Requests.Select(request => string.Join(",", request.ParentValues)), Is.EqualTo(new[] { asked }), "never the parent's answer in the child's row, which is no row of the parent's");
        });
    }

    [Test]
    public async Task AChainThroughARowAndOutOfIt_AsksTheRowsParentInItsRow_AndTheOuterOneOnceForAllRows()
    {
        var towns = Towns();
        var countries = new TestOptionSource("countries", (request, _) =>
            ValueTask.FromResult<IReadOnlyList<FormBuilderOption>>(request.ParentValues.Contains("alps") ? [new FormBuilderOption("CH"), new FormBuilderOption("DE")] : []));
        var region = Field("radio", "region", "Region") with { Options = Options(("alps", "Alps"), ("coast", "Coast")) };
        var country = Field("dropdown", "country", "Country") with { Options = FromCode("countries", parent: region) };
        var town = Field("dropdown", "town", "Town") with { Options = FromCode("towns", parent: country) };
        var stops = Field("repeater", "stops", "Stops") with { Fields = [country, town] };
        var answers = Answers(Answer(region, "alps"), InRow(country, 0, "CH"), InRow(town, 0, "zurich"), InRow(country, 1, "DE"), InRow(town, 1, "berlin"));
        var site = MergeTagTestSite.Create(options: Library(towns, countries).Options());

        var rendered = await site.RenderAsync("{{ field.town }}", Scope(Form(region, stops), answers));

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Text, Is.EqualTo("Zurich, Berlin"));
            Assert.That(countries.Requests.Select(request => string.Join(",", request.ParentValues)), Is.EqualTo(new[] { "alps" }), "the region, outside the group, for every row at once");
            Assert.That(towns.Requests.Select(request => string.Join(",", request.ParentValues)), Is.EqualTo(new[] { "CH", "DE" }), "each row's own country");
        });
    }

    [Test]
    public async Task AGridsColumnsFromAList_ShowTheListsLabels_RowByRow()
    {
        var service = NewKey();
        var speed = NewKey();
        var rating = Field("grid", "rating", "Rating") with
        {
            Options = FromList(Sizes),
            Rows = [new GridRow { Key = service, Label = new LocalizedText("Service") }, new GridRow { Key = speed, Label = new LocalizedText("Speed") }],
        };
        var site = MergeTagTestSite.Create(options: Library().Options());

        var rendered = await site.RenderAsync("{{ field.rating }}", Scope(Form(rating), Answers(new MergeTagAnswer(rating.Key, [new FieldValue("m", service), new FieldValue("l", speed)])), "de-DE"));

        Assert.That(rendered.Text, Is.EqualTo("Service: Mittel, Speed: Gross"), "a grid's values name their rows, and its columns are the list's options");
    }

    [Test]
    public async Task AParentFromASource_GivesItsChildsSourceOnlyTheOptionsItResolved()
    {
        var towns = Towns();
        var regions = new TestOptionSource("regions", (request, _) =>
            ValueTask.FromResult<IReadOnlyList<FormBuilderOption>>(request.ParentValues.Contains("alps") ? [new FormBuilderOption("CH"), new FormBuilderOption("AT")] : [new FormBuilderOption("NL")]));
        var region = Field("radio", "region", "Region") with { Options = Options(("alps", "Alps"), ("coast", "Coast")) };
        var stop = Field("checkboxes", "stop", "Stop") with { Options = FromCode("regions", parent: region) };
        var place = Field("dropdown", "place", "Place") with { Options = FromCode("towns", parent: stop) };
        var site = MergeTagTestSite.Create(options: Library(towns, regions).Options());

        var rendered = await site.RenderAsync("{{ field.place }}", Scope(Form(region, stop, place), Answers(Answer(region, "alps"), Answer(stop, "CH", "NL"), Answer(place, "zurich"))));

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Text, Is.EqualTo("Zurich"));
            Assert.That(regions.Requests.Select(request => string.Join(",", request.ParentValues)), Is.EqualTo(new[] { "alps" }), "the stop's options, for the region");
            Assert.That(towns.Requests.Select(request => string.Join(",", request.ParentValues)), Is.EqualTo(new[] { "CH" }), "NL is no stop in the Alps");
        });
    }

    [Test]
    public async Task AParentWhoseOptionsAreUnknown_LeavesItsChildShowingItsValues_AskingNothing()
    {
        var towns = Towns();
        var country = Field("dropdown", "country", "Country") with { Options = FromList(Missing) };
        var town = Field("dropdown", "town", "Town") with { Options = FromCode("towns", parent: country) };
        var site = MergeTagTestSite.Create(options: Library(towns).Options());

        var rendered = await site.RenderAsync("{{ field.town }}", Scope(Form(country, town), Answers(Answer(country, "CH"), Answer(town, "zurich"))));

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Text, Is.EqualTo("zurich"));
            Assert.That(towns.Requests, Is.Empty, "no parent answer can be held to options nobody knows");
        });
    }

    [Test]
    public async Task AChainOfParentsLeadingBack_IsCutWhereItMeetsItself()
    {
        var letters = new TestOptionSource("letters", (request, _) =>
            ValueTask.FromResult<IReadOnlyList<FormBuilderOption>>(request.ParentValues.Contains("x") ? [new FormBuilderOption("y", "Why")] : [new FormBuilderOption("x", "Ex")]));
        var first = Field("dropdown", "first", "First");
        var second = Field("dropdown", "second", "Second") with { Options = FromCode("letters", parent: first) };
        first = first with { Options = FromCode("letters", parent: second) };
        var site = MergeTagTestSite.Create(options: Library(letters).Options());

        var rendered = await site.RenderAsync("{{ field.first }}", Scope(Form(first, second), Answers(Answer(first, "y"), Answer(second, "x"))));

        Assert.Multiple(() =>
        {
            Assert.That(rendered.Text, Is.EqualTo("Why"), "the second's options for no parent offer x, so the first's are asked for it");
            Assert.That(letters.Requests.Select(request => string.Join(",", request.ParentValues)), Is.EqualTo(new[] { string.Empty, "x" }));
        });
    }

    // The towns of a country, as a site's code source gives them, filtered by the parent's answers itself.
    private static TestOptionSource Towns() => new("towns", (request, _) => ValueTask.FromResult<IReadOnlyList<FormBuilderOption>>(
    [
        .. new[] { ("zurich", "Zurich", "CH"), ("berlin", "Berlin", "DE") }
            .Where(town => request.ParentValues.Contains(town.Item3))
            .Select(town => new FormBuilderOption(town.Item1, town.Item2)),
    ]));

    private static FieldOptions FromCode(string source, FormField parent) => FromCode(source) with { Parent = parent.Key };

    private static FieldOptions FromList(Guid list) => new() { Source = OptionSource.List, OptionList = list };

    private static FieldOptions FromCode(string source) => new() { Source = OptionSource.Code, Config = SettingsObject.Parse($$"""{"source":"{{source}}"}""") };
}
