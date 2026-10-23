# 0023. Undo for entry state changes: their actions wait for the undo window

- Status: accepted
- Date: 2026-10-23
- Roadmap step: 2.4 (decided); built in 8.5 (the delayed runs and the undo endpoint), 11.1 to 11.3 (the toasts)

## Context

ROADMAP §4.13 asks reversible actions to use undo instead of confirm dialogs. Entry handlers change states many
times a day, often from the keyboard (j and k move through the list, then a key or a button approves): Approve,
Reject, Spam and Not spam, one at a time in the entry panel or many at once from the bulk bar. The wireframes of
step 2.4 draw a toast with Undo after each ("Entry #214 approved. [Undo]").

A state change can start actions (§4.4): `approved` and `rejected` triggers, and "not spam" queues the `submitted`
actions. Those send emails to visitors, call webhooks and create content. An email that has gone out cannot be
called back, so an Undo that only puts the state back would leave the visitor holding an approval that no longer
exists.

## Options

1. **Confirm before each change.** Nothing runs by mistake, but every approval costs a dialog, which is exactly what
   §4.13 rules out for a frequent, reversible action, and it slows the keyboard path to a crawl.
2. **Undo puts the state back only.** Quick, but untrue: the actions the change started still run, and the toast's
   Undo promises more than it can keep.
3. **The actions a change starts wait for the undo window** (chosen). The change and its action runs are stored
   together, as always (D6), but the runs carry a not-before time at the end of the window. Undo inside the window
   puts the state back and cancels those runs, which cannot have started yet.

## Decision

Option 3.

- **The window** is 10 seconds from the moment the server stores the change. It is a constant of the product, not a
  setting: it only delays what a person just did, and nobody needs to tune it.
- **What waits:** every action run queued by a state change that a backoffice user makes (single or bulk: approve,
  reject, spam, not spam), and the entry events that Jiron Form Builder sends to Umbraco's webhooks for that change
  (approved, rejected). Changes made by a rule, by a "set state" action, by email verification or by automatic
  approval run at once, as before, since nobody can undo them from a toast.
- **Undo** is one management API call that names the change. Inside the window it restores the earlier state of
  each entry the change touched, cancels the runs and events that change queued, gives places back or takes them
  again (§4.4: when an option has filled up meanwhile, it asks before overbooking), and records an "undone" event on
  each timeline and in the audit log. After the window it is refused with `409`, and the entry keeps its state; the
  user can still change it by hand, as with any entry.
- **The toast** shows Undo until the window ends, and the response of the change says when that is
  (`undoUntilUtc`), so the client never offers an Undo the server will refuse. A toast with an action stays while it
  has the pointer or the focus; its Undo button disappears at the end of the window even then, and the toast says
  "It's too late to undo this. Change the state back by hand if needed."
- **Running at the end of the window:** the processor picks up a run once its not-before time has passed. This
  server sets a timer for the end of the window after the commit, as it wakes the processor today (D8), and the
  distributed job remains the safety net, so a run still goes out when that server stops. The not-before time is
  stored with the run; the timer holds nothing that another server needs.
- **Deleting** stays a confirmed action without undo (§4.4: deleting is permanent).

## Consequences

- **Schema (§4.5, built in 4.1):** a waiting run is an ordinary `Pending` run whose first `NextAttemptUtc` is the
  end of the window, so the queue needs nothing new. To find what one change did, also a bulk change over many
  entries, `jfbActionRun` and `jfbEntryEvent` get a nullable `ChangeKey` (a GUID per change, indexed). Undo marks the
  change's runs `Skipped` with the code `undone`, so the timeline still shows what was cancelled.
- The Umbraco webhook events for approved and rejected are raised by the processor once the window has passed and
  the change still stands.
- Approved and rejected emails arrive about 10 seconds later than they would without undo. For a human decision that
  is invisible to the visitor.
- **Step 8.5's checks** include the delayed runs and the undo call; **11.1 to 11.3** show the toast and test it with
  `page.clock`.
- Revisit if editors ask for a longer window: then it becomes a setting, with the screen that sets it.
