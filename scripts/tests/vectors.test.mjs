// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// Unit tests for tests/vectors/build.mjs. Run: node --test "scripts/tests/*.test.mjs"
import assert from 'node:assert/strict';
import { spawnSync } from 'node:child_process';
import { mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { after, describe, test } from 'node:test';
import { ascii, build, FORMAT, stale } from '../../tests/vectors/build.mjs';

const VECTORS = fileURLToPath(new URL('../../tests/vectors/', import.meta.url));
const BACKSLASH = String.fromCharCode(92);
const NEWLINE = String.fromCharCode(10);

const folders = [];
after(() => {
  for (const dir of folders) rmSync(dir, { recursive: true, force: true });
});

/** A vectors folder with the given suite modules. */
function vectors(suites) {
  const folder = mkdtempSync(join(tmpdir(), 'jfb-vectors-'));
  folders.push(folder);
  mkdirSync(join(folder, 'suites'));
  for (const [name, source] of Object.entries(suites)) writeFileSync(join(folder, 'suites', name), source);
  return folder;
}

const suite = (name, ids) =>
  `export default { suite: '${name}', defaults: { context: {} }, fields: {}, cases: ${JSON.stringify(ids.map((id) => ({ id, number: '1', expect: '1' })))} };`;

describe('the vector files in the repository', () => {
  test('are what their suites make', async () => {
    assert.deepEqual(await stale(VECTORS), []);
  });

  test('are plain ASCII, one per suite, and include the spike file of 1.5', async () => {
    const files = readdirSync(VECTORS).filter((name) => name.endsWith('.json')).sort();
    assert.deepEqual(files, (await build(VECTORS)).map((file) => file.name));
    assert.ok(files.includes('spike.json'));
    for (const name of files) {
      const text = readFileSync(join(VECTORS, name), 'utf8');
      const outside = [...text].filter((ch) => ch !== NEWLINE && (ch.charCodeAt(0) < 32 || ch.charCodeAt(0) > 126));
      assert.deepEqual(outside, [], name);
    }
  });

  test('keep the spike file of 1.5 at its 163 cases', async () => {
    const spike = (await build(VECTORS)).find((file) => file.name === 'spike.json');
    assert.equal(spike?.cases, 163);
  });

  test('hold at least 300 cases of conditions, rules and forms (ROADMAP 5.2a)', () => {
    let count = 0;
    for (const name of readdirSync(VECTORS).filter((file) => file.endsWith('.json'))) {
      const { cases } = JSON.parse(readFileSync(join(VECTORS, name), 'utf8'));
      count += cases.filter((vector) => vector.condition !== undefined || vector.rule !== undefined || vector.form !== undefined).length;
    }
    assert.ok(count >= 300, `${count} cases`);
  });

  test('have a mutation check whose every mutant finds its code once', () => {
    const result = spawnSync(process.execPath, [join(VECTORS, 'mutants.mjs'), '--check'], { encoding: 'utf8' });
    assert.equal(result.status, 0, result.stderr);
    assert.match(result.stdout, /each one's code there exactly once/);
  });
});

describe('ascii', () => {
  test('writes every code unit above "~" as a JSON escape, an emoji as its surrogate pair', () => {
    const json = JSON.stringify('~' + String.fromCodePoint(0xe9, 0x1f600));
    assert.equal(ascii(json), '"~' + BACKSLASH + 'u00e9' + BACKSLASH + 'ud83d' + BACKSLASH + 'ude00"');
    assert.equal(JSON.parse(ascii(json)), JSON.parse(json));
  });
});

describe('build', () => {
  test('writes each suite as <suite>.json in the jfb-vectors/1 format', async () => {
    const [file] = await build(vectors({ 'one.mjs': suite('one', ['a', 'b']) }));
    assert.equal(file.name, 'one.json');
    assert.equal(file.cases, 2);
    assert.equal(JSON.parse(file.text).format, FORMAT);
    assert.ok(file.text.endsWith('}' + NEWLINE));
  });

  test('refuses a suite named differently from its module', async () => {
    await assert.rejects(build(vectors({ 'one.mjs': suite('two', ['a']) })), /called "two", not "one"/);
  });

  test('refuses repeated ids', async () => {
    await assert.rejects(build(vectors({ 'one.mjs': suite('one', ['a', 'a']) })), /repeated ids a/);
  });

  test('reports a missing or changed file as stale', async () => {
    const folder = vectors({ 'one.mjs': suite('one', ['a']), 'two.mjs': suite('two', ['b']) });
    const [one] = await build(folder);
    writeFileSync(join(folder, one.name), one.text);
    assert.deepEqual(await stale(folder), ['two.json']);
    writeFileSync(join(folder, one.name), one.text.replace('"a"', '"z"'));
    assert.deepEqual(await stale(folder), ['one.json', 'two.json']);
  });
});
