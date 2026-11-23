// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// Unit tests for scripts/test-integration.mjs. Run: node --test "scripts/tests/*.test.mjs"
import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';
import {
  TIMED_CATEGORY,
  TIMES_FILE,
  classTimes,
  compareNames,
  defaultShares,
  digestOf,
  findResultFiles,
  formatTimes,
  judge,
  listedTests,
  mergeSettings,
  parseArgs,
  parseShare,
  outputLines,
  parseTrx,
  planShares,
  readTimes,
  resultFile,
  shareEnv,
  sumResults,
  summaryLines,
  testArguments,
  unknownClasses,
  whereFor,
  whereTimed,
} from '../test-integration.mjs';

const repo = join(import.meta.dirname, '..', '..');
const folders = [];
after(() => {
  for (const dir of folders) rmSync(dir, { recursive: true, force: true });
});

/** Which share would run a class, read from the where-clauses the way NUnit reads them. */
function sharesRunning(plan, name) {
  const running = [];
  plan.forEach((_, index) => {
    const where = whereFor(plan, index);
    if (where === null) return;
    const named = [...where.matchAll(/class (==|!=) "([^"]+)"/g)];
    const equals = named.filter((m) => m[1] === '==').map((m) => m[2]);
    const excluded = named.filter((m) => m[1] === '!=').map((m) => m[2]);
    const runs = equals.length > 0 ? equals.includes(name) : !excluded.includes(name);
    if (runs) running.push(index);
  });
  return running;
}

const times = { 'N.A': 50, 'N.B': 40, 'N.C': 30, 'N.D': 20, 'N.E': 10, 'N.F': 5 };

describe('the plan', () => {
  test('shares the classes out by weight, heaviest first, each to the lightest share', () => {
    const plan = planShares(times, 3);
    assert.deepEqual(
      plan.map((share) => share.classes),
      [['N.A', 'N.F'], ['N.B', 'N.E'], ['N.C', 'N.D']],
    );
    assert.deepEqual(plan.map((share) => share.seconds), [55, 50, 50]);
  });

  test('is the same whatever order the file lists the classes in, so every CI job agrees', () => {
    const reversed = Object.fromEntries(Object.entries(times).reverse());
    assert.deepEqual(planShares(reversed, 4), planShares(times, 4));
    assert.deepEqual(planShares({ 'N.Y': 1, 'N.X': 1 }, 2), planShares({ 'N.X': 1, 'N.Y': 1 }, 2));
  });

  test('a class no share names runs in the last share, and only there', () => {
    const plan = planShares(times, 3);
    assert.deepEqual(sharesRunning(plan, 'N.New'), [2]);
  });

  test('every class the file knows runs in exactly one share, for any number of shares', () => {
    for (let count = 1; count <= 8; count++) {
      const plan = planShares(times, count);
      for (const name of Object.keys(times)) assert.equal(sharesRunning(plan, name).length, 1, `${name} in ${count} share(s)`);
      const named = plan.flatMap((share) => share.classes);
      assert.equal(new Set(named).size, named.length, 'no class is in two shares');
    }
  });

  test('a share before the last with no class has no clause and runs nothing', () => {
    const plan = planShares({ 'N.A': 1 }, 3);
    assert.equal(whereFor(plan, 1), null);
    assert.deepEqual(sharesRunning(plan, 'N.A'), [0]);
    assert.deepEqual(sharesRunning(plan, 'N.Other'), [2]);
  });

  test('without a times file every class runs in the last share', () => {
    const plan = planShares({}, 4);
    assert.equal(whereFor(plan, 3), `cat != ${TIMED_CATEGORY}`);
    assert.deepEqual([0, 1, 2].map((index) => whereFor(plan, index)), [null, null, null]);
  });
});

