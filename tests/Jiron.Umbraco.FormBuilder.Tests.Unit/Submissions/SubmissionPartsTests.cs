using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Entries;
using Jiron.Umbraco.FormBuilder.Core.Forms;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Jiron.Umbraco.FormBuilder.Testing.Time;
using NSubstitute;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Submissions;

/// <summary>
/// The parts of the submission pipeline that stand alone (ADR-0088): the digest that tells a retry from other answers,
/// whether a form's times let it take an entry, which entries count toward its limit, and the results the pipeline
/// gives.
/// </summary>
public sealed class SubmissionPartsTests
{
    private static readonly Guid Form = Guid.Parse("f0000000-0000-4000-8000-000000000088");

    private static readonly SubmissionRequest Request = new()
    {
        Form = Form,
        Revision = 2,
        Culture = "en-US",
        ContentKey = Guid.Parse("c0000000-0000-4000-8000-000000000088"),
        Fields = new Dictionary<string, PostedAnswer>
        {
            ["name"] = new(["Ada"]),
            ["topics"] = new(["a", "b"]),
            ["address"] = new([], new Dictionary<string, IReadOnlyList<string?>> { ["city"] = ["Bern"], ["country"] = ["CH"] }),
        },
        Rows = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, PostedAnswer>>>
        {
            ["guests"] = [new Dictionary<string, PostedAnswer> { ["guestName"] = new(["Grace"]) }],
        },
        Query = new Dictionary<string, IReadOnlyList<string>> { ["source"] = ["news"] },
        Context = new Dictionary<string, string> { ["campaign"] = "autumn" },
    };

    [Test]
    public void TheDigest_IsTheSameForTheSameAnswersInAnyOrder_AndLeavesOutTheIdAndTheTokens()
    {
        var reordered = Request with
        {
            Fields = new Dictionary<string, PostedAnswer>
            {
                ["address"] = new([], new Dictionary<string, IReadOnlyList<string?>> { ["country"] = ["CH"], ["city"] = ["Bern"] }),
                ["topics"] = new(["a", "b"]),
                ["name"] = new(["Ada"]),
            },
            SubmissionId = Guid.NewGuid(),
            Tokens = Substitute.For<IUploadTokenReader>(),
        };

        Assert.Multiple(() =>
        {
            Assert.That(SubmissionDigest.Of(Request), Does.Match("^[0-9a-f]{64}$"));
            Assert.That(SubmissionDigest.Of(reordered), Is.EqualTo(SubmissionDigest.Of(Request)));
        });
    }

    [Test]
    public void TheDigest_ChangesWithEveryPartOfTheAnswers()
    {
        var changes = new Dictionary<string, SubmissionRequest>
        {
            ["the revision"] = Request with { Revision = 3 },
            ["the culture"] = Request with { Culture = "de" },
            ["no culture"] = Request with { Culture = null },
            ["the page"] = Request with { ContentKey = Guid.NewGuid() },
            ["an answer"] = Request with { Fields = new Dictionary<string, PostedAnswer>(Request.Fields) { ["name"] = new(["Grace"]) } },
            ["the order of a list"] = Request with { Fields = new Dictionary<string, PostedAnswer>(Request.Fields) { ["topics"] = new(["b", "a"]) } },
            ["two texts as one"] = Request with { Fields = new Dictionary<string, PostedAnswer>(Request.Fields) { ["topics"] = new(["a,b"]) } },
            ["an empty text for none"] = Request with { Fields = new Dictionary<string, PostedAnswer>(Request.Fields) { ["name"] = new([string.Empty]) } },
            ["a null text for an empty one"] = Request with { Fields = new Dictionary<string, PostedAnswer>(Request.Fields) { ["name"] = new([null]) } },
            ["a part"] = Request with { Fields = new Dictionary<string, PostedAnswer>(Request.Fields) { ["address"] = new([], new Dictionary<string, IReadOnlyList<string?>> { ["city"] = ["Basel"], ["country"] = ["CH"] }) } },
            ["a row"] = Request with { Rows = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, PostedAnswer>>> { ["guests"] = [new Dictionary<string, PostedAnswer> { ["guestName"] = new(["Alan"]) }] } },
            ["another row"] = Request with { Rows = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, PostedAnswer>>> { ["guests"] = [new Dictionary<string, PostedAnswer> { ["guestName"] = new(["Grace"]) }, new Dictionary<string, PostedAnswer>()] } },
            ["a query value"] = Request with { Query = new Dictionary<string, IReadOnlyList<string>> { ["source"] = ["ads"] } },
            ["a context value"] = Request with { Context = new Dictionary<string, string> { ["campaign"] = "spring" } },
        };
        var digest = SubmissionDigest.Of(Request);
        var digests = changes.ToDictionary(change => change.Key, change => SubmissionDigest.Of(change.Value));

        Assert.Multiple(() =>
        {
            Assert.That(digests.Where(pair => pair.Value == digest).Select(pair => pair.Key), Is.Empty, "each change gives another digest");
            Assert.That(digests.Values.Distinct().Count(), Is.EqualTo(digests.Count), "and no two changes give the same");
        });
    }

