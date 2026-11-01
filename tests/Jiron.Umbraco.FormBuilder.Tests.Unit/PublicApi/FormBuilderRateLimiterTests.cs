using System.Net;
using Jiron.Umbraco.FormBuilder.Core.Configuration;
using Jiron.Umbraco.FormBuilder.Web.PublicApi;
using Jiron.Umbraco.FormBuilder.Web.PublicApi.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.PublicApi;

/// <summary>
/// The rate limits (ADR-0012, ADR-0137): each policy's partitions with its defaults, the order they count in, a refusal's
/// time and whether it renders, the bound on what a flood can make a server keep, and the windows that roll out and are
/// swept. On a clock that stands still until a test moves it.
/// </summary>
[TestFixture]
public sealed class FormBuilderRateLimiterTests
{
    private static readonly Guid Form = Guid.Parse("71a00000-0000-4000-8000-000000000001");
    private static readonly Guid OtherForm = Guid.Parse("71a00000-0000-4000-8000-000000000002");

    private StoppedClock _clock = null!;

    [SetUp]
    public void SetUp() => _clock = new StoppedClock();

    [TestCase(FormBuilderRateLimitPolicies.Read, 120)]
    [TestCase(FormBuilderRateLimitPolicies.Challenge, 30)]
    [TestCase(FormBuilderRateLimitPolicies.Submit, 20)]
    [TestCase(FormBuilderRateLimitPolicies.Upload, 30)]
    [TestCase(FormBuilderRateLimitPolicies.Events, 120)]
    [TestCase(FormBuilderRateLimitPolicies.Verify, 10)]
    public void EachPolicy_HoldsItsDefaultPerAddressLimit(string policy, int limit)
    {
        var limiter = Limiter(NoSiteWideLimits);
        var address = Address("203.0.113.7");

        Assert.Multiple(() =>
        {
            // Without a form, so a form's lower limit does not come first.
            Assert.That(Count(limiter, address, policy, null, limit + 5), Is.EqualTo(limit));
            Assert.That(limiter.Take(Address("203.0.113.8"), policy, null).Allowed, Is.True, "another address counts by itself");
        });
    }

    [TestCase(FormBuilderRateLimitPolicies.Challenge, 10)]
    [TestCase(FormBuilderRateLimitPolicies.Submit, 5)]
    [TestCase(FormBuilderRateLimitPolicies.Upload, 20)]
    public void APolicyWithAFormsPartition_HoldsItPerAddressAndForm(string policy, int limit)
    {
        var limiter = Limiter(NoSiteWideLimits);
        var address = Address("203.0.113.7");

        Assert.Multiple(() =>
        {
            Assert.That(Count(limiter, address, policy, Form, limit + 3), Is.EqualTo(limit));
            Assert.That(limiter.Take(address, policy, OtherForm).Allowed, Is.True, "the same address on another form");
        });
    }

    [Test]
    public void Submitting_HoldsItsLimitPerForm_FromAnyAddress()
    {
        var limiter = Limiter(NoSiteWideLimits);

        var passed = Enumerable.Range(0, 70).Count(i => limiter.Take(Address($"203.0.113.{i}"), FormBuilderRateLimitPolicies.Submit, Form).Allowed);

        Assert.Multiple(() =>
        {
            Assert.That(passed, Is.EqualTo(60));
            Assert.That(limiter.Take(Address("198.51.100.1"), FormBuilderRateLimitPolicies.Submit, OtherForm).Allowed, Is.True, "another form counts by itself");
        });
    }

    [Test]
    public void Submitting_HoldsItsLimitPerHour_AsTheMinutesRollOut()
    {
        var limiter = Limiter();
        var address = Address("203.0.113.7");
        var passed = 0;
        for (var minute = 0; minute < 10; minute++)
        {
            passed += Count(limiter, address, FormBuilderRateLimitPolicies.Submit, null, 25);
            _clock.Advance(TimeSpan.FromMinutes(1));
        }

        Assert.That(passed, Is.EqualTo(100), "20 a minute would have let 200 through");
    }

