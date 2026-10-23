# 0010. Versioning, branching and the upgrade policy

- Status: accepted
- Date: 2026-10-23
- Roadmap step: 1.7a (decided); used by 3.1 (the version), 4.1 and 4.8 (migrations and the schema baseline), 15.1
  (the public surface), 16.5 (the upgrade test), 18.x (packing and release) and Phase 19 (the 18 line)

## Context

D16 sets the outline: the package major follows the Umbraco major; the first line is `17.0.0-alpha.N`, then
`-beta.N`, then `17.0.0`; `main` is the 17 line and holds every ROADMAP and WORKLOG change; 18 code lives only on
`v18/main`; `main` is merged into `v18/main`, never the reverse; the version lives in `Directory.Build.props`. D21
asks for upgradability by design: Semantic Versioning, idempotent migrations, schema versions and upgraders for
definitions and settings, a fixture and an upgrade test for every released schema, versioned public APIs,
deprecations that last a major, and `docs/upgrading.md`. §4.14 adds the public surface and versioned theme views. D3
puts 17 first: the 18 line never costs `main` a feature, a fix, a delay or a compromise.

Tandem used the same scheme (Tandem ADR-0010, ADR-0067, ADR-0077, ADR-0078) and showed what a second line costs:
OpenAPI moved generators between 17 and 18, the snapshots and the generated client differ per line, and every
hard-coded "17" in a script broke on the 18 branch. Nothing is published during the build (D17).

## Options

1. **SemVer independent of Umbraco** (1.x for CMS 17, 2.x for CMS 18). Usual SemVer, but compatibility is invisible
   in the number.
2. **The major follows the Umbraco major; minor and patch are Jiron Form Builder's** (D16). Compatibility is obvious,
   and Jiron Form Builder's own breaking changes wait for the next Umbraco major.
3. **Calendar versions.** They say nothing about compatibility.

## Decision

**Option 2.**

**Numbers:** `<UmbracoMajor>.<minor>.<patch>[-prerelease]`.

- **Minor:** new features, settings and migration steps. Never a break of the public surface.
- **Patch:** fixes only, and never a migration.
- **Prereleases:** `-alpha.N` until the feature set is complete, then `-beta.N`, then `-rc.N` when a release
  candidate is needed. This extends D16 by `-rc`.

**Where the version comes from:**

- `Directory.Build.props` holds `VersionPrefix` 17.0.0 and `VersionSuffix` alpha.0 (alpha.1 at the first prerelease,
  18.4). Release builds pass the tag's version (`-p:Version=${tag#v}`).
- Local and test packs use `-dev.<yyyyMMddHHmm>`, so no package cache serves stale bits.
- `umbraco-package.json`'s `version` and the asset fingerprints come from the same value (ADR-0001).
- Scripts and tests read the Umbraco major and the version from `Directory.Build.props` and
  `Directory.Packages.props`, never from a literal ("One source for every value").

**Dependency ranges:** Umbraco `[17.5.0, 18.0.0)` (D3). A range widens only when CI has tested the new versions,
and CI also runs against the latest 17.x.

**The public surface** (§4.14), listed in `docs/extending.md`:

- public C# types and members, the notifications and the extension interfaces;
- the public HTTP API `/jfb/api/v1` and its error codes;
- the definition schema (ADR-0002), the public projection and its TypeScript types;
- the website JS API (`window.JironFormBuilder`), the DOM events and the `data-jfb-*` attributes;
- theme view names, the markup contract and the CSS custom properties (ADR-0015);
- host configuration keys.

Within a major it changes only by addition. A replaced member carries `[Obsolete]` naming its replacement, keeps
working for the rest of the major, and goes only at the next major. Package validation (`EnablePackageValidation`,
with a baseline from the first stable release on) and the public-surface snapshot (15.1) enforce it. A breaking change
to the HTTP API is a new version (`/jfb/api/v2`) served beside the old one for at least the rest of the major.

**The management API** (`/umbraco/jfb/management/api/v1`) belongs to the backoffice, which ships in the same
package version. It changes by addition where it can, but it is not part of the guarantee; a change that could break
a site's own script is listed in the upgrade notes.

**Data:**

- **Database migrations** are idempotent and forward-only steps of the plan `Jiron.Umbraco.FormBuilder` (§4.5), each
  with a frozen schema snapshot. There are no down migrations; a downgrade restores a backup.
- **Definitions and settings** carry `schemaVersion` and are upgraded in code (ADR-0002, ADR-0008). Stored history is
  never rewritten.
- **Fixtures:** each released database schema keeps upgrade fixtures as JSON rows (Tandem ADR-0067), so one fixture
  runs on SQLite and SQL Server; each released definition and settings schema keeps stored documents. The tag
  `baseline/schema-v1` (4.8) is the old version the upgrade test (16.5) installs first.
- **A newer document on an older site** is refused with `unsupported_schema_version`, never read in part.

**Theme views are versioned** (§4.14). Each starts with a comment naming its contract and version, in the form
ADR-0015 fixes; the upgrade notes list every view whose contract changed, so sites with overrides know what to check.

**Branches and tags:**

- `main` is the Umbraco 17 line and holds every ROADMAP, WORKLOG and docs change. While the roadmap runs, work lands
  on `main`, one step at a time (CLAUDE.md rule 9).
- `v18/main` is branched from `main` in 19.1 and holds only the Umbraco 18 code. It is worked on through
  `git worktree` in `%TEMP%\jfb-v18`, so `main`'s folder never switches branches. `main` is merged into `v18/main`
  after every later change to `main`; `v18/main` is never merged back.
- **17 first:** every feature and fix lands on `main` and is released for 17 first. A change on `main` for the 18
  line's sake must not change what 17 does, and `main`'s gate and install test prove it (D3).
- **Tags:** `v17.x.y[-pre]` on `main`, `v18.x.y[-pre]` on `v18/main`. The release workflow builds draft releases from
  them and pushes nothing to NuGet while `JFB_NUGET_PUBLISH` is not `true` (D17, 18.2).
- **After the first release,** a patch for an older minor gets a `release/17.<minor>` branch cut from its tag. Work
  moves to pull requests once outside contributions are possible (the contributor agreement, OWNER-TODO).

**The changelog:** Keep a Changelog in `CHANGELOG.md`. `Unreleased` changes in the same commit as the code; a
release moves it under its version heading, and the release workflow refuses a tag without that section (18.2).
**`docs/upgrading.md`** has one section per release: migrations, changed view contracts, deprecations and anything an
operator must do.

**Reproducible builds:** `Deterministic` on, `ContinuousIntegrationBuild` in CI, SourceLink, and
`PublishRepositoryUrl=false` while the repository is private (§3.5).

## Consequences

- A site sees from the number which Umbraco major a version is for, and NuGet refuses mismatches through the ranges.
- Jiron Form Builder's own breaking changes are batched to Umbraco majors, which keeps upgrades predictable but needs
  deprecation discipline; package validation and the surface snapshot hold it.
- Two lines live side by side after Phase 19. Every fix needs a decision whether it reaches 18 too (it does by the
  next merge), and the WORKLOG records it.
- The upgrade test, the stored fixtures and the strict reader make every format change explicit.
- **Revisit** if Umbraco changes its own versioning, or when Umbraco 17 reaches its end of life (2028-11-27).
