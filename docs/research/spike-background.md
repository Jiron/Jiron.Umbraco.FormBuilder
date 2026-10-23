# Background work and email: what the sandbox spike showed

Step 1.6 built the outbox of D6 and D8 in the Umbraco 17.7.1 sandbox (SQLite in WAL mode, port 5510): action runs and
an email queue shaped like `jfbActionRun` and `jfbEmail` of §4.5, a worker woken after each commit, a distributed
job, and a second processor with its own database connections, so that two "servers" raced for the same rows. Mail
went through Umbraco's `IEmailSender` to the sandbox's pickup folder. The spike code is throwaway and git-ignored
(`.local/sandbox/Spike/Outbox/` and `Controllers/SpikeOutboxLabController.cs`); the decisions are in
[ADR-0007](../decisions/0007-background-work-outbox-and-email.md). Measured on 2026-10-23 on the demo machine
(.NET SDK 10.0.401), through lab endpoints of the spike's management API, one call at a time.

## 1. What was built

- **The tables,** one set per lane (the site, the gate check, the simulation): an entry table, a run table with
  `Seq`, `StopOnFailure`, `State`, `Attempts`, `NextAttemptUtc`, `LockedUntilUtc` and `LockedBy`, and a mail table
  with the same claim columns. Times are Unix milliseconds here, so the spike does not depend on how a provider
  stores dates.
- **The processor:** marks expired leases, reads up to 25 due runs, claims each with one conditional update that
  writes a fresh token, runs four attempts at a time, and records each outcome with one statement that names the run,
  its token and its state. A delivered email action inserts its mails in the same transaction that completes the run.
- **The email queue:** the same claims for mail, gated by `CanSendRequiredEmail()`, sending through
  `IEmailSender.SendAsync(message, "JironFormBuilder", enableNotification: true, expires: 45 s)`.
- **The wake:** a bounded channel of one (`DropWrite`), read by one hosted worker per server. A submission publishes
  a notification inside its scope; its handler wakes the worker. Each pass runs the action runs, then the mail.
- **The job:** a 1-minute `IDistributedBackgroundJob` that only wakes the worker, and a 5-second probe job that
  records when it runs and can sleep once on request.
