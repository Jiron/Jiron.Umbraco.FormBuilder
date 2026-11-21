#!/usr/bin/env node
// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
/**
 * Runs the browser projects for the gate (ROADMAP 3.5 and 24.1, ADR-0011, ADR-0261) against several sites side by side.
 * A site is a whole copy of a run (tests/e2e/support/sites.ts): its own demo host, database, mail pickup folder, log,
 * ports, sign-in lock, credentials, results and report, four workers as always.
 *
 *   - **Every spec file on one site.** The sites share the spec files out by time (tests/e2e/spec-times.json, which
 *     `--record` rewrites from the run it just did); the last site names no file and takes every spec file the others
 *     do not name, so a new spec runs without anyone listing it. Inside a site the projects keep the config's order.
 *   - **Every test, counted.** Before the run Playwright lists the projects' tests once for the whole run and once
 *     for each site; the run does not start unless the sites' lists are exactly the whole list, each test once. After
 *     it, the tests every site reported (its results.json) are added up, and the run fails unless they are exactly the
 *     listed tests, each once; it fails when any site fails, naming the site and the failed tests.
 *   - **`budgets` alone, last** (ADR-0169): once every site's process has ended and no port of the sites listens, one
 *     more process runs only `budgets`, on a site of its own (site 1's ports, its own state). The log says when the
 *     last site ended and when budgets started and ended, and the run fails if budgets' results start before a site's
 *     end.
 *   - **The one rerun** ("Flaky means broken"): a site whose host's database hung leaves the hang marker in its host's
 *     folder, and only that site runs once more, on a fresh host; any other failure fails at once. Markers from an
 *     earlier run are removed first. The budgets site follows the same rule.
 *   - **No trace recorder** unless `--trace`: it cost a fifth of the stage's test time for a file read only after a
 *     failure. A failed test keeps its screenshot, and the summary gives the command that runs it again with its trace.
 *
 *   node scripts/e2e-gate.mjs                      the sites side by side (default 2), then budgets alone
 *   node scripts/e2e-gate.mjs --sites 2            two sites (1 to 3; one site is a run as it always was, then budgets)
 *   node scripts/e2e-gate.mjs --plan               list and check the plan (each site's tests), start nothing
 *   node scripts/e2e-gate.mjs --record             then rewrite tests/e2e/spec-times.json from this run, if it passed
 *   node scripts/e2e-gate.mjs --trace              record traces as the config says (retain-on-failure)
 *   node scripts/e2e-gate.mjs --site 2/3           only site 2 of 3, in this process, with its one rerun (each site's
 *                                                  process of a whole run is this)
 *   node scripts/e2e-gate.mjs --site budgets       only the budgets project on its site, with its one rerun
 *   node scripts/e2e-gate.mjs --stop               stop a run this script started (by PID, whole tree)
 *   node scripts/e2e-gate.mjs ... -- <args>        arguments for every `playwright test` (and its lists), such as
 *                                                  --grep; `--project` chooses the projects of a whole run
 *
 * Options: `--site-timeout <minutes>` (a site's process that runs longer is stopped, whole tree, and fails; default 60).
 *
 * Unless JFB_E2E_SKIP_BUILD=1, it builds the client assets and the demo once before the sites start, and the sites use
 * that build. Each site's console goes to .local/e2e-gate/<site>.log as well as here, each line marked with its site;
 * the PID file .local/e2e-gate/run.json records every process it starts, and an interrupt or a site that runs past its
 * time stops each one's whole tree by PID (CLAUDE.md rule 6). scripts/ci-local.ps1 runs it with
 * JFB_E2E_SKIP_BUILD=1 and JFB_E2E_CONFIGURATION=Release.
 *
 * Exit codes: 0 every test ran and passed, 1 a failure or a count that does not add up, 2 usage.
 */
