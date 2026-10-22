// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// The git hooks, end to end (ROADMAP 0.2 "the hooks work"): a throwaway clone under %TEMP%\jfb-hooktest-<timestamp>,
// the hooks installed there, a synthetic term list with one canary pattern, and real commits that must be refused.
// The working tree's scripts/ and .githooks/ are copied over the clone, so uncommitted changes are tested too.
// Every key-shaped value and the canary are generated at runtime.
import assert from 'node:assert/strict';
import { execFileSync, spawnSync } from 'node:child_process';
import { randomBytes, randomInt } from 'node:crypto';
import { cpSync, mkdirSync, rmSync, unlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, before, describe, test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { REQUIRED_EMAIL, REQUIRED_NAME } from '../lib/identity.mjs';

const ROOT = fileURLToPath(new URL('../..', import.meta.url));
const stamp = new Date().toISOString().replace(/[-:T]/g, '').slice(0, 14);
const clone = join(tmpdir(), `jfb-hooktest-${stamp}-${randomBytes(3).toString('hex')}`);
const canary = `jfb-canary-${randomBytes(6).toString('hex')}`;
const awsShaped = () => 'AKIA' + Array.from({ length: 16 }, () => 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567'[randomInt(32)]).join('');

function git(args, options = {}) {
  return spawnSync('git', args, { cwd: clone, encoding: 'utf8', ...options });
}

function head() {
  return git(['rev-parse', 'HEAD']).stdout.trim();
}

/** Stages one file and tries to commit it. Returns the spawn result; the file is unstaged and removed afterwards. */
function tryCommit(name, content, { message = 'test: a commit', config = [], extra = [] } = {}) {
  writeFileSync(join(clone, name), content);
  git(['add', name]);
  const result = git([...config.flatMap((setting) => ['-c', setting]), 'commit', '-m', message, ...extra]);
  return result;
}

function cleanUp(name) {
  git(['reset', '-q', '--', name]);
  try {
    unlinkSync(join(clone, name));
  } catch {
    // Already gone after a successful commit test.
  }
}

describe('the git hooks in a throwaway clone', () => {
  before(() => {
    execFileSync('git', ['clone', '-q', '--no-hardlinks', ROOT, clone], { stdio: 'pipe' });
    for (const folder of ['scripts', '.githooks']) {
      cpSync(join(ROOT, folder), join(clone, folder), { recursive: true, filter: (source) => !source.includes('node_modules') });
    }
    execFileSync('git', ['config', 'user.name', REQUIRED_NAME], { cwd: clone });
    execFileSync('git', ['config', 'user.email', REQUIRED_EMAIL], { cwd: clone });
    execFileSync('git', ['config', 'core.autocrlf', 'false'], { cwd: clone });
    execFileSync(process.execPath, ['scripts/install-hooks.mjs'], { cwd: clone, stdio: 'pipe' });
    mkdirSync(join(clone, '.private'), { recursive: true });
    writeFileSync(join(clone, '.private', 'terms.txt'), `# synthetic list for the hook test\n${canary}\n`);
  });

  after(() => rmSync(clone, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 }));

  test('the clone uses the committed hooks folder', () => {
    assert.equal(git(['config', 'core.hooksPath']).stdout.trim(), '.githooks');
  });

  const refusals = [
    {
      name: 'a staged file containing a key-shaped string',
      file: 'config.txt',
      content: () => `token ${awsShaped()}\n`,
      expect: /secret-scan: 1 hit/,
    },
    {
      name: 'a staged file containing the canary',
      file: 'notes.md',
      content: () => `mentions ${canary} here\n`,
      expect: /term-guard: 1 hit/,
    },
    {
      name: 'a commit message containing the canary',
      file: 'clean.md',
      content: () => 'harmless\n',
      options: { message: `docs: about ${canary}` },
      expect: /commit message line 1 {2}pattern #1/,
    },
    {
      name: 'a staged file containing a form feed',
      file: 'damaged.md',
      content: () => `one${String.fromCharCode(0x0c)}two\n`,
      expect: /check-text: 1 problem/,
    },
    {
      name: 'a commit made with another email address',
      file: 'other.md',
      content: () => 'harmless\n',
      options: { config: ['user.email=someone@example.invalid'] },
      expect: /refused, the author is not .* and the committer is not/,
    },
    {
      name: 'a commit with another author',
      file: 'author.md',
      content: () => 'harmless\n',
      options: { extra: ['--author', 'Someone Else <someone@example.invalid>'] },
      expect: /refused, the author is not/,
    },
  ];

  for (const refusal of refusals) {
    test(`refuses ${refusal.name}`, () => {
      const before = head();
      const result = tryCommit(refusal.file, refusal.content(), refusal.options);
      try {
        const output = `${result.stdout}\n${result.stderr}`;
        assert.notEqual(result.status, 0, `the commit must fail:\n${output}`);
        assert.equal(head(), before, 'no commit was made');
        assert.match(output, refusal.expect);
        assert.ok(!output.includes(canary), 'the output never shows the term');
        assert.ok(!output.includes('someone@example.invalid'), 'the output never shows the address');
      } finally {
        cleanUp(refusal.file);
      }
    });
  }

  test('accepts a clean commit with the right identity, so the refusals above are real', () => {
    const before = head();
    const result = tryCommit('fine.md', 'all good\n', { message: 'docs: a clean commit' });
    assert.equal(result.status, 0, `${result.stdout}\n${result.stderr}`);
    assert.notEqual(head(), before);
    const ident = git(['log', '-1', '--format=%an <%ae> | %cn <%ce>']).stdout.trim();
    assert.equal(ident, `${REQUIRED_NAME} <${REQUIRED_EMAIL}> | ${REQUIRED_NAME} <${REQUIRED_EMAIL}>`);
  });
});