- **The pause switch:** a flag the passes check; turning it off wakes the worker.
- **The simulation:** server A is the site (Umbraco's scopes behind the process gate of D7). Server B opens its own
  SQLite connections to the same file (private cache, `BEGIN IMMEDIATE`) behind a gate of its own, as a second
  process would. Both run the same SQL text, each has its own wake and worker, and each is woken every 100 or 130 ms
  as a job would wake it, so the two race for the same rows all the time.

## 2. Umbraco's job runner

- **Settings** (`DistributedJobSettings`): `Period` 5 s, `Delay` 1 minute, `MaximumExecutionTime` 5 minutes.
- **Umbraco's ten distributed jobs,** in table order: `WebhookFiring` (10 s), `ContentVersionCleanupJob` (1 h),
  `HealthCheckNotifierJob` (1 day), `LogScrubberJob` (4 h), `ScheduledPublishingJob` (1 minute),
  `TemporaryFileCleanupJob` (5 minutes), `WebhookLoggingCleanup` (1 day), `CacheInstructionsPruningJob` (1 minute),
  `LongRunningOperationsCleanupJob` (2 minutes), `OpenIddictCleanupJob` (1 h). Package jobs follow in the order they
  were first registered. The table `umbracoDistributedJob` holds `Name`, `lastRun`, `period` (ticks), `IsRunning`
  and `lastAttemptedRun`.
- **Start-up:** the log says "Registering distributed background jobs" about 60 s after start, and the first job ran
  109 s after the process started.
- **Cadence:**

  | Job | Period | Runs | Seen intervals |
  | --- | --- | --- | --- |
  | probe | 5 s | 11 | 5 to 50 s, 20 s on average (10 intervals) |
  | outbox | 1 minute | 4 | 65, 70, 65 s |

  The runner starts one due job per tick in table order, so a job late in the table waits whenever earlier jobs are
  due, and every due job costs the others a tick.
- **A slow job holds the runner.** The probe slept 70 s in one run (319 s to 389 s after start). Nothing else ran
  meanwhile: the outbox job, due at about 369 s, ran at 429 s, 120 s after its previous run and 40 s after the probe
  woke, because Umbraco's jobs that had fallen due took the ticks before it. So the runner waits for each job, and a
  job that takes a minute delays every distributed job on its server by a minute, scheduled publishing included.

## 3. From the commit to the mail

Each submission stored one entry with one email action in one transaction and woke the worker after the commit.
"Commit" is the submission's own time (one insert of the entry and one of the run); "to sent" runs from the commit to
the moment `SendAsync` had written the mail to the pickup folder.

| Run | Uptime | Submissions | Commit p50 / p95 / max | To sent p50 / p95 / p99 / max | Passes |
| --- | --- | --- | --- | --- | --- |
| at start-up, before the runner | 20 s | 50 one by one | 2.0 / 2.3 / 12.7 ms | 8.3 / 9.4 / 13.6 / 93.9 ms | 50 |
| steady | 122 s | 200 one by one | 1.9 / 2.2 / 6.4 ms | 7.8 / 8.8 / 12.1 / 22.7 ms | 200 |
| burst | 111 s | 20 at once | 22.4 / 36.6 / 38.5 ms | 127.2 / 140.9 / 140.9 / 140.9 ms | 2 |
| burst | 117 s | 50 at once | 48.1 / 90.4 / 95.8 ms | 221.4 / 344.8 / 345.3 / 347.5 ms | 3 |
| the job only, no wake | 139 s | 6, 11 s apart | 6.2 / 7.6 / 9.6 ms | 23.7 / 49.7 / 49.7 / 60.7 s | 2 |

- The highest value at start-up is the first submission (code not yet compiled).
- In a burst the submissions queue behind the gate, and the wakes add up to one: 20 submissions needed two passes and
  50 needed three (the third woken by the worker itself, because a pass found more due than its batch).
- With the job only, the six mails waited 1.7, 12.7, 23.7, 34.7, 49.7 and 60.7 s: up to one period plus the runner's
  queue. Two job runs carried all six.
- An earlier process gave the same picture (200 in a row: 8.5 ms median, 11.7 ms at the 95th percentile).
- The sandbox's pickup folder held 566 lab mails at the end, each subject exactly once.

## 4. After the commit

A lab notification, published through `scope.Notifications` inside the submission's scope, wakes the worker. Its
handler looked at the database through a separate connection when it ran:

| Case | Handler ran | Rows seen by the other connection |
| --- | --- | --- |
| one scope, completed | yes | 1 |
| one scope, not completed (rolled back) | no | |
| nested: after the inner scope completed | not yet | |
| nested: after the outer scope completed | yes | 1 |

So the notification arrives after the outermost commit, never after a rollback, and a woken worker always finds the
rows.

## 5. Two servers on one database

Each simulation: 250 entries with four runs each (a webhook, an email action that renders one or two mails, a
content action and another webhook; the first webhook stops the rest in a quarter of the entries), submitted through
either server, while both servers worked. Lease 800 ms, back-off from 40 ms, tripled after each failure, five attempts, 25 per pass, four at a
time per server. Faults, drawn per attempt from a deterministic hash of the seed:

- **direct actions:** 4% of runs refused on every attempt; per attempt 12% refused, 3% slower than the lease and then
  delivered, 2% cut off after their effect, 1% cut off before it, 2% "crashed" after their effect (the processor
  walks away without recording anything) and 1% crashed before it;
- **email actions:** 12% render failures, 3% slower than the lease, 2% crashes;
- **mail:** 3% of addresses refused on every attempt; per attempt 10% refused, 2% slower than the lease, 1% cut off
  after delivery and 1% crashed after delivery.

The actions counted their effects. The verdict checks every run and mail against its state (a succeeded run has one
effect, a failed one none and five attempts, a skipped one no attempt, an uncertain one at most one effect, a pending
one is waiting behind an uncertain run that stops the rest), the number of mails each email action queued, and the
order of each entry's runs.

| Seed | Seconds | Attempts A / B | Succeeded | Failed | Skipped | Uncertain | Pending (waiting) | Mail sent / failed / uncertain | Expired | Late | Twice | Violations |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | 24.0 | 618 / 588 | 899 | 22 | 12 | 55 | 12 | 346 / 5 / 5 | 42 | 9 | 0 | 0 |
| 2 | 25.1 | 576 / 631 | 908 | 24 | 0 | 56 | 12 | 341 / 10 / 9 | 43 | 9 | 0 | 0 |
| 3 | 24.5 | 588 / 644 | 921 | 29 | 3 | 38 | 9 | 347 / 9 / 11 | 27 | 3 | 0 | 0 |
| 4 | 25.0 | 500 / 705 | 903 | 25 | 3 | 63 | 6 | 334 / 14 / 7 | 53 | 5 | 0 | 0 |
| 5 | 26.3 | 670 / 544 | 879 | 34 | 9 | 57 | 21 | 326 / 13 / 11 | 49 | 12 | 0 | 0 |

"Expired" counts leases that ran out (runs and mail), "late" the attempts that finished after their lease had run out
and recorded their result, "twice" the runs and mails with two effects. In 5,000 runs and 1,788 mails nothing had its
effect twice, and no unit of work needed a rerun for a transient database error. A first run without receivers that
always refuse never reached `Failed` or `Skipped`, which is why those were added.

**The check is not blind.** The same simulation (seed 1) against broken processors:

| Mutant | Runs with two effects | Violations |
| --- | --- | --- |
| the claim without `State = 'Pending'` | 25 | 39 |
| look first, then claim, in two units of work | 8 | 16 |
| expired leases back to `Pending` | 12 | 12 |
| look first, then claim, in one transaction | no verdict: the database hung | |

## 6. SQLite under two connections

- **Read-then-write hangs.** The mutant that reads and then writes in one transaction hung the site's database within
  seconds: Umbraco's transactions are deferred, so a transaction that read before the other connection committed
  cannot write any more, and Microsoft.Data.Sqlite keeps retrying it. The log showed "database is locked", then
  Umbraco's job runner failing with `DistributedWriteLockTimeoutException` ("Failed to acquire write lock for id:
  -347"), then "Cache instruction sync did not complete within 00:01:00 and may be stalled on a hung database
  connection". Only a restart helped. Tandem ADR-0011 saw the same hang with parallel sign-ins.
- **Start-up is fragile.** A simulation started right after start-up hung the database at the moment Umbraco logged
  "Registering distributed background jobs": most likely the same hang, with Umbraco's registration reading
  before it writes while server B's connection wrote. Simulations started 40 s after that line ran cleanly. A real site has no second
  connection, which is why ADR-0007 forbids Jiron Form Builder to open one.
- **NPoco reads `@name` inside SQL string literals as a parameter:** a literal `'visitor@example.test'` failed with
  "Parameter '@example' specified but none of the passed arguments have a property with this name". Values go in as
  parameters.
- `PRAGMA` statements need the table-valued form (`SELECT … FROM pragma_journal_mode`) through NPoco's `Fetch`,
  which otherwise adds a `SELECT … FROM` of its own.

## 7. Umbraco's mail sender

**The gate,** with `EmailSender` built for each configuration:

| Configuration | `CanSendRequiredEmail()` | `SendAsync` |
| --- | --- | --- |
| no SMTP settings | false | returns, nothing sent |
| a From address only | false | returns, nothing sent |
| a host only | true | (not tried: it would connect) |
| a pickup folder, no From address | true | throws `ArgumentNullException` |
| a pickup folder and a From address | true | writes the mail |
| a pickup folder that does not exist | true | throws `DirectoryNotFoundException` |
| no SMTP settings, a `SendEmailNotification` handler registered | true | returns; the mail goes to the handlers that Umbraco's notifications reach |

The queue with three mails: with the gate false it claimed nothing and the mails stayed `Queued` with 0 attempts;
with the site's sender all three were sent at the first attempt.

**What reaches the pickup folder:**

| Message | Top-level type | Parts |
| --- | --- | --- |
| HTML body | `text/html; charset=utf-8` | one |
| text body | `text/plain; charset=utf-8` | one |
| HTML body and an attachment | `multipart/mixed` | `text/html`, the attachment |

- A subject with an umlaut, an en dash and a check mark went out RFC 2047-encoded (UTF-8, base64) and folded.
- A subject with `\r\n` before a `Bcc:` line injected nothing: MimeKit dropped the line break, and the subject read
  "HelloBcc: injected@example.test".
- A recipient with a display name that contains a comma (`"Doe, Nora" <nora@example.test>`) and a reply-to address
  arrived intact.

## 8. The runs

| What | Result |
| --- | --- |
| latency, five runs (§3) | 326 submissions, none missing, none sent twice |
| after-commit cases (§4) | four of four as expected |
| pause: 10 submissions while paused, 3 s wait | 10 runs `Pending` with 0 attempts, nothing sent; all 10 sent within 74 ms of resuming, none twice |
| simulation, five seeds (§5) | 5,000 runs, 0 with two effects, 0 violations |
| mutants (§5) | three caught, one hung the database |
| gate and MIME (§7) | as in the tables |
| runner cadence and the slow job (§2) | as in §2 |

## 9. Notes for later steps

- **4.1:** `Seq` and `StopOnFailure` on `jfbActionRun`, indexed on (EntryId, Seq); `LockedBy` and `Uncertain` on
  `jfbEmail` (ADR-0007).
- **4.3:** every state change of a run or a mail is one conditional statement; no repository reads and then writes
  in one transaction, and Core opens no database connection of its own.
- **8.1:** the two-processor test keeps the spike's shape: the second processor's own connections, the faults, the
  order check, and the mutant claim it must catch. Wait for Umbraco's start-up work before it starts on SQLite.
- **8.2:** clean header values before they reach `EmailMessage`; check that a From address is configured, because
  the gate does not; a missing pickup folder is a convenient refused attempt for tests.
- **9.1:** the health check reports "email cannot be sent" and "no From address" separately.
- **Every job:** under 10 s per run, idempotent batches, a minute or longer.

## 10. What changed in the roadmap

The features list says how the email action's body works; §3.4 gains the runner, notification, NPoco and mail
sender facts and settles the one-body question; §4.4 states the order rule, the late finisher and cut-off attempts;
§4.5 gains the columns of §9; §4.10 replaces line breaks in headers instead of stripping them; 4.3, 8.1, 8.2 and 9.1
gain the checks of §9.
