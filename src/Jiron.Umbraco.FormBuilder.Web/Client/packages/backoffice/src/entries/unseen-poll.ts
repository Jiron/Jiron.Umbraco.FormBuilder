// The Inbox's unseen count (ROADMAP 11.1, IA 1, ADR-0209): read when its menu item appears, when the tab comes back on
// screen, after the user opens or moves entries, and every 60 seconds while the tab is on screen; one read at a time.
// Every read waits at the idle gate first, so the poll never keeps an idle person signed in (section 4.10).

/** The count as the badge says it: up to 99, and whether there are more. */
export interface UnseenCount {
  readonly count: number;
  readonly more: boolean;
}

/** What the poll waits at before each read: the idle gate's wait. */
export interface PollGate {
  untilActive(signal?: AbortSignal): Promise<void>;
}

/** How often the count is read while the tab is on screen. */
export const POLL_MS = 60_000;

/** The polls listened to now, which a change of entries asks to read again. */
const running = new Set<UnseenPoll>();

/** Asks every running poll to read the count again, as after the user opened, moved or deleted entries. */
export function refreshUnseen(): void {
  for (const poll of running) poll.refresh();
}

export class UnseenPoll {
  readonly #read: () => Promise<UnseenCount | undefined>;
  readonly #gate: PollGate;
  readonly #page: Document | undefined;
  readonly #listeners = new Set<(count: UnseenCount) => void>();
  #last: UnseenCount | undefined;
  #timer: ReturnType<typeof setInterval> | undefined;
  #abort: AbortController | undefined;
  #reading = false;
  #again = false;

  constructor(
    read: () => Promise<UnseenCount | undefined>,
    gate: PollGate,
    page: Document | undefined = typeof document === 'undefined' ? undefined : document,
  ) {
    this.#read = read;
    this.#gate = gate;
    this.#page = page;
  }

  /** Hears each count read from now on, the last one at once; the first listener starts the poll, the last to go stops it. */
  listen(listener: (count: UnseenCount) => void): () => void {
    this.#listeners.add(listener);
    if (this.#last) listener(this.#last);
    if (this.#listeners.size === 1) this.#start();
    return () => {
      this.#listeners.delete(listener);
      if (this.#listeners.size === 0) this.#stop();
    };
  }

  /** Reads the count again as soon as the read under way, if any, is done. */
  refresh(): void {
    void this.#poll(true);
  }

  #start() {
    this.#abort = new AbortController();
    running.add(this);
    // A tab out of sight skips its turns; coming back on screen reads at once.
    this.#timer = setInterval(() => {
      if (!this.#page?.hidden) void this.#poll(false);
    }, POLL_MS);
    this.#page?.addEventListener('visibilitychange', this.#onVisible);
    void this.#poll(true);
  }

  #stop() {
    running.delete(this);
    clearInterval(this.#timer);
    this.#timer = undefined;
    this.#abort?.abort();
    this.#abort = undefined;
    this.#page?.removeEventListener('visibilitychange', this.#onVisible);
  }

  readonly #onVisible = () => {
    if (this.#page?.visibilityState === 'visible') void this.#poll(true);
  };

  /** One read, after the gate; a read asked for meanwhile follows it, while a turn of the timer is skipped. */
  async #poll(asked: boolean) {
    const abort = this.#abort;
    if (!abort) return;
    if (this.#reading) {
      this.#again ||= asked;
      return;
    }

    this.#reading = true;
    try {
      await this.#gate.untilActive(abort.signal);
      if (abort.signal.aborted) return;
      const count = await this.#read();
      if (!count || abort.signal.aborted) return;
      this.#last = count;
      for (const listener of this.#listeners) listener(count);
    } finally {
      this.#reading = false;
      // A read asked for meanwhile follows, for the poll that runs now: also the first read of a poll started again while
      // the stopped one's read was under way.
      if (this.#again && this.#abort && !this.#abort.signal.aborted) {
        this.#again = false;
        void this.#poll(true);
      }
    }
  }
}
