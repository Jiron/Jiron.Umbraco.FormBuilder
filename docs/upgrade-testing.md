# How upgrades are tested

For contributors: how every released schema is kept upgradable. Site developers find what an upgrade does and promises
in [upgrading.md](upgrading.md).

Every released database schema gets a real upgrade on a real site, and a fixture that runs on SQLite and SQL Server
([ADR-0240](decisions/0240-the-upgrade-test.md)).

## The install test

`scripts/install-test.ps1` builds packages and installs them into a fresh Umbraco site:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\install-test.ps1 -Mode upgrade
```

- **`-Mode install`** packs the current build as `<VersionPrefix>-dev.<yyyyMMddHHmm>`, creates a site from the Umbraco
  template of the version the repository develops against (in a template hive of the run's own, so the machine's
  templates stay as they are), installs the packages from a local feed only (package source mapping, a fresh package
  cache), and checks after the build that the site's `appsettings-schema.json` references Jiron Form Builder's schema,
  which lies next to it. It starts the site on port 5530 and runs the smoke
  ([ADR-0248](decisions/0248-the-install-smoke-and-the-supported-floor.md)): the migration state the plan in the code
  ends at; a form made from the contact template with an email action to the team and published; a page that shows it,
  made and published through Umbraco's own Management API; then in Edge the admin signs in and opens the Form Builder
  section, a visitor sends the form on the page, the team's mail lands in the pickup folder, and the entry shows in the
  form's Entries view. Then it publishes the site (`dotnet publish`), starts it from the published folder on a fresh
  database and runs the smoke again, for the static files after publishing.
- **`-Mode floor`** runs the same smoke on a fresh site of the supported floor, the lower bound of the Umbraco range in
  `Directory.Packages.props` (17.6.0). It packs the build twice, the second time with a higher version; after the smoke
  the site takes the second build, starts again, and the same browser, which kept its cache and its sign-in, opens the
  section without a hard refresh: it must load the second build's start chunk, not the first one's.
- **`-UmbracoVersion <version>`** gives the site of either mode another Umbraco version.
- **`-Mode upgrade`** first packs a released schema's tag from a `git worktree` as its own version with `.baseline`:
  by default the newest, `baseline/schema-v2` (the schema 17.0.0-alpha.1 released, packed as
  `17.0.0-alpha.1.baseline`); `-Baseline baseline/schema-v1` takes the first schema, frozen before the first release
  (`17.0.0-alpha.0.baseline`). It installs the baseline and lets `scripts/install-test/BaselineSeed` write what a site
  of that time held, through the baseline's own services: settings, folders, forms published twice and changed
  again in a draft, entries with answers of every storage kind, a file, an approved entry. Then the site stops, takes
  the current packages and starts again: the remaining migrations run, and the checks read everything back through the
  database and today's APIs (`tests/e2e/install/check.ts`): the folders and forms in the tree, the versions and the
  draft, every entry's answers and state, the file byte for byte, the settings. A visitor then sends the upgraded form,
  and the entry takes the next number.
- **`-WriteFixture`** (with `-Mode upgrade`) writes the baseline's fixture, `tests/fixtures/upgrade/schema-v<N>.json`
  after its tag, from the baseline's database before the upgrade.
- **`-Keep`** keeps the run's folders to look into a failure; **`-Version`** packs a given version, for a release.

The run needs the network (the Umbraco template and nuget.org), Node 24, the .NET SDK and Edge, and takes ten to twenty
minutes.
It builds with the throwaway-build settings (no MSBuild node reuse, no build server, no shared compiler), records the
site's process in `.local/install-test/site.pid.json` and stops it with its whole tree, also when a step fails, and
removes the site, the feed, the package cache and the worktree at the end, saying so. It runs before each release, not
in every gate.

## The fixtures

`tests/fixtures/upgrade/schema-v<N>.json` holds the rows a site on that released schema stored, and that site's schema:

- `UpgradeFixtureTests` (integration) runs the plan's steps up to the released state, checks that they still build
  exactly that schema, puts the rows in, runs the rest of the plan and reads everything back through today's services,
  on SQLite here and on SQL Server in CI.
- `StoredDefinitionFixtureTests` (unit) reads every form definition the released version stored with today's reader and
  upgraders, and checks that it keeps its form, pages, fields, keys, aliases, types and labels.

## When a schema is released

Each release that changes the database schema or the definition schema adds:

1. a tag `baseline/schema-v<N>` on the release's commit;
2. the seed's changes, if the services it writes through changed;
3. its fixture, written with `-WriteFixture` against that tag, and its own cases in the two fixture tests.

The install test then upgrades from the newest released schema; the fixtures keep every older one tested. The
released schemas so far:

| Tag | Released with | Plan's last state |
| --- | --- | --- |
| `baseline/schema-v1` | none: the schema frozen at the end of Phase 4, before the first release | `jfb-003-grant-forms-section` |
| `baseline/schema-v2` | 17.0.0-alpha.1 | `jfb-015-entry-site` |
