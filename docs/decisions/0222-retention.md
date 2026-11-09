# 0222. Retention: each entry's time, legal holds, the purge, the file store's leftovers and the dry run

- Status: accepted; builds on [ADR-0009](0009-privacy-defaults.md) (the defaults), [ADR-0041](0041-file-store-audit-log-and-daily-stats.md)
  (the file store), [ADR-0045](0045-versions-comparison-and-draft-cleanup.md) (the retention job),
  [ADR-0207](0207-the-entries-list.md) (deleting entries) and
  [ADR-0218](0218-the-settings-screens-and-access.md) (the settings screens); amends ADR-0009's line on consent texts
- Date: 2026-11-09
- Roadmap step: 13.1, its part 13.1 (ADR-0197)

## Context

ADR-0009 set the defaults: entries kept 365 days when submitted or approved, 90 when rejected, 30 in Spam, test entries 7
days, each from the entry's last change of state; a form may set its own days, and a form that keeps entries "for a
number of days" (`settings.submission.store: days`) caps them. The audit log stays a year and the daily statistics three
years. A legal hold keeps an entry past all of it, for at most a year at a time. `jfbEntry` has had `PurgeAfterUtc`,
`HoldUntilUtc` and `HoldReason` since schema v1, with an index on the first. The roadmap asks for the time computed on every
change of state, a job that deletes in batches with files and mail and audits it, a dry run per form, a sweep of what a
dead process leaves in the file store, and the screens.

Three things make this harder than a nightly `DELETE … WHERE`:

- **The rules change after the fact.** A site that shortens Spam to 7 days expects its old spam to follow, and one that
  lengthens it must not lose entries early. A stored time is only right until the next save of the settings or publish.
- **The rules come from two places.** The site's days per state are settings; a form's own days and its days of storage
  are in its definition, which has versions.
- **Deletion is final.** It must never run ahead of the rules, never take an entry a hold keeps, and never race a person
  moving or holding the entry at that moment.

## Decision

1. **The terms of a form** are its served version's own days per state, the site's for the states it leaves, and its days
   of storage when it keeps entries for a number of days. The served version is the one the site serves, else the last one
   it served, else the draft of a form never published (`FormDocuments.ServedOfAsync`), the same one its entries are shown
   by. It is the form as it is now, not the version an entry came with: shortening retention reaches old entries, which is
   what storage limitation asks for.
2. **An entry's time** (`PurgeAfterUtc`) is the earliest of its state's days after its last change of state (a test entry's
   7 days, whatever the rules) and the form's days of storage after its submission; none for a state kept for good. Drafts
   and entries that wait for their address get none: their resume and confirmation links end them (ADR-0162). The time is
   stored with the entry when it is created and in the unit of every change of state (approve, reject, Spam and back,
   confirming an address, Undo), through `EntryRetention`. The form's terms are read before the unit begins, as every
   read is (ADR-0007): read inside a write, the site's settings could wait on a load elsewhere that waits on that write.
3. **Rule changes are followed by the job, and a stored time can only make the purge look.** Each run compares every
   form's terms with those its entries were last given their times by (`jfbKeyValue` `jfb.retention.terms.<form>`) and
   walks the entries of a form whose terms differ, 500 at a time by id, from where the last run stopped, writing the times
   that changed in one statement per 300 entries. Entries stored before retention came have no time and are walked the same
   way after an upgrade. Before the purge deletes an entry, it computes its time again by the rules as they are: one whose
   rules got longer and whose walk has not come yet gets its later time and stays. So no entry goes before its rules say,
   and shortened rules reach every entry within a run or a few.
4. **Legal holds** (`EntryHolds`) keep an entry until an end after now and at most 365 days ahead, with a reason of one
   line and at most 500 characters. Setting one replaces the one before; releasing ends it now. Each is a unit with a
   timeline event (`held`, `released`) and an audit entry (`entry.held`, `entry.released`) that name the end, never the
   reason, which only the entry keeps. Only entries retention deletes can be held: a draft or an entry waiting for its
   address is ended by its own link. **A hold keeps the entry from every delete**, not only retention's: a user's delete
   and Empty spam leave it and say how many they left (`held`), and Erase (13.2) does the same. The screens for holds come
   with People (13.2), where the IA puts them.
