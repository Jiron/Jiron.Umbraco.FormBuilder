using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Definitions.Projection;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.FieldTypes;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.MergeTags;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Logic;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Tests.Unit.Definitions;
using Jiron.Umbraco.FormBuilder.Tests.Unit.MergeTags;
using Jiron.Umbraco.FormBuilder.Tests.Unit.OptionLists;
using NSubstitute;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Forms;

/// <summary>
/// A published form as a visitor's page gets it in one culture (ADR-0084, ADR-0085): the site's required-marker style
/// and button labels where the form has none of its own (ADR-0095), its texts in the culture with the usual fallback, the dictionary's items read once for all the texts that name one, rich text sanitised wherever it
/// comes from, plain text left for the page to escape, and a lone surrogate replaced, so the form always writes; each
/// consent's text as the wording it shows, with its hash; and what the browser renders the visitor texts that render
/// with, or null when none does, and those texts for the page that renders them itself.
/// </summary>
[TestFixture]
public sealed class VisitorFormsTests
{
    private static readonly Guid Colour = Guid.Parse("30000000-0000-4000-8000-0000000000c1");
    private static readonly Guid Several = Guid.Parse("30000000-0000-4000-8000-0000000000c2");

    private TestUmbraco _umbraco = null!;
    private VisitorForms _forms = null!;
    private GeneralSettings _general = null!;
    private IFormBuilderSettingsService _settings = null!;

    [SetUp]
    public void CreateTheSite()
    {
        _umbraco = new TestUmbraco();
        _umbraco.Language("en-US", isDefault: true);
        _umbraco.Language("de-DE", fallback: "en-US");
        var dictionary = new MergeTagDictionary(_umbraco.Dictionary, _umbraco.Languages);
        var site = MergeTagTestSite.Create(umbraco: _umbraco);
        _general = new GeneralSettings();
        _settings = Substitute.For<IFormBuilderSettingsService>();
        _settings.GetAsync<GeneralSettings>(Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Task.FromResult(new SettingsSnapshot<GeneralSettings>(_general, 1, new HashSet<string>(), new Dictionary<string, SettingsLock>())));
        _forms = new VisitorForms(
            dictionary,
            new EditorHtml(),
            TestFieldTypes.Registry,
            site.Service,
            new MergeTagRecallForms(TestFieldTypes.Registry, new OptionListTestSite().Options()),
            new MergeTagCultures(),
            new ConsentWordings(site.Service, dictionary, new EditorHtml(), TestFieldTypes.Registry),
            _settings);
    }

