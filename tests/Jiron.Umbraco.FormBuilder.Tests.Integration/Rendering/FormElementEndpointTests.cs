using System.Net;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Composing;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Themes;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Json.Schema;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Rendering;

/// <summary>
/// <c>GET forms/{key}/element</c> (ROADMAP 6.6d, ADR-0128): the view model of a published form's first render as the
/// headless element writes it, for the instance it asks for, in the page's culture and the theme asked for, with the
/// definition endpoint's answer as its model data and each control named by its kind; without the view paths, the
/// classes below the form or what only a post without JavaScript uses; a form for members shows a visitor its notice
/// and a member the form, a closed form its notice; <c>no-store</c>; and its refusals. Every answer fits the schema in
/// the element's package.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class FormElementEndpointTests : RenderingTestBase
{
    // The element's schema, which the unit tests write from the view models' metadata.
    private static readonly Lazy<JsonSchema> Schema = new(() => JsonSchema.FromText(
        File.ReadAllText(RepositoryRoot.Path(typeof(FormElementEndpointTests).Assembly, "src", "Jiron.Umbraco.FormBuilder.Web", "Client", "packages", "element", "src", "answer.schema.json")),
        new BuildOptions { SchemaRegistry = new SchemaRegistry() }));

    // The properties the element never gets: the view paths, and what only a post without JavaScript uses.
    private static readonly string[] Withheld = ["views", "controlView", "fieldView", "action", "encodingType", "postValues"];

    [Test]
    public async Task AMultiPageForm_AnswersItsFirstRender_BelowTheInstanceAskedFor_WithTheDefinitionAsItsModelData()
    {
        var key = await PublishFormAsync("elementPaged", definition => Headless(Paged(definition)));
        _ = await PagesAsync("element");
        var served = (await PageAsync("element")).Document.QuerySelector("form.jfb-form");

        var (status, cache, answer) = await ElementAsync(key, "instance=jfb-3");
        using var definition = await GetAsync($"{Root}/forms/{key}");
        var form = answer!["form"]!;
        var pages = form["pages"]!.AsArray();
        var fields = Descendants(form).Where(node => node["labelElement"] is not null).ToList();

        Assert.Multiple(async () =>
        {
            Assert.That((status, cache), Is.EqualTo((HttpStatusCode.OK, "no-store")));
            Assert.That(answer["contract"]!.GetValue<int>(), Is.EqualTo(1));
            Assert.That((form["id"]!.GetValue<string>(), form["display"]!.GetValue<string>(), form["revision"]!.GetValue<int>()), Is.EqualTo(("jfb-3", "form", 1)));
            Assert.That(pages.Select(each => each!["isCurrent"]!.GetValue<bool>()), Is.EqualTo(new[] { true, false, false }), "the first page is the visitor's");
            Assert.That(form["navigation"]!["next"]!["value"]!.GetValue<string>(), Is.EqualTo("next"));
            Assert.That(fields.Select(field => field["id"]!.GetValue<string>()), Has.All.StartsWith("jfb-3-"), "every id below the instance asked for");
            Assert.That(JsonNode.DeepEquals(JsonNode.Parse(form["modelData"]!.GetValue<string>()), JsonNode.Parse(await definition.Content.ReadAsStringAsync())), Is.True, "the model data is the definition endpoint's answer");
            Assert.That(form["modelData"]!.GetValue<string>(), Does.Not.Contain("<"), "escaped as the page's script holds it");
            Assert.That(form["honeypot"]!["name"]!.GetValue<string>(), Is.EqualTo(served!.QuerySelector(".jfb-hp input")!.GetAttribute("name")), "the honeypot the page's markup has");
            Assert.That(Names(answer).Intersect(Withheld), Is.Empty, "no view path and nothing only a post without JavaScript uses");
            Assert.That(Paths(answer, "classes"), Is.EqualTo(new[] { "form.classes" }), "the classes once, at the form");
            Assert.That(form["classes"]!["control"]!.GetValue<string>(), Is.EqualTo("jfb-control"));
            Assert.That(answer["assets"]!["styles"]!.AsArray().Select(style => style!.GetValue<string>()), Has.Some.StartsWith("/jfb/assets/themes/base.css?v="));
            Assert.That(answer["assets"]!["script"]!.GetValue<string>(), Does.StartWith("/jfb/assets/runtime/jfb-runtime.js?v="));
        });
    }

    [Test]
    public async Task AFieldOfEveryBuiltInType_IsWrittenWithItsControlsKind()
    {
        var key = await PublishFormAsync("elementSink", definition => Headless(FormRenderingKitchenTests.SinkForm(definition)));

        var (status, _, answer) = await ElementAsync(key, "instance=jfb-1");
        var fields = Descendants(answer!["form"]!).Where(node => node["labelElement"] is not null).ToList();
        var top = answer["form"]!["pages"]![0]!["groups"]!.AsArray().SelectMany(group => group!["fields"]!.AsArray()).Where(field => field!["alias"]?.GetValue<string>() != "hide").ToList();
        var kinds = top.ToDictionary(field => field!["type"]!.GetValue<string>(), field => field!["control"]?["kind"]?.GetValue<string>());
        var row = top.Single(field => field!["type"]!.GetValue<string>() == "repeater")!["control"]!["rows"]![0]!["fields"]!.AsArray();

        Assert.Multiple(() =>
        {
            Assert.That(status, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(kinds, Is.EquivalentTo(new Dictionary<string, string?>
            {
                ["text"] = "text",
                ["textarea"] = "textArea",
                ["email"] = "text",
                ["number"] = "number",
                ["phone"] = "text",
                ["url"] = "text",
                ["date"] = "date",
                ["time"] = "date",
                ["datetime"] = "date",
                ["dropdown"] = "select",
                ["radio"] = "choices",
                ["checkboxes"] = "choices",
                ["grid"] = "grid",
                ["ranking"] = "ranking",
                ["checkbox"] = "singleChoice",
                ["yesno"] = "choices",
                ["consent"] = "singleChoice",
                ["rating"] = "choices",
                ["scale"] = "choices",
                ["slider"] = "slider",
                ["signature"] = "signature",
                ["name"] = "parts",
                ["address"] = "parts",
                ["country"] = "select",
                ["file"] = "file",
                ["calculation"] = "calculation",
                ["repeater"] = "repeater",
                ["heading"] = "heading",
                ["paragraph"] = "textBlock",
                ["divider"] = null,
                ["image"] = "image",
                ["captcha"] = "captcha",
                [MoodFieldType.TypeAlias] = "custom",
            }), "a hidden field renders nothing, a divider has no control");
            Assert.That(row.Select(field => field!["type"]!.GetValue<string>()), Does.Contain("text").And.Contain("grid"), "a row's fields with their own controls");
            Assert.That(row.Select(field => field!["id"]!.GetValue<string>()), Has.All.StartsWith("jfb-1-"));
            Assert.That(fields.Select(field => field["control"]?["kind"]?.GetValue<string>()), Has.None.EqualTo(string.Empty));
            Assert.That(Names(answer).Intersect(Withheld), Is.Empty, "a ranking's moves and a row's buttons without their formaction");
            Assert.That(top.Single(field => field!["type"]!.GetValue<string>() == "consent")!["control"]!["label"]!.GetValue<string>(), Is.EqualTo("<p>I agree to the terms.</p>"), "a rich text as its HTML");
            Assert.That(top.Single(field => field!["type"]!.GetValue<string>() == "text")!["labelElement"]!.GetValue<string>(), Is.EqualTo("label"), "an enum in camel case");
        });
    }

    [Test]
    public async Task AFormForMembers_ShowsAVisitorItsNotice_AndAMemberTheForm()
    {
        var key = await PublishFormAsync("elementMembers", definition => Headless(definition) with { Settings = Headless(definition).Settings with { Availability = definition.Settings.Availability with { Access = MemberAccess.Members } } });

        var (_, visitorCache, visitor) = await ElementAsync(key, "instance=jfb-1");
        await SignInMemberAsync("element-member@example.com");
        var (_, _, member) = await ElementAsync(key, "instance=jfb-1");

        Assert.Multiple(() =>
        {
            Assert.That((visitor!["form"]!["display"]!.GetValue<string>(), visitor["form"]!["notice"]!["kind"]!.GetValue<string>(), visitorCache), Is.EqualTo(("notice", "signIn", "no-store")));
            Assert.That(visitor["form"]!["pages"]?.AsArray(), Is.Null.Or.Empty, "no field for a visitor the form does not admit");
            Assert.That(visitor["form"]!["modelData"]?.ToString(), Is.Null.Or.Empty, "nor what the runtime would read of them");
            Assert.That(member!["form"]!["modelData"]?.ToString(), Is.Not.Null.And.Not.Empty, "which a member it admits gets");
            Assert.That(member!["form"]!["display"]!.GetValue<string>(), Is.EqualTo("form"));
        });
    }

    [Test]
    public async Task AClosedForm_AnswersItsNotice()
    {
        var key = await PublishFormAsync("elementClosed", definition => Headless(definition) with { Settings = Headless(definition).Settings with { Availability = definition.Settings.Availability with { ClosesAt = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc) } } });

        var (status, _, answer) = await ElementAsync(key, "instance=jfb-1");

        Assert.That((status, answer!["form"]!["display"]!.GetValue<string>(), answer["form"]!["notice"]!["kind"]!.GetValue<string>(), answer["form"]!["notice"]!["heading"]!.GetValue<string>()), Is.EqualTo((HttpStatusCode.OK, "notice", "closed", "This form is closed")));
    }

    [Test]
    public async Task TheCultureAndTheTheme_AreTheOnesAskedFor_AndTheContextValuesTheOnesTheFormLists()
    {
        var key = await PublishFormAsync("elementCulture", definition => Headless(definition) with { Settings = Headless(definition).Settings with { Developer = Headless(definition).Settings.Developer with { ContextValues = ["campaign"] } } });
        var content = (await CulturePagesAsync("de-DE", "German", "elementGerman"))["elementGerman"];

        var (_, _, german) = await ElementAsync(key, $"instance=jfb-1&culture=de-DE&contentKey={content}");
        var (_, _, english) = await ElementAsync(key, "instance=jfb-1");
        var (_, _, bootstrap) = await ElementAsync(key, "instance=jfb-1&theme=bootstrap5");
        var (_, _, unknown) = await ElementAsync(key, "instance=jfb-1&theme=a-theme-the-site-lacks");
        var (_, _, given) = await ElementAsync(key, "instance=jfb-1&context-Campaign=spring&context-other=no");

        Assert.Multiple(() =>
        {
            Assert.That((german!["form"]!["culture"]!.GetValue<string>(), FirstField(german)["label"]!.GetValue<string>()), Is.EqualTo(("de-DE", "Name (de)")));
            Assert.That((english!["form"]!["culture"]!.GetValue<string>(), FirstField(english)["label"]!.GetValue<string>()), Is.EqualTo(("en-US", "Name")));
            Assert.That(bootstrap!["form"]!["cssClass"]!.GetValue<string>(), Does.Contain("jfb-theme-bootstrap5"));
            Assert.That(bootstrap["form"]!["classes"]!["control"]!.GetValue<string>(), Does.Contain("form-control"), "the theme's classes beside ours");
            Assert.That(Styles(bootstrap), Is.EqualTo(new[] { "themes/base.css", "themes/bootstrap5.css" }), "the theme's stylesheet after the base rules");
            Assert.That(Styles(english), Is.EqualTo(new[] { "themes/base.css", "themes/default.css" }));
            Assert.That(unknown!["form"]!["cssClass"]!.GetValue<string>(), Does.Contain("jfb-theme-default"), "a theme the site lacks renders as default");
            Assert.That(given!["form"]!["contextValues"]!.ToJsonString(), Is.EqualTo("""[{"name":"jfb-context-campaign","value":"spring"}]"""), "a name the form lists, in any letter case; another left out");
        });
    }

    [Test]
    public async Task ALibraryTheme_RendersAsItsBaseTheme_WithItsLayerAfterTheBaseStylesheet_ServedUnderItsVersion()
    {
        var key = await PublishFormAsync("elementLibraryTheme", definition => Headless(definition));
        var library = GetRequiredService<ThemeLibrary>();
        var created = await library.CreateAsync(
            new ThemeInput("autumn", "Autumn", "default", new ThemeTokens { Light = new Dictionary<string, string> { ["--jfb-color-accent"] = "#8a1538" } }, ".jfb-label { letter-spacing: .02em; }"),
            null);

        var (_, _, autumn) = await ElementAsync(key, "instance=jfb-1&theme=autumn");
        using var layer = await GetAsync($"/jfb/assets/themes/library/autumn.css?v={created.Theme!.ConcurrencyVersion}");
        var css = await layer.Content.ReadAsStringAsync();
        using var missing = await GetAsync("/jfb/assets/themes/library/winter.css?v=1");

        Assert.Multiple(() =>
        {
            Assert.That(autumn!["form"]!["cssClass"]!.GetValue<string>().Split(' '), Is.SupersetOf(new[] { "jfb-theme-autumn", "jfb-theme-default" }), "the theme's class and its base theme's");
            Assert.That(Styles(autumn), Is.EqualTo(new[] { "themes/base.css", "themes/default.css", "themes/library/autumn.css" }), "the layer after its base theme's stylesheet");
            Assert.That(autumn["assets"]!["styles"]!.AsArray().Last()!.GetValue<string>(), Does.EndWith($"?v={created.Theme.ConcurrencyVersion}"));
            Assert.That(layer.StatusCode, Is.EqualTo(HttpStatusCode.OK));
            Assert.That(layer.Content.Headers.ContentType?.MediaType, Is.EqualTo("text/css"));
            Assert.That(layer.Headers.CacheControl?.ToString(), Is.EqualTo("public, max-age=31536000, immutable"));
            Assert.That(css, Does.Contain(".jfb-form.jfb-theme-autumn {\n  --jfb-color-accent: #8a1538;\n}"));
            Assert.That(css, Does.Contain(".jfb-form.jfb-theme-autumn .jfb-label { letter-spacing: .02em; }"));
            Assert.That(missing.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
        });
    }

    [Test]
    public async Task ContextNamesTheFormDoesNotList_AreDroppedBeforeTheRendering_SoNoneIsLoggedOrKept()
    {
        var key = await PublishFormAsync("elementContextNames", definition => Headless(definition) with { Settings = Headless(definition).Settings with { Developer = Headless(definition).Settings.Developer with { ContextValues = ["campaign"] } } });

        // A thousand names made up by the requests: the rendering keeps a name it logs once per process, so a name it
        // never logs is a name it never keeps (6.8a).
        JsonNode? last = null;
        for (var batch = 0; batch < 10; batch++)
        {
            var names = string.Join("&", Enumerable.Range(batch * 100, 100).Select(index => $"context-made{index}=x"));
            (_, _, last) = await ElementAsync(key, $"instance=jfb-1&context-campaign=spring&{names}");
        }

        var warnings = GetRequiredService<LogCapture>().Entries.Where(entry => entry.Category == typeof(FormModels).FullName && entry.Level == LogLevel.Warning).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(warnings.Select(warning => warning.Message), Is.Empty, "no line for a name a request made up");
            Assert.That(last!["form"]!["contextValues"]!.ToJsonString(), Is.EqualTo("""[{"name":"jfb-context-campaign","value":"spring"}]"""), "the name the form lists still reaches it");
        });
    }

    [TestCase("", "400 invalid_request instance")]
    [TestCase("instance=jfb-0", "400 invalid_request instance")]
    [TestCase("instance=form-1", "400 invalid_request instance")]
    [TestCase("instance=jfb-1&contentKey=nope", "400 invalid_request contentKey")]
    [TestCase("instance=jfb-1&culture=not%20a%20culture", "400 invalid_request culture")]
    [TestCase("instance=jfb-1&theme=no%20theme", "400 invalid_request theme")]
    [TestCase("instance=jfb-1&context-campaign=a&context-campaign=b", "400 invalid_request context-campaign")]
    [TestCase("instance=jfb-1&context-=a", "400 invalid_request context-")]
    public async Task AMalformedParameter_IsRefused(string query, string refusal)
    {
        var key = await PublishFormAsync("elementRefused", definition => Headless(definition));

        Assert.That(await RefusalAsync($"{Root}/forms/{key}/element?{query}"), Is.EqualTo(refusal + ", no-store"));
    }

    [Test]
    public async Task AFormTheSiteLacks_OrThatIsNotHeadless_IsNotFound()
    {
        var closed = await PublishFormAsync("elementNotHeadless", definition => Headless(definition, headless: false));

        Assert.Multiple(async () =>
        {
            Assert.That(await RefusalAsync($"{Root}/forms/{Guid.NewGuid()}/element?instance=jfb-1"), Is.EqualTo("404 form_not_found, no-store"));
            Assert.That(await RefusalAsync($"{Root}/forms/elementNotHeadless/element?instance=jfb-1"), Is.EqualTo("404 form_not_found, no-store"), "by key only");
            Assert.That(await RefusalAsync($"{Root}/forms/{closed}/element?instance=jfb-1"), Is.EqualTo("404 form_not_found, no-store"), "as the definition endpoint answers it");
        });
    }

    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.JironFormBuilder().FieldTypes().Append<MoodFieldType>();
        builder.Services.AddLogCapture();
    }

    // The endpoint's status, Cache-Control and answer.
    private async Task<(HttpStatusCode Status, string? Cache, JsonNode? Answer)> ElementAsync(Guid key, string query)
    {
        using var response = await GetAsync($"{Root}/forms/{key}/element?{query}");
        var text = await response.Content.ReadAsStringAsync();
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK), text);
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/json"));
        using var answer = System.Text.Json.JsonDocument.Parse(text);
        var result = Schema.Value.Evaluate(answer.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
        Assert.That(result.IsValid, Is.True, () => "the element's schema: " + string.Join("; ", (result.Details ?? []).Where(detail => detail.Errors is { Count: > 0 }).Take(5).Select(detail => $"{detail.InstanceLocation}: {string.Join(", ", detail.Errors!.Values)}")));
        return (response.StatusCode, response.Headers.CacheControl?.ToString(), JsonNode.Parse(text));
    }

    // A refusal as its status, its code and what it names, and its Cache-Control.
    private async Task<string> RefusalAsync(string path)
    {
        using var response = await GetAsync(path);
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        var named = problem["errors"] is JsonArray errors ? " " + string.Join(" ", errors.Select(error => error!["path"]!.GetValue<string>())) : string.Empty;
        return $"{(int)response.StatusCode} {problem["code"]}{named}, {response.Headers.CacheControl}";
    }

    // The stylesheets an answer names, below the assets' path and without their fingerprints.
    private static string[] Styles(JsonNode answer) =>
        [.. answer["assets"]!["styles"]!.AsArray().Select(style => style!.GetValue<string>().Split('?')[0].Replace("/jfb/assets/", string.Empty, StringComparison.Ordinal))];

    private static JsonNode FirstField(JsonNode answer) => answer["form"]!["pages"]![0]!["groups"]![0]!["fields"]![0]!;

    // Every object in a tree, depth first.
    private static IEnumerable<JsonObject> Descendants(JsonNode node)
    {
        switch (node)
        {
            case JsonObject item:
                yield return item;
                foreach (var (_, value) in item)
                {
                    foreach (var below in value is null ? [] : Descendants(value))
                    {
                        yield return below;
                    }
                }

                break;
            case JsonArray items:
                foreach (var value in items)
                {
                    foreach (var below in value is null ? [] : Descendants(value))
                    {
                        yield return below;
                    }
                }

                break;
        }
    }

    // Every property name in a tree.
    private static HashSet<string> Names(JsonNode node) => [.. Descendants(node).SelectMany(item => item.Select(pair => pair.Key))];

    // The paths of the properties of a name, array positions left out.
    private static List<string> Paths(JsonNode node, string name)
    {
        var paths = new List<string>();
        void Walk(JsonNode? at, string path)
        {
            switch (at)
            {
                case JsonObject item:
                    foreach (var (key, value) in item)
                    {
                        var below = path.Length == 0 ? key : path + "." + key;
                        if (key == name)
                        {
                            paths.Add(below);
                        }

                        Walk(value, below);
                    }

                    break;
                case JsonArray items:
                    foreach (var value in items)
                    {
                        Walk(value, path);
                    }

                    break;
            }
        }

        Walk(node, string.Empty);
        return paths;
    }

    // Three pages of one field each.
    private static FormDefinition Paged(FormDefinition definition) => definition with
    {
        Pages =
        [
            new FormPage { Key = K(0x40), Groups = [new FormGroup { Key = K(0x43), Fields = [NameField] }] },
            new FormPage { Key = K(0x41), Groups = [new FormGroup { Key = K(0x44), Fields = [new FormField { Key = K(0x46), Type = "email", Alias = "email", Label = new LocalizedText("Email") }] }] },
            new FormPage { Key = K(0x42), Groups = [new FormGroup { Key = K(0x45), Fields = [new FormField { Key = K(0x47), Type = "textarea", Alias = "note", Label = new LocalizedText("Note") }] }] },
        ],
    };

    private static Guid K(int number) => Guid.Parse($"30000000-0000-4000-8000-0000006e{number:x4}");
}
