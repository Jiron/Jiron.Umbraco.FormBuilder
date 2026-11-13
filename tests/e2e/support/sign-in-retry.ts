// A sign-in stalled by Umbraco on SQLite, signed in once more (ADR-0011, ADR-0250). Umbraco updates the signed-in user in
// a transaction that reads before it writes. Umbraco 17 runs SQLite with the shared cache, so only another sign-in can
// overtake that read, and the harness runs sign-ins one at a time (support/user-lock.ts). Umbraco 18 takes the shared
// cache out, so any write of another worker between the read and the write leaves the sign-in's snapshot stale, and
// SQLite refuses it until its busy timeout, again and again: the request does not answer for minutes. A sign-in that
// has not finished within 20 s is that stall; it is signed in once more, the test gets back the time the stall took,
// and the run says so. A second stall, or any other failure, fails the test.

import { test } from '@playwright/test';

/** How long a sign-in may take before it counts as stalled; a sound one takes well under a second. */
export const SIGN_IN_STALL_MS = 20_000;

/** How long the second sign-in may take, as long as a sign-in could take before this retry. */
export const SECOND_SIGN_IN_MS = 60_000;

/** Whether an error is a timeout of Playwright's, a locator's or a request's: the stall's only sign on the client. */
export function isTimeout(error: unknown): boolean {
  return (
    (error instanceof Error && error.name === 'TimeoutError') ||
    /Timeout \d+ms exceeded/.test(error instanceof Error ? error.message : String(error))
  );
}

export interface SignInOnceMore<T> {
  /** Who signs in, for the run's output. */
  readonly who: string;
  /** One sign-in, which must finish within the time it is given. */
  readonly attempt: (timeoutMs: number) => Promise<T>;
  /** What to do before the second sign-in, such as opening the sign-in page again. */
  readonly again?: () => Promise<void>;
  /** Where the run says that a sign-in stalled. */
  readonly warn?: (message: string) => void;
  /** Gives back the time a stalled sign-in took; by default the running test's time limit grows by it. */
  readonly lengthen?: (ms: number) => void;
}

/**
 * Lengthens the running test's time limit by `ms`, so a stall the harness waited out does not use up the test's time.
 * Outside a test (the global set-up), and in a test without a limit, it does nothing.
 */
export function lengthenTest(ms: number): void {
  let info: ReturnType<typeof test.info>;
  try {
    info = test.info();
  } catch {
    return;
  }
  if (info.timeout > 0) info.setTimeout(info.timeout + ms);
}

/** Signs in, and once more when the first sign-in stalled. */
export async function signInOnceMore<T>({
  who,
  attempt,
  again,
  warn = console.warn,
  lengthen = lengthenTest,
}: SignInOnceMore<T>): Promise<T> {
  try {
    return await attempt(SIGN_IN_STALL_MS);
  } catch (error) {
    if (!isTimeout(error)) throw error;
    warn(
      `e2e: the sign-in of ${who} did not finish within ${SIGN_IN_STALL_MS / 1000} s, ` +
        "Umbraco's SQLite stall (ADR-0250); signing in once more.",
    );
    lengthen(SIGN_IN_STALL_MS);
    await again?.();
    return attempt(SECOND_SIGN_IN_MS);
  }
}