    [TestCase(FormBuilderRateLimitPolicies.Read, 200)]
    [TestCase(FormBuilderRateLimitPolicies.Challenge, 100)]
    [TestCase(FormBuilderRateLimitPolicies.Submit, 50)]
    [TestCase(FormBuilderRateLimitPolicies.Upload, 20)]
    [TestCase(FormBuilderRateLimitPolicies.Events, 500)]
    public void EachPolicy_HoldsItsSiteWideLimit_WhateverTheAddresses(string policy, int limit)
    {
        var limiter = Limiter();

        var passed = Enumerable.Range(0, limit + 40).Count(i => limiter.Take(Address(Ip(i)), policy, null).Allowed);

        Assert.That(passed, Is.EqualTo(limit));
    }

    [Test]
    public void ALimitOfZero_IsLeftOut()
    {
        var limiter = Limiter(limits => limits.Verify.PerAddressPerMinute = 0);

        Assert.That(Count(limiter, Address("203.0.113.7"), FormBuilderRateLimitPolicies.Verify, null, 50), Is.EqualTo(50));
    }

    [Test]
    public void ARequestOneClientHasUsedUp_CountsTowardNoOneElsesLimit()
    {
        var limiter = Limiter(limits =>
        {
            limits.Submit.PerAddressPerMinute = 2;
            limits.Submit.PerAddressPerHour = 0;
            limits.Submit.PerAddressAndFormPerMinute = 0;
            limits.Submit.SitePerSecond = 5;
        });
        var flooder = Address("203.0.113.7");

        _ = Count(limiter, flooder, FormBuilderRateLimitPolicies.Submit, Form, 500);

        // Two of the flooder's five went through; a stranger's three still do.
        Assert.That(Enumerable.Range(0, 5).Count(i => limiter.Take(Address($"198.51.100.{i}"), FormBuilderRateLimitPolicies.Submit, Form).Allowed), Is.EqualTo(3));
    }

    [Test]
    public void ARequestALaterPartitionRefuses_GivesBackWhatEarlierOnesCounted()
    {
        var limiter = Limiter(limits =>
        {
            limits.Submit.PerAddressPerMinute = 3;
            limits.Submit.PerAddressPerHour = 0;
            limits.Submit.PerAddressAndFormPerMinute = 1;
            limits.Submit.PerFormPerMinute = 0;
            limits.Submit.SitePerSecond = 0;
        });
        var address = Address("203.0.113.7");

        _ = Count(limiter, address, FormBuilderRateLimitPolicies.Submit, Form, 40);

        // One on the form, and what the form refused gave its minute back, so two more fit on other forms.
        Assert.That(Count(limiter, address, FormBuilderRateLimitPolicies.Submit, OtherForm, 1) + Count(limiter, address, FormBuilderRateLimitPolicies.Submit, Guid.NewGuid(), 1), Is.EqualTo(2));
    }

    [Test]
    public void ARefusal_SaysWhenARequestFits_AndOneFitsThen()
    {
        var limiter = Limiter(limits =>
        {
            limits.Verify.PerAddressPerMinute = 1;
        });
        var address = Address("203.0.113.7");
        _ = limiter.Take(address, FormBuilderRateLimitPolicies.Verify, null);
        _clock.Advance(TimeSpan.FromSeconds(20));

        var refused = limiter.Take(address, FormBuilderRateLimitPolicies.Verify, null);
        Assert.That(refused.Allowed, Is.False);
        Assert.That(refused.RetryAfterSeconds, Is.InRange(34, 40), "the segment of the first request, 60 s after it, in steps of 6 s");

        _clock.Advance(refused.RetryAfter - TimeSpan.FromSeconds(1));
        Assert.That(limiter.Take(address, FormBuilderRateLimitPolicies.Verify, null).Allowed, Is.False, "not a second early");
        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.That(limiter.Take(address, FormBuilderRateLimitPolicies.Verify, null).Allowed, Is.True);
    }

