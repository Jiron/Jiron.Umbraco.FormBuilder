# 0003. Entry storage

- Status: accepted; catalogue rows of draft-only fields amended in 1.8
- Date: 2026-10-23
- Roadmap step: 1.3 (decided); built in 4.1 (schema), 4.3 (repositories and the entries query builder), 6.1 (the
  submission transaction), 11.1 (the entries list) and 11.4 (exports); load-tested in 16.2

## Context

D5 makes entries relational: one row per entry and one per value, with typed columns, so that the entries screen
filters and sorts in SQL. §4.5 sketched the tables and their indexes. §4.12 sets the budgets on SQLite, each at a
stated concurrency: the first page of a form with 100,000 entries in ≤ 300 ms, a 10,000-entry CSV export in ≤ 5 s,
and a submission (store plus outbox) in ≤ 150 ms at p95. D7 fixes the path: NPoco DTOs, a `PackageMigrationPlan` of
idempotent `AsyncMigrationBase` steps, one async gate.

Step 1.3 built the tables in the sandbox, seeded 100,000 entries with 12 values each and measured every query on
SQLite ([research](../research/spike-entries.md)). What it found that changes the sketch:

- **Umbraco maps `decimal` to `TEXT` on SQLite,** so number filters and sorts compare text: `-0.001` sorts before
  `-10.0`, and a range `-3..10` finds 7 of 12 values. A custom `DECIMAL(38,10)` type is not passed through
  (`TEXT COLLATE NOCASE` on SQLite, a one-character `NVARCHAR` on SQL Server). A `double` maps to `REAL` and
  `FLOAT`, and is exact for every decimal of up to 15 significant digits (0 of 600,000 random values changed).
- **The sketched indexes allow runaway plans.** A text filter written as `EXISTS` with the `(FieldKey, TextKey)`
  index ran for more than 30 minutes on 100,000 entries, and a sort by a value through `(EntryId)` for more than 10.
  Both held SQLite's single writer, so Umbraco's own writes stalled behind them.
- **"All states" by date had no index:** 129 ms for the first page, 3.3 s for the export.
- **The field key costs more than half of the storage.** A 36-character GUID in every value row and in four indexes:
  the values took 425 MB at 100,000 entries, against 185 MB with an integer field id.
- **Search as a list plus a count scans twice:** 68 to 228 ms, with a p95 of up to 360 ms.
- **Submissions queue on SQLite.** With 20 arriving at once the p95 reached 141 to 184 ms; with 10, 59 to 115 ms.

## Options

1. **The §4.5 sketch as written:** a `decimal` number, two-column value indexes, the field key in every row, queries
   as they come. Wrong number order on SQLite, and plans that run for minutes. Rejected.
2. **Typed relational values with designed indexes and query shapes:** a `double` number, covering value indexes
   that end in `EntryId`, an `(EntryId, FieldId)` join index, a `(FormId, SubmittedUtc)` index, an integer field id,
   and a fixed set of query shapes (below). Meets every budget, measured.
3. **Extra columns on `jfbEntry`** (computed or copied sort and filter values per field). Every form has its own
   fields, so the columns would have to be generic slots (`Sort1`, `Sort2`, …) filled per form: a second copy to keep
   in step, a limit on the number of sortable fields, and nothing option 2 needs. Rejected.
4. **SQLite full-text search (FTS5) for `SearchText`.** The one-pass `LIKE` scan of option 2 takes 42 to 115 ms at
   100,000 entries. FTS5 has no SQL Server counterpart that every host offers, and it would be a second index to
   keep in step. Not now; revisit when 16.2 shows search past its budget.

## Decision

Option 2.

- **Columns:**
  - `jfbEntryValue.Number` is a `double` (`REAL` on SQLite, `FLOAT` on SQL Server). `Text` keeps the value exactly as
    submitted, for display and export. Number fields accept at most 15 significant digits, so every stored number
    filters and sorts exactly.
  - `Date` stays a `DateTime`, always written as a UTC `DateTime` parameter, never as a string, a `DateTimeOffset` or
    a SQL date function. SQLite then stores one text format, which sorts correctly. Values read back are given
    `DateTimeKind.Utc`.
  - **`jfbEntryValue.FieldId`** refers to the field's row in the field catalogue `jfbFormField`, with no cascade
    (SQL Server refuses a second cascade path, and the form service deletes entries before the form). A field gets
    its catalogue row before its first value is stored: on publish for live fields, and on the first preview
    submission for a field that exists only in the draft. Such a row has no `FirstVersionNumber` until a publish
    includes the field, and the columns, filters and exports of real entries leave it out. The small tables that name a field (`jfbEntryFile`,
    `jfbOptionCapacity`) keep `FieldKey`.
- **Indexes:**
  - `jfbEntryValue`: `(EntryId, FieldId)`, `(FieldId, TextKey, EntryId)`, `(FieldId, Number, EntryId)` and
    `(FieldId, Date, EntryId)`.
  - `jfbEntry`: `(FormId, SubmittedUtc)` in addition to §4.5's `(FormId, State, SubmittedUtc)`.
