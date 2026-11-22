// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
// Unit tests for scripts/e2e-gate.mjs. Run: node --test "scripts/tests/*.test.mjs"
import assert from 'node:assert/strict';
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { after, describe, test } from 'node:test';
import {
  BUDGETS,
  DEFAULT_SITES,
  checkPlan,
  compareIds,
  hangMarkerName,
  hangMarkers,
  judge,
  parseArgs,
  parseSite,
  plannedSeconds,
  projectsOf,
  recorderArguments,
  rerunCommand,
  rerunLines,
  runWithRerun,
  SEVERAL_SITES_WORKERS,
  siteArguments,
  siteEnv,
  siteFileLabel,
  specSeconds,
  summaryLines,
  testsOfReport,
  withoutProjects,
  workerArguments,
  workersPerSite,
} from '../e2e-gate.mjs';
import { siteState } from '../../tests/e2e/support/sites.ts';

/** A gate run with fake runs: each entry is the exit code of one run and the markers it leaves. */
function simulate(runs, stale = []) {
  let present = [...stale];
  const calls = { runs: 0, cleared: [], log: [] };
  const code = runWithRerun({
    run: () => {
      const { exit, leaves = [] } = runs[calls.runs++];
      present.push(...leaves);
      return exit;
    },
    markers: () => [...present],
    clear: (files) => {
      calls.cleared.push([...files]);
      present = present.filter((file) => !files.includes(file));
    },
    read: () => 'from 06:00:24, 2 command(s) timed out\n',
    log: (line) => calls.log.push(line),
  });
  return { code, ...calls };
}