describe('the where-clauses', () => {
  const plan = planShares({ 'N.A': 3, 'N.B': 2, 'N.C': 1 }, 3);

  test('a share runs its own classes, without the timed tests', () => {
    assert.equal(whereFor(plan, 0), 'cat != Timed and (class == "N.A")');
    assert.equal(whereFor(planShares({ 'N.A': 3, 'N.B': 2, 'N.C': 1 }, 2), 0), 'cat != Timed and (class == "N.A")');
    assert.equal(whereFor(planShares({ 'N.A': 3, 'N.B': 2, 'N.C': 2 }, 3), 1), 'cat != Timed and (class == "N.B")');
  });

  test('the last share runs every class the others do not name, without the timed tests', () => {
    assert.equal(whereFor(plan, 2), 'cat != Timed and class != "N.A" and class != "N.B"');
    const two = planShares({ 'N.A': 1, 'N.B': 1, 'N.C': 1, 'N.D': 1 }, 2);
    assert.equal(whereFor(two, 0), 'cat != Timed and (class == "N.A" or class == "N.C")');
    assert.equal(whereFor(two, 1), 'cat != Timed and class != "N.A" and class != "N.C"');
  });

  test('the timed run runs only the timed tests', () => {
    assert.equal(whereTimed(), 'cat == Timed');
  });

  test('a class name a quoted value cannot hold is refused, not run wrongly', () => {
    assert.throws(() => whereFor(planShares({ 'N."A': 1 }, 2), 0), /cannot quote/);
  });

  test('the integration tests still mark their timed tests with this category', () => {
    const found = [];
    const walk = (folder) => {
      for (const name of readdirSync(folder)) {
        const path = join(folder, name);
        if (statSync(path).isDirectory()) {
          if (!['bin', 'obj', 'TEMP', 'TestResults'].includes(name)) walk(path);
        } else if (name.endsWith('.cs') && readFileSync(path, 'utf8').includes(`[Category("${TIMED_CATEGORY}")]`)) found.push(name);
      }
    };
    walk(join(repo, 'tests', 'Jiron.Umbraco.FormBuilder.Tests.Integration'));
    assert.ok(found.length >= 3, `found ${found.join(', ')}`);
  });
});

describe('the run settings', () => {
  const where = 'cat != Timed and (class == "N.A")';

  test('without a base file: run settings with only the NUnit Where', () => {
    const xml = mergeSettings(null, where);
    assert.match(xml, /^<\?xml version="1\.0" encoding="utf-8"\?>\n<RunSettings>/);
    assert.match(xml, /<NUnit>\s*<Where>cat != Timed and \(class == "N\.A"\)<\/Where>\s*<\/NUnit>\s*<\/RunSettings>\n$/);
  });

  test('the base file stays as it is, with the NUnit Where put in before its end', () => {
    const base = readFileSync(join(repo, 'tests', 'coverage.runsettings'), 'utf8');
    const xml = mergeSettings(base, where);
    const end = base.lastIndexOf('</RunSettings>');
    assert.ok(xml.startsWith(base.slice(0, end)), 'everything of the base file is kept');
    assert.match(xml.slice(end), /^ {2}<NUnit>\n {4}<Where>cat != Timed and \(class == "N\.A"\)<\/Where>\n {2}<\/NUnit>\n<\/RunSettings>/);
    assert.match(xml, /<DataCollector friendlyName="Code Coverage">/);
  });

  test('an NUnit section of the base keeps its settings and gets the Where', () => {
    const xml = mergeSettings('<RunSettings><NUnit><NumberOfTestWorkers>1</NumberOfTestWorkers></NUnit></RunSettings>', where);
    assert.match(xml, /<NUnit>\s*<Where>cat != Timed and \(class == "N\.A"\)<\/Where><NumberOfTestWorkers>1<\/NumberOfTestWorkers><\/NUnit>/);
    assert.match(mergeSettings('<RunSettings><NUnit /></RunSettings>', where), /<NUnit>\s*<Where>cat != Timed/);
  });

  test("a Where the base already has stays, joined with the share's by and", () => {
    const xml = mergeSettings('<RunSettings><NUnit><Where>cat != Slow</Where></NUnit></RunSettings>', where);
    assert.match(xml, /<Where>\(cat != Slow\) and \(cat != Timed and \(class == "N\.A"\)\)<\/Where>/);
    assert.equal(xml.match(/<Where>/g).length, 1);
  });

  test('characters XML gives a meaning are escaped, and a base without its end is refused', () => {
    assert.match(mergeSettings(null, 'test =~ /a<b&c/'), /<Where>test =~ \/a&lt;b&amp;c\/<\/Where>/);
    assert.throws(() => mergeSettings('<NotRunSettings />', where), /no <\/RunSettings>/);
  });
});