- **Query shapes,** the only ones the entries query builder (4.3) writes:
  - a field filter is `e.Id IN (SELECT v.EntryId FROM jfbEntryValue v WHERE v.FieldId = @f AND <condition>)`, never
    `EXISTS`; several filters are several `IN` subqueries;
  - lists by date page by keyset `(SubmittedUtc, Id)`, never by `OFFSET`;
  - a sort by a field's value walks that field's value index: `FROM jfbEntryValue v CROSS JOIN jfbEntry e WHERE
    e.Id = v.EntryId AND v.FieldId = @f AND e.FormId = @form ORDER BY v.Number DESC, e.Id DESC`, with a keyset
    `(value, Id)` and a plain range bound on the value. `CROSS JOIN` fixes SQLite's join order and is an ordinary
    inner join on SQL Server. Once the walk runs out, the list continues with the entries that have no value for the
    field, by date (`NOT IN`), without a count of their own (the pager has the form's total);
  - a search is `SearchText LIKE @term` on the normalised search text, with `%`, `_` and the escape character in the
    term escaped, and it runs **in one pass**: the newest 1,001 matches give the page and an exact count up to
    1,000. Beyond that the screen says "more than 1,000". Filters without a search term keep their exact count;
  - the values of a page load by `EntryId IN (…)`, with at most 1,000 ids per statement.
- **SQLite needs no planner statistics.** The shapes above meet the budgets with and without `ANALYZE`. Jiron Form
  Builder never runs `ANALYZE`, and a site that does still gets plans inside the budget (measured both ways).
- **A query budget:** every list, count and export page runs under a time limit (10 s; on SQLite through
  the connection's progress handler, on SQL Server as the command timeout). Past it, the statement is interrupted,
  the gate is released, and the API answers `query_timeout`. One bad plan then never blocks a site's writes.
- **Exports** page by keyset in pages of 250 entries, each page its own gated read, so a submission waits behind one
  page at most.
- **Entry numbers** come from `UPDATE jfbForm SET LastEntryNumber = LastEntryNumber + 1`, read back in the same
  transaction, as the first write of the submission, with the unique index `(FormId, Number)` as the backstop.
- **`IgnoreCurrentState` is `false`,** stated in the plan. Every step is still idempotent (guards on tables, columns
  and indexes), so a manual rerun or a restored backup is harmless. With `true`, the spike replayed step 1 on an
  upgrade, harmless only because of its guards.

**The budgets, measured on SQLite.** 100,000 entries with 12 values each, medians of 9 runs in ms (the highest p95
in brackets), across every run of the final design: two databases, with and without statistics. The field id was
measured for the queries that touch values. The other numbers come from the field-key variant, which only makes the
value rows larger, so they are upper bounds.

| §4.12 budget | Measured | At what concurrency | Met |
| --- | --- | --- | --- |
| First page of a 100,000-entry form ≤ 300 ms | By date: 7.9 to 16.7 (20.9). One state: 7 to 11.7 (19.9). Page 1,000 by keyset: 1.7 to 3.6. One field filter: 10.4 to 79 (88) with the field id, 10.9 to 122 (190) with the key. Two filters: 70 to 98 (107), with the key 76 to 128 (173). Search in one pass: 42 to 115 (148). Sort by a value: 7.3 to 26 (29), with the key 8.5 to 34 (39); its page 1,000: 2.5 to 4; the entries without the value: 72 to 100 (134). Counts by state: 11 to 18 (23). | one request at a time | yes |
| 10,000-entry CSV export ≤ 5 s | 0.77 to 1.09 s alone, in pages of 1,000; 1.19 to 1.22 s in pages of 250 while 50 submissions arrive | alone, and with 5 submissions every 100 ms | yes |
| A submission ≤ 150 ms at p95 | 10 at once: p95 59 to 115 ms. One after another: median 5 to 6 ms, p95 23 to 30, 130 to 145 a second. During an export in pages of 250: p95 64 to 70. | up to 10 submissions arriving at the same moment | yes |

Beyond 10 at once, SQLite queues: 20 at once gave a p95 of 141 to 184 ms, 50 at once 356 to 398 ms. That is the
point where §4.12 steers busy sites to SQL Server, and 16.2 reports it again with the finished pipeline. Rendering
and the first email are measured in 1.4 and 1.6.

## Consequences

- §4.5 changes: `jfbEntryValue` (`FieldId`, `Number` as a double, the four indexes), `jfbEntry` (the extra index)
  and `jfbFormField` (a row before the first value). WORKLOG records the change.
- 4.1 creates exactly this schema in `CreateSchemaV1` (the spike's later steps fold into it). Its tests check the
  column types on SQLite (`typeof` gives `real` for numbers and `text` for dates), the indexes, that `FieldId` has no
  cascade, and that `IgnoreCurrentState` is `false`.
- 4.3's query builder writes only the shapes above. Its tests run the 1.3 correctness cases, check each shape's
  plan on SQLite with and without statistics (`EXPLAIN QUERY PLAN`: the value index, no `SCAN` of `jfbEntryValue`,
  no temporary b-tree for a sort by a value), check the one-pass search count ("more than 1,000"), and show a
  statement past its budget interrupted with `query_timeout` while a submission still succeeds.
- 11.1 shows the search count as an exact number up to 1,000 and as "more than 1,000" beyond.
- Number fields validate at most 15 significant digits.
- 11.4 exports in keyset pages of 250 and keeps the formula guard.
- The SQL Server differences to test are in the research note (section 10) for 4.1 and 4.3: collation, `DATETIME`
  rounding, the SET options of filtered indexes, cascade paths, paging syntax and the 2,100-parameter limit.
- **Revisit** when 16.2 shows search past its budget (full-text indexing then), when forms routinely exceed about
  150,000 entries on SQLite, or when an Umbraco release maps `decimal` to a numeric type on SQLite.
