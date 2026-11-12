# 0209. The Inbox, Spam, the unseen badge and the summary email

- Status: accepted; every element that polls starts the idle gate since 16.1
  ([ADR-0236](0236-what-the-security-review-changed.md))
- Date: 2026-11-07
- Roadmap step: 11.1, its part 11.3 (the all-entries inbox and spam; ADR-0197); builds on
  [ADR-0207](0207-the-entries-list.md) (the list), [ADR-0208](0208-the-entry-panel.md) (the panel and the move back
  that asks), [ADR-0048](0048-permission-evaluation-and-visibility-filter.md) (the access filter),
  [ADR-0023](0023-undo-for-entry-state-changes.md) (Undo), [ADR-0166](0166-the-form-library.md) (unseen) and the
  owner's poll rule from the chat package (its ADR-0051, ROADMAP §4.10); IA 1, 6.1, 6.2 and 6.4, copy.md

## Context

The third part of step 11.1 opens the entries of every form at once: the Inbox (unseen first, then the newest, with a
form column and a Form filter), Spam (every form's spam, with Not spam and Empty spam…), the unseen count on the
Inbox's menu item, and the summary email a user may ask for instead of watching the Inbox. A survey of the code
(2026-11-07) found the access filter as SQL (`AccessSql.FormClause`), a cross-form query of the newest entries for the
Overview, `EntryKind.Digest` in the outbox and a `jfbUserSetting` table that names the digest already; but the list's
query takes one form, nothing orders by unseen or indexes it, no job runs at a time of day, and the menu items have no
badge. The owner answers no questions during the run (CLAUDE.md rule 8), so each choice below is this record's.

## Decisions

1. **One list, three scopes.** `EntryLists` lists a form's entries (ADR-0207), or the entries of every form the user
   may view entries of: the query's form becomes either one form or the access filter's clause, applied in the
   database before paging (ADR-0048), over forms outside the Recycle Bin. A row of a cross-form list carries its form,
   and a page names the forms on it with what the user may do on each, so the bulk bar offers per row what that form
   allows. The Inbox lists every state but Spam; Spam lists Spam only. Test entries and drafts stay out, as in a
   form's list.
2. **Unseen first.** The Inbox's own order is unseen first, then newest. The keyset runs in two phases, the unseen
   entries newest first and then the seen ones, so the cursor carries its phase; a new index on
   (`firstSeenUtc`, `submittedUtc`) serves the first phase. A sort the user picks (Received, or a field once the list
   is filtered to one form) replaces it.
3. **The Form filter** (Inbox and Spam) chooses one form or several; a chip names them. The form's fields, as filters,
   columns and sorts, are offered while exactly one form is chosen, as IA 6.1 says; otherwise one line in the column
   chooser says that filtering by a form offers its fields. A filter set saved on the Inbox keeps its forms; choosing
   a set with one form filters the Inbox to it, and a set saved while one form is chosen is that form's. So that every
   name is at hand, the Inbox's view names the forms of the user's sets (leaving out a set whose form the user may no
   longer view, as it could not filter the list), and a page names the forms its filter chose even where none of their
   entries is on it.
4. **Acting across forms.** The bulk bar sends one request per form, each through the form's own endpoints and checks;
   the toast says the total and its Undo takes back every change it made. A move back out of Spam or Rejected asks
   first once for all of them, with every full option and held answer (ADR-0208): each form's move is checked first
   (the state change's `check`, which reads what the move would ask and moves nothing), so a Cancel leaves every form
   as it was, and only then does each form move, overbooking only where it asked. The entry panel opens beside the
   Inbox's rows as beside a form's, its header naming the form (IA 6.3).
5. **Spam** (IA 6.2) has no state column and no chip; its bar holds Not spam and Delete…, its toolbar Empty spam…
   (`deleteEntries`), which asks with the count and files of the forms the user may delete entries of, and deletes
   only those, 500 at a time through the deletes of ADR-0207, up to 10,000 a call; a Spam larger than that says so
   and empties on the next call. Not spam asks first, as a move back does, when an option is full or a unique answer is
   held by then. A row Not spam took out stays where it is, its state said under its name, until the list reads
   again, as a form's list changes rows in place; Spam's filter has neither State nor Actions.
6. **The badge.** `GET entries/unseen` (section-wide) counts the unseen entries of the forms the user may view entries
   of, as the tree marks them (arrived, no test), up to 99, so the menu item reads "99+" beyond; it reads at most 100
   rows of the new index. The Inbox's menu item is a small element of its own, Umbraco's
   menu item with UUI's badge in its badge slot, named for screen readers ("12 unseen"); Spam has none (IA 1). The
   count is read on start, when the tab comes back on screen, after the user opens or moves an entry, and every 60
   seconds.
7. **The poll never keeps an idle person signed in** (§4.10). Every call the badge makes goes through an idle gate,
   ported from the chat package's (the owner's code, ADR-0051 there): while the site does not keep users signed in,
   the access token has run out and nobody has used the backoffice since it was last renewed, the poll waits instead
   of renewing it, so Umbraco's own timeout signs the person out on time. Input in any tab of the browser, shared on a
   `BroadcastChannel`, or another tab's renewal lets it go. Once the session has ended and Umbraco asks the person to
   sign in again, nobody is signed in to poll for, so the poll waits for the next sign-in. A hidden tab skips its
   turns. Each input allows one renewal
   (opening the page counts as input), so with Umbraco's defaults (a 20-minute session, its access token a quarter of
   that) an idle person is signed out at most about 25 minutes after their last input, where an ungated poll would
   keep them signed in for good. The backoffice's other calls are the person's own and are not held.