import { spawn, spawnSync } from 'node:child_process';
import { createWriteStream, existsSync, mkdirSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { constants } from 'node:os';
import { join, relative, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { isOurProcess, portOpen, processStartTime, readPidRecord } from './sandbox.mjs';
import {
  formatSpecTimes,
  MAX_SITES,
  portsOfSite,
  readSpecTimes,
  SPEC_TIMES_FILE,
  siteLabel,
  siteState,
} from '../tests/e2e/support/sites.ts';

const root = resolve(fileURLToPath(new URL('..', import.meta.url)));
const isWindows = process.platform === 'win32';
export const E2E = join(root, 'tests', 'e2e');
export const E2E_STATE = join(root, '.local', 'e2e');

/** The runner's own state (git-ignored): the PID file, each site's console log and outcome, the lists. */
export const STATE = join(root, '.local', 'e2e-gate');

/**
 * How many sites a whole run starts unless told otherwise. Measured on the development machine (8 cores, 16 threads,
 * 2026-11-21): two sites took 19.4 minutes with budgets, three 18.9, one 23; with two the processor is already busy
 * all the time, and three only made each test slower, with five failures more of tests that wait on time.
 */
export const DEFAULT_SITES = 2;

/** The project that runs alone, after every site (ADR-0169). */
export const BUDGETS = 'budgets';

/** Playwright's command line, started with this Node: no shell between the runner and the processes it records. */
export const PLAYWRIGHT_CLI = join(E2E, 'node_modules', '@playwright', 'test', 'cli.js');

/** The hang marker's file name, read from the harness's own constant ("One source for every value"). */
export function hangMarkerName(healthSource = readFileSync(join(E2E, 'support', 'host-health.ts'), 'utf8')) {
  const name = healthSource.match(/export const HANG_MARKER = '([^']+)'/)?.[1];
  if (!name) throw new Error('tests/e2e/support/host-health.ts no longer exports HANG_MARKER');
  return name;
}

/** The hang markers in the hosts' folders below a state folder. */
export function hangMarkers(stateDir, name) {
  if (!existsSync(stateDir)) return [];
  return readdirSync(stateDir, { withFileTypes: true })
    .filter((entry) => entry.isDirectory())
    .map((entry) => join(stateDir, entry.name, name))
    .filter((file) => existsSync(file));
}

/**
 * Runs `run()` (which returns an exit code) once, and once more only when it failed and `markers()` finds a hang
 * marker. `clear(files)` removes markers: the stale ones before the first run, this run's before the second.
 */
export function runWithRerun({ run, markers, clear, read, log }) {
  clear(markers());
  const first = run();
  if (first === 0) return 0;
  const found = markers();
  if (found.length === 0) return first;
  for (const file of found) {
    log(`e2e-gate: a host's database hung (${read(file).trim()}; ${file}). Running the browser projects once more on fresh hosts (ADR-0011).`);
  }
  clear(found);
  const second = run();
  log(second === 0 ? 'e2e-gate: the rerun passed; note the hang marker in docs/WORKLOG.md.' : 'e2e-gate: the rerun failed too.');
  return second;
}

/** `i/n` as a site `{ index, count, budgets: false }`, `budgets` as the budgets site, anything else null. */
export function parseSite(text) {
  if (text === BUDGETS) return { index: 1, count: 1, budgets: true };
  const match = /^(\d+)\/(\d+)$/.exec(String(text));
  if (!match) return null;
  const index = Number(match[1]);
  const count = Number(match[2]);
  return index >= 1 && index <= count && count <= MAX_SITES ? { index, count, budgets: false } : null;
}

/** The command line, or `{ error }`. */
export function parseArgs(argv) {
  const options = { mode: 'run', sites: DEFAULT_SITES, site: null, plan: false, record: false, trace: false, siteTimeout: 60, passThrough: [] };
  let sitesGiven = false;
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    const value = () => {
      if (i + 1 >= argv.length) throw new Error(`${arg} needs a value`);
      return argv[++i];
    };
    try {
      if (arg === '--') {
        options.passThrough = argv.slice(i + 1);
        break;
      } else if (arg === '--sites') {
        options.sites = Number(value());
        sitesGiven = true;
        if (!Number.isInteger(options.sites) || options.sites < 1 || options.sites > MAX_SITES) return { error: `--sites is a whole number from 1 to ${MAX_SITES}` };
      } else if (arg === '--site') {
        options.mode = 'site';
        options.site = parseSite(value());
        if (!options.site) return { error: `--site is i/n (1 <= i <= n <= ${MAX_SITES}) or ${BUDGETS}` };
      } else if (arg === '--record') {
        options.record = true;
      } else if (arg === '--plan') {
        options.plan = true;
      } else if (arg === '--trace') {
        options.trace = true;
      } else if (arg === '--site-timeout') {
        options.siteTimeout = Number(value());
        if (!Number.isFinite(options.siteTimeout) || options.siteTimeout <= 0) return { error: '--site-timeout is minutes, more than 0' };
      } else if (arg === '--stop') {
        options.mode = 'stop';
      } else {
        return { error: `unknown argument ${arg} (arguments for playwright test go after --)` };
      }
    } catch (error) {
      return { error: error.message };
    }
  }
  if (options.mode === 'site' && (sitesGiven || options.record || options.plan)) return { error: '--site runs one site; leave out --sites, --plan and --record' };
  if (options.mode === 'stop' && (sitesGiven || options.record || options.plan || options.trace || options.site || options.passThrough.length > 0)) return { error: '--stop takes no run options' };
  if (options.plan && options.record) return { error: '--plan runs nothing to record' };
  return options;
}

/** An environment with these variables set (undefined removes one), replacing any spelling of a name (Windows ignores case). */
export function withEnv(base, overrides) {
  const env = {};
  const replaced = new Set(Object.keys(overrides).map((key) => key.toUpperCase()));
  for (const [key, value] of Object.entries(base)) if (!replaced.has(key.toUpperCase())) env[key] = value;
  for (const [key, value] of Object.entries(overrides)) if (value !== undefined) env[key] = value;
  return env;
}

