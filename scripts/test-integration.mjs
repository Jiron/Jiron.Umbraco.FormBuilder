#!/usr/bin/env node
// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
/**
 * Runs the built integration assembly in several processes at once, the "shares" (ROADMAP 24.1, ADR-0261). Each
 * integration test starts an Umbraco of its own, so the time is the count times the start; side by side, the run takes
 * about as long as its longest share.
 *
 *   - **By class, weighted by time.** The classes and their seconds come from tests/integration-times.json, which
 *     `--record` rewrites from the run it just did. The last share names no class: it takes every test the other
 *     shares do not name, so a class the file does not know still runs, and no class can run twice or never. The
 *     summary says when the last share ran classes the file does not know, so someone records again.
 *   - **Every test, counted.** Before the run the assembly is asked for its tests (`dotnet test --list-tests`, which
 *     runs none); after it, the tests every process reported are added up. The run fails unless they are exactly the
 *     assembly's tests, each once, and it fails when any process fails or hangs, naming the share and the failed
 *     tests. A test that is skipped while it runs (Assert.Ignore, Assume) counts as reached and is named in the summary.
 *   - **Timed tests alone.** Tests in the NUnit category "Timed" hold the product to a time budget; the shares leave
 *     them out, and once every other process has ended one process runs only them, with nothing beside it.
 *   - **Each process its own state:** a temporary folder (TMP, TEMP and TMPDIR) and System_DefaultWorkingDirectory=1,
 *     so Umbraco's test harness works under that folder and not in the project's shared TEMP; its run settings (the
 *     given base file with the NUnit `Where` put in); its results and console log.
 *
 *   node scripts/test-integration.mjs                       every share at once, then the timed tests alone
 *   node scripts/test-integration.mjs --shares 4            four shares (default: half the cores, 1 to 8)
 *   node scripts/test-integration.mjs --record              then rewrite tests/integration-times.json from this run
 *   node scripts/test-integration.mjs --beside <assembly>   also run another test assembly (the unit or AI tests)
 *                                                           beside the shares, judged by its exit code; it ends
 *                                                           before the timed tests start. Repeatable.
 *   node scripts/test-integration.mjs --share 2/6           only share 2 of 6, in one process (a CI job)
 *   node scripts/test-integration.mjs --share timed         only the timed tests (a CI job)
 *   node scripts/test-integration.mjs --sum <folder>        add up the result.json files of such jobs: fails unless
 *                                                           every share 1..n and the timed run reported, all for the
 *                                                           same assembly, none failed, and together they ran
 *                                                           exactly the assembly's tests
 *   node scripts/test-integration.mjs --stop                stop a run this script started (by PID, whole tree)
 *
 * Options: `--assembly <dll>` (default: the Release build), `--settings <runsettings>` (a base file, for example
 * tests/coverage.runsettings), `--results <folder>` (each process writes into <folder>/<label>; default
 * .local/test-integration), `--share-timeout <minutes>` (a process that runs longer is stopped, whole tree, and fails;
 * default 60), and after `--` arguments passed to every `dotnet test` as they are.
 *
 * It never builds: build first (`dotnet build -c Release`). Each process's working folder is the git-ignored
 * .local/test-integration/work/<label>/ (removed when that process passed); the PID file .local/test-integration/
 * run.json records every process it starts, and an interrupt or a hang stops each one's whole tree by PID (CLAUDE.md
 * rule 6).
 *
 * Exit codes: 0 every test ran and passed, 1 a failure or a count that does not add up, 2 usage.
 */