8. **The summary email** (IA 6.4, "Email me a summary…" in the list's "…"): off, daily, or weekly on Mondays, kept in
   the user's settings (`entries.digest`). An hourly job sends each user's summary once the site's 07:00 has passed in
   its time zone (Settings › General of the default site), for the day or the week before, and records the last day
   it covered in a setting of its own (`entries.digest.sent`), in the unit of work that queues the mail, so a period
   goes out once, even across servers (the job is a distributed one) and after a pause, and a user's change never
   races the job. Turning the summary on, or changing how often, records the period due by then (never an earlier day
   than the one recorded), so the first one covers the first period that ends after the choice, rather than days that
   passed before it; saving the same choice again changes nothing, so the summary that is due still goes. It counts the entries that arrived in each form
   the user may view entries of (drafts, Spam and test entries left out), names each form with its count and a link to
   its Entries view, says how many are waiting for approval, and never holds an answer. Without the site's address
   (Settings › General, which the health check names), it names the forms without links, as Forward leaves its link
   out. A period with no entries sends nothing. It goes through the outbox (`EmailKind.Digest`) in the user's
   backoffice language, its dates as that language writes them, to the user's own address. Send test email sends the
   summary of the period due now (the day or the week before) at once, marked as a test, even when no entry arrived,
   and records nothing; the sidebar's preview shows that same mail in the site's layout. The sidebar says when it goes
   out by the place the time zone is named after ("07:00 Berlin time"; "07:00 UTC" on UTC), and says the user's
   address once, under the preview, whose head leaves out its To line.

## Consequences

- Positive: the Inbox, Spam and a form's Entries view are one list, so filters, keys, the panel and Undo behave the
  same everywhere.
- Positive: the badge never keeps a session alive, and the summary never carries an answer.
- Negative: a bulk action across many forms makes one request per form; 500 entries in 500 forms make 500 requests.
- Negative: a site of several time zones sends every summary at the default site's 07:00.
- Negative: the summary counts an entry by the day it was sent, in the state it holds when the summary goes; one
  confirmed by its visitor, or moved out of Spam, after that day's summary went is in no summary. The Inbox shows it,
  unseen first, which the summary only points to.
