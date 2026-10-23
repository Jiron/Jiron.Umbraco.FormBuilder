# 0007. Background work, outbox and email

- Status: accepted
- Date: 2026-10-23
- Roadmap step: 1.6 (decided); built in 4.1 (the tables), 4.3 (the repositories), 8.1 (the action processor) and
  8.2 (the email outbox); every later job follows its job rules

## Context

D6 stores the entry and one queued action run per action in one transaction (an outbox), and runs the actions in the
background with leases, retries and a visible log. D8 runs background work as distributed jobs, and asks that a
commit also wakes the server's own processor, so that email goes out within seconds and the job is only the safety
net. §4.4 adds the order of actions, five attempts with back-off from one minute, the `Uncertain` state, a retry that
asks first, and the pause switch. §3.4 records that Umbraco's runner starts one due job per 5-second tick and that
`EmailMessage` carries one body.

The spike built the outbox in the 17.7.1 sandbox on SQLite: action runs and an email queue shaped like §4.5, a worker
woken through a channel, a distributed job, and a second processor with its own database connections
([research](../research/spike-background.md)). What it found that shapes the decision:

- **Umbraco's runner awaits each job.** Next to Umbraco's ten distributed jobs, a job with a 5-second period ran on
  average every 20 s (5 to 50 s apart), and a 1-minute job every 65 to 70 s. The first job ran 109 s after the
  process started. While one job slept for 70 s, no other distributed job ran on that server: the 1-minute job then
  ran 120 s after its previous run.
- **The job alone is slow.** With only the 1-minute job to send them, six submissions waited 1.7 to 60.7 s for their
  mail (median 23.7 s).
- **A wake after the commit is fast.** From the commit to the mail in the pickup folder: median 7.8 ms, 95th
  percentile 8.8 ms, 99th 12.1 ms, highest 22.7 ms over 200 submissions in a row; median 8.3 ms 20 s after start-up,
  before the runner had started. Bursts of 20 and 50 simultaneous submissions were all sent within 141 ms and 348 ms
  of their commit, in two and three passes, because wakes add up to one.
- **Umbraco delivers a scoped notification after the outermost commit.** A notification published inside the
  submission's scope reached its handler only after the outer scope committed, never after a rollback, and another
  connection already saw the rows.
- **Conditional claims hold between two connections.** In five simulations of 1,000 action runs on two processors
  with injected faults, no run and no email had its effect twice, and every run ended in the state its attempts
  explain. The check is not blind: a claim without its state condition delivered 25 runs twice, a claim that looks
  first and takes second delivered 8 runs twice, and putting runs back to `Pending` after an expired lease delivered
  12 twice. A claim that reads and then writes in one transaction hung the SQLite database within seconds, and
  Umbraco's own jobs then failed to take their locks.
- **Umbraco's sender takes one body, and its gate is coarse.** `CanSendRequiredEmail()` is false without a host, a
  pickup folder or a `SendEmailNotification` handler, and `SendAsync` then drops the mail without an error. It is
  true for a pickup folder without a From address, and that send throws. An HTML mail is a single `text/html` part,
  a text mail a single `text/plain` part, and attachments make `multipart/mixed`. Nothing makes
  `multipart/alternative`.

## Options

**What carries the latency:**

1. **The distributed job does the work.** Mail waits up to a period plus the runner's queue (above), and a long run
   holds every distributed job on its server, Umbraco's scheduled publishing included.
2. **A poller on every server** (`IRecurringBackgroundJob` or a timer). Every server reads the outbox every few
   seconds, and the latency is the poll interval.
3. **A wake after the commit, a worker on each server, and a job that only wakes a worker** (Tandem ADR-0076's
   pattern, with the job narrowed to a wake).

**Claims:**

1. **One server owns the queue** through a lease on the whole queue. Simple, but one server does all the work, and a
   stuck owner stops everything until its lease ends.
2. **Database row locks** (`UPDLOCK, READPAST`, `FOR UPDATE SKIP LOCKED`). Not available on SQLite.
3. **A claim per run:** one conditional update that writes a token of its own. Portable, and every state change is
   one statement.

**A lease that runs out:**

1. **Retry automatically.** In the simulation this delivered 12 of 1,000 runs twice.
2. **Mark it failed** (Tandem ADR-0052). Safe, but the log says "failed" for something that may have arrived, and an
   attempt that finishes late cannot record that it did.
3. **Mark it `Uncertain` and keep the token,** so the attempt that held the claim can still record its result.

**The mail body:**

1. **HTML only** (Tandem ADR-0042, 0053).
2. **A text part beside the HTML.** That needs `multipart/alternative`, which `EmailMessage` cannot carry. Building
   the MIME message ourselves would bypass `IEmailSender`, and with it the mail services that sites plug in. The
   roadmap rules that out.
3. **One body per mail:** HTML by default, plain text when the action chooses it, and the text version always
   generated.

## Decision

Option 3 in each case.

