import { mkdirSync, readFileSync, rmSync, statSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';
import { e2eState } from './paths.ts';

const LOCK_DIR = join(e2eState, 'locks', 'sign-ins');

/** How long a lock may live before it counts as left behind, and how long a caller waits for it. */
export interface UserLockTimes {
  staleAfterMs: number;
  waitLimitMs: number;
}

const DEFAULT_TIMES: UserLockTimes = { staleAfterMs: 90_000, waitLimitMs: 120_000 };

/**
 * Makes a function that runs actions one at a time across processes, with a lock directory at `dir`.
 *
 * The lock is a directory, which every platform creates atomically, and it names its holder's process id.
 * A lock whose holder process is gone (a crashed test worker) is taken over at once, so one crash does not
 * time out every test that waits; a lock older than `staleAfterMs` is taken over too.
 */
export function createUserLock(dir: string, times: UserLockTimes = DEFAULT_TIMES) {
  return async function withLock<T>(action: () => Promise<T>): Promise<T> {
    mkdirSync(dirname(dir), { recursive: true });
    const deadline = Date.now() + times.waitLimitMs;
    for (;;) {
      try {
        mkdirSync(dir);
        writeFileSync(join(dir, 'owner'), String(process.pid));
        break;
      } catch (error) {
        if ((error as NodeJS.ErrnoException).code !== 'EEXIST') throw error;
        if (holderIsGone(dir) || Date.now() - createdAt(dir) > times.staleAfterMs) {
          rmSync(dir, { recursive: true, force: true });
        } else if (Date.now() > deadline) {
          throw new Error(`Waited ${times.waitLimitMs / 1000} s for the lock ${dir}.`, { cause: error });
        } else {
          await delay(100);
        }
      }
    }
    try {
      return await action();
    } finally {
      rmSync(dir, { recursive: true, force: true });
    }
  };
}

/**
 * Runs sign-ins, a backoffice user's or a member's, and password resets one at a time across all test workers.
 *
 * Umbraco 17 on SQLite updates the signed-in user (BackOfficeUserStore.UpdateAsync) in a deferred transaction that
 * reads before it writes and takes no write lock first. When several sign-ins overlap, the transactions whose read
 * snapshot went stale retry the update indefinitely and the requests hang (the lessons file, ROADMAP section 3.3;
 * ADR-0011). One at a time costs well under a second each. Umbraco 18 takes SQLite's shared cache out, so any other
 * write can overtake the read too; such a sign-in is signed in once more (support/sign-in-retry.ts, ADR-0250). A member
 * who signs in is updated the same way.
 */
export const withUserLock = createUserLock(LOCK_DIR);

/** True when the lock names a holder process that no longer runs; false while it is unknown or alive. */
function holderIsGone(dir: string): boolean {
  let pid: number;
  try {
    pid = Number(readFileSync(join(dir, 'owner'), 'utf8'));
  } catch {
    // The holder has not written its id yet.
    return false;
  }
  if (!Number.isInteger(pid) || pid <= 0) return false;
  try {
    process.kill(pid, 0);
    return false;
  } catch (error) {
    return (error as NodeJS.ErrnoException).code === 'ESRCH';
  }
}

function createdAt(dir: string): number {
  try {
    return statSync(dir).mtimeMs;
  } catch {
    return Date.now();
  }
}

/** Removes a lock an interrupted run left behind; the global set-up calls it before any worker starts. */
export function clearUserLock(): void {
  rmSync(LOCK_DIR, { recursive: true, force: true });
}
