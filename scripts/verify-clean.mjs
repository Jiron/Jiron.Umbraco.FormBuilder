#!/usr/bin/env node
// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
/**
 * The clean-clone verification of ROADMAP 20.1. Origin's `main` is cloned into `%TEMP%\jfb-verify-<stamp>`, outside
 * the repository, so no parent NuGet.config or node_modules reaches it, with a NuGet cache of its own in
 * `%TEMP%\jfb-nuget-<stamp>` and the throwaway-build settings of CLAUDE.md rule 6. In it, in order:
 *
 *   1. scripts/ci-local.ps1: restore, build, the unit and integration tests, the vectors in both runtimes, the
 *      client's lint, tests and build, and the full Playwright suite as its browser stage, with the one rerun the hang
 *      marker allows (ADR-0011);
 *   2. a pack of a version of its own, `<VersionPrefix>-dev.<yyyyMMddHHmm>` (scripts/pack.mjs);
 *   3. the install test on the line's Umbraco and on its floor (18.3);
 *   4. the upgrade, with data, from the schema of the first prerelease, 17.0.0-alpha.1 (18.4, through 16.5's harness);
 *   5. the secret scan of the whole history.
 *
 * Then the term guard of the whole history runs here, where the owner's private folder is, which a clone lacks. The
 * first step that fails ends the run; each step's time and verdict are printed in a summary, and both temporary
 * folders are removed at the end, whatever the outcome, unless --keep. Logs go to `.local/verify/<stamp>/`.
 *
 *   node scripts/verify-clean.mjs [--keep]
 *
 * Exit codes: 0 every step passed, 1 a step failed or could not start, 2 usage.
 */