/** The environment of a site's processes: JFB_E2E_SITES and JFB_E2E_SITE as sites.ts reads them, none for one site. */
export function siteEnv(base, site) {
  if (site.budgets) return withEnv(base, { JFB_E2E_SITES: undefined, JFB_E2E_SITE: BUDGETS });
  if (site.count === 1) return withEnv(base, { JFB_E2E_SITES: undefined, JFB_E2E_SITE: undefined });
  return withEnv(base, { JFB_E2E_SITES: String(site.count), JFB_E2E_SITE: String(site.index) });
}

/** Playwright arguments without their `--project` choices (`--project x`, `--project=x`, `-p x`). */
export function withoutProjects(args) {
  const kept = [];
  for (let i = 0; i < args.length; i++) {
    if (args[i] === '--project' || args[i] === '-p') i++;
    else if (!args[i].startsWith('--project=')) kept.push(args[i]);
  }
  return kept;
}

/** The arguments of a site's `playwright test`: its projects in the config's order, then the rest. */
export function siteArguments(site, projects, passThrough) {
  const chosen = site.budgets ? [BUDGETS] : projects.filter((project) => project !== BUDGETS);
  return [...chosen.map((project) => `--project=${project}`), ...(site.budgets ? ['--no-deps'] : []), ...withoutProjects(passThrough)];
}

/**
 * The trace recorder's argument of a site's `playwright test`: off unless the run was asked to `--trace`, when the
 * config's mode records (`retain-on-failure`), or the arguments after `--` choose a mode themselves. The recorder cost a
 * fifth of the stage's test time, on every passing test, for a file read only after a failure (ADR-0261); a failed
 * test's command to run it again with its trace is in the summary (`rerunLines`).
 */
export function recorderArguments(trace, passThrough) {
  return trace || passThrough.some((arg) => arg === '--trace' || arg.startsWith('--trace=')) ? [] : ['--trace=off'];
}

/** The variables a command needs to run a test as it ran on its site: the site's, and the build the run used. */
function rerunEnv(site, env) {
  const vars = site.budgets ? { JFB_E2E_SITE: BUDGETS } : site.count > 1 ? { JFB_E2E_SITES: site.count, JFB_E2E_SITE: site.index } : {};
  vars.JFB_E2E_SKIP_BUILD = 1;
  for (const name of ['JFB_E2E_CONFIGURATION', 'JFB_E2E_DELIVERY_API']) if (env[name]) vars[name] = env[name];
  return Object.entries(vars).map(([name, value]) => `${name}=${value}`);
}

/**
 * The command, from tests/e2e, that runs one failed test again on its site with its trace. `dependent` is the set of
 * projects that have dependencies, which the command leaves out (`--no-deps`); null when unknown, which leaves them out
 * for every project.
 */
export function rerunCommand(test, site, env, dependent) {
  const noDeps = dependent === null || dependent.has(test.project) ? ['--no-deps'] : [];
  return [...rerunEnv(site, env), 'npx playwright test', `${test.file}:${test.line}`, `--project=${test.project}`, ...noDeps, '--trace=on'].join(' ');
}

/** The summary's lines for the failed tests of the runs (`{ site, tests }`): a command per test, none when none failed. */
export function rerunLines(runs, env, dependent) {
  const failed = runs.flatMap((run) => run.tests.filter((test) => test.status === 'unexpected' || test.status === 'flaky').map((test) => rerunCommand(test, run.site, env, dependent)));
  if (failed.length === 0) return [];
  return ['e2e-gate: to run a failed test again with its trace, in tests/e2e:', ...[...new Set(failed)].map((command) => `e2e-gate:   ${command}`)];
}

/** The file name (without extension) of a site's log and outcome. */
export function siteFileLabel(site) {
  return site.budgets ? BUDGETS : `site-${site.index}-of-${site.count}`;
}

