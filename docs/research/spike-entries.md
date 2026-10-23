# Entry storage at scale: what the SQLite spike showed

Step 1.3 built the entry tables of §4.5 in the Umbraco 17.7.1 sandbox (SQLite, port 5510), seeded 100,000 entries
with 12 values each and measured every query the entries screen, the export and the submission pipeline need. The
spike code is throwaway and git-ignored (`.local/sandbox/Spike/Entries/`); the decisions are in
[ADR-0003](../decisions/0003-entry-storage.md). Measured on 2026-10-22 and 2026-10-23 on the demo machine
(Windows 11, shared with other sessions, so single runs vary), through `Microsoft.Data.Sqlite` 10.0.11, the version
that `Umbraco.Cms.Persistence.Sqlite` 17.7.1 resolves.

## 1. What was built

- **Tables:** `jfbForm`, `jfbFormVersion`, `jfbEntry`, `jfbEntryValue`, `jfbEntryFile`, `jfbEntryEvent`,
  `jfbEntryNote` and `jfbConsentText` with the columns of §4.5, as NPoco DTOs with Umbraco's annotations
  (`[TableName]`, `[PrimaryKeyColumn]`, `[ForeignKey]`, `[Index]`, `[Length]`, `[NullSetting]`, `[SpecialDbType]`).
- **A `PackageMigrationPlan`** with five `AsyncMigrationBase` steps, each guarded by `TableExists`, `ColumnExists` or
  `IndexExists`:

  | State | Step | What it does |
  | --- | --- | --- |
  | `jfb-spike-001-create-entry-schema` | `CreateEntrySchemaV1` | Creates the tables from frozen snapshot DTOs nested in the step, referenced tables first, then the two filtered unique indexes with custom SQL. |
  | `jfb-spike-002-entry-channel` | `AddEntryChannelV2` | Adds a `channel` column with a default to a table that already holds rows. |
  | `jfb-spike-003-cover-value-indexes` | `CoverValueIndexesV3` | Replaces the three two-column value indexes with ones ending in `entryId` (section 5). |
  | `jfb-spike-004-value-join-index` | `ValueJoinIndexV4` | Replaces `(entryId)` with `(entryId, fieldKey)` (section 5). |
  | `jfb-spike-005-form-date-index` | `FormDateIndexV5` | Adds `(formId, submittedUtc)` (section 5). |

  Steps 3 to 5 exist because the measurements changed the index design. In the product they fold into
  `CreateSchemaV1`, which has not shipped.
- **The database gate** of D7, ported from Tandem: one unit of work at a time per process on SQLite (a
  `SemaphoreSlim`, re-entrant through an `AsyncLocal`), every write unit starting with a write, reads and writes
  inside Umbraco scopes.
- **A lab API** under the spike's management path: schema report, DDL from both syntax providers, type checks, seed,
  index switch, statistics (`ANALYZE`, or dropping them), measure, export, submissions, export while submitting, the
  filtered-index check, a copy of the values keyed by field id, and table and index sizes.

## 2. How Umbraco maps column types

What Umbraco 17.7.1's syntax providers write for a DTO property. The spike formatted one probe DTO with both
providers through `DefinitionFactory.GetTableDefinition` and `ISqlSyntaxProvider.Format`, with no SQL Server
connection:

