# 0039. The entries query builder

- Status: accepted; amended in 4.8g and 4.8i (units inside a budgeted read)
- Date: 2026-10-24
- Roadmap step: 4.3d (decided and built); 4.7 (the cursor and `query_timeout` on the wire), 6.x (the search text),
  11.1 (the entries list), 11.3 (the inbox), 11.4 (exports), 16.2 (the load test)

## Context

ADR-0003 fixed the query shapes the entries screen may use, measured at 100,000 entries on SQLite: field filters as
`IN` subqueries, keyset pages, a sort by a value that walks the value index, a search in one pass, and a time budget
per statement. ADR-0038 stored the values with a text key that compares as rules do, cut to 256 code units, which SQL
Server's case-insensitive collation can still equate where rules do not (`ß` and `ss`). Step 4.3d turns typed filters
into those shapes. Open questions:

- **Which filters.** Rules know `contains`, `endsWith` and `matches`, but the key holds only the first 256 folded
  code units of a text, and nothing in SQL folds or matches as the shared logic does.
- **Long texts.** An equality filter on a text past the key's length must compare the whole text, folded.
- **Several answers.** A multi-choice field or a repeater has several values per entry; a sort walks values, not
  entries.
- **Keyset positions on SQL Server.** `DATETIME` rounds to 1/300 s, and nothing may compare a stored time with a
  parameter for equality (ADR-0003).
- **The budget on SQLite.** Microsoft.Data.Sqlite does not cancel a running statement, and Core references no
  database client (ADR-0036).

## Options

**Filters:**

1. **Every rule operator.** `contains` and `endsWith` on a cut key miss what lies past 256 units, and `matches` needs
   the shared matcher. Rejected.
2. **What the key answers exactly,** plus the search for words anywhere. Chosen: per field, a text is one of or none
   of some values, starts with a prefix shorter than the key, a number or a date lies in a range, a checkbox is
   checked or not, and any field is answered or not. Words inside a text are the search's job, over the search text
   of the whole entry.

**Long texts in an equality filter:**

1. **Compare the stored text exactly.** Case and normalisation would then matter past 256 units. Rejected.
2. **Read the distinct texts that share the key, fold them in C#, and filter on those that match,** in the same read
   and under the same budget. Chosen. The texts that match are the case and normalisation variants of one text, so
   the list stays short; the statement's parameters are counted, and a filter past 2,000 is refused.

**Several answers in a sort:**

1. **Sort by the answer at row 0 and item 0.** A repeater whose first row leaves the field empty would drop the
   entry from both the walk and the tail. Rejected.
2. **Every entry once, at its first answer:** the walk keeps a value only when no value of the same field and entry
   with the sort column set comes earlier by row and then by item, which one seek on `(entryId, fieldId)` decides
   for each value the walk reads. Chosen. The tail holds the entries with no value in the sort column.

**Keyset positions:**

