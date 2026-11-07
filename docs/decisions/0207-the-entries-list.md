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
