// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// Unit tests for scripts/gate-clone.mjs. Run: node --test "scripts/tests/*.test.mjs"
import assert from 'node:assert/strict';
import { existsSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';
import { fileURLToPath } from 'node:url';
import { browserPorts, copyBrowserResults, gatePaths, linksUnder, parseArgs, removeTree, verdictOf } from '../gate-clone.mjs';

const root = join(fileURLToPath(new URL('.', import.meta.url)), '..', '..');
const folders = [];
after(() => {
  for (const dir of folders) rmSync(dir, { recursive: true, force: true });
});

describe('parseArgs', () => {
  test('gates HEAD by default and keeps nothing', () => {
    assert.deepEqual(parseArgs(['run']), { command: 'run', commit: 'HEAD', keep: false });
  });

  test('takes one commit and --keep in any order', () => {
    assert.deepEqual(parseArgs(['run', '--keep', 'abc1234']), { command: 'run', commit: 'abc1234', keep: true });
  });

  test('refuses an unknown command, a second commit and options the command does not take', () => {
    assert.match(parseArgs(['go']).error, /unknown command go/);
    assert.match(parseArgs([]).error, /unknown command \(none\)/);
    assert.match(parseArgs(['run', 'a', 'b']).error, /unknown argument b/);
    assert.match(parseArgs(['status', '--keep']).error, /unknown argument --keep/);
    assert.match(parseArgs(['run', '--force']).error, /unknown argument --force/);
  });
});

describe('gatePaths', () => {
  test('puts the clone in the temporary folder by the short hash, and the state under .local/gate', () => {
    const paths = gatePaths('C:/repo', '0123456789abcdef0123456789abcdef01234567', 'C:/temp');
    assert.equal(paths.clone, join('C:/temp', 'jfb-gate-0123456'));
    assert.equal(paths.log, join('C:/repo', '.local', 'gate', '0123456.log'));
    assert.equal(paths.results, join('C:/repo', '.local', 'gate', '0123456-e2e'));
    assert.equal(paths.pidFile, join('C:/repo', '.local', 'gate', 'running.json'));
    assert.equal(paths.lastFile, join('C:/repo', '.local', 'gate', 'last.json'));
  });
});

describe('browserPorts', () => {
  test("reads the harness's own ports", () => {
    const ports = browserPorts(readFileSync(join(root, 'tests', 'e2e', 'support', 'sites.ts'), 'utf8'));
    assert.ok(ports.length >= 3, `found ${ports.join(', ')}`);
    for (const port of ports) assert.ok(port >= 5500 && port <= 5599, `${port} is in Jiron Form Builder's range`);
  });

  test('reads every single port and map of them the harness exports, and nothing else', () => {
    const source = [
      'export const PORT_RANGE = { first: 5500, last: 5599 } as const;',
      'export const E2E_PORT = 5520;',
      'export const E2E_BASE_URL = `http://127.0.0.1:${E2E_PORT}`;',
      'export const REFUSAL_PORTS = { configuration: 5526, globalization: 5527 } as const;',
      'export const STUB_PORT = 5560;',
      'export const CACHED_PORT = 5525;',
    ].join('\n');
    assert.deepEqual(browserPorts(source), [5520, 5526, 5527, 5560, 5525]);
  });

  test('fails when the harness names no port', () => {
    assert.throws(() => browserPorts('export const OTHER = 1;'), /no longer names/);
  });
});

describe('verdictOf', () => {
  test("takes the gate's last word about itself", () => {
    const log = [
      '== scripts',
      "ci-local: FAILED in 'client': npm exited with code 1",
      'more',
      'ci-local: all checks passed in 762 s',
      '',
    ].join('\r\n');
    assert.equal(verdictOf(log), 'ci-local: all checks passed in 762 s');
  });

  test('is null for a log the gate never finished', () => {
    assert.equal(verdictOf('== scripts\nnode scripts/secret-scan.mjs\n'), null);
  });
});

describe('removeTree', () => {
  test("removes a clone's workspace links one by one and never the targets they point at", () => {
    const base = mkdtempSync(join(tmpdir(), 'jfb-gate-test-'));
    folders.push(base);
    const target = join(base, 'package');
    mkdirSync(target);
    writeFileSync(join(target, 'kept.txt'), 'the package itself');
    const clone = join(base, 'clone');
    const scope = join(clone, 'node_modules', '@jfb');
    mkdirSync(scope, { recursive: true });
    writeFileSync(join(clone, 'file.txt'), 'a file of the clone');
    const link = join(scope, 'package');
    symlinkSync(target, link, 'junction');

    assert.deepEqual(linksUnder(clone), [link]);
    removeTree(clone);

    assert.equal(existsSync(clone), false);
    assert.equal(readFileSync(join(target, 'kept.txt'), 'utf8'), 'the package itself');
  });

  test('is quiet about a folder that is not there', () => {
    assert.doesNotThrow(() => removeTree(join(tmpdir(), 'jfb-gate-test-missing')));
    assert.deepEqual(linksUnder(join(tmpdir(), 'jfb-gate-test-missing')), []);
  });
});

describe('copyBrowserResults', () => {
  test("keeps every site's results, every host's log and every site's console log of a failed gate", () => {
    const local = mkdtempSync(join(tmpdir(), 'jfb-gate-results-'));
    folders.push(local);
    const put = (path, text = 'x') => {
      mkdirSync(join(local, path, '..'), { recursive: true });
      writeFileSync(join(local, path), text);
    };
    put('e2e/results/trace-a/trace.zip');
    put('e2e/host-5520/host.log');
    put('e2e/site-2/results/trace-b/trace.zip');
    put('e2e/site-2/host-9002/host.log');
    put('e2e/budgets/host-5520/host.log');
    put('e2e/look/not-a-site.png');
    put('e2e-gate/site-2-of-2.log');
    put('e2e-gate/site-2-of-2.json');
    const target = join(local, 'copied');
    copyBrowserResults(local, target);
    assert.ok(existsSync(join(target, 'trace-a', 'trace.zip')), "the one site's results at the top");
    assert.ok(existsSync(join(target, 'site-2', 'trace-b', 'trace.zip')), "another site's under its name");
    assert.deepEqual(readdirSync(join(target, 'hosts')).sort(), ['budgets-host-5520.log', 'host-5520.log', 'site-2-host-9002.log']);
    assert.deepEqual(readdirSync(join(target, 'sites')), ['site-2-of-2.log']);
  });
});
