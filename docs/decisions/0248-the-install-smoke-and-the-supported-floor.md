# 0248. The install smoke and the supported floor

- Status: accepted
- Date: 2026-11-13
- Roadmap step: 18.3 (the install test from packages)

## Context

Step 18.3 asks the install test of 16.5 (ADR-0240) for more than it checked: on a fresh Umbraco 17.7.1 site that
installs `Jiron.Umbraco.FormBuilder` from the local feed alone, the admin sees the section; a form made through the
management API is placed on a page, renders, is sent and emails the pickup folder; the entry shows in the backoffice;
and after the build the site's `appsettings-schema.json` references Jiron Form Builder's schema, which lies next to it
(18.1, ADR-0008). The same smoke must pass after `dotnet publish` (§3.2: a release once broke only there), and on a
fresh site of the supported floor, Umbraco 17.6.0 (D3). On that floor, an update to a second build of a higher version
must reach a signed-in browser that keeps its cache without a hard refresh: Umbraco sends package files without caching
headers, and 17.6 busts them by the manifest's stamped version (§3.4).

The 16.5 check went through the APIs only, and its form had no action, so no mail could arrive.

## Options

- **Placing the form:** a seed project in the site (as the baseline's), the form picker's property on a document type,
  or a template that calls the view component by the form's alias, made through Umbraco's own Management API. The seed
  would test our seed, not the package; the picker needs a data type the smoke would have to make first; the template is
  what getting-started.md tells a developer to write.
- **Telling which build runs after the update:** a version shown in the backoffice (none exists, and adding one for a
  test says the version twice), the manifest's version (the server's answer, not what the browser ran), or the start
  chunk the browser loaded. Every build stamps its version into each file's banner, so the chunks' content hashes, and
  with them their names, differ between two builds of different versions; the entry `jfb-backoffice.js` keeps its name
  and imports the start chunk by its hashed name. A browser that ran a cached old entry loads the old start chunk (or
  fails, as the old chunk is gone); one that ran the new entry loads the new one.

## Decision

- **The smoke** (`tests/e2e/install/check.ts --mode install`, run by `scripts/install-test.ps1`):
  1. through the management API: a form from the contact template, an email action to the team saved into its draft as
     the Actions view saves it, the form published, and the public API renders it;
  2. through Umbraco's Management API: a template that shows the form with
     `@await Component.InvokeAsync("FormBuilderForm", new { form = "<alias>" })`, a document type allowing it, a page of
     that type at the root, published; the site's root then renders the form;
  3. in Edge (Playwright, a persistent browser profile in the run's folder): a visitor opens the page, every
     `/jfb/assets/` file answers 200, fills the form in, waits as a person does, sends it and sees the message in its
     place; the team's mail, by its subject with the visitor's name, lands in the pickup folder; the entries list and
     the entry's panel hold it (through the API); then the admin signs in on Umbraco's own page, the header shows
     **Form Builder**, its Overview opens, the start chunk loads, and the form's Entries view shows the entry as #1.
     The admin signs in last because Umbraco ends a user's other sessions when they sign in again
     (`AllowConcurrentLogins` is off by default): the API's session ends there, and the browser's stays for the
     update on the floor.
- **The schema reference** is checked by the script right after the site's first build.
- **`-Mode install`** runs the smoke on the line's Umbraco (17.7.1, from `Directory.Packages.props`), then publishes the
  site, starts it from the published folder alone on a fresh database (the published copy of the database is removed, so
  it installs as a new site does), and runs the smoke again with a profile of its own.
- **`-Mode floor`** runs the smoke on a fresh site of the floor, the lower bound of `Umbraco.Cms.Core`'s range in
  `Directory.Packages.props` (17.6.0), from its own template hive. It packs the same sources twice, the second time with
  a higher version (`scripts/install-test/versions.mjs`: a prerelease gains `.2`, a release its next patch); after the
  smoke on the first, the site takes the second, is built and started again, and `check.ts --mode cache` reopens the
  section in the same browser profile, which kept its cache and cookies, by its address (no hard refresh). The
  backoffice must have loaded the second build's start chunk and not the first's; `scripts/install-test/start-chunk.mjs`
  reads both names from the packed Web packages. The second build is packed into a folder of its own and copied into the
  feed, since `scripts/pack.mjs` empties its output folder of packages.
- **The AI add-on is installed beside the product** in both modes, as the listings offer it, and the smoke checks that
  its backoffice loads (18.3's review); the upgrade from a released schema installs the product alone, as the baselines
  had no add-on.
- **The site's builds fail on NuGet's NU1603:** a package reference names a lowest version, so without it a version
  missing from the feed is quietly replaced by the next higher one, which hid the floor run's first mistake.
- **`-UmbracoVersion <version>`** gives the site another Umbraco version for either mode, for a check on a newer 17.x.
- The upgrade from the released schema (`-Mode upgrade`, ADR-0240) is unchanged.

## Consequences

- A release runs the install test twice, `-Mode install` and `-Mode floor`, each with `-Version` (docs/releasing.md);
  together about half an hour, with the network for the templates and nuget.org.
- The smoke depends on Umbraco's Management API shapes for templates, document types and documents of the 17 line; a
  change there fails the smoke with the answer's status, which is the point of running it on the floor and the latest.
- The browser checks run in Edge locally. CI does not run the install test (it needs the published packages' feed and
  minutes); should it ever, `check.ts` falls back to Playwright's Chromium.