describe('runWithRerun', () => {
  test('a passing run is the only run', () => {
    const result = simulate([{ exit: 0 }]);
    assert.equal(result.code, 0);
    assert.equal(result.runs, 1);
    assert.deepEqual(result.log, []);
  });

  test('a failure without the hang marker fails at once ("Flaky means broken")', () => {
    const result = simulate([{ exit: 1 }, { exit: 0 }]);
    assert.equal(result.code, 1);
    assert.equal(result.runs, 1);
  });

  test('a failure with the hang marker runs once more, and says why', () => {
    const passed = simulate([{ exit: 1, leaves: ['host-5520/database-hung.txt'] }, { exit: 0 }]);
    assert.equal(passed.code, 0);
    assert.equal(passed.runs, 2);
    assert.match(passed.log[0], /a host's database hung \(from 06:00:24, 2 command\(s\) timed out; host-5520\/database-hung\.txt\)/);
    assert.match(passed.log.at(-1), /the rerun passed/);
  });

  test('runs at most twice: a second hang fails the gate', () => {
    const marker = 'host-5520/database-hung.txt';
    const result = simulate([{ exit: 1, leaves: [marker] }, { exit: 1, leaves: [marker] }, { exit: 0 }]);
    assert.equal(result.code, 1);
    assert.equal(result.runs, 2);
    assert.match(result.log.at(-1), /the rerun failed too/);
  });

  test('a marker left by an earlier run is removed first and causes no rerun', () => {
    const result = simulate([{ exit: 1 }, { exit: 0 }], ['host-5520/database-hung.txt']);
    assert.deepEqual(result.cleared[0], ['host-5520/database-hung.txt']);
    assert.equal(result.code, 1);
    assert.equal(result.runs, 1);
  });
});

describe('the markers', () => {
  const dir = mkdtempSync(join(tmpdir(), 'jfb-e2e-gate-'));
  after(() => rmSync(dir, { recursive: true, force: true }));

  test("are found in every host's folder, and nowhere else", () => {
    for (const host of ['host-5520', 'host-5522', 'host-5524']) mkdirSync(join(dir, host));
    writeFileSync(join(dir, 'host-5520', 'database-hung.txt'), 'x');
    writeFileSync(join(dir, 'host-5524', 'database-hung.txt'), 'x');
    writeFileSync(join(dir, 'database-hung.txt'), 'not a host folder');
    assert.deepEqual(hangMarkers(dir, 'database-hung.txt').sort(), [join(dir, 'host-5520', 'database-hung.txt'), join(dir, 'host-5524', 'database-hung.txt')]);
    assert.deepEqual(hangMarkers(join(dir, 'missing'), 'database-hung.txt'), []);
  });

  test("take their name from the harness's constant", () => {
    assert.equal(hangMarkerName(), 'database-hung.txt');
    assert.equal(hangMarkerName("export const HANG_MARKER = 'other.txt';"), 'other.txt');
    assert.throws(() => hangMarkerName('nothing here'), /no longer exports HANG_MARKER/);
  });

  test("of one site are found in that site's hosts only, so only the site whose database hung runs again", () => {
    const repo = join(dir, 'repo');
    const two = { index: 2, count: 3, budgets: false };
    const one = { index: 1, count: 3, budgets: false };
    mkdirSync(join(siteState(repo, two), 'host-9002'), { recursive: true });
    mkdirSync(join(siteState(repo, one), 'host-9001'), { recursive: true });
    writeFileSync(join(siteState(repo, two), 'host-9002', 'database-hung.txt'), 'x');
    assert.deepEqual(hangMarkers(siteState(repo, two), 'database-hung.txt'), [join(siteState(repo, two), 'host-9002', 'database-hung.txt')]);
    assert.deepEqual(hangMarkers(siteState(repo, one), 'database-hung.txt'), []);
    // The one site of a default run does not look into the other sites' folders below its own.
    assert.deepEqual(hangMarkers(siteState(repo, { index: 1, count: 1, budgets: false }), 'database-hung.txt'), []);
  });

  test('a site whose database hung runs once more while the others are left as they ended', () => {
    // Each site's process runs runWithRerun over its own markers; one hangs, one fails, one passes.
    const hung = simulate([{ exit: 1, leaves: ['site-2/host/database-hung.txt'] }, { exit: 0 }]);
    const failed = simulate([{ exit: 1 }, { exit: 0 }]);
    const passed = simulate([{ exit: 0 }]);
    assert.deepEqual([hung.runs, failed.runs, passed.runs], [2, 1, 1]);
    assert.deepEqual([hung.code, failed.code, passed.code], [0, 1, 0]);
  });
});

describe('parseArgs', () => {
  test('runs the default number of sites, then budgets, with nothing passed on', () => {
    const options = parseArgs([]);
    assert.equal(options.mode, 'run');
    assert.equal(options.sites, DEFAULT_SITES);
    assert.deepEqual(options.passThrough, []);
    assert.equal(options.record, false);
    assert.equal(options.plan, false);
  });

  test('takes the number of sites, --record, --plan, a timeout, and Playwright arguments after --', () => {
    const options = parseArgs(['--sites', '2', '--record', '--site-timeout', '30', '--', '--grep', 'x']);
    assert.equal(options.sites, 2);
    assert.equal(options.record, true);
    assert.equal(options.siteTimeout, 30);
    assert.deepEqual(options.passThrough, ['--grep', 'x']);
    assert.equal(parseArgs(['--plan']).plan, true);
  });

  test('runs one site, or budgets, in its own process', () => {
    assert.deepEqual(parseArgs(['--site', '2/3', '--', '--project=smoke']).site, { index: 2, count: 3, budgets: false });
    assert.deepEqual(parseArgs(['--site', 'budgets']).site, { index: 1, count: 1, budgets: true });
    assert.equal(parseArgs(['--stop']).mode, 'stop');
  });

  test('refuses what does not fit together, more sites than the ports allow, and unknown arguments', () => {
    assert.match(parseArgs(['--sites', '4']).error, /1 to 3/);
    assert.match(parseArgs(['--sites', '0']).error, /1 to 3/);
    assert.match(parseArgs(['--site', '4/3']).error, /i\/n/);
    assert.match(parseArgs(['--site', '1/4']).error, /i\/n/);
    assert.match(parseArgs(['--site', '1/2', '--sites', '2']).error, /one site/);
    assert.match(parseArgs(['--site', '1/2', '--record']).error, /one site/);
    assert.match(parseArgs(['--plan', '--record']).error, /nothing to record/);
    assert.match(parseArgs(['--stop', '--sites', '2']).error, /no run options/);
    assert.match(parseArgs(['--stop', '--trace']).error, /no run options/);
    assert.match(parseArgs(['--project=smoke']).error, /after --/);
    assert.match(parseArgs(['--sites']).error, /needs a value/);
  });

  test('takes the workers per site of a whole run, 1 to 16, and none for one site or a stop', () => {
    assert.equal(parseArgs([]).workers, null);
    assert.equal(parseArgs(['--workers', '4']).workers, 4);
    assert.equal(parseArgs(['--sites', '1', '--workers', '2']).workers, 2);
    for (const bad of ['0', '17', '2.5', 'x']) assert.match(parseArgs(['--workers', bad]).error, /1 to 16/, bad);
    assert.match(parseArgs(['--workers']).error, /needs a value/);
    assert.match(parseArgs(['--site', '1/2', '--workers', '3']).error, /one site/);
    assert.match(parseArgs(['--stop', '--workers', '3']).error, /no run options/);
  });

  test('records no trace unless --trace, a whole run or one site', () => {
    assert.equal(parseArgs([]).trace, false);
    assert.equal(parseArgs(['--trace']).trace, true);
    assert.equal(parseArgs(['--site', '2/3', '--trace', '--', '--project=smoke']).trace, true);
  });

  test('parseSite reads i/n and budgets only', () => {
    assert.deepEqual(parseSite('1/1'), { index: 1, count: 1, budgets: false });
    assert.equal(parseSite('0/2'), null);
    assert.equal(parseSite('x'), null);
  });
});

describe("a site's processes", () => {
  test('get the variables the harness reads, and none for the one site of a default run', () => {
    const base = { PATH: 'p', jfb_e2e_site: 'stale', JFB_E2E_SITES: '9' };
    assert.deepEqual(siteEnv(base, { index: 2, count: 3, budgets: false }), { PATH: 'p', JFB_E2E_SITES: '3', JFB_E2E_SITE: '2' });
    assert.deepEqual(siteEnv(base, { index: 1, count: 1, budgets: false }), { PATH: 'p' });
    assert.deepEqual(siteEnv(base, { index: 1, count: 1, budgets: true }), { PATH: 'p', JFB_E2E_SITE: 'budgets' });
  });

  test('run the projects in the config order without budgets; the budgets site runs budgets alone, without its dependencies', () => {
    const projects = ['smoke', 'website', 'backoffice', 'limits', 'site-wide', BUDGETS, 'startup'];
    const pass = ['--project=smoke', '-p', 'website', '--project', 'x', '--grep', 'y'];
    assert.deepEqual(withoutProjects(pass), ['--grep', 'y']);
    assert.deepEqual(siteArguments({ index: 1, count: 2, budgets: false }, projects, pass), [
      '--project=smoke',
      '--project=website',
      '--project=backoffice',
      '--project=limits',
      '--project=site-wide',
      '--project=startup',
      '--grep',
      'y',
    ]);
    assert.deepEqual(siteArguments({ index: 1, count: 1, budgets: true }, projects, []), ['--project=budgets', '--no-deps']);
    assert.equal(siteFileLabel({ index: 2, count: 3, budgets: false }), 'site-2-of-3');
    assert.equal(siteFileLabel({ index: 1, count: 1, budgets: true }), 'budgets');
  });

  test("run three workers each when there are several sites, the config's for one site, and what was asked for", () => {
    assert.equal(SEVERAL_SITES_WORKERS, 3);
    assert.equal(workersPerSite(null, 1), null);
    assert.equal(workersPerSite(null, 2), 3);
    assert.equal(workersPerSite(null, 3), 3);
    assert.equal(workersPerSite(4, 2), 4);
    assert.equal(workersPerSite(2, 1), 2);
    const site = { index: 1, count: 2, budgets: false };
    assert.deepEqual(workerArguments(3, site, ['--grep', 'x']), ['--workers=3']);
    assert.deepEqual(workerArguments(null, site, []), [], "the config's number");
    assert.deepEqual(workerArguments(3, { index: 1, count: 1, budgets: true }, []), [], 'budgets keeps its one worker');
    for (const chosen of [['--workers', '2'], ['--workers=2'], ['-j', '2'], ['-j2']]) assert.deepEqual(workerArguments(3, site, chosen), [], chosen.join(' '));
  });

  test('run without the trace recorder, unless the run records or the arguments after -- choose a mode', () => {
    assert.deepEqual(recorderArguments(false, ['--project=smoke']), ['--trace=off']);
    assert.deepEqual(recorderArguments(true, ['--project=smoke']), []);
    assert.deepEqual(recorderArguments(false, ['--trace=on']), []);
    assert.deepEqual(recorderArguments(false, ['--trace', 'on']), []);
  });
});

describe('the command for a failed test', () => {
  const failed = (project, file, line) => ({ project, file, line, status: 'unexpected' });
  const dependent = new Set(['limits', 'site-wide', BUDGETS]);
  const env = { JFB_E2E_CONFIGURATION: 'Release', PATH: 'p' };

  test('runs that test on its site with its trace, on the build the run used', () => {
    assert.equal(
      rerunCommand(failed('backoffice', 'specs/backoffice/a.spec.ts', 90), { index: 2, count: 2, budgets: false }, env, dependent),
      'JFB_E2E_SITES=2 JFB_E2E_SITE=2 JFB_E2E_SKIP_BUILD=1 JFB_E2E_CONFIGURATION=Release npx playwright test specs/backoffice/a.spec.ts:90 --project=backoffice --trace=on',
    );
    assert.equal(
      rerunCommand(failed('smoke', 'specs/smoke/b.spec.ts', 7), { index: 1, count: 1, budgets: false }, {}, dependent),
      'JFB_E2E_SKIP_BUILD=1 npx playwright test specs/smoke/b.spec.ts:7 --project=smoke --trace=on',
    );
  });

  test('leaves out the dependencies of a project that has them, and of every project when they are unknown', () => {
    const budgets = { index: 1, count: 1, budgets: true };
    assert.equal(
      rerunCommand(failed(BUDGETS, 'specs/budgets/c.spec.ts', 3), budgets, {}, dependent),
      'JFB_E2E_SITE=budgets JFB_E2E_SKIP_BUILD=1 npx playwright test specs/budgets/c.spec.ts:3 --project=budgets --no-deps --trace=on',
    );
    assert.match(rerunCommand(failed('smoke', 'specs/smoke/b.spec.ts', 7), budgets, {}, null), / --project=smoke --no-deps --trace=on$/);
  });

  test('is printed once per failed test, under one line that says where to run it, and not at all when none failed', () => {
    const site = { index: 1, count: 2, budgets: false };
    const passed = { project: 'smoke', file: 'specs/smoke/b.spec.ts', line: 7, status: 'expected' };
    const lines = rerunLines([{ site, tests: [passed, failed('backoffice', 'specs/backoffice/a.spec.ts', 90)] }], {}, dependent);
    assert.deepEqual(lines, [
      'e2e-gate: to run a failed test again with its trace, in tests/e2e:',
      'e2e-gate:   JFB_E2E_SITES=2 JFB_E2E_SITE=1 JFB_E2E_SKIP_BUILD=1 npx playwright test specs/backoffice/a.spec.ts:90 --project=backoffice --trace=on',
    ]);
    assert.deepEqual(rerunLines([{ site, tests: [passed] }], {}, dependent), []);
  });
});

/** A Playwright JSON report: each test `[project, file, titles, results]`, results a list of `[status, ms]`. */
function report(tests) {
  const files = new Map();
  for (const [project, file, titles, results = [['passed', 1000]]] of tests) {
    if (!files.has(file)) files.set(file, { title: file.replaceAll('/', '\\'), file, specs: [], suites: [] });
    let suite = files.get(file);
    for (const title of titles.slice(0, -1)) {
      let inner = suite.suites.find((child) => child.title === title);
      if (!inner) suite.suites.push((inner = { title, file, specs: [], suites: [] }));
      suite = inner;
    }
    const failed = results.some(([status]) => status !== 'passed' && status !== 'skipped');
    const skipped = results.length === 0 || results.every(([status]) => status === 'skipped');
    suite.specs.push({
      title: titles.at(-1),
      file,
      line: 7,
      tests: [
        {
          projectName: project,
          status: failed ? 'unexpected' : skipped ? 'skipped' : 'expected',
          results: results.map(([status, duration]) => ({ status, duration, ...(status === 'failed' ? { error: { message: '\u001b[31mExpected 1\u001b[39m\nmore' } } : {}) })),
        },
      ],
    });
  }
  return { suites: [...files.values()] };
}

describe('testsOfReport', () => {
  test('names each test by its project, file and title path, below the file', () => {
    const tests = testsOfReport(
      report([
        ['smoke', 'specs/smoke/a.spec.ts', ['signs in']],
        ['backoffice', 'specs/backoffice/b.spec.ts', ['the designer', 'moves a field'], [['failed', 2500]]],
        ['website', 'specs/website/c.spec.ts', ['listed only'], []],
      ]),
    );
    assert.deepEqual(
      tests.map((test) => test.id),
      [
        'smoke > specs/smoke/a.spec.ts > signs in',
        'backoffice > specs/backoffice/b.spec.ts > the designer > moves a field',
        'website > specs/website/c.spec.ts > listed only',
      ],
    );
    assert.deepEqual(
      tests.map((test) => [test.ran, test.status, test.seconds]),
      [
        [true, 'expected', 1],
        [true, 'unexpected', 2.5],
        [false, 'skipped', 0],
      ],
    );
    assert.equal(tests[1].error, 'Expected 1');
    assert.deepEqual(projectsOf(tests), ['smoke', 'backoffice', 'website']);
  });
});

describe('checkPlan', () => {
  const holds = ['a', 'b', 'c'];

  test('passes when the sites together list each test once', () => {
    assert.deepEqual(checkPlan(holds, [{ ids: ['a'] }, { ids: ['c', 'b'] }]), []);
  });

  test('fails when a test is on no site, or on two', () => {
    const none = checkPlan(holds, [{ ids: ['a'] }, { ids: ['b'] }]);
    assert.match(none[0], /the sites list 2 tests, but the projects hold 3/);
    assert.ok(none.includes('on no site: c'));
    const twice = checkPlan(holds, [{ ids: ['a', 'b'] }, { ids: ['b', 'c'] }]);
    assert.ok(twice.includes('on more than one site: b'));
  });

  test('compareIds counts each id as often as it differs', () => {
    assert.deepEqual(compareIds(['a', 'a', 'b'], ['a', 'c', 'c']), { missing: ['a', 'b'], extra: ['c', 'c'] });
  });
});

describe('judge', () => {
  const site = (label, tests, extra = {}) => ({ label, exit: 0, timedOut: false, tests: testsOfReport(report(tests)), errors: [], ...extra });
  const a = ['smoke', 'specs/smoke/a.spec.ts', ['a']];
  const b = ['website', 'specs/website/b.spec.ts', ['b']];
  const budget = [BUDGETS, 'specs/budgets/d.spec.ts', ['d']];
  const holds = testsOfReport(report([a, b, budget])).map((test) => test.id);

  test('passes when every test ran once and none failed, budgets after the sites', () => {
    const verdict = judge(holds, [
      site('site 1/2', [a], { endedAt: 100 }),
      site('site 2/2', [b], { endedAt: 200 }),
      site('budgets', [budget], { budgets: true, startedAt: 250, endedAt: 300 }),
    ]);
    assert.deepEqual(verdict, { ok: true, problems: [], ran: 3 });
  });

  test('fails a short count, naming what never ran', () => {
    const verdict = judge(holds, [site('site 1/1', [a]), site('budgets', [budget], { budgets: true })]);
    assert.equal(verdict.ok, false);
    assert.ok(verdict.problems.includes('2 tests ran, but the projects hold 3'));
    assert.ok(verdict.problems.includes('never ran: website > specs/website/b.spec.ts > b'));
  });

  test('fails a test that ran twice, and a test that was listed but never ran', () => {
    const twice = judge(holds, [site('site 1/2', [a, b]), site('site 2/2', [b]), site('budgets', [budget], { budgets: true })]);
    assert.ok(twice.problems.includes('ran more than once: website > specs/website/b.spec.ts > b'));
    const listedOnly = judge(holds, [site('site 1/1', [a, [...b.slice(0, 3), []]]), site('budgets', [budget], { budgets: true })]);
    assert.ok(listedOnly.problems.includes('never ran: website > specs/website/b.spec.ts > b'));
  });

  test('fails a site that failed, ran past its time or reported an error outside a test, and a failed test', () => {
    const failed = ['website', 'specs/website/b.spec.ts', ['b'], [['failed', 10]]];
    const verdict = judge(holds, [
      site('site 1/2', [a], { exit: 1, errors: ['Error: the host did not start'] }),
      site('site 2/2', [failed], { timedOut: true }),
      site('budgets', [budget], { budgets: true }),
    ]);
    assert.deepEqual(verdict.problems, [
      'site 1/2 failed (exit 1)',
      'site 1/2: Error: the host did not start',
      'site 2/2 ran past its time and was stopped',
      'site 2/2: failed specs/website/b.spec.ts:7 [website] b: Expected 1',
    ]);
  });

  test('fails when budgets started before a site had ended', () => {
    const verdict = judge(holds, [
      site('site 1/2', [a], { endedAt: 100 }),
      site('site 2/2', [b], { endedAt: 300 }),
      site('budgets', [budget], { budgets: true, startedAt: 250, endedAt: 400 }),
    ]);
    assert.deepEqual(verdict.problems, ['budgets started before site 2/2 had ended, so it did not run alone']);
  });
});

describe('the times', () => {
  test("are each spec file's tests' own seconds, budgets left out, and plan a site by its files", () => {
    const tests = testsOfReport(
      report([
        ['smoke', 'specs/smoke/a.spec.ts', ['one'], [['passed', 1500]]],
        ['smoke', 'specs/smoke/a.spec.ts', ['two'], [['passed', 500]]],
        ['website', 'specs/website/b.spec.ts', ['three'], []],
        [BUDGETS, 'specs/budgets/d.spec.ts', ['four'], [['passed', 9000]]],
      ]),
    );
    assert.deepEqual(specSeconds(tests), { 'specs/smoke/a.spec.ts': 2 });
    assert.equal(plannedSeconds(tests, { 'specs/smoke/a.spec.ts': 2, 'specs/website/b.spec.ts': 3 }), 5);
  });
});

describe('summaryLines', () => {
  test('give each site and budgets a row, then the problems and the count against the projects', () => {
    const rows = [
      { label: 'site 1/2', tests: 290, failed: 0, skipped: 2, seconds: 600.4, planned: 1400, runs: 1, exit: 0 },
      { label: 'budgets', tests: 1, failed: 0, skipped: 0, seconds: 120, runs: 1, exit: 0, alone: 'alone 10:00:00 to 10:02:00' },
    ];
    const ok = summaryLines(rows, { ok: true, problems: [], ran: 291 }, 291, 900);
    assert.match(ok[2], /site 1\/2\s+290\s+0\s+2\s+600\s+1400\s+1$/);
    assert.match(ok[3], /budgets\s+1\s+0\s+0\s+120\s+-\s+1\s+alone 10:00:00 to 10:02:00$/);
    assert.equal(ok.at(-1), 'e2e-gate: 291 of 291 tests ran, each once, none failed, in 900 s');
    const failed = summaryLines(rows, { ok: false, problems: ['site 1/2 failed (exit 1)'], ran: 290 }, 291, 900);
    assert.ok(failed.includes('e2e-gate: site 1/2 failed (exit 1)'));
    assert.equal(failed.at(-1), 'e2e-gate: FAILED: 290 of 291 tests ran, in 900 s');
  });
});
