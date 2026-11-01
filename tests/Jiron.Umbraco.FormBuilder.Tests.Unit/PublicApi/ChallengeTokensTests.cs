using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Submissions;
using Jiron.Umbraco.FormBuilder.Web.PublicApi;
using Microsoft.AspNetCore.DataProtection;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.PublicApi;

/// <summary>
/// The signed challenges (ADR-0012, ADR-0138): bound to the form and the time, under a purpose of their own, with a
/// member on a form for members, and with a renewal that carries the issue time and never the id; and what a form's rules
/// ask of the member, which is all the challenge answers about one.
/// </summary>
[TestFixture]
public sealed class ChallengeTokensTests
{
    private static readonly Guid Form = Guid.Parse("71b00000-0000-4000-8000-000000000001");
    private static readonly Guid Member = Guid.Parse("71b00000-0000-4000-8000-000000000002");

    private EphemeralDataProtectionProvider _protection = null!;
    private StoppedClock _clock = null!;
    private ChallengeTokens _tokens = null!;

    [SetUp]
    public void SetUp()
    {
        _protection = new EphemeralDataProtectionProvider();
        _clock = new StoppedClock();
        _tokens = new ChallengeTokens(_protection, _clock);
    }

    [Test]
    public void AChallenge_NamesItsTime_AndAnIdOfItsOwn()
    {
        var one = _tokens.Read(_tokens.Issue(Form, null), Form, TimeSpan.Zero)!;
        var other = _tokens.Read(_tokens.Issue(Form, null), Form, TimeSpan.Zero)!;

        Assert.Multiple(() =>
        {
            Assert.That(one.IssuedUtc, Is.EqualTo(_clock.GetUtcNow()));
            Assert.That(one.Member, Is.Null);
            Assert.That(one.Id, Is.Not.EqualTo(Guid.Empty));
            Assert.That(one.Id, Is.Not.EqualTo(other.Id));
            Assert.That(FormChallenges.StoredId(one), Has.Length.EqualTo(32).And.Match("^[0-9a-f]+$"));
        });
    }

    [Test]
    public void AChallenge_CarriesItsMember()
    {
        Assert.That(_tokens.Read(_tokens.Issue(Form, Member), Form, TimeSpan.Zero)!.Member, Is.EqualTo(Member));
    }

    [Test]
    public void AChallenge_CountsForItsFormOnly()
    {
        var token = _tokens.Issue(Form, null);

        Assert.That(_tokens.Read(token, Guid.NewGuid(), TimeSpan.Zero), Is.Null);
    }

    [Test]
    public void AChallenge_Expires_AfterADay_AndCountsThenOnlyWithinItsGrace()
    {
        var token = _tokens.Issue(Form, null);

        _clock.Advance(TimeSpan.FromHours(24) - TimeSpan.FromSeconds(1));
        Assert.That(_tokens.Read(token, Form, TimeSpan.Zero), Is.Not.Null, "a second before");

        _clock.Advance(TimeSpan.FromSeconds(1));
        Assert.That(_tokens.Read(token, Form, TimeSpan.Zero), Is.Null, "at its expiry");
        Assert.That(_tokens.Read(token, Form, FormChallenges.ReceiptGrace), Is.Not.Null, "within the receipt's grace");

        _clock.Advance(TimeSpan.FromHours(24));
        Assert.That(_tokens.Read(token, Form, FormChallenges.ReceiptGrace), Is.Null, "past the grace");
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("not a token!")]
    [TestCase("AAAA")]
    public void WhatIsNoChallenge_ReadsAsNone(string? token)
    {
        Assert.That(_tokens.Read(token, Form, TimeSpan.Zero), Is.Null);
    }

