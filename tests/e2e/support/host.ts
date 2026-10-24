import { spawn, spawnSync, type ChildProcess } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import { closeSync, cpSync, existsSync, mkdirSync, openSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { connect } from 'node:net';
import { join, relative, sep } from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';
import { clientDir, DEMO_URL, demoProject, e2eState, PORT_RANGE, repoRoot, TARGET_FRAMEWORK } from './paths.ts';

/** Log lines that mean the host is ready: the demo's seeder and the Testing project's people seeder are done. */
export const READY_LINES = ['Demo seeding finished', 'Jiron Form Builder test people ready'] as const;

/** Log lines that mean the host cannot become ready. */
export const FAILED_LINES = ['Demo seeding failed', 'Jiron Form Builder test people failed'] as const;

const START_TIMEOUT_MS = 4 * 60_000;
const REFUSAL_TIMEOUT_MS = 3 * 60_000;
const STOP_TIMEOUT_MS = 15_000;

/**
 * Inherited variables that would configure the host. They are dropped, so a host's settings come only from the demo's
 * files and from the harness, whatever the shell that starts the tests holds.
 */
const HOST_SETTING_PREFIXES = [
  'Umbraco__',
  'JironFormBuilder',
  'Demo__',
  'ConnectionStrings__',
  'Serilog__',
  'Logging__',
  'ASPNETCORE_',
  'DOTNET_ENVIRONMENT',
];

export interface HostOptions {
  /** A short name; the host's files go to .local/e2e/<name>/. */
  name: string;
  /** A port in Jiron Form Builder's range, never the demo's own (CLAUDE.md rule 6, ROADMAP section 4.11). */
  port: number;
  /** The host's settings and the people it creates, as environment variables. Never printed or written to a file. */
  env: Record<string, string>;
}

/** What a host that had to refuse to start left behind. */
export interface RefusedHost {
  /** The process's exit code; null when a signal ended it. */
  exitCode: number | null;
  /** Everything the host wrote to its console. */
  log: string;
}

/** A running demo host with its own content root, SQLite database, mail pickup folder and log. */
export interface Host {
  baseUrl: string;
  /** Umbraco writes every e-mail here as an .eml file. */
  mailDir: string;
  logFile: string;
  /** The folder that holds the host's files; the hang marker goes here too. */
  stateDir: string;
  /** Stops the host with its whole process tree. */
  stop(): Promise<void>;
}

/** The demo's build output that the hosts run. */
export function demoDll(configuration = process.env.JFB_E2E_CONFIGURATION ?? 'Debug'): string {
  return join(demoProject, 'bin', configuration, TARGET_FRAMEWORK, 'Jiron.Umbraco.FormBuilder.Demo.dll');
}

/** The host's environment: the parent's, without anything that would configure the host, plus the given settings. */
export function hostProcessEnvironment(
  parent: NodeJS.ProcessEnv,
  settings: Record<string, string>,
): Record<string, string | undefined> {
  const kept = Object.fromEntries(
    Object.entries(parent).filter(
      ([key]) => !HOST_SETTING_PREFIXES.some((prefix) => key.toUpperCase().startsWith(prefix.toUpperCase())),
    ),
  );
  return { ...kept, ...settings };
}

/** Refuses a port outside Jiron Form Builder's range, and the demo's own port. */
export function checkPort(port: number): void {
  if (!Number.isInteger(port) || port < PORT_RANGE.first || port > PORT_RANGE.last) {
    throw new Error(`Port ${port} is outside Jiron Form Builder's range ${PORT_RANGE.first} to ${PORT_RANGE.last}.`);
  }
  if (port === Number(new URL(DEMO_URL).port))
    throw new Error(`Port ${port} is the demo's own (scripts/demo.ps1); a test host never uses it.`);
}

function run(command: string, args: string[], cwd: string, env?: NodeJS.ProcessEnv): void {
  // npm is a .cmd file on Windows, which Node starts only through a shell; the arguments are fixed strings.
  const result = spawnSync(command, args, {
    cwd,
    env,
    stdio: 'inherit',
    shell: process.platform === 'win32' && command === 'npm',
  });
  if (result.status !== 0)
    throw new Error(`${command} ${args.join(' ')} failed (exit ${result.status ?? result.signal}).`);
}

/** Builds the client assets when their stamp is missing or stale, as scripts/demo.ps1 does (ADR-0026). */
export function ensureClientAssets(): void {
  const check = spawnSync(process.execPath, [join(clientDir, 'scripts', 'stamp.mjs'), 'check'], {
    cwd: clientDir,
    stdio: 'ignore',
  });
  if (check.status === 0) return;
  if (!existsSync(join(clientDir, 'node_modules'))) run('npm', ['ci', '--no-audit', '--no-fund'], clientDir);
  run('npm', ['run', 'build'], clientDir);
}

/** Builds the demo site and its project references; no build node stays behind for another session to find. */
export function buildDemo(configuration = process.env.JFB_E2E_CONFIGURATION ?? 'Debug'): void {
  run('dotnet', ['build', demoProject, '-c', configuration, '-nologo', '-v', 'q', '-nodeReuse:false'], repoRoot, {
    ...process.env,
    MSBUILDDISABLENODEREUSE: '1',
  });
}

const portOpen = (port: number): Promise<boolean> =>
  new Promise((resolvePort) => {
    const socket = connect({ host: '127.0.0.1', port });
    socket.once('connect', () => {
      socket.destroy();
      resolvePort(true);
    });
    socket.once('error', () => resolvePort(false));
  });

function alive(pid: number): boolean {
  try {
    process.kill(pid, 0);
    return true;
  } catch {
    return false;
  }
}

/** The command line of a process, or an empty string when it is gone or cannot be read. */
function commandLine(pid: number): string {
  if (process.platform === 'win32') {
    const query = `(Get-CimInstance Win32_Process -Filter 'ProcessId=${pid}').CommandLine`;
    return (
      spawnSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', query], {
        encoding: 'utf8',
      }).stdout?.trim() ?? ''
    );
  }
  try {
    return readFileSync(`/proc/${pid}/cmdline`, 'utf8').replaceAll('\0', ' ');
  } catch {
    return '';
  }
}

