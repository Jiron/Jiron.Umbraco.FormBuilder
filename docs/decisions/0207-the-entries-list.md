# 0207. The entries list: one list service, a user's columns and filter sets, and the bulk bar

- Status: accepted
- Date: 2026-11-07
- Roadmap step: 11.1, its part 11.1 (the entries list; ADR-0197); builds on
  [ADR-0003](0003-entry-storage.md) and [ADR-0039](0039-entries-query-builder.md) (the query shapes),
  [ADR-0023](0023-undo-for-entry-state-changes.md) (Undo), [ADR-0048](0048-permission-evaluation-and-visibility-filter.md) (the access filter),
  [ADR-0162](0162-state-changes-verification-and-content.md) (one service moves entries), [ADR-0163](0163-save-and-resume.md) (drafts),
  [ADR-0166](0166-the-form-library.md) (unseen) and [ADR-0204](0204-test-entries-and-sample-answers.md) (test entries);
  IA 5.4 and 6.1, copy.md

## Context

Step 11.1 builds the backoffice's entries screens. Its first part is a form's Entries view: the list of IA 6.1 for one
form, with a column chooser saved per user, filters as chips and filter sets saved per user, search with a count
exact up to 1,000, sort, paging, unseen markers, the action status of each entry, a bulk bar whose state changes Undo
takes back for 10 seconds, the keyboard of §4.13, and the 100,000-entry budget of §4.12. The Inbox and Spam reuse the
list in the step's third part.

Underneath, much is built (a survey of the code, 2026-11-07): the query repository with its filters, sorts, keyset
cursor, one-pass search count and time budget (ADR-0039), the state changes with Undo for up to 500 entries
(ADR-0162), the timeline, notes and the access filter. None of the list is reachable through an endpoint, the
`jfbUserSetting` table has no service, and the repository's "no states" still means every state, drafts included.

The design comes from Phase 2 (IA 6.1, `docs/ux/wireframes/entries.html`, copy.md). The three design skills read it
again for this part (ADR-0019, ADR-0020): an operate-mode data table inside Umbraco's backoffice, in UUI and the
section's tokens, calm and dense, nothing animated that a key repeats. The owner answers no questions during the run
(CLAUDE.md rule 8); each choice below is this record's.

## Decisions

1. **One list service, `EntryLists` (Core),** turns a list request into the repository's query and a page of rows,
   for the Entries view now and the Inbox and Spam later. It always names its states: an empty state filter means
   every state but `Draft` and `Spam` (Spam is its own folder, IA 6.2), so a draft is in no list, count or search
   (ADR-0163), whatever the repository's "no states" means; test entries stay out (ADR-0204).
2. **The request** is the filter of IA 6.1 in JSON, the same shape a saved filter set keeps: the states; Received, as
   a preset (`today`, `thisWeek`, `last7Days`, `thisMonth`, `lastMonth`) or a range of dates, both in the site's time
   zone (`SiteTimeZones`), so "today" in a saved set stays today; Actions (`failed`, `uncertain`, `delivered`); the
   form's fields by key, each with a test its type allows (one of, none of, starts with, a number or date range,
   checked, answered); the search; and the sort (received, or a field). A field filter, sort or column on a field the
   form does not have, or one of another type than its filter, is refused with its path (`400`).
3. **Actions** filter on the entry's runs: `failed` holds an entry with a failed run, `uncertain` one with an uncertain
   run, `delivered` one whose every run succeeded or was skipped, with at least one run.
4. **Search** matches the answers of fields that are not sensitive, as the repository searches (ADR-0039), and a term
   that is an entry's number (`1042`, `#1042`) or its reference in the form's format (`REQ-2026-01042`) matches that
   entry as well. The entry title is written from the answers, so its answers are found; its fixed words are not.
5. **A page** is 50 rows, newest first; **Show more** appends the next 50 after the opaque cursor the last page gave.
   The first page also asks for the count: exact, or "more than 1,000" for a search (ADR-0003).
6. **A row** carries the entry's key, number and title (the form's entry title written for its answers, or none), its
   state, when it was received, whether it is unseen, its action status (how many runs, how many delivered, failed,
   uncertain and waiting) and the values of the columns the user chose, each as its type reads it.
7. **Sensitive fields are no columns, filters or sorts.** A value in a list cell is never masked and revealed there:
   reveal is one value at a time, audited, in the entry's panel (11.2). The column chooser says once that sensitive
   fields show in an entry only.
8. **Unseen** means no backoffice user has opened the entry (`jfbEntry.firstSeenUtc` empty, ADR-0166); the column's
   comments that called it the visitor's first view are corrected. Opening an entry marks it seen, for everyone.
9. **A user's own choices** go to `jfbUserSetting` through one service, validated by name (ADR-0008: no scopes, locks
   or audit): `entries.columns.<form key>`, the columns chosen for a form, at most 20 of its fields beside the details;
   and `entries.filterSets`, at most 50 named sets, each with a name of at most 60 characters, the form it belongs to
   or none, and its filter. A set of a form that is gone, or that the user may no longer view, is not offered.
10. **The bulk bar** acts on the rows ticked, at most a page (the state service's 500): Approve, Reject and Mark as
    spam through the state endpoint, with Undo until the answer's `undoUntilUtc` (the toast now follows it rather than
    a fixed 10 seconds, and says when it is too late); **Delete…** asks with the count first, deletes for good, raises
    `EntryDeletedNotification` for each entry (ADR-0161) and writes one audit entry; **Re-run actions** asks first with
    the actions it runs, and queues, for each entry not in Spam, waiting for its confirmation or a test, the actions of
    the triggers it has passed (Submitted, Approved or Rejected, Verified) as one new event, in the order shown; it
    needs `manageActions`. An action applies when it applies to one ticked entry at least, and the toast says how many
    it left out.
