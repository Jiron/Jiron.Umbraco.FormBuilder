# 0208. The entry panel: one entry read for a user, its moves, edits, notes and forwarding

- Status: accepted
- Date: 2026-11-07
- Roadmap step: 11.1, its part 11.2 (the entry panel; ADR-0197); builds on
  [ADR-0207](0207-the-entries-list.md) (the list beside it), [ADR-0023](0023-undo-for-entry-state-changes.md) (Undo),
  [ADR-0090](0090-places-on-options.md) and [ADR-0091](0091-unique-answers.md) (places and unique answers),
  [ADR-0070](0070-storing-answers-and-reading-them-back.md) (stored answers), [ADR-0161](0161-webhooks-slack-teams-and-entry-events.md) (entry events),
  [ADR-0166](0166-the-form-library.md) (unseen) and [ADR-0067](0067-the-umbraco-sources-and-the-base-url.md) (the entry's link);
  IA 6.3, `docs/ux/wireframes/entry-panel.html`, copy.md

## Context

The second part of step 11.1 opens one entry: its answers, details, timeline and notes, the actions of its state, an
edit of its answers, a reveal of its sensitive values and a forward to someone the site trusts. A survey of the code
(2026-11-07) found the pieces underneath (the answer store, files and their authorised download, events, action runs
and their retry, the mail of an entry, the notes table, the outbox, the deletions) but no endpoint that reads one
entry, nothing that marks an entry seen, no reveal, notes service, edit or forward, and no check of full options or
unique answers when an entry moves back into a state that holds them: the repository takes places back without asking
(ADR-0090, ADR-0091 leave the asking to this step). The design is IA 6.3 and its wireframe; the owner answers no
questions during the run (CLAUDE.md rule 8), so each choice below is this record's.

## Decisions

1. **One read, `EntryPanels` (Core),** gives a user one entry: its header (number, title, state, received, the form),
   its details (the page it was sent from, the language, the time to complete; the member, the IP address if stored
   and the spam reasons behind More details), and its answers in the pages and groups of the version it was sent with,
   each as the summary page reads it (ADR-0122) in the entry's culture. A field the live version no longer has is
   marked so. Files carry their name, size, type and whether they are an image to view; a consent carries the exact
   text the visitor saw; a signature carries its image and, when it was typed, the name read from its PNG. A field
   kept only until the actions ran reads "not kept" once its values are gone. Every empty answer reads "none".
2. **Sensitive values never leave the server unasked.** The read sends a sensitive answer as masked, with whether the
   user may reveal it (`viewSensitive`). **Reveal** asks for one field's answer (a repeatable group's with all its rows)
   and writes one audit entry each time (`entry.sensitiveRevealed`, naming the entry and the field, never the value). A sensitive file is named only once revealed; its download is gated and audited already.
3. **Opening marks seen.** The first time a backoffice user reads an entry, `firstSeenUtc` is set and an `opened` event
   names that user, so the timeline says who opened it first and every list shows it seen, for everyone (ADR-0207).
4. **The timeline** merges the entry's events, its action runs (with Retry through the existing endpoint, for
   `manageActions`; an uncertain run asks first) and its mail, newest first, each named in the user's words. An edit's
   old and new values show on it as the reader may see them: a sensitive one masked without `viewSensitive`.
5. **Notes:** anyone who may open the entry reads them, adds one (at most 10,000 characters) and edits their own; a
   note is no change of the entry, so it needs `viewEntries` only and writes no audit entry.
6. **A move back asks first (ADR-0090, ADR-0091).** When entries move from a state that holds nothing (Rejected, Spam)
   into one that holds places and unique answers (Unverified, Submitted, Approved), the state service first reads
   whether an option they would take is full by then and whether another holding entry has an answer that must be
   unique. Then nothing moves: the answer names each full option (its field, label, limit and places taken) and each
   held answer (its field, never the value; the counts say that an answer is held, not by which entry), and the user may move them
   anyway with `overbook`. An option list's or a source's options are asked of it as a submission asks, in the
   entry's culture and page and for its parent's answers; a source that cannot answer leaves its options unasked. The list's bulk bar,
   the panel and Not spam (11.3) share it. **Undo** is ADR-0023's promise
   to put things back as they were inside its window, so it never asks, and may overbook by what it puts back.