const trx = (rows, extra = '') => `<?xml version="1.0" encoding="utf-8"?>
<TestRun xmlns="http://microsoft.com/schemas/VisualStudio/TeamTest/2010">
  <Results>
${rows
  .map(
    (row, i) =>
      `    <UnitTestResult executionId="e${i}" testId="t${i}" testName="${row.name}" duration="${row.duration ?? '00:00:01.5000000'}" outcome="${row.outcome ?? 'Passed'}">${row.body ?? ''}</UnitTestResult>`,
  )
  .join('\n')}
  </Results>
  <TestDefinitions>
${rows.map((row, i) => `    <UnitTest name="${row.name}" storage="x.dll" id="t${i}"><Execution id="e${i}" /><TestMethod codeBase="x.dll" adapterTypeName="executor://NUnit3TestExecutor" className="${row.className}" name="${row.name}" /></UnitTest>`).join('\n')}
  </TestDefinitions>${extra}
</TestRun>`;

describe('reading results', () => {
  test('a trx file gives each result its name, class, outcome, seconds and message', () => {
    const results = parseTrx(
      trx([
        { name: 'A_Works', className: 'N.A' },
        { name: 'B(&quot;x&quot;)', className: 'N.B', duration: '00:01:02.2500000', outcome: 'Failed', body: '<Output><ErrorInfo><Message>Expected 1&#xD;\nbut was 2</Message></ErrorInfo></Output>' },
        { name: 'C_Skips', className: 'N.C', outcome: 'NotExecuted', body: '<Output><StdOut>Only on Windows.</StdOut></Output>' },
      ]),
    );
    assert.deepEqual(results, [
      { name: 'A_Works', className: 'N.A', outcome: 'Passed', seconds: 1.5, message: '' },
      { name: 'B("x")', className: 'N.B', outcome: 'Failed', seconds: 62.25, message: 'Expected 1' },
      { name: 'C_Skips', className: 'N.C', outcome: 'NotExecuted', seconds: 1.5, message: 'Only on Windows.' },
    ]);
  });

  test('what tests measured is given per test, a line each, without what their host logged', () => {
    const lines = outputLines(
      trx([
        { name: 'A_Works', className: 'N.A' },
        { name: 'B_Measures', className: 'N.B', body: '<Output><StdOut>every entry: median 3950.0 ms (budget 8000 ms)&#xD;\nwarn: Umbraco.Cms[0]&#xD;\n&#xD;\nfiltered: median 1.5 ms &lt; 2 ms\nPassed</StdOut></Output>' },
        { name: 'C_Fails', className: 'N.C', outcome: 'Failed', body: '<Output><ErrorInfo><Message>no</Message></ErrorInfo></Output>' },
      ]),
    );
    assert.deepEqual(lines, ['B_Measures: every entry: median 3950.0 ms (budget 8000 ms)', 'B_Measures: filtered: median 1.5 ms < 2 ms']);
  });

  test("the assembly's tests are the indented lines after the list's heading", () => {
    const output = 'VSTest version 18.7.0 (x64)\r\n\r\nThe following Tests are available:\r\n    A_Works\r\n    B("x",["y", "z"])\r\n    A_Works\r\n';
    assert.deepEqual(listedTests(output), ['A_Works', 'B("x",["y", "z"])', 'A_Works']);
    assert.equal(listedTests('No test is available in x.dll.'), null);
  });

  test('the seconds per class, and the classes the times file does not know', () => {
    const results = [
      { name: 'a', className: 'N.A', seconds: 1 },
      { name: 'b', className: 'N.A', seconds: 2.5 },
      { name: 'c', className: 'N.New', seconds: 4 },
    ];
    assert.deepEqual(classTimes(results), { 'N.A': 3.5, 'N.New': 4 });
    assert.deepEqual(unknownClasses(results, { 'N.A': 1 }), ['N.New']);
  });

  test('the times file: sorted by class, to a tenth of a second, and read back', () => {
    const text = formatTimes({ 'N.B': 2.04, 'N.A': 0.01 }, 8);
    assert.deepEqual(readTimes(text), { 'N.A': 0.1, 'N.B': 2 });
    assert.deepEqual(Object.keys(JSON.parse(text).classes), ['N.A', 'N.B']);
    assert.deepEqual(readTimes(null), {});
    assert.throws(() => readTimes('{"classes":{"N.A":"slow"}}'), /no number of seconds/);
  });

  test('the tracked times file reads, and plans eight even shares', () => {
    const tracked = readTimes(readFileSync(join(repo, TIMES_FILE), 'utf8'));
    assert.ok(Object.keys(tracked).length > 100);
    const plan = planShares(tracked, 8);
    const seconds = plan.map((share) => share.seconds);
    assert.ok(Math.max(...seconds) / Math.min(...seconds) < 1.2, seconds.join(', '));
  });
});

