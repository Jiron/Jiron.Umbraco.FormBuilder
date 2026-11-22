# Upgrading

How Jiron Form Builder upgrades a site, what it promises across versions, and how the upgrade is tested. The release
notes of each version follow at the end, one section per release.

## What stays compatible

The policy is [ADR-0010](decisions/0010-versioning-branching-and-upgrade-policy.md); in short:

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
   Umbraco's Packages section runs them there, with **Run pending package migrations** on Jiron Form Builder's row under
   Installed. Until then Jiron Form Builder pauses: visitors see no forms rather than
   half-upgraded ones, no email goes out, the APIs answer `503 migrations_pending`, and the Overview and the **Database
   migrations** health check say so.
4. On a site with more than one server, upgrade one server first and let it finish its migrations before the others
   start the new version.

Read the release's notes below before you upgrade: they list anything a site's own code, views or scripts should look
at.

## How the upgrade is tested

Every released database schema gets a real upgrade on a real site before each release, and a fixture of its rows that
the tests upgrade on SQLite and SQL Server ([ADR-0240](decisions/0240-the-upgrade-test.md)).
[upgrade-testing.md](upgrade-testing.md) describes both, for contributors.

## Releases

Each release adds its own section here, newest first: what changed for sites, the migrations it runs, the theme views
whose contract changed, and anything to check. [CHANGELOG.md](../CHANGELOG.md) lists every change.

### 17.0.0-beta.1 (2026-11-22)

The first beta, for Umbraco 17.6 or later (within 17). A site on 17.0.0-alpha.1 keeps its forms, entries and
settings: the upgrade was tested from that version's schema with data.

- **Migrations:** none. The plan still ends at `jfb-015-entry-site`, and the released schema stays
  `baseline/schema-v2` ([how upgrades are tested](upgrade-testing.md)).
- **Theme views:** every view still carries contract version 1.
- **To check:**
  - The website runtime may weigh up to 100 KB gzip with every feature (it was 60 KB); what every page with a form
    loads first stays within 16 KB.
  - A package's menu item that named the Settings menu is now a tab at the end of Settings' tabs
    ([extending.md](extending.md)).
  - Saving a form for later answers with a `resume` value in the public API, which the next save and the entry send
    back.
  - A user group other than the administrators needs its permissions in Form Builder › Settings › Access, as
    before; its screen in Umbraco's Users › User groups now shows them ([permissions.md](permissions.md)).

### 17.0.0-alpha.1 (2026-11-13)

The first prerelease, for Umbraco 17.6 or later (within 17). There is nothing to upgrade from: install it on a site
you can reset, and expect changes before 17.0.0.

- **Migrations:** the plan runs from `jfb-001-create-schema` to `jfb-015-entry-site` on the first start
  ([database.md](database.md#migrations) lists each step). This schema is the released schema
  `baseline/schema-v2`: from this version on, every upgrade is tested from it with data
  ([how upgrades are tested](upgrade-testing.md)).
- **Theme views:** every view carries contract version 1.
- **Not yet included:** saving a form to continue later through an emailed link.
