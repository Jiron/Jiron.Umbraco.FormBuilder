// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// Unit tests for scripts/roadmap-status.mjs. Run: node --test "scripts/tests/*.test.mjs"
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { mkdtempSync, mkdirSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { describe, test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { blockedMissingInOwnerTodo, parseRoadmap, parseSteps, statusLine } from '../roadmap-status.mjs';

const SCRIPT = fileURLToPath(new URL('../roadmap-status.mjs', import.meta.url));

const roadmap = [
  '## How to use',
  '  - `[ ]` open (an explanation, not a step)',
  '- [Appendix A: a contents link, not a checkbox](#appendix-a)',
  '- [x] **0.1 Preflight** (2026-10-22, 781401e)',
  '- [~] **1.3 Spike: entries at scale** ',
  '  Blocked: an outage.',
  '- [ ] **4.4 The settings framework** (port the pattern, \u00a73.3)',
  '- [ ] **17.1 Logo and icon**',
  '  - [ ] **9.9 an indented line is not a step**',
].join('\n');

describe('parseSteps', () => {
  test('reads only top-level step lines with their state and title', () => {
    assert.deepEqual(
      parseSteps(roadmap).map(({ id, title, state }) => ({ id, title, state })),
      [
        { id: '0.1', title: 'Preflight', state: 'done' },
        { id: '1.3', title: 'Spike: entries at scale', state: 'blocked' },
        { id: '4.4', title: 'The settings framework', state: 'open' },
        { id: '17.1', title: 'Logo and icon', state: 'open' },
      ],
    );
    assert.deepEqual(parseRoadmap(roadmap).problems, []);
  });

  test('handles CRLF line endings', () => {
    assert.equal(parseSteps(roadmap.replace(/\n/g, '\r\n')).length, 4);
  });

  test('reads a split step (1.2a) with its letter', () => {
    const split = parseSteps('- [x] **1.2a Sandbox: navigation** (2026-10-22, abc1234)\n- [ ] **1.2b Sandbox: integration points**');
    assert.deepEqual(
      split.map((step) => [step.id, step.state]),
      [
        ['1.2a', 'done'],
        ['1.2b', 'open'],
      ],
    );
  });
});

describe('no silent misses', () => {
  test('an unparseable checkbox line is a problem with its line number', () => {
    const { steps, problems } = parseRoadmap(['- [ ] **0.1 Fine**', '- [ ] 0.2 no bold title', '- [x] **3** no id'].join('\n'));
    assert.equal(steps.length, 1);
    assert.deepEqual(
      problems.map((problem) => problem.line),
      [2, 3],
    );
  });

  test('an unknown checkbox mark is a problem, not a skipped line', () => {
    const { problems } = parseRoadmap('- [X] **2.1 Upper-case mark**\n- [-] **2.2 Dash mark**');
    assert.deepEqual(
      problems.map((problem) => problem.line),
      [1, 2],
    );
  });

  test('a done step without its date and hash is a problem', () => {
    const { problems } = parseRoadmap('- [x] **2.1 Done but not stamped**\n- [x] **2.2 Stamped** (2026-10-22, 0123abc)');
    assert.equal(problems.length, 1);
    assert.match(problems[0].reason, /2\.1/);
  });

  test('a step id that appears twice is a problem', () => {
    const { problems } = parseRoadmap('- [ ] **2.1 One**\n- [ ] **2.1 Two**');
    assert.deepEqual(
      problems.map((problem) => problem.line),
      [2],
    );
  });

  test('the script exits 1 and names the line when the roadmap has an unparseable checkbox line', () => {
    const repo = mkdtempSync(join(tmpdir(), 'jfb-roadmap-'));
    try {
      mkdirSync(join(repo, 'docs'));
      writeFileSync(join(repo, 'ROADMAP.md'), '# Roadmap\n- [ ] **0.1 Fine**\n- [ ] broken step\n');
      writeFileSync(join(repo, 'docs', 'OWNER-TODO.md'), '');
      execFileSync('git', ['init', '-q', '-b', 'main'], { cwd: repo });
      let failure;
      try {
        execFileSync(process.execPath, [SCRIPT], { cwd: repo, stdio: ['ignore', 'pipe', 'pipe'] });
      } catch (error) {
        failure = error;
      }
      assert.ok(failure, 'the script must fail');
      assert.equal(failure.status, 1);
      assert.match(failure.stderr.toString(), /ROADMAP\.md:3:/);
    } finally {
      rmSync(repo, { recursive: true, force: true });
    }
  });
});

describe('blockedMissingInOwnerTodo', () => {
  const steps = parseSteps(roadmap);

  test('a blocked step named as "step X.Y" is covered', () => {
    assert.deepEqual(blockedMissingInOwnerTodo(steps, '- [ ] Wait for the outage to end (step 1.3).'), []);
  });

  test('a blocked step that is not named is missing', () => {
    assert.deepEqual(blockedMissingInOwnerTodo(steps, '- [ ] Something else.'), ['1.3']);
  });

  test('a longer id does not count as the shorter one', () => {
    assert.deepEqual(blockedMissingInOwnerTodo(steps, 'see step 1.30 and step 11.3'), ['1.3']);
    assert.deepEqual(blockedMissingInOwnerTodo(steps, 'see step 1.3b'), ['1.3'], 'a split step is another step');
  });

  test('a blocked split step is matched with its letter', () => {
    const split = parseSteps('- [~] **9.3c Drag and drop**');
    assert.deepEqual(blockedMissingInOwnerTodo(split, 'Blocked: step 9.3c, an outage.'), []);
    assert.deepEqual(blockedMissingInOwnerTodo(split, 'Blocked: step 9.3, an outage.'), ['9.3c']);
  });
});

describe('statusLine', () => {
  test('prints every field in the agreed order', () => {
    const line = statusLine(parseSteps(roadmap), 'step 1.3', { head: 'abc1234', originMain: 'abc1234', clean: true });
    assert.equal(
      line,
      'ROADMAP done=1/4 open=2 blocked=1 blocked_missing_in_owner_todo=0 next=4.4 The settings framework head=abc1234 origin_main=abc1234 clean=yes',
    );
  });

  test('says none when nothing is open, and flags a failed fetch', () => {
    const steps = [{ id: '1.1', title: 'Done', state: 'done' }];
    const line = statusLine(steps, '', { head: 'a', originMain: 'b', clean: false, fetchFailed: true });
    assert.match(line, /next=none head=a origin_main=b clean=no fetch=failed$/);
  });
});
