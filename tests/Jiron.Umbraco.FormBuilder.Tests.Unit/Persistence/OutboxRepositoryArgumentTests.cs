using System.Data;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Persistence;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Scoping;
using Umbraco.Cms.Infrastructure.Scoping;
using IScopeProvider = Umbraco.Cms.Infrastructure.Scoping.IScopeProvider;

namespace Jiron.Umbraco.FormBuilder.Tests.Unit.Persistence;

/// <summary>
/// The outbox repositories refuse what their columns cannot hold before they reach the database (ADR-0036, ADR-0040):
/// claimants, tokens, codes, durations and sizes, batches of runs and mail in the shape the processors rely on, and a
/// retry from the log only from a state that has one. Messages too long for their column are cut, never between the
/// halves of a surrogate pair, and every claim gets a token of its own.
/// </summary>
[TestFixture]
public sealed class OutboxRepositoryArgumentTests
{
    private static readonly DeliveryError Error = new("http_503");
    private IScopeProvider _scopeProvider = null!;
    private IFormBuilderDatabaseGate _gate = null!;
    private FormBuilderDatabase _database = null!;

    [SetUp]
    public void SetUp()
    {
        _scopeProvider = Substitute.For<IScopeProvider>();
        _gate = Substitute.For<IFormBuilderDatabaseGate>();
        _database = new FormBuilderDatabase(_scopeProvider, Substitute.For<IScopeAccessor>(), _gate, TimeProvider.System, NullLogger<FormBuilderDatabase>.Instance);
    }

    // Every unit of work passes the gate first, and the substitute gate never runs one: a call that reached it would have
    // reached the database.
    [TearDown]
    public void NothingReachedTheDatabase()
    {
        Assert.That(_gate.ReceivedCalls(), Is.Empty, "a unit of work was started");
        _scopeProvider.DidNotReceiveWithAnyArgs().CreateScope(Arg.Any<IsolationLevel>(), Arg.Any<RepositoryCacheMode>(), Arg.Any<IEventDispatcher?>(), Arg.Any<IScopedNotificationPublisher?>(), Arg.Any<bool?>(), Arg.Any<bool>(), Arg.Any<bool>());
    }

    [Test]
    public void Runs_AreRefused_WhenAColumnCannotHoldThem()
    {
        var runs = new ActionRunRepository(_database);
        var run = new NewActionRun(Guid.NewGuid(), "Jiron.FormBuilder.Webhook", ActionTrigger.Submitted, 0, StopOnFailure: false);

        var change = Guid.NewGuid();

        Assert.Multiple(() =>
        {
            Refused(() => runs.AddAsync(1, 0, change, [run]), "a version");
            Refused(() => runs.AddAsync(1, 1, Guid.Empty, [run]), "no change");
            Refused(() => runs.AddAsync(1, 1, change, [run], TimeSpan.FromTicks(-1)), "a wait in the past");
            Refused(() => runs.AddAsync(1, 1, change, [run], Outbox.MaxDelay + TimeSpan.FromTicks(1)), "a long wait");
            Refused(() => runs.AddAsync(1, 1, change, []), "no runs");
            Refused(() => runs.AddAsync(1, 1, change, null!), "null");
            Refused(() => runs.AddAsync(1, 1, change, [.. Enumerable.Repeat(run, ActionRunRepository.MaxRunsPerCall + 1)]), "too many");
            Refused(() => runs.AddAsync(1, 1, change, [null!]), "a null run");
            Refused(() => runs.AddAsync(1, 1, change, [run with { ActionKey = Guid.Empty }]), "no action");
            Refused(() => runs.AddAsync(1, 1, change, [run with { ActionType = string.Empty }]), "no type");
            Refused(() => runs.AddAsync(1, 1, change, [run with { ActionType = new string('t', ActionRunRepository.MaxActionTypeLength + 1) }]), "a long type");
            Refused(() => runs.AddAsync(1, 1, change, [run with { ActionType = "Jiron Webhook" }]), "a type with a space");
            Refused(() => runs.AddAsync(1, 1, change, [run with { ActionType = "Jiron.Webhook" + (char)0 }]), "a type with a control character");
            Refused(() => runs.AddAsync(1, 1, change, [run with { ActionType = null! }]), "a null type");
            Refused(() => runs.AddAsync(1, 1, change, [run with { Trigger = (ActionTrigger)99 }]), "a trigger");
            Refused(() => runs.AddAsync(1, 1, change, [run with { Seq = -1 }]), "a position");
        });
    }

