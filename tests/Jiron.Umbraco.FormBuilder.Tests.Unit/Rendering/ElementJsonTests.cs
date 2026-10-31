using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Testing;
using Jiron.Umbraco.FormBuilder.Web.Rendering;
using Jiron.Umbraco.FormBuilder.Web.Rendering.Models;
using Json.Schema;
using Microsoft.AspNetCore.Html;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Rendering;

/// <summary>
/// How the element endpoint writes the view models (ADR-0128): in camel case, without the view paths, with the classes
/// once at the form, without what only a post without JavaScript uses; a control as an object with its kind first, a
/// control of a type the element does not know refused; a rich text as its HTML, an enum as its camel-case name and
/// null as nothing. The schema in the element's package is what the models' metadata makes (after a deliberate change,
/// run with <c>JFB_UPDATE_SNAPSHOTS=1</c>, review the diff and run <c>scripts/generate-public-types.mjs</c>), and the
/// answer written fits it.
/// </summary>
public sealed class ElementJsonTests
{
    private static readonly string SchemaPath = RepositoryRoot.Path(typeof(ElementJsonTests).Assembly, ElementAnswerSchema.Path);

    private static readonly FormViewPaths Views = new()
    {
        Page = "~/Page.cshtml",
        Group = "~/Group.cshtml",
        Field = "~/Field.cshtml",
        Navigation = "~/Navigation.cshtml",
        Progress = "~/Progress.cshtml",
        ErrorSummary = "~/ErrorSummary.cshtml",
        Outcome = "~/Outcome.cshtml",
        Notice = "~/Notice.cshtml",
        Summary = "~/Summary.cshtml",
    };

    [Test]
    public void TheAnswer_LeavesOutTheViewPaths_TheClassesBelowTheForm_AndWhatOnlyAPostWithoutJavaScriptUses()
    {
        var answer = Written(Form(new TextControlModel { InputType = "email" }));
        var form = answer["form"]!.AsObject();

        Assert.Multiple(() =>
        {
            Assert.That(Names(answer), Has.None.AnyOf("views", "controlView", "action", "encodingType", "postValues"));
            Assert.That(Count(answer, "classes"), Is.EqualTo(1), "the classes once");
            Assert.That(form["classes"]!["control"]!.GetValue<string>(), Is.EqualTo(ThemeClassesModel.None.Control));
            Assert.That(form.Select(pair => pair.Key).Take(3), Is.EqualTo(new[] { "classes", "key", "alias" }), "camel case, in the model's order");
            Assert.That(form["contextValues"]!.ToJsonString(), Is.EqualTo("""[{"name":"jfb-context-campaign","value":"spring"}]"""), "the context values, which the runtime posts");
        });
    }

    [Test]
    public void AControl_IsWrittenWithItsKindFirst_ThenItsOwnProperties()
    {
        var control = Field(Written(Form(new TextControlModel { InputType = "email", Spellcheck = false })))["control"]!.AsObject();

        Assert.That(control.ToJsonString(), Is.EqualTo("""{"kind":"text","inputType":"email","spellcheck":false}"""));
    }

    [Test]
    public void AControlOfATypeTheElementDoesNotKnow_IsRefused() =>
        Assert.That(() => ElementJson.Write(Answer(Form(new Uri("https://example.com/")))), Throws.InstanceOf<JsonException>().With.Message.Contains("ElementJson.Kinds"));

    [Test]
    public void ARichText_IsItsHtml_AnEnumItsCamelCaseName_AndNullNothing()
    {
        var answer = Written(Form(new SingleChoiceControlModel { Value = "yes", Label = new HtmlString("<p>I <em>agree</em>.</p>"), IsChecked = false }));
        var field = Field(answer);

        Assert.Multiple(() =>
        {
            Assert.That(field["control"]!["label"]!.GetValue<string>(), Is.EqualTo("<p>I <em>agree</em>.</p>"));
            Assert.That(answer["form"]!["modelData"]!.GetValue<string>(), Is.EqualTo("""{"alias":"contact","note":"\u003cb\u003e"}"""), "the model data's text as the page's script holds it");
            Assert.That((answer["form"]!["display"]!.GetValue<string>(), field["labelElement"]!.GetValue<string>(), field["mark"]!.GetValue<string>()), Is.EqualTo(("form", "choice", "none")));
            Assert.That(field.AsObject().ContainsKey("label"), Is.False, "a field without a label has no label property");
            Assert.That(field["control"]!.AsObject().ContainsKey("wording"), Is.False);
        });
    }

    [Test]
    public void EveryControlModel_HasItsKind_ItsNameInCamelCase()
    {
        var models = typeof(FieldViewModel).Assembly.GetExportedTypes().Where(type => type.Namespace == typeof(FieldViewModel).Namespace && type.Name.EndsWith("ControlModel", StringComparison.Ordinal));

        Assert.Multiple(() =>
        {
            Assert.That(ElementJson.Kinds.Keys, Is.EquivalentTo(models));
            Assert.That(ElementJson.Kinds[typeof(TextAreaControlModel)], Is.EqualTo("textArea"));
            Assert.That(ElementJson.Kinds[typeof(SingleChoiceControlModel)], Is.EqualTo("singleChoice"));
        });
    }

