using System.Collections.Concurrent;
using Jiron.Umbraco.FormBuilder.Core.Actions;
using Jiron.Umbraco.FormBuilder.Core.Definitions;
using Jiron.Umbraco.FormBuilder.Core.Email;
using Jiron.Umbraco.FormBuilder.Core.Persistence.Repositories;
using Jiron.Umbraco.FormBuilder.Core.Settings;
using Umbraco.Cms.Core.Mail;
using Umbraco.Cms.Core.Models.Email;
using Umbraco.Cms.Tests.Common.Testing;

namespace Jiron.Umbraco.FormBuilder.Tests.Integration.Emails;

/// <summary>
/// A sender a test writes: whether mail can leave, and what each attempt does; it counts the mail it was given.
/// </summary>
internal sealed class ScriptedEmailSender : IEmailSender
{
    public bool CanSend { get; set; } = true;

    public Func<EmailMessage, Task>? Script { get; set; }

    public ConcurrentQueue<(EmailMessage Message, string Type, bool Notifies, TimeSpan? Expires)> Calls { get; } = new();

    public bool CanSendRequiredEmail() => CanSend;

    public Task SendAsync(EmailMessage message, string emailType) => SendAsync(message, emailType, false, null);

    public Task SendAsync(EmailMessage message, string emailType, bool enableNotification) => SendAsync(message, emailType, enableNotification, null);

    public Task SendAsync(EmailMessage message, string emailType, bool enableNotification, TimeSpan? expires)
    {
        Calls.Enqueue((message, emailType, enableNotification, expires));
        return Script?.Invoke(message) ?? Task.CompletedTask;
    }
}

/// <summary>What an attempt of a mail comes to, whatever the sender does (ROADMAP 8.2, ADR-0007).</summary>
[TestFixture]
[UmbracoTest(Database = UmbracoTestOptions.Database.NewSchemaPerTest, Boot = true)]
public sealed class EmailAttemptTests : EmailTestBase
{
    private ScriptedEmailSender Sender => (ScriptedEmailSender)GetRequiredService<IEmailSender>();

    private EmailAttempts Attempts => GetRequiredService<EmailAttempts>();

    [Test]
    public async Task ATestRun_WhereMailCannotLeave_SaysSo_AndQueuesNothing()
    {
        Sender.CanSend = false;
        var action = new FormAction
        {
            Key = Guid.NewGuid(),
            Type = EmailActionType.Alias,
            Name = "Mail",
            Settings = SettingsObject.Parse("""{"to":["team@jfb.test"],"subject":{"":"S"},"body":{"":"<p>B</p>"}}"""),
        };
        var form = await ActionFormAsync(action);

        var test = await GetRequiredService<ActionTestRuns>().TestAsync(form, action.Key, new ActionTestUser(Guid.NewGuid(), "ada@jfb.test", "Ada"), confirmed: false);

        Assert.Multiple(async () =>
        {
            Assert.That((test.Result!.Outcome, test.Result.Code), Is.EqualTo((ActionTestOutcome.Failed, ActionTestCodes.MailOff)));
            Assert.That(await Emails.DueAsync(10), Is.Empty, "nothing queued");
        });
    }

    [Test]
    public async Task TheSender_IsToldItsTypeAndTime_AndItsHandlersSeeTheMail()
    {
        await Outbox.QueueAsync(Mail());

        await SendDueAsync();

        var call = Sender.Calls.Single();
        Assert.Multiple(() =>
        {
            Assert.That(call.Type, Is.EqualTo("JironFormBuilder"));
            Assert.That(call.Notifies, Is.True);
            Assert.That(call.Expires, Is.EqualTo(TimeSpan.FromSeconds(45)));
            Assert.That(call.Message.IsBodyHtml, Is.True);
            Assert.That(call.Message.From, Is.EqualTo(HostSender));
            Assert.That(call.Message.To, Is.EqualTo(new[] { "anna@jfb.test" }));
        });
    }

