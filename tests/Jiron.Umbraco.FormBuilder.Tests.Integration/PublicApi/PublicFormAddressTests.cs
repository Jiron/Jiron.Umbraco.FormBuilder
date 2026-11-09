using System.Net;
using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Tests.Integration.Support;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.PublicApi;

/// <summary>
/// What an entry keeps of the visitor's IP address (ADR-0009, ADR-0224): nothing by default, the address with its end
/// removed or all of it as the site or the form chooses, a full address only where the host allows it, and nothing of a
/// server key's request, whose address is the site's own server.
/// </summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class PublicFormAddressTests : PublicApiTestBase
{
    private const string Body = """{"revision":1,"values":{"name":"Ada"}}""";

    private string? _address;

    private IEntryRepository Entries => GetRequiredService<IEntryRepository>();

    private IFormRepository FormRows => GetRequiredService<IFormRepository>();

    private IFormBuilderSettingsService Settings => GetRequiredService<IFormBuilderSettingsService>();

    private FormBuilderPrivacyOptions Host => GetRequiredService<IOptions<FormBuilderOptions>>().Value.Privacy;

    [SetUp]
    public void NoAddress() => _address = null;

    [TearDown]
    public void HostAsItWas() => Host.AllowFullIp = false;

    [Test]
    public async Task AnEntry_KeepsNoAddressByDefault_AndTheAddressWithItsEndRemoved_OnceTheSiteSaysSo()
    {
        var key = await PublishAsync();
        _address = "203.0.113.42";

        var before = await SendEntryAsync(key);
        await SiteAsync(IPHandling.Anonymised);
        var after = await SendEntryAsync(key);
        _address = "2001:db8:85a3:8d3:1319:8a2e:370:7348";
        var six = await SendEntryAsync(key);

        Assert.That(new[] { before, after, six }, Is.EqualTo(new string?[] { null, "203.0.113.0", "2001:db8:85a3::" }));
    }

    [Test]
    public async Task AFormsOwnChoice_BeatsTheSites_AndAFullAddress_IsKeptWhole_OnlyWhereTheHostAllowsIt()
    {
        await SiteAsync(IPHandling.Anonymised);
        var none = await PublishAsync("Kept nothing", change: definition => WithIP(definition, IPHandling.None));
        var full = await PublishAsync("Kept whole", change: definition => WithIP(definition, IPHandling.Full));
        _address = "203.0.113.42";

        var keptNothing = await SendEntryAsync(none);
        var refused = await SendEntryAsync(full);
        Host.AllowFullIp = true;
        var whole = await SendEntryAsync(full);

        Assert.Multiple(() =>
        {
            Assert.That(keptNothing, Is.Null);
            Assert.That(refused, Is.EqualTo("203.0.113.0"), "the host does not allow full addresses, so they are kept anonymised");
            Assert.That(whole, Is.EqualTo("203.0.113.42"));
        });
    }

    [Test]
    public async Task AServerKeysEntry_KeepsNoAddress_WhichIsTheSitesOwnServer()
    {
        Host.AllowFullIp = true;
        var key = await PublishAsync(change: definition => WithIP(definition, IPHandling.Full));
        _address = "198.51.100.7";
        SendsWithServerKey = true;

        var kept = await SendEntryAsync(key);

        Assert.That(kept, Is.Null);
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.Services.AddSingleton<IStartupFilter>(new RemoteAddressFilter(() => _address));
    }

    private static FormDefinition WithIP(FormDefinition definition, IPHandling ip) =>
        definition with { Settings = definition.Settings with { Privacy = definition.Settings.Privacy with { IP = ip } } };

    // Sends the form once; the address its new entry keeps.
    private async Task<string?> SendEntryAsync(Guid key)
    {
        var form = (await FormRows.GetAsync(key))!;
        var known = (await Entries.ListIdsAsync(form.Id, 50)).ToHashSet();
        using var sent = await SendAsync(HttpMethod.Post, $"{Root}/forms/{key}/entries", request => request.Content = new StringContent(Body, Encoding.UTF8, "application/json"));
        Assert.That(sent.StatusCode, Is.EqualTo(HttpStatusCode.OK), await sent.Content.ReadAsStringAsync());
        var made = (await Entries.ListIdsAsync(form.Id, 50)).Single(id => !known.Contains(id));
        return (await Entries.GetByIdAsync(made))!.IpAddress;
    }

    private async Task SiteAsync(IPHandling ip)
    {
        var current = await Settings.GetAsync<PrivacySettings>();
        _ = await Settings.SaveAsync(current.Value with { IP = ip }, current.Version, null);
    }
}
