// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// Unit tests for scripts/e2e-gate.mjs. Run: node --test "scripts/tests/*.test.mjs"
import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';
import { hangMarkerName, hangMarkers, runWithRerun } from '../e2e-gate.mjs';

/** A gate run with fake runs: each entry is the exit code of one run and the markers it leaves. */
function simulate(runs, stale = []) {
  let present = [...stale];
  const calls = { runs: 0, cleared: [], log: [] };
  const code = runWithRerun({
    run: () => {
      const { exit, leaves = [] } = runs[calls.runs++];
      present.push(...leaves);
      return exit;
    },
    markers: () => [...present],
    clear: (files) => {
      calls.cleared.push([...files]);
      present = present.filter((file) => !files.includes(file));
    },
    read: () => 'from 06:00:24, 2 command(s) timed out\n',
    log: (line) => calls.log.push(line),
  });
  return { code, ...calls };
}

describe('runWithRerun', () => {
  test('a passing run is the only run', () => {
    const result = simulate([{ exit: 0 }]);
    assert.equal(result.code, 0);
    assert.equal(result.runs, 1);
    assert.deepEqual(result.log, []);
  });

  test('a failure without the hang marker fails at once ("Flaky means broken")', () => {
    const result = simulate([{ exit: 1 }, { exit: 0 }]);
    assert.equal(result.code, 1);
    assert.equal(result.runs, 1);
  });

  test('a failure with the hang marker runs once more, and says why', () => {
    const passed = simulate([{ exit: 1, leaves: ['host-5520/database-hung.txt'] }, { exit: 0 }]);
    assert.equal(passed.code, 0);
    assert.equal(passed.runs, 2);
    assert.match(passed.log[0], /a host's database hung \(from 06:00:24, 2 command\(s\) timed out; host-5520\/database-hung\.txt\)/);
    assert.match(passed.log.at(-1), /the rerun passed/);
  });

  test('runs at most twice: a second hang fails the gate', () => {
    const marker = 'host-5520/database-hung.txt';
    const result = simulate([{ exit: 1, leaves: [marker] }, { exit: 1, leaves: [marker] }, { exit: 0 }]);
    assert.equal(result.code, 1);
    assert.equal(result.runs, 2);
    assert.match(result.log.at(-1), /the rerun failed too/);
  });

  test('a marker left by an earlier run is removed first and causes no rerun', () => {
    const result = simulate([{ exit: 1 }, { exit: 0 }], ['host-5520/database-hung.txt']);
    assert.deepEqual(result.cleared[0], ['host-5520/database-hung.txt']);
    assert.equal(result.code, 1);
    assert.equal(result.runs, 1);
  });
});

describe('the markers', () => {
  const dir = mkdtempSync(join(tmpdir(), 'jfb-e2e-gate-'));
  after(() => rmSync(dir, { recursive: true, force: true }));

  test("are found in every host's folder, and nowhere else", () => {
    for (const host of ['host-5520', 'host-5522', 'host-5524']) mkdirSync(join(dir, host));
    writeFileSync(join(dir, 'host-5520', 'database-hung.txt'), 'x');
    writeFileSync(join(dir, 'host-5524', 'database-hung.txt'), 'x');
    writeFileSync(join(dir, 'database-hung.txt'), 'not a host folder');
    assert.deepEqual(hangMarkers(dir, 'database-hung.txt').sort(), [join(dir, 'host-5520', 'database-hung.txt'), join(dir, 'host-5524', 'database-hung.txt')]);
    assert.deepEqual(hangMarkers(join(dir, 'missing'), 'database-hung.txt'), []);
  });

  test("take their name from the harness's constant", () => {
    assert.equal(hangMarkerName(), 'database-hung.txt');
    assert.equal(hangMarkerName("export const HANG_MARKER = 'other.txt';"), 'other.txt');
    assert.throws(() => hangMarkerName('nothing here'), /no longer exports HANG_MARKER/);
  });
});
