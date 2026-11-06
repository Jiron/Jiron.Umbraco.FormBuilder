# 0195. An action's last run on its card, and its Details

- Status: accepted
- Date: 2026-11-06
- Roadmap step: 10.2f; builds on [ADR-0013](0013-permissions.md) (its amendment: who can retry),
  [ADR-0040](0040-outbox-repositories.md), [ADR-0188](0188-the-actions-view.md) and
  [ADR-0190](0190-test-an-action-as-the-sidebar-holds-it.md); IA 5.3, copy.md

## Context

IA 5.3 gives each action's card its last run ("Delivered 2 hours ago"), and a failed or uncertain one a link to
**Details**: what the server answered and the tries, never the entry, who can retry the run, and Test. copy.md has the
status words (Delivered, Failed, Uncertain) and the line "Failed at 14:02. Next try at 14:30.".

What a run stores (ADR-0040): its state, its attempts, when the first started, when it ended, when it is tried next,
and the code and message of its last failure, which the type has made free of personal data (8.1). It does not store
when its latest attempt ran: a failed attempt that is tried again, or one cut off, keeps no time, so "Failed at 14:02"
had nothing to show. Nor can a form's runs be found by action: the runs table is indexed by entry and by due time
only, and a form's busiest action may have hundreds of thousands of runs.

The design skills shaped it before it was built (ADR-0019, ADR-0020). Read as Operate mode inside Umbraco's design
system, for a designer who glances at a form's actions and, when one has failed, wants to know why and what to do
without seeing anyone's answers. The skills' discovery questions are answered by IA 5.3, copy.md and ADR-0013, as the
owner answers none during the run.

## Options

**Where the card's run comes from**

1. The entries' runs, read by the backoffice: needs `viewEntries`, which a designer may not have, and names entries.
2. **One endpoint for a form's actions' last runs,** each without its entry, from an index on the action.
3. The last run kept on the action in the definition: a write to the form for every run, and versions that change by
   themselves.

**The time of an attempt**

1. Say "Failed. Next try at 14:30." without a time.
2. **Store when each attempt starts,** as the claim already writes the first.

**Who can retry**

1. The same sentence for everyone, naming who can.
2. Words that depend on the reader's permissions: they would name screens the reader may not have, and still name no
   entry.

## Decision

Option 2 each time, then option 1.

- **Step 10 of the migration plan** (`AddActionRunLastAttemptV1`) adds `lastAttemptUtc` to `jfbActionRun`, which a
  claim sets to its own time, and `IX_jfbActionRun_actionKey_id_state_attempts_entryId` on
  `(actionKey, id, state, attempts, entryId)`: the read seeks by action and walks back by id, and the rest is what it
  filters on, so the runs it passes over cost no lookups.
- **`GET form/{key}/action/last-run`** (`design` on the form, as the Actions view) answers, for each action of the
  form's draft that has one, its last run: the newest that has been tried or has settled, so a run that waits for its
  first try never hides the one before. Each is the action's key, the state, the attempts, the first and the latest
  attempt, the next one (for a run that waits for it only), when it ended, and the failure's code and message: no run
  id, no entry and no answer. A run of an entry of another form never counts. Each action is one seek: the newest of
  its runs, by the index, that belongs to an entry of the form.
- **A retry from the log starts the attempts over** (ADR-0040), and now their times with them: the count, the first
  and the latest attempt describe the same round, so Details never say three tries since one retry from a first one
  before it.
- **The card's line,** under its name, summary and rule, in the type's own words: "Delivered 2 hours ago" for a
  webhook, Slack or Teams, "Mail queued 2 hours ago" for an email (the action hands the mail to the outbox, whose
  delivery is the mail log's), and "Ran 2 hours ago" for the others; "Failed at 14:02. Next try at 14:30." while it is
  tried again, and "Next try soon." once that time has passed, as while sending is paused; "Failed at 14:02." once it
  has failed for good; "Uncertain at 14:02. It may have gone through." where an attempt was cut off, in copy.md's
  word; "Running now"; "Skipped 2 hours ago". A recent time is relative, an older one a date; a failure is said in the
  danger colour, an uncertain one in the warning colour, every other line muted. A run tried several times before step
  10 knows only its first try's time, and its line says none ("Failed. Next try at 14:30.") rather than a wrong one.
  An action that has never run says nothing, and a line never repeats the problems the form check counts. The line
  has the card's width under the name, and comes before the card's controls as it is read.
- **Details,** on the line of a failed or uncertain run, opens a small sidebar named for the action, in four parts
  under small headings, without landmarks of their own: Last run, the line again; What happened (the failure's code
  in the backoffice's words: "The server answered 503.", "The server didn't answer in time.", and a site type's own
  message where the backoffice has no words for its code); Tries ("Tried once.", or "Tried 3 times, the first at
  13:40." beside the line, which says the latest's time); and What can be done: Test sends a sample entry, so the
  designer sees whether the server answers again, and someone who sees the form's entries and may change its actions
  finds the entry in Entries with the Actions filter and retries the run there, an administrator in Activity ›
  Deliveries (ADR-0013, amendment). Its footer has Test, for who may change actions, and Close; Test is busy only once
  the test runs, as a webhook's asks first and a No runs nothing (ADR-0190). Who may only read sees the line and
  Details too, without Test.
- **Fresh enough:** the view reads the last runs when it opens and again when its page becomes visible after being
  hidden, and the relative times are said when drawn. Only the latest read's answer is said, and a form chosen
  meanwhile never shows the one before's runs. A Test is no run, so it changes no line.

## Consequences

- A designer sees which actions work and which do not without the entries, and why one failed without anyone's
  answers.
- The migration plan has ten steps; the claim writes one more column.
- The management API has one more endpoint, in the permission matrix and the OpenAPI snapshot.
- Activity › Deliveries (Phase 12) is where an administrator retries; the entry's timeline offers a retry to who may.
- Known limits: the read passes over an action's runs that wait for their first try, which the index makes cheap but
  not free while sending is paused with a long backlog; and an action key that a draft saved through the API shares
  with another form's action makes the read pass over that form's runs too, though they never count. Until an
  administrator runs step 10 on a site that does not run its migrations by itself, queueing an action fails on the
  missing column, as step 8 made entry events do.
