using System.Text;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Insights;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Jiron.Umbraco.FormBuilder.Web.PublicApi;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using NSubstitute;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.PublicApi;

/// <summary>
/// A beacon's request as the events endpoint reads it (ADR-0211), where a test server does not reach: a body past the
/// limit that no count of the host's stopped is refused with 413 and counts nothing, and one within it counts.
/// </summary>
public sealed class InsightRequestsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 7, 9, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task ABodyPastTheLimit_ThatNoCountStopped_Is413_AndCountsNothing()
    {
        var counts = new InsightCounts(new TestTimeProvider(Now));
        var requests = new InsightRequests(new FormInsights(Settings(), counts));
        var form = new PublishedForm(
            new FormInfo(Guid.NewGuid(), "contact", "Contact", null, 1, true, 1, false, null, Now, Now, null, null),
            new FormDefinition { Key = Guid.NewGuid(), Pages = [new FormPage { Key = Guid.NewGuid() }] },
            1);
        var past = Encoding.UTF8.GetBytes("{\"type\":\"view\",\"pad\":\"" + new string('x', FormInsights.MaxBeaconBytes) + "\"}");

        var refused = await requests.CountAsync(Http(past), form, null, CancellationToken.None);
        var counted = await requests.CountAsync(Http("{\"type\":\"view\"}"u8.ToArray()), form, null, CancellationToken.None);

        Assert.Multiple(() =>
        {
            Assert.That((refused as IStatusCodeActionResult)?.StatusCode, Is.EqualTo(StatusCodes.Status413PayloadTooLarge));
            Assert.That(counted, Is.InstanceOf<NoContentResult>());
            Assert.That(counts.Take().Values.Sum(), Is.EqualTo(1), "the beacon within the limit alone");
        });
    }

    // A request whose body has no declared length, as a host that counts nothing passes it on.
    private static DefaultHttpContext Http(byte[] body)
    {
        var http = new DefaultHttpContext();
        http.Request.Protocol = "HTTP/1.1";
        http.Request.Body = new MemoryStream(body);
        return http;
    }

    private static IFormBuilderSettingsService Settings()
    {
        var settings = Substitute.For<IFormBuilderSettingsService>();
        settings.GetAsync<PrivacySettings>(Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(new SettingsSnapshot<PrivacySettings>(new PrivacySettings { Insights = true }, 1, new HashSet<string>(), new Dictionary<string, SettingsLock>()));
        return settings;
    }
}
