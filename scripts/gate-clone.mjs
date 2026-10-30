#!/usr/bin/env node
// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
/**
 * Runs the gate on a commit in a throwaway clone, so the next step can be built in this folder meanwhile (the owner,
 * 2026-10-30; ADR-0117). The clone goes to `%TEMP%\jfb-gate-<short hash>`, `scripts\ci-local.ps1` runs there with the
 * throwaway-build settings, and the clone is removed afterwards, its workspace links one by one first, so no recursive
 * delete ever walks into a link's target.
 *
 *   node scripts/gate-clone.mjs run [<commit>] [--keep]   gate the commit (HEAD by default); --keep leaves the clone
 *   node scripts/gate-clone.mjs status                     say whether a gate runs, and the last one's verdict
 *   node scripts/gate-clone.mjs stop                       stop the gate this script started, its whole process tree
 *
 * The log goes to `.local/gate/<short hash>.log` and the verdict to `.local/gate/last.json`; a failed gate's browser
 * results are copied to `.local/gate/<short hash>-e2e/`. Only one gate runs at a time, and none starts while one of
 * the browser projects' ports is taken (they are fixed, `tests/e2e/support/paths.ts`), so this folder's own Playwright
 * runs wait for the clone's gate. The PID file records the start time, so a recycled PID that now belongs to another
 * session is never touched (CLAUDE.md rule 6).
 *
 * Exit codes: the gate's (0 passed), 1 failure, 2 usage error.
 */
import { spawnSync } from 'node:child_process';
import {
  closeSync,
  cpSync,
  existsSync,
  lstatSync,
  mkdirSync,
  openSync,
  readdirSync,
  readFileSync,
  rmdirSync,
  rmSync,
  unlinkSync,
  writeFileSync,
} from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { BUILD_ENV, isOurProcess, portOpen, processStartTime, readPidRecord } from './sandbox.mjs';

export const COMMANDS = ['run', 'status', 'stop'];

/** The command and its options, or an error. */
export function parseArgs(argv) {
  const [command, ...rest] = argv;
  if (!COMMANDS.includes(command))
    return { error: `unknown command ${command ?? '(none)'}; use one of ${COMMANDS.join(', ')}` };
  let commit = 'HEAD';
  let keep = false;
  let named = false;
  for (const arg of rest) {
    if (command === 'run' && arg === '--keep') keep = true;
    else if (command === 'run' && !named && !arg.startsWith('-')) [commit, named] = [arg, true];
    else return { error: `unknown argument ${arg}` };
  }
  return { command, commit, keep };
}

/** Every path a gate uses: its state under `.local/gate/` and the clone of a commit under the temporary folder. */
export function gatePaths(root, hash = '', temp = tmpdir()) {
  const state = join(root, '.local', 'gate');
  const short = hash.slice(0, 7);
  return {
    state,
    pidFile: join(state, 'running.json'),
    lastFile: join(state, 'last.json'),
    log: join(state, `${short}.log`),
    results: join(state, `${short}-e2e`),
    clone: join(temp, `jfb-gate-${short}`),
  };
}

/**
 * The ports the browser projects listen on, read from the harness's own constants ("One source for every value"): every
 * `<NAME>_PORT` and `<NAME>_PORTS` it exports, such as the cached host's of 6.4k; its `PORT_RANGE` is no port.
 */
export function browserPorts(pathsSource) {
  const ports = [...pathsSource.matchAll(/^export const [A-Z0-9]+(?:_[A-Z0-9]+)*_PORTS? = ([^;]+);/gm)].flatMap(
    ([, value]) => [...value.matchAll(/\b(\d{4})\b/g)].map(([, port]) => Number(port)),
  );
  if (ports.length === 0) throw new Error("tests/e2e/support/paths.ts no longer names the browser projects' ports");
  return ports;
}

/** The gate's verdict from its log: the last line ci-local.ps1 writes about itself, or null when it wrote none. */
export function verdictOf(log) {
  const lines = log.split(/\r?\n/).filter((line) => line.startsWith('ci-local: '));
  return lines.at(-1) ?? null;
}

/** The links below a folder (npm's workspace junctions among them), never walking into one. */
export function linksUnder(folder) {
  const found = [];
  const walk = (dir) => {
    for (const entry of readdirSync(dir, { withFileTypes: true })) {
      if (!entry.isDirectory() && !entry.isSymbolicLink()) continue;
      const path = join(dir, entry.name);
      if (lstatSync(path).isSymbolicLink()) found.push(path);
      else walk(path);
    }
  };
  if (existsSync(folder)) walk(folder);
  return found;
}

/** Removes a folder: its links first, each on its own, so the recursive delete never reaches a link's target. */
export function removeTree(folder) {
  for (const link of linksUnder(folder)) {
    try {
      rmdirSync(link);
    } catch {
      unlinkSync(link);
    }
  }
  rmSync(folder, { recursive: true, force: true, maxRetries: 5, retryDelay: 500 });
}

const git = (root, args) => spawnSync('git', ['-C', root, ...args], { encoding: 'utf8' });

