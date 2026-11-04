using System.Text.Json.Nodes;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>
/// The host's designer locks through the management API (ADR-0060): the field types mark a locked setting read-only
/// with its value and key, and a hidden one hidden without its value; a change of type into a locked type neither carries
/// the locked setting over nor needs it; a draft save that gives a locked setting another value answers
/// <c>409 setting_locked</c> with the setting's path and the lock's key.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class FieldSettingLockApiTests : ManagementApiTestBase
{
    private const string Locks = FormBuilderOptions.SectionName + ":Designer:SettingLocks:";

    /// <summary>The host configuration of every test's site: the text's maximum length shown read-only, its prefix hidden with a value.</summary>
    public FieldSettingLockApiTests()
    {
        InMemoryConfiguration[Locks + "0:Kind"] = "field";
        InMemoryConfiguration[Locks + "0:Type"] = "text";
        InMemoryConfiguration[Locks + "0:Setting"] = "maxLength";
        InMemoryConfiguration[Locks + "0:Value"] = "100";
        InMemoryConfiguration[Locks + "1:Kind"] = "field";
        InMemoryConfiguration[Locks + "1:Type"] = "text";
        InMemoryConfiguration[Locks + "1:Setting"] = "prefix";
        InMemoryConfiguration[Locks + "1:Value"] = "JFBSECRETPREFIX";
        InMemoryConfiguration[Locks + "1:Hidden"] = "true";
        InMemoryConfiguration[Locks + "2:Kind"] = "field";
        InMemoryConfiguration[Locks + "2:Type"] = "dropdown";
        InMemoryConfiguration[Locks + "2:Setting"] = "maxSelections";
        InMemoryConfiguration[Locks + "2:Value"] = "3";
    }

    [Test]
    public async Task AChangeIntoALockedType_NeitherCarriesTheLockedSettingOverNorNeedsIt()
    {
        var answers = new Dictionary<string, JsonObject>();
        var texts = new List<string>();
        foreach (var from in new[] { "checkboxes", "textarea", "number", "text" })
        {
            var answer = await CallAsync("designer", HttpMethod.Get, $"field-type/{from}/changes");
            Assert.That(answer.Outcome, Is.EqualTo("200"), answer.ToString());
            answers[from] = answer.Body!.AsObject();
            texts.Add(answer.Text);
        }

        string Part(string from, string to, string part) => string.Join(',', answers[from]["changes"]!.AsArray()
            .Single(change => change!["to"]!.GetValue<string>() == to)![part]!.AsArray().Select(name => name!.GetValue<string>()));

        Assert.Multiple(() =>
        {
            Assert.That((Part("checkboxes", "dropdown", "keptSettings"), Part("checkboxes", "dropdown", "lockedSettings")), Is.EqualTo(("minSelections", "maxSelections")), "the dropdown's lock sets its most selections");
            Assert.That((Part("textarea", "text", "narrowedSettings"), Part("textarea", "text", "lockedSettings")), Is.EqualTo(("minLength", "maxLength")), "the text's lock sets its maximum length");
            Assert.That((Part("number", "text", "keptSettings"), Part("number", "text", "lockedSettings")), Is.EqualTo(("suffix", "prefix")), "a hidden lock too, by name only");
            Assert.That((Part("text", "textarea", "keptSettings"), Part("text", "textarea", "lockedSettings")), Is.EqualTo(("minLength,maxLength,counter", string.Empty)), "a lock of the type it leaves changes nothing");
            Assert.That(texts, Has.None.Contains("JFBSECRETPREFIX"));
        });
    }

    [Test]
    public async Task TheFieldTypes_MarkALockedSettingReadOnlyWithItsValue_AndAHiddenOneWithoutIt()
    {
        var answer = await CallAsync("designer", HttpMethod.Get, "field-type");
        var types = answer.Body!["types"]!.AsArray().Select(type => type!.AsObject()).ToList();
        JsonNode? Lock(string type, string setting) =>
            types.Single(each => each["alias"]!.GetValue<string>() == type)["settings"]!.AsArray().Single(each => each!["name"]!.GetValue<string>() == setting)!["lock"];

        Assert.Multiple(() =>
        {
            Assert.That(answer.Outcome, Is.EqualTo("200"), answer.ToString());
            Assert.That(Lock("text", "maxLength")?.ToJsonString(), Is.EqualTo($$"""{"hidden":false,"configurationKey":"{{Locks}}0","value":100}"""));
            Assert.That(Lock("text", "prefix")?.ToJsonString(), Is.EqualTo($$"""{"hidden":true,"configurationKey":"{{Locks}}1","value":null}"""));
            Assert.That((Lock("text", "minLength"), Lock("textarea", "maxLength")), Is.EqualTo(((JsonNode?)null, (JsonNode?)null)), "another setting, and another type's");
            Assert.That(answer.Text, Does.Not.Contain("JFBSECRETPREFIX"), "a hidden lock's value stays on the server");
        });
    }

    [Test]
    public async Task ASaveThatGivesALockedSettingAnotherValue_Answers409SettingLocked_WithThePathAndTheKey()
    {
        var form = await FormAsync("Contact");
        var draft = await CallAsync("designer", HttpMethod.Get, $"form/{form}/draft");
        var version = draft.Body!["form"]!["concurrencyVersion"]!.GetValue<int>();
        var definition = draft.Body["definition"]!.AsObject();
        var fields = definition["pages"]![0]!["groups"]![0]!["fields"] as JsonArray ?? [];
        definition["pages"]![0]!["groups"]![0]!["fields"] = fields;
        var index = fields.Count;
        var field = new JsonObject
        {
            ["key"] = Guid.NewGuid().ToString(),
            ["type"] = "text",
            ["alias"] = "name",
            ["label"] = new JsonObject { [string.Empty] = "Name" },
            ["settings"] = new JsonObject { ["maxLength"] = 400 },
        };
        fields.Add(field);

        var refused = await CallAsync("designer", HttpMethod.Put, $"form/{form}/draft", new { concurrencyVersion = version, definition });
        field["settings"] = new JsonObject { ["maxLength"] = 100 };
        var saved = await CallAsync("designer", HttpMethod.Put, $"form/{form}/draft", new { concurrencyVersion = version, definition });

        Assert.Multiple(() =>
        {
            Assert.That(refused.Outcome, Is.EqualTo("409 setting_locked"), refused.ToString());
            Assert.That(refused.Body?["field"]?.GetValue<string>(), Is.EqualTo($"$.pages[0].groups[0].fields[{index}].settings.maxLength"));
            Assert.That(refused.Body?["configurationKey"]?.GetValue<string>(), Is.EqualTo(Locks + "0"));
            Assert.That(saved.Outcome, Is.EqualTo("200"), saved.ToString());
        });
    }
}