    [Test]
    public void ClaimsAndOutcomes_AreRefused_WithTokensDurationsAndCodesTheyCannotTake()
    {
        var runs = new ActionRunRepository(_database);
        var mail = new EmailRepository(_database);
        string[] tokens = [string.Empty, new string('t', Outbox.MaxTokenLength + 1), "server" + (char)10 + "token", null!];
        string[] claimants = [string.Empty, new string('c', Outbox.MaxClaimantLength + 1), "server" + (char)13, null!];

        Assert.Multiple(() =>
        {
            foreach (var claimant in claimants)
            {
                Refused(() => runs.ClaimAsync(1, claimant, TimeSpan.FromMinutes(5)), "claimant");
                Refused(() => mail.ClaimAsync(1, claimant, TimeSpan.FromMinutes(5)), "claimant");
            }

            foreach (var token in tokens)
            {
                Refused(() => runs.SucceedAsync(1, token), "token");
                Refused(() => runs.RetryAsync(1, token, TimeSpan.Zero, Error), "token");
                Refused(() => runs.FailAsync(1, token, Error), "token");
                Refused(() => runs.CutOffAsync(1, token, Error), "token");
                Refused(() => mail.SentAsync(1, token, TimeSpan.FromDays(30)), "token");
                Refused(() => mail.RetryAsync(1, token, TimeSpan.Zero, Error), "token");
                Refused(() => mail.FailAsync(1, token, Error, TimeSpan.FromDays(30)), "token");
                Refused(() => mail.CutOffAsync(1, token, Error, TimeSpan.FromDays(30)), "token");
            }

            foreach (var lease in new[] { TimeSpan.Zero, TimeSpan.FromSeconds(-1), Outbox.MaxLease + TimeSpan.FromTicks(1) })
            {
                Refused(() => runs.ClaimAsync(1, "JFB_TEST", lease), "lease");
                Refused(() => mail.ClaimAsync(1, "JFB_TEST", lease), "lease");
            }

            foreach (var delay in new[] { TimeSpan.FromTicks(-1), Outbox.MaxDelay + TimeSpan.FromTicks(1) })
            {
                Refused(() => runs.RetryAsync(1, "JFB_TEST", delay, Error), "delay");
                Refused(() => mail.RetryAsync(1, "JFB_TEST", delay, Error), "delay");
            }

            foreach (var keep in new[] { TimeSpan.Zero, Outbox.MaxKeep + TimeSpan.FromTicks(1) })
            {
                Refused(() => mail.SentAsync(1, "JFB_TEST", keep), "keep");
                Refused(() => mail.FailAsync(1, "JFB_TEST", Error, keep), "keep");
                Refused(() => mail.CutOffAsync(1, "JFB_TEST", Error, keep), "keep");
                Refused(() => mail.ExpireLeasesAsync(keep), "keep");
            }

            foreach (var error in new[] { new DeliveryError(string.Empty), new DeliveryError("HTTP_503"), new DeliveryError("http 503"), new DeliveryError(new string('c', DeliveryError.MaxCodeLength + 1)), new DeliveryError(null!), null! })
            {
                Refused(() => runs.RetryAsync(1, "JFB_TEST", TimeSpan.Zero, error), "code");
                Refused(() => runs.FailAsync(1, "JFB_TEST", error), "code");
                Refused(() => runs.CutOffAsync(1, "JFB_TEST", error), "code");
                Refused(() => mail.RetryAsync(1, "JFB_TEST", TimeSpan.Zero, error), "code");
                Refused(() => mail.FailAsync(1, "JFB_TEST", error, TimeSpan.FromDays(30)), "code");
                Refused(() => mail.CutOffAsync(1, "JFB_TEST", error, TimeSpan.FromDays(30)), "code");
            }

            foreach (var take in new[] { 0, Outbox.MaxTake + 1 })
            {
                Refused(() => runs.DueAsync(take), "take");
                Refused(() => mail.DueAsync(take), "take");
                Refused(() => mail.PurgeAsync(take), "take");
            }
        });
    }