11. **Export and the summary come with their features.** The toolbar's Export… and the bulk bar's Export selected…
    open the export dialog of 11.4, which builds it; 11.4 gains a check that Export selected… exports the ticked
    entries. "Email me a summary…" comes with the digest, the step's third part. Neither shows before then.
12. **The keyboard** (§4.13): `j` and `k` move a cursor drawn as a focus ring, `Enter` opens, `a`, `r` and `s` act on the
    cursor's row, `/` focuses the search, `Esc` clears the cursor or closes, `?` lists the keys in a dialog with only
    Close. Nothing animates (ADR-0016, amendment).
13. **The budget:** the first page of a form with 100,000 entries, with its count, loads in 300 ms at most on SQLite
    (§4.12), measured through the list service against seeded entries; Playwright seeds 1,000 entries through a test
    host endpoint that writes them as the pipeline stores them.

## Consequences

- Positive: the Entries view, the Inbox and Spam share one service, one request shape and one row; the request a list
  sends is the filter a set saves.
- Positive: a draft, a test entry and a spam entry can reach no list by an empty filter.
- Negative: a sensitive field cannot be a column or a filter; its value is read in the entry.
- Negative: a search for a fixed word of the entry title finds nothing; the answers it is written from are found.

## Amendment (2026-11-07, step 11.1, the server's part)

Building the service settled what the decisions left open:

- **The fields a list offers** are the form's catalogue rows that a published version had and that hold answers: the
  live version's in their order, then those it removed, which older entries still answer. A grid's rows are parts of
  its grid's answer, not fields of their own. A field of a group, a repeatable group or a grid among them, can be a
  column; a repeatable group's child filters and sorts but is no column. A field inside a sensitive repeatable group is
  sensitive too.
- **The tests by kind:** text one of, none of, starts with, answered and not answered; a long answer starts with,
  answered and not answered; a number or a date between (both ends included, either open), answered and not answered;
  a checkbox checked and not checked; files and values of several parts answered and not answered. A date answer is
  compared by the day it names, in no time zone, as it is stored as the visitor's wall clock. Text, numbers and dates
  sort.
- **Received** counts the days of the time zone every site shares (`general.timeZone` without a site); a week starts
  on Monday. Ranges lie in the years 1753 to 9999, where the database stores times.
- **A cell** holds the column's answer as the summary page lists it (ADR-0122), in the entry's culture as its title
  is: a repeatable group's rows each as one text, and a sensitive answer of an older version masked, never revealed.
  A page reads its entries' answers, `submitted` events, versions and pages at once, and counts their action runs in
  one statement, so a page costs the same few reads whatever its columns.
- **Every problem names its path** in the request (`filter.states[1]`, `filter.fields[0].test`, `columns[0]`,
  `after`), and a list answers them all at once; a cursor of another sort, or one the list never gave, is
  `invalid_cursor`.
- **A filter set of every form** filters and sorts by no field. A set's name is trimmed and taken once per form, or
  once among the sets of every form, whatever its case. Deleting a set that is gone already answers that it was not
  there, not an error, as every endpoint of the whole section answers a user of the section.
- **The audit log** takes at most 200 values in a list, so a delete or a re-run writes one entry for every 200
  entries, each naming their numbers.
- **Re-run actions** runs what the rules chose for the entry: the choice its `submitted` event keeps when a later
  trigger needs it, or else the actions its runs show. An action disabled since, or not in the entry's version, does
  not run. Without `confirm` the endpoint answers what would run, which the dialog shows before anything runs.
- **The notifications** of a delete are published in their order under one limit of 5 seconds (`EntryEvents`), so a
  delete of 500 entries never holds the user up for each.

## Amendment (2026-11-07, step 11.1, the Entries view)

Building the view settled what the decisions left open for the browser:

- **Where its code lives.** The view is one of the form workspace's views, so it lives with them
  (`designer/entries/`) and may ask for the workspace's words, which load with the workspace (ADR-0201): a view whose
  own words fail to load says so in them. What the Inbox and Spam share with it (the list's model, its calls, its
  dialogs and its words) lives in `entries/`, and its words load with the view, not with the backoffice's start
  (ADR-0183).
- **The details are fixed columns.** Entry, State, Received and Actions always show, so a list never loses what names
  or sorts an entry; the column chooser offers the form's fields only, at most 20, in a sidebar.
- **A choice field's filter ticks the live version's own options,** by their labels, and its chip names them so;
  options that hang on another answer, or that come from an option list or a source, are typed, as a text field's are.
- **Delete… says what goes with the entries.** Without `confirm` the endpoint answers how many entries and files the
  delete takes, which the dialog names before anything is deleted.
- **The Undo toast follows the change's window** (`undoUntilUtc`, by the browser's clock): until it ends, the toast
  offers Undo; then the button leaves and the toast says for six seconds more that it is too late (copy.md,
  Confirmations), so it never offers an Undo the server refuses. A window under a second offers none, and one over a
  minute offers Undo for a minute. An Undo the server finds too late says the same sentence.
- **The keys act where the list is:** inside the view, or with nothing focused; never while the user types in a field,
  and not inside a dialog. The dialog of `?` lists one key per line.
- **Narrower than 57.5rem,** the action status gives way, and a failure shows under the state, so the names keep
  their width.