describe('the verdict of a run', () => {
  const holds = ['a', 'b', 'c', 'd'];
  const passed = (name, className = 'N.X') => ({ name, className, outcome: 'Passed', seconds: 1, message: '' });
  const share = (label, names, exit = 0) => ({ label, exit, timedOut: false, results: names.map((name) => passed(name)) });

  test('every test once and every process passed: the run passes', () => {
    const verdict = judge(holds, [share('share-1-of-2', ['a', 'b']), share('share-2-of-2', ['c']), share('timed', ['d'])]);
    assert.deepEqual(verdict, { ok: true, problems: [], ran: 4 });
  });

  test('a short count fails, and names the test that never ran', () => {
    const verdict = judge(holds, [share('share-1-of-2', ['a', 'b']), share('share-2-of-2', ['c']), share('timed', [])]);
    assert.equal(verdict.ok, false);
    assert.deepEqual(verdict.problems, ['3 tests reported, but the assembly holds 4', 'never ran: d']);
  });

  test('a test that ran twice fails even when the count adds up', () => {
    const verdict = judge(holds, [share('share-1-of-2', ['a', 'b']), share('share-2-of-2', ['c', 'c']), share('timed', [])]);
    assert.equal(verdict.ok, false);
    assert.deepEqual(verdict.problems, ['4 tests reported, but the assembly holds 4', 'never ran: d', 'ran more often than the assembly holds it: c']);
  });

  test('a failed share fails, naming the share and its failed tests', () => {
    const failing = share('share-2-of-2', ['c']);
    failing.exit = 1;
    failing.results = [{ name: 'c', className: 'N.C', outcome: 'Failed', seconds: 1, message: 'Expected 1 but was 2' }];
    const verdict = judge(holds, [share('share-1-of-2', ['a', 'b']), failing, share('timed', ['d'])]);
    assert.equal(verdict.ok, false);
    assert.deepEqual(verdict.problems, ['share-2-of-2 failed (exit 1)', 'share-2-of-2: failed N.C.c: Expected 1 but was 2']);
  });

  test('a process that failed without a failed test (a crash, a hang) fails too, and so does one stopped for its time', () => {
    const crashed = judge(holds, [share('share-1-of-2', ['a', 'b'], 1), share('share-2-of-2', ['c']), share('timed', ['d'])]);
    assert.deepEqual(crashed.problems, ['share-1-of-2 failed (exit 1)']);
    const stopped = share('share-2-of-2', ['c']);
    stopped.timedOut = true;
    stopped.exit = 1;
    assert.deepEqual(judge(holds, [share('share-1-of-2', ['a', 'b']), stopped, share('timed', ['d'])]).problems, ['share-2-of-2 ran past its time and was stopped']);
  });

  test('a test skipped while it ran counts as reached and fails nothing', () => {
    const skipping = share('timed', []);
    skipping.results = [{ name: 'd', className: 'N.D', outcome: 'NotExecuted', seconds: 0, message: 'Only on Windows.' }];
    assert.equal(judge(holds, [share('share-1-of-2', ['a', 'b']), share('share-2-of-2', ['c']), skipping]).ok, true);
  });

  test('compareNames counts each name as often as it is held', () => {
    assert.deepEqual(compareNames(['x', 'x', 'y'], ['x', 'y', 'y']), { missing: ['x'], extra: ['y'] });
  });
});

