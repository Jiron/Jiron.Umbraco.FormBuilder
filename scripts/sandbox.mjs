#!/usr/bin/env node
// SPDX-License-Identifier: LicenseRef-PolyForm-Perimeter-1.0.1
/**
 * The research sandbox of Phase 1 (ROADMAP 1.2a): a throwaway Umbraco site with a spike extension, under the
 * git-ignored `.local/`, on the port of section 4.11.
 *
 *   node scripts/sandbox.mjs create [--site sandbox]   install the templates into the private hive (section 3.4) and
 *                                                        create the site and the extension
 *   node scripts/sandbox.mjs start  [--site sandbox]   build, start in the background, wait until it answers
 *   node scripts/sandbox.mjs stop   [--site sandbox]   stop the process tree this script started
 *   node scripts/sandbox.mjs status [--site sandbox]   say whether it runs
 *   node scripts/sandbox.mjs reset  [--site sandbox]   stop, then delete the database, logs, media and mail,
 *                                                        so the next start is a fresh install
 *   node scripts/sandbox.mjs delete [--site sandbox]   stop, then delete the whole site folder
 *   node scripts/sandbox.mjs login  [--site sandbox]   print the backoffice address and where the admin is kept
 *
 * The admin is generated into `.local/credentials.json` and reaches Umbraco only through the child's environment
 * (its unattended install), so no password appears in a command line, a log or this script's output (CLAUDE.md
 * rule 3). The script stops only the process tree it started: the PID file records the process start time, so a
 * recycled PID that now belongs to another session is never touched (CLAUDE.md rule 6). Every build uses the
 * throwaway-build settings, so no shared build server keeps the folders locked.
 *
 * Exit codes: 0 fine, 1 failure, 2 usage error.
 */
