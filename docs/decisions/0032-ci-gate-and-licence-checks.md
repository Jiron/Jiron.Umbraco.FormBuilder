# 0032. CI, the local gate and the licence checks

- Status: accepted
- Date: 2026-10-23
- Roadmap step: 3.5 (decided and built); 8.1 fills in the load-balancing job; 18.1 adds the package allowlist;
  18.2 moves the tag runs into the release workflow

## Context

Step 3.5 ports Tandem's CI, its local mirror and its repository checks (ROADMAP section 3.3), and adds a NuGet
licence check that Tandem never had. Five things had to be decided that the roadmap leaves open or that porting
showed.

**Umbraco's own packages are not within rule 4's lists.** The solution restores 342 NuGet packages. 326 of them
come only through Umbraco CMS packages, and many of those carry no SPDX expression: Examine is MS-PL, Lucene.Net
and NPoco name a licence file, several older packages name a licence URL or nothing, and SQL Server support brings
two packages under Microsoft's licence terms. Every Umbraco site runs these already, and rule 4 names Umbraco HQ's
packages first. Of the other 16, only NUnit lacks an SPDX expression: its nuspec names `LICENSE.txt`, which holds
the MIT licence.

**Tandem's expression reader** stripped one pair of outer parentheses with a regular expression, so
`(MIT OR GPL-3.0-only) AND (SSPL-1.0 OR AGPL-3.0-only)` read as an OR with an acceptable side and passed.

**The hand-written part of THIRD-PARTY-NOTICES.md** (the development tools) has no check, so a Dependabot update
would leave its versions behind, and a new tool could arrive without a row.

**Scripts that open a browser** used Edge's channel everywhere. GitHub's Linux runners are not this machine, and
D19 already says Chromium in CI.

**Packing after the gate's build:** `scripts/pack.mjs --no-build` without `--version` packed a new test version
(`-dev.<time>`), which never matches the assets that the earlier build stamped, so the pack check (JFB001) would
always refuse it.

## Options

1. **NuGet licences:**
   - judge every package by rule 4's lists, with an exception for each of Umbraco's 30-odd others: every Umbraco
     update would need new exceptions, and the list would review Umbraco's choices rather than Jiron Form
     Builder's;
   - leave Umbraco's graph out entirely, as Tandem's notices did: a GPL package that Umbraco might bring one day
     would pass unseen;
   - **three tiers by where a package comes from, each with its own rule.**
2. **Licence files:** accept a file when its text looks like a known licence (a phrase test), or accept a reviewed
   file by its hash.
3. **The development table:** generate it (the "used for" column would move into the script), or **check it
   against the restored packages.**
4. **The load-balancing job before 8.1:** leave it out until 8.1, write a job that pretends to test, or **write the
   job with its service and database, and run its suite only once the specs exist.**

## Decision

- **Three tiers** (`scripts/lib/nuget.mjs`), taken from every project's restore graph:
  - **shipped:** the packages' solution filter brings it, and no Umbraco CMS package does. Rule 4's shipped list,
    or a reviewed exception;
  - **dev:** only the tests and the demo bring it. Rule 4's development list and ADR-0027's dedications, or a
    reviewed exception;
  - **platform:** an Umbraco CMS package brings it in every graph it is in. Umbraco's choice, which every site runs,
    so it is accepted, and refused only when its expression, its licence file or its licence URL names a family
    that rule 4 forbids anywhere: the GPL family, SSPL, strong copyleft of the same kind, and source-available or
    non-commercial terms. "Commercial" alone is not such a mark; the Apache licence says it too.

  A package in several graphs takes its strictest tier.
- **Reviewed exceptions are pinned to the licence text:** an exception names the package, the tiers it may appear
  in, and the SHA-256 of each licence file that was read (line ends and outer white space normalised). A new
  version with other terms fails until someone reads it. The only one is NUnit 3.14.0, for development.
- **A real SPDX reader** (`scripts/lib/licences.mjs`): AND before OR, parentheses, `WITH` exceptions, and the usual
  spellings ("BSD 3-Clause"). The npm check, the NuGet check and the notices share it, and a test holds the case
  above.
- **npm tiers by the lock file:** an entry the lock marks `dev` is a development tool; anything else, `devOptional`
  included, is judged as shipped code.
- **THIRD-PARTY-NOTICES.md:** `scripts/third-party-notices.mjs` writes the shipped table and the list of packages
  that only ADR-0027 allows, and checks the development table: every direct development tool (each npm project's
  devDependencies, and the NuGet packages only the tests and the demo reference) must have a row with the versions
  restored now and the licence its metadata gives, and a row for a package nothing restores fails. It reads the
  nuspecs from `NUGET_PACKAGES` when that is set, as CI sets it, and from the default cache otherwise.
- **The audit guard** also refuses a direct ImageSharp reference that nothing suppresses (rule 4), a suppression for
  it outside the host-only group, and a suppression outside any ItemGroup; it ignores XML comments.
- **The workflow check** also requires `main` and `v18/main` in a push trigger on branches, and Dependabot to cover
  every npm project the licence check knows.
- **CI** (`.github/workflows/ci.yml`): four jobs, each gated by `vars.JFB_CI` for pushes, always run by hand and for
  `v*` tags (until 18.2's release workflow calls CI for its tags). The load-balancing job starts SQL Server and
  creates the shared database, and runs the `lb` project once `tests/e2e/specs/lb/` exists; until then it ends
  with a notice that names 8.1, so its first run does not read as a passed suite.
- **The scripts choose their browser in one place** (`scripts/lib/browser.mjs`): Edge locally, Playwright's
  Chromium when `CI` is set, as the browser tests do.
- **`pack.mjs --no-build` without `--version` packs the version a plain build stamps** (VersionPrefix-VersionSuffix),
  which is what the gate and CI build.
- **The gate** (`scripts/ci-local.ps1`) runs CI's stages except SQL Server and load balancing, judges native
  commands by exit code only and prints the time of every stage. Its browser stage runs `scripts/e2e-gate.mjs`,
  which reruns the browser projects once only when a host under `.local/e2e/` left the hang marker in this run
  (stale markers are removed first). The policy lives in Node rather than PowerShell so that its tests run with the
  other script tests.

## Consequences

- Updating Umbraco needs no licence review unless Umbraco brings a forbidden family; a package Jiron Form Builder
  adds needs one, and a changed licence file needs a new read.
- The notices stay current on their own for what is generated, and fail the gate when a tool changes without its
  row.
- The first CI run is the first run of the scripts, the harness and the SQL Server tests on Linux. The owner checks
  it (`docs/OWNER-TODO.md`); nothing here can.
- **Revisit** when Umbraco ships packages whose platform licences need a closer look, when 18.1's package allowlist
  makes the shipped tier visible in the packages themselves, or when 8.1 fills in the load-balancing job.