import { spawn, spawnSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { createWriteStream, existsSync, mkdirSync, readdirSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { availableParallelism, constants } from 'node:os';
import { basename, dirname, join, relative, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const repo = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const isWindows = process.platform === 'win32';

/** The integration assembly of the Release build, relative to the repository. */
export const ASSEMBLY =
  'tests/Jiron.Umbraco.FormBuilder.Tests.Integration/bin/Release/net10.0/Jiron.Umbraco.FormBuilder.Tests.Integration.dll';

/** The seconds per class that weigh the shares, relative to the repository. */
export const TIMES_FILE = 'tests/integration-times.json';

/** The NUnit category of the tests that hold the product to a time budget. */
export const TIMED_CATEGORY = 'Timed';

/** The state of every run, relative to the repository (git-ignored). */
export const STATE = '.local/test-integration';

/** The most shares a run starts by default, and the hang timeout of one test. */
export const MAX_DEFAULT_SHARES = 8;
export const HANG_TIMEOUT = '10m';

/** Outcomes of a test that did not run to an end of its own: skipped, ignored or inconclusive while it ran. */
const SKIPPED = new Set(['NotExecuted', 'NotRunnable', 'Inconclusive', 'Pending', 'Disconnected', 'Warning', 'None']);

/** Outcomes that fail a run. */
const FAILED = new Set(['Failed', 'Error', 'Timeout', 'Aborted', 'PassedButRunAborted']);

/** Half the cores, at least one and at most eight: the default number of shares. */
export function defaultShares(cores = availableParallelism()) {
  return Math.min(MAX_DEFAULT_SHARES, Math.max(1, Math.floor(cores / 2)));
}

/** The command line, or `{ error }`. */
export function parseArgs(argv, cores = availableParallelism()) {
  const options = {
    mode: 'run',
    shares: null,
    share: null,
    sum: null,
    record: false,
    assembly: ASSEMBLY,
    settings: null,
    results: STATE,
    beside: [],
    shareTimeout: 60,
    passThrough: [],
  };
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
      } else if (arg === '--shares') {
        options.shares = Number(value());
        if (!Number.isInteger(options.shares) || options.shares < 1) return { error: '--shares is a whole number, 1 or more' };
      } else if (arg === '--share') {
        options.share = parseShare(value());
        if (!options.share) return { error: '--share is i/n (1 <= i <= n) or timed' };
      } else if (arg === '--sum') {
        options.mode = 'sum';
        options.sum = value();
      } else if (arg === '--stop') {
        options.mode = 'stop';
      } else if (arg === '--record') {
        options.record = true;
      } else if (arg === '--assembly') {
        options.assembly = value();
      } else if (arg === '--settings') {
        options.settings = value();
      } else if (arg === '--results') {
        options.results = value();
      } else if (arg === '--beside') {
        options.beside.push(value());
      } else if (arg === '--share-timeout') {
        options.shareTimeout = Number(value());
        if (!Number.isFinite(options.shareTimeout) || options.shareTimeout <= 0) return { error: '--share-timeout is minutes, more than 0' };
      } else {
        return { error: `unknown argument ${arg}` };
      }
    } catch (error) {
      return { error: error.message };
    }
  }
  if (options.share && options.shares !== null) return { error: '--share names its own count; leave out --shares' };
  if (options.share && options.record) return { error: '--record needs a whole run, not one share' };
  if (options.share && options.beside.length > 0) return { error: '--beside runs beside a whole run, not one share' };
  if (options.mode !== 'run' && (options.share || options.record || options.beside.length > 0 || options.shares !== null)) {
    return { error: `--${options.mode} takes no run options` };
  }
  options.shares ??= defaultShares(cores);
  return options;
}

/** `i/n` (1-based) as `{ index, count }` with a 0-based index, `timed` as `{ timed: true }`, anything else null. */
export function parseShare(text) {
  if (text === 'timed') return { timed: true };
  const match = /^(\d+)\/(\d+)$/.exec(String(text));
  if (!match) return null;
  const index = Number(match[1]);
  const count = Number(match[2]);
  return index >= 1 && index <= count ? { index: index - 1, count } : null;
}

/** The label of a share: its folder name and how the summary names it. */
export function shareLabel(index, count) {
  return `share-${index + 1}-of-${count}`;
}

/** The seconds per class from the times file's text; a missing file is an empty map. */
export function readTimes(text) {
  if (text === null || text === undefined) return {};
  const parsed = JSON.parse(text);
  const classes = parsed?.classes;
  if (!classes || typeof classes !== 'object') throw new Error(`${TIMES_FILE} has no "classes" object`);
  for (const [name, seconds] of Object.entries(classes)) {
    if (typeof seconds !== 'number' || !Number.isFinite(seconds) || seconds < 0) throw new Error(`${TIMES_FILE}: ${name} has no number of seconds`);
  }
  return classes;
}

/** The times file's text for the seconds per class: sorted by name, to a tenth of a second. */
export function formatTimes(classes, shares) {
  const sorted = Object.fromEntries(
    Object.entries(classes)
      .sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0))
      .map(([name, seconds]) => [name, Math.max(0.1, Math.round(seconds * 10) / 10)]),
  );
  const document = {
    about:
      'Seconds per integration test class, which weigh the shares of scripts/test-integration.mjs (ADR-0261). ' +
      `Written by its --record from a run in ${shares} shares; a class missing here still runs, in the last share.`,
    classes: sorted,
  };
  return `${JSON.stringify(document, null, 2)}\n`;
}

/**
 * Shares the known classes out into `count` shares, heaviest first, each to the lightest share so far (the first of
 * equals). The same file gives the same plan everywhere, so the jobs of a CI matrix agree. The last share's own
 * classes are only its weight: it runs whatever the others do not name.
 */
export function planShares(times, count) {
  const shares = Array.from({ length: count }, () => ({ classes: [], seconds: 0 }));
  const classes = Object.entries(times).sort(([a, x], [b, y]) => y - x || (a < b ? -1 : a > b ? 1 : 0));
  for (const [name, seconds] of classes) {
    let lightest = shares[0];
    for (const share of shares) if (share.seconds < lightest.seconds) lightest = share;
    lightest.classes.push(name);
    lightest.seconds += seconds;
  }
  for (const share of shares) share.classes.sort();
  return shares;
}

