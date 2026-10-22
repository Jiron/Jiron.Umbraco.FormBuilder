// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// Unit tests for scripts/term-guard.mjs. Run: node --test "scripts/tests/*.test.mjs"
// The only term used here is a canary generated at runtime, so this file never holds a real one.
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import { mkdirSync, mkdtempSync, rmSync, unlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';
import { displayPath, matchingPatterns, parseArgs, parseTerms, run, scanText } from '../term-guard.mjs';

const canary = () => `jfb-canary-${randomBytes(6).toString('hex')}`;

describe('parseTerms', () => {
  test('skips blank lines and comments, and makes every pattern case-insensitive', () => {
    const { patterns } = parseTerms('# a comment\n\nalpha\r\n  beta[0-9]+  \n#gamma');
    assert.equal(patterns.length, 2);
    assert.deepEqual(matchingPatterns(patterns, 'ALPHA and Beta42'), [1, 2]);
  });

  test('an invalid pattern is an error that names its number, never its text', () => {
    const secret = canary();
    const { error } = parseTerms(`fine\n${secret}(unclosed`);
    assert.match(error, /pattern 2 \(line 2/);
    assert.ok(!error.includes(secret));
  });

  test('a byte-order mark at the start of the list is ignored', () => {
    const { patterns } = parseTerms(String.fromCharCode(0xfeff) + 'alpha');
    assert.deepEqual(matchingPatterns(patterns, 'alpha'), [1]);
  });

  test('decomposed and composed accents match the same pattern', () => {
    const composed = 'caf' + String.fromCharCode(0xe9);
    const decomposed = 'cafe' + String.fromCharCode(0x301);
    const { patterns } = parseTerms(composed);
    assert.deepEqual(matchingPatterns(patterns, `at the ${decomposed} today`), [1]);
  });
});

describe('scanning text and paths', () => {
  test('hits carry line numbers and pattern numbers only', () => {
    const term = canary();
    const { patterns } = parseTerms(`other\n${term}`);
    assert.deepEqual(scanText(patterns, `one\ntwo ${term.toUpperCase()}\r\nthree`), [{ line: 2, pattern: 2 }]);
  });

  test('a path that matches a pattern is redacted', () => {
    const term = canary();
    const { patterns } = parseTerms(term);
    assert.equal(displayPath(patterns, 'docs/readme.md'), 'docs/readme.md');
    const shown = displayPath(patterns, `docs/${term}.md`);
    assert.match(shown, /^<redacted path sha256:[0-9a-f]{12}>$/);
  });
});

describe('parseArgs', () => {
  test('reads each mode and the list option', () => {
    assert.deepEqual(parseArgs([]), { mode: 'staged', termsFile: undefined, messageFile: undefined });
    assert.deepEqual(parseArgs(['--tracked', '--terms', 'x.txt']), { mode: 'tracked', termsFile: 'x.txt', messageFile: undefined });
    assert.deepEqual(parseArgs(['--message', 'MSG']), { mode: 'message', termsFile: undefined, messageFile: 'MSG' });
  });

  test('refuses unknown options, two modes and missing values', () => {
    assert.equal(parseArgs(['--all']), null);
    assert.equal(parseArgs(['--tracked', '--history']), null);
    assert.equal(parseArgs(['--message']), null);
    assert.equal(parseArgs(['--terms']), null);
  });
});

describe('git modes (temporary repository)', () => {
  const repo = mkdtempSync(join(tmpdir(), 'jfb-terms-'));
  const git = (...args) => execFileSync('git', args, { cwd: repo, stdio: ['ignore', 'pipe', 'pipe'] }).toString();
  git('init', '-q', '-b', 'main');
  git('config', 'user.name', 'Guard Test');
  git('config', 'user.email', 'guard@example.test');
  git('config', 'core.autocrlf', 'false');
  git('config', 'core.hooksPath', '.no-hooks');
  writeFileSync(join(repo, '.gitignore'), '.private/\n');
  writeFileSync(join(repo, 'README.md'), '# test\n');
  git('add', '.gitignore', 'README.md');
  git('commit', '-q', '-m', 'start');
  after(() => rmSync(repo, { recursive: true, force: true }));

  const term = canary();
  const listFile = join(repo, 'synthetic-terms.txt');
  writeFileSync(listFile, `# synthetic list\n${term}\n`);

  test('without the default list the guard skips and exits 0', async () => {
    const result = await run({ mode: 'tracked', cwd: repo });
    assert.equal(result.exitCode, 0);
    assert.ok(result.output[0].includes('term list not present, skipped (tracked, 0 patterns loaded)'), result.output[0]);
  });

  test('a --terms file that does not exist is an error', async () => {
    const result = await run({ mode: 'tracked', cwd: repo, termsFile: join(repo, 'missing.txt') });
    assert.equal(result.exitCode, 2);
  });

  test('the default list in .private/terms.txt is used when present, and its pattern count is reported', async () => {
    mkdirSync(join(repo, '.private'), { recursive: true });
    writeFileSync(join(repo, '.private', 'terms.txt'), `${term}\nsecond-pattern\n`);
    try {
      const result = await run({ mode: 'tracked', cwd: repo });
      assert.equal(result.exitCode, 0, result.output.join('\n'));
      assert.match(result.output[0], /clean \(tracked, 2 files, 2 patterns loaded\)/);
    } finally {
      rmSync(join(repo, '.private'), { recursive: true, force: true });
    }
  });

  test('staged mode finds the term, names file, line and pattern number, and never prints the term', async () => {
    writeFileSync(join(repo, 'notes.md'), `fine\nmentions ${term} here\n`);
    git('add', 'notes.md');
    const result = await run({ mode: 'staged', cwd: repo, termsFile: listFile });
    const text = result.output.join('\n');
    assert.equal(result.exitCode, 1);
    assert.match(text, /notes\.md:2 {2}pattern #1/);
    assert.match(text, /1 patterns loaded/);
    assert.ok(!text.toLowerCase().includes(term));
    git('rm', '-q', '--cached', 'notes.md');
    unlinkSync(join(repo, 'notes.md'));
  });

  test('a staged file whose name holds the term is reported with a redacted path', async () => {
    writeFileSync(join(repo, `${term}.txt`), 'harmless\n');
    git('add', `${term}.txt`);
    const result = await run({ mode: 'staged', cwd: repo, termsFile: listFile });
    const text = result.output.join('\n');
    assert.equal(result.exitCode, 1);
    assert.match(text, /the file path <redacted path sha256:[0-9a-f]{12}> {2}pattern #1/);
    assert.ok(!text.includes(term));
    git('rm', '-q', '--cached', `${term}.txt`);
    unlinkSync(join(repo, `${term}.txt`));
  });

  test('message mode finds the term in a commit message', async () => {
    const message = join(repo, 'MSG');
    writeFileSync(message, `feat: something\n\nabout ${term.toUpperCase()}\n`);
    const result = await run({ mode: 'message', cwd: repo, termsFile: listFile, messageFile: message });
    assert.equal(result.exitCode, 1);
    assert.match(result.output.join('\n'), /commit message line 3 {2}pattern #1/);
    unlinkSync(message);
  });

  test('history mode finds the term in removed lines, messages, paths, ref names and tag messages', async () => {
    writeFileSync(join(repo, 'old.txt'), `${term}\n`);
    git('add', 'old.txt');
    git('commit', '-q', '-m', `add (${term})`);
    git('rm', '-q', 'old.txt');
    git('commit', '-q', '-m', 'remove');
    git('tag', '-a', 'v1', '-m', `tag text ${term}`);
    git('branch', `topic-${term}`);

    const tracked = await run({ mode: 'tracked', cwd: repo, termsFile: listFile });
    assert.equal(tracked.exitCode, 0, 'the term is gone from the tracked files');

    const history = await run({ mode: 'history', cwd: repo, termsFile: listFile });
    const text = history.output.join('\n');
    assert.equal(history.exitCode, 1);
    assert.match(text, /commit [0-9a-f]{7} old\.txt:1 {2}pattern #1/);
    assert.match(text, /commit [0-9a-f]{7} message line 1 {2}pattern #1/);
    assert.match(text, /the name of ref #\d+ {2}pattern #1/);
    assert.match(text, /the tag message of ref #\d+ line 1 {2}pattern #1/);
    assert.ok(!text.toLowerCase().includes(term));

    git('tag', '-d', 'v1');
    git('branch', '-D', `topic-${term}`);
  });
});
