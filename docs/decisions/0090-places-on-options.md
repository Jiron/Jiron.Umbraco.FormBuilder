# 0090. Places on options

- Status: accepted; amends section 4.4 and section 4.5 (how a place is taken and given back, what `jfbOptionCapacity`
  counts) and [ADR-0088](0088-the-submission-pipeline-and-the-entries-endpoint.md) (the transaction's last check);
  extends [ADR-0012](0012-anonymous-endpoints-spam-protection-and-challenges.md) (what a page's validation checks) and
  [ADR-0033](0033-schema-v1.md) (the capacity table's rows)
- Date: 2026-10-28
- Roadmap step: 6.1g (decided and built); 6.1h checks `uniquePerForm` under the same lock, 7.1 answers the places left
  with the challenge and keeps spam out of them, 8.6 takes a resumed draft's places when it is submitted, 11.2 moves an
  entry back and edits its values asking before overbooking, 13.2 anonymises entries

## Context

An option of a dropdown, radio buttons or checkboxes can have a number of places (section 4.4): a workshop with 20
seats, a time slot for one. Section 4.4 says the submission takes a place on each chosen option with a limit, so that
two servers can never overbook, that spam and test entries take none, and that rejecting, marking as spam, deleting and
erasing an entry give its places back, while moving it back takes them again. Section 4.5 planned `jfbOptionCapacity`
as one counter per option, raised by one atomic `UPDATE … SET Taken = Taken + 1 WHERE … AND Taken < @limit`.

Open were: what exactly an entry holds, so that giving it back returns what it took; where the limit of an option from
a library list or another source comes from, since a list changes without a publish; what a limit set after entries
came in counts; how a repeatable group's rows and a field's several choices count; how two writers at once stay exact
on SQL Server, whatever its isolation level; what the visitor is told and when; and what `validate` checks.

## Options

1. **The plan of section 4.5: count up at submission, count down from the entry.** The atomic update is exact for
   taking. Giving back has to know what the entry took, and the entry's values say only what it chose: an option of a
   list that gained places after the entry came in, or a field whose limit was set later, would be counted down for a
   place it never took, and the option would overbook. A limit set after entries came in starts from nothing.
2. **A record of the places each entry took** (a table of entry, field and option). Giving back is exact, but it needs
   the first schema step after the freeze for a copy of what the values already say, and a limit set after entries came
   in still starts from nothing, which is not what an editor means by "20 places".
3. **No counter: count the values each time.** Always exact, and a limit set later counts every entry. The check and
   the insert must then hold a range of values against other writers on SQL Server, and the count grows with the
   entries.
4. **The counter as a kept count of the values.** The row of an option holds the number of the form's values at the
   field that equal the option's value and whose entry holds places. It is made with that count when it is first
   needed, and every write that changes such a value, or whether its entry holds places, changes it in the same
   transaction. Exact like option 3, as cheap to read as option 1, and needs no new table. Chosen.

## Decision

### What a place is

- **An entry holds places** while it counts toward the form's entry limit (ADR-0088): `unverified`, `submitted` or
  `approved`, and not a test entry. Drafts, spam and rejected entries hold none.
- **A place** is one value of such an entry, at the field, whose text is the option's value exactly, as the choice
  types compare an answer with their options. Each row of a repeatable group that chooses the option takes one, and so
  does each chosen option of a field with several choices. An answer typed into "Other" is no option and takes none.
- **The limit** is the option's `places`: a field's own option's in the live version, or for an option of a library
  list or another source, the `places` its source gives as it resolves the options for the submission. Only types with
  places have limits (dropdown, radio buttons, checkboxes), so a source's places on another type's options mean
  nothing. `0`, or a negative number from code, leaves no place.

### The counter

- **`jfbOptionCapacity.taken`** is the number of places held on the option, as above. A row exists only for an option
  that has needed one, and it is made, with `INSERT … SELECT … WHERE NOT EXISTS`, holding the count of the places held
  at that moment. A limit set after entries came in therefore counts them: 20 places with 5 entries already in leave
  15. A row whose option loses its limit is kept and goes on counting, so the count is right when a limit returns. Rows
  go with their form (the cascade of ADR-0033).
- **Every write keeps the count:** the repository changes the rows of the values concerned in the same transaction as
  the write, with one statement over the entry's values (the text compared in a binary collation with the row's
  `optionValue`, as the key compares exactly, ADR-0033):
  - storing an entry that holds places adds its values;
  - a state change from a state that holds places to one that does not takes them away, and the way back adds them
    again;
  - replacing an entry's values takes the old ones away and adds the new ones;
  - deleting entries takes away the values of those that hold places.

  13.2's anonymising replaces values, so an anonymised entry gives its places back too. The repository never refuses
  a change for a full option: whether to overbook is the caller's question (below).
- **One transaction at a time per form.** Each of those writes locks the form's row first: a submission through its
  entry counter (ADR-0038), the others with an update of the counter to its own value. A form's places therefore
  change one transaction at a time on SQL Server, whatever its isolation level, and on SQLite, whose gate serialises
  every writer anyway. A count made inside such a transaction sees every change before it and none after.

### The submission

1. **While the answers are read** (`entries` and `validate`), the reader names each option with a limit that a shown
   field's answer chose, with its row. The pipeline reads the options' counts (a row's `taken`, or the count of the
   places held when no row is made yet) and fails each choice past the places with `full` at its field and row: the
   first choices in the order of the form and its rows get the places left, the rest fail. The `full` errors join the
   others in that order, so a visitor sees every problem at once and keeps every answer.
2. **Inside the transaction**, once the entry is stored and its values counted, the pipeline makes the rows it needs
   and reads their counts under the form's lock. A count above the limit fails the choices past the places with
   `full`, and the transaction rolls back: no entry, no number, no event, no count. The answer is
   `422 validation_failed` with those errors, as for the first check; the visitor can choose another option.
3. **A retry** of a stored submission is answered from its entry before any of this (section 4.4, step 3), so the
   same answers get the same receipt once the option is full.

`validate` runs step 1 and stores nothing: places left are no secret (the challenge of 7.1 answers them, ADR-0012), and
a visitor learns on the page they are on that an option is full.

### What other steps do with it

- **Spam** (7.1): an entry the protection puts into Spam holds no place, and the checks above do not run for it, so a
  bot learns nothing from `full`.
- **Moving an entry back, editing a choice** (11.2): the backoffice reads the counts, asks before it overbooks an option
  that is full by then, and the repository counts the entry again.
- **A resumed draft** (8.6) holds no places until it is submitted, which takes them as a submission does.
- **The places left** for the runtime (7.1) are the limits minus the counts the pipeline reads in step 1.

## Consequences

- The counts are exact by construction: no job repairs them, and a test compares each one with the values after
  parallel submissions, state changes, edits and deletes.
- Section 4.4 says the place is taken inside the transaction that holds the form's row, rather than by one atomic
  statement; section 4.5 says what `taken` counts and when a row is made. Two servers still never overbook.
- A state change, an edit and a delete of entries now wait for a submission to the same form, and the other way round;
  they are short, and the entry limit becomes exact against them too.
- An option renamed after entries chose it starts its own count, as the entries keep the value they stored.
- The entries' values hold what a place is, so the schema stays at v1.