describe('the sum of a CI matrix', () => {
  const holds = ['a', 'b', 'c', 'd'];
  const file = (share, names, exit = 0) =>
    resultFile({ share, exit, timedOut: false, seconds: 10, holds, results: names.map((name) => ({ name, className: 'N.X', outcome: 'Passed', seconds: 1, message: '' })) });
  const all = () => [file('1/2', ['a', 'b']), file('2/2', ['c']), file('timed', ['d'])];

  test('every share and the timed run, together the assembly: it passes', () => {
    assert.deepEqual(sumResults(all()), { ok: true, problems: [], ran: 4, holds: 4 });
  });

  test('a share that did not report fails, and so does a missing timed run', () => {
    const [, two, timed] = all();
    assert.deepEqual(sumResults([two, timed]).problems, ['share 1/2 did not report', '2 tests reported, but the assembly holds 4']);
    assert.deepEqual(sumResults(all().slice(0, 2)).problems, ['the timed run did not report', '3 tests reported, but the assembly holds 4']);
    assert.deepEqual(sumResults([]).problems, ['no result files']);
  });

  test('a short count fails even when every share reported', () => {
    const files = [file('1/2', ['a', 'b']), file('2/2', ['c']), file('timed', [])];
    assert.deepEqual(sumResults(files).problems, ['3 tests reported, but the assembly holds 4']);
  });

  test('a failed share fails', () => {
    const files = all();
    files[1] = resultFile({ share: '2/2', exit: 1, timedOut: false, seconds: 10, holds, results: [{ name: 'c', className: 'N.C', outcome: 'Failed', seconds: 1, message: '' }] });
    assert.deepEqual(sumResults(files).problems, ['share 2/2 failed (exit 1)', 'share 2/2: failed N.C.c']);
  });

  test('shares of another assembly, of another count, or reported twice fail', () => {
    const other = resultFile({ share: 'timed', exit: 0, timedOut: false, seconds: 1, holds: ['a', 'b', 'c', 'e'], results: [{ name: 'd', className: 'N.X', outcome: 'Passed', seconds: 1, message: '' }] });
    assert.match(sumResults([...all().slice(0, 2), other]).problems[0], /share timed ran another assembly/);
    assert.ok(sumResults([...all(), file('3/3', [])]).problems.includes('the shares do not agree on their number: 2, 3'));
    assert.ok(sumResults([...all(), file('2/2', ['c'])]).problems.includes('share 2/2 reported 2 times'));
  });

  test('a test two shares both ran fails, named', () => {
    const files = [file('1/2', ['a', 'b', 'c']), file('2/2', ['c']), file('timed', ['d'])];
    assert.deepEqual(sumResults(files).problems, ['5 tests reported, but the assembly holds 4', 'reported more than once: c']);
  });

  test('the result file holds the digest of the assembly and the names it ran, and the files are found below a folder', () => {
    const result = file('1/2', ['b', 'a']);
    assert.deepEqual(result.holds, { count: 4, digest: digestOf(['d', 'c', 'b', 'a']) });
    assert.deepEqual(result.ran, ['a', 'b']);
    const dir = mkdtempSync(join(tmpdir(), 'jfb-shares-'));
    folders.push(dir);
    mkdirSync(join(dir, 'integration-sqlite-1', 'share-1-of-2'), { recursive: true });
    mkdirSync(join(dir, 'integration-sqlite-timed', 'timed'), { recursive: true });
    writeFileSync(join(dir, 'integration-sqlite-1', 'share-1-of-2', 'result.json'), '{}');
    writeFileSync(join(dir, 'integration-sqlite-timed', 'timed', 'result.json'), '{}');
    writeFileSync(join(dir, 'integration-sqlite-timed', 'timed', 'timed.trx'), '');
    assert.equal(findResultFiles(dir).length, 2);
  });
});

