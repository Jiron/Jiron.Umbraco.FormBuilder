using System.Net;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.AI.Suggesting;

/// <summary>
/// Suggest (ROADMAP 15.3, ADR-0018): one field of the saved draft goes to the AI, which answers a help text and
/// improvements to the field's own texts, each beside the path the designer changes.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class SuggestionApiTests : AITestBase
{
    [Test]
    public async Task AFieldWithoutHelp_GetsAHelpText_AndAnAbbreviationWrittenOut()
    {
        var field = FieldOf("date", "dob", new LocalizedText("DOB"));
        var key = await FormWithAsync(FieldOf("text", "name", new LocalizedText("Name"), "As in your passport"), field);

        var answer = await CallAsync("designer", HttpMethod.Post, $"ai/form/{key}/suggestions", new { fieldKey = field.Key });

        Assert.That(answer.Status, Is.EqualTo(HttpStatusCode.OK), answer.ToString());
        var improvement = answer.Body!["improvements"]!.AsArray().Single()!;
        Assert.Multiple(() =>
        {
            Assert.That(answer.Body!["help"]!["text"]!.GetValue<string>(), Is.EqualTo("Fake help for DOB."));
            Assert.That(answer.Body!["help"]!["path"]!.GetValue<string>(), Is.EqualTo("$.pages[0].groups[0].fields[1].help"));
            Assert.That(improvement["setting"]!.GetValue<string>(), Is.EqualTo("label"));
            Assert.That(improvement["path"]!.GetValue<string>(), Is.EqualTo("$.pages[0].groups[0].fields[1].label"));
            Assert.That(improvement["text"]!.GetValue<string>(), Is.EqualTo("DOB written out"));
            Assert.That(improvement["reason"]!.GetValue<string>(), Is.Not.Empty);
            Assert.That(Journal.Entries, Has.Some.Contains("\"nearby\":[\"Name\"]"), "the labels around it");
        });
    }

    [Test]
    public async Task AFieldThatHasHelp_GetsNoSecondOne()
    {
        var field = FieldOf("text", "name", new LocalizedText("Name"), "As in your passport");
        var key = await FormWithAsync(field);

        var answer = await CallAsync("designer", HttpMethod.Post, $"ai/form/{key}/suggestions", new { fieldKey = field.Key });

        Assert.Multiple(() =>
        {
            Assert.That(answer.Status, Is.EqualTo(HttpStatusCode.OK), answer.ToString());
            Assert.That(answer.Body!["help"], Is.Null);
            Assert.That(answer.Body!["improvements"]!.AsArray(), Is.Empty);
        });
    }

    [Test]
    public async Task AFieldNotInTheSavedDraft_IsRefused_AndAUserWhoMayNotDesign_TooAsync()
    {
        var key = await FormWithAsync(FieldOf("text", "name", new LocalizedText("Name")));

        Assert.Multiple(async () =>
        {
            Assert.That((await CallAsync("designer", HttpMethod.Post, $"ai/form/{key}/suggestions", new { fieldKey = Guid.NewGuid() })).Outcome, Is.EqualTo("404 ai_field_missing"));
            Assert.That((await CallAsync("viewer", HttpMethod.Post, $"ai/form/{key}/suggestions", new { fieldKey = Guid.NewGuid() })).Outcome, Is.EqualTo("403 forbidden"));
            Assert.That(Journal.Entries, Has.None.StartsWith("request:"));
        });
    }
}