    [Test]
    public async Task TheSitesStyle_StandsInForInherit_AndItsLabels_ForTheButtonsTheFormLabelsNotItself()
    {
        _umbraco.Item("Forms.Back", new Dictionary<string, string> { ["en-US"] = "Go back", ["de-DE"] = "Zur" + (char)0xFC + "ck bitte" });
        _general = new GeneralSettings
        {
            RequiredMarker = RequiredMarker.Required,
            SubmitLabel = new Dictionary<string, string> { [""] = "Send it" },
            NextLabel = new Dictionary<string, string> { [""] = "Onward", ["de"] = "Weiter so" },
            BackLabel = new Dictionary<string, string> { [""] = "#Forms.Back" },
        };
        var projection = Projection(page => page);
        var inheriting = projection with { Settings = projection.Settings with { RequiredMarker = RequiredMarker.Inherit, NextLabel = null, BackLabel = null } };

        var german = JsonNode.Parse(await FormAsync(inheriting, "de-DE"))!["settings"]!;
        var english = JsonNode.Parse(await FormAsync(inheriting, "en-US"))!["settings"]!;
        var own = JsonNode.Parse(await FormAsync(projection, "en-US"))!["settings"]!;

        Assert.Multiple(() =>
        {
            Assert.That(german["requiredMarker"]!.GetValue<string>(), Is.EqualTo("required"), "the site's style for inherit");
            Assert.That(german["submitLabel"]!.GetValue<string>(), Is.EqualTo("Anmeldung senden"), "the form's own label wins");
            Assert.That(german["nextLabel"]!.GetValue<string>(), Is.EqualTo("Weiter so"), "de-DE falls back to de");
            Assert.That(english["nextLabel"]!.GetValue<string>(), Is.EqualTo("Onward"));
            Assert.That(german["backLabel"]!.GetValue<string>(), Is.EqualTo("Zur" + (char)0xFC + "ck bitte"), "a label may name a dictionary item");
            Assert.That(english["backLabel"]!.GetValue<string>(), Is.EqualTo("Go back"));
            Assert.That(own["requiredMarker"]!.GetValue<string>(), Is.EqualTo("optional"), "a form's own style stands");
            Assert.That((own["nextLabel"]!.GetValue<string>(), own["backLabel"]!.GetValue<string>()), Is.EqualTo(("Next", "Back")), "and its own labels");
        });
        await _settings.Received(3).GetAsync<GeneralSettings>(MergeTagTestSite.SiteKey, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task TheSitesTheme_StandsInForAFormWithoutOne_AndAFormsOwnStands()
    {
        _general = new GeneralSettings { Theme = "bootstrap5" };
        var projection = Projection(page => page);
        var without = projection with { Settings = projection.Settings with { Theme = null } };
        var own = projection with { Settings = projection.Settings with { Theme = "unstyled" } };

        var taken = JsonNode.Parse(await FormAsync(without, "en-US"))!["settings"]!;
        var kept = JsonNode.Parse(await FormAsync(own, "en-US"))!["settings"]!;
        _general = new GeneralSettings();
        var neither = JsonNode.Parse(await FormAsync(without, "en-US"))!["settings"]!.AsObject();

        Assert.Multiple(() =>
        {
            Assert.That(taken["theme"]!.GetValue<string>(), Is.EqualTo("bootstrap5"), "the site's theme for a form without one");
            Assert.That(kept["theme"]!.GetValue<string>(), Is.EqualTo("unstyled"), "a form's own theme stands");
            Assert.That(neither.ContainsKey("theme"), Is.False, "neither names one, so the code's default renders");
        });
    }

    [Test]
    public async Task ASitesLabelThatIsNoLocalisedText_IsNone_SoThePageShowsThePackagesWord()
    {
        _general = new GeneralSettings { NextLabel = new Dictionary<string, string> { ["de"] = "Weiter so" } };
        var projection = Projection(page => page);

        var form = JsonNode.Parse(await FormAsync(projection with { Settings = projection.Settings with { NextLabel = null } }, "de-DE"))!;

        Assert.That(form["settings"]!.AsObject().ContainsKey("nextLabel"), Is.False, "texts without a default are no localised text");
    }

    [Test]
    public async Task TheTexts_AreTheCulturesOwn_WithTheDictionarysItems_ReadOnceForAll()
    {
        _umbraco.Item("Forms.Title", new Dictionary<string, string> { ["en-US"] = "Your details", ["de-DE"] = "Ihre Angaben" });
        _umbraco.Item("Forms.Intro", new Dictionary<string, string> { ["en-US"] = "<p>Hello<script>steal()</script></p>" });
        var projection = Projection(page => page with { Title = new LocalizedText("#Forms.Title"), Description = new LocalizedHtml("#Forms.Intro") });

        var german = JsonNode.Parse(await FormAsync(projection, "de-DE"))!;
        var english = JsonNode.Parse(await FormAsync(projection, "en-US"))!;

        Assert.Multiple(() =>
        {
            Assert.That(german["pages"]![0]!["title"]!.GetValue<string>(), Is.EqualTo("Ihre Angaben"));
            Assert.That(german["pages"]![0]!["description"]!.GetValue<string>(), Is.EqualTo("<p>Hello</p>"), "an item's rich text falls back to English and is sanitised too");
            Assert.That(german["settings"]!["submitLabel"]!.GetValue<string>(), Is.EqualTo("Anmeldung senden"), "de-DE falls back to de");
            Assert.That(english["pages"]![0]!["title"]!.GetValue<string>(), Is.EqualTo("Your details"));
            Assert.That(english["settings"]!["submitLabel"]!.GetValue<string>(), Is.EqualTo("Send registration"));
            Assert.That(_umbraco.DictionaryReads, Is.EqualTo(2), "one read per answer for every item it names");
        });
    }

    [Test]
    public async Task AFormWhoseTextsNameNoItem_ReadsNoDictionary()
    {
        _ = await FormAsync(Projection(page => page), "de-DE");

        Assert.That(_umbraco.DictionaryReads, Is.Zero);
    }

    [Test]
    public async Task AnItemWithoutAText_ShowsAsItIsWritten()
    {
        var shown = JsonNode.Parse(await FormAsync(Projection(page => page with { Title = new LocalizedText("#Forms.Missing") }), "en-US"))!;

        Assert.That(shown["pages"]![0]!["title"]!.GetValue<string>(), Is.EqualTo("#Forms.Missing"));
    }

    [Test]
    public async Task PlainText_IsLeftForThePageToEscape_AndRichTextIsSanitised()
    {
        var projection = Projection(page => page with
        {
            Title = new LocalizedText("<b>Bold</b> & co"),
            Description = new LocalizedHtml("""<p onclick="steal()">Hi <img src="x.png"></p>"""),
        });

        var shown = JsonNode.Parse(await FormAsync(projection, "en-US"))!;

        Assert.Multiple(() =>
        {
            Assert.That(shown["pages"]![0]!["title"]!.GetValue<string>(), Is.EqualTo("<b>Bold</b> & co"));
            Assert.That(shown["pages"]![0]!["description"]!.GetValue<string>(), Is.EqualTo("<p>Hi </p>"));
        });
    }

    [Test]
    public async Task ALoneSurrogateOfAnItem_IsReplaced_SoTheFormWrites()
    {
        _umbraco.Item("Forms.Broken", new Dictionary<string, string> { ["en-US"] = "A" + (char)0xD800 + "B" });

        var shown = JsonNode.Parse(await FormAsync(Projection(page => page with { Title = new LocalizedText("#Forms.Broken") }), "en-US"))!;

        Assert.That(shown["pages"]![0]!["title"]!.GetValue<string>(), Is.EqualTo("A" + (char)0xFFFD + "B"));
    }

    [Test]
    public async Task AFormWhoseTextsRenderAsTheyAreWritten_HasNoMergeTags_AndEachConsentShowsItsWordingWithItsHash()
    {
        var definition = DefinitionFixtures.Read(DefinitionFixtures.Full);

        var answer = await _forms.WriteAsync(PublicProjection.Create(definition, TestFieldTypes.Registry), MergeTagTestSite.Scope(definition, culture: "en-US"));

        var consent = Guid.Parse("30000000-0000-4000-8000-00000000000f");
        const string Wording = """I agree to the <a href="/privacy">privacy notice</a>.""";
        Assert.Multiple(() =>
        {
            Assert.That(System.Text.Encoding.UTF8.GetString(answer.MergeTags), Is.EqualTo("null"));
            Assert.That(JsonNode.Parse(answer.Consents)!.ToJsonString(), Is.EqualTo(new JsonObject { [consent.ToString()] = ConsentTextHash.For("en-US", Wording) }.ToJsonString()));
            Assert.That(FieldOf(JsonNode.Parse(answer.Form)!, consent)["settings"]!["text"]!.GetValue<string>(), Is.EqualTo(Wording));
        });
    }

    [Test]
    public async Task AVisitorTextThatRenders_ComesWithItsValues_AndTheRecallForm()
    {
        var definition = DefinitionFixtures.Read(DefinitionFixtures.Full);
        definition = definition with { Pages = [definition.Pages[0] with { Title = new LocalizedText("Hello from {{ form.name }}") }, .. definition.Pages.Skip(1)] };

        var shown = await _forms.ShowAsync(PublicProjection.Create(definition, TestFieldTypes.Registry), MergeTagTestSite.Scope(definition, culture: "de-DE"));
        var answer = shown.Answer;

        var mergeTags = JsonNode.Parse(answer.MergeTags)!;
        var page = definition.Pages[0].Key.ToString();
        Assert.Multiple(() =>
        {
            Assert.That(mergeTags["texts"]!.ToJsonString(), Is.EqualTo($$"""[{"owner":"{{page}}","text":"title","context":"text"}]"""));
            Assert.That(shown.Tagged, Is.EqualTo(new[] { new VisitorText(definition.Pages[0].Key, "title", false, "Hello from {{ form.name }}") }), "the page gets the same texts to render");
            Assert.That(mergeTags["values"]!["html"]!.AsObject(), Is.Empty);
            Assert.That(mergeTags["values"]!["text"]!.AsObject().Select(pair => (pair.Key, pair.Value!.GetValue<string>())), Is.EqualTo(new[] { ("form.name", "Contact <us>") }), "the context encodes it when it renders");
            Assert.That(mergeTags["recall"]!.ToJsonString(), Is.EqualTo($$"""{"pages":[{"id":"{{page}}","groups":[]}]}"""), "the owner's place alone");
            Assert.That(mergeTags.AsObject().Select(property => property.Key), Is.EqualTo(new[] { "texts", "values", "recall" }), "the words and formats are the answer's own");
        });
    }

    [Test]
    public async Task EveryForm_GetsTheCulturesFormatsAndWords_AndEachFieldsKinds_WhetherItCanBeRequired_AndWhoseTypeItIs()
    {
        var definition = DefinitionFixtures.Read(DefinitionFixtures.Full);
        var site = TestFieldTypes.Build(builder => builder.Append<ColourFieldType>());
        var grid = definition.Pages.SelectMany(page => page.Groups).SelectMany(group => group.Fields).Single(field => field.Type == "grid");
        definition = definition with
        {
            Pages =
            [
                definition.Pages[0] with
                {
                    Groups =
                    [
                        definition.Pages[0].Groups[0] with
                        {
                            Fields =
                            [
                                .. definition.Pages[0].Groups[0].Fields,
                                new FormField { Key = Colour, Type = ColourFieldType.Alias, Alias = "colour" },
                                new FormField { Key = Several, Type = "dropdown", Alias = "several", Settings = SettingsObject.Parse("""{"multiple":true}"""), Options = new FieldOptions { Items = [new FieldOption { Value = "a" }] } },
                            ],
                        },
                        .. definition.Pages[0].Groups.Skip(1),
                    ],
                },
                .. definition.Pages.Skip(1),
            ],
        };
        var forms = new VisitorForms(
            new MergeTagDictionary(_umbraco.Dictionary, _umbraco.Languages),
            new EditorHtml(),
            site,
            MergeTagTestSite.Create(umbraco: _umbraco).Service,
            new MergeTagRecallForms(site, new OptionListTestSite().Options()),
            new MergeTagCultures(),
            new ConsentWordings(MergeTagTestSite.Create(umbraco: _umbraco).Service, new MergeTagDictionary(_umbraco.Dictionary, _umbraco.Languages), new EditorHtml(), site),
            _settings);

        var answer = (await forms.ShowAsync(PublicProjection.Create(definition, site), MergeTagTestSite.Scope(definition, culture: "de-DE"))).Answer;

        var fields = JsonNode.Parse(answer.Fields)!.AsObject();
        string Of(string key) => fields[key]!.ToJsonString();
        Assert.Multiple(() =>
        {
            Assert.That(System.Text.Encoding.UTF8.GetString(answer.MergeTags), Is.EqualTo("null"), "no text holds a tag, and the formats and words come all the same");
            Assert.That(System.Text.Encoding.UTF8.GetString(answer.Words), Is.EqualTo("""{"yes":"Ja","no":"Nein"}"""));
            Assert.That(JsonNode.Parse(answer.Formats)!["months"]![2]!.GetValue<string>(), Is.EqualTo("M" + (char)0xE4 + "rz"));
            Assert.That(Of("30000000-0000-4000-8000-000000000001"), Is.EqualTo("""{"kind":"text","canBeRequired":true,"builtIn":true}"""), "a text");
            Assert.That(Of("30000000-0000-4000-8000-000000000005"), Is.EqualTo("""{"kind":"choices","canBeRequired":true,"builtIn":true}"""), "checkboxes");
            Assert.That(Of("30000000-0000-4000-8000-000000000006"), Is.EqualTo("""{"kind":"choice","canBeRequired":true,"builtIn":true}"""), "a dropdown of one");
            Assert.That(Of(Several.ToString("D")), Is.EqualTo("""{"kind":"choices","canBeRequired":true,"builtIn":true}"""), "a dropdown of several");
            Assert.That(Of(grid.Key.ToString("D")), Is.EqualTo("""{"rowKind":"choice","canBeRequired":true,"builtIn":true}"""), "a grid: its rows have a kind");
            Assert.That(Of("30000000-0000-4000-8000-00000000000a"), Is.EqualTo("""{"canBeRequired":false,"builtIn":true}"""), "a repeatable group");
            Assert.That(Of("30000000-0000-4000-8000-00000000000c"), Is.EqualTo("""{"kind":"number","canBeRequired":true,"builtIn":true}"""), "a child field");
            Assert.That(Of("30000000-0000-4000-8000-00000000000e"), Is.EqualTo("""{"canBeRequired":false,"builtIn":true}"""), "a heading");
            Assert.That(Of("30000000-0000-4000-8000-00000000000f"), Is.EqualTo("""{"kind":"checkbox","canBeRequired":true,"builtIn":true}"""), "a consent");
            Assert.That(Of(Colour.ToString("D")), Is.EqualTo("""{"kind":"text","canBeRequired":true,"builtIn":false}"""), "a site's own type");
        });
    }

    [Test]
    public async Task AConsentWhoseWordingRendersAsNothing_ShowsNothing_AndHasNoHash()
    {
        var definition = DefinitionFixtures.Read(DefinitionFixtures.Full);
        var consent = Guid.Parse("30000000-0000-4000-8000-00000000000f");
        definition = definition with
        {
            Pages = [.. definition.Pages.Select(page => page with
            {
                Groups = [.. page.Groups.Select(group => group with
                {
                    Fields = [.. group.Fields.Select(field => field.Key == consent ? field with { Settings = SettingsObject.Parse("""{"text":{"":"{{ page.name }}"}}""") } : field)],
                })],
            })],
        };

        var answer = await _forms.WriteAsync(PublicProjection.Create(definition, TestFieldTypes.Registry), MergeTagTestSite.Scope(definition, culture: "en-US"));

        Assert.Multiple(() =>
        {
            Assert.That(FieldOf(JsonNode.Parse(answer.Form)!, consent)["settings"]!["text"]!.GetValue<string>(), Is.Empty);
            Assert.That(System.Text.Encoding.UTF8.GetString(answer.Consents), Is.EqualTo("{}"));
        });
    }

    [Test]
    public async Task ASourcesValues_AreWrittenAsTheVectorsWriteThem_AndAConsentKeepsItsOtherSettings()
    {
        var site = MergeTagTestSite.Create(umbraco: _umbraco, sources: [new ShopSource()]);
        var forms = new VisitorForms(
            new MergeTagDictionary(_umbraco.Dictionary, _umbraco.Languages),
            new EditorHtml(),
            TestFieldTypes.Registry,
            site.Service,
            new MergeTagRecallForms(TestFieldTypes.Registry, new OptionListTestSite().Options()),
            new MergeTagCultures(),
            new ConsentWordings(site.Service, new MergeTagDictionary(_umbraco.Dictionary, _umbraco.Languages), new EditorHtml(), TestFieldTypes.Registry),
            _settings);
        var definition = DefinitionFixtures.Read(DefinitionFixtures.Full);
        definition = definition with { Pages = [definition.Pages[0] with { Title = new LocalizedText("{{ shop.list }} {{ shop.price }} {{ shop.opened }} {{ shop.plain }} {{ shop.none }}") }, .. definition.Pages.Skip(1)] };
        var consent = Guid.Parse("30000000-0000-4000-8000-00000000000f");
        var projection = PublicProjection.Create(definition, TestFieldTypes.Registry);
        projection = projection with
        {
            Pages = [.. projection.Pages.Select(page => page with
            {
                Groups = [.. page.Groups.Select(group => group with
                {
                    Fields = [.. group.Fields.Select(field => field.Key == consent ? field with { Settings = SettingsObject.Parse("""{"look":"card","text":{"":"Old"}}""") } : field)],
                })],
            })],
        };

        var answer = await forms.WriteAsync(projection, MergeTagTestSite.Scope(definition, culture: "en-US"));

        var values = JsonNode.Parse(answer.MergeTags)!["values"]!["text"]!;
        var settings = FieldOf(JsonNode.Parse(answer.Form)!, consent)["settings"]!.AsObject();
        Assert.Multiple(() =>
        {
            Assert.That(values.AsObject().Select(pair => pair.Key), Is.EqualTo(new[] { "shop.list", "shop.price", "shop.opened", "shop.plain", "shop.none" }));
            Assert.That(values["shop.none"]!.GetValue<string>(), Is.Empty, "a value of no items is the empty text");
            Assert.That(values["shop.list"]!.ToJsonString(), Is.EqualTo("""["a",{"text":"two","number":"2"}]"""));
            Assert.That(values["shop.price"]!.ToJsonString(), Is.EqualTo("""{"text":"1.50","number":"1.5"}"""));
            Assert.That(values["shop.opened"]!.ToJsonString(), Is.EqualTo("""{"text":"28 Oct","date":"2026-10-28"}"""));
            Assert.That(values["shop.plain"]!.GetValue<string>(), Is.EqualTo("x" + (char)0xFFFD + "y"), "a lone surrogate replaced, so the answer writes");
            Assert.That(settings.Select(pair => pair.Key), Is.EqualTo(new[] { "look", "text" }), "the wording in place of the text, the rest as it was");
            Assert.That(settings["look"]!.GetValue<string>(), Is.EqualTo("card"));
            Assert.That(settings["text"]!.GetValue<string>(), Is.EqualTo("""I agree to the <a href="/privacy">privacy notice</a>."""));
        });
    }

    private static JsonNode FieldOf(JsonNode form, Guid key) =>
        form["pages"]!.AsArray().SelectMany(page => page!["groups"]!.AsArray()).SelectMany(group => group!["fields"]!.AsArray())
            .Single(field => field!["key"]!.GetValue<string>() == key.ToString())!;

    // The visitor's form of a projection of the full form, on a page in a culture.
    private async Task<byte[]> FormAsync(PublicForm projection, string culture) =>
        (await _forms.WriteAsync(projection, MergeTagTestSite.Scope(DefinitionFixtures.Read(DefinitionFixtures.Full), culture: culture))).Form;

    // A site's source with a list, a number, a date, a text that holds a lone surrogate and a value of no items.
    private sealed class ShopSource : IFormBuilderMergeTagSource
    {
        public string Name => "shop";

        public bool IsPerVisitor => false;

        public ValueTask<MergeTagValue?> ResolveAsync(MergeTagSourceRequest request, CancellationToken cancellationToken) => ValueTask.FromResult<MergeTagValue?>(request.Path switch
        {
            "list" => MergeTagValue.ListOf([new MergeTagItem("a"), new MergeTagItem("two", Number: "2")]),
            "price" => MergeTagValue.Of(new MergeTagItem("1.50", Number: "1.5")),
            "opened" => MergeTagValue.Of(new MergeTagItem("28 Oct", Date: "2026-10-28")),
            "plain" => MergeTagValue.Of("x" + (char)0xD800 + "y"),
            "none" => new MergeTagValue([], IsList: false),
            _ => null,
        });
    }

    /// <summary>A site's own type that takes a text, which may be required.</summary>
    public sealed class ColourFieldType() : FormBuilderFieldTypeBase<NoFieldSettings>(new FieldTypeDeclaration
    {
        Alias = Alias,
        Name = "Colour",
        Icon = "icon-colorpicker",
        PaletteGroup = FieldPaletteGroup.Structure,
        HoldsValue = true,
        Storage = FieldStorageKind.Text,
        RuleKind = FieldKind.Text,
        Features = FieldFeatures.Label | FieldFeatures.Required | FieldFeatures.VisibilityRules,
        Messages = FieldMessageKinds.Required,
    })
    {
        /// <summary>The type's alias.</summary>
        public const string Alias = "acme.colour";
    }

    // The full form's projection with its first page changed.
    private static PublicForm Projection(Func<PublicPage, PublicPage> change)
    {
        var projection = PublicProjection.Create(DefinitionFixtures.Read(DefinitionFixtures.Full), TestFieldTypes.Registry);
        return projection with { Pages = [change(projection.Pages[0]), .. projection.Pages.Skip(1)] };
    }
}