describe('the command line', () => {
  test('defaults: half the cores in shares, at most eight, the Release assembly, results under .local', () => {
    const options = parseArgs([], 16);
    assert.equal(options.mode, 'run');
    assert.equal(options.shares, 8);
    assert.match(options.assembly, /Tests\.Integration\/bin\/Release\/net10\.0\/Jiron\.Umbraco\.FormBuilder\.Tests\.Integration\.dll$/);
    assert.equal(options.results, '.local/test-integration');
    assert.equal(options.shareTimeout, 60);
    assert.deepEqual([defaultShares(1), defaultShares(4), defaultShares(7), defaultShares(64)], [1, 2, 3, 8]);
  });

  test('every option is read, and what follows -- passes through as it is', () => {
    const options = parseArgs(['--shares', '3', '--record', '--settings', 's.runsettings', '--results', 'out', '--beside', 'u.dll', '--beside', 'a.dll', '--share-timeout', '5', '--', '--logger', 'console;verbosity=normal'], 16);
    assert.equal(options.shares, 3);
    assert.equal(options.record, true);
    assert.equal(options.settings, 's.runsettings');
    assert.equal(options.results, 'out');
    assert.deepEqual(options.beside, ['u.dll', 'a.dll']);
    assert.equal(options.shareTimeout, 5);
    assert.deepEqual(options.passThrough, ['--logger', 'console;verbosity=normal']);
  });

  test('one share for CI: i of n, 1-based, or the timed tests', () => {
    assert.deepEqual(parseArgs(['--share', '2/6']).share, { index: 1, count: 6 });
    assert.deepEqual(parseArgs(['--share', 'timed']).share, { timed: true });
    assert.deepEqual([parseShare('0/6'), parseShare('7/6'), parseShare('x')], [null, null, null]);
    assert.equal(parseArgs(['--sum', 'shares']).mode, 'sum');
    assert.equal(parseArgs(['--stop']).mode, 'stop');
  });

  test('what does not fit together is refused', () => {
    assert.match(parseArgs(['--share', '1/2', '--shares', '2']).error, /leave out --shares/);
    assert.match(parseArgs(['--share', '1/2', '--record']).error, /whole run/);
    assert.match(parseArgs(['--share', '1/2', '--beside', 'u.dll']).error, /whole run/);
    assert.match(parseArgs(['--sum', 'x', '--record']).error, /takes no run options/);
    assert.match(parseArgs(['--shares', '0']).error, /1 or more/);
    assert.match(parseArgs(['--share', '3/2']).error, /i\/n/);
    assert.match(parseArgs(['--share-timeout', '-1']).error, /minutes/);
    assert.match(parseArgs(['--fresh']).error, /unknown argument --fresh/);
    assert.match(parseArgs(['--settings']).error, /needs a value/);
  });
});

describe('the summary', () => {
  const rows = [
    { label: 'share-1-of-2', tests: 3, failed: 0, skipped: 0, seconds: 100.4, planned: 98, exit: 0, timedOut: false },
    { label: 'timed', tests: 1, failed: 1, skipped: 0, seconds: 50, exit: 1, timedOut: false },
  ];

  test('a row per process with its tests, failures and time, then the total against the assembly', () => {
    const lines = summaryLines(rows, { ok: true, problems: [], ran: 4 }, 4, 151.2);
    assert.match(lines[2], /^test-integration: {3}share-1-of-2 +3 +0 +0 +100 +98$/);
    assert.match(lines[3], /^test-integration: {3}timed +1 +1 +0 +50 +- +exit 1$/);
    assert.equal(lines.at(-1), 'test-integration: 4 of 4 tests ran, none failed, in 151 s');
  });

  test('a failed run lists its problems, then says FAILED', () => {
    const lines = summaryLines(rows, { ok: false, problems: ['timed failed (exit 1)'], ran: 4 }, 4, 151);
    assert.deepEqual(lines.slice(-2), ['test-integration: timed failed (exit 1)', 'test-integration: FAILED: 4 of 4 tests ran, in 151 s']);
  });

  test('one share of a CI matrix leaves the count to the sum', () => {
    const lines = summaryLines(rows.slice(0, 1), { ok: true, problems: [], ran: 3 }, 4, 100, '1/2');
    assert.equal(lines.at(-1), 'test-integration: share 1/2: 3 tests ran (the assembly holds 4; --sum adds the shares up), none failed, in 100 s');
  });
});

