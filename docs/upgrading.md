# Upgrading

How Jiron Form Builder upgrades a site, what it promises across versions, and how the upgrade is tested. The release
notes of each version follow at the end, one section per release.

## What stays compatible

The policy is [ADR-0010](decisions/0010-versioning-branching-and-upgrade-policy.md); in short:

- **Versions follow Semantic Versioning within each Umbraco major.** The package's major is Umbraco's: version 17 runs
  on Umbraco from 17.6.0 up to, but not including, 18, and version 18 on Umbraco from 18.1.0 up to, but not
  including, 19. Prereleases are named `-alpha.N`, `-beta.N` and `-rc.N`. A patch
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
   Umbraco's Packages section runs them there, with **Run pending package migrations** on Jiron Form Builder's row under
   Installed. Until then Jiron Form Builder pauses: visitors see no forms rather than
   half-upgraded ones, no email goes out, the APIs answer `503 migrations_pending`, and the Overview and the **Database
   migrations** health check say so.
4. On a site with more than one server, upgrade one server first and let it finish its migrations before the others
   start the new version.

Read the release's notes below before you upgrade: they list anything a site's own code, views or scripts should look
at.

## Moving a site from Umbraco 17 to 18

Version 18 of the packages runs on Umbraco 18.1 or later (within 18), version 17 on Umbraco 17.6 or later (within 17).
A site moves its Umbraco and Jiron Form Builder together:

1. Back up as for any upgrade, and read Umbraco's own notes for version 18.
2. Raise every Umbraco package the site references (`Umbraco.Cms`, `Umbraco.Cms.DevelopmentMode.Backoffice` and any
   other `Umbraco.Cms.*`) to the same 18.x, and `Jiron.Umbraco.FormBuilder` to 18.x. With the AI add-on, raise
   `Jiron.Umbraco.FormBuilder.AI` and the site's Umbraco.AI packages to 18.x too. Then build.
3. Start the site. Umbraco upgrades its own database first: on its own with
   `Umbraco:CMS:Unattended:UpgradeUnattended`, otherwise on its upgrade screen after a user signs in. Jiron Form
   Builder's migrations then run as for any upgrade.

What stays as it was:

- **The data.** Both lines share one database schema and one migration plan, so the site keeps its folders, forms with
  their versions and drafts, entries with their answers, states and files, and settings. Only the migrations the 18
  release has and the site's 17 release lacked run. Before each release the install test moves a 17 site with data to
  18 and reads everything back ([upgrade-testing.md](upgrade-testing.md)).
- **Everything a site builds on:** the public API under `/jfb/api/v1`, the website's markup, theme views, scripts and
  events, merge tags, webhooks and the C# extension points in [extending.md](extending.md). Code of the site's own that
  uses them builds against version 18 unchanged, unless it also uses something Umbraco 18 removed.

What differs:

- **The OpenAPI documents** Umbraco serves outside production are OpenAPI 3.1 at `/umbraco/openapi/jfb-public.json`
  (and `jfb.json`, `jfb-ai.json`), where Umbraco 17 served them through Swagger at
  `/umbraco/swagger/<name>/swagger.json`. They describe the same operations, answers and models; a free JSON value is
  written as an open object. The 18 line's snapshot is [api/jfb-public.json](api/jfb-public.json).
- **The form's header on a narrow window.** Umbraco 18.3 gives a workspace's view tabs their whole width first and
  the header what is left. Beside all of a form's views on a narrow window (about 1,100 px in German), that is less
  than the form's name needs, and the name and the state wait out of sight until a wider window leaves them room; the
  form stays selected in the tree.
- **Backoffice extensions** that build on Jiron Form Builder's backoffice extension points build against
  `@umbraco-cms/backoffice` 18.

## How the upgrade is tested

Every released database schema gets a real upgrade on a real site before each release, and a fixture of its rows that
the tests upgrade on SQLite and SQL Server ([ADR-0240](decisions/0240-the-upgrade-test.md)).
[upgrade-testing.md](upgrade-testing.md) describes both, for contributors.

## Releases

Each release adds its own section here, newest first: what changed for sites, the migrations it runs, the theme views
whose contract changed, and anything to check. [CHANGELOG.md](../CHANGELOG.md) lists every change.

### 17.0.0-alpha.1 (2026-11-13)

The first prerelease, for Umbraco 17.6 or later (within 17). There is nothing to upgrade from: install it on a site
you can reset, and expect changes before 17.0.0.

- **Migrations:** the plan runs from `jfb-001-create-schema` to `jfb-015-entry-site` on the first start
  ([database.md](database.md#migrations) lists each step). This schema is the released schema
  `baseline/schema-v2`: from this version on, every upgrade is tested from it with data
  ([how upgrades are tested](upgrade-testing.md)).
- **Theme views:** every view carries contract version 1.
- **Not yet included:** saving a form to continue later through an emailed link.
