# Contributing to Jiron Form Builder

Thank you for your interest. This page says how work is done in this repository.

## Outside contributions are not open yet

Pull requests from outside contributors can be accepted only once a contributor licence agreement exists
(it is being prepared). Until then:

- please open an issue to report a bug or suggest a feature (see [SUPPORT.md](SUPPORT.md));
- please report security problems privately (see [SECURITY.md](SECURITY.md)).

## Licence and original work

Jiron Form Builder is free and source-available under the [PolyForm Perimeter License 1.0.1](LICENSE.md). It
is not "open source" in the OSI sense. Every contribution must be your own original work:

- never copy, decompile or derive code, markup, styles, text or icons from another product, open or closed;
- feature ideas may come from public descriptions, but write them down in your own words and design them
  your own way;
- dependencies need a licence that the project accepts (MIT, Apache-2.0, BSD-2/3-Clause, ISC or 0BSD for
  anything that ships to sites), listed in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

## Working in the repository

**Requirements:** the .NET SDK named in [global.json](global.json), Node.js 24 and git.

**Once per clone**, install the git hooks:

```
node scripts/install-hooks.mjs
```

The `pre-commit` hook checks the commit identity and runs these checks on the staged files; the
`commit-msg` hook runs the term check on the message:

| Check | What it refuses |
| --- | --- |
| `node scripts/secret-scan.mjs` | key-, token- and password-shaped values, and files such as `.env` or `*.Local.json` |
| `node scripts/term-guard.mjs` | terms from the maintainer's private list (skipped where the list is not present) |
| `node scripts/check-text.mjs` | control characters, byte-order marks and damaged encodings, and non-ASCII characters in code |

While the repository is being built, the identity check accepts only the maintainer's commit identity. It
will be opened up together with outside contributions.

**Test values** that look like secrets must contain the marker `JFB_FAKE`, so the secret scan can tell them
apart from real ones. Real keys never go into the repository, not even in tests.

**Commits** follow [Conventional Commits](https://www.conventionalcommits.org/) (`feat:`, `fix:`, `docs:`,
`test:`, `chore:` and so on), one change per commit, with docs updated in the same commit as the code they
describe.

**The client** (`src/Jiron.Umbraco.FormBuilder.Web/Client`, an npm workspace) is built before a pack:

```
cd src/Jiron.Umbraco.FormBuilder.Web/Client
npm ci
npm run lint
npm test
npm run build
npm run size
```

`npm test` gives every test a minute, which is a hang's limit and no measure: the gate runs these tests beside the
.NET build, where a test takes several times what it takes alone. A test must not depend on how long something takes;
what holds the product to a time budget is the browser tests' `budgets` project and the integration tests marked
`Timed`, which run alone.

`node scripts/pack.mjs` builds the client for the version it packs and writes the packages to
`.local/nuget-feed`. A plain `dotnet pack` refuses client assets that are missing, stale or built for
another version (`JFB001`).

**The tests** ([tests/README.md](tests/README.md)): the unit and integration tests run on SQLite and need nothing
installed. The integration tests each start an Umbraco, so a script runs them in several processes at once and then
the tests that measure time alone; it never builds, and it fails unless every test of the assembly ran once.

```
dotnet build Jiron.Umbraco.FormBuilder.slnx -c Release
dotnet test tests/Jiron.Umbraco.FormBuilder.Tests.Unit -c Release --no-build --blame-hang-timeout 10m
node scripts/test-integration.mjs
```

A test that holds the product to a time budget gets `[Category("Timed")]`; no other test may depend on how long
something takes.

The browser tests (Playwright, [tests/e2e/README.md](tests/e2e/README.md)) start a fresh demo host on port 5520 and
run in Edge:

```
cd tests/e2e
npm ci
npx playwright test --project=smoke
```

The gate runs every browser project on two sites side by side, each with its own host, ports, database and
people, every spec file on one of them, and the `budgets` project alone after them; it fails unless every test ran
once ([Several sites](tests/e2e/README.md#several-sites)):

```
node scripts/e2e-gate.mjs
```

The shared vectors in `tests/vectors/` run in the unit tests and in the client's `npm test`. After changing a suite in
`tests/vectors/suites/`, write the vector files again:

```
node tests/vectors/build.mjs
```

**The documentation** (`docs/`, written in the same commit as the code it describes): `node scripts/link-check.mjs`
finds every relative link and heading anchor in the pages and the backoffice's "Learn more" map, offline, and the
scripts' tests run it. `node scripts/render-markdown.mjs` shows `README.md` and `docs/README.nuget.md` (or the files you
name) as GitHub renders them, in Edge, and saves a shot of each in `.local/markdown/`; look at them after changing a
README. `docs/configuration.md` is generated: after changing a setting of the host configuration, run
`node scripts/configuration-docs.mjs`, or the scripts' tests fail.

```
cd scripts
npm ci
cd ..
node scripts/link-check.mjs
node scripts/render-markdown.mjs
```

**Before you push**, format everything, then run the gate. `scripts/ci-local.ps1` runs what CI runs, except the SQL
Server and load-balancing jobs (`.github/workflows/ci.yml`), stops at the first failure and prints the time of every
stage. Its cheap checks and builds run as lanes side by side (`scripts/lanes.mjs`), each line of output marked with its
lane; its script header lists what each stage holds. `-SkipE2E` leaves out the browser tests for a quicker run while
working; that run is not the gate.

```
node scripts/fix.mjs
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\ci-local.ps1
```

To go on working while the gate runs, commit and run it on that commit in a throwaway clone under `%TEMP%`; `status`
and `stop` follow it, and its log and verdict land in `.local/gate/`
([ADR-0117](docs/decisions/0117-long-checks-run-beside-the-next-step.md)). Keep this folder off the browser
projects' ports while it runs.

```
node scripts/gate-clone.mjs run HEAD
node scripts/gate-clone.mjs status
```

Before a release, the clean-clone verification (ROADMAP 20.1) clones origin's `main` into `%TEMP%` with a NuGet
cache of its own and runs there `ci-local.ps1`, a pack, the install tests, the upgrade from the first prerelease and
the secret scan of the whole history; the term guard of the whole history runs here. It removes its folders
afterwards and prints a summary; its logs land in `.local/verify/`.

```
node scripts/verify-clean.mjs
```

CI runs on a push only when the repository variable `JFB_CI` is `true`, and always when it is started by hand or for
a version tag.

**The demo site** (`demo/`, Umbraco 18 on SQLite at http://127.0.0.1:5541) is started, stopped and reset only with
`scripts/demo.ps1`, which generates its accounts into the git-ignored `.local/credentials.json`;
[demo/README.md](demo/README.md) says what it holds.

```
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\demo.ps1 start
node scripts/demo-check.mjs
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\demo.ps1 stop
```

## Where things are

- [ROADMAP.md](ROADMAP.md): the plan, worked through in order.
- `docs/decisions/`: the architecture decision records (ADRs).
- `docs/WORKLOG.md`: what was done, step by step.
