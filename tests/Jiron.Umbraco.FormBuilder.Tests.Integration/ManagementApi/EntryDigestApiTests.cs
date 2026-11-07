using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.ManagementApi;

/// <summary>
/// The summary email through the management API (ROADMAP 11.1, IA 6.4, ADR-0209): each user's own choice with when and
/// where it goes, the mail as it would go out for the period due now, and its test, at once to the user's own address.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class EntryDigestApiTests : ManagementApiTestBase
{
    [Test]
    public async Task EachUserChoosesTheirOwn_AndLearnsWhenAndWhereItGoes()
    {
        var settings = GetRequiredService<IFormBuilderSettingsService>();
        var general = await settings.GetAsync<GeneralSettings>();
        await settings.SaveAsync(general.Value with { TimeZone = "Europe/Berlin" }, general.Version, null);

        var first = await CallAsync("viewer", HttpMethod.Get, "entries/digest");
        var saved = await CallAsync("viewer", HttpMethod.Put, "entries/digest", new { frequency = "weekly" });
        var again = await CallAsync("viewer", HttpMethod.Get, "entries/digest");
        var others = await CallAsync("admin", HttpMethod.Get, "entries/digest");
        var wrong = await CallAsync("viewer", HttpMethod.Put, "entries/digest", new { frequency = "hourly" });
        var off = await CallAsync("viewer", HttpMethod.Put, "entries/digest", new { frequency = "off" });
        var gone = await CallAsync("viewer", HttpMethod.Get, "entries/digest");

        Assert.Multiple(() =>
        {
            Assert.That(first.Outcome, Is.EqualTo("200"), first.ToString());
            Assert.That(Shown(first), Is.EqualTo(("off", "07:00", "Europe/Berlin", "api-viewer@jfb.test")));
            Assert.That((saved.Outcome, Shown(saved).Frequency), Is.EqualTo(("200", "weekly")), saved.ToString());
            Assert.That(Shown(again).Frequency, Is.EqualTo("weekly"));
            Assert.That(Shown(others).Frequency, Is.EqualTo("off"), "one user's choice is theirs alone");
            Assert.That((wrong.Outcome, Path(wrong)), Is.EqualTo(("400 invalid_frequency", "frequency")), wrong.ToString());
            Assert.That((off.Outcome, Shown(gone).Frequency), Is.EqualTo(("200", "off")), off.ToString());
        });
    }

    [Test]
    public async Task ThePreview_ShowsTheMailDueNow_AndTheTest_SendsItAtOnce_ToTheUsersOwnAddress()
    {
        var weekly = await CallAsync("viewer", HttpMethod.Get, "entries/digest/preview?frequency=weekly");
        var daily = await CallAsync("viewer", HttpMethod.Get, "entries/digest/preview");
        var wrong = await CallAsync("viewer", HttpMethod.Get, "entries/digest/preview?frequency=yearly");
        var queuedByPreview = await DigestsAsync();
        var test = await CallAsync("viewer", HttpMethod.Post, "entries/digest/test", new { frequency = "daily" });
        var badTest = await CallAsync("viewer", HttpMethod.Post, "entries/digest/test", new { frequency = "" });
        var queued = await DigestsAsync();

        Assert.Multiple(() =>
        {
            Assert.That(weekly.Outcome, Is.EqualTo("200"), weekly.ToString());
            Assert.That(weekly.Body!["to"]!.AsArray().Select(to => to!.GetValue<string>()), Is.EqualTo(new[] { "api-viewer@jfb.test" }));
            Assert.That(weekly.Body!["subject"]!.GetValue<string>(), Does.StartWith("Summary of entries, ").And.Contain(" to "), "the week before");
            Assert.That(weekly.Body!["html"]!.GetValue<string>(), Does.Contain("No entries arrived from "));
            Assert.That(daily.Body!["subject"]!.GetValue<string>(), Does.StartWith("Summary of entries, ").And.Not.Contain(" to "), "the day before, when none is named");
            Assert.That((wrong.Outcome, Path(wrong)), Is.EqualTo(("400 invalid_frequency", "frequency")), wrong.ToString());
            Assert.That(queuedByPreview, Is.Empty, "a preview sends nothing");
            Assert.That((test.Outcome, test.Body?["to"]?.GetValue<string>()), Is.EqualTo(("200", "api-viewer@jfb.test")), test.ToString());
            Assert.That((badTest.Outcome, Path(badTest)), Is.EqualTo(("400 invalid_frequency", "frequency")), badTest.ToString());
            Assert.That(queued.Select(mail => mail.To), Is.EqualTo(new[] { "api-viewer@jfb.test" }), "one mail, at once, even without entries");
            Assert.That(queued.Single().Subject, Does.StartWith("Test: Summary of entries, "));
        });
    }

    private static (string Frequency, string SendsAt, string TimeZone, string Address) Shown(ApiAnswer answer) =>
        (answer.Body?["frequency"]?.GetValue<string>() ?? string.Empty,
         answer.Body?["sendsAt"]?.GetValue<string>() ?? string.Empty,
         answer.Body?["timeZone"]?.GetValue<string>() ?? string.Empty,
         answer.Body?["address"]?.GetValue<string>() ?? string.Empty);

    private static string? Path(ApiAnswer answer) => answer.Body?["errors"]?[0]?["path"]?.GetValue<string>();

    private async Task<List<StoredEmail>> DigestsAsync()
    {
        var emails = GetRequiredService<IEmailRepository>();
        var found = new List<StoredEmail>();
        foreach (var id in await emails.DueAsync(100))
        {
            if (await emails.GetAsync(id) is { Kind: EmailKind.Digest } mail)
            {
                found.Add(mail);
            }
        }

        return found;
    }
}
