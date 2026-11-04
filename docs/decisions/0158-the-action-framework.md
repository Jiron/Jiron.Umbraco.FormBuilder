# 0158. The action framework

- Status: accepted; builds on [ADR-0007](0007-background-work-outbox-and-email.md) and
  [ADR-0040](0040-outbox-repositories.md)
- Date: 2026-11-02
- Roadmap step: 8.1 (decided and built); 8.2 (the mail queue joins the pass), 8.3 to 8.5 (the built-in types and what queues
  the `approved`, `rejected` and `verified` runs), 11.x (the deliveries log's screen)

## Context

ADR-0007 decided the state machine, the leases, the worker and the wake, and ADR-0040 the repositories. Step 8.1 builds what
runs on them. Six questions were left open:

- **What an action type is** to a developer, and how a type's attempt says how it went.
- **Where the back-off's limit, the lease and the time limit live,** and what happens to an attempt that does not stop when told.
- **When the values kept until the actions have run go,** so that two last runs on two servers cannot both miss it and a crash
  cannot leave them behind.
- **What "the whole entry goes" means** for `settings.submission.store: never`, whose entries also spend a challenge and answer
  a retry of the same submission.
- **What a type says about the site's configuration,** and what a test run reaches.
- **How two servers are tested,** when SQLite cannot be shared between hosts.

## Options

**How an attempt reports:**

1. **A boolean and exceptions.** A type could not say "this will never work" or "this may have arrived".
2. **A result with four kinds:** succeeded, try again, failed for good, uncertain. An exception counts as "try again".

**A type that does not stop:**

1. **Wait for it.** One stuck type would hold a pass, and with it every run and mail of the server.
2. **Count it as cut off after a short grace,** and let its result replace that when it comes.

**When kept values go:**

1. **A sweep in the retention job.** A cost per hour for every form, and values stay up to an hour longer than they need to.
2. **After the last outcome, in a unit of its own.** A crash between the two leaves the values behind for good.
3. **In the unit that makes the last run final,** which holds the entry's row first.

**What stays of an entry on a form that stores none:**

1. **A row without its answers.** Lists, counts and exports would all have to learn about it, and it would still be a record of
   a person's submission.
2. **Nothing, not even its single use of a challenge.** A replayed challenge would make a new entry and run the actions again, for
   as long as the challenge is valid.
3. **Nothing of the entry, and a mark that its challenge was used,** kept only as long as a challenge can come back.

## Decision

Option 2 for an attempt's report and a type that does not stop; option 3 for the kept values and for what stays of an entry.

**The model.** `IFormBuilderActionType` is a declaration (alias, name, icon, how a test run behaves, a time limit of at most a
minute), a settings schema read from the same `FormBuilderSetting` attributes as a field type's, the type's own check of its
settings, what the site lacks for an action, a run and a test run. `FormBuilderActionTypeBase<TSettings>` is the usual way to write
one, and `builder.JironFormBuilder().ActionTypes()` is where it is registered. Start-up refuses an alias outside the pattern or
taken by another type, a missing name or icon, a time limit beyond a minute, and a setting marked as one visitors see.

**Publishing** refuses an action whose type the site lacks, switched off or not (`unknown_type` at `actions[n].type`), and holds an
action's settings to the type's schema and then its own check, as for a field (ADR-0056). From 9.5b the own check can also
warn (`ActionSettingsCheck.Warn`) about a value that will not work as the editor may think, such as a template's tag that names
nothing; the form check lists the warning, which never blocks (ADR-0177).

**A pass of the worker** (`OutboxPass`) runs its queues in their order: the action runs (`ActionRunQueue`) now, the mail from 8.2 on.
It does nothing while sending is paused, while Umbraco does not run or while a migration is pending. A queue marks expired leases,
reads up to 25 due runs and claims them four at a time, and stops claiming when the two minutes are over or sending is paused. A
pass that leaves work due wakes the worker again.

**An attempt** (`ActionAttempts`) reads the entry and the version's action, with the host's locks applied, and gives the type its
time on the thread pool.

| What came of it | The run becomes |
| --- | --- |
| Succeeded | `Succeeded` |
| Try again, or an exception | `Pending` after 1, 3, 9 or 27 minutes, and `Failed` after the fifth attempt |
| Failed | `Failed`, with the rest of its batch `Skipped` when it stops the rest |
| Uncertain, cut off by its time limit or by the host stopping, any cancellation, or an error once it was told to stop | `Uncertain` |
| The type is gone, or the version has no such action | `Failed`: no later attempt can change that |
| What it needs could not be read, or the host stopped before it began | `Pending` after the back-off, never `Failed`: nothing was sent |

An exception is recorded by its type alone, because its message may hold what a visitor typed or a secret (D13). A type that does
not stop within 5 seconds of being told counts as cut off, and its result replaces `Uncertain` when it comes. The outcome always
goes through the claim's token, so the late finisher of an attempt that was retried meanwhile records nothing. Outcomes are recorded
whatever the host is doing, with no cancellation; one that cannot be recorded leaves the lease to run out into `Uncertain`, so nothing
is delivered twice.

**The notifications** `ActionRunningNotification`, `ActionRunCompletedNotification` and `ActionRunFailedNotification` come around each
attempt. A handler that throws is logged and changes nothing, and one that hangs holds the attempt up for 5 seconds at most.

**The wake** is `FormBuilderDatabase.PublishAfterCommit`, called by the repository's `AddAsync` and `RequeueAsync`, so everything that
queues a run wakes the worker and a rollback wakes nothing.

**Settling an entry** (`ActionSettlement`) happens in the unit that records a final outcome, when the form has a kept field or stores
no entries: hold the form's row, then the entry's, as every unit does that touches what belongs to a form, record, then look. It lets go only when no run is pending, running or uncertain and no trigger can
still fire for the entry's state: any while it is `Unverified` or in `Spam`, `approved` or `rejected` while it waits for approval,
`approved` when it is approved at once, and none once it is `Approved` or `Rejected`. A `Draft` is left alone. Because the unit's first
statement is the hold, no last run can miss another's outcome, and a crash cannot leave a final run with its values.

- **A kept value** goes with its file rows, and its search text is made again from what stays. The event `valuesRemoved` names the
  fields' keys. The bytes of its files go after the commit.
- **A whole entry** of a form that stores none goes with its values, files, events, notes, runs and unsent mail. An entry in `Spam` or
  `Unverified` stays until a person or the visitor decides, or retention takes it, so no lead is lost. An entry of such a form with
  no action that can fire goes at once, whether its receipt and outcome could be made or the visitor has gone, and a retention part finds
  one that a stopped server left behind.
- **A user's "not spam"** that queues nothing settles when its undo window is over (8.5, ADR-0023), so Undo has something to restore.
- **Its challenge stays spent.** `jfb.usedchallenge.<id>` in `jfbKeyValue` holds that the challenge was used. The unit that stores an entry
  checks it, holding the form's row as the unit that lets an entry go does, so a replay meets either the entry's unique index or the mark,
  whatever the form stores by then. A retention part forgets the mark after the challenge's lifetime and its grace for receipts.

**Imports.** A type names what the site lacks for an action by its configuration key (`MissingConfiguration`), never a value. An
import switches such an action off and adds a note with its path and that key (`action_configuration_missing`); settings that do not
fit the type's class are left to the publish check. One whose type the
site lacks is switched off too (`action_type_missing`), and publishing goes on refusing it until it is removed or the site has the type.

**Designer locks** of kind `action` work as ADR-0060 says for fields: start-up refuses what does not fit, a draft save refuses another
value than the lock's or the stored draft's, and publishing checks and attempts see the locked value. The action types endpoint shows
each lock as the field types endpoint does.

**The retry** (`POST form/{key}/entry/{entryKey}/action-run/{runId}/retry`, `manageActions`) refuses an uncertain run without
`confirmed: true` (`409 retry_needs_confirmation`) and a run that neither failed nor is uncertain (`409 run_not_retryable`). It is audited.

**Test runs** (`POST form/{key}/action/{actionKey}/test`, `manageActions`) run a draft's action against a sample entry, numbered 0, with
made-up answers. No entry and no run changes. A type declares how its test behaves: `DryRun` reaches nobody, `ToCurrentUser` reaches only
the asking user, whose key, address and name the context carries, and `AfterConfirmation` needs `confirmed: true`
(`409 test_needs_confirmation`). A test that throws answers "failed" without its message.

**Two servers.** A test in the process runs two `ActionRunQueue`s with claims of their own against one SQLite database while submissions
come in, with the spike's faults and a clock that moves a minute every few milliseconds, and holds every run to the state its attempts
explain. Against a claim without its state condition it fails. CI's `loadbalancing` job starts two real hosts on one SQL Server database
(`tests/e2e/specs/lb`): every run is delivered exactly once, both servers deliver, a form published on one is served by the other, and a
challenge from one is good on the other. `JFB_E2E_LB=1` runs that suite on one SQLite host, to prove the suite itself.

**A change of the configuration that fails its validation** keeps the last good answer of the pause switch, because the wake runs after a
submission's commit and must not fail a visitor over a typo elsewhere.

## Consequences

- A type that blocks a thread holds that thread and not a pass.
- **A retry from the log after the values went has nothing to send.** Failed and skipped runs count as final (section 4.4), so a retry
  of one runs without the kept values, and on a form that stores no entries there is no entry left to retry. A site that needs retries
  keeps its values.
- **An `Uncertain` run holds its values** until someone retries it or retention takes the entry, because the log must be able to retry
  it. The deliveries log (11.x) shows such runs.
- **A kept option's place follows its value** (ADR-0090): when the value goes, so does the place, and the same goes for a kept unique
  answer (ADR-0091). The form check (9.5) warns about limits on such fields.
- **A form that stores no entries holds no entry limit, place or unique answer once an entry is gone,** and a lost answer's retry cannot be
  answered from its entry: the visitor's page asks for a new challenge and sends again, which can run the actions a second time. The
  form check warns about limits on such a form.
- 8.2 adds the mail queue as a second `IOutboxQueue`. 8.5 queues the runs of confirmation and of approval, calls `SettleAsync` when a state
  change leaves no trigger, and turns "wakes the worker" into a timed wake where an undo window makes it worthwhile.
- **Revisit** if types need a say about their own back-off, if sites report many `Uncertain` runs, or if a retry should be able to see
  values that went.
