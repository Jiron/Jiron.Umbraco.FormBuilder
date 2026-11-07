// The Inbox's unseen poll (ROADMAP 11.1, ADR-0209): when it reads, that a tab out of sight skips its turns, that every
// read waits at the idle gate, that reads never pile up, and that it stops with its last listener.
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { POLL_MS, UnseenPoll, refreshUnseen, type PollGate, type UnseenCount } from './unseen-poll';

/** A page whose visibility the test sets. */
function pageOf() {
  const page = Object.assign(new EventTarget(), {
    hidden: false,
    visibilityState: 'visible' as DocumentVisibilityState,
  });
  const show = (visible: boolean) => {
    page.hidden = !visible;
    page.visibilityState = visible ? 'visible' : 'hidden';
    page.dispatchEvent(new Event('visibilitychange'));
  };
  return { page: page as unknown as Document, show };
}

/** A gate the test holds and lets go. */
function gateOf() {
  let held = false;
  const waiting: (() => void)[] = [];
  const gate: PollGate = {
    untilActive: () => (held ? new Promise<void>((resolve) => waiting.push(resolve)) : Promise.resolve()),
  };
  return {
    gate,
    hold: () => (held = true),
    release: () => {
      held = false;
      for (const resolve of waiting.splice(0)) resolve();
    },
  };
}

let reads: number;
let answer: UnseenCount;

const read = async () => {
  reads++;
  return answer;
};

beforeEach(() => {
  vi.useFakeTimers();
  reads = 0;
  answer = { count: 3, more: false };
});

afterEach(() => {
  vi.useRealTimers();
});

describe('the unseen poll', () => {
  it('reads when heard, every minute while on screen, and at once when the tab comes back', async () => {
    const { page, show } = pageOf();
    const poll = new UnseenPoll(read, gateOf().gate, page);
    const heard: UnseenCount[] = [];
    const stop = poll.listen((count) => heard.push(count));
    await vi.advanceTimersByTimeAsync(0);
    expect([reads, heard]).toEqual([1, [{ count: 3, more: false }]]);

    answer = { count: 99, more: true };
    await vi.advanceTimersByTimeAsync(POLL_MS);
    expect(heard.at(-1)).toEqual({ count: 99, more: true });

    // Out of sight, its turns pass without a read; back on screen, it reads at once.
    show(false);
    await vi.advanceTimersByTimeAsync(POLL_MS * 3);
    expect(reads).toBe(2);
    show(true);
    await vi.advanceTimersByTimeAsync(0);
    expect(reads).toBe(3);

    // A second listener hears the last count at once, without a read of its own.
    const late: UnseenCount[] = [];
    const stopLate = poll.listen((count) => late.push(count));
    expect([late, reads]).toEqual([[{ count: 99, more: true }], 3]);
    stopLate();
    stop();
  });

  it('waits at the gate before every read, and a refresh asked meanwhile reads once after it', async () => {
    const { page } = pageOf();
    const { gate, hold, release } = gateOf();
    const poll = new UnseenPoll(read, gate, page);
    hold();
    const stop = poll.listen(() => undefined);
    await vi.advanceTimersByTimeAsync(POLL_MS * 5);
    poll.refresh();
    poll.refresh();
    expect(reads).toBe(0);

    // Someone uses the backoffice: the held read goes, then the one refresh asked for; the timer's turns don't pile up.
    release();
    await vi.advanceTimersByTimeAsync(0);
    expect(reads).toBe(2);
    stop();
  });

  it('stops with its last listener, and refreshes only while it runs', async () => {
    const { page, show } = pageOf();
    const poll = new UnseenPoll(read, gateOf().gate, page);
    const stop = poll.listen(() => undefined);
    await vi.advanceTimersByTimeAsync(0);
    refreshUnseen();
    await vi.advanceTimersByTimeAsync(0);
    expect(reads).toBe(2);

    stop();
    refreshUnseen();
    show(true);
    await vi.advanceTimersByTimeAsync(POLL_MS * 2);
    expect(reads).toBe(2);
  });
});