    [Test]
    public async Task WhileMailCannotLeave_NoAttemptIsUsed_AndItLeavesLater()
    {
        var mail = await Outbox.QueueAsync(Mail());
        Sender.CanSend = false;

        await Pass.RunAsync(CancellationToken.None);

        var waiting = await Emails.GetAsync(mail!.Id);
        Assert.Multiple(() =>
        {
            Assert.That(Sender.Calls, Is.Empty);
            Assert.That((waiting!.State, waiting.Attempts), Is.EqualTo((EmailState.Queued, 0)));
        });

        Sender.CanSend = true;
        await Pass.RunAsync(CancellationToken.None);

        Assert.That((await Emails.GetAsync(mail.Id))!.State, Is.EqualTo(EmailState.Sent));
    }

    [Test]
    public async Task AnError_ComesBackAfterTheBackOff_AndFailsForGoodAfterTheFifth()
    {
        var mail = await Outbox.QueueAsync(Mail());
        Sender.Script = _ => throw new InvalidOperationException("No mailbox for anna@jfb.test");
        TimeSpan[] waits = [TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(3), TimeSpan.FromMinutes(9), TimeSpan.FromMinutes(27)];

        foreach (var wait in waits)
        {
            await Pass.RunAsync(CancellationToken.None);
            var queued = (await Emails.GetAsync(mail!.Id))!;
            Assert.Multiple(() =>
            {
                Assert.That(queued.State, Is.EqualTo(EmailState.Queued));
                Assert.That(queued.NextAttemptUtc, Is.EqualTo(Clock.GetUtcNow() + wait).Within(TimeSpan.FromSeconds(1)));
                Assert.That(queued.LastError, Is.EqualTo(new DeliveryError(ActionAttempts.Threw, typeof(InvalidOperationException).FullName)), "only the error's type: its message may hold an address");
            });

            await Pass.RunAsync(CancellationToken.None);
            Assert.That(Sender.Calls, Has.Count.EqualTo(Array.IndexOf(waits, wait) + 1), "not before its time");
            Clock.Advance(wait);
        }

        await Pass.RunAsync(CancellationToken.None);

        var failed = (await Emails.GetAsync(mail!.Id))!;
        Assert.Multiple(() =>
        {
            Assert.That(failed.State, Is.EqualTo(EmailState.Failed));
            Assert.That(failed.Attempts, Is.EqualTo(ActionAttempts.MaxAttempts));
            Assert.That(failed.PurgeAfterUtc, Is.EqualTo(Clock.GetUtcNow() + EmailOutbox.Keep).Within(TimeSpan.FromSeconds(1)));
        });

        Clock.Advance(TimeSpan.FromDays(1));
        await Pass.RunAsync(CancellationToken.None);
        Assert.That(Sender.Calls, Has.Count.EqualTo(ActionAttempts.MaxAttempts), "failed is final until a retry from the log");
    }

    [Test]
    public async Task AMailThatRunsOutOfAttempts_TellsTheTeam_ThroughTheQueue()
    {
        const string Team = "team@jfb.test";
        var settings = GetRequiredService<IFormBuilderSettingsService>();
        var current = await settings.GetAsync<EmailSettings>();
        await settings.SaveAsync(current.Value with { TeamRecipients = [Team] }, current.Version, Editor);
        var entry = await SubmitAsync(await PublishAsync(name: "Registration"));
        var mail = await Outbox.QueueAsync(Mail("visitor@jfb.test") with { EntryId = entry.Id });
        Sender.Script = message => message.To.Contains(Team) ? Task.CompletedTask : throw new InvalidOperationException("No mailbox");

        for (var attempt = 0; attempt <= ActionAttempts.MaxAttempts; attempt++)
        {
            await Pass.RunAsync(CancellationToken.None);
            Clock.Advance(TimeSpan.FromHours(1));
        }

        var told = Sender.Calls.Where(call => call.Message.To.Contains(Team)).ToList();
        Assert.Multiple(async () =>
        {
            Assert.That((await Emails.GetAsync(mail!.Id))!.State, Is.EqualTo(EmailState.Failed));
            Assert.That(told, Has.Count.EqualTo(1));
            Assert.That(told[0].Message.Body, Does.Contain("Registration").And.Contain(typeof(InvalidOperationException).FullName));
        });
    }

