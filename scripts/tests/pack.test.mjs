// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// Unit tests for scripts/pack.mjs. Run: node --test "scripts/tests/*.test.mjs"
import assert from 'node:assert/strict';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { describe, test } from 'node:test';
import { builtVersion, devVersion, PACKAGE_IDS, packageFiles, packArguments, packProblems, packVersion, SOLUTION_FILTER } from '../pack.mjs';

const VERSION = '17.0.0-dev.202610231200';

describe('the version of a local test pack', () => {
  test('is VersionPrefix with -dev.<yyyyMMddHHmm> in UTC (D16)', () => {
    const props = '<VersionPrefix>17.0.0</VersionPrefix><VersionSuffix>alpha.0</VersionSuffix>';
    assert.equal(devVersion(props, new Date(Date.UTC(2026, 9, 23, 7, 5))), '17.0.0-dev.202610230705');
    assert.throws(() => devVersion('<Project />'), /VersionPrefix/);
  });

  test('is the one asked for, a test version when the pack builds, and the built version when it does not', () => {
    const props = '<VersionPrefix>17.0.0</VersionPrefix><VersionSuffix>alpha.0</VersionSuffix>';
    const now = new Date(Date.UTC(2026, 9, 23, 7, 5));
    assert.equal(packVersion({ version: '17.0.0-beta.1', build: false, propsText: props, now }), '17.0.0-beta.1');
    assert.equal(packVersion({ build: true, propsText: props, now }), '17.0.0-dev.202610230705');
    assert.equal(packVersion({ build: false, propsText: props, now }), '17.0.0-alpha.0');
    assert.equal(builtVersion('<VersionPrefix>17.1.0</VersionPrefix><VersionSuffix></VersionSuffix>'), '17.1.0');
    assert.throws(() => builtVersion('<Project />'), /VersionPrefix/);
  });
});

describe('dotnet pack', () => {
  test('packs the solution filter in Release with the version', () => {
    assert.deepEqual(packArguments({ output: 'out', version: VERSION }), [
      'pack',
      SOLUTION_FILTER,
      '-c',
      'Release',
      '-nologo',
      '-o',
      'out',
      `-p:Version=${VERSION}`,
    ]);
    assert.equal(packArguments({ output: 'out', version: VERSION, build: false }).at(-1), '--no-build');
  });
});

describe('the packed files', () => {
  const complete = [
    ...PACKAGE_IDS.map((id) => `${id}.${VERSION}.nupkg`),
    `Jiron.Umbraco.FormBuilder.Core.${VERSION}.snupkg`,
    `Jiron.Umbraco.FormBuilder.Web.${VERSION}.snupkg`,
  ];

  test('are the three packages of the version, with the symbols of Core and Web', () => {
    assert.deepEqual(PACKAGE_IDS, ['Jiron.Umbraco.FormBuilder', 'Jiron.Umbraco.FormBuilder.Core', 'Jiron.Umbraco.FormBuilder.Web']);
    assert.deepEqual(packProblems(complete, VERSION), []);
  });

  test('may not miss a package, carry another version or hold anything else', () => {
    assert.deepEqual(packProblems(complete.slice(1), VERSION), [`Jiron.Umbraco.FormBuilder.${VERSION}.nupkg is missing`]);
    assert.deepEqual(packProblems([...complete, 'Jiron.Umbraco.FormBuilder.Core.17.0.0.nupkg'], VERSION), [
      `Jiron.Umbraco.FormBuilder.Core.17.0.0.nupkg is not one of the packages of ${VERSION}`,
    ]);
    assert.deepEqual(packProblems([...complete, `Jiron.Umbraco.FormBuilder.Testing.${VERSION}.nupkg`], VERSION), [
      `Jiron.Umbraco.FormBuilder.Testing.${VERSION}.nupkg is not one of the packages of ${VERSION}`,
    ]);
  });

  test('replace only the package files of a folder', () => {
    const dir = mkdtempSync(join(tmpdir(), 'jfb-pack-'));
    try {
      for (const file of ['a.nupkg', 'a.snupkg', 'notes.txt']) writeFileSync(join(dir, file), '');
      assert.deepEqual(packageFiles(dir).sort(), ['a.nupkg', 'a.snupkg']);
      assert.deepEqual(packageFiles(join(dir, 'missing')), []);
    } finally {
      rmSync(dir, { recursive: true, force: true });
    }
  });
});
