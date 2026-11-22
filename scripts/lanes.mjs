#!/usr/bin/env node
// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
/**
 * Runs named lanes side by side, each lane a list of commands one after the other (ROADMAP 24.1, ADR-0261): the gate's
 * cheap stages that need nothing of each other (scripts/ci-local.ps1).
 *
 *   - **Every line marked with its lane.** A lane's output goes out line by line, each line with the lane's name in
 *     front, so the lines of two lanes may alternate but never mix inside a line. Each command's start and end are
 *     lines of their own, with its seconds.
 *   - **The first failure ends the run.** A command that exits with anything but 0 fails its lane, and every other lane
 *     still running is stopped, its whole process tree by PID. The run fails naming the lane and the command, and the
 *     summary gives each lane's seconds and result (passed, failed or stopped, with the command).
 *   - **A time limit:** a run longer than `--timeout` minutes (default 60) stops every lane and fails, naming the
 *     commands that still ran.
 *
 *   node scripts/lanes.mjs --lane <name> [--cwd <dir>] <command> [<command> ...] [--lane ...]
 *   node scripts/lanes.mjs --timeout 30 --report <file.json> --lane ...
 *   node scripts/lanes.mjs --stop        stop a run this script started (by PID, whole tree)
 *
 * A command is one argument, its words separated by spaces, such as "npm ci --no-audit": no quotes and no shell
 * syntax, so a word can hold no space. `node` runs this Node; any other program runs through the shell on Windows (so
 * that npm.cmd is found) and directly elsewhere. `--cwd` (relative to the repository) sets the folder of the lane's
 * commands; the default is the repository. `--report` writes each lane's name, seconds, result and failed command as
 * JSON, also after a failure, for the caller's own table.
 *
 * The PID file .local/lanes/run.json records every process it starts, and a failure, the time limit or an interrupt
 * stops each one's whole tree by PID (CLAUDE.md rule 6).
 *
 * Exit codes: 0 every lane passed, 1 a lane failed or ran past the time limit, 2 usage.
 */
import { spawn, spawnSync } from 'node:child_process';
import { mkdirSync, rmSync, writeFileSync } from 'node:fs';
import { constants } from 'node:os';
import { dirname, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { isOurProcess, processStartTime, readPidRecord } from './sandbox.mjs';

const repo = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const isWindows = process.platform === 'win32';

/** The state of a run, relative to the repository (git-ignored): the PID file. */
export const STATE = '.local/lanes';

/** The processes of every run of this process that still run, for an interrupt to stop. */
const RUNNING = new Set();

/** Characters a command's word may not hold: quotes and what a shell would read as syntax. */
const UNSAFE = /["'`&|<>^%!;()$\r\n]/;

/** A command's words, or throws when the command is empty or holds quotes or shell syntax. */
export function splitCommand(command) {
  if (UNSAFE.test(command)) throw new Error(`"${command}" holds a quote or shell syntax; a command is plain words`);
  const words = command.trim().split(/\s+/).filter(Boolean);
  if (words.length === 0) throw new Error('a command is empty');
  return words;
}

/** The command line, or `{ error }`. Each lane is `{ name, cwd, commands: [[word, ...], ...] }`. */
export function parseArgs(argv) {
  const options = { mode: 'run', lanes: [], timeout: 60, report: null };
  let lane = null;
  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i];
    const value = () => {
      if (i + 1 >= argv.length || argv[i + 1].startsWith('--')) throw new Error(`${arg} needs a value`);
      return argv[++i];
    };
    try {
      if (arg === '--lane') {
        const name = value().trim();
        if (!name) return { error: '--lane needs a name' };
        if (options.lanes.some((other) => other.name === name)) return { error: `two lanes are named "${name}"` };
        lane = { name, cwd: '.', commands: [] };
        options.lanes.push(lane);
      } else if (arg === '--cwd') {
        if (!lane) return { error: '--cwd belongs to a lane: put it after --lane <name>' };
        if (lane.commands.length > 0) return { error: `--cwd of lane "${lane.name}" goes before its commands` };
        lane.cwd = value();
      } else if (arg === '--timeout') {
        options.timeout = Number(value());
        if (!Number.isFinite(options.timeout) || options.timeout <= 0) return { error: '--timeout is minutes, more than 0' };
      } else if (arg === '--report') {
        options.report = value();
      } else if (arg === '--stop') {
        options.mode = 'stop';
      } else if (arg.startsWith('--')) {
        return { error: `unknown argument ${arg}` };
      } else {
        if (!lane) return { error: `"${arg}" belongs to no lane: put --lane <name> first` };
        lane.commands.push(splitCommand(arg));
      }
    } catch (error) {
      return { error: error.message };
    }
  }
  if (options.mode === 'stop') return options.lanes.length > 0 || options.report ? { error: '--stop takes no lanes' } : options;
  if (options.lanes.length === 0) return { error: 'no lane: --lane <name> <command> ...' };
  const empty = options.lanes.find((each) => each.commands.length === 0);
  if (empty) return { error: `lane "${empty.name}" has no command` };
  return options;
}

