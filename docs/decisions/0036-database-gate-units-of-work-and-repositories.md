# 0036. The database gate, units of work and the repositories

- Status: accepted; amends [ADR-0007](0007-background-work-outbox-and-email.md) (which errors a unit of work runs
  again after); the folder rules (creates and moves only below folders outside the trash, a trash and a restore of
  the whole tree, a delete only from the trash) amended by [ADR-0047](0047-trash-delete-and-folders.md) in 4.5d;
  amended in 4.8g (the cancellation of a budgeted read)
- Date: 2026-10-23
- Roadmap step: 4.3a (decided and built); 4.3b to 4.3f build the other repositories on it, 4.4 and 4.5 the
  services on top

## Context

D7 and ADR-0007 set the rules for the database: every unit of work goes through Umbraco's scopes and one async gate,
a write starts with a write, every state change is one statement, and Jiron Form Builder never opens a connection of
its own (on SQLite a connection outside Umbraco's shared cache can hang Umbraco's own transactions, 1.6). Tandem's
gate and its transient reruns are proven on this machine (§3.3). ADR-0033 gives the repositories product DTOs of their
own and makes aliases unique without regard to case through the collations. Step 4.3a builds the gate and the first
repositories: key-value, folders, forms with their versions and field catalogue, and settings. Building them raised
questions the rules leave open:

