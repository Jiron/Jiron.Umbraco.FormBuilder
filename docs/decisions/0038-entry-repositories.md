# 0038. The entry repositories

- Status: accepted; makes every guard of [ADR-0036](0036-database-gate-units-of-work-and-repositories.md) a locking read
- Date: 2026-10-24
- Roadmap step: 4.3c (decided and built); 4.3d (the entries query builder), 6.x (the submission pipeline), 10.x
  (the entry screens), 13.x (retention and privacy requests)

## Context

ADR-0003 designed the entry tables and ADR-0036 the pattern every repository follows. Step 4.3c adds the
repositories for entries, their values, files, consent wordings, timeline events and notes. Those tables raise
questions the form and library repositories did not:

- **Numbers.** Each form numbers its entries 1, 2, 3, … (§4.4). Two submissions at the same moment must never share a
  number, on two servers either.
- **One insert, many reasons to refuse it.** A submission id the form already has, a challenge that already made an
  entry, a key or a resume token another entry has, a version of another form.
- **Values.** One row per answer, item and repeater row, in the typed column of the field's storage kind (5.1), and a
  text key for filters and sorts that compares as rules do (ADR-0005).
- **Consent wordings.** ADR-0009 names a wording by "the SHA-256 of its culture and text", but not how the two are
  put together, and the form must post the same hash the server computes.
- **Edits.** An entry has no concurrency version in schema v1, and SQL Server's `DATETIME` rounds times to 1/300 s.
- **New rows' ids.** Events and notes have no key of their own, and on SQL Server another server may add a row for the
  same entry at the same moment.
- **Writers at the same moment on SQL Server.** Under read-committed snapshot, a check reads a row's last committed
  version. A guard can therefore miss a row another unit is writing, and the foreign key then refuses with a raw
  database error instead of the repository's refusal.
- **Statement sizes.** A page of entries, a long repeater and a bulk change all name many ids; SQL Server takes at
  most 2,100 parameters.

## Options

**Numbering:**

1. **Read the highest number, then insert.** Two units read the same number; the unique index refuses one of them,
   which then fails. Rejected (ADR-0003).
2. **Raise the form's counter as the first write of the unit,** and take the number from it in the insert. The update
   holds the form's row until the unit ends, so the next unit waits and reads the raised number. Chosen; the unique
   index on (form, number) stays as the backstop.

**Drafts and test entries:**

1. **A number only once submitted.** `number` is not null and unique per form in schema v1, so a draft would need a
   placeholder. Rejected.
2. **Every entry takes its number when it is stored,** drafts and test entries included. Chosen. Numbers of deleted
   entries and drafts leave gaps, and `#1042` still names one entry for good.

**The consent hash:**

1. **The caller passes the hash.** A row could then be stored under a hash that does not name its text. Rejected.
2. **Culture and text joined by a separator character.** Ambiguous as soon as the culture can contain it.
3. **The SHA-256 of the culture's UTF-8 bytes, one zero byte and the text's UTF-8 bytes,** computed by
   `ConsentTextHash`, which the repository and later the renderer call. A culture with a NUL and text with a lone
   surrogate are refused, so no two pairs share the bytes they hash. Chosen.

**Edits of values:**

1. **`updatedUtc` as a version token.** SQL Server rounds the stored time, so a token read back does not reliably
   match the one written. Rejected.
2. **A version column on `jfbEntry`.** A migration for a need that 10.x has not designed yet.
3. **The last writer wins, field by field,** and the timeline events the service writes with each change say who
   changed what. Chosen for now; see "Revisit".

**The ids of new events and notes:**

1. **Insert, then read the entry's newest row.** On SQL Server another server's insert can land in between, and the
   read returns the wrong row. Rejected.
2. **Hold the entry's row, then insert** through NPoco, which reads the new id from the database's own identity
   function, then read the row back by that id. Chosen. The hold is the unit's first write, a no-op update of the
   entry, so it also answers "does the entry exist?".

**Guards against a writer at the same moment:**

1. **Leave it to the foreign keys.** The data stays whole, but the caller gets a raw database error instead of a
   refusal it can answer. Rejected.
2. **A locking read for every check whose answer must hold until the unit ends** (`SqlNames.LockingRead`, the same
   `WITH (UPDLOCK, HOLDLOCK)` as the insert-once checks). The check then waits for the other writer, so the
   statement's own condition refuses. Chosen. On SQLite the gate already serialises writers.

## Decision

**Storing an entry** (`IEntryRepository.CreateAsync`) is one unit of work:

1. The form's counter is raised, the first write of the unit; no form, no entry (null). It becomes the greater of
   itself and the highest number the form's entries hold, plus one, so a counter set back by hand or by a restore
   catches up instead of failing every later submission on the unique index. The counter's update leaves the form's
   concurrency version alone: a submission is not an edit of the form, and an editor's save is never refused
   because a visitor sent it.
2. One `INSERT … SELECT … WHERE` takes the number from the counter. Its conditions:
   - the version belongs to the form (`MissingVersion`), read with a locking read;
   - no entry has the submission id in this form (`DuplicateSubmission`);
   - no entry has the challenge in this form (`ChallengeUsed`);
   - no entry has the key or the resume token (`DuplicateKey`).

   Each "no entry has" is a `NOT EXISTS` of its own, so each is a seek on its unique index; the filtered ones add
   `IS NOT NULL`, so SQL Server can use their indexes. The unique indexes are the backstop. Their refusal is
   classified the same way, with the database's error inside.
