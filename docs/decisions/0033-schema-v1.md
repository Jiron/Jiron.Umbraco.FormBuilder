# 0033. Schema v1: conventions, the migration plan and its checks

- Status: accepted
- Date: 2026-10-23
- Roadmap step: 4.1 (decided and built); 4.2 and 4.3 follow its text rules; 4.7, 6.1, 6.2, 8.1 and 9.1 check the
  migration state

## Context

Step 4.1 turns §4.5, [ADR-0003](0003-entry-storage.md) and the 1.3 spike into the first migration step. Building it
raised questions that the roadmap leaves open or that only the real DDL showed:

- **Names.** §4.5 writes columns in PascalCase; Umbraco's own tables use camelCase. Some of §4.5's names are SQL
  reserved words (`To`, `From`, `Trigger`), and Ligata's lesson (§3.2) is that NPoco needs explicit column names.
- **Foreign-key order on SQL Server.** Umbraco's SQL Server provider adds a table's foreign keys right after the
  table, but `jfbForm` refers to `jfbFormVersion`, which is created after it (and refers back). SQLite writes the keys
  into `CREATE TABLE`, where a reference to a later table is allowed.
- **Indexes that deletes need.** SQL Server does not index foreign-key columns by itself. Pruning a draft save checks
  `jfbEntry.formVersionId`, and the orphan-upload cleanup checks `jfbEntryValue.fileId`; neither had an index in §4.5,
  so each delete would scan a table that grows with every entry.
- **Text in keys and collation.** Umbraco creates text columns `COLLATE NOCASE` on SQLite (ASCII case folding only)
  and requires a case-insensitive collation on SQL Server, which also folds non-ASCII case and, under Windows
  collations, treats some different spellings as equal. A unique key over free text can therefore refuse a row on one
  database and accept it on the other. Aliases are ASCII (`^[a-z][a-zA-Z0-9]{0,63}$`); option values are not.
- **The SQL Server check without a server** that 4.1 asks for, marked *(verify feasibility)*: there is no LocalDB or
  Docker here, and CLAUDE.md rule 6 forbids the local SQL Server instance.
- **The data steps:** what the default settings rows hold, and how the Forms section is granted.
- **The migration state:** with unattended package migrations off, a site runs while the tables are missing or old.

## Options

1. **The offline check:**
   - render the DDL in the test by repeating what Umbraco's `HandleCreateTable` does: cheap, but it drifts from both
     Umbraco and the step;
   - run the step's own creation routine against a recording database with Umbraco's `SqlServerSyntaxProvider`, which
     needs no connection: the snapshot is exactly what the step issues;
   - a real SQL Server: not available here (above), and the CI job already does it.
2. **Free text in keys:**
   - trust each database's collation: option values that differ only by case, or by spellings a Windows collation
     equates, would share one capacity counter on SQL Server and not on SQLite;
   - a binary collation on SQL Server: Umbraco's annotations cannot declare one, and changing a column's collation
     after Umbraco has indexed it fails;
   - keep free text out of keys: key the capacity table by the value's SHA-256, and have code write every other text
     key in one fixed form.
3. **The section grant:** through `IUserGroupService` (it checks the current user's permissions and publishes
   notifications, which can fail inside a migration), or with SQL on `umbracoUserGroup2App`, as Umbraco's own
   migrations grant sections.

## Decision

**Conventions.** Tables keep §4.5's names; columns are camelCase, as Umbraco's are, each with an explicit
`[Column]` name. The reserved words become `toAddresses`, `ccAddresses`, `bccAddresses`, `fromAddress` (the address
lists hold up to 4,000 characters) and `triggerKind`. Every string has a `[Length]` or is `NVARCHAR(MAX)`, which no
index uses: aliases 64, names 255, cultures 32, hashes 64 (lowercase hex), short enum words 16. Ids are `int`, and
`bigint` for the tables that grow with every entry (entries, values, events, action runs, emails, audit). Snapshot
classes are nested in each step and never edited; repositories (4.3) get product DTOs of their own.

**Additions to §4.5.** `jfbEntry` gets `IX_jfbEntry_formVersionId`, and `jfbEntryValue` the filtered
`IX_jfbEntryValue_fileId` (only file fields have a file), so the deletes above use an index. `jfbEmailLayout` gets
`concurrencyVersion`, like the other library tables, so two editors cannot overwrite each other's layout. The
foreign keys are listed in [database.md](../database.md); these columns name a row without one, on purpose:

