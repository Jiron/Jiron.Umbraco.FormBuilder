// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// Unit tests for scripts/third-party-notices.mjs. Run: node --test "scripts/tests/*.test.mjs"
import assert from 'node:assert/strict';
import { readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { describe, test } from 'node:test';
import { packagesFolder } from '../lib/nuget.mjs';
import {
  build,
  devTableProblems,
  expandRow,
  NOTICES,
  readDevTable,
  renderPublicDomain,
  renderShipped,
  replaceBetween,
} from '../third-party-notices.mjs';
import { fakeRepo, put } from './fake-repo.mjs';

describe('the generated parts', () => {
  test('replace only the text between their markers, and refuse a file without them', () => {
    const text = 'a\n<!-- s -->\nold\n<!-- e -->\nz';
    assert.equal(replaceBetween(text, '<!-- s -->', '<!-- e -->', 'new'), 'a\n<!-- s -->\nnew\n<!-- e -->\nz');
    assert.throws(() => replaceBetween('nothing', '<!-- s -->', '<!-- e -->', 'new'), /lacks the markers/);
  });

  test('say there is nothing to list, or render a table that escapes pipes', () => {
    assert.match(renderShipped([]), /^None: /);
    assert.equal(renderPublicDomain([]), 'None.');
    const table = renderShipped([{ id: 'A', version: '1.0.0', licence: 'MIT', note: '', copyright: 'a | b' }]);
    assert.match(table, /\| `A` \| 1\.0\.0 \| MIT \| a \\\| b \|/);
    assert.match(renderShipped([{ id: 'B', version: '2.0.0', licence: 'MIT', note: 'reviewed', copyright: '' }]), /\| MIT \(reviewed\) \| - \|$/);
  });
});

describe('the development table', () => {
  const table = `## Development only (never shipped)

| Package | Version | Licence | Used for |
| --- | --- | --- | --- |
| \`a\`, \`b\` | 1.0.0, 2.0.0 | MIT | x |
| \`c\` | 3.0.0, 3.1.0 | ISC | y |

## Documents

| \`not\` | 9 | MIT | z |
`;

  test('is read from its own section only', () => {
    assert.deepEqual(
      readDevTable(table).map((row) => row.names),
      [['a', 'b'], ['c']],
    );
    assert.throws(() => readDevTable('# nothing'), /lacks the heading/);
  });

  test('pairs versions and licences with names: one each, one for all, or several for a single name', () => {
    const [ab, c] = readDevTable(table).map(expandRow);
    assert.deepEqual(ab.map((entry) => [entry.name, [...entry.versions], [...entry.licences]]), [
      ['a', ['1.0.0'], ['MIT']],
      ['b', ['2.0.0'], ['MIT']],
    ]);
    assert.deepEqual([...c[0].versions], ['3.0.0', '3.1.0']);
    assert.throws(() => expandRow({ names: ['a', 'b', 'c'], versions: ['1', '2'], licences: ['MIT'], line: 'row' }), /2 versions for 3 packages/);
    assert.throws(() => expandRow({ names: [], versions: ['1'], licences: ['MIT'], line: 'row' }), /names no package/);
  });

  test('must name every direct tool with the restored versions and licences, and no package nothing restores', () => {
    const rows = readDevTable(table);
    const npm = {
      direct: new Set(['a', 'missing-tool']),
      versions: new Map([['a', new Set(['1.0.0'])], ['b', new Set(['2.0.1'])]]),
      licences: new Map([['a', new Set(['MIT'])], ['b', new Set(['Apache-2.0'])]]),
    };
    const nuget = { direct: new Set(['dev.tool']), versions: new Map(), licences: new Map() };
    const problems = devTableProblems(rows, npm, nuget);
    assert.deepEqual(problems, [
      'b: the table says 2.0.0, restored is 2.0.1',
      'b: the table says the licence MIT, its metadata says Apache-2.0',
      'c is listed, but nothing restores it: remove its row',
      'missing-tool is a development tool of an npm project but has no row',
      'the NuGet package dev.tool is referenced by the tests or the demo but has no row',
    ]);
  });
});

describe('build', () => {
  test('reads the nuspecs from NUGET_PACKAGES when it is set, and writes the shipped table and the ADR-0027 list', () => {
    const repo = fakeRepo({ cacheOf: (base) => join(base, 'ci-workspace', '.nuget', 'packages') });
    try {
      const cache = packagesFolder({ NUGET_PACKAGES: repo.cache }, join(repo.root, 'no-home'));
      const result = build(repo.root, cache);
      assert.deepEqual(result.problems, []);
      assert.match(result.updated, /<!-- jfb-shipped:start -->\n\| Package \| Version \| Licence \| Copyright \|\n\| --- \| --- \| --- \| --- \|\n\| `Shipped\.Lib` \| 1\.0\.0 \| MIT \| Shipped \\\| Ltd \|\n<!-- jfb-shipped:end -->/);
      assert.match(result.updated, /\| `pd-lib` \| 0\.1\.0 \| CC0-1\.0 \| `scripts\/` \|/);
      assert.notEqual(result.updated, result.text);
      writeFileSync(join(repo.root, NOTICES), result.updated);
      assert.equal(build(repo.root, cache).updated, result.updated, 'a written file is current');
    } finally {
      repo.cleanup();
    }
  });

  test("reads them from the default cache under the user's home when NUGET_PACKAGES is not set", () => {
    const repo = fakeRepo({ cacheOf: (base) => join(base, 'home', '.nuget', 'packages') });
    try {
      const home = join(repo.cache, '..', '..');
      const cache = packagesFolder({}, home);
      assert.equal(cache, repo.cache);
      assert.deepEqual(build(repo.root, cache).problems, []);
      assert.throws(() => build(repo.root, packagesFolder({}, join(home, 'elsewhere'))), /same NUGET_PACKAGES/);
    } finally {
      repo.cleanup();
    }
  });

  test('reports a shipped package whose licence rule 4 refuses, and a stale development row', () => {
    const repo = fakeRepo();
    try {
      put(repo.cache, 'shipped.lib/1.0.0/shipped.lib.nuspec', '<package><metadata><license type="expression">GPL-3.0-only</license></metadata></package>');
      const notices = readFileSync(join(repo.root, NOTICES), 'utf8').replace('| `Dev.Tool` | 2.0.0 |', '| `Dev.Tool` | 1.9.0 |');
      writeFileSync(join(repo.root, NOTICES), notices);
      const { problems } = build(repo.root, repo.cache);
      assert.deepEqual(problems, [
        'Shipped.Lib 1.0.0 ships to sites with GPL-3.0-only, which is not allowed for a shipped package (CLAUDE.md rule 4)',
        'Dev.Tool: the table says 1.9.0, restored is 2.0.0',
      ]);
    } finally {
      repo.cleanup();
    }
  });
});
