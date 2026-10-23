# 0011. Testing strategy

- Status: accepted; the permission matrix (`403` and `404`) amended in 1.8
- Date: 2026-10-23
- Roadmap step: 1.7a (decided); built in 3.4 (the harness) and 3.5 (CI and the gate), extended by every later step

## Context

Every behaviour gets a test (CLAUDE.md quality bar). §4.12 asks for at least 85% line coverage on Core, every public
and management endpoint covered by integration tests, and every shared vector passing in both runtimes. D19 fixes the
tools: NUnit 3 (because `Umbraco.Cms.Tests.Integration` 17.x requires it) with its own constraints and NSubstitute,
Vitest, Playwright and axe-core. D22 forbids real third-party keys, so captchas, webhooks and AI run against stubs and
fakes.

**This machine:** SQLite only (no Docker, no LocalDB, and never a database on the SQL Server instance, CLAUDE.md
rule 6); Edge but no WebKit; GitHub Actions results are not visible from here, so `scripts/ci-local.ps1` is the gate.

**What earlier work taught:**

- **Tandem** ended with about 2,500 tests in five layers, green from a clean clone. Its harness and its gotchas are
  in [the lessons file](../research/tandem-lessons.md): the integration host's settings, sign-ins one at a time on
  SQLite, the start-up wait, the hang marker, and the slips that tooling prevents.
- **The spikes** added three habits:
  - shared JSON vectors run by both runtimes, with mutation sweeps that prove they catch broken operators
    (ADR-0005);
  - a concurrency test that is shown to catch doubles by a known mutant (ADR-0007);
  - measured budgets written into the ADRs (ADR-0003, ADR-0004, ADR-0007).
- **"Flaky means broken"** ("How to use"): browser suites run with `retries: 0`, and only the SQLite start-up hang
  and a crash of the runtime itself earn one rerun, noted in WORKLOG.

## Options

1. **Mostly end-to-end tests against the demo.** They prove the most, but they are slow and localise failures badly.
2. **A pyramid:** many fast unit tests; integration tests on a real Umbraco with a real database; client tests;
   shared vectors in both runtimes; and a focused set of browser journeys, install tests and load tests.
3. **Mocks everywhere,** Umbraco's services included. Fast, but it tests the mocks, and Umbraco's behaviour (scopes,
   migrations, notifications after commit, cache refreshers, the job runner) is exactly what Jiron Form Builder must
   get right.

## Decision

**Option 2.** Every layer has its tools, its scope and its gate.

| Layer | Tools (licence) | Scope | Runs |
| --- | --- | --- | --- |
| **Unit** (`tests/Jiron.Umbraco.FormBuilder.Tests.Unit`) | NUnit 3.14 with its constraint model (MIT), NSubstitute (BSD-3-Clause), coverlet (MIT) | Pure logic with a fake `TimeProvider`: the definition model, upgraders and structural validation; rules, validation, merge tags and calculations; field and action types; the spam score; the settings validators and host rules; the sanitiser's configuration; the SSRF guard; exports and the formula guard. | every build, under a minute |
| **Vectors** (`tests/vectors/*.json`) | the NUnit and Vitest runners of ADR-0005 | Rules, validation, merge tags and calculations in the `jfb-vectors/1` format; a case that passes in one runtime and fails in the other fails the build. The tables check compares fold, clean and recomposition for every code point. | every build |
| **Integration** (`tests/Jiron.Umbraco.FormBuilder.Tests.Integration`) | `Umbraco.Cms.Tests.Integration` 17.x (MIT; it brings Moq, AutoFixture and Bogus), `UmbracoIntegrationTest`, `UmbracoTestServerTestBase` | A real Umbraco with Jiron Form Builder's composers: migrations (fresh, rerun, upgrade from fixtures), repositories and the entries query shapes, the submission pipeline, the outbox with two processors, settings across simulated servers, every public and management endpoint (success, validation, `401`, `403` or `404` for every role as ADR-0013 sets, rate limits, CORS, size limits), file uploads and downloads, logs captured and checked for personal data. | SQLite locally and in CI; **SQL Server in a CI job** |
| **Client** (`Client/packages/*`) | Vitest (MIT), jsdom (MIT) | The logic package, the website runtime's modules, the web component, and the backoffice's element logic and pure editor-model functions (the designer's reducers, ported from Ligata's tested editor model, §3.1). | every build |
| **Browser** (`tests/e2e`) | Playwright Test (Apache-2.0), `@axe-core/playwright` (MPL-2.0, an unmodified dev tool) | Journeys in a real browser against a fresh host: the website with and without JavaScript, phones, headless and cross-origin pages, the backoffice as every role, email files, privacy, resilience, accessibility. | the gate and CI |
| **Packages** (`scripts/install-test.ps1`) | the install harness (16.5) | A fresh site that installs the packed packages from the local feed: install, `dotnet publish`, the 17.5 floor and the upgrade from `baseline/schema-v1` (16.5, 18.3). | the steps that own them and each release |
| **Load** (16.2) | a Node load script against a host of its own | The §4.12 server budgets at stated concurrency, and where SQLite stops keeping up. | 16.2 and the release checks |