    [Test]
    public void ARetryFromTheLog_IsRefused_FromAStateWithoutOne()
    {
        var runs = new ActionRunRepository(_database);
        var mail = new EmailRepository(_database);

        Assert.Multiple(() =>
        {
            foreach (var state in new[] { ActionRunState.Pending, ActionRunState.Running, ActionRunState.Succeeded, ActionRunState.Skipped, (ActionRunState)99 })
            {
                Refused(() => runs.RequeueAsync(1, state), state.ToString());
            }

            foreach (var state in new[] { EmailState.Queued, EmailState.Sending, EmailState.Sent, EmailState.Limited, (EmailState)99 })
            {
                Refused(() => mail.RequeueAsync(1, state), state.ToString());
            }
        });
    }

    [Test]
    public void Mail_IsRefused_WhenAColumnCannotHoldIt()
    {
        var repository = new EmailRepository(_database);
        var mail = new NewEmail(EmailKind.Action, "sales@jfb.test", "forms@jfb.test", "A new entry", "Thank you.", "en-US");
        var line = (char)13 + "Bcc: everyone@jfb.test";

        Assert.Multiple(() =>
        {
            Refused(() => repository.AddAsync(null!), "null");
            Refused(() => repository.AddAsync(mail with { Kind = (EmailKind)99 }), "kind");
            Refused(() => repository.AddAsync(mail with { To = string.Empty }), "no recipient");
            Refused(() => repository.AddAsync(mail with { To = null! }), "null recipients");
            Refused(() => repository.AddAsync(mail with { To = new string('t', EmailRepository.MaxAddressesLength + 1) }), "long recipients");
            Refused(() => repository.AddAsync(mail with { To = "sales@jfb.test" + line }), "a header in the recipients");
            Refused(() => repository.AddAsync(mail with { Cc = string.Empty }), "empty copy");
            Refused(() => repository.AddAsync(mail with { Cc = "copy@jfb.test" + line }), "a header in the copy");
            Refused(() => repository.AddAsync(mail with { Bcc = new string('b', EmailRepository.MaxAddressesLength + 1) }), "long blind copy");
            Refused(() => repository.AddAsync(mail with { ReplyTo = new string('r', EmailRepository.MaxAddressLength + 1) }), "long reply-to");
            Refused(() => repository.AddAsync(mail with { ReplyTo = "anna@jfb.test" + line }), "a header in the reply-to");
            Refused(() => repository.AddAsync(mail with { From = string.Empty }), "no sender");
            Refused(() => repository.AddAsync(mail with { From = "forms@jfb.test" + (char)10 }), "a line break in the sender");
            Refused(() => repository.AddAsync(mail with { Subject = new string('s', EmailRepository.MaxSubjectLength + 1) }), "long subject");
            Refused(() => repository.AddAsync(mail with { Subject = null! }), "null subject");
            Refused(() => repository.AddAsync(mail with { TextBody = string.Empty }), "no text version");
            Refused(() => repository.AddAsync(mail with { HtmlBody = string.Empty }), "an empty HTML body");
            Refused(() => repository.AddAsync(mail with { Culture = new string('c', EmailRepository.MaxCultureLength + 1) }), "long culture");
            Refused(() => repository.AddAsync(mail with { Culture = null! }), "null culture");
            Refused(() => repository.AddAsync(mail with { Culture = string.Empty }), "no culture");
            Refused(() => repository.AddAsync(mail with { RecipientKey = string.Empty }), "empty recipient key");
            Refused(() => repository.AddAsync(mail with { RecipientKey = new string('k', EmailRepository.MaxRecipientKeyLength + 1) }), "long recipient key");
            Refused(() => repository.AddAsync(mail with { EntryId = 0 }), "entry 0");
            Refused(() => repository.AddAsync(mail with { ActionRunId = -1 }), "run -1");
            Refused(() => repository.AddAsync(mail with { Attachments = [Guid.Empty] }), "an empty file key");
            var key = Guid.NewGuid();
            Refused(() => repository.AddAsync(mail with { Attachments = [key, key] }), "a file twice");
        });
    }