    [Test]
    public void AChangedChallenge_AndOneFromAnotherSite_ReadAsNone()
    {
        var token = _tokens.Issue(Form, null);
        var changed = (token[^2] == 'A' ? token[..^2] + "B" : token[..^2] + "A") + token[^1];
        var another = new ChallengeTokens(new EphemeralDataProtectionProvider(), _clock).Issue(Form, null);

        Assert.Multiple(() =>
        {
            Assert.That(_tokens.Read(changed, Form, TimeSpan.Zero), Is.Null);
            Assert.That(_tokens.Read(another, Form, TimeSpan.Zero), Is.Null);
        });
    }

    [Test]
    public void AnotherTokensPurpose_DoesNotReadAsAChallenge()
    {
        var upload = new UploadTokens(_protection, _clock).Issue(Form, Guid.NewGuid(), Guid.NewGuid());

        Assert.That(_tokens.Read(upload, Form, TimeSpan.Zero), Is.Null);
    }

    [TestCase("Another.Purpose", 1, 66, TestName = "A token of the right shape under another purpose")]
    [TestCase(ChallengeTokens.Purpose, 2, 66, TestName = "A token of another version")]
    [TestCase(ChallengeTokens.Purpose, 1, 65, TestName = "A token of another length")]
    public void WhatIsNotThisTokensShape_ReadsAsNone(string purpose, byte version, int length)
    {
        // A challenge's bytes as Issue writes them, whose expiry is far off, so that only the case at hand can refuse it.
        var payload = new byte[length];
        payload[0] = version;
        Form.TryWriteBytes(payload.AsSpan(1, 16));
        Guid.NewGuid().TryWriteBytes(payload.AsSpan(17, 16));
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(49), _clock.GetUtcNow().UtcTicks);
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(57), _clock.GetUtcNow().AddDays(1).UtcTicks);

        var token = System.Buffers.Text.Base64Url.EncodeToString(_protection.CreateProtector(purpose).Protect(payload));

        Assert.That(_tokens.Read(token, Form, TimeSpan.Zero), Is.Null);
    }

    [Test]
    public void AChallenge_CarriesItsPuzzlesDifficulty()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_tokens.Read(_tokens.Issue(Form, null, proofBits: 16), Form, TimeSpan.Zero)!.ProofBits, Is.EqualTo(16));
            Assert.That(_tokens.Read(_tokens.Issue(Form, null), Form, TimeSpan.Zero)!.ProofBits, Is.Zero);
        });
    }

    [Test]
    public void AChallengeTooLong_ReadsAsNone_WithoutBeingDecoded()
    {
        Assert.That(_tokens.Read(new string('A', EntryBodies.MaxChallengeLength + 1), Form, TimeSpan.Zero), Is.Null);
    }

    [Test]
    public void ARenewal_CarriesTheIssueTimeOver_EvenOfAnExpiredOne_WithANewIdAndExpiry()
    {
        var first = _tokens.Issue(Form, null);
        var issued = _clock.GetUtcNow();
        _clock.Advance(TimeSpan.FromDays(30));

        var renewed = _tokens.Issue(Form, null, first);
        var read = _tokens.Read(renewed, Form, TimeSpan.Zero);

        Assert.Multiple(() =>
        {
            Assert.That(_tokens.Read(first, Form, TimeSpan.Zero), Is.Null, "the first has expired");
            Assert.That(read!.IssuedUtc, Is.EqualTo(issued));
            Assert.That(read.Id, Is.Not.EqualTo(_tokens.Read(first, Form, TimeSpan.MaxValue)!.Id));
        });
    }

    [Test]
    public void ARenewal_OfAnotherFormsChallenge_OrOfNothing_StartsTheTimeNow()
    {
        var foreign = _tokens.Issue(Guid.NewGuid(), null);
        _clock.Advance(TimeSpan.FromHours(1));

        Assert.Multiple(() =>
        {
            Assert.That(_tokens.Read(_tokens.Issue(Form, null, foreign), Form, TimeSpan.Zero)!.IssuedUtc, Is.EqualTo(_clock.GetUtcNow()));
            Assert.That(_tokens.Read(_tokens.Issue(Form, null, "forged"), Form, TimeSpan.Zero)!.IssuedUtc, Is.EqualTo(_clock.GetUtcNow()));
        });
    }

    [Test]
    public void AnUploadToken_NamesItsChallenge_AndOnlyTheSameOneClaimsIt()
    {
        var uploads = new UploadTokens(_protection, _clock);
        var (field, file, challenge) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var token = uploads.Issue(Form, field, file, challenge);
        var without = uploads.Issue(Form, field, file);

        Assert.Multiple(() =>
        {
            Assert.That(uploads.FileOf(token, Form, field, challenge), Is.EqualTo(file));
            Assert.That(uploads.FileOf(token, Form, field, Guid.NewGuid()), Is.Null, "another challenge");
            Assert.That(uploads.FileOf(token, Form, field, null), Is.Null, "a request with none");
            Assert.That(uploads.FileOf(without, Form, field, null), Is.EqualTo(file));
            Assert.That(uploads.FileOf(without, Form, field, challenge), Is.Null, "uploaded with none, claimed with one");
        });
    }

    [Test]
    public void TheRules_TellWhatTheyAskOfTheMember_AtEveryLevelAndDepth()
    {
        var uses = FormMemberConditions.Of(Definition(
            page: Rule(new RuleCondition { Context = "memberSignedIn", Operator = "equals", Value = new ConditionOperand.Text("true") }),
            group: Rule(new RuleGroup { Conditions = [new RuleCondition { Context = "memberGroup", Operator = "equals", Value = new ConditionOperand.Text("gold") }] }),
            field: Rule(new RuleCondition { Context = "memberGroup", Operator = "isOneOf", Value = new ConditionOperand.List([new ConditionOperand.Text("silver"), new ConditionOperand.Text("bronze")]) })));

        Assert.Multiple(() =>
        {
            Assert.That(uses.Any, Is.True);
            Assert.That(uses.Groups, Is.EquivalentTo(new[] { "gold", "silver", "bronze" }));
        });
    }

    [Test]
    public void APagesRuleAlone_ThatAsksWhetherAMemberIsSignedIn_AsksThatOnly()
    {
        var uses = FormMemberConditions.Of(Definition(
            page: Rule(new RuleCondition { Context = "memberSignedIn", Operator = "equals", Value = new ConditionOperand.Text("true") }),
            group: null,
            field: null));

        Assert.Multiple(() =>
        {
            Assert.That(uses.Any, Is.True);
            Assert.That(uses.Groups, Is.Empty);
        });
    }

    [Test]
    public void RulesThatLookElsewhere_AskNothingOfTheMember()
    {
        var uses = FormMemberConditions.Of(Definition(
            page: Rule(new RuleCondition { Context = "culture", Operator = "equals", Value = new ConditionOperand.Text("de") }),
            group: null,
            field: null));

        Assert.Multiple(() =>
        {
            Assert.That(uses.Any, Is.False);
            Assert.That(uses.Groups, Is.Empty);
        });
    }

    private static FormRule Rule(RuleItem item) => new() { Key = Guid.NewGuid(), Effect = RuleEffect.Show, Conditions = [item] };

    private static FormDefinition Definition(FormRule? page, FormRule? group, FormRule? field) => new()
    {
        Key = Guid.NewGuid(),
        Pages =
        [
            new FormPage
            {
                Key = Guid.NewGuid(),
                Rules = page is null ? [] : [page],
                Groups =
                [
                    new FormGroup
                    {
                        Key = Guid.NewGuid(),
                        Rules = group is null ? [] : [group],
                        Fields = [new FormField { Key = Guid.NewGuid(), Type = "text", Alias = "name", Label = new LocalizedText("Name"), Rules = field is null ? [] : [field] }],
                    },
                ],
            },
        ],
    };

    // A clock that stands still until a test moves it.
    private sealed class StoppedClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }
}
