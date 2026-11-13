# Upgrading

How Jiron Form Builder upgrades a site, what it promises across versions, and how the upgrade is tested. The release
notes of each version follow at the end, one section per release.

## What stays compatible

The policy is ROADMAP §4.14 and [ADR-0010](decisions/0010-versioning-branching-and-upgrade-policy.md); in short:

- **Versions follow Semantic Versioning within each Umbraco major.** The package's major is Umbraco's: version 17 runs
  on Umbraco from 17.6.0 up to, but not including, 18. Prereleases are named `-alpha.N`, `-beta.N` and `-rc.N`. A patch
  never needs a migration; a minor may add one, but never breaks the public C# surface, the public API, the website's
  markup and JavaScript, or the backoffice's extension points listed in [extending.md](extending.md). A breaking change
  needs a new major, or a new API version served beside the old one for the rest of the major and the whole next one.
- **The management API is the backoffice's own,** outside this promise: it may change in a minor, and the release
  notes list such a change for anyone who calls it.
- **Deprecations last a full major:** `[Obsolete]` names the replacement, and the old member keeps working for the rest
  of its major and the whole next one.
- **Data only moves forward.** The database migrations are idempotent and never go back; form definitions and settings
  carry a `schemaVersion` that code upgrades as it reads them, so a form saved by an older version opens in a newer one
  as it was.
- **Theme views are versioned.** Each view starts with a comment naming its contract version; the notes of a release
  list every view whose contract changed, so a site with its own copy of a view knows what to look at.

## Upgrading a site

1. Back up the site's database and, if the site keeps uploaded files on its own disk, the folder
   `JironFormBuilder:Files:Root` names (by default `umbraco/Data/JironFormBuilder/Uploads`).
2. Update the package: `dotnet add package Jiron.Umbraco.FormBuilder --version <version>`, then build.
3. Start the site. Umbraco runs the package's migrations at start-up when
   `Umbraco:CMS:Unattended:PackageMigrationsUnattended` is on (Umbraco's default). With it off, a user with access to
   Umbraco's Packages section runs them there. Until then Jiron Form Builder pauses: visitors see no forms rather than
   half-upgraded ones, no email goes out, the APIs answer `503 migrations_pending`, and the Overview and the **Database
   migrations** health check say so.
4. On a site with more than one server, upgrade one server first and let it finish its migrations before the others
   start the new version.

Read the release's notes below before you upgrade: they list anything a site's own code, views or scripts should look
at.

## How the upgrade is tested

Every released database schema gets a real upgrade on a real site, and a fixture that runs on SQLite and SQL Server
([ADR-0240](decisions/0240-the-upgrade-test.md); the pattern of the owner's earlier package).

### The install test

`scripts/install-test.ps1` builds packages and installs them into a fresh Umbraco site:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\install-test.ps1 -Mode upgrade
```

- **`-Mode install`** packs the current build as `<VersionPrefix>-dev.<yyyyMMddHHmm>`, creates a site from the Umbraco
  template of the version the repository develops against (in a template hive of the run's own, so the machine's
  templates stay as they are), installs the packages from a local feed only (package source mapping, a fresh package
  cache), starts the site on port 5530 and checks it: the migration state the plan in the code ends at, then a form made
  from the contact template, published, rendered by the public API, sent by a visitor and read back.
- **`-Mode upgrade`** first packs the tag `baseline/schema-v1` (the schema frozen in step 4.8) as
  `17.0.0-alpha.0.baseline` from a `git worktree`, installs it, and lets `scripts/install-test/BaselineSeed` write what a
  site of that time held, through the baseline's own services: settings, folders, forms published twice and changed
  again in a draft, entries with answers of every storage kind, a file, an approved entry. Then the site stops, takes the
  current packages and starts again: the remaining migrations run, and the checks read everything back through the
  database and today's APIs (`tests/e2e/install/check.ts`): the folders and forms in the tree, the versions and the
  draft, every entry's answers and state, the file byte for byte, the settings. A visitor then sends the upgraded form,
  and the entry takes the next number.
- **`-WriteFixture`** (with `-Mode upgrade`) writes `tests/fixtures/upgrade/schema-v1.json` from the baseline's database
  before the upgrade.
- **`-Keep`** keeps the run's folders to look into a failure; **`-Version`** packs a given version, for a release.

The run needs the network (the Umbraco template and nuget.org), Node 24 and the .NET SDK, and takes about ten minutes.
It builds with the throwaway-build settings (no MSBuild node reuse, no build server, no shared compiler), records the
site's process in `.local/install-test/site.pid.json` and stops it with its whole tree, also when a step fails, and
removes the site, the feed, the package cache and the worktree at the end, saying so. It runs before each release, not
in every gate.

### The fixtures

`tests/fixtures/upgrade/schema-v<N>.json` holds the rows a site on that released schema stored, and that site's schema:

- `UpgradeFixtureTests` (integration) runs the plan's steps up to the released state, checks that they still build
  exactly that schema, puts the rows in, runs the rest of the plan and reads everything back through today's services,
  on SQLite here and on SQL Server in CI.
- `StoredDefinitionFixtureTests` (unit) reads every form definition the released version stored with today's reader and
  upgraders, and checks that it keeps its form, pages, fields, keys, aliases, types and labels.

### When a schema is released

Each release that changes the database schema or the definition schema adds:

1. a tag `baseline/schema-v<N>` on the release's commit;
2. the seed's changes, if the services it writes through changed;
3. its fixture, written with `-WriteFixture` against that tag, and its own cases in the two fixture tests.

The install test then upgrades from the newest released schema; the fixtures keep every older one tested.

## Releases

No version has been released yet. The release step (ROADMAP Phase 18) writes the first section here, and every release
after it adds its own: what changed for sites, the migrations it runs, the theme views whose contract changed, and
anything to check.
