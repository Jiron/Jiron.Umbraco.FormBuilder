// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// Unit tests for scripts/coverage.mjs. Run: node --test "scripts/tests/*.test.mjs"
import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';
import { check, findReports, merge, parseArguments, summarise, testArguments } from '../coverage.mjs';

const folders = [];
after(() => {
  for (const dir of folders) rmSync(dir, { recursive: true, force: true });
});

const unit = `<?xml version="1.0" encoding="utf-8"?>
<coverage>
  <packages>
    <package name="Jiron.Umbraco.FormBuilder.Core" line-rate="0.5">
      <classes>
        <class name="A" filename="src/A.cs">
          <lines>
            <line number="1" hits="1" branch="False" />
            <line number="2" hits="0" branch="False" />
          </lines>
        </class>
        <class name="A/&lt;RunAsync&gt;d__1" filename="src/A.cs">
          <lines>
            <line number="2" hits="0" branch="False" />
            <line number="3" hits="0" branch="False" />
          </lines>
        </class>
      </classes>
    </package>
  </packages>
</coverage>`;

const integration = `<coverage><packages>
  <package name="Jiron.Umbraco.FormBuilder.Core"><classes>
    <class name="A" filename="src/A.cs"><lines><line number="3" hits="4" /></lines></class>
    <class name="B" filename="src/B.cs"><lines><line number="1" hits="0" /></lines></class>
  </classes></package>
  <package name="Jiron.Umbraco.FormBuilder.Web"><classes>
    <class name="W" filename="src/W.cs"><lines><line number="9" hits="2" /></lines></class>
  </classes></package>
</packages></coverage>`;

describe('merge and summarise', () => {
  test('a line is covered when any report hit it, and counts once', () => {
    assert.deepEqual(summarise(merge([unit, integration])), [
      { name: 'Jiron.Umbraco.FormBuilder.Core', covered: 2, total: 4, percent: 50 },
      { name: 'Jiron.Umbraco.FormBuilder.Web', covered: 1, total: 1, percent: 100 },
    ]);
  });

  test('one report alone', () => {
    assert.deepEqual(summarise(merge([unit])), [
      { name: 'Jiron.Umbraco.FormBuilder.Core', covered: 1, total: 3, percent: (1 * 100) / 3 },
    ]);
  });

  test('a package without lines is at 0%, not a division by zero', () => {
    const empty = '<coverage><packages><package name="Empty"><classes></classes></package></packages></coverage>';
    assert.deepEqual(summarise(merge([empty])), [{ name: 'Empty', covered: 0, total: 0, percent: 0 }]);
  });
});

describe('check', () => {
  const summary = summarise(merge([unit, integration]));

  test('a package at or above the floor passes', () => {
    assert.deepEqual(check(summary, ['Jiron.Umbraco.FormBuilder.Core'], 50), []);
    assert.deepEqual(check(summary, ['Jiron.Umbraco.FormBuilder.Web'], 100), []);
  });

  test('a package below the floor, or missing, is a problem', () => {
    assert.deepEqual(check(summary, ['Jiron.Umbraco.FormBuilder.Core', 'Jiron.Umbraco.FormBuilder.Logic'], 85), [
      'Jiron.Umbraco.FormBuilder.Core is at 50.0%, below 85%',
      'Jiron.Umbraco.FormBuilder.Logic is not in the reports',
    ]);
  });
});

describe('parseArguments', () => {
  test('defaults: the folder under .local, 85% and Core, without running the tests', () => {
    assert.deepEqual(parseArguments([]), {
      dir: '.local/coverage',
      min: 85,
      packages: ['Jiron.Umbraco.FormBuilder.Core'],
      run: false,
    });
  });

  test('named packages replace Core, and every option is read', () => {
    assert.deepEqual(parseArguments(['--run', '--dir', 'x', '--min', '90', '--package', 'A', '--package', 'B']), {
      dir: 'x',
      min: 90,
      packages: ['A', 'B'],
      run: true,
    });
  });

  test('an unknown argument, a missing value or a floor that is no percentage is refused', () => {
    assert.throws(() => parseArguments(['--fresh']), /Unknown argument --fresh/);
    assert.throws(() => parseArguments(['--min']), /--min needs a value/);
    assert.throws(() => parseArguments(['--min', 'high']), /percentage/);
    assert.throws(() => parseArguments(['--min', '101']), /percentage/);
  });
});

describe('the test run', () => {
  test('runs the solution in Release with the run settings into the folder, and names a hanging test', () => {
    const args = testArguments('C:/out');
    assert.equal(args[0], 'test');
    assert.match(args[1], /Jiron\.Umbraco\.FormBuilder\.slnx$/);
    assert.deepEqual(args.slice(2, 5), ['-c', 'Release', '-nologo']);
    assert.match(args[args.indexOf('--settings') + 1], /tests[\\/]coverage\.runsettings$/);
    assert.equal(args[args.indexOf('--results-directory') + 1], 'C:/out');
    assert.equal(args[args.indexOf('--blame-hang-timeout') + 1], '10m');
  });
});

test('findReports finds every Cobertura report under the folder, as each collector names it, and nothing else', () => {
  const dir = mkdtempSync(join(tmpdir(), 'jfb-coverage-'));
  folders.push(dir);
  mkdirSync(join(dir, 'a'), { recursive: true });
  mkdirSync(join(dir, 'b', 'c'), { recursive: true });
  writeFileSync(join(dir, 'a', 'Server_HOST_2026-10-27.22_56_16.cobertura.xml'), unit);
  writeFileSync(join(dir, 'b', 'c', 'coverage.cobertura.xml'), integration);
  writeFileSync(join(dir, 'b', 'coverage.json'), '{}');
  writeFileSync(join(dir, 'b', 'cobertura.xml.txt'), unit);

  assert.deepEqual(
    findReports(dir).map((path) => path.slice(dir.length + 1).replaceAll('\\', '/')).sort(),
    ['a/Server_HOST_2026-10-27.22_56_16.cobertura.xml', 'b/c/coverage.cobertura.xml'],
  );
});
