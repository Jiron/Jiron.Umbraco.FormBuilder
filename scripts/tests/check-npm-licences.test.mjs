// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// Unit tests for scripts/check-npm-licences.mjs, with the cases ADR-0027 names. Run: node --test "scripts/tests/*.test.mjs"
import assert from 'node:assert/strict';
import { existsSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';
import { after, describe, test } from 'node:test';
import { LOCK_FILES, licenceOf, needsPublicDomain, run, thirdParty, tierOf, violations } from '../check-npm-licences.mjs';

const lock = (packages) => ({ lockfileVersion: 3, packages: { '': {}, ...packages } });
const dev = (license) => ({ version: '1.0.0', license, dev: true });
const shipped = (license) => ({ version: '1.0.0', license });
const verdict = (entry) => violations(lock({ 'node_modules/x': entry })).length === 0;

describe('ADR-0027 in a development package', () => {
  test('accepts MIT-0, Unlicense, CC0-1.0 and "BSD 3-Clause", and the development licences of rule 4', () => {
    for (const license of ['MIT-0', 'Unlicense', 'CC0-1.0', 'BSD 3-Clause', 'MPL-2.0', 'BlueOak-1.0.0', 'Python-2.0', 'CC-BY-4.0', '(MPL-2.0 OR Apache-2.0)', 'MIT AND ISC']) {
      assert.equal(verdict(dev(license)), true, license);
    }
  });

  test('refuses UNLICENSED, an empty or missing licence, and the forbidden families', () => {
    for (const license of ['UNLICENSED', '', undefined, 'GPL-3.0-only', 'AGPL-3.0', 'LGPL-2.1-or-later', 'SSPL-1.0', 'MIT AND GPL-2.0', 'BUSL-1.1']) {
      assert.equal(verdict(dev(license)), false, String(license));
    }
  });
});

describe('a package that could ship', () => {
  test("is held to rule 4's list: no public-domain dedication, no development licence", () => {
    for (const license of ['MIT', 'Apache-2.0', 'BSD 3-Clause', 'ISC', '0BSD']) assert.equal(verdict(shipped(license)), true, license);
    for (const license of ['MIT-0', 'Unlicense', 'CC0-1.0', 'MPL-2.0', 'UNLICENSED', '', 'GPL-3.0-only']) {
      assert.equal(verdict(shipped(license)), false, license);
    }
  });

  test('is any entry the lock does not mark `dev`, devOptional included', () => {
    assert.equal(tierOf({ dev: true }), 'dev');
    assert.equal(tierOf({ devOptional: true }), 'shipped');
    assert.equal(tierOf({ optional: true }), 'shipped');
    assert.equal(tierOf({}), 'shipped');
  });
});

describe('the lock entries', () => {
  test("read old package.json licence objects and arrays, and leave out the workspace's own links", () => {
    assert.equal(licenceOf({ license: { type: 'MIT', url: 'x' } }), 'MIT');
    assert.equal(licenceOf({ licenses: [{ type: 'MIT' }, { type: 'Apache-2.0' }] }), 'MIT OR Apache-2.0');
    assert.equal(licenceOf({}), '');
    const packages = thirdParty(lock({ 'node_modules/a': dev('MIT'), 'node_modules/@jfb/logic': { link: true }, 'packages/logic': { version: '0.0.0' } }));
    assert.deepEqual(packages.map((pkg) => pkg.name), ['a']);
  });

  test('report the refused ones by name, version and tier, nested ones too', () => {
    const result = violations(
      lock({
        'node_modules/ok': dev('MIT'),
        'node_modules/bad': { version: '2.0.0', license: 'GPL-3.0', dev: true },
        'node_modules/none': { version: '3.0.0' },
        'node_modules/a/node_modules/nested': { version: '4.0.0', license: 'MIT-0' },
      }),
    );
    assert.deepEqual(result, ['bad@2.0.0 (dev): GPL-3.0', 'none@3.0.0 (shipped): no licence', 'nested@4.0.0 (shipped): MIT-0']);
  });

  test('know which development licences only ADR-0027 allows', () => {
    assert.equal(needsPublicDomain('CC0-1.0'), true);
    assert.equal(needsPublicDomain('MIT OR CC0-1.0'), false);
    assert.equal(needsPublicDomain('MIT'), false);
  });
});

describe('run', () => {
  const dir = mkdtempSync(join(tmpdir(), 'jfb-npm-licences-'));
  after(() => rmSync(dir, { recursive: true, force: true }));

  test('passes when every lock file is clean, and counts its packages', () => {
    const file = join(dir, 'clean.json');
    writeFileSync(file, JSON.stringify(lock({ 'node_modules/a': dev('MIT'), 'node_modules/b': dev('Unlicense') })));
    const result = run([file], dir);
    assert.equal(result.ok, true);
    assert.equal(result.lines[0], 'check-npm-licences: clean.json: 2 packages (2 dev, 0 that could ship), all licences allowed; 1 through ADR-0027');
  });

  test('fails on a refused licence in any lock file, and on a missing lock file', () => {
    const clean = join(dir, 'clean2.json');
    const dirty = join(dir, 'dirty.json');
    writeFileSync(clean, JSON.stringify(lock({ 'node_modules/a': dev('MIT') })));
    writeFileSync(dirty, JSON.stringify(lock({ 'node_modules/b': shipped('CC0-1.0') })));
    const result = run([clean, dirty, join(dir, 'nope.json')], dir);
    assert.equal(result.ok, false);
    assert.ok(result.lines.some((line) => line.includes('b@1.0.0 (shipped): CC0-1.0 is not allowed')));
    assert.ok(result.lines.some((line) => line.endsWith('nope.json is missing')));
  });

  test("covers every npm project of the repository, and the repository's own locks pass", () => {
    const repo = resolve(fileURLToPath(new URL('../..', import.meta.url)));
    for (const file of LOCK_FILES) assert.ok(existsSync(join(repo, file)), file);
    const result = run(LOCK_FILES.map((file) => join(repo, file)), repo);
    assert.equal(result.ok, true, result.lines.join('\n'));
  });
});