**Approval tests** keep reviewed snapshots: the OpenAPI documents `jfb-public` and `jfb` (`docs/api/`), the public C#
surface (15.1), the SQL Server DDL (4.1), the IntelliSense schema (ADR-0008) and the configuration reference. A change
fails the build until the snapshot is updated in the same commit, so every surface change is visible in review.

**Architecture tests** fail the build when a boundary of ADR-0001 is crossed: an HTTP or web assembly in Core, a
database connection constructed by Core (ADR-0007), a direct use of Umbraco's confirm modal in the backoffice
(§4.10), a test-only endpoint reachable outside a test host.

**Tests that must be shown to catch something.** Where a test guards an invariant that is easy to test badly
(concurrency, rule semantics, escaping), it is run once against a deliberately broken implementation and must fail.
The vector suites have their mutation sweeps (ADR-0005); the two-processor test has its mutant claim (ADR-0007, 8.1);
later steps name theirs.

**The Testing project** (`tests/Jiron.Umbraco.FormBuilder.Testing`, never packed, ADR-0001):

- **Time:** a test clock, a `TimeProvider` over it, and manual timers.
- **People:** seeders for the test roles (admin, designer, entry handler, viewer, privacy officer, no access) and two
  members (§4.11).
- **Test-only endpoints** under `/jfb-testing/…` (seed, move the clock, run a job now, age entries): their composer
  answers only on a test host, in Development, on loopback, and a test proves that a normal host has none of them.
- **Stubs and fakes:** the HTTP stub on 5560 (webhook calls recorded, URL option lists, captcha verification answers),
  reached only through the test `IFormBuilderHttpTransport` (§4.10); the fake AI provider for 15.3, strict about what
  it accepts; scripted scenarios for screenshots.
- **Builders** for definitions, entries and settings, so tests state only what they are about.
- **The mail reader:** decodes `.eml` files from the host's pickup folder and retries while a file is still being
  written.

**The integration harness** follows the lessons file (part 1): a direct `Microsoft.NET.Test.Sdk` reference, its own
`appsettings.Tests.json` (package migrations unattended off, `ModelsMode` `Nothing`, quiet logging, the test database
pool), one fixture per builder configuration, internal fixture classes with public test methods, a capturing logger
through `ILogger<>`, a messenger derived from `ServerMessengerBase` for cross-server tests, and
`dotnet test --blame-hang-timeout 10m`. Anything that brings tables of its own (the AI add-on with Umbraco.AI) gets an
integration project of its own.

**The browser harness** follows the lessons file (part 2), with Jiron Form Builder's ports (§4.11):

- **The host:** a fresh demo build on 5520 with its own content root under `.local/e2e/host-5520/` (database, mail
  folder, log), on the test clock. The global set-up waits for Umbraco's start-up work with a marker that appears
  whether the Content Delivery API is on or off (3.4), and starts the static site on 5521 and the stub on 5560.
