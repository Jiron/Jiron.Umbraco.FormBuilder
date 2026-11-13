# 0249. The version between releases

- Status: accepted (refines D16)
- Date: 2026-11-13
- Roadmap step: 18.1 (18.4, the first prerelease, and the phase review)

## Context

D16 has `Directory.Build.props` hold `VersionPrefix` 17.0.0 and `VersionSuffix` alpha.0, release builds take the tag's
version, and local test packs a `-dev.<yyyyMMddHHmm>` suffix, so a package cache never serves stale bits. Step 18.4 set
the version to 17.0.0-alpha.1 for the first prerelease. The phase review found that the props kept naming it after the
tag: every gate and CI pack since then is stamped 17.0.0-alpha.1, the released version, with other contents. Nothing
installs those packs, but a pack, an artifact or a backoffice manifest that names a released version while holding newer
code is what D16 set out to prevent, and `docs/releasing.md` had no step to move on.

## Options

1. **Back to `alpha.0` after a release.** It sorts below the release just made, so a local build would look older.
2. **The next prerelease (`alpha.2`) at once.** It names a version that does not exist yet, and the next release might
   be a beta.
3. **The release's own version with `.dev`** (`17.0.0-alpha.1.dev`): Semantic Versioning sorts it above 17.0.0-alpha.1
   (one more identifier) and below any later prerelease or the release (`alpha.1` < `alpha.1.dev` < `alpha.2` < `beta.1`
   < `17.0.0`), and it says what it is.

## Decision

Option 3. After a release is tagged, the next commit sets `VersionSuffix` to the release's suffix with `.dev` (after a
release without a suffix, such as 17.0.0, the next patch with `-dev`: `VersionPrefix` 17.0.1, `VersionSuffix` dev). The
release itself still sets the props to its exact version (`docs/releasing.md`, step 1), and the tag's version still wins
in the release workflow. The script test that wants a changelog section for the version the props name leaves out the
line's first placeholder (`-alpha.0`) and any version ending in `.dev` or `-dev`. `main` names 17.0.0-alpha.1.dev from
here.

## Consequences

- Gate, CI and local builds between releases name a version no release ever has.
- Cutting a release has one more step: moving the props on after the tag (`docs/releasing.md`).