import { spawnSync } from 'node:child_process';
import { closeSync, existsSync, mkdirSync, openSync, readFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { browserPorts, removeTree } from './gate-clone.mjs';
import { BUILD_ENV, portOpen } from './sandbox.mjs';

/** The options, or an error. */
export function parseArgs(argv) {
  let keep = false;
  for (const arg of argv) {
    if (arg === '--keep') keep = true;
    else return { error: `unknown argument ${arg}` };
  }
  return { keep };
}

/** A run's stamp: the time in UTC to the minute, as a local test pack's version names it (D16). */
export function stampOf(date) {
  const two = (value) => String(value).padStart(2, '0');
  return (
    `${date.getUTCFullYear()}${two(date.getUTCMonth() + 1)}${two(date.getUTCDate())}` +
    `${two(date.getUTCHours())}${two(date.getUTCMinutes())}`
  );
}

/** Every path a run uses: the clone and the NuGet cache under the temporary folder, the logs under .local/verify. */
export function verifyPaths(root, stamp, temp = tmpdir()) {
  return {
    clone: join(temp, `jfb-verify-${stamp}`),
    cache: join(temp, `jfb-nuget-${stamp}`),
    logs: join(root, '.local', 'verify', stamp),
  };
}

const powershell = (script, ...args) => [
  'powershell.exe',
  ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', join('scripts', script), ...args],
];

/** The steps in their order: a name, the command and where it runs (`clone` or `here`). */
export function stepsOf() {
  return [
    { name: 'ci-local', where: 'clone', command: powershell('ci-local.ps1') },
    { name: 'pack', where: 'clone', command: ['node', [join('scripts', 'pack.mjs'), '--output', join('.local', 'verify-pack')]] },
    { name: 'install', where: 'clone', command: powershell('install-test.ps1', '-Mode', 'install') },
    { name: 'floor', where: 'clone', command: powershell('install-test.ps1', '-Mode', 'floor') },
    { name: 'upgrade', where: 'clone', command: powershell('install-test.ps1', '-Mode', 'upgrade') },
    { name: 'secret-scan', where: 'clone', command: ['node', [join('scripts', 'secret-scan.mjs'), '--history']] },
    { name: 'term-guard', where: 'here', command: ['node', [join('scripts', 'term-guard.mjs'), '--history']] },
  ];
}

/** The last line of a log that is not empty: a step's own verdict. */
export function lastLineOf(log) {
  return (
    log
      .split(/\r?\n/)
      .map((line) => line.trim())
      .filter((line) => line.length > 0)
      .at(-1) ?? ''
  );
}

/** The summary: one line a step, its name, its result, its seconds and its verdict, and the run's own verdict. */
export function summaryOf(results) {
  const lines = results.map(
    (result) =>
      `verify-clean: ${result.name.padEnd(12)} ${result.code === 0 ? 'passed' : 'FAILED'} ${String(result.seconds).padStart(6)} s  ${result.verdict}`,
  );
  const failed = results.find((result) => result.code !== 0);
  lines.push(
    failed === undefined
      ? `verify-clean: every step passed in ${results.reduce((sum, result) => sum + result.seconds, 0)} s`
      : `verify-clean: FAILED at ${failed.name}`,
  );
  return lines;
}

const git = (cwd, args, env) => spawnSync('git', ['-C', cwd, ...args], { encoding: 'utf8', env });

async function run(root, { keep }) {
  const ports = browserPorts(readFileSync(join(root, 'tests', 'e2e', 'support', 'sites.ts'), 'utf8'));
  const taken = (await Promise.all(ports.map((port) => portOpen(port)))).flatMap((open, at) => (open ? [ports[at]] : []));
  if (taken.length > 0) {
    console.error(`verify-clean: port ${taken.join(', ')} is taken; the browser projects need it free`);
    return 1;
  }
  const stamp = stampOf(new Date());
  const paths = verifyPaths(root, stamp);
  const origin = git(root, ['remote', 'get-url', 'origin']).stdout.trim();
  if (origin === '') {
    console.error('verify-clean: the repository names no origin');
    return 1;
  }
  const env = { ...process.env, ...BUILD_ENV, UseSharedCompilation: 'false', NUGET_PACKAGES: paths.cache, GIT_TERMINAL_PROMPT: '0' };
  mkdirSync(paths.logs, { recursive: true });
  const results = [];
  try {
    const cloned = spawnSync('git', ['clone', '-q', '--branch', 'main', origin, paths.clone], { encoding: 'utf8', env });
    if (cloned.status !== 0) {
      console.error(`verify-clean: the clone of origin's main failed: ${cloned.stderr.trim()}`);
      return 1;
    }
    const head = git(paths.clone, ['rev-parse', 'HEAD'], env).stdout.trim();
    console.log(`verify-clean: origin's main at ${head.slice(0, 8)} in ${paths.clone}, NuGet cache ${paths.cache}`);
    for (const step of stepsOf()) {
      const log = join(paths.logs, `${step.name}.log`);
      const out = openSync(log, 'w');
      const started = Date.now();
      let code = 1;
      try {
        const [file, args] = step.command;
        const result = spawnSync(file, args, { cwd: step.where === 'clone' ? paths.clone : root, stdio: ['ignore', out, out], env });
        code = result.status ?? 1;
      } finally {
        closeSync(out);
      }
      const seconds = Math.round((Date.now() - started) / 1000);
      const verdict = existsSync(log) ? lastLineOf(readFileSync(log, 'utf8')) : '';
      results.push({ name: step.name, code, seconds, verdict });
      console.log(`verify-clean: ${step.name} ${code === 0 ? 'passed' : 'FAILED'} in ${seconds} s, log ${log}`);
      if (code !== 0) break;
    }
  } finally {
    if (!keep) {
      removeTree(paths.clone);
      removeTree(paths.cache);
      console.log(
        `verify-clean: the temporary folders are ${existsSync(paths.clone) || existsSync(paths.cache) ? 'NOT all removed' : 'removed'}`,
      );
    }
  }
  for (const line of summaryOf(results)) console.log(line);
  return results.length === stepsOf().length && results.every((result) => result.code === 0) ? 0 : 1;
}

if (import.meta.url === pathToFileURL(process.argv[1] ?? '').href) {
  const parsed = parseArgs(process.argv.slice(2));
  if (parsed.error) {
    console.error(`verify-clean: ${parsed.error}; usage: node scripts/verify-clean.mjs [--keep]`);
    process.exit(2);
  }
  process.exit(await run(fileURLToPath(new URL('..', import.meta.url)), parsed));
}
