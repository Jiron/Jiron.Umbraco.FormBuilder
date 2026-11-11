using System.Net;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Services;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.AI.Privacy;

/// <summary>
/// No entry data ever reaches the AI (D23, ROADMAP 15.3, ADR-0018): a published form with entries whose answers are
/// unmistakable is drafted beside, translated and suggested for, and no message any of the three sent holds an answer
/// of any entry, while each holds the form's own texts it was about.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class NoEntryDataTests : AITestBase
{
    private static readonly string[] Answers = ["Quokka Zanzibar-4711", "quokka.4711@example.test", "Unmistakable answer about pelicans 9931"];

    [Test]
    public async Task ThePromptsOfEveryFeature_HoldTheFormsTexts_AndNoAnswerOfAnyEntry()
    {
        await GetRequiredService<ILanguageService>().CreateAsync(new Language("de-DE", "German"), Constants.Security.SuperUserKey);
        var message = FieldOf("textarea", "message", new LocalizedText("Your message"));
        var key = await FormWithAsync(
            FieldOf("text", "name", new LocalizedText("Your name"), "As in your passport"),
            FieldOf("email", "email", new LocalizedText("Email address")),
            message);
        var forms = GetRequiredService<IFormBuilderFormService>();
        _ = await forms.PublishAsync(key, (await forms.GetAsync(key))!.ConcurrencyVersion, null, null);
        var entries = GetRequiredService<IFormBuilderEntryService>();
        for (var i = 0; i < 3; i++)
        {
            var created = await entries.CreateAsync(new FormBuilderNewEntry
            {
                Form = key,
                Answers = new Dictionary<string, FormBuilderNewAnswer> { ["name"] = new(Answers[0]), ["email"] = new(Answers[1]), ["message"] = new(Answers[2]) },
            });
            Assert.That(created.Entry, Is.Not.Null, "the entry is stored");
        }

        var translated = await CallAsync("designer", HttpMethod.Post, $"ai/form/{key}/translation", new { culture = "de-DE" });
        var suggested = await CallAsync("designer", HttpMethod.Post, $"ai/form/{key}/suggestions", new { fieldKey = message.Key });
        var drafted = await CallAsync("designer", HttpMethod.Post, "ai/draft", new { text = "A form like the contact form, with a message." });

        var sent = Journal.Entries.Where(entry => entry.StartsWith("message ", StringComparison.Ordinal)).ToList();
        Assert.Multiple(() =>
        {
            Assert.That(translated.Status, Is.EqualTo(HttpStatusCode.OK), translated.ToString());
            Assert.That(suggested.Status, Is.EqualTo(HttpStatusCode.OK), suggested.ToString());
            Assert.That(drafted.Status, Is.EqualTo(HttpStatusCode.Created), drafted.ToString());
            Assert.That(Journal.Entries.Count(entry => entry.StartsWith("request:", StringComparison.Ordinal)), Is.EqualTo(3), "each feature asked once");
            Assert.That(sent, Has.Some.Contains("Your name").And.Some.Contains("Your message"), "the form's texts went");
            foreach (var answer in Answers)
            {
                Assert.That(sent, Has.None.Contains(answer), $"an entry's answer never goes: {answer}");
                Assert.That(sent, Has.None.Contains(answer.Split(' ')[0]), $"nor a part of it: {answer}");
            }
        });
    }
}
