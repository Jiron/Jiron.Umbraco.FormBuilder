using Jiron.Umbraco.FormBuilder.Core.Settings;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using Jiron.Umbraco.FormBuilder.Tests.Unit.OptionLists;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Settings;

/// <summary>
/// The time zone a site's forms run in (ADR-0043, ADR-0084): the stored zone of the site, and UTC for one this server
/// cannot resolve, with a warning that names the setting at most once an hour per site and zone, kept within a bound.
/// </summary>
[TestFixture]
public sealed class SiteTimeZonesTests
{
    private static readonly Guid Shop = Guid.Parse("5a1e0000-0000-4000-8000-000000000001");
    private static readonly Guid Blog = Guid.Parse("5a1e0000-0000-4000-8000-000000000002");

    private readonly Dictionary<Guid, string> _zones = [];
    private TestTimeProvider _clock = null!;
    private RecordingLogger<SiteTimeZones> _logger = null!;
    private SiteTimeZones _timeZones = null!;

    [SetUp]
    public void CreateTheSites()
    {
        _zones.Clear();
        _clock = new TestTimeProvider(new DateTimeOffset(2026, 10, 28, 9, 0, 0, TimeSpan.Zero));
        _logger = new RecordingLogger<SiteTimeZones>();
        var settings = Substitute.For<IFormBuilderSettingsService>();
        settings.GetAsync<GeneralSettings>(Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var zone = call.ArgAt<Guid?>(0) is { } site && _zones.TryGetValue(site, out var stored) ? stored : GeneralSettings.DefaultTimeZone;
            return Task.FromResult(new SettingsSnapshot<GeneralSettings>(new GeneralSettings { TimeZone = zone }, 1, new HashSet<string>(), new Dictionary<string, SettingsLock>()));
        });
        _timeZones = new SiteTimeZones(settings, _clock, _logger);
    }

    [Test]
    public async Task ASitesZone_IsTheOneItStores_AndTheGlobalOneIsUtcByDefault()
    {
        _zones[Shop] = "Pacific/Auckland";

        Assert.Multiple(async () =>
        {
            Assert.That((await _timeZones.GetAsync(Shop)).Id, Is.EqualTo("Pacific/Auckland"));
            Assert.That(await _timeZones.GetAsync(null), Is.EqualTo(TimeZoneInfo.Utc));
            Assert.That(await _timeZones.GetAsync(Blog), Is.EqualTo(TimeZoneInfo.Utc));
            Assert.That(_logger.Entries, Is.Empty);
        });
    }

    [Test]
    public async Task AZoneThisServerCannotResolve_IsUtc_WithAWarningAtMostOnceAnHour()
    {
        _zones[Shop] = "Mars/Olympus_Mons";
        var zones = new List<TimeZoneInfo>();
        var warnings = new List<int>();
        foreach (var later in new[] { TimeSpan.Zero, TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(49), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(30) })
        {
            _clock.Advance(later);
            zones.Add(await _timeZones.GetAsync(Shop));
            warnings.Add(_logger.Entries.Count);
        }

        Assert.Multiple(() =>
        {
            Assert.That(zones, Is.All.EqualTo(TimeZoneInfo.Utc));
            Assert.That(warnings, Is.EqualTo(new[] { 1, 1, 1, 2, 2 }), "again an hour after the first");
            Assert.That(_logger.Entries.Select(entry => entry.Level), Is.All.EqualTo(LogLevel.Warning));
            Assert.That(_logger.Entries[0].Message, Is.EqualTo($"The setting general.timeZone of the site {Shop} names the time zone 'Mars/Olympus_Mons', which this server cannot resolve, so its forms run in UTC. Choose the site's time zone again in the Forms section's settings."));
        });
    }

    [Test]
    public async Task EachSiteAndZone_IsWarnedAboutOnItsOwn()
    {
        _zones[Shop] = "Mars/Olympus_Mons";
        _zones[Blog] = "Mars/Olympus_Mons";
        await _timeZones.GetAsync(Shop);
        await _timeZones.GetAsync(Blog);
        _zones[Shop] = "Moon/Tranquility";
        await _timeZones.GetAsync(Shop);
        await _timeZones.GetAsync(Shop);

        Assert.That(
            _logger.Entries.Select(entry => (entry.Message.Contains(Shop.ToString(), StringComparison.Ordinal), entry.Message.Contains("Moon/", StringComparison.Ordinal))),
            Is.EqualTo(new[] { (true, false), (false, false), (true, true) }),
            "the shop, the blog with the same zone, the shop with another; the repeat is quiet");
    }

    [Test]
    public async Task TheSitesWarnedAbout_StayWithinABound_AndARepeatAfterItWarnsAgain()
    {
        _zones[Shop] = "Mars/Olympus_Mons";
        await _timeZones.GetAsync(Shop);
        for (var i = 0; i < 256; i++)
        {
            _zones[Blog] = $"Mars/Crater_{i}";
            await _timeZones.GetAsync(Blog);
        }

        await _timeZones.GetAsync(Shop);

        Assert.That(_logger.Entries.Count(entry => entry.Message.Contains(Shop.ToString(), StringComparison.Ordinal)), Is.EqualTo(2), "forgotten as the bound emptied the list");
    }
}
