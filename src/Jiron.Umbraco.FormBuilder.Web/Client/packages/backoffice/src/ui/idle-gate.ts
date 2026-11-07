// A poll never keeps an idle person signed in (ROADMAP 4.10, ADR-0209; ported from the owner's chat package, its
// ADR-0051). A backoffice call made after the access token ran out renews it first, and with it the whole session; so a
// poll made while nobody uses the backoffice waits here, before that renewal, until someone does: a click, a key, the
// mouse or a tab coming back on screen, in any tab of this browser (the tabs share their input on a BroadcastChannel).
// Sites that keep users signed in are not held. Once the session has ended, nobody is signed in to poll for: a poll
// waits for the next sign-in.
import type { UmbAuthSession } from '@umbraco-cms/backoffice/auth';

/** What the gate reads of Umbraco's auth context. */
export interface IdleAuth {
  readonly keepUserLoggedIn: boolean;
  readonly session$: { subscribe(next: (session: UmbAuthSession | undefined) => void): unknown };
}

/** Where the backoffice session stands before the next poll. Times are in seconds since 1970, as Umbraco's. */
export interface SessionSituation {
  readonly now: number;
  /** When the access token expires; undefined while there is no session. */
  readonly accessTokenExpiresAt: number | undefined;
  /** When the access token was last renewed after the page loaded, in any tab; 0 if never. */
  readonly sessionChangedAt: number;
  /** The last input in any backoffice tab of this browser. */
  readonly lastInputAt: number;
  /** The site keeps users signed in (Umbraco:CMS:Security:KeepUserLoggedIn). */
  readonly keepUserLoggedIn: boolean;
}

/**
 * True when the next poll waits: there is no session, or the poll would renew an expired access token, and with it the
 * whole session, although nobody used the backoffice since that token was issued.
 */
export function pollWaitsForInput(situation: SessionSituation): boolean {
  if (situation.accessTokenExpiresAt === undefined) return true;
  if (situation.keepUserLoggedIn) return false;
  return situation.accessTokenExpiresAt <= situation.now && situation.lastInputAt < situation.sessionChangedAt;
}

/** What counts as someone using the backoffice. */
const INPUT_EVENTS = ['pointerdown', 'pointermove', 'keydown', 'wheel', 'touchstart'] as const;

/** The tabs tell each other about input at most this often (seconds). */
const SHARE_INTERVAL = 15;

const CHANNEL = 'jfb-idle';

export class IdleGate {
  readonly #now: () => number;
  #session: UmbAuthSession | undefined;
  #renewedAt = 0;
  #keepUserLoggedIn = false;
  #lastInputAt: number;
  #sharedAt = 0;
  #channel: BroadcastChannel | null = null;
  #started = false;
  readonly #wakers = new Set<() => void>();

  /** `now` gives the time in seconds since 1970, as Umbraco's session does. */
  constructor(now: () => number = () => Math.floor(Date.now() / 1000)) {
    this.#now = now;
    // Opening the page counts as using the backoffice.
    this.#lastInputAt = now();
  }

  /** Follows the session and the input from now on; the first poll starts it, once. */
  start(
    auth: IdleAuth,
    target: EventTarget = window,
    page: Document | undefined = typeof document === 'undefined' ? undefined : document,
  ): void {
    if (this.#started) return;
    this.#started = true;
    this.#keepUserLoggedIn = auth.keepUserLoggedIn;
    auth.session$.subscribe((session) => {
      // A renewal (in any tab) moves the access token's end; the session the page opened with is not one.
      const before = this.#session;
      this.#session = session;
      if (before && session && session.accessTokenExpiresAt > before.accessTokenExpiresAt)
        this.#renewedAt = this.#now();
      this.#wake();
    });
    for (const type of INPUT_EVENTS) target.addEventListener(type, this.#onInput, { capture: true, passive: true });
    page?.addEventListener('visibilitychange', () => {
      if (page.visibilityState === 'visible') this.#onInput();
    });
    if (typeof BroadcastChannel !== 'undefined') {
      this.#channel = new BroadcastChannel(CHANNEL);
      this.#channel.onmessage = (event: MessageEvent<{ at?: unknown }>) => {
        if (typeof event.data?.at !== 'number') return;
        this.#lastInputAt = Math.max(this.#lastInputAt, event.data.at);
        this.#wake();
      };
    }
  }

  /** True while a call now would keep an idle person signed in, or nobody is; a gate that follows no session holds nothing. */
  get idle(): boolean {
    if (!this.#started) return false;
    return pollWaitsForInput({
      now: this.#now(),
      accessTokenExpiresAt: this.#session?.accessTokenExpiresAt,
      sessionChangedAt: this.#renewedAt,
      lastInputAt: this.#lastInputAt,
      keepUserLoggedIn: this.#keepUserLoggedIn,
    });
  }

  /** Resolves at once, or when someone uses the backoffice, a session is renewed or begins, or the signal ends. */
  async untilActive(signal?: AbortSignal): Promise<void> {
    while (this.idle && !signal?.aborted) {
      await new Promise<void>((resolve) => {
        const wake = () => {
          this.#wakers.delete(wake);
          signal?.removeEventListener('abort', wake);
          resolve();
        };
        this.#wakers.add(wake);
        signal?.addEventListener('abort', wake, { once: true });
      });
    }
  }

  readonly #onInput = (): void => {
    const now = this.#now();
    this.#lastInputAt = now;
    if (now - this.#sharedAt >= SHARE_INTERVAL) {
      this.#sharedAt = now;
      this.#channel?.postMessage({ at: now });
    }

    this.#wake();
  };

  #wake(): void {
    for (const wake of [...this.#wakers]) wake();
  }
}

/** The gate of this browser tab. */
export const idleGate = new IdleGate();
