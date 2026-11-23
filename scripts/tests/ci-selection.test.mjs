// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// Unit tests for scripts/ci-selection.mjs: which part of CI a run runs, from the inputs of a targeted run.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { describe, test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { outputLines, PARTS, select, SHARES } from '../ci-selection.mjs';

describe('select', () => {
  test('runs everything without inputs, as a push, a tag and the release call it', () => {
    for (const only of ['', 'all', '  ']) {
      const all = select(only, '');
      assert.deepEqual(
        [all.build, all.sqlite, all.sqlserver, all.smoke, all.loadbalancing, all.everything],
        [true, true, true, true, true, true],
        `only "${only}"`,
      );
      assert.deepEqual([all.sqliteShares, all.sqlserverShares], [[...SHARES.sqlite], [...SHARES.sqlserver]]);
    }
  });

  test('runs one part alone, and adds nothing up', () => {
    for (const part of PARTS.filter((each) => each !== 'all')) {
      const one = select(part);
      const running = ['build', 'sqlite', 'sqlserver', 'smoke', 'loadbalancing'].filter((name) => one[name]);
      assert.deepEqual(running, [part]);
      assert.equal(one.everything, false);
    }
  });

  test('runs the shares named of the database named, in the matrix order, and every share of a skipped one', () => {
    const three = select('sqlserver', '["timed","3","8"]');
    assert.deepEqual(three.sqlserverShares, ['3', '8', 'timed']);
    assert.deepEqual(three.sqliteShares, [...SHARES.sqlite], 'a skipped matrix is never empty');
    assert.deepEqual(select('sqlite', '["6"]').sqliteShares, ['6']);
  });

  test('refuses a wrong input, naming it', () => {
    assert.match(select('docs').error, /only must be one of/);
    assert.match(select('all', '["3"]').error, /go with only sqlite or only sqlserver/);
    assert.match(select('smoke', '["3"]').error, /go with only sqlite/);
    assert.match(select('sqlite', '["8"]').error, /sqlite has no share 8/);
    assert.match(select('sqlserver', '3').error, /JSON list/);
    assert.match(select('sqlserver', '[]').error, /JSON list/);
    assert.match(select('sqlserver', '[3]').error, /JSON list/);
    assert.match(select('sqlserver', 'not json').error, /JSON list/);
  });

  test('writes the outputs ci.yml reads', () => {
    assert.deepEqual(outputLines(select('sqlserver', '["3"]')), [
      'build=false',
      'sqlite=false',
      'sqlserver=true',
      'smoke=false',
      'loadbalancing=false',
      'everything=false',
      `sqlite-shares=${JSON.stringify(SHARES.sqlite)}`,
      'sqlserver-shares=["3"]',
    ]);
  });

  test('every output is one the selection job of ci.yml passes on', () => {
    const workflow = readFileSync(fileURLToPath(new URL('../../.github/workflows/ci.yml', import.meta.url)), 'utf8');
    for (const line of outputLines(select())) {
      const name = line.split('=')[0];
      assert.ok(workflow.includes(`${name}: \${{ steps.select.outputs.${name} }}`), name);
    }
  });
});
