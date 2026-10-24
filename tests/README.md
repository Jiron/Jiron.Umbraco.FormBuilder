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
| `Jiron.Umbraco.FormBuilder.Tests.Unit` | NUnit 3 and NSubstitute, no Umbraco host: the architecture tests (Core knows nothing about HTTP), the vector runner and the tables check for Core's shared logic (`Logic/`), the definition model and its JSON (`Definitions/`, below), the host configuration with its start-up checks and IntelliSense schema (`Configuration/`, below), and the Testing project's own tests. |
| `Jiron.Umbraco.FormBuilder.Tests.Integration` | A real Umbraco (`Umbraco.Cms.Tests.Integration` 17.x) with Jiron Form Builder's composers: the boot test, the people factories against real services, the test-only endpoints on a real site, the schema and its migrations, and the repositories (`Persistence/`, below). |
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
| `DefinitionSchemaTests` | `schemas/jfb-form.v1.schema.json` validates the generated definitions and the fixture, names the same objects, properties, required ones and words as the model (`ModelProperties`), and refuses the refusals a schema can say. |
| `UpgradeTests` | A synthetic older version read through the real reader, and the chain's checks. |
| `DefinitionValueTests` | Localised texts, default values, settings objects, instants and the string escaping. |
| `StructureTests` | A document that publishes, and each publish refusal with its code and JSON path as one change to it (aliases that differ only in letter case and option values that fold to one text included); what publishes; that a save refusal comes first; and 500 generated definitions checked without throwing, every path naming a part of them. |
| `FieldTypeSupportTests` | §4.3.2 restated apart from the code: each built-in type refuses exactly the common properties, checks, rules and messages it does not support. |
| `RedirectUrlTests` | The redirects that could leave the editor's site, and those that stay on it. |
| `ProjectionTests` | 500 generated definitions with a marker in every private place project without it; the projection names only allowlisted properties; `fixtures/definitions/v1-full.public.json` is the reviewed projection of the fixture; which changes move `PublicHash` and which do not. |

A new property of the model needs the record, the schema, the generator and the fixture: until all four agree, the
coverage, schema and fixture tests fail.

## The Testing project

| Folder | What it gives |
| --- | --- |
| `Time/` | `TestTimeProvider` (a clock moved by hand), `ManualTimers` (timers that fire when told) and `TestClock` (the real time plus an offset, for test hosts). |
| `People/` | `TestPeople` creates user groups, users, member groups and members, and `NewPassword()` makes passwords per run; `TestPeopleSeeder` creates a host's configured people at start-up. |
| `Mail/` | `TestMailbox` reads and waits for the `.eml` files in a host's mail pickup folder. |
| `Stub/` | `StubServer`, the local HTTP stub: webhooks, captcha verification (`StubCaptcha` tokens) and scripted answers, with every call recorded. |
| `Endpoints/` | The test-only endpoints under `/jfb-testing/`. |
| `Startup/` | `StartupMarkerPopulator`, which logs a line once Umbraco has rebuilt its indexes, for the browser tests' start-up wait. |

A host turns these on in the `JironFormBuilderTesting` section; everything is off by default:

| Setting | Effect |
| --- | --- |
| `Endpoints` | Answers `/jfb-testing/status` and `/jfb-testing/clock`, only in Development and only for requests from this machine. |
| `Clock` | Runs the host on `TestClock`. |
| `StubPort` | Starts the stub on that port (5560 for the browser tests' host), only in Development. |
| `StartupMarker` | Logs `Jiron Form Builder test host: indexes rebuilt` after each rebuild of Umbraco's indexes. |
| `UserGroups`, `Users`, `Members` | The people the host creates at start-up; their passwords come from the environment only. |

## The schema tests

`Tests.Integration/Persistence/` runs the migration plan on a fresh database per test and reads the schema back with
queries of its own (`SchemaInspector`), comparing it with `ExpectedSchemaV1`, which is written out by hand from §4.5
and [ADR-0033](../docs/decisions/0033-schema-v1.md). The same tests run against SQL Server in the `sqlserver` CI job.
Two of them compare with files under review, and rewrite them when `JFB_UPDATE_SNAPSHOTS=1` is set (and then fail, so
the diff gets read and committed):

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
- the outbox (`ActionRunRepositoryTests`, `EmailRepositoryTests`): every state change, a claim once (ten claims at
  once, which SQLite's gate takes in turn; the two-processor test is 8.1's), a token of each claim's own, outcomes
  only with the claim's token and from the states they need, the order of a batch and "stop the rest" within it,
  leases that run out and the late finisher, retries from the log, undo, the purge and an entry's deletion
  (ADR-0007, ADR-0040);
- the audit log (`AuditRepositoryTests`): entries that commit and roll back with their change and come back as
  stored, the filters and pages newest first, a view recorded once per window (also twenty at once, and again after
  a unit that rolled back), the purge oldest first, and an entry full of
  personal data through every kind of action about entries, after which `Support/AuditLogSearch` finds none of its
  values anywhere in the log (ADR-0041);
- the audit log's search itself: it finds a value in a column, in the details and escaped, ignoring case, and never
  inside a key;
- daily statistics (`DailyStatRepositoryTests`): counts by day, metric and dimension, 400 increments and 100 batches
  at once from two repositories while fields are added to the form, a day's first counts at once, a form that does
  not exist (also with values of it left), counts racing their form's delete, the caller's unit, a form's delete and
  the purge a form and a day at a time (ADR-0041). SQLite runs one write at a time, so only the SQL Server run in CI
  shows that the parallel ones never deadlock.

`Tests.Integration/Files/PhysicalFileStoreTests` runs the file store as a site gets it, in a folder under
`%TEMP%\jfb-files-*`: a file per id in its shard, read asynchronously from the stream, a delete during a download,
and saves that are cut off, cancelled, repeated or made while another save of the id writes, none of which leaves
anything behind or replaces a file, not even one that appears while the save writes. A partial file or a deleted
file's bytes that cannot be deleted are logged without their path and left for retention (ADR-0006, ADR-0041).

The unit tests (`Tests.Unit/Persistence/`) cover what a database cannot be made to do on cue:

- a commit that fails on its way, a lost connection, and the waits between attempts;
- the arguments each repository refuses, and how the outbox cuts and stores errors and attachments;
- the file store's root (`Tests.Unit/Files/FileStoreRootTests`), with a site whose files would lie in its web root
  failing its start-up validation, device paths and streams refused, and real junctions or symbolic links followed
  into, around and out of the web root (`FileStoreRootLinkTests`, in `%TEMP%\jfb-links-*`); and audit details that
  take no text, down to every method and field of the type (`Tests.Unit/Audit/AuditDetailsTests`);
- the entry text key against the rule vectors of `tests/vectors`: two texts share a key exactly when an `equals`
  vector says they match (`EntryTextKeyTests`);
- the query builder's statements and refusals (`EntryQuerySqlTests`), and the time budget of a read: a timeout that
  never runs again, a caller's cancellation, a read inside another (`FormBuilderDatabaseTests`);
- the product rows against the migration's snapshots.

`Architecture/CoreDatabaseConnectionTests` reads Core's IL and fails on any connection of Core's own (ADR-0036).
`Architecture/QueryBuilderReadsTests` reads the query builder's source and fails on any database call but its one
budgeted read, or on SQL that changes rows (ADR-0039).
`Architecture/SqlGuardTests` reads the repositories' source and fails on a guard without a locking read (ADR-0038):
the hints are empty on SQLite, so no database test here can tell.

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
- Read files of the repository in place through `RepositoryRoot.Path(...)`.