- **The tables** are `jfbActionRun` and `jfbEmail` of §4.5. `jfbActionRun` gains `Seq` (the action's position) and
  `StopOnFailure`, indexed on (EntryId, Seq). `jfbEmail` gains `LockedBy` and the state `Uncertain`.
- **One state machine for both tables:**
  - **The claim:** `Pending` (`Queued` for mail) becomes `Running` (`Sending`) through
    `UPDATE … SET State = 'Running', LockedBy = @token, LockedUntilUtc = @until, Attempts = Attempts + 1 WHERE Id = @id AND State = 'Pending' AND NextAttemptUtc <= @now`,
    plus the order condition below. One changed row means the claim is ours. The token is the server's name and 128
    random bits.
  - **The outcomes:** a delivered attempt ends `Succeeded` (`Sent`). An attempt that ends in an error goes back to
    `Pending` with back-off, and becomes `Failed` after the fifth. An attempt that was cut off (by its own timeout, or
    because the host stopped) becomes `Uncertain`, because it may have arrived. Each change is one statement that
    names the run, its token and its state, so only the holder of the claim can make it.
  - **An expired lease** turns `Running` into `Uncertain` and keeps the token. The attempt that held it may still
    finish, and its result then replaces `Uncertain`: the late finisher. Nothing retries an `Uncertain` run by
    itself. The retry of §4.4 asks first and clears the token.
  - **Stopping the rest:** when a run with `StopOnFailure` fails for good, the entry's later pending runs become
    `Skipped` in the same transaction.
- **Order:** a run can be claimed only when no earlier run of its entry is running, still waits for its first
  attempt, or stops the rest and is not final yet. A retrying run does not hold the later ones back, and neither does
  a run whose lease ran out, unless it stops the rest.
- **Leases, timeouts and back-off:** a lease of 5 minutes, taken when each attempt starts. Each attempt has a
  timeout well inside it: 45 s for mail, through Umbraco's `expires`, and at most 1 minute for any action type.
  Retries come after 1, 3, 9 and 27 minutes, so five attempts in all.
- **The worker:** one per server, as a hosted service. It waits on a bounded channel of one with `DropWrite`, so
  wakes add up to one. Each pass marks expired leases, runs up to 25 due action runs, then up to 25 due mails, four at
  a time, and stops claiming after 2 minutes. A pass that left work due wakes the worker again. Passes never overlap
  on one server, and servers share the work through their claims.
- **The wake:** the submission publishes a scoped notification inside its scope, and its handler wakes this server's
  worker. Umbraco delivers it after the outermost scope commits, so an entry created inside a caller's scope wakes the
  worker only after the caller's commit, and a rollback wakes nothing. Everything else that queues mail
  (verification, resume links, forwards, test mail) wakes the worker the same way.
- **The job:** `Jiron.FormBuilder.Outbox`, an `IDistributedBackgroundJob` with a 1-minute period, only wakes its
  server's worker and returns. It picks up retries as they fall due, expired leases, and work that a stopped server
  left behind, each at most about a minute late.
- **Every Jiron Form Builder job** (the outbox, retention, stats roll-ups, abandoned-upload cleanup, file-mirror sync)
  returns from `ExecuteAsync` within 10 seconds, because the runner waits for it and holds every other distributed
  job on its server meanwhile. Longer work goes in bounded, idempotent batches that continue on the next run. Periods
  are a minute or longer, and each job first checks that Umbraco runs and that no migration is pending.
- **The pause switch:** while `JironFormBuilder:Actions:Paused` is on, passes claim nothing, so no attempt is used.
  Turning it off wakes the worker (`IOptionsMonitor.OnChange`).
- **The database:** every unit of work goes through Umbraco's scopes and the database gate (D7), starts with a write
  when it writes, and reads first when it only might write. Jiron Form Builder never opens a database connection of
  its own: on SQLite, a connection outside Umbraco's shared cache can hang Umbraco's own transactions that read
  before they write (Tandem ADR-0011 saw the same hang with parallel sign-ins). Units are rerun on SQLite codes 5 and
  6 and SQL Server 1205 and 1222. None were needed in the spike's 5,000 runs.
- **Email:**
  - Mail is sent through `IEmailSender.SendAsync(message, "JironFormBuilder", enableNotification: true, expires: 45 s)`,
    so a site's own sender and its `SendEmailNotification` handlers see every mail.
  - While `CanSendRequiredEmail()` is false, the queue claims nothing: no attempt is used and nothing is dropped. The
    health check (9.1) reports it, and reports a missing From address too, because the gate passes without one and
    the send then fails.
  - **One body per mail:** HTML by default, or plain text when the action chooses "plain text". The text version is
    always generated, stored in `TextBody` and shown in previews. There is no `multipart/alternative`.
  - Line breaks in subjects and other header values are replaced by a space before they reach `EmailMessage`. MimeKit
    would drop them and glue the words together.

## Consequences

- Mail leaves within milliseconds of the commit and does not depend on the job's period. Retries leave up to about a
  minute after they fall due.
- No Jiron Form Builder job holds Umbraco's scheduled publishing back for more than 10 seconds.
- 8.1's test of two processors uses the spike's design: a second processor with its own connections, the injected
  faults, the order check, and a mutant claim without its state condition that the test must catch. The real check
  with two hosts on SQL Server runs in CI (8.1).
- Reading and then writing in one transaction is ruled out for state changes, and the repositories' tests cover it.
- Mail is HTML only unless the action chooses plain text. Clients that show only text show the HTML's text, and some
  spam filters count HTML-only mail slightly against it. Editors who need text-only mail have the plain-text choice.
- The numbers come from SQLite on one machine. SQL Server and two hosts are measured in 8.1.
- Roadmap: the features list, §3.4, §4.4, §4.5, §4.10, 4.3, 8.1, 8.2 and the email health check change accordingly
  (WORKLOG).
- **Revisit** if Umbraco's runner starts several jobs per tick or stops waiting for them, if `EmailMessage` gains a
  second body, or if sites report many `Uncertain` runs from slow receivers.
