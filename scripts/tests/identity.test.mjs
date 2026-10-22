// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// Unit tests for scripts/lib/identity.mjs and the identity part of scripts/pre-commit.mjs.
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { describe, test } from 'node:test';
import { REQUIRED_EMAIL, REQUIRED_IDENT, REQUIRED_NAME, identityProblems, parseIdent } from '../lib/identity.mjs';
import { run } from '../pre-commit.mjs';

const good = `${REQUIRED_IDENT} 1761150000 +0200`;

describe('parseIdent', () => {
  test('reads the name and the email of a git ident', () => {
    assert.deepEqual(parseIdent(good), { name: REQUIRED_NAME, email: REQUIRED_EMAIL });
    assert.equal(parseIdent('no email here'), null);
  });
});

describe('identityProblems', () => {
  test('the required identity as author and committer passes', () => {
    assert.deepEqual(identityProblems({ author: good, committer: good }), []);
  });

  test('another email, another name or a missing ident is refused, per role', () => {
    assert.deepEqual(identityProblems({ author: 'Jiron <someone@example.invalid> 1 +0000', committer: good }), [`the author is not ${REQUIRED_IDENT}`]);
    assert.deepEqual(identityProblems({ author: good, committer: `Someone <${REQUIRED_EMAIL}> 1 +0000` }), [`the committer is not ${REQUIRED_IDENT}`]);
    assert.equal(identityProblems({ author: '', committer: undefined }).length, 2);
  });

  test('the refusal never repeats the offending address', () => {
    const problems = identityProblems({ author: 'Owner <private@example.invalid> 1 +0000', committer: good });
    assert.ok(!problems.join(' ').includes('private@example.invalid'));
  });
});

describe('pre-commit run', () => {
  test('refuses a wrong identity even when the staged files are clean, and still runs the other checks', async () => {
    const repo = mkdtempSync(join(tmpdir(), 'jfb-precommit-'));
    try {
      execFileSync('git', ['init', '-q', '-b', 'main'], { cwd: repo });
      execFileSync('git', ['config', 'core.autocrlf', 'false'], { cwd: repo });
      writeFileSync(join(repo, 'a.txt'), 'hello\n');
      execFileSync('git', ['add', 'a.txt'], { cwd: repo });

      const refused = await run({ cwd: repo, idents: { author: 'Someone <someone@example.invalid> 1 +0000', committer: good } });
      assert.equal(refused.exitCode, 1);
      assert.match(refused.output[0], /refused, the author is not/);
      assert.ok(refused.output.some((line) => line.startsWith('secret-scan: clean')));
      assert.ok(refused.output.some((line) => line.startsWith('check-text: clean')));

      const accepted = await run({ cwd: repo, idents: { author: good, committer: good } });
      assert.equal(accepted.exitCode, 0, accepted.output.join('\n'));
    } finally {
      rmSync(repo, { recursive: true, force: true });
    }
  });
});