import { spawn, spawnSync } from 'node:child_process';
import { randomBytes } from 'node:crypto';
import { closeSync, existsSync, mkdirSync, openSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { connect } from 'node:net';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

/**
 * The sites this script manages. Ports come from ROADMAP section 4.11; 5510 is the Phase 1 research sandbox. `heyApi` pins
 * the extension's client generator: the 17.7.1 extension template asks for `^0.85.2`, while
 * `@umbraco-cms/backoffice@17.7.1` needs a peer `>=0.99.0 <1.0.0`, so `npm install` fails as generated
 * (docs/research/umbraco-backoffice.md).
 */
export const SITES = {
  sandbox: { folder: '.local/sandbox', umbraco: '17.7.1', port: 5510, heyApi: '0.99.0' },
};

/** The project names inside a site folder. */
export const SITE_PROJECT = 'Sandbox';
export const EXTENSION_PROJECT = 'Spike';

/** Environment for every dotnet call: no shared build servers (CLAUDE.md rule 6). */
export const BUILD_ENV = { MSBUILDDISABLENODEREUSE: '1', DOTNET_CLI_USE_MSBUILD_SERVER: '0', DOTNET_NOLOGO: '1' };
export const BUILD_ARGS = ['-p:UseSharedCompilation=false', '-nodeReuse:false'];

export const COMMANDS = ['create', 'start', 'stop', 'status', 'reset', 'delete', 'login'];

/** Parses `<command> [--site <name>]`. Returns `{ command, site }` or `{ error }`. */
export function parseArgs(argv) {
  const [command, ...rest] = argv;
  if (!COMMANDS.includes(command)) return { error: `unknown command ${command ?? '(none)'}; use one of ${COMMANDS.join(', ')}` };
  let site = 'sandbox';
  for (let i = 0; i < rest.length; i++) {
    if (rest[i] === '--site' && rest[i + 1]) site = rest[++i];
    else return { error: `unknown argument ${rest[i]}` };
  }
  if (!SITES[site]) return { error: `unknown site ${site}; known: ${Object.keys(SITES).join(', ')}` };
  return { command, site };
}

/** Every path a site uses, from the repository root. */
export function sitePaths(root, site) {
  const profile = SITES[site];
  const folder = join(root, ...profile.folder.split('/'));
  const project = join(folder, SITE_PROJECT);
  return {
    folder,
    project,
    csproj: join(project, `${SITE_PROJECT}.csproj`),
    exe: join(project, 'bin', 'Debug', 'net10.0', `${SITE_PROJECT}.exe`),
    extension: join(folder, EXTENSION_PROJECT),
    extensionCsproj: join(folder, EXTENSION_PROJECT, `${EXTENSION_PROJECT}.csproj`),
    client: join(folder, EXTENSION_PROJECT, 'Client'),
    pidFile: join(folder, `${site}.pid.json`),
    logFile: join(folder, `${site}.log`),
    mail: join(folder, 'mail'),
    credentials: join(root, '.local', 'credentials.json'),
    hive: join(tmpdir(), `jfb-templates-${profile.umbraco}`),
    url: `http://127.0.0.1:${profile.port}`,
  };
}

/** A password that any Umbraco password policy accepts: about 32 random characters plus one of each class. */
export function newPassword() {
  return randomBytes(24).toString('base64').replace(/[+/=]/g, '') + 'Aa1!';
}

/**
 * The site's generated secrets from `.local/credentials.json`, created on first use. Other entries in the file
 * are kept as they are. Never print what this returns.
 */
export function loadSecrets(root, site) {
  const paths = sitePaths(root, site);
  const all = existsSync(paths.credentials) ? JSON.parse(readFileSync(paths.credentials, 'utf8')) : {};
  const entry = all[site] ?? {};
  entry.url = `${paths.url}/umbraco`;
  entry.admin ??= { name: 'Sandbox admin', email: `admin@${site}.jfb.test`, password: newPassword() };
  entry.imagingHmacSecretKey ??= randomBytes(64).toString('base64');
  all[site] = entry;
  mkdirSync(join(root, '.local'), { recursive: true });
  writeFileSync(paths.credentials, JSON.stringify(all, null, 2) + '\n', 'utf8');
  return entry;
}

/**
 * The environment of the started site: the unattended install's admin, the address, plain http, the mail folder
 * and the throwaway-build settings. It is passed to the child only; this process's own environment is unchanged.
 */
export function childEnv(baseEnv, paths, secrets) {
  return {
    ...baseEnv,
    ...BUILD_ENV,
    ASPNETCORE_ENVIRONMENT: 'Development',
    ASPNETCORE_URLS: paths.url,
    Umbraco__CMS__Unattended__InstallUnattended: 'true',
    Umbraco__CMS__Unattended__UnattendedUserName: secrets.admin.name,
    Umbraco__CMS__Unattended__UnattendedUserEmail: secrets.admin.email,
    Umbraco__CMS__Unattended__UnattendedUserPassword: secrets.admin.password,
    Umbraco__CMS__Imaging__HMACSecretKey: secrets.imagingHmacSecretKey,
    // Plain http on loopback: without this the backoffice sign-in fails (tandem-lessons.md).
    Umbraco__CMS__Global__UseHttps: 'false',
    Umbraco__CMS__WebRouting__UmbracoApplicationUrl: paths.url,
    // Mail is written to a folder as .eml files and never sent.
    Umbraco__CMS__Global__Smtp__From: 'sandbox@jfb.test',
    Umbraco__CMS__Global__Smtp__DeliveryMethod: 'SpecifiedPickupDirectory',
    Umbraco__CMS__Global__Smtp__PickupDirectoryLocation: paths.mail,
  };
}

/** The lines `login` prints: the address, the admin's email and where the password is kept, never the password. */
export function loginLines(secrets, paths) {
  return [
    `sandbox: backoffice ${secrets.url}`,
    `sandbox: admin ${secrets.admin.email}; the password is in ${paths.credentials} (never printed)`,
  ];
}

/**
 * Files that stop MSBuild and the editor settings from walking up into the repository's own build files (warnings
 * as errors, central package versions, analyzers), so the sandbox builds as a plain Umbraco site would.
 */
export const ISOLATION_FILES = {
  'Directory.Build.props': '<Project>\n  <!-- Stops the walk to the repository\'s build settings (scripts/sandbox.mjs). -->\n</Project>\n',
  'Directory.Build.targets': '<Project>\n  <!-- Stops the walk to the repository\'s build targets (scripts/sandbox.mjs). -->\n</Project>\n',
  '.editorconfig': 'root = true\n',
};

/** The PID record of a started site: the PID plus its start time, which tells it apart from a recycled PID. */
export function readPidRecord(pidFile) {
  if (!existsSync(pidFile)) return null;
  try {
    const record = JSON.parse(readFileSync(pidFile, 'utf8'));
    return Number.isInteger(record.pid) && typeof record.startedAt === 'string' ? record : null;
  } catch {
    return null;
  }
}

/**
 * The start time of a process as a text that stays the same while the process runs and differs for another process
 * that later gets the same PID, or null when it does not run. Each system writes it its own way, and a record is only
 * ever compared on the system that wrote it:
 *   - Windows: the start time in UTC as ISO text, from Windows PowerShell 5.1;
 *   - Linux: the process's start in clock ticks after boot (`/proc/<pid>/stat`, field 22) with the boot's time
 *     (`btime` in `/proc/stat`), as `linux:<btime>:<ticks>`: exact, and no program to start (ROADMAP 26.1);
 *   - elsewhere: `ps -o lstart=`, to the second.
 */
export function processStartTime(pid, { platform = process.platform, readText = (file) => readFileSync(file, 'utf8') } = {}) {
  const id = Number(pid);
  if (!Number.isInteger(id) || id <= 0) return null;
  if (platform === 'win32') {
    const script = `$p = Get-Process -Id ${id} -ErrorAction SilentlyContinue; if ($p) { $p.StartTime.ToUniversalTime().ToString('o') }`;
    const result = spawnSync('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', script], { encoding: 'utf8' });
    const text = (result.stdout ?? '').trim();
    return text === '' ? null : text;
  }
  if (platform === 'linux') {
    let stat;
    let boot;
    try {
      stat = readText(`/proc/${id}/stat`);
      boot = readText('/proc/stat');
    } catch {
      return null;
    }
    return linuxStartText(stat, boot);
  }
  const result = spawnSync('ps', ['-o', 'lstart=', '-p', String(id)], { encoding: 'utf8' });
  const text = (result.stdout ?? '').trim();
  return result.status === 0 && text !== '' ? `ps:${text}` : null;
}