    [Test]
    public void AFormsTimes_LetItTakeEntriesFromItsOpeningUpToButNotAtItsClosing()
    {
        var opens = new DateTime(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc);
        var closes = new DateTime(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc);
        var times = new FormAvailabilitySettings { OpensAt = opens, ClosesAt = closes };
        bool OpenAt(FormAvailabilitySettings settings, DateTime instant) => FormAvailability.IsOpen(settings, new DateTimeOffset(instant));

        Assert.Multiple(() =>
        {
            Assert.That(OpenAt(new FormAvailabilitySettings(), opens), Is.True, "without times");
            Assert.That(OpenAt(times, opens.AddTicks(-1)), Is.False);
            Assert.That(OpenAt(times, opens), Is.True);
            Assert.That(OpenAt(times, closes.AddTicks(-1)), Is.True);
            Assert.That(OpenAt(times, closes), Is.False);
            Assert.That(OpenAt(new FormAvailabilitySettings { ClosesAt = closes }, opens.AddYears(-10)), Is.True, "no opening time");
            Assert.That(OpenAt(new FormAvailabilitySettings { OpensAt = opens }, closes.AddYears(10)), Is.True, "no closing time");
        });
    }

    [Test]
    public async Task TheAvailability_CountsTheEntriesOnlyForALimit_AndOnlyWhileTheTimesLetTheFormTakeOne()
    {
        var forms = Substitute.For<IFormRepository>();
        var entries = Substitute.For<IEntryRepository>();
        var clock = new TestTimeProvider(new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero));
        entries.CountTowardsLimitAsync(7, Arg.Any<CancellationToken>()).Returns(3);
        var availability = new FormAvailability(forms, entries, clock);
        var closed = new FormAvailabilitySettings { ClosesAt = new DateTime(2026, 11, 1, 11, 0, 0, DateTimeKind.Utc), MaxEntries = 10 };

        var noLimit = await availability.CheckAsync(new FormAvailabilitySettings(), 7);
        var below = await availability.CheckAsync(new FormAvailabilitySettings { MaxEntries = 4 }, 7);
        var reached = await availability.CheckAsync(new FormAvailabilitySettings { MaxEntries = 3 }, 7);
        var closedFirst = await availability.CheckAsync(closed, 7);
        var noRow = await availability.CheckAsync(new FormAvailabilitySettings { MaxEntries = 1 }, Form);

        Assert.Multiple(async () =>
        {
            Assert.That((noLimit, below, reached, closedFirst, noRow), Is.EqualTo(((FormUnavailable?)null, (FormUnavailable?)null, (FormUnavailable?)FormUnavailable.Full, (FormUnavailable?)FormUnavailable.Closed, (FormUnavailable?)null)));
            await entries.Received(2).CountTowardsLimitAsync(7, Arg.Any<CancellationToken>());
        });
    }

    [Test]
    public void TheEntriesThatCountTowardTheLimit_AreTheOnesNotTestsThatAreUnverifiedSubmittedOrApproved()
    {
        var counted = Enum.GetValues<EntryState>().Where(state => EntryLimits.Counts(state, isTest: false));

        Assert.Multiple(() =>
        {
            Assert.That(counted, Is.EquivalentTo(new[] { EntryState.Unverified, EntryState.Submitted, EntryState.Approved }));
            Assert.That(Enum.GetValues<EntryState>().Where(state => EntryLimits.Counts(state, isTest: true)), Is.Empty, "a test entry never counts");
            Assert.That(EntryLimits.CountingStates, Is.EquivalentTo(counted));
        });
    }

    [Test]
    public void AResult_IsAnEntryOrARefusal_AndARefusalWithProblemsHasOne()
    {
        var receipt = new SubmissionReceipt(Guid.NewGuid(), "2026-00001", DateTimeOffset.UnixEpoch);
        var accepted = SubmissionResult.Accepted(receipt, SubmissionOutcome.Default, replayed: true);
        var refused = SubmissionResult.Refused(SubmissionRefusal.FormClosed);
        var invalid = SubmissionResult.Invalid([new SubmissionProblem("required", "name")]);
        var unreadable = SubmissionResult.Unreadable(["values.x", "query.y"]);

        Assert.Multiple(() =>
        {
            Assert.That((accepted.Refusal, accepted.Receipt, accepted.Outcome, accepted.Replayed, accepted.Problems.Length), Is.EqualTo(((SubmissionRefusal?)null, receipt, SubmissionOutcome.Default, true, 0)));
            Assert.That((refused.Refusal, refused.Receipt, refused.Problems.Length), Is.EqualTo(((SubmissionRefusal?)SubmissionRefusal.FormClosed, (SubmissionReceipt?)null, 0)));
            Assert.That((invalid.Refusal, invalid.Problems.Single()), Is.EqualTo(((SubmissionRefusal?)SubmissionRefusal.ValidationFailed, new SubmissionProblem("required", "name"))));
            Assert.That(unreadable.Refusal, Is.EqualTo(SubmissionRefusal.InvalidRequest));
            Assert.That(unreadable.Problems, Is.EqualTo(new[] { new SubmissionProblem(SubmissionProblems.InvalidValue, "values.x"), new SubmissionProblem(SubmissionProblems.InvalidValue, "query.y") }));
            Assert.Throws<ArgumentException>(() => SubmissionResult.Refused(SubmissionRefusal.ValidationFailed));
            Assert.Throws<ArgumentException>(() => SubmissionResult.Refused(SubmissionRefusal.InvalidRequest));
            Assert.Throws<ArgumentException>(() => SubmissionResult.Invalid([]));
            Assert.Throws<ArgumentException>(() => SubmissionResult.Unreadable([]));
        });
    }
}