    [Test]
    public async Task AFailedMail_CountsFromWhenItFailed_NotFromWhenItWasQueued()
    {
        var mail = await Outbox.QueueAsync(Mail());
        var claimed = (await Emails.ClaimAsync(mail!.Id, "test", TimeSpan.FromMinutes(1)))!;
        Clock.Advance(TimeSpan.FromDays(3));
        var before = await Emails.CountFailedSinceAsync(Clock.GetUtcNow() - TimeSpan.FromHours(24), EmailOutbox.Keep);

        await Emails.FailAsync(mail.Id, claimed.LockedBy!, new DeliveryError(ActionAttempts.Threw), EmailOutbox.Keep);
        var fresh = await Emails.CountFailedSinceAsync(Clock.GetUtcNow() - TimeSpan.FromHours(24), EmailOutbox.Keep);
        Clock.Advance(TimeSpan.FromHours(25));
        var old = await Emails.CountFailedSinceAsync(Clock.GetUtcNow() - TimeSpan.FromHours(24), EmailOutbox.Keep);

        Assert.Multiple(() =>
        {
            Assert.That(before, Is.Zero);
            Assert.That(fresh, Is.EqualTo(1), "queued three days ago, failed now");
            Assert.That(old, Is.Zero);
        });
    }

    [Test]
    public async Task ARetryFromTheLog_SendsItAgain_WithAttemptsFromTheStart()
    {
        var mail = await Outbox.QueueAsync(Mail());
        var claimed = (await Emails.ClaimAsync(mail!.Id, "server", EmailQueue.Lease))!;
        await Emails.FailAsync(mail.Id, claimed.LockedBy!, new DeliveryError(ActionAttempts.Threw), EmailOutbox.Keep);

        var again = await Emails.RequeueAsync(mail.Id, EmailState.Failed);
        await Pass.RunAsync(CancellationToken.None);

        var sent = (await Emails.GetAsync(mail.Id))!;
        Assert.Multiple(() =>
        {
            Assert.That(again!.Attempts, Is.Zero);
            Assert.That(sent.State, Is.EqualTo(EmailState.Sent));
            Assert.That(sent.Attempts, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task ASendTheSenderCutOff_IsUncertain_AndNothingSendsItAgain()
    {
        var mail = await Outbox.QueueAsync(Mail());
        Sender.Script = _ => throw new OperationCanceledException();

        await Pass.RunAsync(CancellationToken.None);
        Clock.Advance(TimeSpan.FromHours(1));
        await Pass.RunAsync(CancellationToken.None);

        var uncertain = (await Emails.GetAsync(mail!.Id))!;
        Assert.Multiple(() =>
        {
            Assert.That(uncertain.State, Is.EqualTo(EmailState.Uncertain));
            Assert.That(uncertain.LastError!.Code, Is.EqualTo(ActionAttempts.TimedOut));
            Assert.That(Sender.Calls, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public async Task ATimeoutFromTheSender_IsUncertainToo()
    {
        var mail = await Outbox.QueueAsync(Mail());
        Sender.Script = _ => throw new TimeoutException();

        await Pass.RunAsync(CancellationToken.None);

        Assert.That((await Emails.GetAsync(mail!.Id))!.State, Is.EqualTo(EmailState.Uncertain));
    }

    [Test]
    public async Task TheHostStopping_MidSend_LeavesItUncertain_AndALateAnswerReplacesThat()
    {
        var mail = await Outbox.QueueAsync(Mail());
        var claimed = (await Emails.ClaimAsync(mail!.Id, "server", EmailQueue.Lease))!;
        var answer = new TaskCompletionSource();
        Sender.Script = _ => answer.Task;
        using var stopping = new CancellationTokenSource();

        var attempt = Attempts.SendAsync(claimed, stopping.Token);
        await WaitForCallAsync();
        await stopping.CancelAsync();
        await attempt.WaitAsync(TimeSpan.FromSeconds(30));

        var uncertain = (await Emails.GetAsync(mail.Id))!;
        Assert.Multiple(() =>
        {
            Assert.That(uncertain.State, Is.EqualTo(EmailState.Uncertain));
            Assert.That(uncertain.LastError!.Code, Is.EqualTo(ActionAttempts.HostStopped));
        });

        answer.SetResult();
        await WaitForStateAsync(mail.Id, EmailState.Sent);
    }

    [Test]
    public async Task ALateError_AfterTheHostStopped_GoesBackToTheQueue()
    {
        var mail = await Outbox.QueueAsync(Mail());
        var claimed = (await Emails.ClaimAsync(mail!.Id, "server", EmailQueue.Lease))!;
        var answer = new TaskCompletionSource();
        Sender.Script = _ => answer.Task;
        using var stopping = new CancellationTokenSource();

        var attempt = Attempts.SendAsync(claimed, stopping.Token);
        await WaitForCallAsync();
        await stopping.CancelAsync();
        await attempt.WaitAsync(TimeSpan.FromSeconds(30));
        answer.SetException(new InvalidOperationException("Refused"));

        await WaitForStateAsync(mail.Id, EmailState.Queued);
    }

    [Test]
    public async Task ALateCancellation_LeavesItUncertain()
    {
        var mail = await Outbox.QueueAsync(Mail());
        var claimed = (await Emails.ClaimAsync(mail!.Id, "server", EmailQueue.Lease))!;
        var answer = new TaskCompletionSource();
        Sender.Script = _ => answer.Task;
        using var stopping = new CancellationTokenSource();

        var attempt = Attempts.SendAsync(claimed, stopping.Token);
        await WaitForCallAsync();
        await stopping.CancelAsync();
        await attempt.WaitAsync(TimeSpan.FromSeconds(30));
        answer.SetCanceled();
        await Task.Delay(200);

        Assert.That((await Emails.GetAsync(mail.Id))!.State, Is.EqualTo(EmailState.Uncertain));
    }

    [Test]
    public async Task ASenderThatNeverAnswers_DoesNotHoldTheAttempt_PastItsTime()
    {
        var mail = await Outbox.QueueAsync(Mail());
        var claimed = (await Emails.ClaimAsync(mail!.Id, "server", EmailQueue.Lease))!;
        var answer = new TaskCompletionSource();
        Sender.Script = _ => answer.Task;

        await Attempts.SendAsync(claimed, CancellationToken.None).WaitAsync(EmailAttempts.Expires + ActionAttempts.Grace + TimeSpan.FromSeconds(30));

        var uncertain = (await Emails.GetAsync(mail.Id))!;
        Assert.Multiple(() =>
        {
            Assert.That(uncertain.State, Is.EqualTo(EmailState.Uncertain));
            Assert.That(uncertain.LastError!.Code, Is.EqualTo(ActionAttempts.TimedOut));
        });

        answer.SetResult();
        await WaitForStateAsync(mail.Id, EmailState.Sent);
    }

    [Test]
    public async Task ALeaseThatRanOut_MakesTheMailUncertain_AndASecondServerLeavesIt()
    {
        var mail = await Outbox.QueueAsync(Mail());
        await Emails.ClaimAsync(mail!.Id, "server-a", EmailQueue.Lease);

        Clock.Advance(EmailQueue.Lease + TimeSpan.FromSeconds(1));
        await Pass.RunAsync(CancellationToken.None);

        var uncertain = (await Emails.GetAsync(mail.Id))!;
        Assert.Multiple(() =>
        {
            Assert.That(uncertain.State, Is.EqualTo(EmailState.Uncertain));
            Assert.That(uncertain.LastError!.Code, Is.EqualTo(DeliveryError.LeaseExpired));
            Assert.That(uncertain.PurgeAfterUtc, Is.EqualTo(Clock.GetUtcNow() + EmailOutbox.Keep).Within(TimeSpan.FromSeconds(1)));
            Assert.That(Sender.Calls, Is.Empty);
        });
    }

    [Test]
    public async Task TwoPassesAtOnce_SendEachMailOnce()
    {
        for (var i = 0; i < 20; i++)
        {
            await Outbox.QueueAsync(Mail(subject: $"Mail {i}"));
        }

        var queue = GetRequiredService<IEnumerable<IOutboxQueue>>().OfType<EmailQueue>().Single();
        var deadline = Clock.GetUtcNow().AddMinutes(1);
        await Task.WhenAll(queue.PassAsync("server-a", deadline, CancellationToken.None), queue.PassAsync("server-b", deadline, CancellationToken.None));

        Assert.That(Sender.Calls.Select(call => call.Message.Subject), Is.Unique);
        Assert.That(Sender.Calls, Has.Count.EqualTo(20));
    }

    [Test]
    public async Task AMailWithoutASender_LeavesItToUmbraco()
    {
        await Emails.AddAsync(new NewEmail(EmailKind.Test, "anna@jfb.test", string.Empty, "Subject", "Text", "en"));

        await SendDueAsync();

        Assert.That(Sender.Calls.Single().Message.From, Is.Null);
    }

    [Test]
    public async Task QueuingAndARetryFromTheLog_WakeTheWorker_OnceCommitted()
    {
        var wake = GetRequiredService<OutboxWake>();
        await DrainWakesAsync(wake);

        var mail = await Outbox.QueueAsync(Mail());

        Assert.That(await WokenAsync(wake), Is.True, "queuing wakes it");

        var claimed = (await Emails.ClaimAsync(mail!.Id, "server", EmailQueue.Lease))!;
        await Emails.FailAsync(mail.Id, claimed.LockedBy!, new DeliveryError(ActionAttempts.Threw), EmailOutbox.Keep);
        await DrainWakesAsync(wake);
        await Emails.RequeueAsync(mail.Id, EmailState.Failed);

        Assert.That(await WokenAsync(wake), Is.True, "a retry wakes it");
    }

    [Test]
    public async Task AMailQueuedWithinTheLimits_WakesTheWorker_AndOneHeldBackDoesNot()
    {
        var wake = GetRequiredService<OutboxWake>();
        MailLimits.PerRecipientPerDay = 1;
        await DrainWakesAsync(wake);

        await Outbox.QueueAsync(Mail(toVisitor: true));
        Assert.That(await WokenAsync(wake), Is.True, "it is queued");

        await DrainWakesAsync(wake);
        await Outbox.QueueAsync(Mail(toVisitor: true));
        Assert.That(await WokenAsync(wake), Is.False, "it never leaves");
    }

    [Test]
    public async Task ARolledBackMail_WakesNothing()
    {
        var wake = GetRequiredService<OutboxWake>();
        var database = GetRequiredService<Core.Persistence.FormBuilderDatabase>();
        await DrainWakesAsync(wake);
        var prepared = (await Outbox.PrepareAsync(Mail()))!;

        Assert.ThrowsAsync<InvalidOperationException>(() => database.WriteAsync<int>(
            async _ =>
            {
                await Outbox.QueueAsync(prepared);
                throw new InvalidOperationException("Rolled back");
            },
            CancellationToken.None));

        Assert.Multiple(async () =>
        {
            Assert.That(await WokenAsync(wake), Is.False);
            Assert.That(await Emails.DueAsync(1), Is.Empty);
        });
    }

    [Test]
    public async Task AMailPreparedBeforeItsUnit_LeavesWhenTheUnitCommits()
    {
        var wake = GetRequiredService<OutboxWake>();
        var database = GetRequiredService<Core.Persistence.FormBuilderDatabase>();
        await DrainWakesAsync(wake);
        var prepared = (await Outbox.PrepareAsync(Mail()))!;

        var stored = await database.WriteAsync(_ => Outbox.QueueAsync(prepared), CancellationToken.None);

        Assert.Multiple(async () =>
        {
            Assert.That(await WokenAsync(wake), Is.True);
            Assert.That(await Emails.DueAsync(1), Is.EqualTo(new[] { stored!.Id }));
        });
    }

    [Test]
    public async Task PreparingAMailInsideAUnit_IsRefused_RatherThanWaitingForItself()
    {
        var database = GetRequiredService<Core.Persistence.FormBuilderDatabase>();

        var refused = Assert.ThrowsAsync<InvalidOperationException>(() => database.WriteAsync(_ => Outbox.PrepareAsync(Mail()), CancellationToken.None));

        Assert.Multiple(async () =>
        {
            Assert.That(refused!.Message, Does.Contain("before the unit of work"));
            Assert.That(await Emails.DueAsync(1), Is.Empty);
        });
    }

    [Test]
    public async Task ComposingAMail_NeedsNoRecipient_QueuesNothing_AndIsRefusedInsideAUnit()
    {
        var database = GetRequiredService<Core.Persistence.FormBuilderDatabase>();

        var composed = await Outbox.ComposeAsync(Mail() with { To = ["not an address"], Cc = ["copy@jfb.test", "COPY@jfb.test"] });
        var refused = Assert.ThrowsAsync<InvalidOperationException>(() => database.WriteAsync(_ => Outbox.ComposeAsync(Mail()), CancellationToken.None));

        Assert.Multiple(async () =>
        {
            Assert.That(composed.To, Is.Empty, "what is no address is left out, as the queue leaves it out");
            Assert.That(composed.Cc, Is.EqualTo(new[] { "copy@jfb.test" }), "each address once");
            Assert.That((composed.Subject, composed.Text.Trim()), Is.EqualTo(("Your answers", "Thank you.")));
            Assert.That(composed.Html, Does.Contain(">Thank you.</p>").And.Contain("prefers-color-scheme"), "the one body in the layout");
            Assert.That(await Outbox.PrepareAsync(Mail() with { To = ["not an address"] }), Is.Null, "a mail to nobody is not prepared for the queue");
            Assert.That(refused!.Message, Does.Contain("before the unit of work"));
            Assert.That(await Emails.DueAsync(1), Is.Empty);
        });
    }

    /// <inheritdoc />
    protected override void CustomTestSetup(IUmbracoBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        base.CustomTestSetup(builder);
        builder.Services.AddUnique<IEmailSender, ScriptedEmailSender>();
    }

    private static async Task DrainWakesAsync(OutboxWake wake) => await WokenAsync(wake, TimeSpan.FromMilliseconds(50));

    private static async Task<bool> WokenAsync(OutboxWake wake, TimeSpan? within = null)
    {
        using var limit = new CancellationTokenSource(within ?? TimeSpan.FromMilliseconds(500));
        try
        {
            await wake.WaitAsync(limit.Token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private async Task WaitForCallAsync()
    {
        for (var i = 0; i < 300 && Sender.Calls.IsEmpty; i++)
        {
            await Task.Delay(100);
        }

        Assert.That(Sender.Calls, Is.Not.Empty, "the sender was called");
    }

    private async Task WaitForStateAsync(long id, EmailState state)
    {
        for (var i = 0; i < 300; i++)
        {
            if ((await Emails.GetAsync(id))!.State == state)
            {
                return;
            }

            await Task.Delay(100);
        }

        Assert.Fail($"The mail did not become {state}.");
    }
}