const quoted = (name) => {
  if (/["\\\r\n]/.test(name)) throw new Error(`a class name the NUnit filter cannot quote: ${name}`);
  return `"${name}"`;
};

/**
 * The NUnit `Where` of a share. A share before the last runs its classes; the last runs every class the others do not
 * name. Neither runs the timed tests. An empty share before the last has no clause (null): it runs nothing.
 */
export function whereFor(plan, index) {
  const notTimed = `cat != ${TIMED_CATEGORY}`;
  if (index < plan.length - 1) {
    const own = plan[index].classes;
    return own.length === 0 ? null : `${notTimed} and (${own.map((name) => `class == ${quoted(name)}`).join(' or ')})`;
  }
  const named = plan.slice(0, -1).flatMap((share) => share.classes);
  return [notTimed, ...named.map((name) => `class != ${quoted(name)}`)].join(' and ');
}

/** The NUnit `Where` of the timed run. */
export function whereTimed() {
  return `cat == ${TIMED_CATEGORY}`;
}

const escapeXml = (text) => text.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;');

/**
 * Run settings for one process: the base file (or an empty one) with the NUnit `Where` put in. A `Where` the base
 * already has stays, joined with this one by `and`.
 */
export function mergeSettings(base, where) {
  const ours = escapeXml(where);
  if (base === null || base === undefined) {
    return `<?xml version="1.0" encoding="utf-8"?>\n<RunSettings>\n  <NUnit>\n    <Where>${ours}</Where>\n  </NUnit>\n</RunSettings>\n`;
  }
  const nunit = /<NUnit(\s[^>]*)?>([\s\S]*?)<\/NUnit>/.exec(base);
  if (nunit) {
    const inner = nunit[2];
    const existing = /<Where>([\s\S]*?)<\/Where>/.exec(inner);
    const merged = existing
      ? inner.replace(existing[0], () => `<Where>(${existing[1].trim()}) and (${ours})</Where>`)
      : `\n    <Where>${ours}</Where>${inner}`;
    return base.replace(nunit[0], () => `<NUnit${nunit[1] ?? ''}>${merged}</NUnit>`);
  }
  if (/<NUnit\s*\/>/.test(base)) return base.replace(/<NUnit\s*\/>/, () => `<NUnit>\n    <Where>${ours}</Where>\n  </NUnit>`);
  const end = base.lastIndexOf('</RunSettings>');
  if (end < 0) throw new Error('the base run settings have no </RunSettings>');
  return `${base.slice(0, end)}  <NUnit>\n    <Where>${ours}</Where>\n  </NUnit>\n${base.slice(end)}`;
}

const unescapeXml = (text) =>
  text
    .replace(/&#x([0-9a-f]+);/gi, (_, hex) => String.fromCodePoint(Number.parseInt(hex, 16)))
    .replace(/&#(\d+);/g, (_, dec) => String.fromCodePoint(Number(dec)))
    .replaceAll('&quot;', '"')
    .replaceAll('&apos;', "'")
    .replaceAll('&lt;', '<')
    .replaceAll('&gt;', '>')
    .replaceAll('&amp;', '&');

const attributesOf = (text) => Object.fromEntries([...text.matchAll(/([\w:]+)="([^"]*)"/g)].map((m) => [m[1], unescapeXml(m[2])]));

const secondsOf = (duration) => {
  const match = /^(\d+):(\d+):([\d.]+)$/.exec(duration ?? '');
  return match ? Number(match[1]) * 3600 + Number(match[2]) * 60 + Number(match[3]) : 0;
};

/** Every result of a trx file: `{ name, className, outcome, seconds, message }`. */
export function parseTrx(xml) {
  const classOf = new Map();
  for (const match of xml.matchAll(/<UnitTest\b([^>]*)>([\s\S]*?)<\/UnitTest>/g)) {
    const id = attributesOf(match[1]).id;
    const method = /<TestMethod\b([^>]*)\/?>/.exec(match[2]);
    if (id && method) classOf.set(id, attributesOf(method[1]).className ?? '');
  }
  const results = [];
  for (const match of xml.matchAll(/<UnitTestResult\b([^>]*?)(?:\/>|>([\s\S]*?)<\/UnitTestResult>)/g)) {
    const attributes = attributesOf(match[1]);
    const body = match[2] ?? '';
    const message = /<Message>([\s\S]*?)<\/Message>/.exec(body)?.[1] ?? /<StdOut>([\s\S]*?)<\/StdOut>/.exec(body)?.[1] ?? '';
    results.push({
      name: attributes.testName ?? '',
      className: classOf.get(attributes.testId) ?? '',
      outcome: attributes.outcome ?? 'None',
      seconds: secondsOf(attributes.duration),
      message: unescapeXml(message).trim().split(/\r?\n/)[0] ?? '',
    });
  }
  return results;
}

/**
 * What the tests of a trx file measured, as `<test>: <line>` each: the lines of their output that name a time in
 * milliseconds (`1885 ms`). The timed tests write what they measured there, among the lines their host logs, and the
 * console of `dotnet test` shows none of it; a hosted runner's times are read from the job's log (ADR-0264).
 */
export function outputLines(xml) {
  const lines = [];
  for (const match of xml.matchAll(/<UnitTestResult\b([^>]*?)(?:\/>|>([\s\S]*?)<\/UnitTestResult>)/g)) {
    const name = attributesOf(match[1]).testName ?? '';
    const out = /<StdOut>([\s\S]*?)<\/StdOut>/.exec(match[2] ?? '')?.[1];
    if (out === undefined) continue;
    for (const line of unescapeXml(out).split(/\r?\n/)) {
      if (/\d ms\b/.test(line)) lines.push(`${name}: ${line.trim()}`);
    }
  }
  return lines;
}

/** The test names `dotnet test <dll> --list-tests` prints, one per test, in its order. */
export function listedTests(output) {
  const lines = output.split(/\r?\n/);
  const start = lines.findIndex((line) => /The following Tests are available:/.test(line));
  if (start < 0) return null;
  return lines.slice(start + 1).filter((line) => line.startsWith('    ')).map((line) => line.slice(4));
}

/** A digest of a list of test names, the same whatever their order. */
export function digestOf(names) {
  return createHash('sha256').update([...names].sort().join('\n')).digest('hex');
}

/** The tests one list holds more often than another, by name: `{ missing, extra }`, each name as often as it differs. */
export function compareNames(holds, ran) {
  const counts = new Map();
  for (const name of holds) counts.set(name, (counts.get(name) ?? 0) + 1);
  for (const name of ran) counts.set(name, (counts.get(name) ?? 0) - 1);
  const missing = [];
  const extra = [];
  for (const [name, count] of counts) {
    for (let i = 0; i < count; i++) missing.push(name);
    for (let i = 0; i < -count; i++) extra.push(name);
  }
  return { missing: missing.sort(), extra: extra.sort() };
}

/** The seconds per class of a run's results. */
export function classTimes(results) {
  const times = {};
  for (const result of results) {
    if (!result.className) continue;
    times[result.className] = (times[result.className] ?? 0) + result.seconds;
  }
  return times;
}

/** The classes in results that the times file does not know, sorted. */
export function unknownClasses(results, times) {
  return [...new Set(results.map((result) => result.className).filter((name) => name && !(name in times)))].sort();
}

/** What a process's results come to: failed and skipped tests by name. */
export function outcomeOf(results) {
  return {
    failed: results.filter((result) => FAILED.has(result.outcome)),
    skipped: results.filter((result) => SKIPPED.has(result.outcome)),
  };
}

/**
 * The verdict of a whole run. `holds` is the assembly's test names; each run is `{ label, exit, timedOut, results }`.
 * Fails when a process failed, hung or did not exit cleanly, when a test failed, and unless the tests reported are
 * exactly the assembly's tests, each once.
 */
export function judge(holds, runs) {
  const problems = [];
  for (const run of runs) {
    const { failed } = outcomeOf(run.results);
    if (run.timedOut) problems.push(`${run.label} ran past its time and was stopped`);
    else if (run.exit !== 0) problems.push(`${run.label} failed (exit ${run.exit})`);
    for (const test of failed) problems.push(`${run.label}: ${test.outcome.toLowerCase()} ${test.className ? `${test.className}.` : ''}${test.name}${test.message ? `: ${test.message}` : ''}`);
  }
  const ran = runs.flatMap((run) => run.results.map((result) => result.name));
  const { missing, extra } = compareNames(holds, ran);
  if (ran.length !== holds.length || missing.length > 0 || extra.length > 0) {
    problems.push(`${ran.length} tests reported, but the assembly holds ${holds.length}`);
    for (const name of missing.slice(0, 20)) problems.push(`never ran: ${name}`);
    if (missing.length > 20) problems.push(`never ran: ${missing.length - 20} more`);
    for (const name of extra.slice(0, 20)) problems.push(`ran more often than the assembly holds it: ${name}`);
    if (extra.length > 20) problems.push(`ran more often than the assembly holds it: ${extra.length - 20} more`);
  }
  return { ok: problems.length === 0, problems, ran: ran.length };
}

/** The result file of one process, for --sum. */
export function resultFile({ share, exit, timedOut, seconds, holds, results, unknown = [] }) {
  const { failed, skipped } = outcomeOf(results);
  return {
    share,
    exit: timedOut ? null : exit,
    timedOut: Boolean(timedOut),
    seconds: Math.round(seconds),
    holds: { count: holds.length, digest: digestOf(holds) },
    ran: results.map((result) => result.name).sort(),
    failed: failed.map((test) => `${test.className ? `${test.className}.` : ''}${test.name}`),
    skipped: skipped.map((test) => test.name),
    unknownClasses: unknown,
  };
}

/**
 * Adds up the result files of a CI matrix: every share 1..n of one n, and the timed run, each once, all for the same
 * assembly, none failed, and together exactly the assembly's tests.
 */
export function sumResults(files) {
  const problems = [];
  if (files.length === 0) return { ok: false, problems: ['no result files'], ran: 0, holds: 0 };
  const holds = files[0].holds;
  for (const file of files) {
    if (file.holds?.count !== holds?.count || file.holds?.digest !== holds?.digest) {
      problems.push(`share ${file.share} ran another assembly (${file.holds?.count} tests where share ${files[0].share} has ${holds?.count})`);
    }
  }
  const seen = new Map();
  for (const file of files) seen.set(file.share, (seen.get(file.share) ?? 0) + 1);
  for (const [share, times] of seen) if (times > 1) problems.push(`share ${share} reported ${times} times`);
  const counts = new Set();
  for (const share of seen.keys()) {
    if (share === 'timed') continue;
    const parsed = parseShare(share);
    if (!parsed) problems.push(`a result names no share: ${share}`);
    else counts.add(parsed.count);
  }
  if (counts.size > 1) problems.push(`the shares do not agree on their number: ${[...counts].sort().join(', ')}`);
  const count = counts.size === 1 ? [...counts][0] : 0;
  for (let i = 1; i <= count; i++) if (!seen.has(`${i}/${count}`)) problems.push(`share ${i}/${count} did not report`);
  if (!seen.has('timed')) problems.push('the timed run did not report');
  for (const file of files) {
    if (file.timedOut) problems.push(`share ${file.share} ran past its time and was stopped`);
    else if (file.exit !== 0) problems.push(`share ${file.share} failed (exit ${file.exit})`);
    for (const name of file.failed ?? []) problems.push(`share ${file.share}: failed ${name}`);
  }
  const ran = files.flatMap((file) => file.ran ?? []);
  if (ran.length !== holds?.count || digestOf(ran) !== holds?.digest) {
    problems.push(`${ran.length} tests reported, but the assembly holds ${holds?.count}`);
    const twice = compareNames([...new Set(ran)], ran).extra;
    for (const name of [...new Set(twice)].slice(0, 20)) problems.push(`reported more than once: ${name}`);
  }
  return { ok: problems.length === 0, problems, ran: ran.length, holds: holds?.count ?? 0 };
}

/** Every result.json under a folder, as downloaded artifacts lay them out. */
export function findResultFiles(dir) {
  const found = [];
  const walk = (folder) => {
    for (const name of readdirSync(folder)) {
      const path = join(folder, name);
      if (statSync(path).isDirectory()) walk(path);
      else if (name === 'result.json') found.push(path);
    }
  };
  walk(dir);
  return found.sort();
}

/** An environment with these variables set, replacing any spelling of the same name (Windows ignores case). */
export function withEnv(base, overrides) {
  const env = {};
  const replaced = new Set(Object.keys(overrides).map((key) => key.toUpperCase()));
  for (const [key, value] of Object.entries(base)) if (!replaced.has(key.toUpperCase())) env[key] = value;
  return { ...env, ...overrides };
}

/** The environment of one share: its own temporary folder, which Umbraco's harness then works under. */
export function shareEnv(base, tmp) {
  return withEnv(base, { System_DefaultWorkingDirectory: '1', TMP: tmp, TEMP: tmp, TMPDIR: tmp });
}

/** The arguments of one `dotnet test` process. */
export function testArguments({ assembly, settings, label, results, passThrough = [] }) {
  return [
    'test',
    assembly,
    ...(settings ? ['--settings', settings] : []),
    '--logger',
    `trx;LogFileName=${label}.trx`,
    '--results-directory',
    results,
    '--blame-hang',
    '--blame-hang-timeout',
    HANG_TIMEOUT,
    '--blame-hang-dump-type',
    'none',
    '--nologo',
    ...passThrough,
  ];
}

/** The summary's lines. Each row: `{ label, tests, failed, skipped, seconds, planned, exit, timedOut }`. */
export function summaryLines(rows, verdict, holds, wall, share = null) {
  const lines = ['', 'test-integration:   process                tests  failed  skipped  seconds  planned'];
  for (const row of rows) {
    const state = row.timedOut ? 'stopped' : row.exit === 0 ? '' : `exit ${row.exit}`;
    lines.push(
      `test-integration:   ${row.label.padEnd(22)} ${String(row.tests ?? '-').padStart(5)}  ${String(row.failed ?? '-').padStart(6)}  ${String(row.skipped ?? '-').padStart(7)}  ${String(Math.round(row.seconds)).padStart(7)}  ${row.planned === undefined ? '      -' : String(Math.round(row.planned)).padStart(7)}  ${state}`.trimEnd(),
    );
  }
  lines.push('');
  for (const problem of verdict.problems) lines.push(`test-integration: ${problem}`);
  const counted = share
    ? `share ${share}: ${verdict.ran} tests ran (the assembly holds ${holds}; --sum adds the shares up)`
    : `${verdict.ran} of ${holds} tests ran`;
  lines.push(verdict.ok ? `test-integration: ${counted}, none failed, in ${Math.round(wall)} s` : `test-integration: FAILED: ${counted}, in ${Math.round(wall)} s`);
  return lines;
}

// ---------------------------------------------------------------------------------------------------------------
// Processes: everything below runs dotnet and is checked by the runs themselves, not by the script's tests.
// ---------------------------------------------------------------------------------------------------------------

/** Start times of processes (Windows), as `Map<pid, iso>`; empty elsewhere. */
function startTimes(pids) {
  if (!isWindows || pids.length === 0) return new Map();
  const script = `Get-Process -Id ${pids.map(Number).join(',')} -ErrorAction SilentlyContinue | ForEach-Object { '{0} {1}' -f $_.Id, $_.StartTime.ToUniversalTime().ToString('o') }`;
  const result = spawnSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', script], { encoding: 'utf8' });
  return new Map(
    (result.stdout ?? '')
      .split(/\r?\n/)
      .map((line) => line.trim().split(' '))
      .filter((parts) => parts.length === 2)
      .map(([pid, started]) => [Number(pid), started]),
  );
}

/** Whether a recorded process still runs and is the one recorded (its start time tells a recycled PID apart). */
function stillOurs(entry) {
  if (!entry || !Number.isInteger(entry.pid)) return false;
  if (isWindows) return entry.startedAt !== undefined && startTimes([entry.pid]).get(entry.pid) === entry.startedAt;
  try {
    process.kill(entry.pid, 0);
    return true;
  } catch {
    return false;
  }
}

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

class Runner {
  constructor(options) {
    this.options = options;
    this.state = resolve(repo, STATE);
    this.results = resolve(repo, options.results);
    this.pidFile = join(this.state, 'run.json');
    this.running = new Map();
    this.record = { pid: process.pid, startedAt: startTimes([process.pid]).get(process.pid), children: [] };
    this.stream = options.share !== null;
  }

  claim() {
    if (existsSync(this.pidFile)) {
      let previous = null;
      try {
        previous = JSON.parse(readFileSync(this.pidFile, 'utf8'));
      } catch {
        previous = null;
      }
      if (previous && previous.pid !== process.pid && stillOurs(previous)) {
        throw new Error(`another run (PID ${previous.pid}) is going; wait for it, or stop it: node scripts/test-integration.mjs --stop`);
      }
    }
    mkdirSync(this.state, { recursive: true });
    this.saveRecord();
    const stop = (signal) => {
      console.error(`test-integration: ${signal}; stopping every process this run started`);
      this.stopAll();
      process.exit(130);
    };
    for (const signal of ['SIGINT', 'SIGTERM', 'SIGHUP', 'SIGBREAK']) {
      if (signal in constants.signals) process.on(signal, () => stop(signal));
    }
  }

  saveRecord() {
    writeFileSync(this.pidFile, `${JSON.stringify(this.record, null, 2)}\n`);
  }

  release() {
    rmSync(this.pidFile, { force: true });
  }

  stopAll() {
    for (const child of this.running.values()) killTree(child.pid);
    this.release();
  }

  folders(label) {
    const work = join(this.state, 'work', label);
    const results = join(this.results, label);
    rmSync(work, { recursive: true, force: true });
    rmSync(results, { recursive: true, force: true });
    mkdirSync(join(work, 'tmp'), { recursive: true });
    mkdirSync(results, { recursive: true });
    return { work, results, tmp: join(work, 'tmp') };
  }

  /** Runs one `dotnet test`; resolves `{ label, exit, timedOut, seconds, results }`. */
  start({ label, assembly, where, isolated = true }) {
    const folders = this.folders(label);
    let settings = this.options.settings ? resolve(repo, this.options.settings) : null;
    if (where !== undefined) {
      const base = settings ? readFileSync(settings, 'utf8') : null;
      settings = join(folders.work, `${label}.runsettings`);
      writeFileSync(settings, mergeSettings(base, where));
    }
    const args = testArguments({ assembly, settings, label, results: folders.results, passThrough: this.options.passThrough });
    const env = isolated ? shareEnv(process.env, folders.tmp) : { ...process.env };
    const log = createWriteStream(join(folders.results, 'console.log'));
    const began = Date.now();
    const child = spawn('dotnet', args, { cwd: repo, env, stdio: ['ignore', 'pipe', 'pipe'], detached: !isWindows, windowsHide: true });
    this.running.set(label, child);
    this.record.children.push({ label, pid: child.pid });
    for (const stream of [child.stdout, child.stderr]) {
      stream.on('data', (chunk) => {
        log.write(chunk);
        if (this.stream) process.stdout.write(chunk);
      });
    }
    let timedOut = false;
    const timer = setTimeout(
      () => {
        timedOut = true;
        console.error(`test-integration: ${label} ran past ${this.options.shareTimeout} minutes; stopping its process tree`);
        killTree(child.pid);
      },
      this.options.shareTimeout * 60 * 1000,
    );
    return new Promise((done) => {
      const finish = (exit) => {
        clearTimeout(timer);
        this.running.delete(label);
        log.end();
        const seconds = (Date.now() - began) / 1000;
        const results = this.readResults(folders.results, label);
        if (exit === 0 && !timedOut) rmSync(folders.work, { recursive: true, force: true });
        console.log(`test-integration: ${label} ended (${timedOut ? 'stopped' : `exit ${exit}`}) after ${Math.round(seconds)} s with ${results.length} test(s); log ${relative(repo, join(folders.results, 'console.log'))}`);
        done({ label, exit, timedOut, seconds, results, folder: folders.results });
      };
      child.on('error', (error) => {
        log.write(`\n${error.message}\n`);
        finish(-1);
      });
      child.on('close', (code) => finish(code ?? -1));
    });
  }

  /** Records the start time of every child so far, so --stop never touches a recycled PID. */
  noteStartTimes() {
    const times = startTimes(this.record.children.filter((child) => child.startedAt === undefined).map((child) => child.pid));
    for (const child of this.record.children) if (child.startedAt === undefined && times.has(child.pid)) child.startedAt = times.get(child.pid);
    this.saveRecord();
  }

  readResults(folder, label) {
    const trx = join(folder, `${label}.trx`);
    return existsSync(trx) ? parseTrx(readFileSync(trx, 'utf8')) : [];
  }

  /** Prints what the tests of a run measured, from their output. */
  printOutput(folder, label) {
    const trx = join(folder, `${label}.trx`);
    if (!existsSync(trx)) return;
    for (const line of outputLines(readFileSync(trx, 'utf8'))) console.log(`test-integration: ${label}: ${line}`);
  }
}

function listTests(assembly) {
  const result = spawnSync('dotnet', ['test', assembly, '--list-tests', '--nologo'], { cwd: repo, encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 });
  const names = result.status === 0 ? listedTests(result.stdout ?? '') : null;
  if (!names) throw new Error(`could not list the assembly's tests (exit ${result.status}):\n${result.stdout ?? ''}${result.stderr ?? ''}`);
  return names;
}

function readTimesFile() {
  const path = join(repo, TIMES_FILE);
  return readTimes(existsSync(path) ? readFileSync(path, 'utf8') : null);
}

function rowOf(run, planned) {
  const { failed, skipped } = outcomeOf(run.results);
  return { label: run.label, tests: run.results.length, failed: failed.length, skipped: skipped.length, seconds: run.seconds, planned, exit: run.exit, timedOut: run.timedOut };
}

function printSkipped(runs) {
  const skipped = runs.flatMap((run) => outcomeOf(run.results).skipped.map((test) => ({ run: run.label, test })));
  if (skipped.length === 0) return;
  console.log(`test-integration: ${skipped.length} test(s) were reached but skipped while they ran (they count as reached):`);
  for (const { run, test } of skipped.slice(0, 30)) console.log(`test-integration:   ${run}: ${test.name}${test.message ? `: ${test.message}` : ''}`);
  if (skipped.length > 30) console.log(`test-integration:   and ${skipped.length - 30} more`);
}

async function runAll(options) {
  const assembly = resolve(repo, options.assembly);
  if (!existsSync(assembly)) {
    console.error(`test-integration: the integration assembly is missing: ${relative(repo, assembly)}. This script never builds; build first: dotnet build -c Release`);
    return 1;
  }
  const beside = options.beside.map((path) => resolve(repo, path));
  for (const path of beside) {
    if (!existsSync(path)) {
      console.error(`test-integration: the assembly to run beside is missing: ${relative(repo, path)}. This script never builds; build first: dotnet build -c Release`);
      return 1;
    }
  }
  if (options.settings && !existsSync(resolve(repo, options.settings))) {
    console.error(`test-integration: the run settings are missing: ${options.settings}`);
    return 1;
  }

  const began = Date.now();
  const runner = new Runner(options);
  try {
    runner.claim();
  } catch (error) {
    console.error(`test-integration: ${error.message}`);
    return 1;
  }
  try {
    const holds = listTests(assembly);
    const times = readTimesFile();

    if (options.share) return await runOneShare(runner, options, assembly, holds, times, began);

    const count = options.shares;
    const plan = planShares(times, count);
    // A whole run replaces every earlier process's folders, so nothing stale is read or merged.
    rmSync(join(runner.state, 'work'), { recursive: true, force: true });
    if (existsSync(runner.results)) {
      for (const name of readdirSync(runner.results)) {
        if (/^(share-\d+-of-\d+|timed|beside-.+)$/.test(name)) rmSync(join(runner.results, name), { recursive: true, force: true });
      }
    }
    console.log(`test-integration: ${holds.length} tests in ${relative(repo, assembly)}; ${count} share(s) at once${beside.length ? `, ${beside.length} assembly(ies) beside them` : ''}, then the timed tests alone`);

    const pending = [];
    for (let index = 0; index < count; index++) {
      const where = whereFor(plan, index);
      if (where === null) continue;
      pending.push(runner.start({ label: shareLabel(index, count), assembly, where }).then((run) => ({ ...run, planned: plan[index].seconds, share: true })));
    }
    for (const path of beside) {
      const label = `beside-${basename(path, '.dll').replace(/^.*\.Tests\./, '')}`;
      pending.push(runner.start({ label, assembly: path, isolated: false }).then((run) => ({ ...run, beside: true })));
    }
    runner.noteStartTimes();
    const first = await Promise.all(pending);

    // Nothing else runs now: the timed tests alone.
    const timedPending = runner.start({ label: 'timed', assembly, where: whereTimed() });
    runner.noteStartTimes();
    const timed = await timedPending;
    runner.printOutput(timed.folder, 'timed');

    const shares = first.filter((run) => run.share);
    const besides = first.filter((run) => run.beside);
    const integration = [...shares, timed];
    const verdict = judge(holds, integration);
    for (const run of besides) {
      const { failed } = outcomeOf(run.results);
      if (run.timedOut) verdict.problems.push(`${run.label} ran past its time and was stopped`);
      else if (run.exit !== 0) verdict.problems.push(`${run.label} failed (exit ${run.exit})`);
      for (const test of failed) verdict.problems.push(`${run.label}: ${test.outcome.toLowerCase()} ${test.className ? `${test.className}.` : ''}${test.name}${test.message ? `: ${test.message}` : ''}`);
    }
    verdict.ok = verdict.problems.length === 0;

    const lastLabel = shareLabel(count - 1, count);
    const last = shares.find((run) => run.label === lastLabel);
    const unknown = last ? unknownClasses(last.results, times) : [];
    printSkipped(integration);
    for (const line of summaryLines([...integration.map((run) => rowOf(run, run.planned)), ...besides.map((run) => rowOf(run))], verdict, holds.length, (Date.now() - began) / 1000)) {
      (verdict.ok ? console.log : console.error)(line);
    }
    if (unknown.length > 0) {
      console.log(`test-integration: the last share ran ${unknown.length} class(es) ${TIMES_FILE} does not know: ${unknown.slice(0, 10).join(', ')}${unknown.length > 10 ? ', ...' : ''}. Record the times again: node scripts/test-integration.mjs --record`);
    }
    if (options.record) {
      if (!verdict.ok) console.error(`test-integration: ${TIMES_FILE} is left as it was, since the run failed`);
      else {
        writeFileSync(join(repo, TIMES_FILE), formatTimes(classTimes(shares.flatMap((run) => run.results)), count));
        console.log(`test-integration: wrote ${TIMES_FILE} from this run's ${count} share(s)`);
      }
    }
    return verdict.ok ? 0 : 1;
  } catch (error) {
    console.error(`test-integration: ${error.message}`);
    runner.stopAll();
    return 1;
  } finally {
    runner.release();
  }
}

async function runOneShare(runner, options, assembly, holds, times, began) {
  const { share } = options;
  let run;
  let shareName;
  let unknown = [];
  let planned;
  if (share.timed) {
    shareName = 'timed';
    run = runner.start({ label: 'timed', assembly, where: whereTimed() });
  } else {
    const plan = planShares(times, share.count);
    const where = whereFor(plan, share.index);
    shareName = `${share.index + 1}/${share.count}`;
    planned = plan[share.index].seconds;
    const label = shareLabel(share.index, share.count);
    if (where === null) {
      console.log(`test-integration: share ${shareName} has no class in ${TIMES_FILE}; it runs nothing`);
      run = Promise.resolve({ label, exit: 0, timedOut: false, seconds: 0, results: [], folder: runner.folders(label).results });
    } else {
      console.log(`test-integration: share ${shareName} of the ${holds.length} tests in ${relative(repo, assembly)}`);
      run = runner.start({ label, assembly, where });
    }
  }
  runner.noteStartTimes();
  const done = await run;
  if (share.timed) runner.printOutput(done.folder, 'timed');
  if (share.index === share.count - 1) unknown = unknownClasses(done.results, times);
  const file = resultFile({ share: shareName, exit: done.exit, timedOut: done.timedOut, seconds: done.seconds, holds, results: done.results, unknown });
  writeFileSync(join(done.folder, 'result.json'), `${JSON.stringify(file, null, 2)}\n`);
  const { failed } = outcomeOf(done.results);
  const problems = [];
  if (done.timedOut) problems.push(`share ${shareName} ran past its time and was stopped`);
  else if (done.exit !== 0) problems.push(`share ${shareName} failed (exit ${done.exit})`);
  for (const test of failed) problems.push(`share ${shareName}: ${test.outcome.toLowerCase()} ${test.className ? `${test.className}.` : ''}${test.name}${test.message ? `: ${test.message}` : ''}`);
  if (done.results.length === 0 && done.exit === 0 && !share.timed && planned > 0) problems.push(`share ${shareName} reported no test`);
  printSkipped([done]);
  for (const line of summaryLines([rowOf(done, planned)], { ok: problems.length === 0, problems, ran: done.results.length }, holds.length, (Date.now() - began) / 1000, shareName)) {
    (problems.length === 0 ? console.log : console.error)(line);
  }
  if (unknown.length > 0) console.log(`test-integration: this last share ran ${unknown.length} class(es) ${TIMES_FILE} does not know; record the times again.`);
  console.log(`test-integration: wrote ${relative(repo, join(done.folder, 'result.json'))}; the sum of every share decides the count (--sum)`);
  return problems.length === 0 ? 0 : 1;
}

function sum(dir) {
  const folder = resolve(repo, dir);
  const paths = existsSync(folder) ? findResultFiles(folder) : [];
  const files = paths.map((path) => JSON.parse(readFileSync(path, 'utf8')));
  const verdict = sumResults(files);
  for (const file of [...files].sort((a, b) => String(a.share).localeCompare(String(b.share), 'en', { numeric: true }))) {
    console.log(`test-integration:   share ${String(file.share).padEnd(7)} ${String(file.ran?.length ?? 0).padStart(5)} tests  ${String(file.failed?.length ?? 0).padStart(3)} failed  ${String(file.seconds).padStart(5)} s${file.unknownClasses?.length ? `  (${file.unknownClasses.length} class(es) the times file does not know)` : ''}`);
  }
  if (verdict.ok) console.log(`test-integration: ${files.length} result file(s) under ${dir}: ${verdict.ran} of ${verdict.holds} tests ran, each once, none failed`);
  else {
    for (const problem of verdict.problems) console.error(`test-integration: ${problem}`);
    console.error(`test-integration: FAILED: ${files.length} result file(s) under ${dir}`);
  }
  return verdict.ok ? 0 : 1;
}

function stop() {
  const pidFile = join(repo, STATE, 'run.json');
  if (!existsSync(pidFile)) {
    console.log('test-integration: no run is recorded');
    return 0;
  }
  const record = JSON.parse(readFileSync(pidFile, 'utf8'));
  let stopped = 0;
  for (const entry of [record, ...(record.children ?? [])]) {
    if (stillOurs(entry)) {
      killTree(entry.pid);
      stopped++;
    }
  }
  rmSync(pidFile, { force: true });
  console.log(stopped === 0 ? 'test-integration: none of its processes runs any more' : `test-integration: stopped ${stopped} process tree(s)`);
  return 0;
}

async function main(argv) {
  const options = parseArgs(argv);
  if (options.error) {
    console.error(`test-integration: ${options.error}`);
    return 2;
  }
  if (options.mode === 'sum') return sum(options.sum);
  if (options.mode === 'stop') return stop();
  return runAll(options);
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  process.exitCode = await main(process.argv.slice(2));
}
