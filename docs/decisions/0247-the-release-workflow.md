# 0247. The release workflow

- Status: accepted (amends ADR-0032)
- Date: 2026-11-13
- Roadmap step: 18.2 (the release workflow)

## Context

Step 18.2 asks for `.github/workflows/release.yml` in the shape the owner's chat package proved (Tandem ADR-0077): on a
version tag or by hand, a `prepare` job that checks the version and its changelog section in a minute, CI called through
`workflow_call` rather than copied, the pack through the one `scripts/pack.mjs`, a draft GitHub Release with the
changelog's notes, and a publish job that stays off (D17). Until now `ci.yml` ran on `v*` tags itself (ADR-0032).

Nobody can watch Actions from this machine, so the workflows must be right by construction and by the local checks.

## Options

1. **Copy CI's steps into the release.** Two copies drift: a check added to CI would be missing from a release.
2. **Run the release after CI (`workflow_run`).** A tag would still run CI on its own, and the release would have to
   find CI's run and its commit again.
3. **Call CI from the release (`workflow_call`), and let tags run CI only that way.** One definition of CI; a tag runs
   it once, and the pack starts only after all of it passed.

## Decision

Option 3.

- **`ci.yml`** gains `workflow_call` and drops its `v*` tag trigger. Its jobs keep their conditions, which let a tag
  through; called from the release, `github.event_name` and `github.ref` are the tag's.
- **`release.yml`**, on a tag `v*.*.*` or by hand:
  - `prepare` reads the version (`scripts/release.mjs version`: a semantic version on the branch's Umbraco major, D16),
    and on a tag its `CHANGELOG.md` section (`scripts/release.mjs notes`), so a mistake fails before CI;
  - `ci` calls `ci.yml`;
  - `pack` installs the client's packages and runs `node scripts/pack.mjs --version <version>`: the client stamped with
    the version, the Release pack with package validation, the allowlist and the nuspecs (ADR-0246); its artifact is
    `release-packages`, because CI, called in the same run, uploads its own pack as `packages`;
  - `release` (tags only) drafts the GitHub Release with the packages and the notes, marked as a prerelease when the
    version has a suffix;
  - `publish` (tags only, and only with `vars.JFB_NUGET_PUBLISH == 'true'`) runs in the environment `nuget`, signs in
    with nuget.org's trusted publishing (`NuGet/login@v1`, `vars.JFB_NUGET_USER`) and pushes with `--skip-duplicate`.
  - A manual run packs the version it is given as a dry run: no release, nothing published.
- **Permissions** stay `contents: read` at the top; only `release` gets `contents: write`, only `publish` gets
  `id-token: write`, and no job's checkout keeps a token (`persist-credentials: false`).
- **Inputs reach scripts through the environment.** `scripts/check-workflows.mjs` now refuses a step whose `run` inlines
  `github.ref_name`, `github.ref`, `github.head_ref`, `github.event.*` or `inputs.*`, since GitHub pastes an inline
  expression into the script before the shell reads it.
- **The dry run of the pack steps:** `scripts/release.mjs compare` holds two folders of packs side by side, with the
  version and the chunks' content hashes taken out. In 18.2 a release-style pack (`pack.mjs --version`, which builds the
  client and the packages itself) and the gate's (`ci-local.ps1`: the client and the Release build, then
  `pack.mjs --no-build`) gave the same 7 files with the same 136 entries. A script test holds that the release, CI and
  the gate all pack through `scripts/pack.mjs`.
- **`docs/releasing.md`** says how to cut a release and what only the owner does to turn publishing on.

## Consequences

- A tag costs one full CI run plus a pack, in the private repository's Actions minutes.
- Cutting a release needs a changelog section for its version first; a script test fails once `Directory.Build.props`
  names a version without one.
- Revisit if GitHub allows required reviewers on environments for this plan, which would gate publishing better than a
  repository variable.
