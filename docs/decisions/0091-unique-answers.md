# 0091. Unique answers

- Status: accepted; amends section 4.4 (when a unique answer is checked and held) and
  [ADR-0002](0002-form-definition-model-versions-and-public-projection.md) (which field types may have a
  `uniquePerForm` check); extends [ADR-0088](0088-the-submission-pipeline-and-the-entries-endpoint.md) and
  [ADR-0090](0090-places-on-options.md) (the transaction's last check holds unique answers beside places)
- Date: 2026-10-28
- Roadmap step: 6.1h (decided and built); 7.1 keeps spam out of it, 8.6 checks a resumed draft when it is submitted,
  11.2 moves an entry back and edits its values asking before an answer appears twice, 11.3's "not spam" asks the same
  way, 13.2 anonymises entries

## Context

A field's `validation` list may hold a `uniquePerForm` check (ADR-0002): one registration per email address, one
entry per membership number. ADR-0012 keeps it out of a page's validation, so that `validate` cannot be used to ask
whether an address is registered, and ADR-0088 left the check itself for this step.

Open were: whose answers an answer is held against; what "the same answer" is for a text, a number and a date; how a
repeatable group's child and a field of several values compare; how two submissions at once cannot both pass on SQL
Server, whatever its isolation level; which field types may have the check; and what the visitor is told.

## Options

1. **A unique index over the answer** (a table of form, field and key). The database refuses the second insert by
   itself. But whose answers count changes with an entry's state (rejected, spam, moved back), so the table has to
   follow every write as the places do; it needs a schema step after the freeze for a copy of what the values already
   hold; and a refusal by an index is the database's error, not a field's (ADR-0036 keeps refusals in a statement's own
   condition).
2. **One check before the transaction.** Simple, and two submissions at once both pass.
3. **A check while the answers are read and again inside the transaction, under the form's row**, as the places are
   held (ADR-0090). Every write that changes whose answers count already locks the form's row first, so a check inside
   such a transaction sees every change before it and none after. Exact on both databases, and the values and their
   keys are in `jfbEntryValue` already. Chosen.

## Decision

### What an answer is held against

- **The entries that hold their answers** are those that hold places (ADR-0090): the entries that count toward the
  entry limit, `unverified`, `submitted` or `approved`, and not test entries. Drafts, spam and rejected entries hold
  none. A deleted entry holds none, and so does a value an edit or an erasure replaced; 13.2's anonymising replaces
  values too. A field kept only until the actions have run holds its answer while it is kept.
- **The same answer** is the same as the entries screen filters it (ADR-0038):
  - a number or a date that its column holds compares by that column, so `12` and `12.0` are the same number;
  - any other value compares by its text key, cleaned and folded as rules compare text (ADR-0005), so letter case and
    surrounding spaces make no difference. The key is sought on the value index and compared again in a binary
    collation with a `LIKE` whose wildcards are escaped, so `50%` matches only itself;
  - a key that may have been cut (a long text past the key's length) stands for every text that starts with it, so
    the stored texts with that key are compared whole.
- **At the same field:** the field's key decides, so the answers of other fields and other forms do not count. A
  repeatable group's child is held against every row of the other entries. An entry may repeat its own answer in two
  rows: each value is held against the other entries only.
- **Which types:** only a type that supports the check (`FieldFeatures.UniquePerFormCheck`) runs it, as publishing
  already requires. The registry also refuses a type that supports it but stores anything but text, long text, a
  number or a date: yes or no, parts and files have no value that compares this way.

### The submission

1. **While the answers are read** (`entries`), the reader names each shown answer of a field with the check that
   passed the field's other checks, with its row and values. The pipeline reads, without locks, which of the values
   another entry holds, and fails each `uniquePerForm` check of such an answer with `check` and the check's index at
   its field and row. These errors join the others in the order of the form, so a visitor sees every problem at once
   and keeps every answer.
2. **Inside the transaction**, once the entry is stored, for an entry that holds its answers: the pipeline locks the
   form's row (the entry counter holds it already), then reads with locking reads which of the values another entry
   holds. Any such value rolls the transaction back: no entry, no number, no event, no count. The answer is
   `422 validation_failed` with these errors and the choices past the places of ADR-0090, in the order of the form.
3. **A retry** of a stored submission is answered from its entry before any of this (section 4.4, step 3), so the entry
   that holds the answer gets its receipt again.
4. **`validate`** does not check uniqueness (ADR-0012): it passes an answer another entry holds, and every other check
   still runs.

### What other steps do with it

- **Spam** (7.1): a submission the protection puts into Spam holds no unique answer, and neither check runs for it, so
  a bot learns nothing about which answers are taken.
- **Moving an entry back, editing an answer** (11.2, and "not spam" in 11.3): the backoffice asks before an answer
  that must be unique appears twice; the repository refuses nothing, as for places.
- **A resumed draft** (8.6) holds no unique answer until it is submitted, and the submit checks its answers as a
  submission's are checked.
- **A test entry** is checked while its answers are read, so an editor sees the error a visitor would, and holds
  nothing.

## Consequences

- Two servers never store the same unique answer twice for a form; a test sends ten at once.
- Each check costs one statement per value, on the value index of its column; forms have few unique fields.
- A submission and a write of entries to the same form already wait for each other (ADR-0090); the unique check adds
  no lock of its own.
- An answer is unique as the entries screen finds it, so an editor's filter for the answer finds the entry that holds
  it.
- The schema stays at v1.
