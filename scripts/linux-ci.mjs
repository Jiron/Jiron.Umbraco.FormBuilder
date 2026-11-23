#!/usr/bin/env node
// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
/**
 * Runs the workflow's Linux steps in a container on the development machine (ROADMAP 26.1), so a Linux failure is found
 * before a run on GitHub: the build job's checks and tests, the integration tests on SQLite, the browser smoke tests.
 *
 *   node scripts/linux-ci.mjs                          build, integration and smoke, one after the other
 *   node scripts/linux-ci.mjs build                    one stage, or several: build, integration, smoke
 *   node scripts/linux-ci.mjs integration -- --share 2/6   a stage's own arguments after -- (test-integration.mjs's)
 *   node scripts/linux-ci.mjs run <dir> <command...>   one command as the runner's user, in a folder of the copy
 *   node scripts/linux-ci.mjs stop                     stop the container
 *   --keep      leave the container running at the end (it is stopped if this run started it)
 *   --rebuild   build the image again, as after a change to scripts/linux/Dockerfile
 *
 * Each step says its verdict: Docker, the image (built from scripts/linux/Dockerfile when missing), the container
 * (`jfb-linux`: the repository read-only at /src, the volume `jfb-linux-work` at /work for the copy, its builds and the
 * NuGet and npm caches; an existing one is used as it is), the sync of the working tree into the copy, uncommitted
 * changes too, each stage (scripts/linux/linux-ci.sh, which says each of its own steps' verdicts and keeps their logs
 * in /work/logs), and the stop of what this run started.
 *
 * It makes no SQL Server and refuses every argument or setting that would ask for one: SQL Server runs only on GitHub
 * (CLAUDE.md rule 6). It touches no other container, image or volume, pulls nothing itself (the image build fetches
 * its base images), and never prunes. Exit codes: 0 every step passed, 1 a step failed, 2 usage or a refusal.
 */