/**
 * The Linux start text from `/proc/<pid>/stat` and `/proc/stat`, or null when either cannot be read as written. The
 * command's name stands in brackets and may itself hold spaces and brackets, so the fields are counted after the last
 * closing bracket: the state is field 3 and the start time field 22. A zombie (state Z) has ended and counts as gone.
 */
export function linuxStartText(stat, boot) {
  const close = stat.lastIndexOf(')');
  if (close < 0) return null;
  const fields = stat.slice(close + 1).trim().split(/\s+/);
  const [state] = fields;
  const ticks = fields[19];
  const btime = /^btime\s+(\d+)\s*$/m.exec(boot)?.[1];
  if (!state || state === 'Z' || state === 'X' || !/^\d+$/.test(ticks ?? '') || !btime) return null;
  return `linux:${btime}:${ticks}`;
}

/** True when the recorded process still runs and is the one this script started. */
export function isOurProcess(record, startTimeOf = processStartTime) {
  if (!record) return false;
  const started = startTimeOf(record.pid);
  return started !== null && started === record.startedAt;
}

/** Resolves true when something accepts connections on 127.0.0.1:port. */
export function portOpen(port) {
  return new Promise((resolve) => {
    const socket = connect({ host: '127.0.0.1', port, timeout: 500 });
    const done = (open) => {
      socket.destroy();
      resolve(open);
    };
    socket.once('connect', () => done(true));
    socket.once('timeout', () => done(false));
    socket.once('error', () => done(false));
  });
}

