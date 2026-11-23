using Jiron.Umbraco.FormBuilder.Logic;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Logic;

/// <summary>
/// The older names of time zones as a server's time zone database links them (ROADMAP 26.1), read from a
/// <c>tzdata.zi</c> written here, so the test is the same on every system: followed to the end in their exact spelling,
/// parsed once per server and again only when the file changed, and a missing file costs nothing per call.
/// </summary>
public sealed class SystemTimeZoneLinksTests
{
    private const string Database = """
        # version 2026e
        R Ar 1930 o - D 1 0 1 -
        Z America/Argentina/Buenos_Aires -3:53:48 - LMT 1894 O 31
        -3 Ar -03/-02
        Z Asia/Kolkata 5:53:28 - LMT 1854 Jun 28
        5:30 - IST
        L America/Argentina/Buenos_Aires America/Buenos_Aires
        L Asia/Kolkata Asia/Calcutta
        L Asia/Calcutta Asia/Old_Calcutta
        L Bad
        Link Asia/Kolkata Asia/Bombay
        """;

    private string _folder = null!;
    private long _now;

    [SetUp]
    public void SetUp()
    {
        _folder = Directory.CreateTempSubdirectory("jfb-tz-").FullName;
        Write(Database);
        _now = 1_000;
    }

    [TearDown]
    public void TearDown() => Directory.Delete(_folder, recursive: true);

    [Test]
    public void AnOlderName_LeadsToItsZone_ThroughEveryLink_InItsExactSpelling()
    {
        var links = Links();

        Assert.Multiple(() =>
        {
            Assert.That(links.Target("America/Buenos_Aires"), Is.EqualTo("America/Argentina/Buenos_Aires"));
            Assert.That(links.Target("Asia/Old_Calcutta"), Is.EqualTo("Asia/Kolkata"), "a link to a link");
            Assert.That(links.Target("asia/calcutta"), Is.Null, "another letter case");
            Assert.That(links.Target("Asia/Kolkata"), Is.Null, "a zone links nowhere");
            Assert.That(links.Target("Asia/Bombay"), Is.Null, "only the database's short form of a link counts");
            Assert.That(links.Target("Bad"), Is.Null);
            Assert.That(links.Target(string.Empty), Is.Null);
        });
    }

    [Test]
    public void TheFile_IsParsedOnce_ForManyLookups()
    {
        var links = Links();

        for (var i = 0; i < 1_000; i++)
        {
            _ = links.Target(i % 2 == 0 ? "Asia/Calcutta" : "Europe/Berlin");
            _now += 50;
        }

        Assert.That(links.Reads, Is.EqualTo(1));
    }

    [Test]
    public void AChangedFile_IsParsedAgain_OnceItsStampIsLookedAtAgain()
    {
        var links = Links();
        Assert.That(links.Target("Asia/Saigon"), Is.Null);

        Write(Database + "\nL Asia/Ho_Chi_Minh Asia/Saigon\n");
        File.SetLastWriteTimeUtc(Path.Combine(_folder, "tzdata.zi"), DateTime.UtcNow.AddMinutes(1));
        var soon = links.Target("Asia/Saigon");
        _now += SystemTimeZoneLinks.RecheckMilliseconds;

        Assert.Multiple(() =>
        {
            Assert.That(soon, Is.Null, "the stamp is looked at again at most once a minute");
            Assert.That(links.Target("Asia/Saigon"), Is.EqualTo("Asia/Ho_Chi_Minh"), "seen without a restart");
            Assert.That(links.Reads, Is.EqualTo(2));
            _now += SystemTimeZoneLinks.RecheckMilliseconds;
            Assert.That(links.Target("Asia/Calcutta"), Is.EqualTo("Asia/Kolkata"));
            Assert.That(links.Reads, Is.EqualTo(2), "an unchanged stamp parses nothing");
        });
    }

    [Test]
    public void ASystemWithoutTheFile_LinksNothing_AtNoCostPerCall()
    {
        var missing = new SystemTimeZoneLinks(Path.Combine(_folder, "missing"), () => _now);
        var windows = new SystemTimeZoneLinks(null, () => _now);

        for (var i = 0; i < 1_000; i++)
        {
            Assert.That(missing.Target("Asia/Calcutta"), Is.Null);
            Assert.That(windows.Target("Asia/Calcutta"), Is.Null, "Windows, which finds such names through ICU");
        }

        Assert.That((missing.Reads, windows.Reads), Is.EqualTo((0, 0)));
    }

    [Test]
    public void ANameWithAFileOfItsOwn_IsNotFollowed_SoUtcStaysUtc()
    {
        // The database links UTC to Etc/UTC, and every system has a file for both; a system with its package of older
        // names has a file for each of them too. .NET finds such a name itself, under its own id.
        Write(Database + "\nL Etc/UTC UTC\n");
        File.WriteAllText(Path.Combine(_folder, "UTC"), string.Empty);
        var links = Links();

        Assert.Multiple(() =>
        {
            Assert.That(links.Target("UTC"), Is.Null);
            Assert.That(CalendarValues.FindTimeZone("UTC", links)?.Id, Is.EqualTo("UTC"));
            Assert.That(links.Target("Asia/Calcutta"), Is.EqualTo("Asia/Kolkata"), "a name without its file still is");
        });
    }

    [Test]
    public void ALoopOfLinks_LeadsToNoZone()
    {
        Write("L A/B A/C\nL A/C A/D\nL A/D A/B\nL Z/Z A/B\n");

        Assert.Multiple(() =>
        {
            Assert.That(Links().Target("A/B"), Is.Null);
            Assert.That(Links().Target("Z/Z"), Is.Null, "a name that leads into a loop");
        });
    }

    [Test]
    public void AStoredName_FindsItsZone_ThroughTheLinks_AndAZonesOwnNameAsBefore()
    {
        // A system without the older name's file finds it through the link; every system finds the zone's own name.
        var links = Links();
        var kolkata = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");

        Assert.Multiple(() =>
        {
            Assert.That(CalendarValues.FindTimeZone("Asia/Old_Calcutta", links)?.BaseUtcOffset, Is.EqualTo(kolkata.BaseUtcOffset));
            Assert.That(CalendarValues.FindExactTimeZone("Asia/Old_Calcutta", links), Is.Not.Null, "a name the system links is spelled as it knows it");
            Assert.That(CalendarValues.FindTimeZone("Europe/Berlin", links)?.Id, Is.EqualTo("Europe/Berlin"));
            Assert.That(CalendarValues.FindExactTimeZone("asia/old_calcutta", links), Is.Null);
            Assert.That(CalendarValues.FindTimeZone("+01:00", links), Is.Null);
        });
    }

    private SystemTimeZoneLinks Links() => new(_folder, () => _now);

    private void Write(string text) => File.WriteAllText(Path.Combine(_folder, "tzdata.zi"), text.ReplaceLineEndings("\n"));
}