7. **Back to waiting.** On a form with manual approval, an approved or rejected entry's "…" holds **Back to waiting for
   approval**, the way copy.md's "change the state back by hand" takes. From Rejected it asks as a move back does.
8. **Edit answers** (`editEntries`) edits the answers of the fields outside repeatable groups whose kind is a text, a
   long text, a number, a date, a choice of one or many of the field's own options, or a checkbox; files, consents,
   signatures, calculations, a repeatable group's rows, grids, rankings, values of several parts and the choices of an
   option list or a source stay as they are. A sensitive answer is edited only once revealed, as its value reaches the
   browser no other way. Each value is read and checked by its field type as a submission's is, rules aside (an edit corrects what
   was stored). A changed choice moves its place, and a change asks as a move back does when an option is full or an
   answer is held by then, if the entry holds places. The values change at once; an `answersEdited` event keeps the
   old and new values of each field for the timeline, and the audit names the entry and the fields.
9. **Forward** (`editEntries`, as the permission model has it; a privacy officer's bar holds no Forward…) sends the
   entry through the outbox (`EmailKind.Forward`) to a backoffice user who may view the form's
   entries and has an address, or to one of the forward addresses of the entry's site (Settings › Email); any other
   recipient is refused (`400 forward_recipient_not_allowed`). The mail holds the optional message, the answers and
   the entry's link; sensitive values go along only when the sender may see them and chooses to. It is audited
   (`entry.forwarded`) and shows on the timeline as a `forwarded` event and as the mail it sent.
10. **The panel's actions** are IA 6.3's table, filtered by what the user may do and whether the form approves by
    hand. Delete… asks with the files that go too (ADR-0207), and Re-run actions asks with the actions, as the bulk bar
    does, for the one entry. **Print** comes with 11.4's printable view of an entry, as Export comes with 11.4.
11. **Beside the list:** opening a row narrows the list to each row's name, state and time, and the panel takes the
    rest; where the view is narrower than 50rem the panel covers the list and its header adds what the row showed.
    The list keeps its place, its ticks and its scroll. `Enter` opens the cursor's entry, `j` and `k` move on with the
    panel following, `a`, `r` and `s` act on the open entry, and `Esc` closes it, the focus back on its row.
12. **The entry's link** (`/umbraco/section/jfb-forms/workspace/jfb-entry/edit/<key>`) opens the panel alone, its header
    naming the entry's title, the form and the state. Its form is found through `GET entry/{key}/form`, which names none
    to a user who may not view that form's entries, as for an entry that is gone (every endpoint of the whole section
    answers a user of the section), so the link shows the backoffice's "not found" and nothing of the entry.
13. **The routes** sit under the form, as the list's do, so the permission attribute guards them:
    `GET form/{key}/entry/{entryKey}`, `POST …/reveal`, `GET …/timeline`, `GET`/`POST …/notes`,
    `PUT …/notes/{noteId}`, `PUT …/answers`, `GET`/`POST …/forward`; the move's `overbook` joins the state endpoint.

## Consequences

- Positive: a sensitive value reaches the browser only by a reveal that is audited, one value at a time.
- Positive: no move, edit or Not spam overbooks an option or doubles a unique answer without the user saying so.
- Negative: a repeatable group's rows, grids, rankings and files can't be edited in the panel; they are the visitor's
  answers as sent.
- Negative: Undo may overbook by the place it puts back, when another entry took it inside the window.
- Negative: a move back onto a source's option overbooks without asking while the source cannot answer.
