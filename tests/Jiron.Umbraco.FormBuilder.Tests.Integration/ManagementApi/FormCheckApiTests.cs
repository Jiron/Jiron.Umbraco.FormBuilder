using System.Net;
using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>
/// The form check through the management API (ROADMAP 9.5b, ADR-0177): the draft the designer holds, checked without
/// saving, gives its errors, warnings and hints, each with the path of its element and setting; publishing refuses the
/// errors at their paths, and a form with warnings only goes live.
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