    [Test]
    public async Task TheSchema_IsWhatTheModelsMetadataMakes_AsTheElementsPackageHoldsIt()
    {
        var written = ElementAnswerSchema.Build().ToJsonString(new JsonSerializerOptions { WriteIndented = true }).ReplaceLineEndings("\n") + "\n";
        if (Environment.GetEnvironmentVariable("JFB_UPDATE_SNAPSHOTS") == "1")
        {
            await File.WriteAllTextAsync(SchemaPath, written, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            Assert.Fail($"JFB_UPDATE_SNAPSHOTS=1: wrote {SchemaPath}. Review the diff, run scripts/generate-public-types.mjs, and run again without the variable.");
        }

        Assert.That(File.Exists(SchemaPath), Is.True, $"the reviewed schema {SchemaPath}; run with JFB_UPDATE_SNAPSHOTS=1");
        Assert.That((await File.ReadAllTextAsync(SchemaPath)).ReplaceLineEndings("\n"), Is.EqualTo(written), "the view models changed: run with JFB_UPDATE_SNAPSHOTS=1 and review the diff");
    }

    [Test]
    public void TheSchema_AllowsNothingElse_AndAControlIsOneOfEveryKind()
    {
        var schema = ElementAnswerSchema.Build();
        var definitions = schema["$defs"]!.AsObject();
        var control = definitions[nameof(FieldViewModel)]!["properties"]!["control"]!["oneOf"]!.AsArray();

        Assert.Multiple(() =>
        {
            Assert.That(definitions.Select(pair => pair.Value!["additionalProperties"]?.GetValue<bool>()), Has.All.False);
            Assert.That(control.Select(item => item!["$ref"]!.GetValue<string>()), Is.EquivalentTo(ElementJson.Kinds.Keys.Select(type => "#/$defs/" + ElementAnswerSchema.ControlName(type))));
            Assert.That(definitions["TextControl"]!["properties"]!.AsObject().First().Key, Is.EqualTo("kind"));
            Assert.That(definitions["SelectControl"]!["properties"]!["kind"]!["const"]!.GetValue<string>(), Is.EqualTo("select"), "a select as a control, with its kind");
            Assert.That(definitions[nameof(SelectControlModel)]!["properties"]!.AsObject().ContainsKey("kind"), Is.False, "and as an address's countries, without one");
            Assert.That(definitions.Select(pair => pair.Key), Does.Not.Contain(nameof(FormViewPaths)));
        });
    }

    [Test]
    public void TheAnswerWritten_FitsTheSchema()
    {
        var schema = JsonSchema.FromText(File.ReadAllText(SchemaPath), new BuildOptions { SchemaRegistry = new SchemaRegistry() });
        foreach (var control in new object[] { new TextControlModel { InputType = "text" }, new SingleChoiceControlModel { Value = "yes", Label = new HtmlString("Yes"), IsChecked = true } })
        {
            using var written = JsonDocument.Parse(ElementJson.Write(Answer(Form(control))));
            var result = schema.Evaluate(written.RootElement, new EvaluationOptions { OutputFormat = OutputFormat.List });
            Assert.That(result.IsValid, Is.True, () => string.Join("; ", (result.Details ?? []).Where(detail => detail.Errors is { Count: > 0 }).Take(5).Select(detail => $"{detail.InstanceLocation}: {string.Join(", ", detail.Errors!.Values)}")));
        }
    }

    private static ElementAnswer Answer(FormViewModel form) => new(ElementAnswer.Version, form, new ElementAssets(["/jfb/assets/themes/base.css?v=1"], "/jfb/assets/runtime/jfb-runtime.js?v=1"));

    private static JsonNode Written(FormViewModel form) => JsonNode.Parse(ElementJson.Write(Answer(form)))!;

    private static JsonNode Field(JsonNode answer) => answer["form"]!["pages"]![0]!["groups"]![0]!["fields"]![0]!;

    // A form of one page, one group and one field with the control, and what only a post without JavaScript uses.
    private static FormViewModel Form(object control) => new()
    {
        Key = Guid.Parse("6f1c3a52-31f4-4e57-9d2e-61b0f6a1c084"),
        Alias = "contact",
        Revision = 3,
        Id = "jfb-1",
        Culture = "en-US",
        Direction = "ltr",
        CssClass = "jfb-form jfb-theme-default",
        Display = FormDisplay.Form,
        Action = "/contact/#jfb-1",
        EncodingType = "multipart/form-data",
        ApiBase = "/jfb/api/v1",
        ModelData = new HtmlString("""{"alias":"contact","note":"\u003cb\u003e"}"""),
        PostValues = [new HiddenInputModel("ufprt", "token")],
        ContextValues = [new HiddenInputModel("jfb-context-campaign", "spring")],
        Views = Views,
        Pages =
        [
            new PageViewModel
            {
                Key = Guid.Parse("7a2d4b63-42a5-4f68-8e3f-72c1a7b2d195"),
                IsCurrent = true,
                IsHidden = false,
                Views = Views,
                Groups =
                [
                    new GroupViewModel
                    {
                        Key = Guid.Parse("8b3e5c74-53b6-4a79-9f40-83d2b8c3e2a6"),
                        DescriptionId = "jfb-1-group-help",
                        CssClass = "jfb-group",
                        IsHidden = false,
                        Views = Views,
                        Fields =
                        [
                            new FieldViewModel
                            {
                                Key = Guid.Parse("9c4f6d85-64c7-4b8a-8a51-94e3c9d4f3b7"),
                                Type = "consent",
                                Id = "jfb-1-agree",
                                Name = "agree",
                                CssClass = "jfb-field",
                                LabelElement = FieldLabelElement.Choice,
                                HelpId = "jfb-1-agree-help",
                                ErrorId = "jfb-1-agree-error",
                                ControlView = "~/fields/consent.cshtml",
                                Control = control,
                            },
                        ],
                    },
                ],
            },
        ],
    };

    // Every property name in a tree.
    private static List<string> Names(JsonNode? node) => node switch
    {
        JsonObject item => [.. item.SelectMany(pair => Names(pair.Value).Prepend(pair.Key))],
        JsonArray items => [.. items.SelectMany(Names)],
        _ => [],
    };

    private static int Count(JsonNode node, string name) => Names(node).Count(each => each == name);
}
