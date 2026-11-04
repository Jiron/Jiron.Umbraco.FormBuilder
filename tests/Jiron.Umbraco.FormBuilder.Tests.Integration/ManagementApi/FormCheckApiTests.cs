using System.Net;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>
/// The form check through the management API (ROADMAP 9.5b, 9.5d, 9.5e; ADR-0177, ADR-0179, ADR-0180): the draft the
/// designer holds, checked without saving, gives its errors, warnings and hints, each with the path of its element and
/// setting, the checks of the form's rules, of the site and of what the form collects among them; publishing refuses the
/// errors at their paths, conditions a change of type left and a label's tag of a source labels refuse too, and a form
/// with warnings and hints only goes live.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class FormCheckApiTests : ManagementApiTestBase
{
    [Test]
    public async Task TheDraftHeld_IsChecked_ItsErrorsWarningsAndHints_EachAtItsPath()
    {
        var (form, _, definition) = await DraftAsync();
        Fields(definition, Field("text", "nameless", null), Field("text", "greeting", "Hello {{ nothing.here }}"));

        var answer = await CallAsync("designer", HttpMethod.Post, $"form/{form}/check", new { definition });

        Assert.That(answer.Outcome, Is.EqualTo("200"), answer.ToString());
        var issues = answer.Body!["issues"]!.AsArray().Select(issue => (issue!["severity"]!.GetValue<string>(), issue["code"]!.GetValue<string>(), issue["path"]!.GetValue<string>())).ToList();
        Assert.That(issues, Is.EqualTo(new[]
        {
            ("error", LabelFormCheck.LabelMissing, "$.pages[0].groups[0].fields[0].label"),
            ("warning", "unknown_path", "$.pages[0].groups[0].fields[1].label['']"),
            ("hint", Hinting.Code, "$.pages[0]"),
        }));
        Assert.That(answer.Body["issues"]![0]!["message"]!.GetValue<string>(), Is.EqualTo("This field has no label, which screen readers and the entries name it by."));
    }

    [Test]
    public async Task TheStructuralValidationsErrors_AreTheChecksErrors_ADuplicateAliasAndARuleOnARemovedField()
    {
        var (form, _, definition) = await DraftAsync();
        var shown = Field("text", "name", "Name");
        shown["rules"] = JsonNode.Parse("""[{"key":"40000000-0000-4000-8000-000000000001","effect":"show","conditions":[{"field":"30000000-0000-4000-8000-0000000000ff","op":"isNotEmpty"}]}]""");
        Fields(definition, Field("text", "name", "First"), shown);

        var answer = await CallAsync("designer", HttpMethod.Post, $"form/{form}/check", new { definition });

        Assert.That(answer.Outcome, Is.EqualTo("200"), answer.ToString());
        var errors = answer.Body!["issues"]!.AsArray().Where(issue => issue!["severity"]!.GetValue<string>() == "error").Select(issue => (issue!["code"]!.GetValue<string>(), issue["path"]!.GetValue<string>())).ToList();
        Assert.That(errors, Is.SupersetOf(new[]
        {
            (DefinitionErrors.AliasDuplicate, "$.pages[0].groups[0].fields[1].alias"),
            (DefinitionErrors.UnknownField, "$.pages[0].groups[0].fields[1].rules[0].conditions[0].field"),
        }), answer.ToString());
    }

    [Test]
    public async Task TheCheck_ChangesNothing_AndADefinitionThatCannotBeReadIsRefused()
    {
        var (form, version, definition) = await DraftAsync();
        var before = (await CallAsync("designer", HttpMethod.Get, $"form/{form}/draft")).Body!["definition"]!.ToJsonString();
        Fields(definition, Field("text", "nameless", null));

        var checkedDraft = await CallAsync("designer", HttpMethod.Post, $"form/{form}/check", new { definition });
        var unreadable = await CallAsync("designer", HttpMethod.Post, $"form/{form}/check", new { definition = new JsonArray() });

        Assert.Multiple(async () =>
        {
            Assert.That(checkedDraft.Outcome, Is.EqualTo("200"), checkedDraft.ToString());
            Assert.That(unreadable.Status, Is.EqualTo(HttpStatusCode.BadRequest), unreadable.ToString());
            Assert.That(unreadable.Body?["errors"]?.AsArray(), Is.Not.Empty, unreadable.ToString());
            Assert.That((await GetRequiredService<IFormBuilderFormService>().GetAsync(form))!.ConcurrencyVersion, Is.EqualTo(version), "nothing saved");
            Assert.That((await CallAsync("designer", HttpMethod.Get, $"form/{form}/draft")).Body!["definition"]!.ToJsonString(), Is.EqualTo(before));
        });
    }

    [Test]
    public async Task Publishing_RefusesTheErrors_AtTheirPaths_AndAFormWithWarningsOnlyGoesLive()
    {
        var (form, version, definition) = await DraftAsync();
        Fields(definition, Field("text", "nameless", null));
        version = await SaveAsync(form, version, definition);

        var refused = await CallAsync("designer", HttpMethod.Post, $"form/{form}/publish", new { concurrencyVersion = version });

        Assert.That(refused.Status, Is.EqualTo(HttpStatusCode.BadRequest), refused.ToString());
        Assert.That(refused.Body!["errors"]!.AsArray().Select(error => (error!["code"]!.GetValue<string>(), error["path"]!.GetValue<string>())), Is.EqualTo(new[] { (LabelFormCheck.LabelMissing, "$.pages[0].groups[0].fields[0].label") }));

        Fields(definition, Field("text", "greeting", "Hello {{ nothing.here }}"));
        version = await SaveAsync(form, version, definition);
        var published = await CallAsync("designer", HttpMethod.Post, $"form/{form}/publish", new { concurrencyVersion = version });

        Assert.That(published.Status, Is.EqualTo(HttpStatusCode.OK), "a warning never keeps a form from going live: " + published);
    }

    [Test]
    public async Task TheFormsRules_AreChecked_ARequiredFieldARuleHides_AndAPageNoVisitorReaches()
    {
        // The notes are hidden for the press, yet required for it; the second page shows only once the third is answered.
        var (form, _, definition) = await DraftAsync();
        var topic = Field("dropdown", "topic", "Topic");
        topic["options"] = JsonNode.Parse("""{"items":[{"value":"delivery"},{"value":"shipping"},{"value":"press"}]}""");
        var notes = Field("textarea", "notes", "Notes");
        notes["rules"] = JsonNode.Parse(
            $$"""
            [{"key":"{{Guid.NewGuid()}}","effect":"hide","conditions":[{"field":"{{topic["key"]}}","op":"equals","value":"press"}]},
             {"key":"{{Guid.NewGuid()}}","effect":"require","conditions":[{"field":"{{topic["key"]}}","op":"isOneOf","value":["shipping","press"]}]}]
            """);
        Fields(definition, topic, notes);
        var later = Field("text", "later", "Later");
        var pages = definition["pages"]!.AsArray();
        pages.Add(Page(JsonNode.Parse($$"""[{"key":"{{Guid.NewGuid()}}","effect":"show","conditions":[{"field":"{{later["key"]}}","op":"isNotEmpty"}]}]"""), Field("textarea", "more", "More")));
        pages.Add(Page(null, later));

        var answer = await CallAsync("designer", HttpMethod.Post, $"form/{form}/check", new { definition });

        Assert.That(answer.Outcome, Is.EqualTo("200"), answer.ToString());
        var warnings = answer.Body!["issues"]!.AsArray().Where(issue => issue!["severity"]!.GetValue<string>() == "warning").Select(issue => (issue!["code"]!.GetValue<string>(), issue["path"]!.GetValue<string>())).ToList();
        Assert.That(warnings, Is.EqualTo(new[]
        {
            (RequiredHiddenFormCheck.RequiredCanHide, "$.pages[0].groups[0].fields[1].rules[1]"),
            (UnreachablePageFormCheck.PageUnreachable, "$.pages[1].rules[0]"),
        }), answer.ToString());
    }

    [Test]
    public async Task Publishing_RefusesTheConditionsAChangeOfTypeLeft()
    {
        // The details show once the box is ticked; the box then became a short text, which no condition ticks.
        var (form, version, definition) = await DraftAsync();
        var box = Field("checkbox", "agree", "Agree");
        var details = Field("textarea", "details", "Details");
        details["rules"] = JsonNode.Parse($$"""[{"key":"{{Guid.NewGuid()}}","effect":"show","conditions":[{"field":"{{box["key"]}}","op":"isChecked"}]}]""");
        Fields(definition, box, details);
        version = await SaveAsync(form, version, definition);
        box["type"] = "text";
        version = await SaveAsync(form, version, definition);

        var refused = await CallAsync("designer", HttpMethod.Post, $"form/{form}/publish", new { concurrencyVersion = version });

        Assert.That(refused.Status, Is.EqualTo(HttpStatusCode.BadRequest), refused.ToString());
        Assert.That(refused.Body!["errors"]!.AsArray().Select(error => (error!["code"]!.GetValue<string>(), error["path"]!.GetValue<string>())), Is.EqualTo(new[]
        {
            (DefinitionErrors.OperatorNotForKind, "$.pages[0].groups[0].fields[1].rules[0].conditions[0].op"),
        }));
    }

    [Test]
    public async Task Publishing_RefusesAMergeTagInALabel_FromASourceALabelRefuses()
    {
        var (form, version, definition) = await DraftAsync();
        Fields(definition, Field("text", "greeting", "Hello {{ member.name }}"));
        version = await SaveAsync(form, version, definition);

        var refused = await CallAsync("designer", HttpMethod.Post, $"form/{form}/publish", new { concurrencyVersion = version });

        Assert.That(refused.Status, Is.EqualTo(HttpStatusCode.BadRequest), refused.ToString());
        Assert.That(refused.Body!["errors"]!.AsArray().Select(error => (error!["code"]!.GetValue<string>(), error["path"]!.GetValue<string>())), Is.EqualTo(new[]
        {
            ("source_not_for_place", "$.pages[0].groups[0].fields[0].label['']"),
        }));
    }

    [Test]
    public async Task TheSitesChecks_AndThoseOfWhatTheFormCollects_ReachTheDesigner_AndTheirWarningsNeverKeepItFromGoingLive()
    {
        // An email address on a form without a consent field, under a dictionary item the site lacks; a file field without a
        // size; a theme and a page the site lacks.
        var (form, version, definition) = await DraftAsync();
        Fields(definition, Field("email", "mail", "#jfb.test.nothing"), Field("file", "cv", "CV"));
        if (definition["settings"] is not JsonObject settings)
        {
            settings = new JsonObject();
            definition["settings"] = settings;
        }

        settings["appearance"] = JsonNode.Parse("""{"theme":"gone"}""");
        definition["outcomes"] = JsonNode.Parse($$$"""[{"key":"{{{Guid.NewGuid()}}}","kind":"redirect","redirect":{"content":"{{{Guid.NewGuid()}}}"}}]""");

        var answer = await CallAsync("designer", HttpMethod.Post, $"form/{form}/check", new { definition });

        Assert.That(answer.Outcome, Is.EqualTo("200"), answer.ToString());
        var issues = answer.Body!["issues"]!.AsArray().Select(issue => (issue!["severity"]!.GetValue<string>(), issue["code"]!.GetValue<string>(), issue["path"]!.GetValue<string>())).ToList();
        Assert.That(issues, Is.SupersetOf(new[]
        {
            ("warning", DictionaryFormCheck.DictionaryTextMissing, "$.pages[0].groups[0].fields[0].label['']"),
            ("warning", ThemeFormCheck.ThemeMissing, "$.settings.appearance.theme"),
            ("warning", SiteReferencesFormCheck.ContentMissing, "$.outcomes[0].redirect.content"),
            ("hint", ConsentFormCheck.ConsentMissing, "$.pages[0].groups[0].fields[0]"),
            ("hint", FileLimitFormCheck.FileSizeUnset, "$.pages[0].groups[0].fields[1].settings.maxBytes"),
        }), answer.ToString());
        Assert.That(issues.Where(issue => issue.Item1 == "error"), Is.Empty, answer.ToString());

        version = await SaveAsync(form, version, definition);
        var published = await CallAsync("designer", HttpMethod.Post, $"form/{form}/publish", new { concurrencyVersion = version });

        Assert.That(published.Status, Is.EqualTo(HttpStatusCode.OK), published.ToString());
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.Services.AddSingleton<IFormCheck, Hinting>();
    }

    private async Task<(Guid Form, int Version, JsonNode Definition)> DraftAsync()
    {
        var form = (await GetRequiredService<IFormBuilderFormService>().CreateAsync(new NewFormRequest("Checked"))).Key;
        var draft = await CallAsync("designer", HttpMethod.Get, $"form/{form}/draft");
        Assert.That(draft.Outcome, Is.EqualTo("200"), draft.ToString());
        return (form, (await GetRequiredService<IFormBuilderFormService>().GetAsync(form))!.ConcurrencyVersion, draft.Body!["definition"]!.DeepClone());
    }

    private async Task<int> SaveAsync(Guid form, int version, JsonNode definition)
    {
        var saved = await CallAsync("designer", HttpMethod.Put, $"form/{form}/draft", new { concurrencyVersion = version, definition });
        Assert.That(saved.Outcome, Is.EqualTo("200"), saved.ToString());
        return (await GetRequiredService<IFormBuilderFormService>().GetAsync(form))!.ConcurrencyVersion;
    }

    private static void Fields(JsonNode definition, params JsonObject[] fields) => definition["pages"]![0]!["groups"]![0]!["fields"] = new JsonArray(fields);

    // A page of one group with the fields, and its rules where given.
    private static JsonObject Page(JsonNode? rules, params JsonObject[] fields)
    {
        var page = new JsonObject
        {
            ["key"] = Guid.NewGuid().ToString(),
            ["groups"] = new JsonArray(new JsonObject { ["key"] = Guid.NewGuid().ToString(), ["fields"] = new JsonArray(fields) }),
        };
        if (rules is not null)
        {
            page["rules"] = rules;
        }

        return page;
    }

    private static JsonObject Field(string type, string alias, string? label)
    {
        var field = new JsonObject { ["key"] = Guid.NewGuid().ToString(), ["type"] = type, ["alias"] = alias };
        if (label is not null)
        {
            field["label"] = new JsonObject { [string.Empty] = label };
        }

        return field;
    }

    // A check of this site that hints, as a site's own checks may: the API carries hints as it carries the rest.
    private sealed class Hinting : IFormCheck
    {
        public const string Code = "test_hint";

        public ValueTask<IReadOnlyList<FormCheckIssue>> CheckAsync(FormDefinition definition, CancellationToken cancellationToken) =>
            ValueTask.FromResult<IReadOnlyList<FormCheckIssue>>([new FormCheckIssue(FormCheckSeverity.Hint, Code, "$.pages[0]", "A hint.")]);
    }
}
