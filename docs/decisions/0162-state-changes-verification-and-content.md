# ADR-0162: State changes, email verification and the content action

Status: accepted, 2026-11-02 (ROADMAP 8.5). Builds on ADR-0007 (the outbox), ADR-0023 (undo), ADR-0140 (the spam folder), ADR-0144 (scanned files), ADR-0158 (letting go) and ADR-0159 (visitor mail).

## Context

An entry moves through states by hand, by a rule, by a confirmation and on its own when a form does not wait for a person. Each move can start actions and tell other systems. A person's move can be undone for 10 seconds, so what it starts has to wait, on whichever server it was made, and survive that server stopping.

## Decision

**Automatic approval means the state.** A form without manual approval stores its entry as `Approved`, and that entry's submitted and approved actions queue together, in the order shown. The state tells a reader what happened. Merge tags that reach a visitor never show `Spam`: they show the state the entry would have reached, so a bot learns nothing from a mail or a page.

**One service moves entries.** `EntryStateChanges` makes each move a unit of its own with its timeline event and its trigger's runs, so a bulk change that meets one entry it cannot move leaves the others moved and says which it did not. A change moves at most 500 entries.

- An unverified entry goes only to Spam or through its confirmation: a person cannot approve around a double opt-in.
- A move that races another user stops where it stands. The other user's move stays, and the answer lists this entry as not moved.
- Moving back to a state that holds places overbooks without asking here. Asking belongs to the panel that shows the question (11.2).
- Approving an entry again after it was moved away fires its approved actions again. A person chose to, and the deliveries log shows both.

**A later move replaces what an earlier one had waiting.** Two people can act on an entry inside one window, so "the last move decides" has to hold for what goes out.

- A move cancels the entry's earlier moves that still wait, and skips that change's runs for this entry, with the code `superseded`.
- A move to Rejected or Spam skips every run of the entry that has not started. An entry that was thrown out should send nothing more. A run that has started goes on, since it may already have arrived.
- A queue item tells of a state only while the entry still has it.
- Taking an entry out of Spam queues only actions that have no earlier run for the same trigger, so a trip to Spam and back repeats nothing.

**What waits is an event's `dueUtc`.** Runs already carry their not-before time. The other things a person's change leaves until its window is over (entry events, letting go of kept values, an address's mail) need the same durability, so `jfbEntryEvent` gets a nullable `dueUtc` and a queue beside the other two. The alternatives were worse: a hidden action run would show on every timeline and deliveries list, and a timer alone is lost when a server stops.

- A pass claims each due event by clearing `dueUtc` in one statement, so servers cannot both do it, and an Undo that clears it first wins. What the claim leads to is told at most once.
- Nothing lets go of an entry's kept values while a move of that entry still waits, as an Undo could bring its actions back.
- A due item that fails waits 5 minutes for its next try, so it cannot hold up what comes after it.
- This server also sets a timer for the end of the window, so the wait is 10 seconds and not the distributed job's interval.

**An Undo is all or nothing.** It holds the form, then clears the change's events and checks its runs in one unit. It answers too late and changes nothing when the window is over, it was undone already, an entry has moved on since, the queue got to one of the events, or one of its runs has started. An Undo that overwrote a colleague's later decision would be worse than one that says it is too late. The time the answer promises is taken before the first move, so it holds for every entry of a bulk change.

**Daily counts cannot go down**, so a person's move out of Spam counts a submission only once its window is over, and a system's at once. An entry that was counted before someone marked it as spam is not counted again.

**Set state** is a thin action over the same service with no user, so it runs at once. An entry already in that state has nothing left to do and succeeds, and one the move does not apply to fails for good.

**Email verification** stays a form setting, and its mail is a system mail.

- The request for the mail and the time the entry expires are events due in the entry's own unit: at creation, when it leaves Spam, and for a new link. A server that stops after the commit loses neither. Expiry does not depend on a mail having gone: an entry whose mail never could go still leaves on time, and the latest expiry decides.
- The mail goes through the visitor limits of ADR-0159, in one unit with the claim of its event. A failure leaves the event for a later try. Its link needs the site's own address and never a request's (section 4.10), so a site that does not know its address sends no mail with a dead link, and says so in the log.
- The address field cannot be kept only until the actions have run, as it has to outlast them. Publishing refuses that.
- The token is Data Protection's, as the placements' are: it names an entry and a time and can be neither read nor made up, across load-balanced servers.
- `GET` shows a page and changes nothing, as mail scanners follow links. `POST` confirms, once. The page stands alone: no script, nothing from elsewhere, a content security policy with its one style's hash, `Referrer-Policy: same-origin` and `noindex`, so the token goes to no other site. It is not `no-referrer`, as a browser then sends `Origin: null` with the form, which the public API refuses.
- A link past its time offers a new one, at most once an hour, which obeys the same limits and gives the entry more time. A link still in time offers nothing new, so the button cannot be used to fill a mailbox.
- **An entry nobody confirms is deleted** a week after its link's time, with its values, its files and any mail still waiting, and the audit log says so. An address that was never confirmed should not hold personal data or a place on an option for a year. Keeping it in a retention period was the alternative, but nothing can come of it, and the week leaves a late visitor their way back.

**The content action** creates or updates a document.

- **A form's page comes from the request, so nothing it names is taken on its word.** With the visitor's page or its site root as the parent, the action has to name an item it must stay under, and a run whose page lies elsewhere fails. Otherwise a visitor could pick where content gets created by posting another page's key. The cost is that one action serves one site's tree on a multi-site install; a form used on several sites has one action for each.
- Publishing holds a mapping to what a property can store, by field type and property editor, and the run converts an answer to what the editor stores. An answer it cannot convert fails the run for good, with nothing saved. A fixed value on a non-text editor has to be valid when the form is published, and holds no merge tags.
- The keys of the item and its media come from the run's delivery key, so a retry finds what an attempt before it created, instead of making a second one.
- A document type that varies by culture gets the entry's language, or the site's default where the site does not have that one, and publishes only that culture. Names are cut to the 255 characters an item holds.
- A file goes to media only where the mapping names its field, under the same rules as a mail's attachment (ADR-0144), and a run waits while a mapped file's scan is pending on a site with a scanner. Umbraco's own list of allowed file types applies. A sensitive field only goes where the action names it (ADR-0014); otherwise its property stays empty, with no mask and no file.
- Updating a same-named child is off by default, as what a visitor typed can choose the item. A site turns it on only where that is what it wants.

## Consequences

- `Approved` is now what most entries say, on forms with no approval step.
- The events of a person's change go out at most once, as ADR-0161 says: a server that stops between the claim and the publish loses that event, and the runs are not affected.
- Undo does not yet ask before overbooking. 11.2 adds the question with the rest of the panel's state actions.
- A content action that used the visitor's page without a limit cannot be published. No released version had one.