    [TestCase("http_503", true)]
    [TestCase("smtp.550", true)]
    [TestCase("lease_expired", true)]
    [TestCase("a-b", true)]
    [TestCase("", false)]
    [TestCase("HTTP_503", false)]
    [TestCase("http 503", false)]
    [TestCase("http:503", false)]
    [TestCase(null, false)]
    public void ACode_IsLowercaseLettersDigitsAndSeparators(string? code, bool valid) =>
        Assert.That(DeliveryError.IsCode(code), Is.EqualTo(valid));

    [Test]
    public void ACodeOfTheLongestLength_IsACode() =>
        Assert.Multiple(() =>
        {
            Assert.That(DeliveryError.IsCode(new string('c', DeliveryError.MaxCodeLength)), Is.True);
            Assert.That(DeliveryError.IsCode(new string('c', DeliveryError.MaxCodeLength + 1)), Is.False);
        });

    [Test]
    public void EveryClaim_GetsATokenOfItsOwn()
    {
        var claimant = new string('c', Outbox.MaxClaimantLength);
        var tokens = Enumerable.Range(0, 100).Select(_ => Outbox.NewToken(claimant)).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(Outbox.NewToken("server-a"), Does.Match("^server-a/[0-9a-f]{32}$"), "the claimant's name and 128 random bits");
            Assert.That(tokens, Is.Unique);
            Assert.That(tokens.Max(token => token.Length), Is.LessThanOrEqualTo(Outbox.MaxTokenLength), "the longest claimant's token fits its column");
        });
    }

    [Test]
    public void AMessage_IsCutToItsColumn_NeverInsideASurrogatePair()
    {
        var face = char.ConvertFromUtf32(0x1F600);

        Assert.Multiple(() =>
        {
            Assert.That(Outbox.Cut(null, 5), Is.Null);
            Assert.That(Outbox.Cut("short", 5), Is.EqualTo("short"));
            Assert.That(Outbox.Cut("longer", 5), Is.EqualTo("longe"));
            Assert.That(Outbox.Cut("abcd" + face, 5), Is.EqualTo("abcd"), "the pair would start at the last place");
            Assert.That(Outbox.Cut("abc" + face + "d", 5), Is.EqualTo("abc" + face), "the pair fits");
        });
    }

    [Test]
    public void AMailsError_IsStoredAsCodeAndMessage_AndReadBack()
    {
        var wordy = new string('m', EmailRepository.MaxErrorLength);

        Assert.Multiple(() =>
        {
            Assert.That(EmailRepository.ErrorText(new DeliveryError("smtp.550", "Mailbox unavailable.")), Is.EqualTo("smtp.550: Mailbox unavailable."));
            Assert.That(EmailRepository.ErrorText(new DeliveryError("lease_expired")), Is.EqualTo("lease_expired"));
            Assert.That(EmailRepository.ErrorText(new DeliveryError("smtp.550", wordy)), Has.Length.EqualTo(EmailRepository.MaxErrorLength).And.StartWith("smtp.550: m"), "the code always fits");
            Assert.That(EmailRepository.Error("smtp.550: Mailbox unavailable: try later."), Is.EqualTo(new DeliveryError("smtp.550", "Mailbox unavailable: try later.")), "the first separator ends the code");
            Assert.That(EmailRepository.Error("lease_expired"), Is.EqualTo(new DeliveryError("lease_expired")));
            Assert.That(EmailRepository.Error("Something: went wrong"), Is.EqualTo(new DeliveryError("unknown", "Something: went wrong")), "text without a code");
            Assert.That(EmailRepository.Error(null), Is.Null);
        });
    }

    [Test]
    public void Attachments_AreAJsonArrayOfFileKeys()
    {
        var keys = new[] { Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"), Guid.Parse("7c9e6679-7425-40de-944b-e07fc1f90ae7") };

        Assert.Multiple(() =>
        {
            Assert.That(EmailRepository.AttachmentsText(keys), Is.EqualTo("""["0f8fad5b-d9cb-469f-a165-70867728950e","7c9e6679-7425-40de-944b-e07fc1f90ae7"]"""));
            Assert.That(EmailRepository.AttachmentsText([]), Is.EqualTo("[]"));
        });
    }

    private static void Refused<T>(Func<Task<T>> call, string why) =>
        Assert.That(call, Throws.InstanceOf<ArgumentException>(), why);
}