5. **The purge** (`EntryPurge`) reads up to 200 due entries, soonest first, on the index of the times, without those a
   hold keeps past now. It deletes them through `EntryDeletions`, the same path as a user's delete, in one unit per form
   that holds the form's row and the entries' rows first and reads each entry again: one held, moved or given a later time
   meanwhile stays. With an entry go its answers, files, events, notes, action runs and **all its mail, sent mail too**,
   since a sent mail's copy of the answers should not outlive the entry by up to 30 days. Each unit writes an
   `entry.purged` audit entry per 200 entries that names their numbers, with no user; the files' bytes go after the commit
   and each entry raises `EntryDeletedNotification` as a user's delete does, so webhooks hear of it.
6. **The other parts of the job**, in the order they run after the draft-version cleanup: the purge, the walk of point 3,
   the file store's sweep, the audit log after 365 days and the daily statistics after three years (1,095 days). The job
   stays hourly with its budget of eight seconds; each part stops at the deadline and goes on in the next run.
7. **The file store's sweep** (`FileStoreSweep`) goes through the default store's folders in order and deletes what a dead
   process left there, each once its last write is a day old, so a save or an upload under way is never touched: a save's
   `.partial` file, a deleted file's `.deleted-` bytes, and a stored file no `jfbEntryFile` row names. It keeps how far it
   got in `jfbKeyValue` and starts a new pass a day after the last ended. A name the store never writes is left alone. A
   store of the site's own (`IFormBuilderFileStore`, ADR-0041) keeps its bytes elsewhere and is not swept; the public
   interface gains nothing, as the listing is the default store's own (`IFileStoreLeftovers`). Migration step 14 adds the
   index on `jfbEntryFile.storedName` the sweep looks a folder's names up on.
8. **The dry run** (`RetentionDryRun`) counts, per form, the entries the rules delete within the next 7, 30 or 90 days, due
   ones among them, the files of those entries, and apart from them those a hold keeps past the span. It counts from the rules directly, by the latest
   change of state or submission that reaches each state's time, not from the stored times, so it follows days not yet
   saved at once and needs no walk. Forms with the same terms share one statement. Forms in the trash count, as retention
   deletes their entries too.
9. **Kept for good.** The site may keep a state's entries for good only when the host sets
   `JironFormBuilder:Privacy:AllowRetentionOff`. A state kept for good is stored as 0 days, since a stored section
   leaves its nulls out and a missing day reads as the default. Without the host's leave the rule `RetentionOffRule`
   restricts the four days, a stored 0 reads as that state's default, and a save of one is refused as locked. A form's own days are always 1 to
   36,500; a form takes the site's by leaving a state empty.
10. **The screens.**
    - **Privacy › Retention** (IA 9), for `privacyTools` or `manageSettings`, changed by `manageSettings` only: the four
      days, the forms with their own retention and what each sets, and "What will be deleted" per form with its files for 7,
      30 or 90 days, counting with the days as typed. Where the host does not allow keeping for good, the days box names the
      key that would. Its read and its count accept either permission, so the access declaration gains
      `AnyOf` (the endpoint needs one of its permissions) and the permission matrix checks it. Its save is the privacy
      section's own `PUT settings/privacy`.
    - **Privacy › Data collection** shows the privacy section without the retention box.
    - **The form's Settings view:** its days per state in the Privacy group, each empty for the site's, with the site's
      days in the box; and "For a number of days" among Store entries in the Submission group, with its days under it. Each
      says its range when a number is out of it.
11. **Consent texts stay.** ADR-0009 kept a wording "while an entry points at them". A wording is the site's own text and
    names no visitor (ADR-0014), and finding which wordings no value names would read every value of every entry, as a
    consent value keeps the hash in its JSON. So retention leaves them, and ADR-0009's line now reads "kept: no personal
    data". 13.2's erase test checks that no table holds the person's data after an erase, which wordings never do.

## Consequences

- A site that changes its days sees what will go before it saves, and the change reaches old entries within a run or a
  few; nothing goes before its rules say.
- Retention never deletes an entry a hold keeps, and neither does anyone else; the delete answers say how many stayed.
- Every new place a state changes must store the time through `EntryRetention`; the tests of each path check it, and the
  walk repairs a time a path left behind on the next change of rules.
- The hourly budget limits how fast a large backlog goes; a site with more than about a hundred thousand entries due an
  hour would need a larger budget, which is a host setting to add when one asks.
- The site's own file store sweeps its own leftovers; the docs say so.
- **Revisit** when the job's budget proves too small on large sites, or when a store of the site's own asks for a sweep.