function run(command, args, options = {}) {
  const result = spawnSync(command, args, { stdio: 'inherit', ...options, env: { ...process.env, ...BUILD_ENV, ...(options.env ?? {}) } });
  if (result.status !== 0) throw new Error(`${command} ${args[0]} failed with exit code ${result.status ?? result.error?.message}`);
}

function runNpm(args, cwd) {
  // npm is a .cmd file on Windows, which Node only starts through a shell; the arguments are fixed strings.
  run('npm', args, { cwd, shell: process.platform === 'win32' });
}

/** True when the site's private hive (one per Umbraco version) holds the Umbraco templates. */
function templatesInstalled(paths) {
  if (!existsSync(paths.hive)) return false;
  const result = spawnSync('dotnet', ['new', 'list', 'umbraco', '--debug:custom-hive', paths.hive], { encoding: 'utf8', env: { ...process.env, ...BUILD_ENV } });
  return result.status === 0 && /umbraco-extension/.test(result.stdout);
}

/** Pins the extension's packages to exact versions that fit together (see `SITES`). */
export function pinClientPackages(client, profile) {
  const file = join(client, 'package.json');
  const manifest = JSON.parse(readFileSync(file, 'utf8'));
  manifest.devDependencies['@hey-api/openapi-ts'] = profile.heyApi;
  manifest.devDependencies['@umbraco-cms/backoffice'] = profile.umbraco;
  writeFileSync(file, JSON.stringify(manifest, null, 2) + '\n', 'utf8');
}

function create(root, site) {
  const profile = SITES[site];
  const paths = sitePaths(root, site);
  if (existsSync(paths.csproj)) {
    console.log(`sandbox: ${site} exists already in ${paths.folder}; use reset or delete first`);
    return;
  }
  if (!templatesInstalled(paths)) {
    console.log(`sandbox: installing Umbraco.Templates@${profile.umbraco} into ${paths.hive}`);
    run('dotnet', ['new', 'install', `Umbraco.Templates@${profile.umbraco}`, '--debug:custom-hive', paths.hive]);
  }
  mkdirSync(paths.folder, { recursive: true });
  for (const [name, text] of Object.entries(ISOLATION_FILES)) writeFileSync(join(paths.folder, name), text, 'utf8');
  const hive = ['--debug:custom-hive', paths.hive];
  // No admin options here: the admin comes through the unattended install's environment at the first start.
  run('dotnet', ['new', 'umbraco', '-n', SITE_PROJECT, '-o', paths.project, '--development-database-type', 'SQLite', '--use-delivery-api', '--telemetry-level', 'Minimal', ...hive]);
  run('dotnet', ['new', 'umbraco-extension', '-n', EXTENSION_PROJECT, '-o', paths.extension, '--include-example', '--site-domain', paths.url, ...hive]);
  run('dotnet', ['add', paths.csproj, 'reference', paths.extensionCsproj]);
  pinClientPackages(paths.client, profile);
  runNpm(['install', '--no-audit', '--no-fund'], paths.client);
  run('dotnet', ['build', paths.csproj, ...BUILD_ARGS]);
  console.log(`sandbox: created ${site} (Umbraco ${profile.umbraco}) in ${paths.folder}`);
  // The template's pre-generated client does not type-check against the pinned packages, so the client is
  // generated from the running site first (docs/research/umbraco-backoffice.md).
  console.log(`sandbox: next, start it, then run "npm run generate-client" and "npm run build" in ${paths.client}`);
}

