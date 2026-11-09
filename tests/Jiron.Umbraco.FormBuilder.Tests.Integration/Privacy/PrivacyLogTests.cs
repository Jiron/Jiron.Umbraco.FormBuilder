using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Email;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Emails;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Privacy;

/// <summary>
/// What the logs never hold (ADR-0009, ADR-0225): a visitor's answers, their address, a sensitive answer, one kept only until
/// the actions have run, or their IP address, from the post through the entry, its mail and its actions, even where an action
/// fails with an exception or an answer that names them.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class PrivacyLogTests : EmailTestBase
{
    private const string Name = "Grace 7f3c Hopper";
    private const string Address = "grace.7f3c@example.com";
    private const string Secret = "DE89 3704 0044 0532 0130 00";
    private const string Kept = "kept 9b1e for the actions";
    private const string Visitor = "198.51.100.23";

    private static readonly FormField EmailField = new() { Key = Guid.Parse("e2250000-0000-4000-8000-000000000001"), Type = "email", Alias = "email", Label = new LocalizedText("Email") };

    private static readonly FormField Iban = new() { Key = Guid.Parse("e2250000-0000-4000-8000-000000000002"), Type = "text", Alias = "iban", Label = new LocalizedText("IBAN"), Sensitive = true };

    private static readonly FormField Note = new() { Key = Guid.Parse("e2250000-0000-4000-8000-000000000003"), Type = "textarea", Alias = "note", Label = new LocalizedText("Note"), Keep = FieldKeep.UntilActionsRun };

    private string? _address;

    [Test]
    public async Task AVisitorsAnswersAddressAndIP_ReachNoLog_ThroughTheEntryItsMailAndActionsThatFail()
    {
        GetRequiredService<IOptions<FormBuilderOptions>>().Value.Privacy.AllowFullIp = true;
        var mail = new FormAction
        {
            Key = Guid.NewGuid(),
            Type = EmailActionType.Alias,
            Name = "Thank you",
            Settings = SettingsObject.Parse($$$"""{"to":[],"toField":"{{{EmailField.Key}}}","subject":{"":"Thanks, {{ field.name }}"},"body":{"":"<p>{{ field.note }} {{ field.iban }} {{ field.email }}</p>"}}"""),
        };
        var failing = Action("boom");
        var retrying = Action("tired");
        var tries = 0;
        Scripted.Scripts["boom"] = (_, _) => throw new InvalidOperationException($"Could not reach {Address} about {Secret} and {Kept} from {Visitor}");
        Scripted.Scripts["tired"] = (_, _) => Task.FromResult(tries++ == 0 ? ActionResult.TryAgain("http_503", $"Busy with {Name}, {Address}") : ActionResult.Succeeded);
        var form = await PublishAsync(
            change: definition => definition with { Actions = [mail, failing, retrying], Settings = definition.Settings with { Privacy = definition.Settings.Privacy with { IP = IPHandling.Full } } },
            fields: [NameField, EmailField, Iban, Note]);
        _address = Visitor;

        var entry = await SubmitAsync(form, $$"""{"name":"{{Name}}","email":"{{Address}}","iban":"{{Secret}}","note":"{{Kept}}"}""");
        await DrainAsync(entry.Id);
        await SendDueAsync();
        Clock.Advance(TimeSpan.FromMinutes(5));
        await DrainAsync(entry.Id);
        await SendDueAsync();

        var logs = GetRequiredService<LogCapture>().Entries;
        var written = logs.Select(log => $"{log.Category} {log.Message} {log.Exception}").ToList();
        Assert.Multiple(() =>
        {
            Assert.That(entry.IpAddress, Is.EqualTo(Visitor), "the entry keeps it, as the form says");
            Assert.That(Written(), Has.Count.EqualTo(1), "the mail went");
            Assert.That(logs.Where(log => log.Level >= LogLevel.Warning).Select(log => log.Category), Has.Some.Contains("Action"), "the failing action was logged");
            foreach (var text in new[] { Name, Address, Secret, Kept, Visitor })
            {
                Assert.That(written.Where(line => line.Contains(text, StringComparison.OrdinalIgnoreCase)), Is.Empty, text);
            }
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.Services.AddLogCapture();
        builder.Services.AddSingleton<IStartupFilter>(new RemoteAddressFilter(() => _address));
    }

    [TearDown]
    public void HostAsItWas() => GetRequiredService<IOptions<FormBuilderOptions>>().Value.Privacy.AllowFullIp = false;
}
