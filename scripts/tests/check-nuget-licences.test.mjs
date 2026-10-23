// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// Unit tests for scripts/check-nuget-licences.mjs. Run: node --test "scripts/tests/*.test.mjs"
import assert from 'node:assert/strict';
import { describe, test } from 'node:test';
import { EXCEPTIONS, judge, run } from '../check-nuget-licences.mjs';
import { licenceTextHash } from '../lib/nuget.mjs';
import { fakeRepo, putPackage } from './fake-repo.mjs';

const pkg = (id, tier) => ({ id, version: '1.0.0', tier, projects: ['P'] });
const expression = (value) => ({ kind: 'expression', value, copyright: '' });
const file = (text) => ({ kind: 'file', value: 'LICENSE.txt', copyright: '', text });

describe('judge', () => {
  test("shipped packages need rule 4's list; dev packages may also use the development list and ADR-0027", () => {
    assert.equal(judge(pkg('A', 'shipped'), expression('MIT')).ok, true);
    assert.equal(judge(pkg('A', 'shipped'), expression('MPL-2.0')).ok, false);
    assert.equal(judge(pkg('A', 'shipped'), expression('CC0-1.0')).ok, false);
    assert.equal(judge(pkg('A', 'dev'), expression('MPL-2.0')).ok, true);
    assert.equal(judge(pkg('A', 'dev'), expression('Unlicense')).ok, true);
    assert.equal(judge(pkg('A', 'dev'), expression('GPL-3.0-only')).ok, false);
  });

  test('a licence file, a URL or no licence needs a reviewed exception outside the platform', () => {
    for (const meta of [file('MIT text'), { kind: 'url', value: 'https://example.test/LICENSE', copyright: '' }, { kind: 'none', value: '', copyright: '' }]) {
      const verdict = judge(pkg('A', 'dev'), meta);
      assert.equal(verdict.ok, false);
      assert.match(verdict.note, /not allowed for a dev package/);
    }
  });

  test('an exception holds only for its tiers and only while the licence file is the one reviewed', () => {
    const exceptions = [{ id: 'Tool', tiers: ['dev'], licence: 'MIT', files: [licenceTextHash('the MIT text')], reason: 'read it' }];
    assert.deepEqual(judge(pkg('tool', 'dev'), file('the MIT text\r\n'), exceptions), { ok: true, licence: 'MIT', note: 'read it' });
    const changed = judge(pkg('Tool', 'dev'), file('other terms'), exceptions);
    assert.equal(changed.ok, false);
    assert.match(changed.note, /differs from the reviewed licence file \(sha256 [0-9a-f]{64}\)/);
    assert.equal(judge(pkg('Tool', 'shipped'), file('the MIT text'), exceptions).ok, false);
  });

  test("NUnit's reviewed exception is for development only", () => {
    const nunit = EXCEPTIONS.find((entry) => entry.id === 'NUnit');
    assert.deepEqual(nunit.tiers, ['dev']);
    assert.equal(nunit.licence, 'MIT');
  });

  test("the platform is Umbraco's choice, except a family rule 4 forbids anywhere", () => {
    for (const meta of [expression('MS-PL'), file('Apache License 2.0, commercial damages'), { kind: 'url', value: 'https://example.test/EULA', copyright: '' }, { kind: 'none', value: '', copyright: '' }]) {
      assert.equal(judge(pkg('Umbraco.Thing', 'platform'), meta).ok, true, meta.kind);
    }
    for (const meta of [expression('(MIT OR GPL-2.0-only)'), file('GNU AFFERO GENERAL PUBLIC LICENSE'), { kind: 'url', value: 'https://www.gnu.org/licenses/lgpl-3.0', copyright: '' }]) {
      const verdict = judge(pkg('Umbraco.Thing', 'platform'), meta);
      assert.equal(verdict.ok, false, meta.kind);
      assert.match(verdict.note, /forbids anywhere/);
    }
  });
});

describe('run', () => {
  test('passes on a repository whose licences are allowed, and counts the tiers', () => {
    const repo = fakeRepo();
    try {
      const result = run(repo.root, repo.cache, []);
      assert.equal(result.ok, true, result.lines.join('\n'));
      assert.match(result.lines.at(-1), /4 packages from .*: 1 shipped, 1 dev, 2 from Umbraco's platform; every licence allowed$/);
    } finally {
      repo.cleanup();
    }
  });

  test('fails on a refused licence and on a package missing from the packages folder', () => {
    const repo = fakeRepo();
    try {
      putPackage(repo.cache, { id: 'Shipped.Lib', version: '1.0.0', licence: '<license type="expression">MPL-2.0</license>' });
      const refused = run(repo.root, repo.cache, []);
      assert.equal(refused.ok, false);
      assert.ok(refused.lines.some((line) => line.includes('Shipped.Lib 1.0.0 (shipped, Pkg, Tool): MPL-2.0 is not allowed')));
      const elsewhere = run(repo.root, `${repo.cache}-elsewhere`, []);
      assert.equal(elsewhere.ok, false);
      assert.ok(elsewhere.lines.some((line) => line.includes('same NUGET_PACKAGES')));
    } finally {
      repo.cleanup();
    }
  });
});
