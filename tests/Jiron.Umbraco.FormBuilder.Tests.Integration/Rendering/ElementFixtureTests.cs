using System.Globalization;
using System.Net;
using System.Text.Json.Nodes;
using AngleSharp.Html.Parser;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Jiron.Umbraco.FormBuilder.Core.Themes;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Rendering;

/// <summary>
/// The whole forms the headless element is held to (ROADMAP 6.6e, ADR-0128): for each form, theme and culture, the
/// element endpoint's answer and the server's own markup for the same first render (the render endpoint's, the same
/// instance, theme and culture, on the same clock), in <c>tests/fixtures/rendering/element-&lt;language&gt;.json</c>
/// with each form's key and honeypot fixed, Umbraco's route token left out and the model data kept once, in the answer,
/// the server's script holding the same text. <c>packages/element</c>'s
/// <c>markup.fixture.test.ts</c> writes each answer and expects the server's markup without what only a post without
/// JavaScript uses, which each case names, and runs ADR-0015's contract on the sinks. The forms: a field of every
/// built-in type in each built-in theme and hidden by its rule, three pages with steps, marks and a titled group, two
/// pages with a bar and the form's own labels, texts that recall answers with a default that holds tags, and the
/// notices of a closed form, a form not open yet and a form for members. After a deliberate change, run with
/// <c>JFB_UPDATE_SNAPSHOTS=1</c>: it writes the files and fails, so the diff gets reviewed.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class ElementFixtureTests : RenderingTestBase
{
    private static readonly DateTimeOffset Now = new(2026, 6, 15, 9, 30, 0, TimeSpan.Zero);

    // The post-only parts of the server's markup, which the element leaves out (ADR-0128).
    private static readonly JsonObject PostOnly = new()
    {
        ["inputs"] = new JsonArray(FormPostFields.RouteToken, FormPostFields.Form, FormPostFields.Instance, FormPostFields.Revision, FormPostFields.Page),
        ["inputPrefixes"] = new JsonArray(FormPostFields.QueryPrefix),
        ["attributes"] = new JsonArray("action", "enctype", "formaction"),
    };

    // The names the contract gives a field's controls beside its alias (ADR-0015), which the contract check reads.
    private static readonly JsonObject Names = new()
    {
        ["nav"] = FormPostFields.Nav,
        ["consentPrefix"] = FormModels.ConsentPrefix,
        ["rowsPrefix"] = FormPostFields.RowsPrefix,
        ["orderPrefix"] = FormPostFields.OrderPrefix,
    };

    [TestCase("en")]
    [TestCase("de")]
    public async Task TheServersFirstRenders_AreTheFixtureTheElementIsHeldTo(string language)
    {
        GetRequiredService<TestClock>().SetNow(Now);
        var culture = language == "de" ? "de-DE" : "en-US";
        if (language == "de")
        {
            var languages = GetRequiredService<ILanguageService>();
            var english = (await languages.GetDefaultLanguageAsync())!;
            Assert.That((await languages.CreateAsync(new Language("de-DE", "German") { FallbackIsoCode = english.IsoCode }, Constants.Security.SuperUserKey)).Success, Is.True);
        }

        var forms = new (string Name, Func<FormDefinition, FormDefinition> Change, string[] Themes)[]
        {
            ("sink", BuiltInSink, [BuiltInThemes.DefaultAlias, "bootstrap5", "unstyled"]),
            ("sink-hidden", definition => Hidden(BuiltInSink(definition)), [BuiltInThemes.DefaultAlias]),
            ("paged", Paged, [BuiltInThemes.DefaultAlias, "bootstrap5"]),
            ("paged-bar", PagedBar, [BuiltInThemes.DefaultAlias]),
            ("recall", Recall, [BuiltInThemes.DefaultAlias]),
            ("closed", definition => Available(definition, new FormAvailabilitySettings { ClosesAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc) }), [BuiltInThemes.DefaultAlias]),
            ("opens", definition => Available(definition, new FormAvailabilitySettings { OpensAt = new DateTime(2026, 7, 1, 8, 0, 0, DateTimeKind.Utc) }), [BuiltInThemes.DefaultAlias]),
            ("members", definition => Available(definition, new FormAvailabilitySettings { Access = MemberAccess.Members }), [BuiltInThemes.DefaultAlias]),
        };

        var cases = new JsonArray();
        var at = 0;
        foreach (var (name, change, themes) in forms)
        {
            var fixedKey = K(0x10 + at++);
            var key = await PublishFormAsync("element" + Pascal(name), definition => Headless(change(definition)), NameField);
            foreach (var theme in themes)
            {
                var query = $"instance=jfb-1&culture={culture}&theme={theme}";
                using var rendered = await GetAsync($"{Root}/forms/{key}/render?{query}&page=/element/");
                using var element = await GetAsync($"{Root}/forms/{key}/element?{query}");
                var html = await rendered.Content.ReadAsStringAsync();
                var answer = await element.Content.ReadAsStringAsync();
                Assert.That((rendered.StatusCode, element.StatusCode), Is.EqualTo((HttpStatusCode.OK, HttpStatusCode.OK)), $"{name} {theme}: {html} {answer}");
                var classes = GetRequiredService<FormBuilderThemes>().Find(theme)!.Classes;
                cases.Add(new JsonObject
                {
                    ["name"] = theme == BuiltInThemes.DefaultAlias ? name : name + "-" + theme,
                    ["theme"] = theme,
                    ["culture"] = culture,
                    ["now"] = Now.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture),
                    ["themeClasses"] = new JsonObject(classes.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => KeyValuePair.Create<string, JsonNode?>(pair.Key, pair.Value))),
                    ["postOnly"] = PostOnly.DeepClone(),
                    ["names"] = Names.DeepClone(),
                    ["answer"] = WithoutVersions(JsonNode.Parse(Fixed(answer, key, fixedKey))!),
                    ["html"] = Fixed(await ServerMarkupAsync(html, JsonNode.Parse(answer)!["form"]!["modelData"]?.GetValue<string>()), key, fixedKey),
                });
            }
        }

        await MatchFixtureAsync($"element-{language}.json", cases, "the server's first renders changed");
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        var clock = new TestClock();
        builder.Services.AddSingleton(clock);
        builder.Services.AddUnique<TimeProvider>(clock);
    }

    // The form's root as the render endpoint wrote it, without Umbraco's route token, which is new on every render, and
    // with its model data's script emptied once it holds the answer's model data, which the fixture keeps.
    private static async Task<string> ServerMarkupAsync(string html, string? modelData)
    {
        var document = await new HtmlParser().ParseDocumentAsync(html);
        var root = document.Body!.FirstElementChild ?? throw new AssertionException($"no markup in {html}");
        foreach (var token in root.QuerySelectorAll($"input[name='{FormPostFields.RouteToken}']").ToList())
        {
            token.Remove();
        }

        if (root.QuerySelector("script[data-jfb-model]") is { } script)
        {
            Assert.That(script.TextContent, Is.EqualTo(modelData), "the element's model data is the page's script's text");
            script.TextContent = string.Empty;
        }

        return root.OuterHtml;
    }

    // A text with the form's key, and the honeypot's name that follows from it, fixed.
    private static string Fixed(string text, Guid key, Guid fixedKey) => text
        .Replace(FormHoneypot.NameOf(key, 1), FormHoneypot.NameOf(fixedKey, 1), StringComparison.Ordinal)
        .Replace(key.ToString("D"), fixedKey.ToString("D"), StringComparison.Ordinal)
        .Replace(key.ToString("N"), fixedKey.ToString("N"), StringComparison.Ordinal);

    // The answer with its assets' addresses without their content's version, which changes with every change to a
    // stylesheet or the runtime; the element's tests use addresses of their own.
    private static JsonNode WithoutVersions(JsonNode answer)
    {
        var assets = answer["assets"]!.AsObject();
        assets["styles"] = new JsonArray([.. assets["styles"]!.AsArray().Select(style => JsonValue.Create(Unversioned(style!.GetValue<string>())))]);
        assets["script"] = Unversioned(assets["script"]!.GetValue<string>());
        return answer;

        static string Unversioned(string address) => address.Split("?v=")[0] + "?v=0";
    }

    // The kitchen sink's built-in types: a site's own type renders through the site's own view, which the element
    // leaves to the site (ADR-0128).
    private static FormDefinition BuiltInSink(FormDefinition definition)
    {
        var sink = FormRenderingKitchenTests.SinkForm(definition);
        return sink with
        {
            Pages = [.. sink.Pages.Select(page => page with { Groups = [.. page.Groups.Select(group => group with { Fields = BuiltIn(group.Fields) })] })],
        };

        static List<FormField> BuiltIn(IEnumerable<FormField> fields) =>
            [.. fields.Where(field => field.Type != MoodFieldType.TypeAlias).Select(field => field with { Fields = BuiltIn(field.Fields) })];
    }

    // The sink with its trigger's default saying yes, so every rule hides its element on the first render.
    private static FormDefinition Hidden(FormDefinition sink) => sink with
    {
        Pages = [.. sink.Pages.Select(page => page with { Groups = [.. page.Groups.Select(group => group with { Fields = [.. group.Fields.Select(field => field.Alias == "hide" ? field with { DefaultValue = FieldDefaultValue.Of("yes") } : field)] })] })],
    };

    // Three pages with their steps and asterisks: a titled card group of two columns with help, a placeholder, affixes, a
    // counter and the email again; then numbers and a text area; then a required choice and a consent.
    private static FormDefinition Paged(FormDefinition definition) => definition with
    {
        Settings = definition.Settings with
        {
            Submission = definition.Settings.Submission with { Progress = ProgressIndicator.Steps },
            Appearance = definition.Settings.Appearance with { RequiredMarker = RequiredMarker.Required },
        },
        Pages =
        [
            new FormPage
            {
                Key = K(0x40),
                Title = new LocalizedText([new(string.Empty, "About you"), new("de", "Zu Ihnen")]),
                Description = new LocalizedHtml("<p>We ask only what we need.</p>"),
                Groups =
                [
                    new FormGroup
                    {
                        Key = K(0x41),
                        Title = new LocalizedText("Your name"),
                        Description = new LocalizedHtml("<p>As it is on your passport.</p>"),
                        Style = GroupStyle.Card,
                        Columns = 2,
                        Fields =
                        [
                            Field(K(0x42), "text", "first", "First name", """{"prefix":{"":"Dr"},"suffix":{"":"(given)"},"counter":true,"maxLength":40}""") with { Required = true, Help = new LocalizedText("Your given name."), Placeholder = new LocalizedText("Ada") },
                            Field(K(0x43), "email", "email", "Email", """{"confirm":true,"confirmLabel":{"":"Email again"}}""") with { Required = true },
                            Field(K(0x44), "phone", "phone", "Phone", null),
                        ],
                    },
                ],
            },
            new FormPage
            {
                Key = K(0x45),
                Title = new LocalizedText("Your stay"),
                Groups =
                [
                    new FormGroup
                    {
                        Key = K(0x46),
                        Fields =
                        [
                            Field(K(0x47), "number", "guests", "Guests", """{"min":"1","max":"40","step":"1","suffix":{"":"people"}}"""),
                            Field(K(0x48), "number", "budget", "Budget", """{"decimals":2,"min":"0","prefix":{"":"EUR"}}"""),
                            Field(K(0x49), "textarea", "about", "About you", """{"rows":3,"counter":true,"maxLength":200,"autoGrow":false}""") with { DefaultValue = FieldDefaultValue.Of("First line") },
                        ],
                    },
                ],
            },
            new FormPage
            {
                Key = K(0x4a),
                Groups =
                [
                    new FormGroup
                    {
                        Key = K(0x4b),
                        Fields =
                        [
                            Field(K(0x4c), "radio", "room", "Room", null) with { Required = true, Options = new FieldOptions { Items = [new FieldOption { Value = "single", Label = new LocalizedText("Single") }, new FieldOption { Value = "double", Label = new LocalizedText("Double"), Description = new LocalizedText("For two") }] } },
                            Field(K(0x4d), "consent", "terms", "Terms", """{"text":{"":"<p>I agree to the terms.</p>"}}""") with { Required = true },
                        ],
                    },
                ],
            },
        ],
    };

    // Two pages with a bar, the form's own labels for Send and Back, and no marks.
    private static FormDefinition PagedBar(FormDefinition definition) => definition with
    {
        Settings = definition.Settings with
        {
            Submission = definition.Settings.Submission with { Progress = ProgressIndicator.Bar, SubmitLabel = new LocalizedText("Book it"), BackLabel = new LocalizedText("Previous") },
            Appearance = definition.Settings.Appearance with { RequiredMarker = RequiredMarker.None },
        },
        Pages =
        [
            new FormPage { Key = K(0x50), Groups = [new FormGroup { Key = K(0x51), Fields = [NameField] }] },
            new FormPage { Key = K(0x52), Groups = [new FormGroup { Key = K(0x53), Fields = [Field(K(0x54), "text", "city", "City", null)] }] },
        ],
    };

    // Texts that recall the name as it is typed, a page title among them, and a default that holds a merge tag.
    private static FormDefinition Recall(FormDefinition definition) => definition with
    {
        Pages =
        [
            new FormPage
            {
                Key = K(0x60),
                Title = new LocalizedText("Hello {{ field.name }}"),
                Groups =
                [
                    new FormGroup
                    {
                        Key = K(0x61),
                        Fields =
                        [
                            NameField,
                            Field(K(0x62), "text", "greeting", "Thanks, {{ field.name }}", null) with { Help = new LocalizedText("We call you {{ field.name | default: \"friend\" }}.") },
                            Field(K(0x63), "text", "where", "Where", null) with { DefaultValue = FieldDefaultValue.Of("{{ form.name }}") },
                        ],
                    },
                ],
            },
        ],
    };

    // One page of the name field, whose keys are fixed, that shows a notice for its availability.
    private static FormDefinition Available(FormDefinition definition, FormAvailabilitySettings availability) => definition with
    {
        Settings = definition.Settings with { Availability = availability },
        Pages = [new FormPage { Key = K(0x70), Groups = [new FormGroup { Key = K(0x71), Fields = [NameField] }] }],
    };

    private static FormField Field(Guid key, string type, string alias, string label, string? settings) => new()
    {
        Key = key,
        Type = type,
        Alias = alias,
        Label = new LocalizedText(label),
        Settings = settings is null ? null : SettingsObject.Parse(settings),
    };

    private static string Pascal(string name) => string.Concat(name.Split('-').Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    private static Guid K(int number) => Guid.Parse($"30000000-0000-4000-8000-0000006f{number:x4}");
}
