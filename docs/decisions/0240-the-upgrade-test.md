# 0240. The upgrade test: a real site from the released schema, and a fixture of it

- Status: accepted
- Date: 2026-11-12
- Roadmap step: 16.5 (part 16.5, upgrade and migration tests)

## Context

Part 16.5 asks for an install harness (which 18.3 reuses) that installs Jiron Form Builder from packages into a fresh
Umbraco site and upgrades one from the released schema (`baseline/schema-v1`, the schema frozen in 4.8) to the current
build, with data in it, and for a fixture per released schema stored as JSON rows, plus definition-upgrader fixtures.
The owner's earlier package proved the shape on this machine; its harness stopped only the site's own process, had no
build-server settings, and read its final migration state from a hard-coded value until a later step moved it on. Three
questions were open here:

1. **How the baseline site gets its data.** The baseline has services for forms, folders, settings and entries, but no
   submission pipeline and no management API, and every one of its services is internal.
2. **What the fixture holds**, so that it runs on SQLite and SQL Server alike.
3. **How the checks after the upgrade read the data**, through today's code rather than through its tables alone.

## Options

For the data: (a) SQL written straight into the baseline's database through `node:sqlite`; (b) reflection on the
baseline's internal services from a seed inside the site; (c) a seed project of the run's own whose assembly takes the
name of a friend assembly the baseline's Core already names (`InternalsVisibleTo Jiron.Umbraco.FormBuilder.Testing`),
so it calls the baseline's services as its own tests did (chosen).

For the fixture: (a) the baseline's database file; (b) the seeded rows and the baseline's schema as JSON (chosen).

For the checks: (a) the database only; (b) the database and the site's own APIs, signed in as its admin (chosen).

## Decision

- **`scripts/install-test.ps1`** (`-Mode install|upgrade`, `-WriteFixture`, `-Keep`, `-Version`) packs the current build
  through `scripts/pack.mjs` as `<VersionPrefix>-dev.<yyyyMMddHHmm>`, creates a site from the Umbraco template of the
  repository's Umbraco version in a template hive of the run's own under `%TEMP%\jfb-install-<timestamp>`, restores
  Jiron Form Builder only from the run's feed (package source mapping, a `NuGet.config` that clears every inherited
  source) into a fresh package cache `%TEMP%\jfb-nuget-<timestamp>`, and runs the site on port 5530 over plain http with
  the admin generated into `.local/credentials.json` and handed over through the site's environment only.
- **The throwaway-build settings everywhere:** `MSBUILDDISABLENODEREUSE=1`, `DOTNET_CLI_USE_MSBUILD_SERVER=0` and
  `UseSharedCompilation=false` for the script and all it starts, and `-nodeReuse:false -p:UseSharedCompilation=false` on
  its own builds and packs, so no build server keeps the run's folders or cache locked.
- **The site's process is recorded** in `.local/install-test/site.pid.json` with its start time and stopped with its
  whole tree (`taskkill /T /F /PID`), on success and on failure; a later run stops a site an earlier one left only while
  the record still names that very process. At the end the script says that the process is gone and that the site, the
  feed, the cache and the worktree are removed (Node's `fs.rmSync` past MAX_PATH, then `git worktree prune`).
- **The upgrade** checks out the tag with `git worktree add` into `%TEMP%\jfb-baseline-<timestamp>`, builds its client
  and packs it as `17.0.0-alpha.0.baseline` with the audit off (a historic build that ships nowhere), installs it on
  the baseline's own Umbraco, and starts the site with `JFB_INSTALL_SEED`: `scripts/install-test/BaselineSeed` writes,
  through the baseline's own services, the general settings, a folder with a folder in it, a form published twice and
  changed again in its draft, a form at the root, four entries with answers of every storage kind and a file, one
  approved, and leaves what the checks read back in a file of the run. The site stops, drops the seed, takes the current
  packages and starts again with `UpgradeUnattended`.
- **The expected states come from the code:** `scripts/install-test/migration-plan.mjs` reads the plan's steps, their
  states and the plan's stored name from the sources (the repository's, and the baseline's from its worktree), so a new
  step never leaves the test expecting an old state.
- **The checks:** `scripts/install-test/jfb-db.mjs` (Node's own SQLite, read-only) finds the plan's last state and every
  seeded row with its answers; `tests/e2e/install/check.ts` signs in through Umbraco's login and PKCE flow and reads the
  folders and forms in the tree, the versions and the draft, every entry's state and answers in its panel, the file byte
  for byte, and the settings, then sends the upgraded form as a visitor and finds the entry with the next number. In
  install mode it makes, publishes, renders, sends and reads a form of the contact template.
- **The fixture** is `tests/fixtures/upgrade/schema-v1.json`: the baseline's schema in the form of the integration tests'
  `SchemaInspector`, and every row the seed wrote. `UpgradeFixtureTests` runs the plan's first three steps, checks they
  build exactly that schema, puts the rows in with new ids (forms point at their versions after the versions are in; the
  general settings replace the defaults), runs the rest and reads everything through today's services.
  `StoredDefinitionFixtureTests` reads every stored definition with today's reader and upgraders, checks that it keeps
  its form, pages and fields, and that a rewrite reads back the same.

## Consequences

- The data is what the baseline's own code wrote, so a test that passes on it says something about real sites: the
  hashes, the catalogue, the stored names and the search texts are the baseline's.
- The fixture runs on both databases, pins the frozen first steps to the schema a real site of that time had, and a
  review can read what it holds.
- The seed takes a friend assembly's name the baseline already granted; a later baseline that drops that grant needs a
  seed of another kind.
- The install test needs the network (the template and nuget.org) and about ten minutes, so it runs before each release
  (18.3) and at a phase review, not in every gate. `docs/upgrading.md` describes it and what each released schema adds.