/** Stops a process and everything it started (CLAUDE.md rule 6), and waits until it is gone. */
async function stopTree(pid: number): Promise<void> {
  if (!alive(pid)) return;
  if (process.platform === 'win32') spawnSync('taskkill', ['/T', '/F', '/PID', String(pid)], { stdio: 'ignore' });
  else process.kill(pid, 'SIGTERM');
  const deadline = Date.now() + STOP_TIMEOUT_MS;
  while (alive(pid) && Date.now() < deadline) await delay(200);
  if (alive(pid) && process.platform !== 'win32') process.kill(pid, 'SIGKILL');
}

/**
 * Stops a host that an earlier, interrupted run left behind. Only the process named in this host's own PID file is
 * considered, and only while its command line still names this host's content root, so a reused process id is never
 * touched (CLAUDE.md rule 6).
 */
async function stopStaleHost(pidFile: string, contentRoot: string): Promise<void> {
  if (!existsSync(pidFile)) return;
  const pid = Number(readFileSync(pidFile, 'utf8').trim());
  if (Number.isInteger(pid) && pid > 0 && alive(pid) && commandLine(pid).includes(contentRoot)) {
    console.log(`e2e: stopping the host an earlier run left behind (pid ${pid})`);
    await stopTree(pid);
  }
  rmSync(pidFile, { force: true });
}

/** A fresh content root: the demo's settings, views and static files, but no database, media or logs. */
function prepareContentRoot(contentRoot: string): void {
  for (const file of ['appsettings.json', 'appsettings.Development.json'])
    cpSync(join(demoProject, file), join(contentRoot, file));
  cpSync(join(demoProject, 'Views'), join(contentRoot, 'Views'), { recursive: true });
  const wwwroot = join(demoProject, 'wwwroot');
  cpSync(wwwroot, join(contentRoot, 'wwwroot'), {
    recursive: true,
    filter: (source) => !relative(wwwroot, source).split(sep).includes('media'),
  });
}

/** The last lines of the host's log, for an error message. */
export function logTail(logFile: string, lines = 30): string {
  if (!existsSync(logFile)) return '(no log)';
  return readFileSync(logFile, 'utf8').split(/\r?\n/).slice(-lines).join('\n');
}

const pageAnswers = async (url: string): Promise<boolean> => {
  try {
    const response = await fetch(url, { redirect: 'manual', signal: AbortSignal.timeout(10_000) });
    return response.status === 200;
  } catch {
    return false;
  }
};

/**
 * Starts the built demo as a fresh host: its own content root under .local/e2e/<name>/site (so its own SQLite
 * database), its own mail pickup folder and log. Resolves when the demo content and the test people exist and the
 * English home page answers.
 */