import { spawnSync } from 'node:child_process';
import { existsSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const repo = resolve(dirname(fileURLToPath(import.meta.url)), '..');

/** The image, the container and the work volume this script makes and uses, and nothing else. */
export const IMAGE = 'jfb-linux:local';
export const CONTAINER = 'jfb-linux';
export const VOLUME = 'jfb-linux-work';

/** The stages, in the order a run without stages runs them. */
export const STAGES = Object.freeze(['build', 'integration', 'smoke']);

/** The in-container script, as the container sees the repository. */
export const INSIDE = '/src/scripts/linux/linux-ci.sh';

/** Arguments and settings that name SQL Server, which this script never starts. */
const SQL_SERVER = /sql\s*server|mssql/i;

/**
 * The command line: `{ stages, stageArgs, run, stop, keep, rebuild }` or `{ error }`. `run` is `{ dir, command }`;
 * `stageArgs` go to the last stage named.
 */
export function parseArgs(argv, env = {}) {
  const options = { stages: [], stageArgs: [], run: null, stop: false, keep: false, rebuild: false };
  const dash = argv.indexOf('--');
  const own = dash < 0 ? argv : argv.slice(0, dash);
  options.stageArgs = dash < 0 ? [] : argv.slice(dash + 1);
  const named = [...own, ...options.stageArgs].find((arg) => SQL_SERVER.test(arg));
  if (named) return { error: `refused: no SQL Server here ("${named}"); it runs only on GitHub` };
  const database = env.Tests__Database__DatabaseType;
  if (database && database !== 'Sqlite') return { error: `refused: the integration tests run on SQLite here, not ${database}` };
  for (let i = 0; i < own.length; i++) {
    const arg = own[i];
    if (arg === '--keep') options.keep = true;
    else if (arg === '--rebuild') options.rebuild = true;
    else if (arg === 'stop') options.stop = true;
    else if (arg === 'run') {
      const [dir, ...command] = own.slice(i + 1);
      if (!dir || command.length === 0) return { error: 'run needs a folder of the repository and a command' };
      if (dir.startsWith('/') || dir.split(/[\\/]/).includes('..')) return { error: `run's folder is one of the repository, not ${dir}` };
      options.run = { dir, command: [...command, ...options.stageArgs] };
      options.stageArgs = [];
      break;
    } else if (STAGES.includes(arg)) {
      if (!options.stages.includes(arg)) options.stages.push(arg);
    } else return { error: `unknown argument ${arg}; stages are ${STAGES.join(', ')}, or run, or stop` };
  }

  if (options.stop && (options.run || options.stages.length > 0)) return { error: 'stop takes nothing else' };
  if (options.run && options.stages.length > 0) return { error: 'run takes no stage' };
  if (options.stageArgs.length > 0 && options.stages.length === 0) return { error: 'arguments after -- belong to a stage: name it' };
  if (!options.stop && !options.run && options.stages.length === 0) options.stages = [...STAGES];
  return options;
}

/** The Docker program: on PATH, or where Docker Desktop installs it on Windows. */
export function dockerProgram(platform = process.platform, exists = existsSync) {
  const desktop = 'C:\\Program Files\\Docker\\Docker\\resources\\bin\\docker.exe';
  return platform === 'win32' && exists(desktop) ? desktop : 'docker';
}

/** `docker build` of the image from scripts/linux. */
export function buildImageArgs(root = repo) {
  const context = join(root, 'scripts', 'linux');
  return ['build', '--tag', IMAGE, '--file', join(context, 'Dockerfile'), context];
}

/**
 * `docker run` of the container: the repository read-only, the work volume, room for Chromium's shared memory, and a
 * process that only waits, so each stage runs in it through `docker exec`.
 */
export function runContainerArgs(root = repo) {
  return [
    'run',
    '--detach',
    '--name',
    CONTAINER,
    '--label',
    'jfb.linux-ci=1',
    '--shm-size',
    '1g',
    '--volume',
    `${root}:/src:ro`,
    '--volume',
    `${VOLUME}:/work`,
    IMAGE,
    'sleep',
    'infinity',
  ];
}

/** `docker exec` of the in-container script with its arguments, from /work. */
export function execArgs(args) {
  return ['exec', '--workdir', '/work', CONTAINER, 'bash', INSIDE, ...args];
}

/** The in-container calls of a parsed command line, in order: the sync, then each stage or the one command. */
export function insideCalls(options) {
  if (options.stop) return [];
  const calls = [{ name: 'sync', args: ['sync'] }];
  if (options.run) return [...calls, { name: `run ${options.run.command.join(' ')}`, args: ['run', options.run.dir, ...options.run.command] }];
  options.stages.forEach((stage, index) => {
    const own = index === options.stages.length - 1 ? options.stageArgs : [];
    calls.push({ name: stage, args: [stage, ...own] });
  });
  return calls;
}

function main(argv) {
  const options = parseArgs(argv, process.env);
  if (options.error) {
    console.error(`linux-ci: ${options.error}`);
    return 2;
  }

  const docker = dockerProgram();
  const results = [];
  const run = (args, inherit = true) => spawnSync(docker, args, { stdio: inherit ? 'inherit' : 'pipe', encoding: 'utf8', windowsHide: true });
  const step = (name, work) => {
    const began = Date.now();
    const ok = work();
    const seconds = Math.round((Date.now() - began) / 1000);
    results.push({ name, ok, seconds });
    console.log(`linux-ci: ${name} ${ok ? 'passed' : 'FAILED'} in ${seconds} s`);
    return ok;
  };
  const state = () => {
    const inspect = run(['container', 'inspect', '--format', '{{.State.Running}}', CONTAINER], false);
    return inspect.status !== 0 ? 'missing' : inspect.stdout.trim() === 'true' ? 'running' : 'stopped';
  };

  if (!step('docker', () => run(['version', '--format', '{{.Server.Version}}'], false).status === 0)) return 1;
  if (options.stop) {
    const ok = state() !== 'running' || step('stop', () => run(['stop', CONTAINER], false).status === 0);
    return ok ? 0 : 1;
  }

  const hasImage = run(['image', 'inspect', IMAGE], false).status === 0;
  if ((options.rebuild || !hasImage) && !step('image', () => run(buildImageArgs()).status === 0)) return 1;
  let started = false;
  const before = state();
  const ready = step('container', () => {
    if (before === 'running') return true;
    started = true;
    return run(before === 'stopped' ? ['start', CONTAINER] : runContainerArgs(), false).status === 0;
  });

  let ok = ready;
  if (ready) {
    for (const call of insideCalls(options)) {
      if (!step(call.name, () => run(execArgs(call.args)).status === 0)) {
        ok = false;
        if (call.name === 'sync') break;
      }
    }
  }

  if (started && !options.keep) step('stop', () => run(['stop', CONTAINER], false).status === 0);
  console.log(`linux-ci: ${ok ? 'every step passed' : `FAILED: ${results.filter((each) => !each.ok).map((each) => each.name).join(', ')}`}`);
  return ok ? 0 : 1;
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  process.exitCode = main(process.argv.slice(2));
}