function run(root, { commit, keep }) {
  const resolved = git(root, ['rev-parse', '--verify', '--quiet', `${commit}^{commit}`]);
  const hash = resolved.stdout.trim();
  if (resolved.status !== 0 || !/^[0-9a-f]{40}$/.test(hash)) {
    console.error(`gate-clone: ${commit} is not a commit here`);
    return 1;
  }
  const paths = gatePaths(root, hash);
  if (isOurProcess(readPidRecord(paths.pidFile))) {
    console.error('gate-clone: a gate runs already; wait for it, or stop it with "node scripts/gate-clone.mjs stop"');
    return 1;
  }
  return checkPorts(root).then((taken) => {
    if (taken.length > 0) {
      console.error(`gate-clone: port ${taken.join(', ')} is taken; the gate's browser projects need it free`);
      return 1;
    }
    return gate(root, hash, paths, keep);
  });
}

async function checkPorts(root) {
  const ports = browserPorts(readFileSync(join(root, 'tests', 'e2e', 'support', 'paths.ts'), 'utf8'));
  const open = await Promise.all(ports.map((port) => portOpen(port)));
  return ports.filter((_, at) => open[at]);
}

function gate(root, hash, paths, keep) {
  const short = hash.slice(0, 7);
  mkdirSync(paths.state, { recursive: true });
  writeFileSync(
    paths.pidFile,
    JSON.stringify({ pid: process.pid, startedAt: processStartTime(process.pid), commit: hash, clone: paths.clone }) +
      '\n',
    'utf8',
  );
  const started = Date.now();
  let code = 1;
  try {
    removeTree(paths.clone);
    const cloned = spawnSync('git', ['clone', '-q', '--no-checkout', root, paths.clone], { encoding: 'utf8' });
    const checkedOut = cloned.status === 0 ? git(paths.clone, ['checkout', '-q', '--detach', hash]) : cloned;
    if (checkedOut.status !== 0) {
      console.error(`gate-clone: the clone of ${short} failed: ${checkedOut.stderr.trim()}`);
      return 1;
    }
    console.log(`gate-clone: ${short} in ${paths.clone}, log ${paths.log}`);
    const log = openSync(paths.log, 'w');
    try {
      const result = spawnSync(
        'powershell.exe',
        ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', join('scripts', 'ci-local.ps1')],
        {
          cwd: paths.clone,
          stdio: ['ignore', log, log],
          env: { ...process.env, ...BUILD_ENV, UseSharedCompilation: 'false' },
        },
      );
      code = result.status ?? 1;
    } finally {
      closeSync(log);
    }
    const results = join(paths.clone, '.local', 'e2e', 'results');
    if (code !== 0 && existsSync(results)) {
      rmSync(paths.results, { recursive: true, force: true });
      cpSync(results, paths.results, { recursive: true });
    }
  } finally {
    const verdict = existsSync(paths.log) ? verdictOf(readFileSync(paths.log, 'utf8')) : null;
    const seconds = Math.round((Date.now() - started) / 1000);
    const last = { commit: hash, exit: code, seconds, verdict, finishedAt: new Date().toISOString() };
    writeFileSync(paths.lastFile, JSON.stringify(last, null, 2) + '\n', 'utf8');
    console.log(
      `gate-clone: ${short} ${code === 0 ? 'passed' : `FAILED (exit ${code})`} in ${seconds} s: ${verdict ?? 'no verdict in the log'}`,
    );
    if (!keep) removeTree(paths.clone);
    rmSync(paths.pidFile, { force: true });
  }
  return code;
}

function status(root) {
  const paths = gatePaths(root);
  const record = readPidRecord(paths.pidFile);
  console.log(
    isOurProcess(record)
      ? `gate-clone: ${record.commit.slice(0, 7)} runs since ${record.startedAt}`
      : 'gate-clone: none runs',
  );
  if (existsSync(paths.lastFile)) {
    const last = JSON.parse(readFileSync(paths.lastFile, 'utf8'));
    console.log(
      `gate-clone: last ${last.commit.slice(0, 7)} exit ${last.exit} in ${last.seconds} s: ${last.verdict ?? 'no verdict'}`,
    );
  }
  return 0;
}

function stop(root) {
  const paths = gatePaths(root);
  const record = readPidRecord(paths.pidFile);
  if (!isOurProcess(record)) {
    rmSync(paths.pidFile, { force: true });
    console.log('gate-clone: none runs');
    return 0;
  }
  spawnSync('taskkill.exe', ['/PID', String(record.pid), '/T', '/F'], { stdio: 'ignore' });
  if (typeof record.clone === 'string' && /[\\/]jfb-gate-[0-9a-f]{7}$/.test(record.clone)) removeTree(record.clone);
  rmSync(paths.pidFile, { force: true });
  console.log(`gate-clone: stopped the gate of ${record.commit.slice(0, 7)}`);
  return 0;
}

const isMain = process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href;
if (isMain) {
  const root = join(fileURLToPath(new URL('.', import.meta.url)), '..');
  const args = parseArgs(process.argv.slice(2));
  if (args.error) {
    console.error(`gate-clone: ${args.error}`);
    process.exit(2);
  }
  const commands = { run, status, stop };
  Promise.resolve(commands[args.command](root, args)).then(
    (code) => process.exit(code),
    (error) => {
      console.error(`gate-clone: ${error.message}`);
      process.exit(1);
    },
  );
}