const ANSI = /\u001b\[[0-9;]*m/g;

/**
 * Every test of a Playwright JSON report (its --list or its results): `{ id, project, file, line, title, ran, status,
 * seconds, error }`. A test's id is its project, file and title path, which is unique in a run. `ran` is whether it has
 * a result: a test skipped while it ran counts as reached, as in the integration tests' count.
 */
export function testsOfReport(report) {
  const tests = [];
  const walk = (suite, titles) => {
    for (const spec of suite.specs ?? []) {
      const path = [...titles, spec.title];
      for (const test of spec.tests ?? []) {
        const results = test.results ?? [];
        const failed = results.find((result) => ['failed', 'timedOut', 'interrupted'].includes(result.status));
        tests.push({
          id: `${test.projectName} > ${spec.file} > ${path.join(' > ')}`,
          project: test.projectName,
          file: spec.file,
          line: spec.line,
          title: path.join(' > '),
          ran: results.length > 0,
          status: test.status,
          seconds: results.reduce((sum, result) => sum + (result.duration ?? 0), 0) / 1000,
          error: failed ? String(failed.error?.message ?? failed.status).replace(ANSI, '').trim().split(/\r?\n/)[0] : '',
        });
      }
    }
    for (const child of suite.suites ?? []) walk(child, [...titles, child.title]);
  };
  // The top suites are the spec files, whose titles are their paths: a title path starts below them.
  for (const file of report?.suites ?? []) walk(file, []);
  return tests;
}

/** The projects that hold tests in a list, in the order they first appear. */
export function projectsOf(tests) {
  return [...new Set(tests.map((test) => test.project))];
}

/** The ids one list holds more often than another: `{ missing, extra }`, each id as often as it differs. */
export function compareIds(holds, ran) {
  const counts = new Map();
  for (const id of holds) counts.set(id, (counts.get(id) ?? 0) + 1);
  for (const id of ran) counts.set(id, (counts.get(id) ?? 0) - 1);
  const missing = [];
  const extra = [];
  for (const [id, count] of counts) {
    for (let i = 0; i < count; i++) missing.push(id);
    for (let i = 0; i < -count; i++) extra.push(id);
  }
  return { missing: missing.sort(), extra: extra.sort() };
}

const listProblems = (problems, missing, extra, never, twice) => {
  for (const id of missing.slice(0, 20)) problems.push(`${never}: ${id}`);
  if (missing.length > 20) problems.push(`${never}: ${missing.length - 20} more`);
  for (const id of extra.slice(0, 20)) problems.push(`${twice}: ${id}`);
  if (extra.length > 20) problems.push(`${twice}: ${extra.length - 20} more`);
};

/**
 * Checks the plan before anything starts: the sites' lists (and budgets') together are exactly the whole run's list,
 * each test once. `holds` is the whole list's ids; `lists` is `[{ label, ids }]`.
 */
export function checkPlan(holds, lists) {
  const problems = [];
  const planned = lists.flatMap((list) => list.ids);
  const { missing, extra } = compareIds(holds, planned);
  if (missing.length > 0 || extra.length > 0) {
    problems.push(`the sites list ${planned.length} tests, but the projects hold ${holds.length}`);
    listProblems(problems, missing, extra, 'on no site', 'on more than one site');
  }
  return problems;
}

/**
 * The verdict of a whole run. `holds` is the ids the projects hold; each run is `{ label, exit, timedOut, tests,
 * errors, startedAt, endedAt, budgets }` (times in ms). Fails when a site or budgets failed, ran past its time or
 * reported an error outside a test, when a test failed, when budgets started before a site had ended, and unless the
 * tests that ran are exactly the projects' tests, each once.
 */
export function judge(holds, runs) {
  const problems = [];
  for (const run of runs) {
    if (run.timedOut) problems.push(`${run.label} ran past its time and was stopped`);
    else if (run.exit !== 0) problems.push(`${run.label} failed (exit ${run.exit})`);
    for (const error of run.errors ?? []) problems.push(`${run.label}: ${error}`);
    for (const test of run.tests.filter((t) => t.status === 'unexpected' || t.status === 'flaky')) {
      problems.push(`${run.label}: failed ${test.file}:${test.line} [${test.project}] ${test.title}${test.error ? `: ${test.error}` : ''}`);
    }
  }
  const budgets = runs.find((run) => run.budgets);
  if (budgets?.startedAt !== undefined) {
    for (const run of runs.filter((r) => !r.budgets && r.endedAt !== undefined)) {
      if (run.endedAt > budgets.startedAt) problems.push(`budgets started before ${run.label} had ended, so it did not run alone`);
    }
  }
  const ran = runs.flatMap((run) => run.tests.filter((test) => test.ran).map((test) => test.id));
  const { missing, extra } = compareIds(holds, ran);
  if (ran.length !== holds.length || missing.length > 0 || extra.length > 0) {
    problems.push(`${ran.length} tests ran, but the projects hold ${holds.length}`);
    listProblems(problems, missing, extra, 'never ran', 'ran more than once');
  }
  return { ok: problems.length === 0, problems, ran: ran.length };
}

/** Seconds per spec file of the sites' results (budgets' left out), for the times file. */
export function specSeconds(tests) {
  const times = {};
  for (const test of tests) {
    if (test.project === BUDGETS || !test.ran) continue;
    times[test.file] = (times[test.file] ?? 0) + test.seconds;
  }
  return times;
}

/** The planned seconds of a site: the times of the spec files its list holds. */
export function plannedSeconds(tests, times) {
  return [...new Set(tests.map((test) => test.file))].reduce((sum, file) => sum + (times[file] ?? 0), 0);
}

const clock = (ms) => (ms === undefined ? '-' : new Date(ms).toTimeString().slice(0, 8));

/** The summary's lines. Each row: `{ label, tests, failed, skipped, seconds, planned, runs, exit, timedOut, alone }`. */
export function summaryLines(rows, verdict, holds, wall) {
  const lines = ['', 'e2e-gate:   run            tests  failed  skipped  seconds  planned  runs'];
  for (const row of rows) {
    const state = row.timedOut ? 'stopped' : row.exit === 0 ? '' : `exit ${row.exit}`;
    const cells = [
      row.label.padEnd(14),
      String(row.tests ?? '-').padStart(5),
      String(row.failed ?? '-').padStart(6),
      String(row.skipped ?? '-').padStart(7),
      String(Math.round(row.seconds ?? 0)).padStart(7),
      (row.planned === undefined ? '-' : String(Math.round(row.planned))).padStart(7),
      String(row.runs ?? '-').padStart(4),
      [state, row.alone ?? ''].filter(Boolean).join('  '),
    ];
    lines.push(`e2e-gate:   ${cells.join('  ')}`.trimEnd());
  }
  lines.push('');
  for (const problem of verdict.problems) lines.push(`e2e-gate: ${problem}`);
  const counted = `${verdict.ran} of ${holds} tests ran`;
  lines.push(verdict.ok ? `e2e-gate: ${counted}, each once, none failed, in ${Math.round(wall)} s` : `e2e-gate: FAILED: ${counted}, in ${Math.round(wall)} s`);
  return lines;
}

// ---------------------------------------------------------------------------------------------------------------
// Processes: everything below starts Playwright and is checked by the runs themselves, not by the script's tests.
// ---------------------------------------------------------------------------------------------------------------

/** Stops a process and everything it started, by PID. */
function killTree(pid) {
  if (isWindows) spawnSync('taskkill.exe', ['/PID', String(pid), '/T', '/F'], { stdio: 'ignore' });
  else {
    try {
      process.kill(-pid, 'SIGKILL');
    } catch {
      try {
        process.kill(pid, 'SIGKILL');
      } catch {
        // gone already
      }
    }
  }
}

const stamp = () => new Date().toTimeString().slice(0, 8);
const say = (line) => console.log(`e2e-gate: ${line}`);

/** One site in this process: `playwright test` with the site's environment, and the one rerun after a hung database. */
function runOneSite(options) {
  const { site, passThrough } = options;
  const state = siteState(root, site);
  const name = hangMarkerName();
  const env = siteEnv(process.env, site);
  const outcome = { site: siteLabel(site), runs: [] };
  mkdirSync(STATE, { recursive: true });
  const run = () => {
    const began = Date.now();
    say(`${siteLabel(site)}: playwright test starts at ${stamp()} (state ${relative(root, state)})`);
    const result = spawnSync(process.execPath, [PLAYWRIGHT_CLI, 'test', ...passThrough, ...recorderArguments(options.trace, passThrough)], { cwd: E2E, stdio: 'inherit', env });
    const exit = result.status ?? 1;
    const ended = Date.now();
    outcome.runs.push({ exit, startedAt: began, endedAt: ended });
    say(`${siteLabel(site)}: playwright test ended at ${stamp()} (exit ${exit}) after ${Math.round((ended - began) / 1000)} s`);
    return exit;
  };
  const code = runWithRerun({
    run,
    markers: () => hangMarkers(state, name),
    clear: (files) => files.forEach((file) => rmSync(file, { force: true })),
    read: (file) => readFileSync(file, 'utf8'),
    log: (line) => console.error(line),
  });
  outcome.exit = code;
  writeFileSync(join(STATE, `${siteFileLabel(site)}.json`), `${JSON.stringify(outcome, null, 2)}\n`);
  return code;
}

class Runner {
  constructor(options) {
    this.options = options;
    this.pidFile = join(STATE, 'run.json');
    this.running = new Map();
    this.record = { pid: process.pid, startedAt: processStartTime(process.pid), children: [] };
  }

  claim() {
    const previous = readPidRecord(this.pidFile);
    if (previous && previous.pid !== process.pid && isOurProcess(previous)) {
      throw new Error(`another run (PID ${previous.pid}) is going; wait for it, or stop it: node scripts/e2e-gate.mjs --stop`);
    }
    mkdirSync(STATE, { recursive: true });
    this.save();
    const stop = (signal) => {
      console.error(`e2e-gate: ${signal}; stopping every process this run started`);
      this.stopAll();
      process.exit(130);
    };
    for (const signal of ['SIGINT', 'SIGTERM', 'SIGHUP', 'SIGBREAK']) {
      if (signal in constants.signals) process.on(signal, () => stop(signal));
    }
  }

  save() {
    writeFileSync(this.pidFile, `${JSON.stringify(this.record, null, 2)}\n`);
  }

  release() {
    rmSync(this.pidFile, { force: true });
  }

  stopAll() {
    for (const child of this.running.values()) killTree(child.pid);
    this.release();
  }

  /** Starts a node process, recorded; resolves `{ exit, timedOut, startedAt, endedAt }`. Output is marked and logged. */
  start({ label, args, env, cwd = root, log, mark, timeoutMinutes }) {
    const stream = log ? createWriteStream(log) : null;
    const startedAt = Date.now();
    const child = spawn(process.execPath, args, { cwd, env, stdio: ['ignore', 'pipe', 'pipe'], detached: !isWindows, windowsHide: true });
    this.running.set(label, child);
    const entry = { label, pid: child.pid };
    this.record.children.push(entry);
    entry.startedAt = processStartTime(child.pid) ?? undefined;
    this.save();
    const lines = { out: '', err: '' };
    const pipe = (key, target) => (chunk) => {
      stream?.write(chunk);
      if (!mark) return;
      lines[key] += chunk.toString('utf8');
      const parts = lines[key].split(/\r?\n/);
      lines[key] = parts.pop() ?? '';
      for (const line of parts) target.write(`[${mark}] ${line}\n`);
    };
    child.stdout.on('data', pipe('out', process.stdout));
    child.stderr.on('data', pipe('err', process.stderr));
    let timedOut = false;
    const timer = timeoutMinutes
      ? setTimeout(
          () => {
            timedOut = true;
            console.error(`e2e-gate: ${label} ran past ${timeoutMinutes} minutes; stopping its process tree`);
            killTree(child.pid);
          },
          timeoutMinutes * 60 * 1000,
        )
      : null;
    return new Promise((done) => {
      const finish = (exit) => {
        if (timer) clearTimeout(timer);
        this.running.delete(label);
        for (const [key, target] of [['out', process.stdout], ['err', process.stderr]]) if (mark && lines[key]) target.write(`[${mark}] ${lines[key]}\n`);
        stream?.end();
        done({ exit, timedOut, startedAt, endedAt: Date.now() });
      };
      child.on('error', (error) => {
        stream?.write(`\n${error.message}\n`);
        finish(-1);
      });
      child.on('close', (code) => finish(code ?? -1));
    });
  }
}

/** Lists a site's tests (`playwright test --list`, which starts no host); resolves the JSON report. */
async function listTests(runner, label, env, args) {
  const file = join(STATE, 'lists', `${label}.json`);
  rmSync(file, { force: true });
  const result = await runner.start({
    label: `list-${label}`,
    args: [PLAYWRIGHT_CLI, 'test', '--list', '--reporter=json', '--pass-with-no-tests', ...args],
    env: withEnv(env, { PLAYWRIGHT_JSON_OUTPUT_FILE: file, PLAYWRIGHT_JSON_OUTPUT_NAME: undefined }),
    cwd: E2E,
    log: join(STATE, 'lists', `${label}.log`),
  });
  if (result.exit !== 0 || !existsSync(file)) throw new Error(`Playwright could not list the tests of ${label} (exit ${result.exit}; ${relative(root, join(STATE, 'lists', `${label}.log`))})`);
  return JSON.parse(readFileSync(file, 'utf8'));
}

/** Builds the client assets and the demo once, as each site's global set-up would, so the sites share one build. */
function buildOnce() {
  say('building the client assets and the demo once for every site');
  const script = "import { buildDemo, ensureClientAssets } from './support/host.ts'; ensureClientAssets(); buildDemo();";
  const result = spawnSync(process.execPath, ['--input-type=module', '-e', script], { cwd: E2E, stdio: 'inherit' });
  if (result.status !== 0) throw new Error(`the build failed (exit ${result.status ?? result.signal})`);
}

/** The ports of these sites that something listens on. */
async function listening(sites) {
  const ports = [...new Set(sites.flatMap((index) => portsOfSite(index)))];
  const open = await Promise.all(ports.map((port) => portOpen(port)));
  return ports.filter((_, at) => open[at]);
}

function readReport(site) {
  const file = join(siteState(root, site), 'results.json');
  if (!existsSync(file)) return { tests: [], errors: [`wrote no results (${relative(root, file)})`] };
  const report = JSON.parse(readFileSync(file, 'utf8'));
  const errors = (report.errors ?? []).map((error) => String(error.message ?? error).replace(ANSI, '').trim().split(/\r?\n/)[0]);
  return { tests: testsOfReport(report), errors };
}

function readOutcome(site) {
  const file = join(STATE, `${siteFileLabel(site)}.json`);
  return existsSync(file) ? JSON.parse(readFileSync(file, 'utf8')) : null;
}

/** The projects of the config that depend on others, from the config itself; null when it cannot be read here. */
async function dependentProjects() {
  try {
    const { default: config } = await import(pathToFileURL(join(E2E, 'playwright.config.ts')).href);
    return new Set((config.projects ?? []).filter((project) => (project.dependencies ?? []).length > 0).map((project) => project.name));
  } catch {
    return null;
  }
}

function readTimesFile() {
  const path = join(E2E, SPEC_TIMES_FILE);
  return readSpecTimes(existsSync(path) ? readFileSync(path, 'utf8') : null);
}

async function runAll(options) {
  const began = Date.now();
  if (!existsSync(PLAYWRIGHT_CLI)) {
    console.error(`e2e-gate: Playwright is not installed in tests/e2e: run npm ci there first`);
    return 1;
  }
  const runner = new Runner(options);
  try {
    runner.claim();
  } catch (error) {
    console.error(`e2e-gate: ${error.message}`);
    return 1;
  }
  try {
    const count = options.sites;
    const sites = Array.from({ length: count }, (_, i) => ({ index: i + 1, count, budgets: false }));
    const budgetsSite = { index: 1, count: 1, budgets: true };
    rmSync(join(STATE, 'lists'), { recursive: true, force: true });
    mkdirSync(join(STATE, 'lists'), { recursive: true });
    for (const site of [...sites, budgetsSite]) rmSync(join(STATE, `${siteFileLabel(site)}.json`), { force: true });

    // Which projects the run has (a project chosen with --project brings the projects it depends on); then what they
    // hold, each on its own, so a filter applies to all of them alike (Playwright does not filter the projects another
    // depends on); then each site's share and budgets'.
    const oneSite = siteEnv(process.env, { index: 1, count: 1, budgets: false });
    const projects = projectsOf(testsOfReport(await listTests(runner, 'projects', oneSite, options.passThrough)));
    const listsBudgets = projects.includes(BUDGETS);
    const named = [...projects.map((project) => `--project=${project}`), '--no-deps', ...withoutProjects(options.passThrough)];
    const [whole, ...lists] = await Promise.all([
      listTests(runner, 'whole', oneSite, named).then(testsOfReport),
      ...sites.map(async (site) => ({ site, tests: testsOfReport(await listTests(runner, siteFileLabel(site), siteEnv(process.env, site), siteArguments(site, projects, options.passThrough))) })),
      ...(listsBudgets ? [listTests(runner, BUDGETS, siteEnv(process.env, budgetsSite), siteArguments(budgetsSite, projects, options.passThrough)).then((report) => ({ site: budgetsSite, tests: testsOfReport(report) }))] : []),
    ]);
    const holds = whole.map((test) => test.id);
    const planProblems = checkPlan(holds, lists.map((list) => ({ label: siteLabel(list.site), ids: list.tests.map((test) => test.id) })));
    if (planProblems.length > 0) {
      for (const problem of planProblems) console.error(`e2e-gate: ${problem}`);
      console.error('e2e-gate: FAILED before any site started: the plan does not give every test to exactly one site');
      return 1;
    }
    const times = readTimesFile();
    const planned = new Map(lists.map((list) => [siteLabel(list.site), plannedSeconds(list.tests, times)]));
    const withBudgets = lists.some((list) => list.site.budgets && list.tests.length > 0);
    const active = lists.filter((list) => !list.site.budgets && list.tests.length > 0).map((list) => list.site);
    say(`${holds.length} tests in ${projects.length} project(s) (${projects.join(', ')}); ${active.length} site(s) side by side${withBudgets ? ', then budgets alone' : ''}`);
    for (const list of lists) {
      say(`  ${siteLabel(list.site).padEnd(10)} ${String(list.tests.length).padStart(4)} tests in ${new Set(list.tests.map((test) => test.file)).size} spec file(s), ${Math.round(planned.get(siteLabel(list.site)) ?? 0)} s of test time planned`);
    }

    if (options.plan) return 0;

    const taken = await listening([...new Set([...active.map((site) => site.index), ...(withBudgets ? [1] : [])])]);
    if (taken.length > 0) {
      console.error(`e2e-gate: port ${taken.join(', ')} is taken; the sites need every port of theirs free (tests/e2e/support/sites.ts)`);
      return 1;
    }
    // No site's results of an earlier run may be counted for this one.
    for (const site of [...active, budgetsSite]) rmSync(join(siteState(root, site), 'results.json'), { force: true });
    if (process.env.JFB_E2E_SKIP_BUILD !== '1') buildOnce();
    const baseEnv = withEnv(process.env, { JFB_E2E_SKIP_BUILD: '1' });

    const startSite = (site) =>
      runner.start({
        label: siteLabel(site),
        args: [fileURLToPath(import.meta.url), '--site', site.budgets ? BUDGETS : `${site.index}/${site.count}`, ...(options.trace ? ['--trace'] : []), '--', ...siteArguments(site, projects, options.passThrough)],
        env: baseEnv,
        log: join(STATE, `${siteFileLabel(site)}.log`),
        mark: siteLabel(site),
        timeoutMinutes: options.siteTimeout,
      });

    const siteRuns = await Promise.all(active.map(async (site) => ({ site, ...(await startSite(site)) })));
    const lastEnd = Math.max(...siteRuns.map((run) => run.endedAt), began);
    say(`every site has ended, the last at ${clock(lastEnd)}`);

    let budgetsRun = null;
    if (withBudgets) {
      // Nothing of this run may be left beside budgets: no process it started, no port of the sites.
      const deadline = Date.now() + 60_000;
      let open = await listening(active.map((site) => site.index));
      while (open.length > 0 && Date.now() < deadline) {
        await new Promise((resolveWait) => setTimeout(resolveWait, 1000));
        open = await listening(active.map((site) => site.index));
      }
      if (runner.running.size > 0 || open.length > 0) {
        console.error(`e2e-gate: FAILED: something of the sites still runs (${[...runner.running.keys(), ...open.map((port) => `port ${port}`)].join(', ')}), so budgets cannot run alone`);
        return 1;
      }
      say(`no process this run started is running and no port of the sites listens; budgets runs alone from ${stamp()}`);
      budgetsRun = { site: budgetsSite, ...(await startSite(budgetsSite)) };
      say(`budgets ended at ${clock(budgetsRun.endedAt)}; nothing else of this run ran from ${clock(budgetsRun.startedAt)} to ${clock(budgetsRun.endedAt)}`);
    }

    const runs = [...siteRuns, ...(budgetsRun ? [budgetsRun] : [])].map((run) => {
      const report = readReport(run.site);
      const outcome = readOutcome(run.site);
      const lastRun = outcome?.runs?.at(-1);
      return {
        ...run,
        label: siteLabel(run.site),
        budgets: run.site.budgets,
        tests: report.tests,
        errors: report.errors,
        runs: outcome?.runs?.length,
        // The Playwright run whose results count: the rerun when there was one.
        startedAt: lastRun?.startedAt ?? run.startedAt,
        endedAt: lastRun?.endedAt ?? run.endedAt,
        wall: (run.endedAt - run.startedAt) / 1000,
      };
    });
    const verdict = judge(holds, runs);
    const rows = runs.map((run) => ({
      label: run.label,
      tests: run.tests.filter((test) => test.ran).length,
      failed: run.tests.filter((test) => test.status === 'unexpected' || test.status === 'flaky').length,
      skipped: run.tests.filter((test) => test.ran && test.status === 'skipped').length,
      seconds: run.wall,
      planned: planned.get(run.label),
      runs: run.runs,
      exit: run.exit,
      timedOut: run.timedOut,
      alone: run.budgets ? `alone ${clock(run.startedAt)} to ${clock(run.endedAt)}, after the last site's end at ${clock(lastEnd)}` : '',
    }));
    for (const line of summaryLines(rows, verdict, holds.length, (Date.now() - began) / 1000)) (verdict.ok ? console.log : console.error)(line);
    for (const run of runs) say(`${run.label}: report ${relative(root, join(siteState(root, run.site), 'report'))}, log ${relative(root, join(STATE, `${siteFileLabel(run.site)}.log`))}`);
    const again = rerunLines(runs, process.env, runs.some((run) => run.tests.some((test) => test.status === 'unexpected' || test.status === 'flaky')) ? await dependentProjects() : null);
    for (const line of again) console.error(line);

    const unknown = runs
      .filter((run) => !run.budgets)
      .flatMap((run) => run.tests.map((test) => test.file))
      .filter((file, at, all) => all.indexOf(file) === at && !(file in times));
    if (unknown.length > 0) say(`${unknown.length} spec file(s) ran that ${SPEC_TIMES_FILE} does not know (on the last site): ${unknown.slice(0, 10).join(', ')}; record the times again: node scripts/e2e-gate.mjs --record`);
    if (options.record) {
      if (!verdict.ok) console.error(`e2e-gate: tests/e2e/${SPEC_TIMES_FILE} is left as it was, since the run failed`);
      else {
        writeFileSync(join(E2E, SPEC_TIMES_FILE), formatSpecTimes(specSeconds(runs.flatMap((run) => run.tests)), active.length));
        say(`wrote tests/e2e/${SPEC_TIMES_FILE} from this run's ${active.length} site(s)`);
      }
    }
    return verdict.ok ? 0 : 1;
  } catch (error) {
    console.error(`e2e-gate: ${error.message}`);
    runner.stopAll();
    return 1;
  } finally {
    runner.release();
  }
}

function stop() {
  const pidFile = join(STATE, 'run.json');
  const record = readPidRecord(pidFile);
  if (!record) {
    console.log('e2e-gate: no run is recorded');
    return 0;
  }
  let stopped = 0;
  for (const entry of [record, ...(record.children ?? [])]) {
    if (typeof entry.startedAt === 'string' && isOurProcess(entry)) {
      killTree(entry.pid);
      stopped++;
    }
  }
  rmSync(pidFile, { force: true });
  console.log(stopped === 0 ? 'e2e-gate: none of its processes runs any more' : `e2e-gate: stopped ${stopped} process tree(s)`);
  return 0;
}

async function main(argv) {
  const options = parseArgs(argv);
  if (options.error) {
    console.error(`e2e-gate: ${options.error}`);
    return 2;
  }
  if (options.mode === 'stop') return stop();
  if (options.mode === 'site') return runOneSite(options);
  return runAll(options);
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  process.exitCode = await main(process.argv.slice(2));
}
