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
| `Jiron.Umbraco.FormBuilder.Tests.Unit` | NUnit 3 and NSubstitute, no Umbraco host: the architecture tests (Core knows nothing about HTTP), the vector runner and the tables check for Core's shared logic (`Logic/`), and the Testing project's own tests. |
| `Jiron.Umbraco.FormBuilder.Tests.Integration` | A real Umbraco (`Umbraco.Cms.Tests.Integration` 17.x) with Jiron Form Builder's composers: the boot test, the people factories against real services, and the test-only endpoints on a real site. |
| `Jiron.Umbraco.FormBuilder.Testing` | Test-only code for the tests and the test hosts, never packed. |
| `vectors/` | The shared vectors that Core and `packages/logic` both run (below). |
| `e2e/` | The browser tests: Playwright against a fresh demo host on 5520 ([e2e/README.md](e2e/README.md), [ADR-0031](../docs/decisions/0031-browser-harness.md)). |

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

## Writing tests

- Name C# tests `Method_Situation_Expected` and assert with NUnit's constraint model.
- No sleeps to wait for state: poll with a deadline, as `TestMailbox.WaitForAsync` does, and use the fake clocks where
  time matters.
- Generate passwords with `TestPeople.NewPassword()`; test values that look like secrets carry the marker `JFB_FAKE`.
- Integration fixtures set configuration on `builder.Config` in `CustomTestSetup` (an `UmbracoIntegrationTest`) or on
  `InMemoryConfiguration` before `base.Setup()` (an `UmbracoTestServerTestBase`), and capture logs with
  `builder.Services.AddLogCapture()`.
- Read files of the repository in place through `RepositoryRoot.Path(...)`.