- **Users:** passwords are new for every run and live only in `.local/credentials.json` and the host's environment.
  Sign-ins and password resets run one at a time across workers (a lock directory with PID takeover), because parallel
  sign-ins hang SQLite. Tests read only the error section of Playwright's error context, which can hold typed values.
- **The hang marker:** the teardown writes `database-hung.txt` when the host's database hung, and the gate reruns the
  browser projects once, only then.
- **Fixtures:** `loginAs`, `apiAs` (the PKCE flow), `waitForMail`, `fillForm`, `submitForm`, and one helper per area.
  The support code has node tests of its own.
- **Projects** (Edge locally through `channel: 'msedge'`, Chromium in CI, 4 workers, `retries: 0`): `smoke`;
  `website` (with `@visual` snapshots run locally only); `website-phone` and `website-iphone` (emulated in Chromium);
  `backoffice`; `privacy` (5522) and `resilience` (5524), which start hosts of their own or move the clock and run
  after the others; `ux` and `screenshots`, never in the gate; `lb`, in CI only (5500 and 5501 on SQL Server). Each
  project joins the config with its first spec. The load script (16.2) runs against its own host on 5523.
- **Accessibility:** axe on every page and state a journey reaches, with `iframes: false` and the preview frame's own
  document scanned separately; keyboard-only paths; layout tests that assert geometry at 375 px, not only presence
  (§4.13).

**Rules for every test:**

- **Deterministic:** no sleeps to wait for state (poll with a deadline), fake time where time matters, fixed ports,
  and generated data only from seeded generators.
- **Independent:** a fresh database per test or fixture, no order dependence.
- **Named for the behaviour:** `Method_Situation_Expected` in C#, sentences in Vitest and Playwright.
- **Assertions** use NUnit's constraint model (`Assert.That(x, Is.EqualTo(y))`); no FluentAssertions or other
  commercially licensed tools (CLAUDE.md rule 4).
- **A bug fix comes with the test that would have caught it.**
- **Security is first-class:** the permission matrix for every management endpoint (`403` where the user sees the
  resource but lacks the permission, `404` where they hold nothing on it, ADR-0013; route parameters filled by type, so
  a route never answers `404` for a malformed parameter before the permission check), escaping tests with hostile names and values in every
  output (Razor, email, backoffice, exports), and log checks for every path that handles values, tokens or file
  names (§4.10).
- **Bodies, not only statuses:** error tests check the ProblemDetails `code`, because Umbraco's error handling can drop
  a refusal's body and keep its status.
- **No real keys:** every outside service is a stub or a fake (D22). Real-key checks are OWNER-TODO items.

**The gate** is `scripts/ci-local.ps1`, run after `node scripts/fix.mjs`. Its stages, each judged by exit code
only: repository checks (secret scan, term guard, text check, workflows, licences, links, the Marketplace schema,
script tests); client (`npm ci`, lint, tests, build, size budgets); .NET (restore, a Release build with zero warnings,
`dotnet format --verify-no-changes`, the third-party notices); packages (pack, validation, the file allowlist); tests
(`dotnet test`, then the coverage merge and the 85% check on Core); the browser projects. It prints the time per
stage. **CI** (3.5) runs the same plus SQL Server and the load-balancing job, gated by `JFB_CI` until the owner turns
it on. A slow gate is fixed by making slow tests faster, never by skipping a stage.

## Consequences

- Most behaviour is proven in seconds without a browser; the browser suites prove the journeys, accessibility and the
  no-JavaScript path; the install and upgrade tests prove what sites actually get.
- The integration tests carry the Umbraco-specific risk, so an Umbraco update that changes scopes, notifications,
  migrations or the runner fails them rather than a site.
- SQL Server and two real hosts are proven only in CI, which the owner checks (OWNER-TODO); everything SQL Server
  specific that can be checked offline (the DDL snapshot, NULL handling in keys) is checked locally too.
- The stubs and fakes are products in their own right, with their own tests, and they never ship.
- **Revisit** when `Umbraco.Cms.Tests.Integration` moves to NUnit 4 (D19 changes with it), when CI results become
  visible from this machine, or when WebKit becomes available here.
