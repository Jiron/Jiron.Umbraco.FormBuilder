// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// Unit tests for scripts/verify-clean.mjs. Run: node --test "scripts/tests/*.test.mjs"
import assert from 'node:assert/strict';
import { join } from 'node:path';
import { describe, test } from 'node:test';
import { lastLineOf, parseArgs, stampOf, stepsOf, summaryOf, verifyPaths } from '../verify-clean.mjs';

describe('parseArgs', () => {
  test('keeps nothing by default, the folders with --keep, and refuses anything else', () => {
    assert.deepEqual(parseArgs([]), { keep: false });
    assert.deepEqual(parseArgs(['--keep']), { keep: true });
    assert.match(parseArgs(['--force']).error, /unknown argument --force/);
  });
});

describe('a run', () => {
  test('is stamped with the time in UTC to the minute', () => {
    assert.equal(stampOf(new Date(Date.UTC(2026, 10, 14, 3, 7, 59))), '202611140307');
  });

  test('clones and caches under the temporary folder, outside the repository, and logs under .local/verify', () => {
    const paths = verifyPaths('C:/repo', '202611140307', 'C:/temp');
    assert.deepEqual(paths, {
      clone: join('C:/temp', 'jfb-verify-202611140307'),
      cache: join('C:/temp', 'jfb-nuget-202611140307'),
      logs: join('C:/repo', '.local', 'verify', '202611140307'),
    });
  });

  test("runs the roadmap's steps in its order, all in the clone but the term guard, which needs the private folder", () => {
    const steps = stepsOf();
    assert.deepEqual(
      steps.map((step) => [step.name, step.where]),
      [
        ['ci-local', 'clone'],
        ['pack', 'clone'],
        ['install', 'clone'],
        ['floor', 'clone'],
        ['upgrade', 'clone'],
        ['secret-scan', 'clone'],
        ['term-guard', 'here'],
      ],
    );
    const args = (name) => steps.find((step) => step.name === name)?.command[1] ?? [];
    assert.ok(args('secret-scan').includes('--history'));
    assert.ok(args('term-guard').includes('--history'));
    assert.deepEqual(args('upgrade').slice(-2), ['-Mode', 'upgrade']);
  });
});

describe('the summary', () => {
  test("takes a step's verdict from the last line of its log", () => {
    assert.equal(lastLineOf('one\r\nci-local: all checks passed in 3,939 s\r\n\r\n'), 'ci-local: all checks passed in 3,939 s');
    assert.equal(lastLineOf(''), '');
  });

  test('says each step, and every step passed with their time, or where it failed', () => {
    const passed = [
      { name: 'ci-local', code: 0, seconds: 3900, verdict: 'ci-local: all checks passed' },
      { name: 'pack', code: 0, seconds: 100, verdict: 'pack: packed' },
    ];
    const lines = summaryOf(passed);
    assert.equal(lines.length, 3);
    assert.match(lines[0], /^verify-clean: ci-local\s+passed\s+3900 s {2}ci-local: all checks passed$/);
    assert.equal(lines.at(-1), 'verify-clean: every step passed in 4000 s');
    assert.equal(summaryOf([...passed, { name: 'install', code: 1, seconds: 5, verdict: 'boom' }]).at(-1), 'verify-clean: FAILED at install');
  });
});
