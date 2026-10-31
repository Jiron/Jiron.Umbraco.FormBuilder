// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// Unit tests for scripts/pack.mjs. Run: node --test "scripts/tests/*.test.mjs"
import assert from 'node:assert/strict';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { describe, test } from 'node:test';
import {
  builtVersion,
  contentProblems,
  devVersion,
  PACKAGE_IDS,
  packageFiles,
  packArguments,
  packProblems,
  packVersion,
  SHIPPED_SCHEMAS,
  SOLUTION_FILTER,
  zipEntries,
} from '../pack.mjs';

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

/** A zip of empty entries with these names, as a package's central directory lists them. */
function zipOf(names) {
  const locals = [];
  const centrals = [];
  let offset = 0;
  for (const name of names) {
    const bytes = Buffer.from(name, 'utf8');
    const local = Buffer.alloc(30);
    local.writeUInt32LE(0x04034b50, 0);
    local.writeUInt16LE(bytes.length, 26);
    const central = Buffer.alloc(46);
    central.writeUInt32LE(0x02014b50, 0);
    central.writeUInt16LE(bytes.length, 28);
    central.writeUInt32LE(offset, 42);
    locals.push(local, bytes);
    centrals.push(central, bytes);
    offset += local.length + bytes.length;
  }
  const directory = Buffer.concat(centrals);
  const end = Buffer.alloc(22);
  end.writeUInt32LE(0x06054b50, 0);
  end.writeUInt16LE(names.length, 8);
  end.writeUInt16LE(names.length, 10);
  end.writeUInt32LE(directory.length, 12);
  end.writeUInt32LE(offset, 16);
  return Buffer.concat([...locals, directory, end]);
}

describe("the main package's contents (ADR-0125)", () => {
  test('lists the entries of a zip by its central directory', () => {
    assert.deepEqual(zipEntries(zipOf(['lib/net10.0/_._', 'schemas/jfb-public-form.v1.d.ts'])), [
      'lib/net10.0/_._',
      'schemas/jfb-public-form.v1.d.ts',
    ]);
    assert.throws(() => zipEntries(Buffer.from('not a zip')), /no end of central directory/);
  });

  test("ships the schemas and the visitor form's types, and names what is missing", () => {
    assert.deepEqual(contentProblems(['lib/net10.0/_._', ...SHIPPED_SCHEMAS]), []);
    assert.deepEqual(contentProblems(['lib/net10.0/_._', ...SHIPPED_SCHEMAS.filter((name) => !name.endsWith('.d.ts'))]), [
      'the main package lacks schemas/jfb-public-form.v1.d.ts',
    ]);
    assert.ok(SHIPPED_SCHEMAS.includes('schemas/jfb-public-form.v1.schema.json'));
  });
});