| DTO property | SQLite | SQL Server | Note |
| --- | --- | --- | --- |
| `decimal` | `TEXT` | `DECIMAL(38,6)` | SQLite stores `'-2.0'`, `'10.0'` and compares them as text: wrong order, wrong ranges. SQL Server keeps only 6 decimals. |
| `decimal` with `[SpecialDbType("DECIMAL(38,10)")]` | `TEXT COLLATE NOCASE` | `NVARCHAR` | A custom special type is not passed through. On SQL Server, `NVARCHAR` without a length holds one character. Worse on both. |
| `double` | `REAL` | `FLOAT` | An 8-byte float on both. |
| `DateTime` | `TEXT` | `DATETIME` | SQLite holds `yyyy-MM-dd HH:mm:ss.FFFFFFF` (Microsoft.Data.Sqlite's format, trailing zeros trimmed). SQL Server rounds to about 3 ms. |
| `bool` | `INTEGER` | `BIT` | |
| `Guid` | `TEXT` | `UNIQUEIDENTIFIER` | 36 characters on SQLite. The two sort differently, so nothing may sort by a GUID. |
| `string` with `[Length(n)]` | `TEXT COLLATE NOCASE` | `NVARCHAR(n)` | `NOCASE` folds ASCII only. |
| `string` with `NVARCHARMAX` | `TEXT COLLATE NOCASE` | `NVARCHAR(MAX)` | |
| `ColumnDefinition.CustomType` | as given | as given | Only reachable by building the table definition in our own code instead of `Create.Table<T>()`. |
| `long` primary key | `INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT` plus `CONSTRAINT PK_… UNIQUE (id)` | `BIGINT IDENTITY(1,1)` plus a separate primary key | On SQLite the `UNIQUE` constraint adds a second, redundant index on the row id. |

What is stored, read back through the DTO and through plain `Execute` (`typeof()` on SQLite):

- a number property: `real` (`-12.5`);
- a date: `text` (`'2026-10-22 21:30:15.123'`), read back with `DateTimeKind.Unspecified`, so repositories must set
  the kind to UTC;
- a bool: `integer`; a GUID: `text`.

**Text-stored dates sort correctly only while every value is written the same way.** The fixed format puts the
fields in order, and trimmed fractions still compare correctly (`…:59` < `…:59.25` < `…:59.5`). A value written in
another shape breaks this: an ISO string with `T`, a `DateTimeOffset` parameter (which adds `+00:00`), or a SQL
function with another format. The product writes dates only as `DateTime` parameters in UTC.

**Numbers as doubles.** A `double` is exact for every decimal of up to 15 significant digits. The spike converted
200,000 random decimals per digit count, sorted:

| Significant digits | Changed by the conversion | Order broken | Two values merged |
| --- | --- | --- | --- |
| 13 | 0 | 0 | 0 |
| 14 | 0 | 0 | 0 |
| 15 | 0 | 0 | 0 |
| 16 | 180,138 | 0 | 0 |
| 17 | 197,960 | 0 | 0 |

So `Number` is a `double`, `Text` keeps the value exactly as submitted, and number fields accept at most 15
significant digits (ADR-0003).

## 3. Migrations: fresh install, upgrade and `IgnoreCurrentState`

Every check below started from `node scripts/sandbox.mjs reset`, which deletes the database, so the next start is
an unattended fresh install. Environment variables simulate package versions: `SPIKE_ENTRY_PLAN_STEPS=1` ships only
step 1, and `SPIKE_ENTRY_IGNORE_STATE=1` overrides `IgnoreCurrentState`.

| Run | What happened |
| --- | --- |
| Fresh install with step 1 only | The 8 tables and 2 filtered indexes were created; the state is `jfb-spike-001-create-entry-schema`. 2,000 entries were seeded, plus 35 for the type checks. |
| Upgrade to step 5 | Only steps 2 to 5 ran. The new `channel` column was filled with its default on all 2,035 existing rows. The index swaps took under a second. All 21 type checks pass. |
| Fresh install of all five steps | Steps 1 to 5 ran in order. The schema report (the columns of `jfbEntry` and `jfbEntryValue`, every index's SQL) is **identical** to the upgraded one: 44 columns, 13 indexes. All 21 type checks pass. |
| Restart at the final state, `IgnoreCurrentState=true` | Nothing ran. A plan at its final state is not pending, whatever `IgnoreCurrentState` says (Tandem's spike found the same). |
| Upgrade from step 1, `IgnoreCurrentState=true` | Step 1 ran again: "table … exists" eight times and "index … exists" twice, harmless only because of its guards. Then steps 2 to 5 ran. The schema is again identical. |
| Filtered unique indexes, after the upgrade and after the fresh install | Two entries with a NULL `challengeId` and a NULL `resumeTokenHash` were stored. A repeated `challengeId` in one form and a repeated `resumeTokenHash` were refused with `UNIQUE constraint failed`. |

What else the migration API showed:

- `Create.Table<T>().Do()` writes the annotated indexes and foreign keys. On SQLite the foreign keys go into
  `CREATE TABLE`, so referenced tables must be created first.
- A filtered index (`… WHERE x IS NOT NULL`) cannot be expressed by the annotations. `Execute.Sql` with the syntax
  provider's quoting creates it on both databases.
- `Delete.Index(name).OnTable(table).Do()` drops an index on both providers; `AddColumn<TDto>(table, column)` adds a
  column from a snapshot DTO, default included.

**`IgnoreCurrentState` stays `false`** (ADR-0003). With `false`, an upgrade runs only the new steps, each once,
atomically with its state. `true` replays every step on every upgrade, so every step, data steps included, would
have to stay idempotent forever. The steps are idempotent anyway, as a guard against a manual rerun or a restored
backup.

## 4. Correctness: filters and sorts on numbers and dates

The type checks insert 19 numbers and 16 dates into a test form, run each query and compare the result with what
C# computes. The numbers are negative, decimal and multi-digit: `-123456789012.345`, `-10`, `-2.5`, `-2`, `-0.001`,
`0`, `0.000000001`, `0.1`, `0.3`, `0.30000000000001`, `0.5`, `2`, `9`, `10`, `10.25`, `100`, `1000`,
`12345.6789` and `99999999999999.9`. The dates run from 1900 to 2100, with fractions of a second.

All 21 checks of the design pass on the fresh install and after the upgrade:

- number ranges `-3..10`, `9.5..1000` and `-11..-2.25`, `> 9`, `= 0.3` (not `0.30000000000001`),
  `>= 0.30000000000001 and < 1`, `> 0 and < 0.001`, and a range bound as `decimal` parameters;
- number sorts ascending and descending, through a join and through the value-index walk of section 7;
- date ranges `2009..2024-12-31 23:59:59`, the year 2025 and the day 2025-06-30 (half-open, with fractions), and
  before 2000;
- date sorts ascending and descending, through a join and through the value-index walk;
- keyset pages of 4 over a number sort (cursor `(number, id)`), which return every value once and in order;
- the storage classes: `real` numbers, `text` dates.

Probes of the rejected column types, kept in the same run as evidence:

- a `TEXT` column (Umbraco's mapping of `decimal`) sorts `-0.001 -10.0 -123456789012.345 -2.0 …` and finds 7 of the
  12 values in `-3..10`;
- a `DECIMAL(38,10)` column has NUMERIC affinity, so SQLite stores a REAL anyway: the range is right, but
  `99999999999999.9` reads back as `99999999999999.906`. It buys nothing over `REAL`, and Umbraco cannot create it
  through the annotations (section 2).

## 5. The load and the index design

**The load:** a form with 12 fields (first name, last name, email, city, country, age, amount, quantity, date of
birth, appointment, consent, message) and 100,000 entries over three years (60% Submitted, 30% Approved, 5%
Rejected, 5% Spam), each with 12 values: 1.2 million value rows. The amounts run from -5,000 to 50,000 with two
decimals. Names and cities include `ü`, `ø`, `ç` and `ł`, and the message has 5 to 40 words. A second form has
2,000 entries. Seeding took 151 s in transactions of 2,000 entries.

How the indexes changed, and why:

| Index (`jfbEntryValue` unless named) | §4.5 sketch | Now | Why |
| --- | --- | --- | --- |
| `(fieldKey, textKey)` | yes | `(fieldKey, textKey, entryId)` | A filter written as `EXISTS (… v.entryId = e.id AND v.fieldKey = ? AND v.textKey = ?)` seeked on two columns and read every matching value once per entry. On 100,000 entries without statistics it ran for more than 30 minutes and blocked Umbraco's own writers (the cache instruction sync stalled) until the process was stopped. With `entryId` in the index, the `IN` shape reads entry ids straight from the index. |
| `(fieldKey, number)`, `(fieldKey, date)` | yes | `… , entryId` | The same, for number and date filters. |
| `(entryId)` | yes | `(entryId, fieldKey)` | Sorting by one field's value joins each entry to that value. With only `(entryId)`, SQLite chose the covering `(fieldKey, number, entryId)` index and read all of a field's values for every entry: stopped after more than 10 minutes, later interrupted by the query budget at 20 s. `(entryId, fieldKey)` also serves the cascade delete. |
| `jfbEntry (formId, state, submittedUtc)` | yes | kept | Serves one state and the counts by state. |
| `jfbEntry (formId, submittedUtc)` | no | added | "All states" and "not spam" sorted every entry of the form in a temporary b-tree (129 ms). This index walks the newest first (8 ms), and the export pages along it (3.3 s became under 1 s). |

**Planner statistics.** SQLite has none until `ANALYZE` runs (2.3 s on this database, 1.7 s on a later copy), and
then it judges selectivity from them, for example `IX_jfbEntryValue_fieldKey_textKey_entryId: 1224035 47079 6 1`.
The pathological shapes above ran away without statistics and took 0.2 to 0.5 s with them. The shapes ADR-0003
chooses do not depend on them: without statistics SQLite walks `(formId, submittedUtc)` and tests each entry against
a Bloom filter built from the value subquery; with statistics it fetches the matching entries by row id and sorts
them. Both stay inside the budget (section 7). Dropping `sqlite_stat1` does not reach connections that already
loaded the statistics; only new connections plan without them, so the comparison runs after a restart.

**A query budget.** A runaway statement holds the gate, and through SQLite's single writer it blocks every other
write of the site. The spike interrupts a statement after 20 s through SQLite's progress handler
(`sqlite3_progress_handler`, checked every 10,000 virtual-machine steps), on the `SqliteConnection` unwrapped from
Umbraco's connection wrappers. The interrupted statement fails with `SQLITE_INTERRUPT`, and the gate is free again.

## 6. The field key: a GUID string or an integer id

§4.5 sketched `jfbEntryValue.FieldKey` as the field's key, a GUID. On SQLite that is a 36-character text, stored in
every value row and in all four value indexes. The spike copied the 1.2 million values into a second table that
refers to the field by an integer id (a row of a field table, as `jfbFormField` provides), with the same indexes,
and measured both. Sizes were measured as the pages each `DROP` frees inside a rolled-back transaction, because this
SQLite build has no `dbstat` (1 MB = 1,048,576 bytes here):

| | Field key (GUID text) | Field id (integer) |
| --- | --- | --- |
| Value rows, with the redundant unique id index | 135.2 MB | 93.0 MB |
| `(entry, field)` | 65.0 MB | 15.9 MB |
| `(field, textKey, entryId)` | 88.7 MB | 37.2 MB |
| `(field, number, entryId)` | 66.1 MB | 18.2 MB |
| `(field, date, entryId)` | 69.7 MB | 20.8 MB |
| **Values with their indexes** | **424.7 MB** | **185.2 MB** (56% less) |
| Whole database, 100,000 entries | 489.5 MB | about 250 MB (computed) |

`jfbEntry`'s nine indexes take 21.2 MB in all.

Speed, same database, medians of 9 runs in ms (p95 in brackets). "No statistics" ran twice on fresh connections:

| Query | Key, no statistics | Key, again | Id, no statistics | Id, again | Key, statistics | Id, statistics |
| --- | --- | --- | --- | --- | --- | --- |
| text: city = 'berlin' (IN) | 14 (38.4) | 11.9 (14) | 13.2 (17.2) | 16.3 (18.2) | 19.7 (26.4) | 15.8 (18.6) |
| text: last name starts with 'sch' | 30.8 (54.5) | 30.7 (35.7) | 45.1 (46.8) | 37.4 (44) | 91.6 (105.8) | 79.1 (88) |
| number: amount 1000..2000 | 13.9 (17.1) | 11.7 (15) | 17.1 (20.5) | 10.4 (17.2) | 16.2 (19.5) | 15.5 (20.2) |
| number: age 30..39 | 41 (44.3) | 24.5 (28.2) | 31.4 (32.8) | 36.4 (43.7) | 69.7 (72.8) | 62.8 (65.2) |
| number: amount < 0 | 24 (38.5) | 21.4 (25.2) | 29.6 (32.1) | 31.1 (48.6) | 54.3 (58.1) | 50.3 (57.6) |
| date: appointment in March 2025 | 25.2 (25.8) | 14.8 (21) | 19.5 (21.4) | 20.2 (23.3) | 23.3 (30.6) | 23.1 (26.9) |
| date: born in the 1980s | 29.7 (48.3) | 27.2 (29.1) | 40.4 (43) | 48.6 (72.4) | 79.5 (92.8) | 70.7 (74.5) |
| two filters: city = 'berlin' and amount > 20000 | 127.5 (132.6) | 107.2 (136.5) | 97.7 (106.7) | 78.4 (94.2) | 92.1 (121) | 69.8 (77.1) |
| sort: amount descending (value index walk) | 8.5 (12.6) | 13.8 (18.1) | 7.3 (10.7) | 13.2 (15.5) | 11.7 (14.5) | 7.9 (11.8) |
| sort: last name ascending (value index walk) | 15.2 (25.8) | 25.4 (28.9) | 15.8 (20.1) | 26 (28.6) | 23.6 (28.2) | 16.2 (17.6) |
| sort: amount descending, page 1,000 (keyset on the walk) | 3 (3.5) | 3.7 (4.5) | 2.5 (4.1) | 3.7 (5.9) | 4 (6.6) | 2.6 (4.5) |
| sort: amount, entries without an amount (tail) | 86.7 (134) | 100.2 (130.2) | 74.2 (87) | 90.2 (115.4) | 89.8 (123.1) | 72.2 (77.8) |

Without statistics the two are level: that plan's time goes into walking the entries. With statistics the integer id
is faster on every query, by 1% to 35%. The size difference holds either way.

## 7. Measurements

Medians of 9 runs in ms, p95 in brackets, with one request at a time. Every list query also counts its matches for
the pager and loads the 50 entries' values for display; both are included. The final indexes (field key variant) on
a database that was freshly seeded, first without statistics, then after `ANALYZE`. "No indexes" ran 3 times on an
earlier copy of the load with only the unique constraints and with statistics.

| Query | No indexes | Final indexes, no statistics | Final indexes, statistics |
| --- | --- | --- | --- |
| first page by date, all states | 511 (669.9) | 10.6 (16.4) | 7.9 (9) |
| first page by date, state = Submitted | 688.5 (829.8) | 7 (8.1) | 10.5 (19.9) |
| first page by date, not spam (IN) | 640.1 (773.3) | 10 (16) | 13.1 (14.5) |
| page 1,000 by date (keyset) | 589.6 (620.1) | 1.7 (2.4) | 2.4 (3) |
| small form (2,000 entries), first page | 344.3 (356.3) | 2.1 (5.3) | 2.3 (9.4) |
| text: city = 'berlin' (IN) | 1214.5 (1325.2) | 17.4 (26.2) | 34.5 (42.6) |
| text: last name starts with 'sch' | 1060.3 (1078.4) | 47.5 (65.4) | 122.1 (176.4) |
| number: amount 1000..2000 | 952.2 (1011.8) | 14.6 (16.4) | 17.9 (30.9) |
| number: age 30..39 | 1024.4 (1032.3) | 27.8 (33.8) | 72.6 (94.2) |
| number: amount < 0 | 988.3 (1007.8) | 18.4 (26.3) | 75.1 (118.7) |
| date: appointment in March 2025 | 971.3 (988.4) | 10.9 (12.3) | 22.1 (34.9) |
| date: born in the 1980s | 1058.1 (1066.2) | 26 (36.8) | 110.4 (162.1) |
| two filters: city = 'berlin' and amount > 20000 | 945.7 (1132.9) | 76.2 (110.8) | 87 (118.5) |
| search: 'müller' (common) | 331 (338.2) | 106.1 (133.3) | 87.6 (139.7) |
| search: one email address (rare) | 289.2 (313.9) | 150.9 (187) | 188.6 (251.3) |
| search: no match | 112.2 (114.4) | 126.7 (226.2) | 199.1 (273.4) |
| search: 'müller', not spam | 370.6 (380.2) | 151.8 (164.1) | 203.2 (259.9) |
| counts by state | 76.3 (79.3) | 11.4 (17.4) | 16.4 (19.7) |
| sort: amount descending (value index walk) | 470.7 (471.3) | 10.6 (12.3) | 14.4 (16.6) |
| sort: last name ascending (value index walk) | 440.7 (478.2) | 21.3 (24.3) | 23.5 (27) |

Two list queries that only the sort by a value needs (final indexes, the field key variant):

| Query | No statistics (two runs) | Statistics |
| --- | --- | --- |
| Sort by amount, page 1,000 (keyset on the walk) | 3.0 (3.5), 3.7 (4.5) | 4.0 (6.6) |
| Sort by amount, the entries without an amount (the tail) | 86.7 (134), 100.2 (130.2) | 89.8 (123.1) |

**Search in one pass.** A search as a list plus a count scans the form twice when the term is rare: the list walks
every entry looking for 50 matches, and the count walks them all again. Reading the newest 1,001 matches instead
gives the page and an exact count up to 1,000 in one walk ("more than 1,000" beyond), and a common term stops early.
Same database, final indexes:

| Query | No statistics | Again | Statistics | Again |
| --- | --- | --- | --- | --- |
| search: 'müller' (common) | 110.9 (123.7) | 110.3 (114.3) | 67.9 (99.8) | 89.4 (147) |
| search: one email address (rare) | 228 (255.5) | 221.4 (312.7) | 147.7 (241.7) | 180 (244.4) |
| search: no match | 204.5 (243.3) | 192.2 (221.4) | 148.7 (177.3) | 185.9 (247.9) |
| search: 'müller', not spam | 170.3 (205.7) | 131.2 (173) | 155.1 (202.3) | 122.4 (216.4) |
| search, one pass: 'müller' (common) | 53.5 (68.5) | 53.6 (83.7) | 46.9 (67.1) | 42.2 (60.1) |
| search, one pass: one email address (rare) | 114.6 (131.7) | 87.2 (114) | 86.9 (123.1) | 82.5 (87) |
| search, one pass: no match | 96.2 (112.4) | 91 (98.9) | 67 (148.2) | 97.3 (137.3) |
| search, one pass: 'müller', not spam | 62.6 (76) | 44.6 (54.3) | 44.9 (60.6) | 43.4 (86.7) |

The list-and-count search reached a p95 of 313 ms here and 360 ms in an earlier run, past the budget; one pass stays
under 150 ms.

**Query shapes the product does not use** (final indexes, 9 runs):

| Shape | Median | Instead |
| --- | --- | --- |
| Text filter as `EXISTS (…)` | 192 (419 in an earlier run) | `e.id IN (SELECT v.entryId …)`: 12 to 35 |
| Page 1,000 by `OFFSET 49,950` | 7.4 (257 without the date index) | keyset `(submittedUtc, id) < (?, ?)`: 2 to 4 |
| Sort by amount, value join driven from the value index | 430 | the value-index walk: 8.5 to 15 |
| Sort by amount, `LEFT JOIN` from the entries | 447 | the value-index walk: 8.5 to 15 |
| Sort by last name, value join | 496 | the value-index walk: 15 to 34 |
| Search as a list plus a count | 68 to 228 (p95 up to 360) | one pass: 42 to 115 (p95 up to 148) |
| The tail of a sort with a count of its own | 246 to 267 (p95 up to 315) | without the count: 72 to 100 (p95 up to 134) |

**The value-index walk** fixes the join order with `CROSS JOIN`, which SQLite never reorders:
`FROM jfbEntryValue v CROSS JOIN jfbEntry e WHERE e.id = v.entryId AND v.fieldKey = ? AND e.formId = ?
ORDER BY v.number DESC, e.id DESC LIMIT 50`. SQLite reads the field's value index in sort order and stops after the
page. Later pages continue from the cursor `(number, id)` with a plain range bound (`v.number <= ? AND (v.number < ?
OR e.id < ?)`), so SQLite seeks into the index instead of reading it from the start. Entries without a value for the
field are not in that walk; once it runs out, the list continues with them by date
(`e.id NOT IN (SELECT v.entryId … WHERE v.fieldKey = ?)`). That tail needs no count of its own, because the pager
already has the form's total. Measured where it costs most: a field that every entry answered, so the tail walks the
whole form and finds nothing.

## 8. Export

A 10,000-entry CSV (2.8 MB, 15 columns, UTF-8 with BOM, formula-like cells prefixed with `'`) streamed in keyset
pages along `(formId, submittedUtc)`, each page one gated read of its entries and their values, flushed after each
page:

| Run | Total | Longest page | Submissions during it (5 every 100 ms, 50 in all) |
| --- | --- | --- | --- |
| Pages of 1,000, before the date index | 3.26 to 3.80 s | 405 to 608 ms | |
| Pages of 1,000 | 0.77 to 1.09 s | 114 to 156 ms | |
| Pages of 1,000, while submitting | 1.15 to 1.25 s | 153 to 225 ms | median 49 to 55, p95 160 to 173 |
| Pages of 500, while submitting | 1.40 to 1.64 s | 151 to 160 ms | median 38 to 43, p95 104 to 137 |
| Pages of 250, while submitting | 1.19 to 1.22 s | 81 to 101 ms | median 30 to 35, p95 64 to 70 |

A submission waits behind at most one page, so the page size sets how long. Pages of 250 keep submissions well
inside their budget, and the export still takes about 1.2 s.

## 9. Submissions and entry numbers

One submission as the pipeline will store it: `UPDATE jfbForm SET lastEntryNumber = lastEntryNumber + 1` (a write
first), the number read back in the same transaction, the entry with its search text, 12 value rows and three
event rows standing in for the timeline event and two queued action runs. Timed from the call until the commit
returned, so the wait at the gate is included. 100,000 entries were already stored.

| Load | Median | p95 | Max | Wall time |
| --- | --- | --- | --- | --- |
| 50 one after another (3 runs) | 5.0 to 6.2 | 23 to 30 | 34 to 38 | 342 to 384 |
| 10 at the same moment (3 runs) | 30 to 66 | 59 to 115 | 59 to 115 | 59 to 119 |
| 20 at the same moment (3 runs) | 89 to 119 | 141 to 184 | 165 to 197 | 165 to 197 |
| 50 at the same moment (3 runs) | 201 to 232 | 356 to 398 | 364 to 406 | 364 to 407 |

On SQLite, submissions queue at the gate (and would queue at SQLite's single writer without it), so each one waiting
ahead adds about 7 ms. One after another, SQLite stores 130 to 145 submissions a second. In every run the numbers
were unique and contiguous, and the unique index `(formId, number)` never refused one. These runs used the field
key; the integer id only makes the value rows and their indexes smaller.

## 10. SQL Server differences to test (steps 4.1 and 4.3)

The spike ran on SQLite only, because SQL Server tests run in CI (CLAUDE.md rule 6). These differences need their
own tests there:

1. **Types:** `Number` becomes `FLOAT` (the same 8-byte float as SQLite's `REAL`), `Date` becomes `DATETIME`
   (rounded to about 3 ms: ranges are half-open, lists order by `(time, id)`, and nothing compares times for
   equality), bools `BIT`, GUIDs `UNIQUEIDENTIFIER` (sorted differently from SQLite's text, so never sort by them).
2. **Collation:** SQLite compares `TEXT COLLATE NOCASE`, folding ASCII only, then by code point (`ü` sorts after
   `z`). SQL Server uses the database collation, usually case-insensitive and accent-sensitive; an
   accent-insensitive one would make `muller` match `müller`. `TextKey` and `SearchText` are folded in C#, so
   equality, prefix and search filters agree on both under a case-insensitive, accent-sensitive collation. Tests
   cover them with non-ASCII text on both; the sort order of non-ASCII text, and the wider matches under an
   accent-insensitive collation, follow the database and are documented.
3. **Filtered unique indexes** require certain SET options (among them `ANSI_NULLS` and `QUOTED_IDENTIFIER` on) on
   every connection that writes the table. `Microsoft.Data.SqlClient`'s defaults are expected to meet them; a test
   stores two NULL `ChallengeId` rows and refuses a repeated one (§4.5).
4. **Cascade paths:** SQL Server refuses two cascading paths to one table. `jfbEntryValue.FieldId` therefore refers
   to `jfbFormField` without a cascade: the form service deletes entries before the form. The DDL snapshot test of
   4.1 catches a second cascade path before CI does.
5. **Index shapes:** a SQL Server non-clustered index already carries the clustered key (`Id`), so
   `(FieldId, TextKey, EntryId)` covers the `IN` shape there too. `CROSS JOIN … WHERE` is an inner join there, and
   the optimizer picks the order itself; the CI test checks that the sort by a value stays inside the budget at
   100,000 entries.
6. **No gate:** SQL Server runs without the gate, so the entry number relies on the atomic `UPDATE … SET
   LastEntryNumber = LastEntryNumber + 1` read back in the same transaction (or `OUTPUT inserted.…`). The unique
   index `(FormId, Number)` is the backstop. The 50 parallel inserts of 4.3 run on both databases.
7. **Statistics** are automatic on SQL Server; `ANALYZE` is SQLite's.
8. **The query budget** is a command timeout on SQL Server, with the same `query_timeout` outcome.
9. **Paging and parameters:** `LIMIT n` becomes `OFFSET 0 ROWS FETCH NEXT n ROWS ONLY`. `IN (@list)` expansion has to
   stay under SQL Server's 2,100 parameters, so value loads take at most 1,000 entry ids per query.

## 11. What changed in the roadmap

- §4.5: `jfbEntry` gains the index `(FormId, SubmittedUtc)`. `jfbEntryValue` refers to the field by `FieldId` (the
  field catalogue row in `jfbFormField`, no cascade) instead of `FieldKey`. `Number` becomes a double (exact to 15
  significant digits; `Text` keeps the submitted value). The value indexes become `(EntryId, FieldId)`,
  `(FieldId, TextKey, EntryId)`, `(FieldId, Number, EntryId)` and `(FieldId, Date, EntryId)`. A field gets its
  catalogue row before its first value is stored.
- §4.5 also says how dates are written on SQLite and which query shapes entry queries use; §4.12 states the
  submission budget's concurrency on SQLite (10 at the same moment).
- 4.1 and 4.3 gain checks for the column types, the indexes, the query plans with and without statistics, the
  query budget and the one-pass search count; 5.4 limits numbers to 15 significant digits; 1.5 notes that limit;
  11.1 shows "more than 1,000" for a search; 11.4 exports in pages of 250 (WORKLOG, 2026-10-23).
