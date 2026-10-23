# What Jiron Form Builder reuses from Tandem

Every script, pattern and gotcha of the owner's chat package Tandem that Jiron Form Builder ports, adapts or drops,
with the step that does it. Written in ROADMAP step 1.1 (2026-10-22).

- **The source:** Tandem at `C:\Code\Umbraco.Community.AI.Tandem`, finished on 2026-10-22 with all 110 steps and an
  Umbraco 18 line. Its packages and namespaces are `Jiron.Umbraco.ChatSupport.*`; its class prefix, table prefix and
  settings section are still "Tandem". It is the owner's own work, so its code may be ported freely (CLAUDE.md rule 5).
- **How this was made:** Tandem was only read (files opened, `git ls-tree`); nothing there was built, run or changed.
  The off-limits files of CLAUDE.md rule 5 were never opened. One search for port numbers listed file *names* under
  Tandem's git-ignored `.local/` (only paths and port numbers came back, no contents).
- **How to use it:** before a step, look up its number here. [ROADMAP §3.3](../../ROADMAP.md#33-tandem-what-to-reuse-and-its-lessons)
  says what to port; [tandem-lessons.md](tandem-lessons.md) holds the gotchas Tandem's own notes recorded and the
  shape of its test harness. This file maps those gotchas to steps (section 9.1) and adds the ones the inventory found
  beyond them (section 9.2), so each gotcha is written down once.
- **Abbreviations:** `Core/`, `Web/` and `BO/` are Tandem's `src/Jiron.Umbraco.ChatSupport.Core/`,
  `src/Jiron.Umbraco.ChatSupport.Web/` and `Web/Client/packages/backoffice/`; `TU/` and `TI/` its unit and integration
  test projects. Plain step numbers are Jiron Form Builder's.

## 1. The §3.3 table, row by row

| Area | Tandem source | Decision | Steps |
| --- | --- | --- | --- |
| Repository scripts | `scripts/*.mjs`, `ci-local.ps1`, `demo.ps1`, `scripts/tests/*` | **Port**, renamed (section 2). The progress line, secret scan and hooks are ported already, with Jiron Form Builder's identity check, term guard and text check added. | 0.2 (done), 3.3, 3.5, 16.5, 17.1, 17.3, 18.1, 18.2 |
| Build files | root `Directory.Build.props`, `Directory.Packages.props`, `NuGetAudit.props`, `.editorconfig`, `global.json`, `NuGet.config`, `src/Directory.Build.*`, `tests/Directory.Build.props` | **Port.** The root files are in place since 0.2; the `src/` and `tests/` files come with the projects (section 3). | 0.2 (done), 3.1, 3.4 |
| Database gate, migration plan, frozen snapshots, migration state, `SqlNames` | `Core/Persistence/*`, Tandem ADR-0005, 0006, 0063, 0065 | **Port the patterns** (section 4.1), with Jiron Form Builder's own plan name, states and table prefix. | 1.3, 4.1, 4.3 |
| Settings framework | `Core/Settings/*`, `Core/Configuration/*`, Tandem ADR-0008, 0039 | **Port the patterns** (section 4.2), with a new cache refresher id and the `JironFormBuilder` options section. | 4.4, 12.4a |
| Captcha, limits, email queue, audit log, daily stats | `Captcha/*`, `Limits/*`, `Email/*`, `Audit/*`, `Stats/*` | **Port and adapt** (sections 4.3 to 4.7): providers become `IFormBuilderCaptchaProvider`s, limits count submissions and uploads, stats get a form dimension. | 4.3, 7.1, 7.2, 8.2 |
| Public API conventions | `Web/PublicApi/*`, Tandem ADR-0020, 0068 | **Port the patterns** (section 5.1): route convention, base path validation, ProblemDetails codes, CORS, body and time limits, the OpenAPI snapshot and endpoint table tests. | 6.1, 6.6 |
| Backoffice client | `BO/` | **Port the patterns** (section 5.4): workspace and settings frame, generated client from a committed snapshot, manifests test, Vite config, runtime-free logic modules. | 3.2, 4.7, 9.1, 12.4a |
| Test harness | `tests/*`, `tandem-lessons.md` part 2 | **Port that shape** (section 6), with Jiron Form Builder's ports, roles, markers and a form-shaped Testing project. | 3.4 |
| Email | `Core/Email/*`, Tandem ADR-0042, 0052, 0053, 0076 | **Port the patterns** (section 4.5): the outbox with claims, the woken sender, the safety-net job, the sending gate, Outlook- and Gmail-safe HTML. | 1.6, 8.2, 8.3 |
| Packing | `scripts/pack.mjs`, `scripts/check-packages.mjs`, `Client/scripts/stamp.mjs` (`TANDEM001`), Tandem ADR-0001, 0077 | **Port** as one pack script, a package allowlist and the `JFB001` asset check (section 3). | 3.2, 3.5, 18.1, 18.2 |
| Install and upgrade tests | `scripts/install-test.ps1`, `scripts/install-test/*`, Tandem ADR-0067 | **Port**, fixing two rule 6 gaps: stop the whole process tree, and use the throwaway-build settings (section 2). | 16.5, 18.3, 19.2 |
| Release | `.github/workflows/release.yml`, `scripts/release.mjs` | **Port** in its final shape: `prepare`, CI through `workflow_call`, the one pack script, a draft release, publishing off. | 18.2, 18.4 |
| Screenshots, UX walk, icons | `scripts/screenshots.mjs`, the `ux` and `screenshots` projects, `scripts/icons.mjs` | **Port**, with seeded forms and entries instead of AI scenarios. | 2.4, 9.6, 17.1, 17.3 |
| The safe confirm helper | `BO/src/ui/confirm.ts` and its source test | **Port unchanged**, the guarded flows extended to forms, entries, option lists and templates. | 9.1 |
| Precompressed, fingerprinted assets | `Web/Rendering/WidgetAssets*.cs` | **Port** for `/jfb/assets/`, with every lazy chunk carrying the `?v=` fingerprint. Jiron Form Builder writes its own Brotli and gzip copies at build (3.2), so it does not depend on the site's publish. | 3.2, 6.2 |
| The fake AI provider | `tests/…Testing/FakeAI/` | **Port** the strict core (scripted answers, tool calls, failures, the journal) for the AI add-on only; drop Tandem's chat-only directives. | 15.3 |
| CI, Dependabot, templates | `.github/*` | **Port** `ci.yml` and `dependabot.yml` with `JFB_CI`; the issue forms, PR template and `CODEOWNERS` exist since 0.2. | 0.2 (done), 3.5, 8.1 |

## 2. Scripts

Every script under Tandem's `scripts/`, plus the client scripts. All are Node ESM using `node:` built-ins (unless
noted) or Windows PowerShell 5.1; each `.mjs` exports its functions for tests and runs only when executed directly;
exit codes are 0 fine, 1 problems, 2 usage errors.