    [Test]
    public void ARefusal_Renders_OnlyBelowTenTimesTheLimit()
    {
        var limiter = Limiter(limits =>
        {
            limits.Submit.PerAddressPerMinute = 0;
            limits.Submit.PerAddressPerHour = 0;
            limits.Submit.PerAddressAndFormPerMinute = 2;
            limits.Submit.PerFormPerMinute = 0;
            limits.Submit.SitePerSecond = 0;
        });
        var address = Address("203.0.113.7");
        var decisions = Enumerable.Range(0, 22).Select(_ => limiter.Take(address, FormBuilderRateLimitPolicies.Submit, Form)).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(decisions.Count(decision => decision.Allowed), Is.EqualTo(2));
            Assert.That(decisions.Take(2).All(decision => decision.Allowed), Is.True);
            Assert.That(decisions.Skip(2).Take(17).All(decision => decision.Renders), Is.True, "tried up to 19 times");
            Assert.That(decisions.Skip(19).Any(decision => decision.Renders), Is.False, "at 20 tries, ten times the limit, a bare 429");
        });
    }

    [Test]
    public void ASiteWideRefusal_NeverRenders()
    {
        var limiter = Limiter(limits => limits.Submit.SitePerSecond = 1);
        _ = limiter.Take(Address("203.0.113.7"), FormBuilderRateLimitPolicies.Submit, Form);

        var refused = limiter.Take(Address("203.0.113.8"), FormBuilderRateLimitPolicies.Submit, Form);

        Assert.Multiple(() =>
        {
            Assert.That(refused.Allowed, Is.False);
            Assert.That(refused.Renders, Is.False);
        });
    }

    [Test]
    public void AFloodOf20000Addresses_KeepsAtMostTheBound_AndTheOverflowIsLimited_NeverLetThrough()
    {
        const int Bound = 1_000;
        var limiter = Limiter(limits =>
        {
            limits.MaxTrackedClients = Bound;
            limits.Read.PerAddressPerMinute = 5;
            limits.Read.SitePerSecond = 0;
        });

        var passed = Enumerable.Range(0, 20_000).Count(i => limiter.Take(Address(Ip(i)), FormBuilderRateLimitPolicies.Read, null).Allowed);
        var next = limiter.Take(Address("198.51.100.9"), FormBuilderRateLimitPolicies.Read, null);

        Assert.Multiple(() =>
        {
            Assert.That(limiter.TrackedWindows, Is.LessThanOrEqualTo(Bound));
            Assert.That(passed, Is.EqualTo(Bound + 5), "each kept address once, then the shared overflow's limit");
            Assert.That(next.Allowed, Is.False, "fails closed");
            Assert.That(next.Renders, Is.False, "an overflow's refusal costs no render");
        });
    }

    [Test]
    public void ARefusalInTheOverflow_NeverRenders_EvenJustOverItsLimit()
    {
        var limiter = Limiter(limits =>
        {
            limits.MaxTrackedClients = 1_000;
            limits.Read.PerAddressPerMinute = 5;
            limits.Read.SitePerSecond = 0;
        });
        _ = Enumerable.Range(0, 1_000).Count(i => limiter.Take(Address(Ip(i)), FormBuilderRateLimitPolicies.Read, null).Allowed);
        var late = Address("198.51.100.9");
        Assert.That(Count(limiter, late, FormBuilderRateLimitPolicies.Read, null, 5), Is.EqualTo(5), "the overflow's own limit");

        var refused = limiter.Take(late, FormBuilderRateLimitPolicies.Read, null);

        Assert.Multiple(() =>
        {
            Assert.That(refused.Allowed, Is.False);
            Assert.That(refused.Renders, Is.False, "the sixth of a limit of five: not a client's own window");
        });
    }

    [Test]
    public void AnAddressAlreadyKept_KeepsItsOwnWindow_ThroughAFlood()
    {
        var limiter = Limiter(limits =>
        {
            limits.MaxTrackedClients = 1_000;
            limits.Read.PerAddressPerMinute = 5;
            limits.Read.SitePerSecond = 0;
        });
        var known = Address("198.51.100.9");
        _ = limiter.Take(known, FormBuilderRateLimitPolicies.Read, null);

        _ = Enumerable.Range(0, 5_000).Count(i => limiter.Take(Address(Ip(i)), FormBuilderRateLimitPolicies.Read, null).Allowed);

        Assert.That(limiter.Take(known, FormBuilderRateLimitPolicies.Read, null).Allowed, Is.True);
    }

    [Test]
    public void InventedFormKeys_CountTowardTheBoundToo()
    {
        var limiter = Limiter(limits =>
        {
            limits.MaxTrackedClients = 1_000;
            limits.Submit.PerAddressPerMinute = 0;
            limits.Submit.PerAddressPerHour = 0;
            limits.Submit.PerAddressAndFormPerMinute = 0;
            limits.Submit.PerFormPerMinute = 5;
            limits.Submit.SitePerSecond = 0;
        });
        var address = Address("203.0.113.7");

        var passed = Enumerable.Range(0, 5_000).Count(_ => limiter.Take(address, FormBuilderRateLimitPolicies.Submit, Guid.NewGuid()).Allowed);

        Assert.Multiple(() =>
        {
            Assert.That(limiter.TrackedWindows, Is.LessThanOrEqualTo(1_000));
            Assert.That(passed, Is.EqualTo(1_005));
        });
    }

    [Test]
    public void AWindowUnusedForItsLength_Retires_AndCountsNothingMore()
    {
        var window = new RateWindow(5, TimeSpan.FromMinutes(1), _clock);
        var inUse = window.TryRetire();
        _ = window.TryTake(out _, out _);
        _clock.Advance(TimeSpan.FromMinutes(1) - TimeSpan.FromTicks(1));
        var justBefore = window.TryRetire();
        _clock.Advance(TimeSpan.FromTicks(1));

        var retires = window.TryRetire();
        var fits = window.TryTake(out var wait, out var retired);

        Assert.Multiple(() =>
        {
            Assert.That(inUse, Is.False, "a new window has been used just now");
            Assert.That(justBefore, Is.False, "not before its whole length");
            Assert.That(retires, Is.True);
            Assert.That(fits, Is.False);
            Assert.That(retired, Is.True);
            Assert.That(wait, Is.EqualTo(TimeSpan.Zero));
            Assert.That(window.Tried, Is.Zero, "the request is not counted here");
            Assert.That(window.TryRetire(), Is.True, "and it stays ended");
        });
    }

    [Test]
    public void AWindowThatStillCounts_DoesNotRetire()
    {
        var window = new RateWindow(5, TimeSpan.FromMinutes(1), _clock);
        _ = window.TryTake(out _, out _);
        _clock.Advance(TimeSpan.FromSeconds(30));
        _ = window.TryTake(out _, out _);
        _clock.Advance(TimeSpan.FromSeconds(30));

        Assert.That(window.TryRetire(), Is.False, "its second request still counts");
    }

    [Test]
    public void RequestsThatMeetTheSweep_EachCount_AndTheNextWindowHoldsThemAll()
    {
        const int Limit = 40;
        const int AtOnce = 8;
        var limiter = Limiter(limits =>
        {
            limits.Submit.PerAddressPerMinute = 0;
            limits.Submit.PerAddressPerHour = 0;
            limits.Submit.PerFormPerMinute = 0;
            limits.Submit.SitePerSecond = 0;
            limits.Submit.PerAddressAndFormPerMinute = Limit;
        });
        var address = Address("203.0.113.7");

        for (var round = 0; round < 500; round++)
        {
            // The window has been empty for its whole length, and the sweep is due: the first of them starts it.
            _clock.Advance(TimeSpan.FromMinutes(2));
            Parallel.For(0, AtOnce, new ParallelOptions { MaxDegreeOfParallelism = AtOnce }, _ => limiter.Take(address, FormBuilderRateLimitPolicies.Submit, Form));

            Assert.That(Count(limiter, address, FormBuilderRateLimitPolicies.Submit, Form, Limit), Is.EqualTo(Limit - AtOnce), $"round {round}: none of them was lost");
            Assert.That(limiter.TrackedWindows, Is.EqualTo(1), $"round {round}");
        }
    }

    [Test]
    public void TheEmptyKey_NamesNoForm_AndCannotFillTheBoundBySweeping()
    {
        var limiter = Limiter(limits =>
        {
            limits.Submit.PerAddressPerMinute = 0;
            limits.Submit.PerAddressPerHour = 0;
            limits.Submit.PerAddressAndFormPerMinute = 0;
            limits.Submit.PerFormPerMinute = 1;
            limits.Submit.SitePerSecond = 0;
        });
        var address = Address("203.0.113.7");

        Assert.Multiple(() =>
        {
            Assert.That(Count(limiter, address, FormBuilderRateLimitPolicies.Submit, Guid.Empty, 3), Is.EqualTo(3), "no form's limit applies");
            Assert.That(limiter.TrackedWindows, Is.Zero);
        });
    }

    [Test]
    public void EmptiedWindows_AreSwept_AndWindowsStillCounting_AreNot()
    {
        var limiter = Limiter();
        foreach (var i in Enumerable.Range(0, 50))
        {
            _ = limiter.Take(Address(Ip(i)), FormBuilderRateLimitPolicies.Read, null);
        }

        var kept = limiter.TrackedWindows;
        _clock.Advance(TimeSpan.FromSeconds(30));
        _ = limiter.Take(Address("198.51.100.9"), FormBuilderRateLimitPolicies.Read, null);
        Assert.That(limiter.TrackedWindows, Is.EqualTo(kept + 1), "no sweep inside its minute");

        _clock.Advance(TimeSpan.FromMinutes(1));
        _ = limiter.Take(Address("198.51.100.10"), FormBuilderRateLimitPolicies.Read, null);
        Assert.That(limiter.TrackedWindows, Is.LessThanOrEqualTo(2), "the 50 emptied ones gone, a counting one kept");

        _clock.Advance(TimeSpan.FromMinutes(3));
        _ = limiter.Take(Address("198.51.100.11"), FormBuilderRateLimitPolicies.Read, null);
        Assert.That(limiter.TrackedWindows, Is.EqualTo(1), "only the newest is left");
    }

    [Test]
    public void AFloodThatEnds_LeavesTheBoundFreeForNewAddresses()
    {
        var limiter = Limiter(limits =>
        {
            limits.MaxTrackedClients = 1_000;
            limits.Read.PerAddressPerMinute = 1;
            limits.Read.SitePerSecond = 0;
        });
        _ = Enumerable.Range(0, 3_000).Count(i => limiter.Take(Address(Ip(i)), FormBuilderRateLimitPolicies.Read, null).Allowed);

        _clock.Advance(TimeSpan.FromMinutes(3));

        Assert.Multiple(() =>
        {
            Assert.That(limiter.Take(Address("198.51.100.9"), FormBuilderRateLimitPolicies.Read, null).Allowed, Is.True);
            Assert.That(limiter.TrackedWindows, Is.EqualTo(1));
        });
    }

    [Test]
    public void AServerKey_CountsInItsOwnPartition_WithItsFactor_AndNeverTouchesItsAddresss()
    {
        var limiter = Limiter(limits =>
        {
            limits.ServerKeyFactor = 3;
            limits.Submit.PerAddressPerMinute = 2;
            limits.Submit.PerAddressPerHour = 0;
            limits.Submit.PerAddressAndFormPerMinute = 0;
            limits.Submit.PerFormPerMinute = 0;
            limits.Submit.SitePerSecond = 0;
        });
        var signed = Address("203.0.113.7");
        signed.Features.Set(new ServerKeyRequest("shop"));
        var other = Address("203.0.113.7");
        other.Features.Set(new ServerKeyRequest("app"));
        var anonymous = Address("203.0.113.7");

        Assert.Multiple(() =>
        {
            Assert.That(Count(limiter, signed, FormBuilderRateLimitPolicies.Submit, Form, 10), Is.EqualTo(6), "the key's limit holds");
            Assert.That(Count(limiter, other, FormBuilderRateLimitPolicies.Submit, Form, 1), Is.EqualTo(1), "each key by itself");
            Assert.That(Count(limiter, anonymous, FormBuilderRateLimitPolicies.Submit, Form, 10), Is.EqualTo(2), "the address's is untouched, and has no factor");
        });
    }

    [Test]
    public void TheFormAndSiteLimits_HoldForAServerKeyToo_WithoutItsFactor()
    {
        var limiter = Limiter(limits =>
        {
            limits.ServerKeyFactor = 100;
            limits.Submit.PerAddressPerMinute = 0;
            limits.Submit.PerAddressPerHour = 0;
            limits.Submit.PerAddressAndFormPerMinute = 0;
            limits.Submit.PerFormPerMinute = 3;
            limits.Submit.SitePerSecond = 0;
        });
        var signed = Address("203.0.113.7");
        signed.Features.Set(new ServerKeyRequest("shop"));

        Assert.That(Count(limiter, signed, FormBuilderRateLimitPolicies.Submit, Form, 10), Is.EqualTo(3));
    }

    [TestCase("2001:db8:1:2:aaaa::1", "2001:db8:1:2:bbbb::2", true)]
    [TestCase("2001:db8:1:2::1", "2001:db8:1:3::1", false)]
    [TestCase("::ffff:203.0.113.7", "203.0.113.7", true)]
    [TestCase("203.0.113.7", "203.0.113.8", false)]
    public void AnIPv6Address_CountsAsItsNetwork_AndAMappedOne_AsIPv4(string one, string other, bool same)
    {
        var limiter = Limiter(limits =>
        {
            limits.Verify.PerAddressPerMinute = 1;
        });
        _ = limiter.Take(Address(one), FormBuilderRateLimitPolicies.Verify, null);

        Assert.That(limiter.Take(Address(other), FormBuilderRateLimitPolicies.Verify, null).Allowed, Is.EqualTo(!same));
    }

    [Test]
    public void ARequestWithNoAddress_CountsAsOneUnknownClient()
    {
        var limiter = Limiter(limits => limits.Verify.PerAddressPerMinute = 1);

        Assert.That(Count(limiter, new DefaultHttpContext(), FormBuilderRateLimitPolicies.Verify, null, 3), Is.EqualTo(1));
    }

    [Test]
    public void AClockThatGoesBack_FreesNothingEarly()
    {
        var window = new RateWindow(1, TimeSpan.FromMinutes(1), _clock);
        Assert.That(window.TryTake(out _, out _), Is.True);

        _clock.Advance(TimeSpan.FromMinutes(-5));

        Assert.That(window.TryTake(out _, out _), Is.False);
    }

    [Test]
    public void AWindow_RollsOutSegmentBySegment_AndGivesBackTheNewest()
    {
        var window = new RateWindow(2, TimeSpan.FromSeconds(100), _clock);
        Assert.That(window.TryTake(out _, out _), Is.True);
        _clock.Advance(TimeSpan.FromSeconds(50));
        Assert.That(window.TryTake(out _, out _), Is.True);
        Assert.That(window.TryTake(out var after, out _), Is.False);
        Assert.That(after, Is.EqualTo(TimeSpan.FromSeconds(50)), "when the first rolls out");

        window.Release();
        Assert.That(window.TryTake(out _, out _), Is.True, "what was given back fits again");
        Assert.That(window.TryTake(out _, out _), Is.False);

        _clock.Advance(TimeSpan.FromSeconds(50));
        Assert.Multiple(() =>
        {
            Assert.That(window.TryTake(out _, out _), Is.True, "the first has rolled out");
            Assert.That(window.TryTake(out _, out _), Is.False, "the second has not");
        });
    }

    // The clock stands still, so a site-wide limit of a second would come before a higher one of a minute.
    private static void NoSiteWideLimits(FormBuilderLimitsOptions limits)
    {
        foreach (var policy in new[] { limits.Read, limits.Challenge, limits.Submit, limits.Upload, limits.Events, limits.Verify })
        {
            policy.SitePerSecond = 0;
        }
    }

    private FormBuilderRateLimiter Limiter(Action<FormBuilderLimitsOptions>? change = null)
    {
        var options = new FormBuilderOptions();
        change?.Invoke(options.Limits);
        return new FormBuilderRateLimiter(Options.Create(options), _clock);
    }

    private static int Count(FormBuilderRateLimiter limiter, HttpContext http, string policy, Guid? form, int tries) =>
        Enumerable.Range(0, tries).Count(_ => limiter.Take(http, policy, form).Allowed);

    private static DefaultHttpContext Address(string address)
    {
        var http = new DefaultHttpContext();
        http.Connection.RemoteIpAddress = IPAddress.Parse(address);
        return http;
    }

    private static string Ip(int i) => $"10.{(i >> 16) & 255}.{(i >> 8) & 255}.{i & 255}";

    // A clock that stands still until a test moves it.
    private sealed class StoppedClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