describe('the workflow', () => {
  test("each database's matrix holds shares 1 to JFB_SHARES and the timed run, and the last job adds them up", async () => {
    const { parse } = await import('yaml');
    const ci = parse(readFileSync(join(repo, '.github', 'workflows', 'ci.yml'), 'utf8'));
    for (const id of ['sqlite', 'sqlserver']) {
      const job = ci.jobs[id];
      const count = Number(job.env.JFB_SHARES);
      assert.ok(count >= 2, `${id} has shares`);
      assert.deepEqual(job.strategy.matrix.share, [...Array.from({ length: count }, (_, i) => String(i + 1)), 'timed'], id);
      assert.equal(job.strategy['fail-fast'], false, `${id} lets every share report`);
      const run = job.steps.map((step) => step.run ?? '').join('\n');
      assert.match(run, /node scripts\/test-integration\.mjs --share "\$share" --results TestResults\/integration/);
      const upload = job.steps.find((step) => String(step.uses).startsWith('actions/upload-artifact@'));
      assert.equal(upload.with.name, `integration-${id}-\${{ matrix.share }}`);
      assert.equal(upload.with.path, 'TestResults/integration');
      assert.equal(job['timeout-minutes'], 45);
    }
    const shares = ci.jobs.shares;
    assert.deepEqual(shares.needs, ['sqlite', 'sqlserver']);
    const sums = shares.steps.map((step) => step.run ?? '').join('\n');
    assert.match(sums, /--sum shares\/sqlite/);
    assert.match(sums, /--sum shares\/sqlserver/);
    assert.doesNotMatch(ci.jobs.build.steps.map((step) => step.run ?? '').join('\n'), /Tests\.Integration/, 'the build job leaves them to the shares');
  });
});

describe('one process', () => {
  test('dotnet test on the assembly with its settings, a trx of its own, its results folder and the hang timeout', () => {
    const args = testArguments({ assembly: 'x.dll', settings: 's.runsettings', label: 'share-1-of-8', results: 'out/share-1-of-8', passThrough: ['--diag', 'd.log'] });
    assert.deepEqual(args.slice(0, 4), ['test', 'x.dll', '--settings', 's.runsettings']);
    assert.equal(args[args.indexOf('--logger') + 1], 'trx;LogFileName=share-1-of-8.trx');
    assert.equal(args[args.indexOf('--results-directory') + 1], 'out/share-1-of-8');
    assert.equal(args[args.indexOf('--blame-hang-timeout') + 1], '10m');
    assert.equal(args[args.indexOf('--blame-hang-dump-type') + 1], 'none');
    assert.deepEqual(args.slice(-2), ['--diag', 'd.log']);
    assert.equal(testArguments({ assembly: 'x.dll', settings: null, label: 'l', results: 'r' }).includes('--settings'), false);
  });

  test("a share's environment: its own temporary folder, whatever the spelling of the names it replaces", () => {
    const env = shareEnv({ Path: 'p', Tmp: 'C:\\old', TEMP: 'C:\\old', TMPDIR: '/old' }, 'C:\\new');
    assert.deepEqual(env, { Path: 'p', System_DefaultWorkingDirectory: '1', TMP: 'C:\\new', TEMP: 'C:\\new', TMPDIR: 'C:\\new' });
  });
});