| Script | What it does | Steps | What changes for Jiron Form Builder |
| --- | --- | --- | --- |
| `secret-scan.mjs` | secrets by shape and forbidden paths; staged, tracked and history modes; fingerprinted hits and an allowlist | 0.2 (done) | the marker `JFB_FAKE`, no owner key file, more rules (captcha keys, webhooks, signed URLs) |
| `install-hooks.mjs` | sets `core.hooksPath` | 0.2 (done) | plus the commit-msg hook, the identity check and the term guard |
| `roadmap-status.mjs` | the one-line progress report | 0.2 (done) | the same line, with problems reported per roadmap line |
| `ci-local.ps1` | the gate: repository checks, client, .NET build and format, packages, tests with blame-hang, browser projects, one rerun only on the hang marker, a timing table | 3.5 | paths, `JFB_E2E_*`, the marker under `.local/e2e/host-5520/`, Jiron Form Builder's projects, a coverage stage (Tandem ran coverage only in reviews), `fix.mjs` before the gate; no Marketplace stage until 18.1 |
| `check-audit-suppressions.mjs` | fails when a package whose advisories `NuGetAudit.props` suppresses is referenced directly | 3.5 | none |
| `check-links.mjs` | relative links and anchors in Markdown, slugged as GitHub does; never fetches | 3.5 (used from then on, 17.2a) | none |
| `check-marketplace.mjs` | validates `umbraco-marketplace.json` against the Marketplace's schema, pinned by SHA-256, and the listing's own rules | 18.1 | Jiron Form Builder's title, categories, screenshots and links; ajv, `@actions/workflow-parser` and `yaml` join `scripts/package.json` |
| `check-npm-licences.mjs` | every npm lock file against the allowed licences (SPDX AND and OR) | 3.5 | Jiron Form Builder's lock files; a stricter list if a runtime npm package is ever bundled into the website runtime |
| `check-nuget-licences.mjs` | new in Jiron Form Builder: each restored package's nuspec licence against CLAUDE.md rule 4 | 3.5 | |
| `check-packages.mjs` | each `.nupkg` and `.snupkg` against its allowlist, with forbidden entries named (source maps, test assemblies, appsettings, `.local/`), nuspec metadata and exact sibling versions; its own zip reader | 3.5 (the gate's pack stage), 18.1 | `PACKAGES` for Core, Web, the meta package and the AI add-on; static assets under `App_Plugins/JironFormBuilder/` and `jfb/assets/`; the PolyForm licence file instead of "All rights reserved" |
| `check-workflows.mjs` | workflows through GitHub's own parser plus rules (read-only permissions, concurrency, gated jobs, no `always()`, timeouts, pinned actions, no top-level `\|\|`), and `dependabot.yml` | 3.5 | `JFB_CI`; never the SchemaStore workflow schemas (GPL-3.0) |
| `coverage.mjs` | merges Cobertura reports and prints line coverage per assembly, with a minimum | 3.5 | the bar is 85% on Core (§4.12) |
| `demo.ps1` | `start`, `stop`, `reset`, `login`; secrets generated into `.local/credentials.json`; the child's environment set only for the start; PID file; readiness by a log line and two pages | 3.3 | port 5500, `.local/demo/`, the seeder's line, no `-Live` |
| `icons.mjs` | renders the logo SVGs to PNGs with Playwright, plus a preview sheet | 17.1 | Playwright from `scripts/package.json` |
| `install-test.ps1` and `install-test/*` | installs the packages into a fresh site (private template hive, fresh package cache, a cleared `NuGet.config` with the run's feed), `install` and `upgrade` modes, a seed, the database read with `node:sqlite`, the final state read from the plan's source | 16.5, 18.3, 19.2 | `%TEMP%\jfb-install-*`, `-nuget-*`, `-baseline-*`, port 5530, `JFB_INSTALL_*`; **stop the whole tree by a PID file and use the throwaway-build settings** (Tandem did neither); seeds create a page that renders a form, and the smoke checks submit it |
| `pack.mjs` | the one pack step of the gate, CI and the release, with package validation, the allowlist and the version check | 3.5, 18.2 | the solution filter's name |
| `release.mjs` | `version` (a SemVer on the branch's Umbraco major) and `notes` (the version's changelog section) | 18.2 | none |
| `screenshots.mjs` | the `screenshots` project on a fresh host, then the screenshot README and a size check of every PNG | 17.3 | seeded forms and entries; the shot list Jiron Form Builder needs |
| `third-party-notices.mjs` | generates and checks the runtime-dependency tables of `THIRD-PARTY-NOTICES.md` from the restore graph | 3.5 | **reads `NUGET_PACKAGES`** (Tandem read only the default cache, which a fresh CI runner lacks); Jiron Form Builder's projects and exceptions |
| client `stamp.mjs`, `version.mjs` | the build stamp with an inputs hash, and the `TANDEM001` pack check | 3.2 | `JFB001`, `JFB_VERSION`, `JFB_SOURCEMAP`, the licence banner check |
| client `size.mjs`, `bundle-report.mjs` | gzip budgets and each folder's share | 3.2 | budgets of §4.12, plus the backoffice bundle (budget set in 9.6) |
| client `check-ascii.mjs` | ASCII-only widget sources | 0.2 (done) | covered by `check-text.mjs` |
| `scripts/tests/*` | one `node:test` file per script | with each script | the pattern "a docs table equals the code's registry" (Tandem's privacy docs test) is reused in 13.4, the README test in 17.2a |

## 3. Build and packaging

| Pattern | What it is | Steps |
| --- | --- | --- |
| Root build settings | net10.0, nullable, warnings as errors, analyzers, deterministic, not packable by default, the version prefix and suffix, NuGet audit at `low` | 0.2 (done) |
| `src/Directory.Build.props` | every project under `src/` is packable with its id, tags, project address, licence file, readme and icon; `snupkg` with Source Link; package validation; `InternalsVisibleTo` for the test projects and NSubstitute | 3.1, 18.1 |
| Exact sibling versions | a target after NuGet's `_GetProjectReferenceVersions` pins sibling packages to `[version]`, so they install in lockstep; it hooks an internal target, so the allowlist's exact-dependency check is what proves it still works | 3.1, 18.1 |
| The pack-time asset check | an MSBuild target before `GenerateNuspec` runs the client's stamp check and fails on missing or stale assets, another version, or source maps; MSBuild never runs npm | 3.2 |
| The meta package | no code, `IncludeBuildOutput=false`, an empty `lib/net10.0/_._`; the Marketplace tags live here | 3.1, 18.1 |
| `buildTransitive` | ships the appsettings IntelliSense schema into the site (with a `!` exception in `.gitignore`) | 4.4, 18.1 |
| The solution filter in `src/` | packs only the packable projects; a second solution file in the root makes `dotnet format` refuse | 3.1 |
| `NuGetAudit.props` | one reviewed suppression per advisory URL, reviewed at every phase end; Jiron Form Builder's list is smaller (no AI packages in the core) | 3.1, 16.1 |
| Versioning | the package major follows the Umbraco major; ranges widen only after tests; `main` merged into `v18/main`, never the reverse | D16, 18.4, 19.1 |

## 4. Patterns in Core

### 4.1 Persistence

| Pattern | What to keep | Steps |
| --- | --- | --- |
| The database gate | one async gate (`SemaphoreSlim(1,1)`) on SQLite only, decided from the provider name and never cached while it is still empty; re-entrant through an `AsyncLocal` flag set inside `RunAsync`; never entered from an outer Umbraco scope that has written | 1.3, 4.3 |
| The unit of work | `ReadAsync` and `WriteAsync` through the gate in a scope; a write starts with a write; notifications only after the outermost unit; nothing with side effects inside a unit | 4.3 |
| Retries | the outermost unit only, 3 attempts (50 ms, 250 ms), never after `Complete()`, never inside an Umbraco-opened scope, never a cancelled request; lock conflicts always, lost connections for reads only | 4.3, 16.4 |
| Transient errors | classified by the exception's type name and a reflected code (SQLite 5 and 6; SQL Server 1205 and 1222, and the connection codes for reads), so Core references neither client | 4.3 |
| The migration plan | `PackageMigrationPlan` with `IgnoreCurrentState => false` stated; fixed, never reused state strings (`jfb-001-…`); a plan name chosen once; idempotent `AsyncMigrationBase` steps; referenced tables first (SQLite puts foreign keys into `CREATE TABLE`) | 1.3, 4.1 |
| Frozen snapshots | each step owns nested, fully annotated DTOs marked "never edit"; product DTOs are separate | 4.1, 4.8 |
| The section grant | raw SQL guarded by a `jfbKeyValue` flag, so an administrator's removal stands; the user-group cache refreshed on every server after a run that executed a step | 4.1 |
| The pending guard | "pending" while not at Run or behind the plan; re-read after 5 s, "current" cached; an unreadable state counts as pending. The public API answers `503 jfb_migrations_pending`, rendering and jobs idle | 4.1, 6.1, 8.1 |
| `SqlNames` | quoting per provider, the `UPDLOCK, HOLDLOCK` hint on SQL Server, `LIKE` escaping including `[` | 4.3 |
| Once-only inserts | `INSERT … SELECT … WHERE NOT EXISTS` with the hint, true for one writer only; update first, insert on a miss | 4.3 |
| Times and hashes | dates marked UTC on read; hashes lowercase hex; test times in whole milliseconds | 4.3 |
| Tests | plan tests (fresh install, upgrade from every state, rerun, replay, rollback per data step), own schema queries for both databases, `docs/database.md` checked against them, SQLite query plans on 10,000 rows, 200 parallel writers | 4.1, 4.3 |

### 4.2 Settings

| Pattern | What to keep | Steps |
| --- | --- | --- |
| Two layers | host options (validated on start, messages naming the key) and typed backoffice sections with a schema version and an optimistic version; effective value = defaults, then stored, then host rules | 4.4 |
| Sections | immutable records whose initialisers are the defaults; enums as camelCase text with integers refused; schema migrations per section | 4.4 |
| The read path | runtime cache with a loading token, so a read that raced a save is not cached; an unreadable row gives the defaults with its version | 4.4 |
| The save path | locks (Locked, Restricted, Clamped), validators that use host caps as their top, the row and the audit entry in one write, the cache cleared on every server through a payload cache refresher, a notification after the commit | 4.4 |
| Host options | lists start empty and get defaults in `PostConfigure`; secrets as `$JironFormBuilder:Secrets:<Name>` references | 4.4 |
| The generated reference | `docs/configuration.md` generated from the options, sections, XML docs and validator rules, with a test | 17.2b |
| The settings API and workspace | one `GET\|PUT settings/{section}?site=` and one workspace with a draft per section (section 5.4) | 12.4a |

### 4.3 Captcha

`ICaptchaVerifier` never throws for input or provider answers; outcomes are Passed, Failed or Unavailable; the
orchestrator fails closed (`403 captcha_failed`, `503 captcha_unavailable`); only the secret and the token go to the
provider; hostnames come from configuration, never from the request; a site's own implementation wins over the
package's whatever the registration order; the action is one constant shared with the client and checked by a test
that reads the client source. Jiron Form Builder makes the action and score checks optional per provider, and tests
against the stub on 5560. **Steps:** 7.1, 7.2.

### 4.4 Limits

`RateWindow` (sliding segments on an injected clock), `RateWindowStore` (replaces a window whose limit changed, sweeps
idle ones), reservations that commit or give their permits back, the client key from `RemoteIpAddress` only (IPv6 by
/64), and the proxy hint that warns when internal addresses send `X-Forwarded-For`. The package's limiter stays out of
ASP.NET Core's `RateLimiterOptions`, reports `IdleDuration = null` while a window counts, and puts the limit into the
partition key. Renamed to submissions, uploads and forms. **Steps:** 7.1, 9.1 (the proxy health check).

### 4.5 Email

| Pattern | What to keep | Steps |
| --- | --- | --- |
| The outbox | claims by conditional update (10 minutes), 5 attempts (1, 3, 9, 27 minutes), stuck claims failed and never resent, redacted errors | 8.2 |
| The sending gate | while `CanSendRequiredEmail()` is false, claim nothing, burn no attempt, warn once an hour | 8.2 |
| The woken sender | queuing pulses a one-item channel; a `BackgroundService` runs the queue at once and wakes itself while a run reports "more"; purging after sending | 1.6, 8.2 |
| The safety-net job | a distributed job once a minute for retries, stuck claims, purging and missed wakes | 8.1, 8.2 |
| Sending | `IEmailSender.SendAsync(…, enableNotification: true)`, so a site's own handler still sees the mail | 8.2 |
| Caps | per mailbox and per site in one count-and-insert statement with a range lock; the mailbox normalised in C# and stored, because SQLite's `LOWER` folds ASCII only | 4.1 (§4.5 `RecipientKey`), 8.2 |
| HTML | a 600 px card with inline styles, Outlook `<!--[if mso]>` tables, nothing on `<body>`, every value encoded, button text colour from WCAG luminance | 8.2, 12.2 |
| Preview and test email | the preview rendered on the server from unsaved settings into a sandboxed frame; the test email at once, `409 email_not_configured` without SMTP | 10.2, 12.2 |
| Not ported | the HTML regular expression that refuses `<script` and `on*=`: §4.10 needs an established sanitiser | |

### 4.6 Audit log

The entry is written inside the change's unit of work; `AuditDetails` accepts only values that cannot carry content
(GUIDs, numbers, flags, enums, UTC times, identifiers); identifier lists are capped with a count, so a valid save
never fails because of its audit entry; reads are audited, deduplicated per user and item over 15 minutes; pages
ordered by time and id. **Steps:** 4.3, 11.2, 12.4b.

### 4.7 Daily statistics and other Core pieces

- **Stats:** update first and insert on a miss with a retry on a key clash; counted by handlers after the commit (a
  lost increment after a crash is acceptable); a capped counter for daily caps; metric names validated. A form
  dimension replaces Tandem's site profile. **Steps:** 4.3, 11.5.
- **Composition:** one idempotent `AddJironFormBuilderCore()` behind the composer; a fluent `builder.FormBuilder()`
  surface for extension points. **Steps:** 3.1, 15.1.
- **Logging safety:** exceptions described by type and data-free codes, job runs wrapped so only those are logged.
  **Steps:** 8.1, 13.3.
- **Validation and text helpers:** a field-error collector with stable codes; email, culture and address checks;
  `PagedResult<T>`. **Steps:** 4.2, 4.7.

## 5. Web and backoffice patterns

### 5.1 The public API

| Pattern | What to keep | Steps |
| --- | --- | --- |
| Internal controllers | an application feature provider adds the package's internal controllers by base type (MVC finds only public ones) | 4.7, 6.1 |
| The route convention | every public controller under `{JironFormBuilder:PublicApi:BasePath}/v1`; the base path refused when it is `/umbraco`, `/App_Plugins`, `/media`, `/jfb/assets`, or under or above them (so `/jfb` itself is refused) | 4.4, 6.1 |
| The base controller | anonymous, its own OpenAPI document and named JSON options, `no-store`, the exception and validation filters, a time limit, shared 400 and 429 answers | 6.1 |
| Time limits | a resource filter that swaps `RequestAborted` for a linked token on the site's clock; the most specific attribute wins by checking it is the last of its type; 504 `timeout` told apart from a visitor who left | 6.1 |
| Body limits | 413 before reading when `Content-Length` is too large, a counting stream otherwise, 415 for other content types; Kestrel's own 413 mapped | 6.1 |
| Validation and errors | model-state keys normalised to camelCase fields; one exception filter; one ProblemDetails writer (`code`, `Retry-After` and `retryAfter`, `errors`, `no-store`) | 6.1 |
| CORS | only under the base path, an allowlist, `Vary: Origin` and `nosniff` exactly once, a refused preflight `403 origin_denied`, `ETag` and `Retry-After` exposed, never credentials | 6.1, 6.6 |
| The preview ticket | the sandboxed preview's `null` origin accepted only with a short-lived signed ticket bound to one form | 10.3 |
| OpenAPI | each document's registration in one place (Umbraco 18 replaces Swashbuckle), JSON-only request bodies, a snapshot test per document, an endpoint table test so no endpoint skips CORS, body and rate checks; decide on purpose how error bodies are described (Tandem's snapshot listed plain JSON) | 6.1, 6.6, 19.1 |

### 5.2 The management API

- **The base:** `ManagementApiControllerBase` with `[MapToApi("jfb")]`, the backoffice JSON options, the
  backoffice-access policy and the exception filter on the base (two controllers without it once turned refusals into
  500s); `[ApiVersion("1.0")]` on every concrete controller. **Steps:** 4.7.
- **Permissions:** a `TypeFilterAttribute` with `Arguments`, checked on every endpoint; one open "my permissions"
  endpoint so the client hides what the user may not use; Umbraco's section permission protects only the UI.
  **Steps:** 4.6, 4.7, 9.1.
- **Operation ids and tag:** an own `IOperationIdHandler` (`{Controller}{Action}`); the tag "Forms" names the SDK
  class. **Steps:** 4.7.
- **The 403 matrix:** every endpoint found through the API description, each called as a user without its
  permission, route parameters filled by type. **Steps:** 4.7.
- **Two validation shapes:** the client reads both (a dictionary, or Umbraco's list of `$.field` entries).
  **Steps:** 9.1.

### 5.3 Rendering and assets

- **Assets:** a content fingerprint (package version plus a hash of the files), the best accepted precompressed copy,
  a year of caching only for the current fingerprint, CORS and `Cross-Origin-Resource-Policy` on the static files so
  headless pages can `import()` them. **Steps:** 3.2, 6.2, 6.6.
- **Razor helpers:** render once per request, encode every value, a CSP nonce interface, nothing while migrations are
  pending. **Steps:** 6.2.

### 5.4 The backoffice client

| Pattern | What to keep | Steps |
| --- | --- | --- |
| Layout | an npm workspace; Lit without decorators under strict TypeScript; one entry point; per-area folders with runtime-free helper modules | 3.2, 9.1 |
| The generated client | hey-api with `asClass`, generated from the committed snapshot (not a running host), `// @ts-nocheck` on the output, every call gated on `clientReady()` | 4.7 |
| Manifests and their test | aliases `^Jiron\.FormBuilder\.` and unique, one bundle, no telemetry, the section and menu items, English and German keys equal and non-empty | 9.1 |
| `confirmText()` | Umbraco's confirm modal with its content as a Lit template, plus the source test that bans the raw modal; ESLint bans `unsafeHTML` | 9.1 |
| The idle gate | wraps the client's sign-in, so no call renews an expired session while nobody uses the backoffice; input shared across tabs; one tab polls (Web Locks, which plain http lacks) | 11.3 |
| The settings workspace | a draft per section, a dirty dot, one save bar; conflicts and locks as toasts; errors under each field; the leave guard on Umbraco's router; `?tab=` in the address; late answers dropped after a site switch | 12.4a |
| Accessibility | tabs with manual activation; names by `aria-labelledby`, help by `aria-describedby`; status text colours mixed 60/40 with the text colour | 9.1, 12.4a |
| Pages on phones | a page sized exactly `100vw` at 480 px and below, because Umbraco keeps its 300 px sidebar | 9.1, 9.6 |
| Design tokens | two radii, tinted shadows, words instead of dashes for empty values; a hand-picked radius is a review finding (Tandem ADR-0075) | 2.2, 9.6 |
| Vite | library build with a fixed file name, `@umbraco-cms/*` external, source maps only on request, the manifest's version stamped | 3.2 |
| Client tests | Vitest projects; backoffice tests import only types and runtime-free modules; invariants held by source-scanning tests; elements covered by Playwright | 3.2, 9.1 |

### 5.5 From the widget to the website runtime

The loader plus lazily loaded modules, ASCII-only sources outside the language tables, no HTML strings (one DOM
builder), guarded browser storage with a memory fallback, a fake API for tests, and a custom-CSS cleaner that
neutralises references to other hosts. **Steps:** 6.4, 6.6, 12.2.

## 6. The test harness

The shape (projects, Testing project, E2E host, Playwright projects, gate stages) is in
[tandem-lessons.md](tandem-lessons.md) part 2. The details to port:

| Piece | What to keep | Steps |
| --- | --- | --- |
| `tests/Directory.Build.props` | only `*.Tests.*` projects are test projects, with a direct `Microsoft.NET.Test.Sdk`, the adapter, coverlet and a global `using NUnit.Framework` | 3.4 |
| Versions | `Umbraco.Cms.Tests.Integration` 17.7.1 with NUnit 3.14, NSubstitute, coverlet; hosts on 17.7.1, products on the range | 3.4 |
| The Testing project | a composer that every referencing host gets, with each risky switch needing configuration, Development and a loopback caller; fake time (`TestTimeProvider`, `ManualTimers`, the e2e `TestClock`); a user seeder from configuration; the HTTP transport override for the stub; `/jfb-testing/` endpoints to seed, age entries, move the clock and run a job | 3.4 |
| Unit tests | the NUnit constraint model; `InstancePerTestCase` where fields hold clocks or substitutes; internal fixtures with public methods; generated-file tests (configuration reference, appsettings schema, public API approval, the `TimeProvider` scan) | 3.4, 15.1, 17.2b |
| Integration settings | `appsettings.Tests.json` with SQLite, package migrations off, ModelsBuilder off; SQL Server through environment variables in CI only | 3.4 |
| Integration bases | a `[SetUpFixture]` per assembly; the migration test base with a rollback helper; a repository base; a test-server base whose `ConfigureHost` runs before `base.Setup()`, with an API user signing in by client credentials | 3.4 |
| Integration patterns | one fixture per configuration; fake time through `AddUnique<TimeProvider>`; real Razor through a compiled test view; log capture; a pickup folder per test; jobs run by hand with `DistributedJobSettings.Delay` at an hour; every simulated failure starts with `JFB_TEST:` | 3.4, 6.2, 8.1 |
| The upgrade fixture | rows plus the baseline's schema as JSON, inserted in parent order and read back through today's services, on both databases | 16.5 |
| E2E config | `retries: 0`, fully parallel, Edge locally and Chromium in CI, phones emulated in Chromium, 120 to 300 s for backoffice journeys | 3.4 |
| Global set-up | per-run secrets in `.local/credentials.json`; the user lock cleared; the client and demo built unless skipped; a fresh host on 5520 started and stopped by its PID file; settings prepared once; the start-up wait; the static site on 5521; the stub on 5560 | 3.4 |
| Teardown | stop the host, then write the hang marker when the log shows a stalled database | 3.4 |
| Fixtures | `loginAs` (waits 45 s for the form and reloads once), `apiAs` (PKCE through the API, bearer token), `waitForMail` (skips `EBUSY`), plus Jiron Form Builder's `fillForm` and `submitForm` | 3.4 |
| Support | the sign-in lock (an atomic folder with a PID owner), the mail parser, the axe helper (legacy mode, only findings inside `jfb-*` elements, known Umbraco findings listed), the static site on both loopback addresses, the host health parser, each with its own tests | 3.4 |

**Ports** (§4.11): demo 5500 (and 5501 for the load-balancing pair in CI), sandbox 5510, e2e host 5520, static site
5521, extra hosts 5522 to 5524, install site 5530, Umbraco 18 sandbox 5540 and demo 5541, Vite 5550, report 5551, stub
5560 (one constant, where Tandem had two literals). **Markers and variables:** `JFB_FAKE`, `JFB_TEST:`,
`database-hung.txt`, `JFB_E2E_*`, `JFB_UPDATE_*`, `JFB_INSTALL_*`, `JFB_CI`, `JFB_NUGET_PUBLISH`, `JFB_NUGET_USER`,
`JironFormBuilderTesting__*`.

## 7. CI

- **`ci.yml`:** pushes to `main` and `v18/main`, `workflow_dispatch` and `workflow_call`; read-only permissions; a
  concurrency group that names the workflow; every job gated on `JFB_CI` for pushes; jobs `build`, `sqlserver`,
  `smoke`, `loadbalancing` and a manual `load`. Its SQL Server password is per run. **Steps:** 3.5, 8.1, 16.2.
- **`release.yml`:** `prepare`, `ci` through `workflow_call`, `pack`, a draft `release`, and a `publish` job that
  stays off; inputs reach shells only through `env:`. **Steps:** 18.2.
- **`dependabot.yml`:** nuget, npm and github-actions with `open-pull-requests-limit: 0` until the owner raises it.
  **Steps:** 3.5.
- **Templates:** Jiron Form Builder's YAML issue forms, PR template and `CODEOWNERS` exist since 0.2.

## 8. Tandem's ADRs and where they apply

| Tandem ADR | Decision in one line | Steps |
| --- | --- | --- |
| 0001 Solution and packages | Core, Web, a code-less meta package and add-ons; exact sibling versions; internals by default; no committed build output | 1.7 (ADR-0001), 3.1 |
| 0005 Persistence and migrations | one plan, fixed states, idempotent steps with frozen snapshots, additive changes only | 1.3, 4.1 |
| 0008 Settings model | host options plus typed sections with locks, versions and a cache refresher | 1.7 (ADR-0008), 4.4 |
| 0010 Versioning and branching | the major follows Umbraco; `main` into `v18/main` | 1.7 (ADR-0010) |
| 0011 Testing strategy | the pyramid, a strict fake, fresh hosts per run, one sign-in at a time | 1.7 (ADR-0011), 3.4 |
| 0014 Transitive advisories | exact reviewed suppressions guarded by a script | 3.1, 16.1 |
| 0016 Identity and limits | tokens as hashes compared in fixed time; per-server limits on the remote address | 7.1, 8.6 |
| 0019 Audit and daily stats | audit inside the unit, stats after the commit | 4.3 |
| 0020 Public API surface | internal MVC controllers, own middleware for CORS, filters for limits, one ProblemDetails writer | 6.1 |
| 0021 Captcha | fail closed, configured hostnames, only secret and token sent | 7.2 |
| 0034 The section | one section, a menu of areas, permissions on every endpoint, a client condition | 9.1 |
| 0039 Settings workspace | one settings API and one workspace frame for every tab | 12.4a |
| 0042 Templates, preview, test email | server-rendered preview in a sandboxed frame, an immediate test email | 10.2, 12.2 |
| 0051 Polls and the session | an idle gate on the API client | 11.3 |
| 0052 The email queue | claims, attempts, the sending gate, `enableNotification: true` | 8.2 |
| 0053 Email kinds | throttles and caps in one statement; nothing visitor-written in a confirmation | 8.2, 8.3 |
| 0063 SQLite under load | keep the gate, make each operation cheaper, steer busy sites to SQL Server | 16.2 |
| 0064 Accessibility audit | axe with known findings pinned, keyboard walks, status colour mixing | 3.4, 16.3 |
| 0065 Transient database errors | rerun the outermost unit only | 4.3 |
| 0066 Recovering in the browser | only the API's own 404 ends something; retries reuse their ids; guarded storage | 6.4, 16.4 |
| 0067 Upgrade tests | a real site upgraded from the baseline tag, plus a JSON fixture per released schema | 16.5 |
| 0068 Public API approval | extension points only, held by an approval test | 15.1, 16.7 |
| 0070 Live checks | dropped (no real keys); its Playwright lesson stays (a failed test ends its worker) | 3.4 |
| 0075 Design refinement | tokens, not hand-picked values | 2.2, 9.6, 16.6 |
| 0076 New mail at once | the woken sender, the job as a safety net | 1.6 (ADR-0007), 8.2 |
| 0077 Release workflow | `prepare`, CI called, one pack script, publishing gated | 18.2 |
| 0078 The Umbraco 18 line | the smallest port; 17 first; documents per line | 19.1, 19.2 |
| 0079 Say it once | each fact once per screen; an own sidebar element without Umbraco's menu heading | 2.2, 9.1, §4.13 |

## 9. Gotchas

### 9.1 The gotchas of tandem-lessons.md, part 1

| Gotcha | Applies in |
| --- | --- |
| Integration tests need a direct `Microsoft.NET.Test.Sdk`, their own `appsettings.Tests.json` and `PackageMigrationsUnattended=false` | 3.4 |
| `UmbracoTestServerTestBase` needs `Umbraco.Tests.Integration.deps.json` in the output | 3.4 |
| `ModelsBuilder:ModelsMode=Nothing` in the test settings | 3.4 |
| The integration project references the meta package | 3.4 |
| `[ConfigureBuilder]` needs one fixture per configuration | 3.4, Phase 4 |
| NUnit runs public methods only: fixtures with internal types are internal classes with public methods | 3.4 onwards |
| `dotnet test --blame-hang-timeout 10m` | 3.4, 3.5 |
| Tests capture logs by overriding `ILogger<>` | 3.4, 4.3, 13.3 |
| Moving the test clock ages access tokens: sign in again after moving it | 3.4, 8.1, 11.3, 13.1 |
| .NET 10 starts `BackgroundService.ExecuteAsync` through `Task.Run` | 1.6, 8.1, 8.2 |
| `NoAppCache`, a messenger that delivers nothing, fixture reuse: `InstancePerTestCase` | 3.4, 4.4 |
| The test messenger derives from `ServerMessengerBase` | 3.4, 4.4, 14.4 |
| `IUserService.Save` does not persist a rename: `UpdateAsync` and clear the user cache | 3.4, 4.6 |
| The navigation cache lags new roots; domains match in sort order | 14.4 |
| A package with its own tables breaks a host with package migrations off: a separate test project | 3.4, 4.1 |
| SQLite `Cache=Shared` locks: one async gate, writes start with a write | 1.3, 4.1, 4.3 |
| SQLite reports nullability inverted; SQL Server index columns unordered: own schema queries | 4.1 |
| Case-insensitive text compares: hashes stored lowercase hex | 4.1, 4.3 |
| Dates come back `Unspecified`: mark them UTC on read | 4.3 |
| NULLs in unique indexes differ between SQL Server and SQLite | 4.1 |
| SQL Server `DATETIME` precision: order by (time, Id), never compare times for equality | 4.3, 11.1 |
| Once-only inserts on SQL Server: `UPDLOCK, HOLDLOCK` or `INSERT … SELECT … WHERE NOT EXISTS` | 4.3, 6.1 (place counters) |
| SQLite `LOWER` folds ASCII only: normalise search, sort and mailbox keys in C# | 4.3, 8.2 |
| Umbraco's own writers hold the SQLite write lock too; parallelism makes it worse | 1.3, 16.2 |
| Transient database errors: rerun the outermost unit of work | 4.3, 16.4 |
| The one-minute stall after the first request | 3.4 |
| Cache refreshers reach other servers after about 2 × `TimeBetweenSyncOperations` | 4.4, 8.1, 16.4 |
| Parallel backoffice sign-ins hang on SQLite: one at a time | 3.4 |
| Backoffice tokens in HTTP-only cookies; API clients send the bearer placeholder | 3.4 |
| No Data Protection application name by default: load-balanced servers share the key ring and the name | 7.1, 9.1, 17.2b |
| `EmailMessage` has one body | 1.6, 8.2 |
| `CanSendRequiredEmail()` false means Umbraco drops mail silently | 1.6, 8.2 |
| Gmail drops `<body>` styles; Outlook needs `<!--[if mso]>` tables; a long word at 375 px | 8.2, 12.2 |
| `waitForMail` retries on `EBUSY` | 3.4 |
| Mail to visitor-typed addresses is an abuse surface | 8.2, 8.3, 8.5, 8.6 |
| Umbraco's confirm modal renders strings as HTML | 9.1 |
| A controller's own `403` attribute breaks the OpenAPI document | 4.7 |
| `IFormFile` adds an implicit multipart constraint | 4.7, 6.1 |
| The `403` sweep fills route parameters by type | 4.7 |
| Error handling can drop a refusal's body; validation answers drop `code` | 4.7, 6.1 |
| Polls keep sessions alive: pause while idle; `page.clock` in tests | 11.3 |
| A notification handler that cannot be constructed blocks later ones | 4.5, 6.7, 14.3 |
| The router cancels clicks on anchors: downloads use a detached anchor | 9.2, 11.2, 11.4 |
| `localize.termOrDefault(key, null)` tells a missing text from a present one | 9.1, 14.1 |
| The backoffice needs about 920 px | 9.6, 10.4, 11.6, 12.6 |
| Umbraco's static files: no compression, no caching headers | 3.2, 6.2 |
| Precompressed copies are renewed only by `dotnet build` | 3.4, 6.2 |
| Generate the client from the committed OpenAPI snapshot | 4.7 |
| Plain-http sites need `UseHttps=false` | 1.2a, 3.3, 16.5 |
| Kestrel's body limit surfaced as a `500` | 6.1 |
| Playwright snapshots and traces record passwords | 3.4 |
| Local Network Access blocks loopback scripts from a routed page | 6.6, 7.2 |
| The API request context resolves `*.localhost` to IPv6 | 14.4 |
| `page.unrouteAll({ behavior: 'ignoreErrors' })` after routed tests | 7.2 onwards |
| Toasts are short-lived alerts: assert in your own element | Phase 9 onwards |
| Visual snapshots shift with machine rendering | 6.3 onwards |
| Removing a focused element drops focus: reconcile children | 9.3c, 11.1 |
| `event.detail === 0` marks a keyboard click | 9.3c |
| axe and `uui-menu-item`; sandboxed frames hang axe | 9.1 onwards, 10.3 |
| Lighthouse cannot read Edge's debugging port | 16.2 |
| Start the demo only through its script | 3.3 |
| A second solution file in the root makes `dotnet format` refuse | 3.1 |
| `[ApiVersion]` is not inherited | 4.7 |
| `TypeFilterAttribute` needs `Arguments` | 4.7 |
| `WriteAsJsonAsync` falls back to `RequestAborted` | 6.1 |
| `System.Threading.RateLimiting` lives with ASP.NET Core: adapters in the web project | 7.1 |
| NPoco async methods have no `params` overloads | 4.3 |
| An `AsyncLocal` set in an async method does not flow back | 4.3 |
| `IEventAggregator` dispatches on the runtime type | 4.5, 15.1 |
| Umbraco's section migration writes `umbracoUserGroup2App` directly | 4.1 |
| Transitive advisories under warnings-as-errors: exact suppression plus a guard | 3.1, 16.1 |
| Vite source maps carried TypeScript into the package | 3.2 |
| Pack through a solution filter | 3.1, 18.1 |
| `PublishRepositoryUrl=false` does not hide an explicit `RepositoryUrl` | 18.1 |
| The Marketplace schema's patterns need ajv without the `u` flag; pin the schema by SHA-256 | 18.1 |
| Heredocs with backticks and quotes break; replace with split and join | every step |
| Windows cannot delete a folder a shell sits in | 16.5, 20.1 |
| MSBuild node reuse locks temp folders | 16.5, 18.3, 20.1 |
| `node --test` needs a glob | 0.2 (done), 3.5 |
| `core.autocrlf=true` in the system git | 0.2 (done: `.gitattributes`) |
| Text damaged in transit | 0.2 (done: `check-text.mjs`), every step |
| PowerShell 5.1 turns stderr into error records | 3.5 |
| `git worktree remove` fails past MAX_PATH | 16.5 |
| The running demo locks its `bin` folder | 3.3 onwards |
| `git add -p` does not work here: apply a filtered diff with `git apply --cached` | every step |
| A stopped background task leaves its children running | every step (CLAUDE.md rule 6) |
| SchemaStore workflow schemas on npm are GPL-3.0 | 3.5 |

### 9.2 Gotchas the inventory found beyond the lessons file

Each is written once here; Tandem's source is its WORKLOG, ADRs or code, and "(inference)" marks a conclusion drawn
from reading the code.

**Migrations and schema**

| Gotcha | Applies in |
| --- | --- |
| The plan name is a stored key (`Umbraco.Core.Upgrader.State+<plan name>`); renaming it reruns every step | 4.1 |
| State `IgnoreCurrentState => false` explicitly; only the automatic plan replays | 1.3, 4.1 |
| A throwing step rolls back with its state and the boot fails: small steps, a rollback test per data step | 4.1 |
| SQLite cannot change a column's type (a new column plus a backfill); no down migrations | 4.1 onwards |
| SQLite puts foreign keys into `CREATE TABLE`: referenced tables first; a column added later gets no foreign key | 4.1 |
| From 17.5 one server migrates while the others wait (`Unattended:MigrationClaimTimeout`); the docs say to start one server first | 4.1, 17.2b |
| A migration that writes Umbraco's tables refreshes that cache on every server afterwards | 4.1 |
| A one-time data action an administrator may undo needs a key-value flag, or a replay brings it back | 4.1 |
| The pending-migration reader: unreadable counts as pending, "current" cached, "pending" re-read; tests call `Invalidate()` | 4.1, 6.1, 8.1 |
| Scripts that name the final state go stale: read it from the plan's source | 16.5 |
| A step that renames a column breaks the upgrade fixture, on purpose: steps only add | 16.5 |

**The database gate, retries and SQL**

| Gotcha | Applies in |
| --- | --- |
| Never enter the gate from an outer Umbraco scope that has written (a content *saving* handler): a deadlock; use the "-ed" notification | 4.3, 6.7, 14.3 |
| Never start parallel database work inside a gated unit | 4.3 |
| Do not cache "not SQLite" while the provider name is still empty | 4.3 |
| Lost connections are retried for reads only; nothing after `Complete()`; Umbraco's `RetryDbConnection` reruns single commands, not transactions; side effects belong after the commit | 4.3, 16.4 |
| Core classifies database errors by type name and a reflected code, referencing neither client | 4.3 |
| Polling jobs read first and write only when there is work, or they take SQLite's write lock every run | 8.1, 8.2, 13.1 |
| Every SQLite write queues behind other writers: batch counters into one write, wake only the pollers a change concerns | 4.3, 6.1, 11.3 |
| `LIKE` escaping includes `[` besides `\`, `%` and `_` | 4.3, 11.1 |
| Nested units share one transaction; notifications go out after the outer unit | 4.3, 4.5 |

**Settings, options and caches**

| Gotcha | Applies in |
| --- | --- |
| A read that races a save can re-cache the old row: a loading token | 4.4 |
| After a concurrency refusal, drop this server's cached copy | 4.4 |
| The configuration binder appends to non-empty lists: start empty, apply defaults in `PostConfigure` | 4.4 |
| Data-annotation messages do not name the key: one `IValidateOptions` names each key and collects every failure | 4.4 |
| Enums stored as text with integers refused keep their meaning when the enum grows; compare records with lists by their JSON | 4.2, 4.4 |
| An unreadable settings row gives the defaults but keeps its version | 4.4 |
| Validators take host caps as the top of their ranges | 4.4, 12.4a |
| A public `const` is copied into the consumer: values a site reads are static properties | 15.1 |

**Background work and the email queue**

| Gotcha | Applies in |
| --- | --- |
| Umbraco's job runner starts one due job per 5 s tick, its own first: time-critical work from a woken service, jobs at a minute or more | 1.6, 8.1, 8.2 |
| A job past `MaximumExecutionTime` is handed to another server: bound each run, claim each item when its work starts | 8.1, 8.2 |
| A changed job period needs no migration | 8.1 |
| A connection-pool timeout is no `DbException`, and a throwing `BackgroundService` stops the site: checks inside the logged run | 8.1, 8.2 |
| Umbraco's job runner logs whole exceptions, messages included: wrap job runs | 8.1, §4.10 |
| A wake inside an outer transaction can arrive before the commit: the safety-net job finds the item | 8.1 |
| One wake is not one batch: a run that stops reports "more" and wakes itself; purge after sending | 8.2 |
| `enableNotification: false` skips a site's own mail handler: always `true` | 8.2 |
| Queue tests: a missing pickup folder simulates a failure; `DistributedJobSettings.Delay` at an hour; remove the hosted sender | 3.4, 8.2 |
| In-memory channels lose what is queued when a site stops: what matters goes through the outbox | 8.1 |

**Email content and abuse**

| Gotcha | Applies in |
| --- | --- |
| A confirmation to a typed-in address repeats nothing the visitor wrote, and is capped per entry, mailbox and site | 8.2, 8.3 |
| Templates saved before a merge tag left still render; the editor refuses unknown tags | 8.3, 9.5b |
| Throttle team mail with one statement, not a read then a write | 8.2, 11.3 (digest) |
| A per-mailbox cap on `LOWER(address)` misses non-ASCII capitals on SQLite: a normalised mailbox column (inference) | 4.1, 8.2 |

**Captcha, rate limits and proxies**

| Gotcha | Applies in |
| --- | --- |
| The captcha action must match on client and server; a test reads the client source | 7.2 |
| Fail closed: failing open lets an attacker provoke an outage to pass | 7.2 |
| Captcha hostnames come from configuration, never the `Host` header | 7.2 |
| A site's own implementation wins whatever the registration order | 7.2, 15.1 |
| Limiter traps: stay out of `RateLimiterOptions`; built-in limiters ignore an injected clock; report `IdleDuration = null` while counting; the limit in the partition key; dispose chained limiters first | 7.1 |
| A refused start gives its permits back | 6.1, 7.1 |
| Key only on `RemoteIpAddress`, IPv6 by /64, never parse `X-Forwarded-For`; warn when internal addresses send it | 7.1, 9.1, 13.3 |
| Count a per-key limit only after the token proved ownership; never put keys or tokens in URLs | 8.5, 8.6 |

**Audit and logging**

| Gotcha | Applies in |
| --- | --- |
| An audit entry must never make a valid save fail: cap identifier lists and record a count | 4.3 |
| Audit reads too, deduplicated per user and item over 15 minutes | 11.2 |
| Audit inside the unit of work, not from notifications | 4.3 |
| MVC logs action arguments at Trace through `ToString`; records print every property: seal a `ToString` that prints the type | 6.1, §4.10 |

**Public API, controllers and errors**

| Gotcha | Applies in |
| --- | --- |
| MVC finds only public controllers: a feature provider adds internal ones | 4.7, 6.1 |
| Minimal APIs sit outside Umbraco's document grouping and filters: MVC controllers | 4.7, 6.1 |
| A stream started inside an action breaks under Umbraco's no-cache filter: stream from an `IActionResult` | 11.4 |
| `//path` requests never reach the package's routes | 4.7, 6.1 |
| An unchecked base path can capture the whole site: refuse `/umbraco`, `/App_Plugins`, `/media`, `/jfb/assets` and anything under or above them | 4.4, 6.1 |
| Model-state keys arrive as `$.field` or the body's name: normalise, report `body` only when nothing else explains it | 6.1 |
| "The most specific filter wins" is not automatic | 6.1 |
| The management API's validation answers come in two shapes | 9.1 |
| Put the exception filter on the base controller and test it per endpoint | 4.7 |
| Umbraco's section permission protects only the UI; one open "my permissions" endpoint | 4.6, 9.1 |
| Grant the section from code with `IUserGroupService.UpdateAsync`, only to groups that just got a permission | 4.1, 12.4a |
| Only the API's own ProblemDetails 404 ends a poll or forgets client state | 6.4, 11.3 |
| A consumer that stops at a `yield` never runs the code after it: claim side effects first, run them without the request's token | 8.1, 11.4 |

**OpenAPI and the generated client**

| Gotcha | Applies in |
| --- | --- |
| Umbraco's operation-id handler names only its own operations; the SDK class comes from the tag | 4.7 |
| Swashbuckle lists several request media types: trim to JSON | 6.1 |
| The public document described error bodies loosely; decide it on purpose for the headless API (inference) | 6.1, 6.6 |
| Umbraco 18's generator describes the same API differently; schema rules keep the client stable | 19.1 |
| The generated client starts without an address: gate calls on `clientReady()`; `// @ts-nocheck` on its output | 4.7, 9.1 |
| Per Umbraco line, not shared: the snapshots, the client, `THIRD-PARTY-NOTICES.md`; `umbraco-marketplace.json` stays `main`'s | 19.1 |

**Static assets and caching**

| Gotcha | Applies in |
| --- | --- |
| Fingerprint by content, not by version or file dates | 6.2 |
| A cross-origin `import()` needs CORS on the static file itself | 6.2, 6.6 |
| The site's build makes `.gz` on build and `.br` only on publish; Jiron Form Builder writes its own copies, and the install test checks `Content-Encoding` | 3.2, 18.3 |
| Cross-origin script cannot read `ETag` or `Retry-After` without `Access-Control-Expose-Headers`; `Vary` and `nosniff` exactly once | 6.1 |
| A short browser cache hides server changes: fetch with `cache: 'no-cache'` to revalidate | 6.4, 6.6 |

**The backoffice client**

| Gotcha | Applies in |
| --- | --- |
| A same-origin preview frame hands untrusted script the backoffice session: sandbox, opaque origin, a signed ticket | 10.3, §4.10 |
| The unsaved-changes guard on Umbraco's router (`willchangestate`, then the discard modal, then `pushState`) plus `beforeunload` | 9.3a, 10.3, 12.4a |
| Web Locks exist only in secure contexts | 11.3 |
| The menu sidebar-app kind always draws a heading; menu items have no badge slot: own elements | 9.1, 11.3 |
| On phones Umbraco keeps its 300 px sidebar: pages sized `100vw` at 480 px and below | 9.1, 9.6 |
| Pages that load per site drop late answers after a switch | 12.4a, 14.4 |
| Permission conditions computed into a manifest go stale: ask the server once per page load | 9.1 |
| Backoffice unit tests cannot load the backoffice runtime: runtime-free modules and source-scanning tests | 9.1, 9.3a |
| A custom-CSS cleaner neutralises references to other hosts | 12.2, §4.10 |
| Only the widget had size budgets; a form designer is much larger | 3.2, 9.6 |
| Grid items stretch to their row's height: `align-items: start` | 9.3b |
| Hand-picked radii drift and black shadows look cheap: tokens only | 2.2, 9.6 |

**Accessibility**

| Gotcha | Applies in |
| --- | --- |
| Settings-style tabs need manual activation; name by `aria-labelledby`, describe by `aria-describedby` | 9.4, 12.4a |
| Status colours on their own tints fail 4.5:1: mix text colours 60/40 with the theme's text colour | 9.1, 11.1 |
| `@axe-core/playwright` honours `iframes: false` only in legacy mode; audit the page and filter to own elements | 3.4, 9.1 |
| A keyboard-walk test catches focus traps; a toast mid-walk can fill it | 9.6, 16.3 |

**The integration test host**

| Gotcha | Applies in |
| --- | --- |
| NUnit runs a `[SetUpFixture]` only from its own assembly | 3.4 |
| Configuration added in a fixture's own `[SetUp]` comes too late: a `ConfigureHost` hook before `base.Setup()` | 3.4 |
| Umbraco.AI's store needs its own connection string: a SQLite file per test under `%TEMP%\jfb-*` | 15.3 |
| Integration tests write inside the repository (views, test databases): ignore rules | 3.4 |
| The test host does not compile `.cshtml` at run time: a Razor SDK test project with a compiled view | 3.4, 6.2 |
| Jobs in tests: `Delay` at an hour, run by hand, a second server through `ActivatorUtilities` | 3.4, 8.1 |
| The test host lacks Umbraco's distributed-cache handlers for content, domains, languages and public access | 5.5, 6.7, 14.4 |
| Management API calls in tests use an API user with client credentials; no PKCE in TestServer | 3.4, 4.7 |
| In TestServer, disposing a streamed response aborts the request | 11.4 |
| Stand-in endpoints through `UmbracoPipelineFilter.Endpoints`; middleware tests on a bare `WebApplication` | 3.4, 6.1 |
| NSubstitute cannot configure a substitute inside another's `Returns` | 3.4 |
| The time-provider scan covers only `src/`; test support read the real clock | 3.4 |
| A thread-pool starvation check counts growth beyond the minimum and takes the best of several bursts | 4.3 |
| The Testing composer reaches every referencing host: configuration plus Development plus loopback | 3.4 |

**The e2e harness and Playwright**

| Gotcha | Applies in |
| --- | --- |
| The start-up wait looked for "The Delivery API is not enabled", which disappears once it is on (now in 3.4) | 3.4 |
| The hang detector parses culture-formatted log lines and needs the console log at Information | 3.4 |
| Under load the browser can fail to fetch a backoffice file (`ERR_NO_BUFFER_SPACE`): wait 45 s and reload once | 3.4 |
| Several tests sign in as one user at once: `AllowConcurrentLogins=true` on the e2e host | 3.4 |
| Parallel specs race on settings versions: shared settings once in the set-up, other settings on their own site or host | 3.4, Phase 9 onwards |
| The e2e clock moves only `GetUtcNow`: timers keep real time, so tests run jobs through the test endpoint | 3.4, 13.1 |
| A stopped run's worker can keep holding the stub's port: a clear error, then stop it by PID | 3.4 |
| Host stdio only to its log file; state folders deleted with retries; `npm` needs `shell: true` | 3.4 |
| Some browsers try `localhost` as `::1` first: the static site listens on both | 3.4, 6.6 |
| Visual snapshots need fixed data and settled motion; baselines are per platform | 6.3 onwards |
| A re-render can steal a key press: `expect(…).toPass()`; menus fill in as each permission check answers | Phase 9 onwards |
| Backoffice journeys need 120 to 300 s per test | Phase 9 onwards |
| A failed test ends its worker, and the next runs `beforeAll` again: no in-memory "once per run" guards | 3.4 onwards |
| A fixture without dependencies uses the empty pattern `async ({}, use)` | 3.4 |
| TypeScript support files run in Node 24 only with `.ts` imports, `allowImportingTsExtensions`, `verbatimModuleSyntax`, `noEmit` | 3.4 |
| A request the client drops at a stream's end is reported as failed | 11.4 |
| Lighthouse's Edge children outlive the launcher: start Edge yourself and stop its tree by PID | 16.2 |

**Packing, release and the two Umbraco lines**

| Gotcha | Applies in |
| --- | --- |
| A tag runs the workflow file of its own commit | 18.4 |
| A new kind of packed file goes on the allowlist in the same change; a new package in the allowlist and the solution filter | 18.1 onwards |
| The Testing package is packed only into the install test's own feed | 16.5, 18.3 |
| A historic baseline build needs `-p:NuGetAudit=false` and the empty local feed folder | 16.5 |
| Umbraco's own migration state moves with a new major; within a major, check only that it is well-formed | 16.5, 19.2 |
| The install site needs a published page that renders a form | 18.3 |
| Checks that name a version read the major from the props files | 3.5 onwards, 19.1 |
| One line's gate at a time: both lines' hosts use the same ports | 19.2 |
| A worktree has its own `.local` | 19.1 |
| The notices generator ignored `NUGET_PACKAGES` (inference; now in 3.5) | 3.5 |
| The exact-version target hooks an internal NuGet target: the allowlist's exact-dependency check proves it (inference) | 3.1, 18.1 |
| `NuGet.config`'s local feed folder may be missing; restore works without it | 0.2 (done) |
| A `.gitignore` rule for `appsettings-schema*.json` swallows a shipped schema: a `!` exception | 4.4 |
| The secret scan also stops placeholder values in docs: leave the value out and say where it is set | 17.2b |

**Shell, PowerShell and Windows**

| Gotcha | Applies in |
| --- | --- |
| Windows PowerShell 5.1 drops double quotes on the way to a native program: `node -e` in single quotes | 3.3 onwards |
| Under `Set-StrictMode` an empty object has no `.Name`: `PSObject.Properties.Match('x').Count` | 3.3, 16.5 |
| `Start-Process` inherits no handles, so a long-running child never holds the caller's console | 3.3 |
| UTF-8 without a BOM: `[IO.File]::WriteAllText` with `UTF8Encoding($false)` | 3.3, 16.5 |
| A child's secrets as environment variables only for the moment of the start, then cleared | 3.3, 16.5 |
| Bash: a redirect into a missing folder still runs the earlier commands; `sed` fails on backslash patterns; `node -e` with backticks breaks | every step |
| `dotnet format` requires the `_` prefix on private fields (IDE1006); the build does not say so | every step (`fix.mjs`) |
| This PC crashed runtimes on unchanged code (CLR `0x80131506`, Node `0xC0000005`): one recorded rerun | every gate (ROADMAP "Flaky means broken") |
| Stack reports of an own process: a tool path under `%TEMP%\jfb-tools`, never a global tool | when needed |

**Process and repository hygiene**

| Gotcha | Applies in |
| --- | --- |
| A new extension point gets its working sample in the same change | 15.1, 15.2 |
| A public-surface change fails the approval test and writes a received file to review | 15.1, 16.7 |
| Docs the gate checks need their update in the same commit (configuration reference, database doc, links, README requirements) | 4.1 onwards, 17.2b |
| A parameter added before a `CancellationToken`: pass the token by name | every step |
| Run Prettier on new specs and language files, and `dotnet format` on changed C#, before the gate | every step |

## 10. What changed in the roadmap because of this inventory

Recorded in `docs/WORKLOG.md` with step 1.1:

- **§3.3 "Porting cautions":** never copy Tandem's fixed identifiers; never port its HTML regex as a sanitiser; fix
  what Tandem left behind when porting it.
- **3.4:** the start-up wait uses a marker that appears with the Delivery API on and off, with a test.
- **3.5:** the notices script reads `NUGET_PACKAGES`, with a test.
- **3.2 and 9.6:** the size check reports the backoffice bundle, and 9.6 sets its budget.
- **§4.5 and 8.2:** `jfbEmail` stores the visitor-typed address normalised in C# (`RecipientKey`) for the per-address limits, and 8.2 tests two spellings of one mailbox.
- **§4.10:** the preview frame is sandboxed with a signed ticket for its `null` origin (tested in 10.3); request
  models seal their `ToString`; job runs log types and codes only; the custom-CSS cleaner also covers `image-set()`
  and `@font-face` sources.
- **16.5:** the install test stops its whole process tree by a PID file and uses the throwaway-build settings for its
  packs too; its heading names 18.3 as the step that reuses it.
- **18.2:** the release workflow takes Tandem's final shape (`prepare`, CI through `workflow_call`, the one pack
  script).

## 11. Rename checklist

- **Names:** `Tandem*` classes to `FormBuilder*` or `Jfb*` (per ADR-0001); `tandem*` tables to `jfb*`; the options
  section `Tandem` to `JironFormBuilder`; `TandemTesting` to `JironFormBuilderTesting`; `/tandem-testing/` to
  `/jfb-testing/`; `TANDEM_*` variables to `JFB_*`; `TANDEM001` to `JFB001`; npm names to `jfb-*`; CSS tokens to
  `--jfb-*`; aliases to `Jiron.FormBuilder.*`; element tags to `jfb-*`.
- **Routes:** public `/jfb/api/v1`; management `/umbraco/jfb/management/api/v1`; assets `/jfb/assets/`; backoffice
  files `/App_Plugins/JironFormBuilder/`.
- **New identifiers, chosen once:** the migration plan name, the cache refresher id, job and HttpClient names, the
  email type, Data Protection purposes, BroadcastChannel and Web Lock names, the secret prefix.
- **Folders:** `%TEMP%\tandem-*` to `%TEMP%\jfb-*`.
- **Dropped:** the owner key file in the secret scan, `demo.ps1 -Live`, the live suites and their budget, the second
  fake provider and the chat-only fake directives.
