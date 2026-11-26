// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// Unit tests for scripts/lib/identity.mjs and the identity part of scripts/pre-commit.mjs.
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { describe, test } from 'node:test';
import { MAINTAINER_EMAIL, MAINTAINER_IDENT, MAINTAINER_NAME, identityProblems, isNoreplyAddress, parseIdent } from '../lib/identity.mjs';
import { run } from '../pre-commit.mjs';

const good = `${MAINTAINER_IDENT} 1761150000 +0200`;
const contributor = 'Ada Example <1234567+ada-example@users.noreply.github.com> 1 +0000';
const personal = 'Ada Example <private@example.invalid> 1 +0000';
const NOT_PRIVATE = "address is not one of GitHub's private commit addresses";

describe('parseIdent', () => {
  test('reads the name and the email of a git ident', () => {
    assert.deepEqual(parseIdent(good), { name: MAINTAINER_NAME, email: MAINTAINER_EMAIL });
    assert.equal(parseIdent('no email here'), null);
  });
});

describe('isNoreplyAddress', () => {
  test("knows GitHub's private commit addresses, with and without the account's number", () => {
    assert.ok(isNoreplyAddress(MAINTAINER_EMAIL));
    assert.ok(isNoreplyAddress('ada-example@users.noreply.github.com'));
    assert.ok(isNoreplyAddress('1234567+ada-example@USERS.NOREPLY.GITHUB.COM'));
  });

  test('refuses every other address, look-alikes included', () => {
    const others = [
      'private@example.invalid',
      '@users.noreply.github.com',
      'ada@users.noreply.github.com.example.invalid',
      'ada@noreply.github.com',
      'a da@users.noreply.github.com',
      '',
    ];
    for (const email of others) assert.ok(!isNoreplyAddress(email), email);
  });
});

describe('identityProblems', () => {
  test("the maintainer's identity and a contributor's private commit address pass", () => {
    assert.deepEqual(identityProblems({ author: good, committer: good }), []);
    assert.deepEqual(identityProblems({ author: contributor, committer: contributor }), []);
    assert.deepEqual(identityProblems({ author: contributor, committer: good }), []);
  });

  test('a personal address is refused, per role', () => {
    assert.deepEqual(identityProblems({ author: personal, committer: good }), [`the author's ${NOT_PRIVATE}`]);
    assert.deepEqual(identityProblems({ author: good, committer: personal }), [`the committer's ${NOT_PRIVATE}`]);
    assert.equal(identityProblems({ author: personal, committer: personal }).length, 2);
  });

  test('a personal address passes where the clone allows its own address', () => {
    assert.deepEqual(identityProblems({ author: personal, committer: personal, ownEmail: true }), []);
  });

  test('a missing ident, name or address is refused, also where the clone allows its own address', () => {
    for (const ownEmail of [false, true]) {
      assert.deepEqual(identityProblems({ author: '', committer: undefined, ownEmail }), [
        'the author has no name or no address',
        'the committer has no name or no address',
      ]);
      assert.equal(identityProblems({ author: ` <${MAINTAINER_EMAIL}> 1 +0000`, committer: 'Ada Example <> 1 +0000', ownEmail }).length, 2);
    }
  });

  test('the refusal never repeats the offending address', () => {
    const problems = identityProblems({ author: personal, committer: good });
    assert.ok(!problems.join(' ').includes('private@example.invalid'));
  });
});

describe('pre-commit run', () => {
  test('refuses a personal address even when the staged files are clean, and still runs the other checks', async () => {
    const repo = mkdtempSync(join(tmpdir(), 'jfb-precommit-'));
    try {
      execFileSync('git', ['init', '-q', '-b', 'main'], { cwd: repo });
      execFileSync('git', ['config', 'core.autocrlf', 'false'], { cwd: repo });
      writeFileSync(join(repo, 'a.txt'), 'hello\n');
      execFileSync('git', ['add', 'a.txt'], { cwd: repo });

      const refused = await run({ cwd: repo, idents: { author: 'Someone <someone@example.invalid> 1 +0000', committer: good } });
      assert.equal(refused.exitCode, 1);
      assert.equal(refused.output[0], `pre-commit: refused, the author's ${NOT_PRIVATE}.`);
      assert.ok(!refused.output.join('\n').includes('someone@example.invalid'), 'the output never shows the address');
      assert.ok(
        refused.output.some((line) => line.includes('git config jfb.ownEmail true')),
        'the refusal says how a clone allows its own address',
      );
      assert.ok(refused.output.some((line) => line.startsWith('secret-scan: clean')));
      assert.ok(refused.output.some((line) => line.startsWith('check-text: clean')));

      const accepted = await run({ cwd: repo, idents: { author: good, committer: good } });
      assert.equal(accepted.exitCode, 0, accepted.output.join('\n'));
    } finally {
      rmSync(repo, { recursive: true, force: true });
    }
  });

  test("reads the idents and the clone's own setting from git when none are handed in", async () => {
    const repo = mkdtempSync(join(tmpdir(), 'jfb-precommit-'));
    try {
      execFileSync('git', ['init', '-q', '-b', 'main'], { cwd: repo });
      execFileSync('git', ['config', 'user.name', 'Someone'], { cwd: repo });
      execFileSync('git', ['config', 'user.email', 'someone@example.invalid'], { cwd: repo });
      assert.equal((await run({ cwd: repo })).exitCode, 1);

      execFileSync('git', ['config', 'jfb.ownEmail', 'true'], { cwd: repo });
      const allowed = await run({ cwd: repo });
      assert.equal(allowed.exitCode, 0, allowed.output.join('\n'));

      execFileSync('git', ['config', 'jfb.ownEmail', 'false'], { cwd: repo });
      execFileSync('git', ['config', 'user.email', '1234567+someone@users.noreply.github.com'], { cwd: repo });
      const privateAddress = await run({ cwd: repo });
      assert.equal(privateAddress.exitCode, 0, privateAddress.output.join('\n'));
    } finally {
      rmSync(repo, { recursive: true, force: true });
    }
  });
});