async function start(root, site) {
  const paths = sitePaths(root, site);
  if (!existsSync(paths.csproj)) throw new Error(`${site} does not exist; run create first`);
  if (isOurProcess(readPidRecord(paths.pidFile))) {
    console.log(`sandbox: already running at ${paths.url}`);
    return;
  }
  if (await portOpen(SITES[site].port)) throw new Error(`port ${SITES[site].port} is in use by a process this script did not start; it is left alone`);
  run('dotnet', ['build', paths.csproj, ...BUILD_ARGS]);
  const secrets = loadSecrets(root, site);
  mkdirSync(paths.mail, { recursive: true });
  const log = openSync(paths.logFile, 'w');
  let child;
  try {
    // The built site itself, detached, with its output in the log file, so the caller's console is never held
    // open. Not `dotnet run`: the site would then be a grandchild with a console of its own, and its output would
    // never reach the log.
    child = spawn(paths.exe, [], {
      cwd: paths.project,
      env: childEnv(process.env, paths, secrets),
      detached: true,
      windowsHide: true,
      stdio: ['ignore', log, log],
    });
  } finally {
    closeSync(log);
  }
  child.unref();
  const startedAt = processStartTime(child.pid);
  if (startedAt === null) throw new Error(`the site exited at once; see ${paths.logFile}`);
  writeFileSync(paths.pidFile, JSON.stringify({ pid: child.pid, startedAt }) + '\n', 'utf8');
  console.log(`sandbox: starting (pid ${child.pid}); log in ${paths.logFile}`);
  const deadline = Date.now() + 6 * 60 * 1000;
  while (Date.now() < deadline) {
    await new Promise((resolve) => setTimeout(resolve, 2000));
    if (!isOurProcess(readPidRecord(paths.pidFile))) {
      rmSync(paths.pidFile, { force: true });
      throw new Error(`the site stopped while starting; see ${paths.logFile}`);
    }
    try {
      const response = await fetch(`${paths.url}/umbraco`, { redirect: 'manual', signal: AbortSignal.timeout(10_000) });
      if (response.status === 200) {
        console.log(`sandbox: ready at ${paths.url} (backoffice ${paths.url}/umbraco)`);
        return;
      }
    } catch {
      // Not listening yet.
    }
  }
  throw new Error(`the site did not answer within six minutes; see ${paths.logFile}`);
}

function stop(root, site) {
  const paths = sitePaths(root, site);
  const record = readPidRecord(paths.pidFile);
  if (isOurProcess(record)) {
    spawnSync('taskkill.exe', ['/PID', String(record.pid), '/T', '/F'], { stdio: 'ignore' });
    console.log(`sandbox: stopped (pid ${record.pid} and its children)`);
  } else {
    console.log('sandbox: not running');
  }
  rmSync(paths.pidFile, { force: true });
}

function reset(root, site) {
  const paths = sitePaths(root, site);
  stop(root, site);
  for (const folder of [join(paths.project, 'umbraco', 'Data'), join(paths.project, 'umbraco', 'Logs'), join(paths.project, 'wwwroot', 'media'), paths.mail]) {
    rmSync(folder, { recursive: true, force: true, maxRetries: 5, retryDelay: 500 });
  }
  rmSync(paths.logFile, { force: true });
  console.log(`sandbox: reset ${site}; the next start is a fresh install`);
}

function remove(root, site) {
  const paths = sitePaths(root, site);
  stop(root, site);
  rmSync(paths.folder, { recursive: true, force: true, maxRetries: 5, retryDelay: 500 });
  console.log(`sandbox: deleted ${paths.folder}`);
}

const isMain = process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href;
if (isMain) {
  const root = join(fileURLToPath(new URL('.', import.meta.url)), '..');
  const args = parseArgs(process.argv.slice(2));
  if (args.error) {
    console.error(`sandbox: ${args.error}`);
    process.exit(2);
  }
  try {
    const { command, site } = args;
    if (command === 'create') create(root, site);
    else if (command === 'start') await start(root, site);
    else if (command === 'stop') stop(root, site);
    else if (command === 'reset') reset(root, site);
    else if (command === 'delete') remove(root, site);
    else if (command === 'status') {
      const paths = sitePaths(root, site);
      console.log(isOurProcess(readPidRecord(paths.pidFile)) ? `sandbox: running at ${paths.url}` : 'sandbox: not running');
    } else if (command === 'login') {
      for (const line of loginLines(loadSecrets(root, site), sitePaths(root, site))) console.log(line);
    }
  } catch (error) {
    console.error(`sandbox: ${error.message}`);
    process.exit(1);
  }
}
