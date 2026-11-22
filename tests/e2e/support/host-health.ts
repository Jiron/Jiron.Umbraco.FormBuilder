import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';

// What the host's log says about its start-up work and, after a run, about its database (ADR-0011, ADR-0031). Umbraco
// on SQLite can stall when several requests write at the same moment: Umbraco 17 hangs, and Umbraco 18, which runs it
// in write-ahead mode without the shared cache, fails the write as "database is locked" (ADR-0250); the tests then
// time out one after the other, far from the cause. On Umbraco 17 the cause is most often one of its two jobs
// over the servers' cache instructions meeting a request's own instruction; the hosts of one server leave both jobs
// out (ADR-0253). The global set-up waits for the start-up work, and a run that
// ends with a stalled database leaves the hang marker, so the gate knows the one failure it may run again ("Flaky
// means broken").

/** The commands that timed out, the writes that found the database locked, and when the first was logged. */
export interface StalledDatabase {
  timedOut: number;
  locked: number;
  firstAt: string;
}

/** The file a run writes next to the host's log when the host's database hung; the next host start removes it. */
export const HANG_MARKER = 'database-hung.txt';

/**
 * The two pieces of start-up work Umbraco 17 does about a minute after it starts, and the test hosts as soon as their
 * own seeding has finished (`hostEnvironment`, the Testing project's StartupSeeding): it registers its distributed
 * jobs and it rebuilds its search indexes. The wait is the same either way: both lines, then the settle time. Umbraco logs the first; for the second it logs nothing that appears
 * whether the Content Delivery API is on or off, so the Testing project's start-up marker logs the second line once the
 * last index populator has run (StartupMarkerPopulator).
 */
export const STARTUP_LINES = [
  'Completed registering distributed background jobs',
  'Jiron Form Builder test host: indexes rebuilt',
] as const;

/** What the start-up marker logs instead when another populator runs after it: the wait cannot rely on it. */
export const MARKER_NOT_LAST = 'Jiron Form Builder test host: the index marker is not the last index populator';

// A command that ran into its 30 s timeout. The elapsed time carries the culture's thousands separator: "30,050ms" or
// "30.050ms". Umbraco's own warning about a hung connection counts too.
const TIMED_OUT = /^\[(\d\d:\d\d:\d\d) ERR\] Failed executing DbCommand \(([\d,.]+)ms\)/;
const STALLED = /^\[(\d\d:\d\d:\d\d) ERR\] .* may be stalled on a hung database connection/;
// Umbraco 18's form of it: SQLite's error 5 on the line after an error's header, which gives its time. An inner
// exception repeats it indented ("--->"), which is the same write and not counted again.
const LOCKED = /^Microsoft\.Data\.Sqlite\.SqliteException \(0x[0-9A-Fa-f]+\): SQLite Error 5: 'database is locked'/;
const ERROR = /^\[(\d\d:\d\d:\d\d) ERR\]/;
const TIMEOUT_MS = 30_000;

/**
 * The stalled database the log shows, or null when every failed command failed within half a minute and no write
 * found the database locked.
 */
export function stalledDatabase(log: string): StalledDatabase | null {
  let timedOut = 0;
  let locked = 0;
  let firstAt: string | null = null;
  let errorAt: string | undefined;
  for (const line of log.split(/\r?\n/)) {
    errorAt = ERROR.exec(line)?.[1] ?? errorAt;
    const command = TIMED_OUT.exec(line);
    const stalled = STALLED.exec(line);
    const hung = command !== null && Number((command[2] ?? '0').replace(/[,.]/g, '')) >= TIMEOUT_MS;
    const lockedAt = LOCKED.test(line) ? errorAt : undefined;
    const at = hung ? command[1] : (stalled?.[1] ?? lockedAt);
    if (!at) continue;
    if (hung) timedOut++;
    if (lockedAt) locked++;
    firstAt ??= at;
  }
  return firstAt ? { timedOut, locked, firstAt } : null;
}

/** One line for the run's console. */
export function describeStall(stall: StalledDatabase, logFile: string): string {
  return (
    `e2e: the host's database stalled from ${stall.firstAt} (${stall.timedOut} command(s) ran into their 30 s timeout, ` +
    `${stall.locked} write(s) found it locked); the tests that failed after it did not fail on their own. This is ` +
    `Umbraco's SQLite limit (ADR-0011, ADR-0250, ADR-0253): run again. Log: ${logFile}`
  );
}

/**
 * Reads the host's log after a run, or after a start that failed, and writes the hang marker next to it when the
 * database hung. Returns the stall, or null.
 */
export function recordStall(logFile: string): StalledDatabase | null {
  const stall = existsSync(logFile) ? stalledDatabase(readFileSync(logFile, 'utf8')) : null;
  if (stall) {
    writeFileSync(
      join(dirname(logFile), HANG_MARKER),
      `from ${stall.firstAt}, ${stall.timedOut} command(s) timed out, ${stall.locked} write(s) found it locked\n`,
    );
  }
  return stall;
}

/**
 * A host's stop that looks at its log afterwards, so that a host of one spec whose database hung leaves its marker as the
 * run's host does, and the gate knows that failure for what it is.
 */
export function stopThenRecordStall(stop: () => Promise<void>, logFile: string): () => Promise<void> {
  return async () => {
    await stop();
    recordStall(logFile);
  };
}

/** True once both pieces of Umbraco's start-up work show in the log. */
export function startupWorkDone(log: string): boolean {
  return STARTUP_LINES.every((line) => log.includes(line));
}

/** How the wait for the start-up work ended. */
export type StartupWait =
  | { state: 'done' }
  | { state: 'stalled'; stall: StalledDatabase }
  | { state: 'misordered' }
  | { state: 'timeout'; missing: string[] };

export interface StartupWaitTimes {
  /** How long to wait for both lines. */
  timeoutMs: number;
  /** How long to wait after they appear, for the index writes that run on after the last populator. */
  settleMs: number;
  /** How often to read the log. */
  pollMs: number;
}

const DEFAULT_TIMES: StartupWaitTimes = { timeoutMs: 180_000, settleMs: 5_000, pollMs: 1_000 };

/**
 * Waits until the host's start-up work is done, so no sign-in lands in the minute in which every SQLite hang of the
 * browser tests began (ADR-0011). Ends early when the log shows a hung database or a misplaced marker.
 */
export async function waitForStartupWork(logFile: string, times: Partial<StartupWaitTimes> = {}): Promise<StartupWait> {
  const { timeoutMs, settleMs, pollMs } = { ...DEFAULT_TIMES, ...times };
  const deadline = Date.now() + timeoutMs;
  for (;;) {
    const log = existsSync(logFile) ? readFileSync(logFile, 'utf8') : '';
    const stall = stalledDatabase(log);
    if (stall) return { state: 'stalled', stall };
    if (log.includes(MARKER_NOT_LAST)) return { state: 'misordered' };
    if (startupWorkDone(log)) {
      await delay(settleMs);
      return { state: 'done' };
    }
    if (Date.now() >= deadline)
      return { state: 'timeout', missing: STARTUP_LINES.filter((line) => !log.includes(line)) };
    await delay(pollMs);
  }
}