3. The entry takes its files: uploads of its form that no entry took yet (`MissingFile` otherwise).
4. Every value names a field of the form's catalogue (`MissingField`), checked with a locking read.
5. The values go in, 100 rows to an insert, which keeps each statement at 1,000 parameters.

A refusal anywhere rolls the unit back, the number included. A service that stores more with the entry (the consent
wordings, the timeline event, the action runs, §4.4) runs it all in one unit and calls `CreateAsync` first, so the
counter stays the unit's first write.

**Values:**

- A value fills at most one typed column (number, date, bool or file), and has text when it fills none. A number is
  finite, a date is UTC, and no two values share a field, row and item.
- `textKey` is set for values that are text only: `TextSemantics.Fold(TextSemantics.Clean(text))`, exactly as rules
  compare text. It is cut to the column's 256 code units, never between the two halves of a surrogate pair, and
  `EntryTextKey.MayBeCut` tells the query builder (4.3d) when to compare the full text too.
- **The collation:** on SQLite the column compares `NOCASE`, which folds ASCII only, so equal keys are equal texts.
  On SQL Server the database's case-insensitive collation can treat keys as equal that rules keep apart (`ß` and
  `ss`, ADR-0033). So the query builder (4.3d) seeks with the column's collation and then compares the key again in a
  binary collation.
- Values load by `entryId IN (…)`, at most 1,000 ids to a statement, by entry, field, row and item.
- `ReplaceValuesAsync` replaces the values of the fields it names, with the search text, in one unit. A file value
  names one of the entry's own files, checked with a locking read. A file that a replaced value named stays the
  entry's file until the service deletes it.

**States** change only from the state the user saw, in one conditional update that stamps when and by whom. When the
update changes nothing, the entry was not in that state at that moment, whatever a later read shows: the state may
have come back since. So that always throws `FormBuilderConcurrencyException`. A move to the state the entry is in
writes nothing. A file's scan state works the same way.

**Deleting an entry** is one `DELETE`: its values, files, events, notes and action runs go with it through the
database's cascades. Queued emails keep their row with the link set to null, so the service deletes them first
(§4.4). The file store's bytes are the service's to delete (ADR-0006).

**Files** are recorded with an insert-once on the key, for a form that exists, and wait without an entry. Orphans
list oldest first, up to 1,000 at a time, for the cleanup (ADR-0006). A file's row is deleted only:

- from where it was seen: the entry the caller names, or no entry for the cleanup, so an orphan that an entry took
  meanwhile throws `FormBuilderConcurrencyException`;
- while no value names it (`InUse`), checked with a locking read.

**Consent wordings** are stored once under `ConsentTextHash.For(culture, text)`, with an insert-once; the first time
a wording was seen stays. A unique-key refusal from a writer at the same moment means the same wording is stored, so
it is the answer, not an error.

**Events** are append-only: a correction or an undo is a new event (ADR-0023). A kind is 1 to 64 ASCII letters,
digits, dots or hyphens, starting with a letter, and details are a JSON object. They are read per entry by when they
happened and then by id (a server whose clock is behind still lands where it belongs), and per change.

**Notes** are added, read, changed and deleted; a note holds 1 to 10,000 characters, not only white space. They are
read by when they were written. Who may change or delete a note is the entry service's (10.x).

**Locking reads** guard every check whose answer must hold until the unit ends, in every repository:

- a row that must stay: the version, fields and files an entry names, the form a file or a catalogue field is added
  to, the folder a form is created in or moved to, and the live folder a restored form or folder goes back to;
- rows that must not appear: the entries that keep a form or a draft version from being deleted, the values that keep
  a catalogue field or a file, the forms that point at a draft version, and the folders and forms that keep a folder.

Each check finds its rows through an index or a key, so the lock holds those rows and not the table; the check of a
draft version therefore reads its own form by id. The one check without a lock is the theme in use, a name without a
foreign key whose race ADR-0037 accepts. A unit test reads the repositories' source and fails on an `EXISTS` check
or a count of named ids without the hint, because on SQLite the hints are empty and no database test here can tell.

**What the columns hold** is checked before the database:

- the digest and the resume token hash are 64 lowercase hex characters, and a challenge id is 32;
- a file's stored name is 32 lowercase hex characters, and its SHA-256 64;
- the page is a site path of 1 to 2,048 characters (ADR-0009): one slash first, not two or a slash and a backslash
  (which browsers read as another host), and no query or fragment;
- the context and an event's details are JSON objects;
- the culture, address, spam reasons, names, content type and scan state fit their columns.

## Consequences

- The submission pipeline (6.x) answers a `DuplicateSubmission` by reading the first entry with
  `GetBySubmissionAsync` and comparing digests (§4.4), and maps `ChallengeUsed` to its own answer. `MissingVersion`,
  `MissingField` and `MissingFile` mean the pipeline's own checks missed something, and fail the submission.
- Two submissions to one form queue on the form's row for the length of a submission's unit; other forms are not
  held up.
- Adding an event or a note holds the entry's row for the length of its unit, which also keeps an entry from being
  deleted under it.
- On SQL Server a submission and a publish of the same form wait for each other on the catalogue's rows, uploads of
  one form on the form's row, and the orphan cleanup and a submission on the files they share. All are short, and a
  unit that loses a deadlock runs again (ADR-0036).
- The renderer (6.x) computes the consent hash it posts with `ConsentTextHash`, so the two never drift.
- **Revisit** when 10.x designs editing an entry by several people at once: it may need a version column on
  `jfbEntry`, through a new migration step.