- **Units of work.** A service must combine several repository calls into one transaction (a publish writes a
  version, the form's row and the catalogue), but each repository call is a unit of its own.
- **Reruns.** ADR-0007 reruns units after lock conflicts. A lost connection or a timeout is transient too, but a write
  that loses its connection may have committed.
- **Refusals.** A stale save, a taken alias, a missing folder or a folder with contents must reach the API as distinct
  codes, without a read before the write that decides.
- **Duplicates.** A unique index refuses a duplicate, but Umbraco's database logs every failed statement as an error,
  with the statement, and the two databases raise different errors.
- **The folder tree.** A move must never make a ring (a folder below itself), and the walks up and down the tree need
  a bound: SQL Server stops recursion after 100 levels.
- **The connection ban.** It has to fail the build, also when the call hides in an async method or a lambda.

## Options

**How services combine repository calls:**

1. **An explicit unit-of-work object** passed to every repository method. Clear, but every call carries it, and the
   repositories could not be called on their own.
2. **Nesting:** a unit called inside another unit (or inside a scope Umbraco opened) joins its transaction, because
   Umbraco's nested scopes do. Repository calls stay plain; a service wraps them in one `WriteAsync`. Chosen.

**How refusals surface:**

1. **A result type per method.** Every caller unpacks it, and a nested call cannot end the outer unit.
2. **Exceptions:** one for a stale version and one for the other refusals, with a reason. A missing row is no
   refusal: it comes back as null or false. Chosen, because one mapping turns them into API codes (4.5, 4.7).

**How duplicates are kept out:**

1. **The unique index alone,** translating its error. Logs an error with the statement's values for an expected
   outcome, and differs per database.
2. **A condition in the statement** (`INSERT … SELECT … WHERE NOT EXISTS`, an `UPDATE … WHERE NOT EXISTS`), with the
   index as a backstop whose error is translated. Chosen.

**How the ban is tested:**

1. **A search of the source.** Misses calls through a variable or a base type.
2. **The assembly references.** Core needs Umbraco.Infrastructure, which holds everything.
3. **The IL of every method,** compiler-generated state machines and closures included, with a deliberate offender in
   the test that shows the scan finds each kind of call. Chosen.

## Decision

**The gate** is Tandem's: one async `SemaphoreSlim` on SQLite and a pass-through on SQL Server, re-entrant through an
`AsyncLocal`, with the provider read on first use because the database may be configured only during the install.

**A unit of work** is `FormBuilderDatabase.ReadAsync` or `WriteAsync`:

- Each runs in its own Umbraco scope, behind the gate. A read's scope completes itself; a write commits only when its
  work returns, and an exception rolls it back. The unit's cancellation token reaches its statements.
- A unit inside another unit, or inside a scope Umbraco opened, joins that transaction. So a service combines
  repository calls by running them inside one `WriteAsync`.
- A change a repository refuses inside an outer unit ends that unit too: Umbraco does not commit a scope whose child
  did not complete. A service that wants to carry on catches the refusal outside the unit.
- **The first statement of a write is a write,** so the transaction holds the write lock before it reads. A repository
  reads in a write unit only after the write, to return the row or to say why nothing changed.

**Reruns:** a unit the database turns away with a transient error runs again, at most 3 attempts, after waits of 50 and
250 ms taken from the `TimeProvider`.

- Every unit reruns after SQLite's busy and locked (5, 6) and SQL Server's deadlock and lock timeout (1205, 1222).
  This is ADR-0007's list.
- A read also reruns after a lost connection, Azure SQL's busy or moving database, or a command timeout. This amends
  ADR-0007. A write never reruns after these, because its commit may have arrived.
- Nothing reruns inside another unit or inside a scope Umbraco opened, because only the owner of a transaction can
  start it over. Nothing reruns once the commit is on its way, or after the caller cancelled.
- The rerun is logged as a warning with the error's type and code only. A database's message can quote the
  statement's values.

**State changes are one conditional statement each:**

- **Optimistic concurrency:** the version the caller read is a condition of the `UPDATE`. Every change to a form's row
  raises `concurrencyVersion`, which starts at 1, and a stale one throws `FormBuilderConcurrencyException` (`409
  concurrency_conflict`). A settings section's `version` works the same way, with 0 for a section the scope never
  saved.
- **Inserts that must happen once** are `INSERT … SELECT … WHERE NOT EXISTS`. On SQL Server the check takes
  `WITH (UPDLOCK, HOLDLOCK)`, which locks the checked range until the transaction ends, also under read-committed
  snapshot. On SQLite the gate serialises writers.
- **Version numbers and public revisions** are computed inside the `INSERT` of the version, after the concurrency
  `UPDATE` has locked the form's row. Two writers of one form therefore never count the same number. A publish keeps
  the revision of the last publish when its `PublicHash` is the same, and takes the next one otherwise: A, A, B, A
  gives 1, 1, 2, 3 (ADR-0002).
- **Deletes** carry their guard as `NOT EXISTS`:
  - a folder only while empty;
  - a form only without entries;
  - a catalogue field only without values;
  - a draft version only when it is neither pinned nor the form's draft, and no entry points at it.
- **A change that has nothing to do** (trashing a trashed form, unpublishing an unpublished one) returns the row as it
  is and raises nothing.

**Refusals:**

- `FormBuilderConflictException` carries a `FormBuilderConflict`: `DuplicateAlias`, `DuplicateKey`, `MissingFolder`,
  `FolderIntoItself`, `FolderTooDeep`, `FolderNotEmpty` or `InUse`.
- A unique index that refuses a row anyway (a writer the database let through at the same moment) is translated into
  the same exception.
- A missing row gives null or false.
- Each repository refuses what its columns cannot hold before the statement runs: lengths, key formats, lowercase hex
  hashes. SQL Server would refuse an overlong value and SQLite would store it, so this keeps the two databases alike.

**The repositories,** one per aggregate, internal, async and stateless:

- **Key-value:** keys are 1 to 255 characters of `a-z`, `0-9` and `. _ : / -`, so the case-insensitive key column
  never has to choose between two spellings. Listing by prefix escapes the wildcards of `LIKE`.
- **Folders:**
  - The tree is at most 32 levels deep, which bounds the walks far below SQL Server's limit.
  - A create checks the parent and the depth in one statement. A move checks the target, the ring and the depth in one
    statement, with recursive common table expressions (`WITH RECURSIVE` on SQLite, `WITH` on SQL Server). On SQL
    Server the walks lock what they read, so two moves that would make a ring together wait for each other.
  - Trash marks the folder's own row. Restore returns it to its parent, or to the root when the parent is in the
    trash. Trashing a folder's contents with it is the form service's (4.5).
- **Forms:** one aggregate of `jfbForm`, `jfbFormVersion` and `jfbFormField`.
  - A create writes version 1 as the draft. A save appends a draft and a publish appends a published version
    (ADR-0002).
  - The theme alias follows the published version, and unpublishing clears it.
  - Restore returns a form to its folder, or to the root when the folder is in the trash.
  - A delete lets go of the form's two version pointers first, then lets the cascades remove the versions and the
    catalogue.
  - Lookups by alias are plain equality: the collation compares without case and the unique index serves it.
  - Library lists filter, sort and page in the database, with offset paging of at most 500 rows. Entries get keyset
    paging (4.3d).
- **Settings:** a scope is `global` or a site's root key in the lowercase `D` format. The global rows exist from the
  migration on and are never deleted.

**Product DTOs** live in `Persistence/Dtos`, apart from the migration snapshots. A unit test checks that each names
exactly the columns of its table, with the same types and nullability.

**The connection ban** is a unit test that reads the IL of every method in Core and fails on any of these:

- a constructor of a `DbConnection` or an NPoco database;
- `DbProviderFactory.CreateConnection`;
- a `DbDataSource`'s connection or command;
- `IUmbracoDatabaseFactory.CreateDatabase`;
- `IDbProviderFactoryCreator.CreateFactory`.

The same scan of a deliberate offender in the test must find every kind of call, also in an async method and a
lambda. The gate runs the unit tests, so a connection of Core's own fails the build. Reflection could still get past
it; code review covers that.

**Amended in 4.8g** (the review of the Phase 4 fixes): a budgeted read (`QueryAsync`, ADR-0039) links its caller's
token to its unit's, also inside another unit, so a read stopped through its caller's token, its unit's or an outer
unit's ends with `OperationCanceledException`, never with the provider's own error for an interrupted statement.
SQLite runs a statement before its call returns, so a cancellation already scheduled is what interrupts it. The
change pipeline of ADR-0053 refuses a change inside a unit at its own steps as well.

## Consequences

- Services (4.4, 4.5) combine repository calls freely, and the API maps two exceptions to its codes.
- Both databases behave alike: the same refusals, the same lengths, aliases found in any case.
- The SQL that differs per database lives in `SqlNames`: quoting, paging, the insert-once hint and `WITH RECURSIVE`.
  The SQL Server variants run only in CI's SQL Server job, which the owner checks (OWNER-TODO). They use only
  constructs SQL Server documents: common table expressions before `INSERT` and `UPDATE`, scalar subqueries in an
  `INSERT … SELECT` without `FROM`, and table hints in the walks.
- Search in the library folds ASCII only on SQLite, as its `LIKE` and `NOCASE` do. SQL Server folds every letter.
- 4.3b to 4.3f follow the same pattern: the outbox claims of 4.3e are conditional updates with a token, and the entry
  numbers of 4.3c come from one `UPDATE … + 1`.
- **Revisit** if Umbraco's scopes stop joining nested scopes, if Core needs a second database, or if a site needs
  folders deeper than 32 levels.