1. **The last row's values as parameters.** A `DATETIME` read back and sent again may not compare equal. Rejected.
2. **The last row's id, with its values read back in the statement** (`COALESCE((SELECT … WHERE id = @id),
   @fallback)`), so the comparison is column against column, exact on both databases. Chosen. The fallback is the
   value the cursor saw, for a row deleted meanwhile.

**The budget on SQLite:**

1. **A command timeout or a cancellation token.** Microsoft.Data.Sqlite checks neither while a statement runs.
2. **SQLite's progress handler,** set on the connection the unit runs on and cleared before the unit ends, as the
   1.3 spike did. Core reaches it through reflection by type name, as it already recognises database errors, so it
   brings no database client to a site. Chosen. On SQL Server the unit's cancellation token is cancelled at the
   deadline, which stops the running command.

## Decision

**`IEntryQueryRepository.ListAsync(query, after, take, count)`** reads one page of one form's entries in one budgeted
read (`FormBuilderDatabase.QueryAsync`). The query (`EntryQuery`) holds:

- the form, the states (none means every state) and the test entries (left out, included, or only them);
- a submission range, from (inclusive) to before (exclusive), in UTC;
- up to 10 field filters (`EntryFieldFilter`), each naming a field of the form's catalogue:
  - `EntryTextFilter`: one of, or none of, up to 50 values, or starting with a prefix of 1 to 254 folded units;
  - `EntryNumberFilter` and `EntryDateFilter`: a range, open at either end, numbers as decimals stored as doubles
    (ADR-0003), dates half-open;
  - `EntryCheckedFilter` and `EntryAnsweredFilter`;
- a search term, and the sort: by submission, newest or oldest first, or by a field's text key, number or date.

**How a filter compares:**

- A text value compares by its key (`EntryTextKey`): the seek uses the column's collation, and a `LIKE` without
  wildcards in a binary collation then keeps only the keys equal by their code units, which keeps apart what rules
  keep apart. Unlike `=` and `IN`, `LIKE` pads nothing on SQL Server, where a cut key can end in spaces that a
  shorter key would otherwise equal. A key that may be cut compares the full texts as above. On SQLite the keys are
  folded, so `NOCASE` and the binary comparison agree.
- A prefix is one `LIKE` on SQLite, which seeks the key on its `NOCASE` index. SQLite's `LIKE` ignores collations
  and folds ASCII letters only, which folded keys no longer differ by, so it decides by the code units. On SQL
  Server only the binary `LIKE` decides, over the field's values: a database collation may read letters as one
  (Danish `aa`, Czech `ch`) and miss a key that starts with the prefix.
- A number bound is the double its digits parse to, as a submitted number with the same digits is stored; a
  conversion from `decimal` can miss by one unit in the last place.
- "None of" and "not checked" are the exact opposite of "one of" and "checked": `NOT IN` the same subquery, so an
  entry without the field counts as not matching.
- An entry matches a filter when any of its answers does (rules' `isOneOf`), for every kind.
- A search term is cleaned and folded as rules compare text (`EntrySearchText`), escaped for `LIKE`, and compared with
  the entry's search text, in a binary collation on SQL Server. The submission pipeline writes the search text with
  the same function (6.x).

**The shapes,** and only these (ADR-0003):

- **By submission:** `(submittedUtc, id)` along `(formId, submittedUtc)` or `(formId, state, submittedUtc)`; the
  next page goes on from the last row's id.
- **By a value:** the walk `FROM jfbEntryValue v CROSS JOIN jfbEntry e WHERE e.id = +v.entryId AND v.fieldId = @f`,
  ordered by the value and the entry id, which the value index already holds in order. The unary plus keeps the
  walk on that index: with planner statistics, SQLite would otherwise reach the values through the entry ids of a
  filter and sort them in a temporary b-tree; a test shows that this happens without it. `CROSS JOIN` is SQLite's
  documented way to keep the values the outer loop. With the plus, no statistics the tests could produce make SQLite
  choose another order, so the cross join only guards against a later planner's choice. Once the walk runs out, the
  tail holds the entries without a value in that column, newest first. A page may hold the end of the walk and the
  start of the tail.
- **Counts:** exact without a search; with a search, up to 1,000 and then "more than 1,000". The first page of a
  search by newest first reads its rows and its count in one pass of the newest 1,001 matches.
- **The rows:** the shapes read ids and positions only; the page's entries load by id in a second statement.

**The budget** is 10 seconds per read unit. Past it the statement is interrupted, the read ends without a result,
and `FormBuilderQueryTimeoutException` (`query_timeout`, 4.7) says so. It is never run again, and the gate is free
for the next unit at once. A refusal of the builder stays a refusal, also past the deadline. A budgeted read
inside another one keeps the outer one's budget and adds its own caller's cancellation (4.8g); a read stopped through
its caller's token or a unit's ends with `OperationCanceledException`, never with the provider's error. Since 4.8i
this holds for a plain read inside a budgeted one too, and SQLite's progress handler stops a statement once the unit
that runs it is cancelled: the budgeted read's caller or deadline, or the caller of a unit inside it. The SQLite
progress handler is cleared before the connection goes back to Umbraco.

**The cursor** (`EntryCursor`) names the sort it belongs to, the last entry, its submission time and, in the walk, its
value row and value. A cursor of another sort is refused. 4.7 carries it as an opaque token: it names a position,
never an entry the caller may not see, because every page still filters by the form. Its rows are read back only
as a row of the form and a value of the sort's field, so a cursor never places a page by another form's data, and
once (`IFNULL` on SQLite, `ISNULL` on SQL Server, whose `COALESCE` runs a subquery twice and can see a row that
went away in between as null).

## Consequences

- 11.1 offers these filter kinds and the search, and the sorts by text, number and date fields. 11.3's inbox reads
  across forms and needs a shape of its own, measured in 16.2.
- 6.x writes `searchText` with `EntrySearchText.Normalize` over the non-sensitive answers.
- Plans are tested on SQLite with and without planner statistics: no `SCAN` of `jfbEntryValue`, and no temporary
  b-tree for a sort by a value. On SQL Server the CI job runs the same correctness tests; the optimizer picks its own
  order there (research note, section 10).
- A query that names a field of another form matches no entries, because every shape also filters by the form.
- **Revisit** when 16.2 measures the shapes past the §4.12 budgets, or when an editor needs words inside one field
  (then a per-field search text).
