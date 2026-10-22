# Lessons from Tandem

What the owner's chat package Tandem (`C:\Code\Umbraco.Community.AI.Tandem`, finished 2026-10-22) learned on this machine, collected for Jiron Form Builder's build. [ROADMAP §3.3](../../ROADMAP.md#33-tandem-what-to-reuse-and-its-lessons) says what to port; this file says what to watch out for and how the test harness is shaped. Details and the reasons are in Tandem's `docs/WORKLOG.md` and ADRs (cited as "Tandem ADR-NNNN" and "Tandem X.Y" for its steps).

Read it in full in step 1.1. Before each step, look up the parts on what the step touches. Section signs (§) and plain step numbers refer to Jiron Form Builder's ROADMAP.md.

## 1. Gotchas on this machine

- **Integration tests:**
  - need a direct `Microsoft.NET.Test.Sdk` reference, their own `appsettings.Tests.json`, and `Umbraco:CMS:Unattended:PackageMigrationsUnattended=false`; otherwise a pending plan hangs the test host;
  - `UmbracoTestServerTestBase` needs `Umbraco.Tests.Integration.deps.json` in the output;
  - `appsettings.Tests.json` sets `Umbraco:CMS:ModelsBuilder:ModelsMode=Nothing`, because the InMemoryAuto handler threw and stopped every later start-up handler, migrations included;
  - the integration project references the meta package, because the test server validates the whole container;
  - `[ConfigureBuilder]` finds its method through `Type.GetType` from Umbraco's own assembly, so an external test project needs one fixture per configuration;
  - NUnit 3 runs only public methods, so fixtures whose cases take internal types are `internal` classes with public methods;
  - `dotnet test --blame-hang-timeout 10m` names a hanging test instead of stalling the gate;
  - Umbraco owns the logger factory, so tests capture logs by overriding `ILogger<>`;
  - moving the test clock also ages access tokens, so a test signs in again after moving it;
  - .NET 10 starts `BackgroundService.ExecuteAsync` through `Task.Run`, so stopping right after `StartAsync` reports Canceled.
- **The test host itself:**
  - It uses `NoAppCache` and a messenger that delivers nothing, and NUnit reuses fixture instances, so mutable fixtures need `FixtureLifeCycle(InstancePerTestCase)`.
  - Tandem's own test messenger called only `Refresh`, but Umbraco's domain and language refreshers work in `RefreshInternal`, so it derives from `ServerMessengerBase`.
  - `IUserService.Save` did not persist a rename there; use `UpdateAsync` and clear the user cache.
  - The navigation cache lags newly created roots, so tests read roots from the database. Domains match in sort order, so a hostname domain must come before relative ones.
  - A package that brings its own tables breaks a host that runs with package migrations off (Umbraco's Search writes on every save). Tests of such a package get a project of their own.
- **SQLite with Umbraco's default `Cache=Shared`:** concurrent read-then-write transactions fail with "database table is locked" and starve the thread pool. Route everything through one async gate and start writes with a write.
- **What the database reports back:**
  - Umbraco 17.7.1 reports SQLite column nullability inverted, and SQL Server's `GetDefinedIndexes` does not order index columns, so schema tests use their own queries;
  - text compares case-insensitively, so store hashes as lowercase hex;
  - dates come back `Unspecified`, so mark them UTC when reading;
  - in unique indexes SQL Server treats NULLs as equal and SQLite as distinct, hence the key rule in §4.5;
  - SQL Server stores Umbraco's `DateTime` as `DATETIME`, rounded to about 3 ms, so never compare stored times for equality, and break ordering ties by id;
  - once-only inserts on SQL Server need `UPDLOCK, HOLDLOCK`, or a single `INSERT … SELECT … WHERE NOT EXISTS`;
  - SQLite's `LOWER` folds ASCII only, so search and sort keys are normalised in C#.
- **SQLite under load:**
  - Umbraco's own writers, and any EF Core context such as Umbraco.AI's, hold the single write lock too. Gated commits then wait 100 to 800 ms under load, and more parallelism makes it worse (Tandem ADR-0063).
  - **Transient errors:** rerun the whole outermost unit of work on SQLite codes 5 and 6 and SQL Server 1205 and 1222. Never retry inside a commit or a nested scope (Tandem ADR-0065).
  - **The one-minute stall:** about 60 s after the first request, Umbraco 17 registers its distributed jobs and rebuilds its indexes. Parallel backoffice sign-ins at that moment hang the SQLite database. Part 2 below has the three-part fix.
- **Cache refreshers** reach other servers after up to about 2× `TimeBetweenSyncOperations` (about 9 s). `InstructionProcessJob` starts 1 minute after boot. Inside a request, the batched messenger writes instructions only at request end unless `SendMessages()` is called.
- **Parallel backoffice sign-ins hang on SQLite** in 17.7.1. E2E fixtures sign in one at a time (a lock file).
- **Auth and keys:**
  - Umbraco 17 keeps backoffice tokens in HTTP-only cookies; API clients send `Authorization: Bearer [redacted]`, and the cookie carries the real token;
  - Umbraco sets no Data Protection application name, so load-balanced servers must share the key ring and the name.
- **Email:**
  - Umbraco's `EmailMessage` has one body, so mail through `IEmailSender` is HTML only, with no multipart/alternative (Tandem ADR-0042, 0053).
  - While `CanSendRequiredEmail()` is false, Umbraco drops mail silently, so the outbox waits instead of handing it over.
  - Gmail drops `<body>` styles. Outlook ignores `max-width` and padding on links and divs, and needs `<!--[if mso]>` tables and `mso-padding-alt`. Put inline styles on table cells, and check a long word at 375 px.
  - Reading a pickup file that is still being written fails with `EBUSY`, so `waitForMail` retries.
  - Mail to addresses that visitors typed came up in four of Tandem's reviews: mail to strangers, no cap on confirmations, and no per-site cap. For a form builder it is an abuse surface from the first line (§4.10).
- **Management API, backoffice and OpenAPI:**
  - **Confirm modal:** Umbraco's confirm modal renders its strings as HTML, which was a stored XSS in Tandem (10.1). Every confirm goes through one helper with a Lit template (§4.10).
  - **The generated document and its tests:**
    - Umbraco's OpenAPI filter documents `403` itself, so a controller's own `403` attribute breaks the document;
    - an `IFormFile` parameter adds an implicit multipart constraint;
    - the `403` sweep must fill route parameters by type, or `int` routes answer `404` before the permission check.
  - **Error bodies:** Umbraco's error handling can drop a refusal's body and keep its status, and validation answers drop ProblemDetails extensions such as `code`. Test the body, not only the status.
  - **Polls keep sessions alive:** any Management API call after the access token expired renews the whole session, so a poll keeps an idle user signed in forever. Polls pause while the user is idle (Tandem ADR-0051). In tests, `page.clock` moves the backoffice past a token's end.
  - **Notification handlers:** one that cannot be constructed blocks the handlers after it. Resolve optional services lazily, inside a try.
  - **Inside the backoffice:**
    - the router cancels clicks on anchors inside the document, so downloads use a detached anchor;
    - `localize.termOrDefault(key, null)` tells a missing text from a present one (`term` returns the key itself);
    - the backoffice needs about 920 px, so tablet-portrait and phone widths are limited by Umbraco itself (§4.13).
  - **Static files:** Umbraco serves package files through `UseStaticFiles`, with no compression and no caching headers.
    - Tandem serves its own assets precompressed (Brotli and gzip) with a `?v=` fingerprint.
    - The precompressed copies are renewed only by `dotnet build`, so a run that skips the build serves stale files.
  - **The client from a running demo:** generating it there made the demo and the client wait on each other. Generate it from the committed OpenAPI snapshot instead (4.7).
  - **Plain-http sites** need `Umbraco:CMS:Global:UseHttps=false`, or the backoffice sign-in refuses.
  - **Kestrel's body limit** surfaced as a `500` until it was handled.
- **Playwright** page snapshots and traces record typed values, passwords included. Generate E2E passwords per run, and read only the error section of `error-context.md`.
- **Playwright and the browser:**
  - **Network:**
    - Chromium's Local Network Access blocks a page fulfilled by `page.route` from loading loopback scripts;
    - the API request context resolves `*.localhost` to IPv6, so API calls to `site-a.localhost` reach the default host. Browsers resolve `*.localhost` themselves (14.4). Never edit the hosts file;
    - a spec that routes calls `page.unrouteAll({ behavior: 'ignoreErrors' })` after each test.
  - **Assertions and snapshots:**
    - Umbraco announces toasts in a short-lived `role="alert"`, so assert inside your own element;
    - visual snapshots shift with machine rendering. Before refreshing a baseline, compare it with the last commit's (`git stash -u`) and look at both.
  - **Focus and clicks:**
    - removing a focused element drops the focus, so reconcile children instead of calling `replaceChildren` (the designer depends on this);
    - `event.detail === 0` marks a keyboard click, and jsdom's `click()` is one.
  - **axe:** UUI 1.18's `uui-menu-item` is named "menuitem" and must be listed, not hidden. A sandboxed iframe hangs axe, so scan with `iframes: false` and check the iframe's own document separately (the preview, 10.3).
  - **Lighthouse** cannot read Edge's debugging port. Start Edge with `--remote-debugging-port=0` and read `DevToolsActivePort`.
- **The demo:** start the demo build only through its script. Otherwise Umbraco writes a generated imaging HMAC key into the tracked `appsettings.json`.
- **.NET quirks:**
  - a second solution file in the repository root makes `dotnet format` refuse to run; keep solution filters in `src/`;
  - `[ApiVersion]` is not inherited: put it on every concrete controller;
  - `TypeFilterAttribute` needs `Arguments`;
  - `WriteAsJsonAsync` falls back to `RequestAborted` when given `CancellationToken.None`;
  - `System.Threading.RateLimiting` ships with ASP.NET Core, so limiter adapters live in the web project;
  - NPoco's async methods have no `params` overloads;
  - an `AsyncLocal` set inside an async method does not flow back to the caller;
  - Umbraco's `IEventAggregator` dispatches on the runtime type;
  - Umbraco's own section migration writes `umbracoUserGroup2App` directly, because the user-group service may fail inside a migration.
- **Packing and NuGet:**
  - a transitive advisory under warnings-as-errors gets a suppression of exactly the reviewed advisory URL, plus a guard script that fails if the package becomes a direct reference (Tandem ADR-0014);
  - Vite source maps carried the original TypeScript into the package, so maps are opt-in and the pack check refuses them;
  - pack through a solution filter (`.slnf`), because a whole-solution pack warns on the demo;
  - `PublishRepositoryUrl=false` does not keep an explicitly set `RepositoryUrl` out of the package (§3.5);
  - the Marketplace schema's URL pattern `\:` fails a Unicode regular expression, so ajv reads its patterns without the `u` flag, and the schema is pinned by SHA-256.
- **Shell and Windows:**
  - Bash heredocs with backticks and quotes break, so write edit scripts with the file tool, and replace text with split/join rather than `String.replace` (`$'`);
  - Windows cannot delete a folder a shell sits in;
  - MSBuild node reuse locks temp folders;
  - `node --test` needs a glob;
  - the system git has `core.autocrlf=true`, which `.gitattributes` overrides;
  - **text gets damaged in transit.** The Edit and Write tools, Bash heredocs and PowerShell strings have each eaten backslashes, or turned a Unicode escape for a curly apostrophe into the character itself (it happened again while this file was written). The text check of ROADMAP 0.2 catches it: non-ASCII characters in client sources outside the language tables, and control characters in any tracked text file. This plan's own CLAUDE.md once held a path whose `\f` had become a form feed;
  - PowerShell 5.1 turns redirected stderr lines into error records, so `ci-local.ps1` decides on exit codes only;
  - `git worktree remove` fails past MAX_PATH: delete the folder with `fs.rmSync`, then run `git worktree prune`;
  - the running demo locks its `bin` folder, so stop it before `dotnet build`;
  - `git add -p` doesn't work in this shell; stage hunks with a filtered `git apply --cached`;
  - a stopped background task leaves its child processes running. Stop trees by PID (CLAUDE.md rule 6).
- **Licences:** the SchemaStore workflow schemas on npm are GPL-3.0, so the workflow check uses GitHub's MIT parser.
- **Slips to prevent with tooling, not memory:**
  - an ad-hoc demo start wrote a key into the tracked `appsettings.json`, hence "only through the script";
  - a Playwright error-context file showed a generated password, hence reading only its error section;
  - a broad search read an off-limits folder in a reference repository, hence excluding those globs in every search (CLAUDE.md rule 5).

## 2. The test harness: the shape to port

Tandem ended with 112 script tests, 443 client tests, 1,170 unit tests, 570 integration tests and 173 browser tests, all green from a clean clone. Port this shape, renamed to Jiron Form Builder's names and ports (§4.11).

- **Projects:**
  - `tests/Directory.Build.props` gives every test project `Microsoft.NET.Test.Sdk`, `NUnit3TestAdapter` and coverlet.
  - `Tests.Unit`: NUnit 3 and NSubstitute, with no Umbraco host.
  - `Tests.Integration`: `Umbraco.Cms.Tests.Integration` on SQLite. Its `appsettings.Tests.json` sets:
    - `PackageMigrationsUnattended=false` and `ModelsBuilder:ModelsMode=Nothing`;
    - quiet logging;
    - the test database pool (`Tests:Database`: SQLite, 4 prepare threads, 4 schema databases, 2 empty ones).
  - A separate integration project for anything that brings its own tables.
  - `tests/fixtures/upgrade/schema-vN.json`: upgrade fixtures as JSON rows, loaded by `UpgradeFixtureTests`. Rows rather than a database file, so the same fixture runs on SQLite and SQL Server (Tandem ADR-0067).
- **The Testing project** (`Jiron.Umbraco.FormBuilder.Testing`, never packed):
  - `Time/`: a test clock, a `TimeProvider` over it, and manual timers;
  - `Users/`: seeders for every test role and for the members;
  - **test-only endpoints** under `/jfb-testing/…`: seed, move the clock, run a job now, age entries. Their composer answers only on test hosts, in Development, on loopback, and a test proves that a normal host has none of them;
  - **stubs and fakes:**
    - the HTTP stub on 5560 (webhooks, URL option lists, captcha verification);
    - the fake AI provider for 15.3 (Tandem's scripted `FakeAI/` client);
    - scripted scenarios for screenshots.
- **E2E** (`tests/e2e`, with its own `package.json`):
  - **`global-setup.ts`:**
    - builds the client and the demo, unless `JFB_E2E_SKIP_BUILD=1` (and then warns that the precompressed assets may be stale);
    - starts a fresh demo DLL on 5520. Its content root is `.local/e2e/host-5520/`, with its own SQLite file, `mail/` pickup folder and `host.log`, and it runs on the test clock;
    - prepares the site, for example raising the per-IP limits, because every spec comes from 127.0.0.1;
    - **waits for Umbraco's start-up work** (`waitForStartupWork` in `support/host-health.ts`, about 50 s), so no sign-in lands in the one-minute stall (§3.3);
    - starts the static site on 5521 and the stub on 5560.
  - **The hang marker:** the teardown writes `database-hung.txt` when the host's database hung, and `ci-local.ps1` then reruns the browser suites once. That is the only automatic rerun (Tandem ADR-0011; "Flaky means broken" in "How to use").
  - **`support/`:**
    - hosts: `host.ts`, `host-env.ts`, `host-health.ts`, and `paths.ts` (ports and URLs, read from the demo's `appsettings.json`);
    - users and sign-in:
      - `users.ts` creates fresh passwords per run and keeps them in `.local/credentials.json`;
      - `user-lock.ts` allows one sign-in at a time, through a lock directory with PID takeover;
      - `api-login.ts`;
    - checks and fakes: `axe.ts`, `mail.ts` (decodes `.eml`, retries on `EBUSY`), `static-site.ts`, `stub.ts`;
    - one helper per area (for example `designer.ts`, `entries.ts`, `website.ts`).

    The support code has node tests of its own.
  - **`fixtures.ts`:**
    - sign-in: `loginAs`, and `apiAs` (the PKCE flow; `Authorization: Bearer [redacted]`, with the real token in the cookie);
    - forms: `fillForm`, `submitForm`;
    - mail and accounts: `waitForMail`, `accountOf`.
- **Playwright projects** (`playwright.config.ts`: Edge locally, Chromium in CI, 4 workers, `retries: 0`):
  - `smoke`;
  - `website`, with `@visual` snapshots locally only;
  - `website-phone` (Pixel 7) and `website-iphone` (iPhone 13 emulated in Chromium);
  - `backoffice`, as every role;
  - `privacy` and `resilience`. They start hosts of their own (5522, 5524) or move the shared clock, so they depend on the main projects;
  - `ux` and `screenshots`, which never run in the gate;
  - `lb`, CI only (5500 and 5501 on SQL Server).

  A separate `install.config.ts` (5530, tagged `@packages`) drives the install test.
- **`scripts/ci-local.ps1` stages,** in order, each judged by its exit code:
  1. repository checks: secret scan, term guard, text check, workflows, licences, links, the Marketplace schema, and `node --test "scripts/tests/*.test.mjs"`;
  2. client: `npm ci`, lint, tests, build, size budgets;
  3. .NET: restore, a Release build with `-nodeReuse:false`, `dotnet format --verify-no-changes`, the third-party notices;
  4. packages: `scripts/pack.mjs`, package validation and the file allowlist;
  5. `dotnet test --blame-hang-timeout 10m`, then the coverage merge (`scripts/coverage.mjs`);
  6. the browser projects, rerun once only when `database-hung.txt` exists.

  Tandem's gate grew from 97 seconds to 15 to 30 minutes. Never skip a stage to save time; a slow gate is fixed by making the slow tests faster.