- `jfbEmail.actionRunId`: a second path from `jfbEntry`, which SQL Server refuses;
- `jfbEntryFile.formId`: uploads exist before their entry, and the form service deletes a form's files, bytes
  included, before the form (4.5);
- `jfbActionRun.formVersionId`: its entry already holds the version;
- `jfbReference.formId` and `jfbDailyStat.formId`: written by content saves and counters, which must never fail over
  a form that is gone.

**Foreign keys on SQL Server** that refer to a table created later are taken off their table's definition and added
once every table exists. Only the form's two version keys are affected.

**Text and collation.** Aliases are unique without regard to case on both databases, through the collations above.
The capacity table's key is `(formId, fieldKey, valueHash)`, `valueHash` being the SHA-256 of the value's UTF-8 bytes;
the value itself is kept beside it. Every other text in a key is written by code in one fixed form (lowercase hex,
GUIDs in the `D` format, fixed words), and challenge ids are 32 lowercase hex characters. 4.2 refuses aliases that
differ only by case, and option values in one field that are equal once folded as rules compare text
([ADR-0005](0005-shared-logic-and-test-vectors.md)), because entry filters compare folded text. 4.3 looks aliases up
without regard to case on both databases.

**The offline SQL Server check** is feasible and replaces nothing: `SchemaV1DdlTests` runs `CreateSchemaV1.CreateAsync`
on a recorder with `SqlServerSyntaxProvider` and compares the statements with the reviewed
`tests/Jiron.Umbraco.FormBuilder.Tests.Integration/Persistence/schema-v1.sqlserver.sql`. On the same statements it
checks that no table is reached by two cascading paths or a cascading cycle, that index keys fit 900 bytes (clustered)
and 1,700 bytes (non-clustered) and never include `NVARCHAR(MAX)`, that no string is a bare `NVARCHAR` (one
character), and that no key refers to a table before it exists. `JFB_UPDATE_SNAPSHOTS=1` rewrites the snapshot and
fails, so every change is reviewed. What only a server shows (the session options of filtered indexes, the deletes
that cascade into a no-action key, `DATETIME` rounding) runs in the `sqlserver` CI job.

**The plan** is `Jiron.Umbraco.FormBuilder` with three steps: `jfb-001-create-schema`, `jfb-002-default-settings`,
`jfb-003-grant-forms-section`. Each runs in its own transaction with its state and is idempotent; `IgnoreCurrentState`
is `false` (ADR-0003). Step 2 writes the global row of each settings section of
[ADR-0008](0008-settings-model.md) at schema version 1 and version 1 with `{}`: the defaults stay in code, and the
first save of a section has a row and a version to check. Step 3 grants the section to Umbraco's `admin` group with SQL,
as Umbraco does, and records the grant in `jfbKeyValue`, so a rerun never grants again and an administrator's later
removal stands. Without that group it warns, grants nothing and records that. After the plan has run, a notification
handler refreshes the user group cache on every server.

**The migration state** (`IFormBuilderMigrationState`) is pending while Umbraco is not running, while the stored state
differs from the plan's last state, and when the state cannot be read. "Current" is remembered for the life of the
process; "pending" is read again after 5 seconds, so a migration on another server is noticed. Until it is current,
the management API (4.7) and the public API (6.1) answer `503 migrations_pending`, forms render nothing for visitors
and a note for signed-in backoffice users (6.2), the jobs do nothing (8.1), and the dashboard and the health check say
so (9.1).

## Consequences

- §4.5, ADR-0003 (column names and the capacity key) and the steps named above change; WORKLOG records each change.
- [database.md](../database.md) describes every table, with a diagram that `DatabaseDocsTests` generates from the
  snapshots and checks.
- The `sqlserver` CI job runs the same migration, constraint and DDL tests; the owner checks the first run
  (OWNER-TODO).
- A later schema change is a new step with snapshots of its own, a new DDL snapshot of its own, and an upgrade test
  from every released state (16.5).
- **Revisit** if Umbraco starts supporting case-sensitive collations on SQL Server, or maps strings to another
  collation on SQLite: the alias rules then need an index of their own.
