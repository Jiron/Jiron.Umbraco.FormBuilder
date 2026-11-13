// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// Unit tests for scripts/release.mjs and the shape of the release workflow (ROADMAP 18.2, ADR-0247).
// Needs `npm ci --prefix scripts` (yaml). Run: node --test "scripts/tests/*.test.mjs"
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { parse } from 'yaml';
import { comparePacks, normalisedEntries, propsVersion, releaseNotes, releaseVersion } from '../release.mjs';

const root = fileURLToPath(new URL('../..', import.meta.url));
const read = (path) => readFileSync(join(root, path), 'utf8');
const PROPS = '<VersionPrefix>17.0.0</VersionPrefix><VersionSuffix>alpha.0</VersionSuffix>';

describe('the version a release packs (D16)', () => {
  test('comes from a tag or a manual run, without the v', () => {
    assert.equal(releaseVersion('v17.0.0-alpha.1', PROPS), '17.0.0-alpha.1');
    assert.equal(releaseVersion('refs/tags/v17.0.0-beta.2', PROPS), '17.0.0-beta.2');
    assert.equal(releaseVersion(' 17.1.0 ', PROPS), '17.1.0');
    assert.equal(releaseVersion('v17.0.0-rc.1', PROPS), '17.0.0-rc.1');
  });

  test('must be a semantic version on the branch line, whose major follows Umbraco', () => {
    assert.throws(() => releaseVersion('v17.0', PROPS), /is not a version such as v17\.0\.0-alpha\.1/);
    assert.throws(() => releaseVersion('latest', PROPS), /is not a version/);
    assert.throws(() => releaseVersion('v17.0.0-alpha..1', PROPS), /is not a version/);
    assert.throws(() => releaseVersion('v18.0.0-alpha.1', PROPS), /not on this branch's line 17\.x/);
    assert.throws(() => releaseVersion('v17.0.0', '<Project />'), /no VersionPrefix/);
  });

  test('of Directory.Build.props is its prefix with its suffix when there is one', () => {
    assert.equal(propsVersion(PROPS), '17.0.0-alpha.0');
    assert.equal(propsVersion('<VersionPrefix>17.0.0</VersionPrefix><VersionSuffix></VersionSuffix>'), '17.0.0');
    assert.throws(() => propsVersion('<Project />'), /no VersionPrefix/);
  });
});

describe('the notes of a release', () => {
  const changelog = [
    '# Changelog',
    '',
    '## [Unreleased]',
    '',
    '## [17.0.0-alpha.2] - 2026-11-20',
    '',
    '### Fixed',
    '',
    '- A fix.',
    '',
    '## [17.0.0-alpha.1] - 2026-11-13',
    '',
    '### Added',
    '',
    '- Everything.',
    '',
    '## Links',
    '',
    'Not part of a release.',
  ].join('\n');

  test("are the version's section without its heading, up to the next section", () => {
    assert.equal(releaseNotes('17.0.0-alpha.1', changelog), '### Added\n\n- Everything.\n');
    assert.equal(releaseNotes('17.0.0-alpha.2', changelog), '### Fixed\n\n- A fix.\n');
  });

  test('refuse a version without a section, or with an empty one', () => {
    assert.throws(() => releaseNotes('17.0.0-alpha.3', changelog), /has no section "## \[17\.0\.0-alpha\.3\]"/);
    assert.throws(() => releaseNotes('Unreleased', changelog), /section of Unreleased is empty/);
  });

  test("exist in CHANGELOG.md for the version Directory.Build.props names, once it names a release", () => {
    const version = propsVersion(read('Directory.Build.props'));
    // alpha.0 is the line's placeholder before the first prerelease (D16), never released.
    if (version.endsWith('-alpha.0')) return;
    assert.ok(releaseNotes(version, read('CHANGELOG.md')).length > 0);
  });
});

describe('two folders of packs', () => {
  const v1 = '17.0.0-dev.202611130403';
  const v2 = '17.0.0-alpha.0';
  const web = (version, hash) => [`Jiron.Umbraco.FormBuilder.Web.nuspec`, `staticwebassets/App_Plugins/JironFormBuilder/chunks/designer-${hash}.js`, 'README.md'];

  test("compare a pack's entries whatever its version and its chunks' content hashes", () => {
    assert.deepEqual(normalisedEntries(web(v1, 'CdFF-JzS'), v1), normalisedEntries(web(v2, 'Ab_cd123'), v2));
    assert.deepEqual(normalisedEntries([`lib/net10.0/${v1}.txt`], v1), ['lib/net10.0/<version>.txt']);
    const a = new Map([['Jiron.Umbraco.FormBuilder.Web.nupkg', normalisedEntries(web(v1, 'CdFF-JzS'), v1)]]);
    const b = new Map([['Jiron.Umbraco.FormBuilder.Web.nupkg', normalisedEntries(web(v2, 'Ab_cd123'), v2)]]);
    assert.deepEqual(comparePacks(a, b), []);
  });

  test('name a package only one holds, and an entry only one holds or holds more often', () => {
    const a = new Map([
      ['Jiron.Umbraco.FormBuilder.nupkg', ['README.md']],
      ['Jiron.Umbraco.FormBuilder.Web.nupkg', ['a.js', 'a.js', 'b.js']],
    ]);
    const b = new Map([
      ['Jiron.Umbraco.FormBuilder.Web.nupkg', ['a.js', 'c.js']],
      ['Jiron.Umbraco.FormBuilder.AI.nupkg', ['README.md']],
    ]);
    assert.deepEqual(comparePacks(a, b), [
      'Jiron.Umbraco.FormBuilder.nupkg is only in the first folder',
      'Jiron.Umbraco.FormBuilder.Web.nupkg: a.js is only in the first folder',
      'Jiron.Umbraco.FormBuilder.Web.nupkg: b.js is only in the first folder',
      'Jiron.Umbraco.FormBuilder.Web.nupkg: c.js is only in the second folder',
      'Jiron.Umbraco.FormBuilder.AI.nupkg is only in the second folder',
    ]);
  });
});

describe('the release workflow (ADR-0247)', () => {
  const release = parse(read('.github/workflows/release.yml'));
  const ci = parse(read('.github/workflows/ci.yml'));

  test('runs on version tags and by hand, and CI only through its call, so a tag runs CI once', () => {
    assert.deepEqual(release.on.push.tags, ['v*.*.*']);
    assert.ok('workflow_dispatch' in release.on);
    assert.ok('workflow_call' in ci.on, 'ci.yml can be called');
    assert.equal(ci.on.push.tags, undefined, 'ci.yml no longer runs on tags itself');
  });

  test('checks the version and its notes first, then runs CI, then packs, then drafts the release', () => {
    assert.deepEqual(Object.keys(release.jobs), ['prepare', 'ci', 'pack', 'release', 'publish']);
    assert.equal(release.jobs.ci.uses, './.github/workflows/ci.yml');
    assert.deepEqual(release.jobs.ci.needs, 'prepare');
    assert.deepEqual(release.jobs.pack.needs, ['prepare', 'ci']);
    assert.deepEqual(release.jobs.release.needs, ['prepare', 'pack']);
    const prepare = release.jobs.prepare.steps.map((step) => step.run ?? '').join('\n');
    assert.match(prepare, /node scripts\/release\.mjs version "\$REQUESTED"/);
    assert.match(prepare, /node scripts\/release\.mjs notes "\$VERSION"/);
  });

  test('packs through the one pack script that the gate and CI use', () => {
    const runs = (job) => job.steps.map((step) => step.run ?? '').join('\n');
    assert.match(runs(release.jobs.pack), /node scripts\/pack\.mjs --version "\$JFB_VERSION" --output artifacts\/packages/);
    assert.match(runs(ci.jobs.build), /node scripts\/pack\.mjs --no-build --output artifacts\/packages/);
    assert.match(read('scripts/ci-local.ps1'), /Invoke-Native 'node' @\('scripts\/pack\.mjs', '--no-build', '--output', \$Packages\)/);
  });

  test('drafts the release on a tag only, with the write permission there and no token in a checkout', () => {
    assert.equal(release.permissions.contents, 'read');
    const job = release.jobs.release;
    assert.match(job.if, /^startsWith\(github\.ref, 'refs\/tags\/v'\) && github\.event_name == 'push'$/);
    assert.deepEqual(job.permissions, { contents: 'write' });
    assert.match(job.steps.at(-1).run, /gh release create "\$GITHUB_REF_NAME" packages\/\* --draft/);
    for (const name of ['prepare', 'pack', 'release', 'publish']) {
      const checkout = release.jobs[name].steps.find((step) => String(step.uses).startsWith('actions/checkout@'));
      assert.equal(checkout.with?.['persist-credentials'], false, `${name} keeps no token`);
    }
  });

  test('publishes only when the owner sets JFB_NUGET_PUBLISH, through trusted publishing in the nuget environment (D17)', () => {
    const job = release.jobs.publish;
    assert.equal(job.if, "startsWith(github.ref, 'refs/tags/v') && vars.JFB_NUGET_PUBLISH == 'true'");
    assert.equal(job.environment, 'nuget');
    assert.deepEqual(job.permissions, { contents: 'read', 'id-token': 'write' });
    assert.ok(job.steps.some((step) => step.uses === 'NuGet/login@v1'));
    assert.match(job.steps.at(-1).run, /dotnet nuget push "packages\/\*\.nupkg" .* --skip-duplicate$/);
  });

  test("keeps its packages apart from CI's, which the same run uploads as packages", () => {
    const upload = release.jobs.pack.steps.find((step) => String(step.uses).startsWith('actions/upload-artifact@'));
    const ciUpload = ci.jobs.build.steps.find((step) => step.name === 'Upload the packages');
    assert.equal(upload.with.name, 'release-packages');
    assert.equal(ciUpload.with.name, 'packages');
    for (const name of ['release', 'publish']) {
      const download = release.jobs[name].steps.find((step) => String(step.uses).startsWith('actions/download-artifact@'));
      assert.equal(download.with.name, 'release-packages');
    }
  });
});