/**
 * What runs a command's words: this Node for `node`; on Windows the shell for the rest, given one line (the words are
 * plain, see splitCommand), so that npm.cmd is found; elsewhere the program itself.
 */
export function invocation(words, { windows = isWindows, node = process.execPath } = {}) {
  const [program, ...args] = words;
  if (program === 'node') return { file: node, args, shell: false };
  if (windows) return { file: words.join(' '), args: [], shell: true };
  return { file: program, args, shell: false };
}

/**
 * Splits chunks of output into whole lines, each written with a mark in front; `flush()` writes a last line that
 * ended without a line break.
 */
export function lineWriter(mark, write) {
  let pending = '';
  return {
    push(chunk) {
      pending += chunk;
      const lines = pending.split(/\r?\n/);
      pending = lines.pop() ?? '';
      for (const line of lines) write(`${mark} ${line}`);
    },
    flush() {
      if (pending) write(`${mark} ${pending}`);
      pending = '';
    },
  };
}

/** A lane's mark: its name in brackets, padded to the longest name, so the lanes' lines line up. */
export function markOf(name, width) {
  return `[${name}]`.padEnd(width + 2);
}

/** The summary's lines. Each result: `{ name, seconds, result, failed }`. */
export function summaryLines(results, ok, failure) {
  const width = Math.max(4, ...results.map((result) => result.name.length));
  const lines = [`lanes:   ${'lane'.padEnd(width)}  seconds  result`];
  for (const result of results) {
    const state = result.failed && result.result !== 'passed' ? `${result.result} at "${result.failed}"` : result.result;
    lines.push(`lanes:   ${result.name.padEnd(width)}  ${String(Math.round(result.seconds)).padStart(7)}  ${state}`);
  }
  lines.push(ok ? `lanes: every lane passed` : `lanes: FAILED: ${failure}`);
  return lines;
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

/**
 * Runs the lanes side by side; resolves `{ ok, failure, results }`. `out` writes a line (default: stdout). `pidFile`,
 * when given, records the run's processes while they run.
 */
export async function runLanes(lanes, { timeoutMinutes = 60, out = (line) => process.stdout.write(`${line}\n`), pidFile = null, root = repo, env = process.env } = {}) {
  const width = Math.max(...lanes.map((lane) => lane.name.length));
  const running = new Map();
  const record = { pid: process.pid, startedAt: processStartTime(process.pid), children: [] };
  const save = () => {
    if (!pidFile) return;
    mkdirSync(dirname(pidFile), { recursive: true });
    writeFileSync(pidFile, `${JSON.stringify(record, null, 2)}\n`);
  };
  save();
  let stopping = false;
  let failure = null;
  const stopOthers = () => {
    stopping = true;
    for (const child of running.values()) killTree(child.pid);
  };

  const timer = setTimeout(
    () => {
      if (stopping) return;
      const still = [...running.entries()].map(([name, child]) => `"${name}" at "${child.command}"`);
      failure = `the lanes ran past ${timeoutMinutes} minutes; stopped ${still.join(', ') || 'nothing'}`;
      out(`lanes: ${failure}`);
      stopOthers();
    },
    timeoutMinutes * 60 * 1000,
  );

  const runCommand = (lane, words, mark) =>
    new Promise((done) => {
      const { file, args, shell } = invocation(words);
      const cwd = resolve(root, lane.cwd);
      const child = spawn(file, args, { cwd, env, shell, stdio: ['ignore', 'pipe', 'pipe'], detached: !isWindows, windowsHide: true });
      child.command = words.join(' ');
      running.set(lane.name, child);
      RUNNING.add(child);
      const entry = { label: lane.name, command: child.command, pid: child.pid };
      record.children.push(entry);
      save();
      // The start time tells a later --stop that the PID is still this process; reading it takes a moment.
      entry.startedAt = processStartTime(child.pid) ?? undefined;
      save();
      const writers = [lineWriter(mark, out), lineWriter(mark, out)];
      child.stdout.setEncoding('utf8').on('data', (chunk) => writers[0].push(chunk));
      child.stderr.setEncoding('utf8').on('data', (chunk) => writers[1].push(chunk));
      const finish = (code) => {
        running.delete(lane.name);
        RUNNING.delete(child);
        for (const writer of writers) writer.flush();
        done(code);
      };
      child.on('error', (error) => {
        out(`${mark} ${error.message}`);
        finish(-1);
      });
      child.on('close', (code) => finish(code ?? -1));
    });

  const runLane = async (lane) => {
    const mark = markOf(lane.name, width);
    const began = Date.now();
    const result = { name: lane.name, seconds: 0, result: 'passed', failed: null };
    for (const words of lane.commands) {
      const command = words.join(' ');
      if (stopping) {
        result.result = 'stopped';
        result.failed = command;
        break;
      }
      const at = Date.now();
      out(`${mark} > ${command}${lane.cwd === '.' ? '' : ` (in ${lane.cwd})`}`);
      const code = await runCommand(lane, words, mark);
      const seconds = Math.round((Date.now() - at) / 1000);
      if (code === 0) {
        out(`${mark} ok after ${seconds} s: ${command}`);
        continue;
      }
      result.failed = command;
      if (stopping) {
        result.result = 'stopped';
        out(`${mark} stopped after ${seconds} s: ${command}`);
      } else {
        result.result = 'failed';
        failure = `lane "${lane.name}": "${command}" exited with code ${code}`;
        out(`${mark} FAILED after ${seconds} s (exit ${code}): ${command}`);
        stopOthers();
      }
      break;
    }
    result.seconds = (Date.now() - began) / 1000;
    return result;
  };

  try {
    const results = await Promise.all(lanes.map((lane) => runLane(lane)));
    return { ok: failure === null, failure, results };
  } finally {
    clearTimeout(timer);
    if (pidFile) rmSync(pidFile, { force: true });
  }
}

function stop() {
  const pidFile = resolve(repo, STATE, 'run.json');
  const record = readPidRecord(pidFile);
  if (!record) {
    console.log('lanes: no run is recorded');
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
  console.log(stopped === 0 ? 'lanes: none of its processes runs any more' : `lanes: stopped ${stopped} process tree(s)`);
  return 0;
}

async function main(argv) {
  const options = parseArgs(argv);
  if (options.error) {
    console.error(`lanes: ${options.error}`);
    return 2;
  }
  if (options.mode === 'stop') return stop();
  const pidFile = resolve(repo, STATE, 'run.json');
  const previous = readPidRecord(pidFile);
  if (previous && previous.pid !== process.pid && isOurProcess(previous)) {
    console.error(`lanes: another run (PID ${previous.pid}) is going; wait for it, or stop it: node scripts/lanes.mjs --stop`);
    return 1;
  }
  for (const signal of ['SIGINT', 'SIGTERM', 'SIGHUP', 'SIGBREAK']) {
    if (!(signal in constants.signals)) continue;
    process.on(signal, () => {
      console.error(`lanes: ${signal}; stopping every process this run started`);
      for (const child of RUNNING) killTree(child.pid);
      rmSync(pidFile, { force: true });
      process.exit(130);
    });
  }
  const { ok, failure, results } = await runLanes(options.lanes, { timeoutMinutes: options.timeout, pidFile });
  if (options.report) {
    const report = resolve(process.cwd(), options.report);
    mkdirSync(dirname(report), { recursive: true });
    writeFileSync(report, `${JSON.stringify(results, null, 2)}\n`);
  }
  // On stdout like every lane's lines: Windows PowerShell 5.1 turns stderr lines into error records when redirected.
  for (const line of summaryLines(results, ok, failure)) console.log(line);
  return ok ? 0 : 1;
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  process.exitCode = await main(process.argv.slice(2));
}
