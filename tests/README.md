# Tests

How Jiron Form Builder is tested is decided in [ADR-0011](../docs/decisions/0011-testing-strategy.md); the Testing
project is described in [ADR-0029](../docs/decisions/0029-testing-project.md), the shared vectors in
[ADR-0005](../docs/decisions/0005-shared-logic-and-test-vectors.md) and
[ADR-0030](../docs/decisions/0030-porting-the-shared-logic.md).

## Running them

```
dotnet test Jiron.Umbraco.FormBuilder.slnx --blame-hang-timeout 10m
```

The hang timeout names a test that hangs instead of stalling the run. The integration tests use SQLite and need nothing
installed; Umbraco works in `Tests.Integration/TEMP/`, which git ignores.

The browser tests start a fresh demo host of their own and run in Edge (Playwright's Chromium in CI):

```
cd tests/e2e
npm ci
npx playwright test --project=smoke
```

## The projects

| Project | What it holds |
| --- | --- |
| `Jiron.Umbraco.FormBuilder.Tests.Unit` | NUnit 3 and NSubstitute, no Umbraco host: the architecture tests (Core knows nothing about HTTP), the vector runner and the tables check for Core's shared logic (`Logic/`), the definition model and its JSON (`Definitions/`, below), the host configuration with its start-up checks and IntelliSense schema (`Configuration/`, below), the backoffice settings (`Settings/`, below), the form service's parts (`Forms/`, below), the permission model (`Access/`, below), the management API's policies, access filter and answers (`ManagementApi/`, below), and the Testing project's own tests. |
| `Jiron.Umbraco.FormBuilder.Tests.Integration` | A real Umbraco (`Umbraco.Cms.Tests.Integration` 17.x) with Jiron Form Builder's composers: the boot test (each service of Core resolved, one per site, and each notification handler registered), the people factories against real services, the test-only endpoints on a real site, the schema and its migrations, the repositories (`Persistence/`, below), the settings service (`Settings/`, below), the form services and the change pipeline they share (`Forms/`, below), the access service with the authorization policies (`Access/`, below) and the management API (`ManagementApi/`, below). |
| `Jiron.Umbraco.FormBuilder.Testing` | Test-only code for the tests and the test hosts, never packed. |
| `vectors/` | The shared vectors that Core and `packages/logic` both run (below). |
| `e2e/` | The browser tests: Playwright against a fresh demo host on 5520 ([e2e/README.md](e2e/README.md), [ADR-0031](../docs/decisions/0031-browser-harness.md)). |

## The host configuration

`Configuration/` checks the `JironFormBuilder` options as a site starts them (ADR-0042): `HostOptions` binds a
configuration, runs the validator and builds the services, and `HostOptions.Start` runs what the host runs before it
serves anything (`IStartupValidator`). A site that must not start is also run as a real process: the browser tests'
`startup` project ([e2e/README.md](e2e/README.md)).

`AppSettingsSchemaTests` generates the IntelliSense schema from the options and Core's XML docs and fails when
`src/Jiron.Umbraco.FormBuilder.Core/appsettings-schema.Jiron.Umbraco.FormBuilder.json` is stale; run the unit tests
with `JFB_UPDATE_SCHEMA=1` to rewrite it. Every `$ref` of the committed file must resolve within the file.

## The shared vectors

Rules, validation, merge tags and calculations exist in C# and in TypeScript, and both must give the same answer
(D10). Each module in `vectors/suites/` holds cases in the `jfb-vectors/1` format, and `vectors/build.mjs` writes it to
`vectors/<suite>.json` as plain ASCII, with special characters built from code points:

```
node tests/vectors/build.mjs
node tests/vectors/build.mjs --check
```

Both runners read every `vectors/*.json` in place, one test case per vector: `Logic/VectorTests.cs` in the unit tests
(a case is named `Vector(<suite>/<id>)`) and `packages/logic/test/vectors.test.ts` in the client's `npm test`. A case
that passes in one runtime and fails in the other is a bug in one of them.

**What long answers cost** is checked in both runtimes too, by `Logic/LogicCostTests.cs` and
`packages/logic/test/cost.test.ts`: rules over a number answer of 10,000 digits take under 50 ms (and in Core
allocate less than one copy of it, so no number, key or cleaned copy is built), a text condition against 20 operands
folds its answer once, an evaluation compiles each pattern once (Core shows both by what it allocates, TypeScript by
counting the calls), and a pattern's run keeps one set of buffers, which Core shows by what it allocates and TypeScript
by its time over ten times the answer. What numbers compare as lives in the shared vectors, not in these tests. `Logic/TextSemanticsTests.cs` shows
that cleaning returns a text it leaves alone as it is, and puts each replacement where it belongs.

**The tables check** compares the two runtimes' Unicode data. `npm run tables` in `packages/logic` writes what
TypeScript's clean, fold and NFC recomposition do to every code point into `vectors/tables/typescript.json`; a Vitest
test fails when the file no longer matches the running Node, and `Logic/UnicodeTablesTests.cs` compares Core with it,
listing the known gaps exactly.

## The definition tests

`Tests.Unit/Definitions/` tests the definition model, its reader and writer, the schema and the upgraders
([ADR-0034](../docs/decisions/0034-definition-model-reading-and-writing.md)), and publishing's structural validation
and the public projection ([ADR-0035](../docs/decisions/0035-structural-validation-and-public-projection.md)):

| Tests | What they show |
| --- | --- |
| `RoundTripTests` | 500 definitions from `DefinitionGenerator` (one per seed) write, read and write to the same bytes and model, and between them set every property of the model. |
| `CanonicalFormTests` | `fixtures/definitions/v1-full.json`, written by hand with every property, reads and writes byte for byte, and its hash. |
| `RefusalTests` | Every save refusal with its code and JSON path, each one change to a valid document. |
| `SaveCheckTests` | Every enum property of the full fixture set to a value its enum does not define, and every required value set missing, is refused at its path and by the writer, and the fixture reaches every such property of the model; a rule nested 100,000 groups deep is too deep at `$`, and at every depth what the checks pass reads back. |
| `RuleItemReadTests` | An error inside a rule item, and inside groups inside groups, named at its full path. |
| `DefinitionSchemaTests` | `schemas/jfb-form.v1.schema.json` validates the generated definitions and the fixture, names the same objects, properties, required ones and words as the model (`ModelProperties`), and refuses the refusals a schema can say. |
| `UpgradeTests` | A synthetic older version read through the real reader, and the chain's checks. |
| `DefinitionValueTests` | Localised texts (a null culture refused as documented), default values, settings objects, instants and the string escaping; what the writer cannot write reported as what it is; the reader of what the site stored built once; both readers refusing text and bytes alike; a group nested 30 deep read in about the time of one. |
| `ModelListTests` | No list of a read definition, its value types or its projection can be changed through a cast or through the list a record was built from, none takes a null item, and records compare their lists by their items, in order. |
| `StructureTests` | A document that publishes, and each publish refusal with its code and JSON path as one change to it (aliases that differ only in letter case and option values that fold to one text included); what publishes; that a save refusal comes first; that each one-per-form type counts on its own; and 500 generated definitions checked without throwing, every path naming a part of them. |
| `FieldTypeSupportTests` | §4.3.2 restated apart from the code: each built-in type refuses exactly the common properties, checks, rules and messages it does not support, and the one list of field messages that both checks use holds every message of the record with its own kind. |
| `RedirectUrlTests` | The redirects that could leave the editor's site, and those that stay on it. |
| `ProjectionTests` | 500 generated definitions with a marker in every private place project without it; the projection names only allowlisted properties; `fixtures/definitions/v1-full.public.json` is the reviewed projection of the fixture; which changes move `PublicHash` and which do not. |
| `DefinitionDiffTests` | The comparison of two versions ([ADR-0045](../docs/decisions/0045-versions-comparison-and-draft-cleanup.md)): texts per culture, a type's settings one by one, option items by value and grid rows by key with their order, moves to another parent and the fewest moves within one, rules whose order means nothing, settings groups by name, the order of the changes. Then 100 generated definitions, each changed by `DefinitionMutator` as editors change them: the comparison swapped mirrors it, names each element once, and each changed element's property changes rebuild its newer properties from its older ones. Also when a save changes the actions ([ADR-0051](../docs/decisions/0051-form-endpoints.md)): an action, or a rule of one added, changed or moved in or out, and nothing else. |
| `DefinitionKeysTests` | The element keys of a definition ([ADR-0046](../docs/decisions/0046-copies-templates-and-import.md)), the same as the model's by reflection over the fixture and 500 generated definitions; renewing them and mapping them back gives the same bytes; every reference follows, in rules, checks, the verification field and settings (as values, property names, in any letter case and in lists), but not a key inside a longer text, another format or the key of something outside the form. |
| `ActionCopiesTests` | Copied actions: new keys, fields mapped by alias in any letter case, grid rows by a single matching label, what is not mapped kept and reported with its path, and the target's limits. |
| `FormExportFormatTests` | The export envelope: a round trip, two-space indentation with line feeds and ASCII escapes, `schemas/jfb-form-export.v1.schema.json` (which refuses what the reader refuses, unless a schema cannot say it), the format checked first, every refusal with its path, the definition measured in its canonical text, an older definition upgraded, and a file with thousands of bad option lists answering its first 100 problems. |

A new property of the model needs the record, the schema, the generator and the fixture: until all four agree, the
coverage, schema and fixture tests fail.

## The Testing project

| Folder | What it gives |
| --- | --- |
| `Time/` | `TestTimeProvider` (a clock moved by hand), `ManualTimers` (timers that fire when told) and `TestClock` (the real time plus an offset, for test hosts). |
| `People/` | `TestPeople` creates user groups, users, member groups and members, and `NewPassword()` makes passwords per run; `TestAccess` gives a group its global grants and creates a restricted folder for it; `TestPeopleSeeder` creates a host's configured people, grants and restricted folders at start-up. |
| `Mail/` | `TestMailbox` reads and waits for the `.eml` files in a host's mail pickup folder. |
| `Stub/` | `StubServer`, the local HTTP stub: webhooks, captcha verification (`StubCaptcha` tokens) and scripted answers, with every call recorded. |
| `Endpoints/` | The test-only endpoints under `/jfb-testing/`. |
| `Startup/` | `StartupMarkerPopulator`, which logs a line once Umbraco has rebuilt its indexes, for the browser tests' start-up wait. |
| `Settings/` | `SampleSettings`, the settings tests' section (below), and `SettingsJson.Global`, a section's global row as the settings service writes it. |

A host turns these on in the `JironFormBuilderTesting` section; everything is off by default:

| Setting | Effect |
| --- | --- |
| `Endpoints` | Answers `/jfb-testing/status`, `/jfb-testing/clock` and `/jfb-testing/access` (each backoffice user's effective permissions, globally, at the root and in each root folder), only in Development and only for requests from this machine. |
| `Clock` | Runs the host on `TestClock`. |
| `StubPort` | Starts the stub on that port (5560 for the browser tests' host), only in Development. |
| `StartupMarker` | Logs `Jiron Form Builder test host: indexes rebuilt` after each rebuild of Umbraco's indexes. |
| `UserGroups`, `Users`, `Members` | The people the host creates at start-up, each group with its global `Permissions`; their passwords come from the environment only. |
| `RestrictedFolders` | Root folders the host restricts at start-up, each to one group (`Name`, `Group`, `Permissions`); a folder of that name in the trash is left there and a new one made. |

## The schema tests

`Tests.Integration/Persistence/` runs the migration plan on a fresh database per test and reads the schema back with
queries of its own (`SchemaInspector`), comparing it with `ExpectedSchemaV1`, which is written out by hand from §4.5
and [ADR-0033](../docs/decisions/0033-schema-v1.md). The same tests run against SQL Server in the `sqlserver` CI job.
Two of them compare with files under review, and rewrite them when `JFB_UPDATE_SNAPSHOTS=1` is set (and then fail, so
the diff gets read and committed; the management API's document is the third such file, below):

- `SchemaV1DdlTests`: the SQL Server statements of step 1, rendered without a server, in
  `Persistence/schema-v1.sqlserver.sql`;
- `DatabaseDocsTests`: the diagram in [docs/database.md](../docs/database.md).

## The repository tests

`Tests.Integration/Persistence/Repositories/` migrates a fresh database per test, with a clock that stands still
(`RepositoryTestBase`). The tests cover:

- each repository's reads and writes, and every refusal, with nothing written (for entries, the number too). A
  refusal must come from the statement's own condition: `AssertRefused` fails when the unique index refused instead;
- the unique index as the backstop: `CommandRewriter` (in `StatementInterceptors`) takes the condition out of one
  statement, so the test shows the index's refusal classified as the condition's would be;
- writers at the same moment: of ten saves from one version, exactly one gets through; fifty entries stored at once get
  the numbers 1 to 50, each once, and ten retries of one submission store one entry;
- what option lists, themes and email layouts share, written once in `AliasedLibraryRepositoryTests` and run for each
  of the three;
- units of work that commit or roll back together;
- the reruns after SQLite's real busy error;
- the entries query builder (`EntryQueryRepositoryTests`): every filter kind with the 1.3 spike's numbers and dates,
  text compared as rules compare it (`ß` and `ss` apart, texts longer than their key), the sorts and their keyset
  pages, the counts, and a list past its time budget while a submission comes in;
- SQLite's plans of every shape the query builder writes, with and without planner statistics: no scan of entries
  or values, and no temporary b-tree for a sort by a value (`EntryQueryPlanTests`), with a walk stripped of its
  unary plus that the check must catch;
- SQLite's plans of the statements the indexes of 4.8a serve (`IndexPlanTests`): the statements a repository really
  issues, recorded by `StatementRecorder` (in `StatementInterceptors`) and explained with their values, each naming
  its index and scanning nothing: an entry's files and the uploads that wait for one, one form's daily statistics
  and their delete, retention by day, and a content item's references; a create's checks of its alias and its
  key, each a search of its own unique index, never one search over both (4.8b); and one form's uploads that wait
  for an entry, for its delete, sought on both columns of the index that leads with the form (4.8g);
- stored names: a state written in another letter case is refused when read, as a row something else wrote, and
  `Tests.Unit/Persistence/StoredNamesTests` shows every stored enum written as the camel case of its names, read back
  only from that text, and fitting its column; a scan state or an option list kind in another letter case is
  refused as well, since every stored name is a camelCase word (4.8g);
- the outbox (`ActionRunRepositoryTests`, `EmailRepositoryTests`): every state change, a claim once (ten claims at
  once, which SQLite's gate takes in turn; the two-processor test is 8.1's), a token of each claim's own, outcomes
  only with the claim's token and from the states they need, the order of a batch and "stop the rest" within it,
  leases that run out and the late finisher, retries from the log, undo, the purge and an entry's deletion
  (ADR-0007, ADR-0040);
- the audit log (`AuditRepositoryTests`): entries that commit and roll back with their change and come back as
  stored, the filters and pages newest first, a view recorded once per window (also twenty at once, and again after
  a unit that rolled back), the purge oldest first, a filtered list past its time budget, a budgeted query stopped
  through its unit answered as cancelled, one inside another that keeps its own caller's token, and an entry full of
  personal data through every kind of action about entries, after which `Support/AuditLogSearch` finds none of its
  values anywhere in the log (ADR-0041);
- the audit log's search itself: it finds a value in a column, in the details and escaped, ignoring case, and never
  inside a key;
- consent wordings (`ConsentTextRepositoryTests`): stored once under the hash of their culture and text, first seen
  when first stored, and a writer at the same moment answered by the stored wording;
- daily statistics (`DailyStatRepositoryTests`): counts by day, metric and dimension, 400 increments and 100 batches
  at once from two repositories while fields are added to the form, a day's first counts at once, a form that does
  not exist (also with values of it left), counts racing their form's delete, the caller's unit, a form's delete and
  the purge a form and a day at a time (ADR-0041). SQLite runs one write at a time, so only the SQL Server run in CI
  shows that the parallel ones never deadlock;
- the trash and the delete (`TrashRepositoryTests`, ADR-0047): the forms of a folder's tree going to the trash with
  its key and coming back with it, a form held in the trash at its version, the entries of a form lowest number
  first with their files, the uploads that wait for an entry, a batch's unsent mail, and a form's references.

`Tests.Integration/Files/PhysicalFileStoreTests` runs the file store as a site gets it, in a folder under
`%TEMP%\jfb-files-*`: a file per id in its shard, read asynchronously from the stream, a delete during a download,
and saves that are cut off, cancelled, repeated or made while another save of the id writes, none of which leaves
anything behind or replaces a file, not even one that appears while the save writes. A partial file or a deleted
file's bytes that cannot be deleted are logged without their path and left for retention (ADR-0006, ADR-0041).

The unit tests (`Tests.Unit/Persistence/`) cover what a database cannot be made to do on cue:

- a commit that fails on its way, a lost connection, and the waits between attempts;
- the arguments each repository refuses, and how the outbox cuts and stores errors and attachments;
- the one trash check, which the services and the repositories share, with the refusal of a restore whose landing
  changed (`LibraryTrashTests`), and where forms and folders go and land (`Tests.Unit/Forms/LibraryFoldersTests`);
- every update of a row that keeps who changed it last requires that user, in its record and in every repository
  method (`RepositoryUserTests`);
- the file store's root (`Tests.Unit/Files/FileStoreRootTests`), with a site whose files would lie in its web root
  failing its start-up validation, a store of the site's own, registered before the package or after it, in the
  default's place, device paths and streams refused, and real junctions or symbolic links followed into, around and
  out of the web root (`FileStoreRootLinkTests`, in `%TEMP%\jfb-links-*`); and audit details that take no text, down
  to every method and field of the type (`Tests.Unit/Audit/AuditDetailsTests`);
- the default folder on more than one server (`Tests.Unit/Files/FileStoreLoadBalancingCheckTests`): an error naming
  the setting for a subscriber or beside other active servers, none for a single server (the elected publisher of
  one included, without reading the registrations), a root the host set however it is written, a store of the site's
  own or an Umbraco that does not run, and registrations that cannot be read only as a warning;
- the entry text key against the rule vectors of `tests/vectors`: two texts share a key exactly when an `equals`
  vector says they match (`EntryTextKeyTests`);
- the query builder's statements and refusals (`EntryQuerySqlTests`), and the time budget of a read: a timeout that
  never runs again, a caller's cancellation, a read inside another (`FormBuilderDatabaseTests`);
- units inside units (`FormBuilderDatabaseTests`): an inner unit keeps the outer unit's cancellation and deadline and
  adds its own caller's, and a write inside a read is refused; the migration state read again after a busy read
  (`FormBuilderMigrationStateTests`);
- statement text on SQL Server, with names from `TestSqlNames`: the due list of action runs reads without lock hints
  and the claim with them (`ActionRunSqlTests`); and the numbering of a statement's arguments (`PersistenceHelperTests`);
- the product rows against the migration's snapshots.

`Architecture/CoreDatabaseConnectionTests` reads Core's IL (through `IlReader`) and fails on any connection of Core's
own (ADR-0036). `Architecture/StoredEnumTests` reads the IL and the source of Core's persistence and fails on an enum
parsed, turned into text, boxed or named with `nameof` anywhere but `StoredNames` (ADR-0033, 4.8g).
`Architecture/StaticDataTests` reads every static field of Core and Web and fails on one that is not read-only or that
holds anything but what cannot change, all the way down: values, frozen and immutable collections, read-only
serializer options and contexts, and the package's own types with read-only fields (ROADMAP 4.8f, 4.8h). A test of
its own samples shows it finds a concurrent dictionary, options that can change and a list behind a read-only view.
`Architecture/QueryBuilderReadsTests` reads the query builder's source and fails on any database call but its one
budgeted read, or on SQL that changes rows (ADR-0039).
`Architecture/SqlGuardTests` reads the repositories' source and fails on a guard without a locking read (ADR-0038):
the hints are empty on SQLite, so no database test here can tell. The few checks that read without a lock on purpose
are listed there, each with its reason.

## The settings tests

`Tests.Unit/Settings/` covers the backoffice sections without a host (ADR-0043): a section's JSON (camelCase, enums
as text, nulls left out, unknown properties dropped), each migration step and a missing or doubled one, how a site row
combines with the global row, the changed field paths, the host rules of every kind and a save's lock check, every
validator and default (`general`'s time zone: IANA ids pass; Windows names, offsets, untrimmed text and another letter
case do not, even once the exact spelling is cached; the consent default's texts, refused as a definition refuses them),
the cache keys and what a refresher instruction clears.
`Testing/Settings/SampleSettings` is a section with every kind of field (texts, a number, an enum, a nested object, a
list, a `[GlobalOnly]` field, one stored under a name of its own) and what is no field (a worked-out value, an
indexer), two migrations, a validator that normalises and checks across fields, and a host rule whose locks a test
sets.

`Tests.Integration/Settings/` runs the settings service on a real site, with a fresh database per test: saves
globally and per site with their versions, stale saves, audit entries and notifications (a failing handler too); old
rows migrated and rewritten; unreadable rows; host locks of every kind and a whole locked section
(`LockedSectionTests`); a save refused inside a caller's unit of work or an Umbraco scope; and the cache with its
loading token and the refresher, also as the JSON instruction other servers get. `Tests.Unit/RuntimeCacheLoadsTests`
covers the load that the settings and the access snapshot share: misses that come together read once, a change
during the load or as it ends keeps it from being kept, a failed load (also one that throws before its first await)
is cleared, a caller that stops waiting leaves the load to the others, and the load runs outside its first caller's
unit, cancellation and async-local state (4.8g). Umbraco's test host runs without
caches (`NoAppCache`), so `SettingsServiceTests` gives its site a runtime cache, as a real site has, and a server
messenger that records the instructions a save sends instead of delivering them (`RecordingServerMessenger`).

## The form service tests

`Tests.Unit/Forms/` covers the parts of the form service without a host (ADR-0044): aliases from names (accents,
spelled-out letters, camelCase, a leading digit, names without Latin letters, the length) and the lowest free number
in any letter case, the alias cut so its number keeps it within the longest alias; the field catalogue of a definition
and what a publish adds, updates, brings back and ends, with the storage kind of every built-in type written out apart
from the code; the code of every refusal and conflict; the notifications' arguments (also of the folder service,
`FolderNotificationTests`) and the structural publish check; the name rule of forms and folders (`LibraryNamesTests`);
and the built-in templates (`BuiltInFormTemplatesTests`: one per file, each reading with its key, publishing and
having English and German texts) with the package version an export names.

`Tests.Integration/Forms/` runs the service on a real site, with a fresh database per test: a create from blank with
and without the consent default (and with a stored default a definition would refuse), aliases and their races,
folders outside the trash, draft saves that change something or nothing, stale changes and a change between the
checks and the write, publishes with the catalogue, the theme and the public revision, every publish check at once,
unpublish, rename and move, the trash, cancelling and failing handlers, the order of the notifications, the audit
entries, and a change refused inside a unit of work or an Umbraco scope. `FormNotificationRecorder` records every
notification and whether a unit was open, and cancels, fails or changes the form when a test says so;
`TestPublishCheck` is a second publish check whose problems a test sets. `FormServiceLimitTests` lowers the pages a
definition may have, so a form saved before still opens and its save returns `limit_exceeded`, as do a restore of a
version above the limit, a duplicate of it, copied actions into it, a saved template and an import above it.

`Tests.Integration/FormBuilderChangesTests` runs the change pipeline every service of Core shares (ADR-0053) on a real
site: a change refused inside a unit of work or a scope with its service's reason, and by the pipeline itself when it
skipped that check, writing nothing; a write that commits with its audit
entries in the order given (none for a write that found nothing), a write whose entry cannot be made leaving nothing,
a cancelling handler's messages, and a handler that fails after the commit logged as the service's with what changed.
The three parts of the form service (the lifecycle with the trash, `IFormBuilderFormVersionService` and
`IFormBuilderFormExchangeService`) are tested below, each with what it does.

`FormCopyServiceTests` covers copies, templates, imports and exports (ADR-0046) on a form designed with every kind of
reference: a duplicate whose rules, checks, verification field and action settings name its own elements, with and
without actions; copied actions mapped by alias with the unmapped ones reported and refused at publish; forms from the
Contact template and from saved templates, with the consent default; an export of the draft or a version with its
option lists that imports as a new form, a theme the site lacks, option lists by key, by alias or missing; and every
refusal, notification and audit entry (an import's notes named once per code, with their counts), a reimport of the
longest alias numbered within it, and each operation refused inside a unit of work or a scope.

`FormVersionServiceTests` covers a form's versions (ADR-0045) on a history of six versions: the History of publishes
or of every version with what changed into each, paged, one version, two compared either way, a restore that creates a
new version (or writes nothing when the draft holds it already) with its notifications and audit entry, pins and
comments that leave the form's row alone, and every refusal. `Tests.Integration/Retention/` runs the draft-version
cleanup by the history settings, with the versions it must keep, its rounds, its deadline and its audit entries, and the
retention job that runs it; `Tests.Unit/Retention/` covers the job's own rules without a host.

`FormTrashServiceTests` covers the trash and the permanent delete of a form (ADR-0047): a trash and a restore into its
folder or, when that is in the trash, the root, refused with `landing_changed` when that changed after the check (the
repositories refuse it too); stale changes; a delete that removes the entries with their values, files, events and
notes, the versions, catalogue, grants, references and daily statistics, unsent mail and the bytes of the files (with
`Support/MemoryFileStore` as the site's file store), keeping sent mail and other forms; a delete recorded as begun
(`form.deleting`) with its first batch that removes something, one that stops after its first batch and finishes when
run again, bytes that cannot be deleted, a late entry and entries that keep arriving until `in_use`.
`FolderServiceTests` covers the folder service on the same rules: create, rename and move with every refusal, a trash
and a restore that take exactly what went (a folder and a form trashed before stay), a subfolder restored on its own,
a restore refused when where it lands changed after the check, the delete of a folder's tree, emptying the trash, and
that none of these publishes notifications of the forms it takes.

## The access tests

`Tests.Unit/Access/` covers the permission model without a host (ADR-0013, ADR-0048): the flags' names in any letter
case, what each needs and where each may be granted (`PermissionRulesTests`); how a user is read, the administrators by
Umbraco's alias and the Forms section matched exactly (`AccessUserTests`); and the evaluation on a tree with restricted
folders, a ring and unknown folders, globally, per folder and per form, the lists' scope and visibility, and what a move
gives each group (`AccessSnapshotTests`). `Tests.Unit/Persistence/AccessSqlTests` covers the lists' clause and its
limit of groups.

`Tests.Integration/Access/` runs the access service on a real site, with a fresh database per test. `AccessPersonas`
creates the personas of ADR-0013, one group and one user each, with their global grants, and `AccessListTests` lists
forms and folders as each of them over a library with a restricted folder for a separate group, a restricted form and
a form in the trash, paged and counted. `AccessServiceTests` changes grants and restrictions with their audit entries,
refusals and stale versions, deletes a user group through Umbraco, and shows that the cached evaluation follows every
change of access and of the tree, while a folder's rename keeps it, and that a change, a deleted group's grants
included, is refused inside a caller's unit of work: it gives the site a runtime cache, the recording server
messenger, and `HookedPermissionRepository`, which lets another server change a grant while the snapshot loads or a
change writes. `AccessLockTests` locks the section, and `HostGrantsBootTests` boots a site whose host sets the global
grants.

The authorization policies ([ADR-0049](../docs/decisions/0049-authorization-policies.md)) are tested twice.
`Tests.Unit/ManagementApi/FormBuilderAuthorizationTests` covers their names, scheme and requirements, the resources
and denials, and both handlers against a substitute access service: the section first, then what is held, and a
folder or form the user cannot see answering as not found. `Tests.Integration/Access/AccessPolicyTests` signs each
persona in through Umbraco as an API user with a client secret (`Support/BackOfficeApiUsers`) and has
`AccessProbeController`, a controller of the test assembly, authorize every policy globally, at the root, on folders
(the restricted one, and one inside it that another group holds on its own) and on forms, against a table written
out by hand; it also calls actions guarded by `[Authorize(Policy = ...)]` without a token and as personas with and
without the section or the permission. `Testing/TestPeopleTests` seeds the grants and restricted folders, again,
next to a folder of that name in the trash or inside another, for one of Umbraco's groups alone and with broken
settings, and `Testing/TestAccessEndpointTests` reads them back through `/jfb-testing/access`.

## The management API tests

`Tests.Integration/ManagementApi/` calls the management API on a real site as the personas of ADR-0013, each an API
user signed in through Umbraco's token endpoint on the first call (`ManagementApiTestBase`, `ManagementApiCaller`)
([ADR-0050](../docs/decisions/0050-management-api-base.md)). `ManagementApiEndpoints` lists every endpoint through
the API explorer and fills its route, query and body by type, so the permission check, not routing, answers; the
tests that use it call a new endpoint by themselves:

| Tests | What they show |
| --- | --- |
| `PermissionMatrixTests` | Each endpoint's declaration against a table written out by hand; each endpoint as the outsider (`403 no_section`), as a persona who sees the folder or form but lacks the permission (`403 forbidden` naming the first missing one), as one who holds nothing on the restricted folder or its form and with a key that names nothing (`404`), and the section-wide ones as a user of the section without grants. Each test writes the endpoints it called, with their answers, to its output. |
| `PendingMigrationsApiTests` | Every endpoint answers `503 migrations_pending` with `Retry-After` while the plan has not run (the site boots without it), and normally once it ran; the test writes each answer to its output. |
| `RefusalBodiesApiTests` | Each kind of refusal through Umbraco's pipeline, from a real call: `application/problem+json`, the status in the body, the code, a title for editors and what the kind adds (the problems, the permission, a handler's messages). |
| `TreeApiTests` | The tree and the trash as each persona sees them, folders first, with the user's permissions and `hasChildren` (only for what the user sees, in the folder's own state of the trash); paging across folders and forms, at the root and in the trash too; a folder in the trash holding what went with it; no key as the root; pages outside the limits. |
| `FolderApiTests` | Each folder endpoint's answer and change: the permission checked before the request's values, `design` on a move's new parent, `managePermissions` and a confirmation for a move that widens access, `design` and `deleteEntries` on everything a permanent delete removes (restricted folders and forms below it included), an emptied trash that keeps what the user may not delete, and a restore to the root (its parent in the trash) that needs what a move there needs. |
| `FormApiTests` | Each form endpoint's answer and change: a search of what the user sees by name or alias, the form for anyone who sees it and the draft's document for `design`, a save of the document with its version (or none when nothing changed), problems with paths into the document and `409` for a stale version, `manageActions` for a save that changes the actions, publish and unpublish, rename, a move's new folder and its widened access, a restore to the root that needs what a move there needs, and a delete for good with `deleteEntries`. |
| `VersionApiTests` | The History of a form with four versions: the publishes or every version, newest first, with what changed and paged; a version with its document; a comparison with each property before and after; a restore into a new draft that needs `manageActions` when it changes the actions, after the concurrency check; pins and comments that leave the form's concurrency version alone; `404 version_not_found`. |
| `RestoreLandingApiTests` | A form's and a folder's restore whose folder comes back from the trash after the endpoint checked where it lands and before the write (`RestoreActionHook`, an action filter after every other, arms `HookedFolderRepository` to bring it back right after the action read it): `409 landing_changed`, and the form or folder stays in the trash where it was. |
| `OpenApiSnapshotTests` | The document `jfb` against `docs/api/jfb.json` (rewritten with `JFB_UPDATE_SNAPSHOTS=1`, then fails), its paths and operation names, and schemas that are only its own models, never a framework type described by its properties. |

`Tests.Unit/ManagementApi/` covers the same without a host: the access filter's order, refusals and fail-closed
cases, how it reads a key (`FormBuilderAccessFilterTests`); every answer to a refusal, the exception filter, the
declarations, the operation and schema names and the feature provider (`FormBuilderProblemsTests`); the models of the library
(`LibraryModelsTests`) and of the versions, with the stable names of their kinds (`VersionModelsTests`); and the
policies and handlers (`FormBuilderAuthorizationTests`). In the client,
`packages/backoffice/scripts/generate-client.test.mjs` generates the client from the snapshot next to the committed
one and fails on any difference: after a change to the API, run the snapshot test with `JFB_UPDATE_SNAPSHOTS=1`, then
`npm run generate-client -w @jfb/backoffice`, and commit both. `packages/backoffice/src/hey-api.test.ts` shows that
the generated client starts from the backoffice's own HTTP client, whose settings win.

## Writing tests

- Name C# tests `Method_Situation_Expected` and assert with NUnit's constraint model.
- No sleeps to wait for state: poll with a deadline, as `TestMailbox.WaitForAsync` does, and use the fake clocks where
  time matters.
- Generate passwords with `TestPeople.NewPassword()`; test values that look like secrets carry the marker `JFB_FAKE`.
- Integration fixtures set their site's configuration on `InMemoryConfiguration`: in the constructor of an
  `UmbracoIntegrationTest`, before `base.Setup()` in an `UmbracoTestServerTestBase`. Never set a key on
  `builder.Config`: the test host chains one configuration into every fixture's site, so the key would reach every
  fixture that runs later, and `Support/SharedConfigurationTests` fails on it. Capture logs with
  `builder.Services.AddLogCapture()`.
- A test server fixture that calls the Management API derives from `Support/FormBuilderTestServerTestBase` and boots
  the runtime (`[UmbracoTest(Boot = true)]`): it runs Jiron Form Builder's migration plan after the host starts, and
  its `ConfigureHost` sets the configuration before the host is built.
- Read files of the repository in place through `RepositoryRoot.Path(...)`.
