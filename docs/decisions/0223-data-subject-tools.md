# 0223. Data subject tools: find a person, export everything, erase or anonymise

- Status: accepted; builds on [ADR-0009](0009-privacy-defaults.md) (the defaults and the tools' scope),
  [ADR-0013](0013-permissions.md) (`privacyTools`), [ADR-0090](0090-places-on-options.md) (places),
  [ADR-0091](0091-unique-answers.md) (unique answers), [ADR-0210](0210-entry-exports.md) (background exports) and [ADR-0222](0222-retention.md) (legal holds, deletes)
- Date: 2026-11-09
- Roadmap step: 13.1, its part 13.2 (ADR-0197)

## Context

A site that takes entries must answer a person who asks what it holds about them, wants a copy, or wants it gone
(GDPR Art. 15, 17 and 20). ADR-0009 put the tools in the Privacy group, for `privacyTools` across every form, restricted
ones included: find a person by an email field's value or by member, export everything as a ZIP, and erase (delete) or
anonymise with a preview first, reaching every copy, so that a search of every table for the person's address afterwards
finds nothing. IA 9 draws the People screen; ADR-0222 left the screens for legal holds to it.

Three things shape the design:

- **A person is not a row.** Nothing in the schema names a person. An address sits in the values of email fields, in
  mail that was sent to it, and in the timeline of a forward; a member is a key on the entry.
- **Copies are everywhere.** Besides the values: the entry's search text, IP address, context and page; its events,
  whose edits keep values before and after; its notes; its mail, sent mail included; its action runs' errors; its files,
  in rows and in the file store; and drafts, which are entries too.
- **The tools must not leave a copy of their own.** The audit log, the export's record in `jfbKeyValue` and the requests
  themselves are tables too.

## Decision

1. **Who.** Every endpoint, under `/umbraco/jfb/management/api/v1/people`, needs `privacyTools`, which is global and
   reaches every form, those in the trash and restricted ones included (ADR-0013). The export holds sensitive values in
   full, since it is the person's own copy.
2. **Find** takes an email address or a member's key, never both (`person_invalid`; an address that is not one is
   `email_invalid`):
   - **by address:** the entries with a value of an email field whose folded text (`EntryTextKey`) is the address's;
     the entries of the members whose email is the address, whatever its case (at most 50 members); and the drafts whose
     link to resume was mailed to it;
   - **by member:** the entries whose member key is the member's, and those found by the member's address.
   An entry stores the key of the member signed in when it is sent (ADR-0009 always meant it to); test entries store none.
   The answer lists at most 500 entries, newest first, each with its form, number, when it came, its state, its files
   and mail, and its hold's end; more than that says so. Drafts are listed as drafts. Each search is audited as
   `people.found` with its kind and count.
3. **The person is never stored.** Export, hold and erase send the same address or member again, and the server finds
   the entries again, so no request can reach entries that are not the person's: the entries a hold or an export names
   must be among those found. The audit log's entries stand for the entity `person` with no key, and name the counts
   and the kind of search, never the address; an export's record keeps the entries' keys, not the address.
4. **Export everything** runs in the background as an export of its own kind (ADR-0210): the same record, worker, lease,
   24 hours and ticket for the download, with the entries' keys in place of a form (the record's form is the empty key),
   and no `ExportCompletedNotification`, since it is no form's. The ZIP, `personal-data-<date>.zip`, holds a folder
   per entry, `<form alias>-<number>` (a draft `<form alias>-draft-<n>`), with `entry.json`: the form, the entry's
   number, state, culture, times and page; every answer by page, group and label, with its files, each consent's exact
   wording and a signature's name, and "not kept" for a field that keeps nothing; the timeline's kinds and times; and the
   notes. The entry's files sit beside it; one the scanner holds back stays out, and the JSON says so. Audited as
   `people.exported` with the counts. The People screen follows it and offers the download when it is ready.
5. **Legal holds** for the person's entries come from the People screen, in a sidebar: which entries, the reason on one
   line (at most 500 characters) and the day it ends (tomorrow to a year ahead, stored as the start of that day in UTC
   and shown as that day everywhere), through `EntryHolds` (ADR-0222). Release ends them now. A hold keeps an entry
   from every delete, not just retention's: the panel says it and offers no Delete..., a delete of several and Empty
   spam say how many a hold keeps, the entry's timeline shows it set and released, and a form whose entries a hold
   keeps cannot be deleted for good (`entries_held`), alone or with its folder; emptying the trash leaves it there.
6. **Erase** always shows its preview first, in a dialog with Cancel focused: the entries and drafts per form, their files
   and the mail that goes, and the entries a hold keeps, which it leaves out. It then does one of two things, chosen in
   the dialog:
   - **Delete** (the default): the entries go through `EntryDeletions`, as retention's do, with all their mail, sent
     mail too, audited per form as `entry.erased`. Their places and unique answers come back (ADR-0090, ADR-0091).
   - **Anonymise:** the entry stays with its form, number, state and times, so counts and statistics keep it, and
     everything else about the person goes: every value (so its places and unique answers come back), its files and
     their bytes, its search text, IP address, context, page, member key and resume token, its notes, all its mail, its
     action runs' errors, and the details of its edits and forwards, which keep their kind and time. Its timeline gains
     an `anonymised` event, and the audit log `entry.anonymised`.
   Drafts are always deleted: a draft without its answers is nothing. Beyond the entries, the erase deletes every mail
   addressed to the person, and empties the details of a forward's event that names the address (the event stays, so the
   timeline still says a forward happened). Audited as `people.erased` with the counts and the mode; the screen says
   what went, leaving out what there was none of, and links to the audit log for a user who may read it.
7. **Statistics stay.** The daily statistics count without naming anyone (ADR-0009), and the audit log's own entries
   name no person.

## Consequences

- A privacy officer answers an access, portability or erasure request from one screen, and the audit log shows that it
  happened without keeping who it was about.
- Entries found by member need the member key, which entries sent before this step lack; they are found by address.
- A hold now blocks more than retention: a form cannot be deleted for good while it keeps entries. That is what a
  hold is for, and the refusal says why and that it lifts when the holds end.
- An erase reaches the copies Jiron Form Builder keeps. Mail already delivered, files a user downloaded and exports made
  earlier are outside it; the privacy documentation (13.4) says so.
- Every new table or column that can hold personal data joins the erase test (ADR-0009).
