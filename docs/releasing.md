# Releasing

For contributors and the owner: how a Jiron Form Builder version is built, packed and released, and the steps only the
owner takes to publish it ([ADR-0247](decisions/0247-the-release-workflow.md)). Nothing is published during the build:
the publish job stays off until the owner turns it on ([OWNER-TODO](OWNER-TODO.md)).

## What the release workflow does

`.github/workflows/release.yml` runs on a version tag such as `v17.0.0-alpha.1`:

1. **The version and its notes,** in a minute: the version from the tag (`node scripts/release.mjs version`), and the
   version's section of `CHANGELOG.md`, so a tag without one fails before any CI runs.
2. **CI, all of it.** It calls `.github/workflows/ci.yml`: the repository checks, the client, the Release build, the
   unit and integration tests on SQLite and on SQL Server, the browser smoke tests and the load-balancing suite. A
   version tag runs CI only this way, so it runs once.
3. **Pack.** `node scripts/pack.mjs --version <version>` builds the client stamped with the version, packs the four
   packages and their symbols in Release with NuGet's package validation, and checks every entry against its package's
   allowlist and every nuspec (`scripts/check-packages.mjs`,
   [ADR-0246](decisions/0246-the-packages-and-their-listings.md)). The files are kept as the run's `release-packages`
   artifact.
4. **Draft release.** It creates a **draft** GitHub Release for the tag with the `.nupkg` and `.snupkg` files attached
   and the version's section of `CHANGELOG.md` as its notes (`node scripts/release.mjs notes`). A version with a suffix
   (`-alpha.1`) is marked as a prerelease. Nobody sees a draft until it is published by hand.
5. **Publish**, only when the repository variable `JFB_NUGET_PUBLISH` is `true`: it signs in to nuget.org with trusted
   publishing (`NuGet/login`, a key valid for one hour, no stored secret) and pushes the packages with their symbols
   (`dotnet nuget push --skip-duplicate`). Until then the job is skipped.

`scripts/ci-local.ps1` and CI pack through the same `scripts/pack.mjs`, so every local gate is a dry run of the
release's pack steps, with the version of `Directory.Build.props`. `node scripts/release.mjs compare <folder> <folder>`
shows that two packs hold the same packages with the same entries, whatever their versions; in 18.2 a release-style pack
and the gate's gave the same 7 files with the same 136 entries.

## A targeted run of CI

A full run of CI, as a release or a dry run of one starts it, costs about 4 h 20 min of the repository's runner time,
most of it the 18 integration shares and the browser jobs. To check one failure that only GitHub's runners show, start
**Targeted run** (`.github/workflows/targeted.yml`) by hand instead: it calls `ci.yml` with the part to run (`only`:
`build`, `sqlite`, `sqlserver`, `smoke`, `loadbalancing` or `all`) and, for one database, the shares to run (`shares`, a
JSON list such as `["3","timed"]`; empty for every share). A first job reads the two inputs
(`scripts/ci-selection.mjs`) and fails in seconds on a wrong value; the jobs not chosen do not start, and "Integration
tests add up" runs only when everything ran. One integration share takes about 20 minutes of runner time, the build job
or the smoke job about 30, the load-balancing job about 25. A targeted run packs, drafts and publishes nothing; a push, a
tag and the release give `ci.yml` no inputs and run all of it, which `scripts/check-workflows.mjs` holds.

## Versions

- The package major follows the Umbraco major (D16): `main` releases `17.x` for Umbraco 17, and `v18/main` releases
  `18.x`. A tag on another major is refused.
- The line goes `17.0.0-alpha.N`, then `-beta.N` (and `-rc.N` when needed, ADR-0010), then `17.0.0`.
- The tag's version wins over `VersionPrefix` and `VersionSuffix` in `Directory.Build.props`, which name the version of
  local builds. Local test packs use a `-dev.<yyyyMMddHHmm>` suffix, so a package cache never serves stale bits.

## Cutting a release

1. Move the changes under `## [Unreleased]` in `CHANGELOG.md` to a new section `## [17.0.0-alpha.1] - YYYY-MM-DD`
   (Keep a Changelog), and keep an empty `## [Unreleased]` above it. Add the version's section to
   [upgrading.md](upgrading.md#releases). Set `VersionSuffix` in `Directory.Build.props` to the same version, so local
   builds name it too; a script test fails while the changelog lacks its section.
2. Commit, push `main`, and run the install test with the version, on the line's Umbraco (run and published) and on the
   supported floor with the update after it. Each packs exactly that version into a fresh site
   ([how upgrades are tested](upgrade-testing.md#the-install-test)), in a fresh package cache that goes with the run.

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File scripts\install-test.ps1 -Mode install -Version 17.0.0-alpha.1
   ```

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File scripts\install-test.ps1 -Mode floor -Version 17.0.0-alpha.1
   ```

3. Tag the commit and push the tag:

   ```bash
   git tag v17.0.0-alpha.1
   ```

   ```bash
   git push origin v17.0.0-alpha.1
   ```

4. In GitHub, open **Actions › Release** and wait for the run. Then open **Releases**: the draft has the packages and
   the notes. Check them, and publish the draft when the version should be visible.
5. Move `main` on: set `VersionSuffix` to the release's suffix with `.dev` (`alpha.1.dev` after 17.0.0-alpha.1; after
   a release without a suffix, the next patch with `dev`), commit and push, so no build between releases names a
   released version ([ADR-0249](decisions/0249-the-version-between-releases.md)).

**A dry run:** **Actions › Release › Run workflow** with a version such as `17.0.0-alpha.1` runs CI and packs that
version into the run's `release-packages` artifact, without a release and without publishing.

## Turning on publishing (owner)

Publishing needs the repository to be public, which the Umbraco Marketplace needs too, and the lawyer's review of the
licence texts ([OWNER-TODO](OWNER-TODO.md)). Then:

1. **Create the trusted-publishing policy on nuget.org.** Sign in to nuget.org as the account that owns the `Jiron.`
   prefix, open **Trusted Publishing**, and add a GitHub Actions policy with the repository owner `Jiron`, the
   repository `Jiron.Umbraco.FormBuilder`, the workflow file `release.yml` and the environment `nuget`, which the
   publish job runs in. In **Settings › Environments › nuget**, limit its deployments to the tags `v*` where the plan
   allows it, so a branch's copy of the workflow cannot publish. For a private repository the policy is active for 7
   days until its first publish.
2. **Set two repository variables** (Settings › Secrets and variables › Actions › Variables):
   - `JFB_NUGET_USER`: the nuget.org profile name (not the email address) that owns the policy;
   - `JFB_NUGET_PUBLISH`: `true`.
3. **Push the next version tag.** The publish job pushes the packages once the draft release exists; versions tagged
   before the variables were set stay unpublished, and `--skip-duplicate` leaves a version already on nuget.org alone.
4. **The Marketplace** lists `Jiron.Umbraco.FormBuilder` and the AI add-on by their `umbraco-marketplace` tag, with the
   listings `umbraco-marketplace.json` and `umbraco-marketplace-jiron.umbraco.formbuilder.ai.json`: new packages are
   synced daily at 04:00 UTC, known ones every two hours. Validate both at https://marketplace.umbraco.com/validate
   first; the build checks them against the Marketplace's schema with `node scripts/check-marketplace.mjs`.

To stop publishing, set `JFB_NUGET_PUBLISH` to anything but `true`.