export async function startHost(options: HostOptions): Promise<Host> {
  const { name, port } = options;
  checkPort(port);

  const stateDir = join(e2eState, name);
  const contentRoot = join(stateDir, 'site');
  const pidFile = join(stateDir, 'host.pid');
  await stopStaleHost(pidFile, contentRoot);
  if (await portOpen(port)) {
    throw new Error(
      `Port ${port} is in use by a process these tests did not start; it is left alone. Stop it and run again.`,
    );
  }

  // Retries cover file handles Windows releases a moment after the old host exited. This also removes an earlier
  // run's hang marker.
  rmSync(stateDir, { recursive: true, force: true, maxRetries: 10, retryDelay: 200 });
  const mailDir = join(stateDir, 'mail');
  const logFile = join(stateDir, 'host.log');
  mkdirSync(contentRoot, { recursive: true });
  mkdirSync(mailDir, { recursive: true });
  prepareContentRoot(contentRoot);

  const dll = demoDll();
  if (!existsSync(dll)) throw new Error(`The demo is not built (${relative(repoRoot, dll)} is missing).`);

  const baseUrl = `http://127.0.0.1:${port}`;
  const env = hostProcessEnvironment(process.env, {
    ASPNETCORE_ENVIRONMENT: 'Development',
    ASPNETCORE_URLS: baseUrl,
    DOTNET_NOLOGO: '1',
    Umbraco__CMS__WebRouting__UmbracoApplicationUrl: baseUrl,
    Umbraco__CMS__Global__Smtp__From: 'e2e-site@jfb.test',
    Umbraco__CMS__Global__Smtp__DeliveryMethod: 'SpecifiedPickupDirectory',
    Umbraco__CMS__Global__Smtp__PickupDirectoryLocation: mailDir,
    ...options.env,
  });

  const launched = launch(dll, contentRoot, env, logFile, pidFile);
  const { stop } = launched;

  const deadline = Date.now() + START_TIMEOUT_MS;
  try {
    for (;;) {
      if (Date.now() > deadline)
        throw new Error(`The host ${name} was not ready within ${START_TIMEOUT_MS / 60_000} minutes.`);
      await delay(500);
      if (launched.exited()) throw new Error(`The host ${name} exited during start-up.`);
      const log = existsSync(logFile) ? readFileSync(logFile, 'utf8') : '';
      const failed = FAILED_LINES.find((line) => log.includes(line));
      if (failed) throw new Error(`The host ${name} logged "${failed}".`);
      if (READY_LINES.every((line) => log.includes(line)) && (await pageAnswers(`${baseUrl}/en/`))) break;
    }
  } catch (error) {
    await stop();
    const message = error instanceof Error ? error.message : String(error);
    throw new Error(`${message} Last log lines (${relative(repoRoot, logFile)}):\n${logTail(logFile)}`, {
      cause: error,
    });
  }

  return { baseUrl, mailDir, logFile, stateDir, stop };
}

/**
 * Starts the built demo as a fresh host that must refuse to start (ROADMAP 4.4a, ADR-0042): its own content root under
 * .local/e2e/<name>/site and no database, because an empty connection string boots Umbraco to its installer state.
 * Resolves with the exit code and the log once the process ends. A host still running at the deadline is stopped,
 * and the call fails.
 */
export async function runRefusedHost(options: HostOptions): Promise<RefusedHost> {
  const { name, port } = options;
  checkPort(port);

  const stateDir = join(e2eState, name);
  const contentRoot = join(stateDir, 'site');
  const pidFile = join(stateDir, 'host.pid');
  await stopStaleHost(pidFile, contentRoot);
  rmSync(stateDir, { recursive: true, force: true, maxRetries: 10, retryDelay: 200 });
  const logFile = join(stateDir, 'host.log');
  mkdirSync(contentRoot, { recursive: true });
  prepareContentRoot(contentRoot);

  const dll = demoDll();
  if (!existsSync(dll)) throw new Error(`The demo is not built (${relative(repoRoot, dll)} is missing).`);

  const env = hostProcessEnvironment(process.env, {
    ASPNETCORE_ENVIRONMENT: 'Development',
    ASPNETCORE_URLS: `http://127.0.0.1:${port}`,
    DOTNET_NOLOGO: '1',
    ConnectionStrings__umbracoDbDSN: '',
    // The demo refuses to start without an imaging key; this one is never used.
    Umbraco__CMS__Imaging__HMACSecretKey: randomBytes(64).toString('hex'),
    ...options.env,
  });

  const launched = launch(dll, contentRoot, env, logFile, pidFile);
  const deadline = Date.now() + REFUSAL_TIMEOUT_MS;
  while (!launched.exited() && Date.now() < deadline) await delay(250);
  const kept = !launched.exited();
  await launched.stop();
  const log = existsSync(logFile) ? readFileSync(logFile, 'utf8') : '';
  if (kept) {
    throw new Error(
      `The host ${name} was still running after ${REFUSAL_TIMEOUT_MS / 60_000} minutes; it should have refused to start. Last log lines:\n${logTail(logFile)}`,
    );
  }

  return { exitCode: launched.exitCode(), log };
}

/**
 * Starts the demo's DLL in a content root with only the log file as its console, so the test runner's console is never
 * held open by the host, and writes its PID file. `stop` ends it with its whole process tree.
 */
function launch(
  dll: string,
  contentRoot: string,
  env: NodeJS.ProcessEnv,
  logFile: string,
  pidFile: string,
): { exited(): boolean; exitCode(): number | null; stop(): Promise<void> } {
  let child: ChildProcess | null = null;
  let exited = false;
  let code: number | null = null;
  const logFd = openSync(logFile, 'a');
  try {
    child = spawn('dotnet', [dll, '--contentRoot', contentRoot], {
      cwd: contentRoot,
      env,
      stdio: ['ignore', logFd, logFd],
      windowsHide: true,
    });
  } finally {
    closeSync(logFd);
  }
  const started = child;
  started.once('exit', (exitCode) => {
    code = exitCode;
    exited = true;
  });
  if (started.pid) writeFileSync(pidFile, String(started.pid));

  return {
    exited: () => exited,
    exitCode: () => code,
    stop: async () => {
      if (started.pid && !exited) await stopTree(started.pid);
      rmSync(pidFile, { force: true });
    },
  };
}
