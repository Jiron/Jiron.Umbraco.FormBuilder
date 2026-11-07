// The idle gate (ROADMAP 4.10, ADR-0209): a poll waits, before an expired token is renewed, while nobody used the
// backoffice since the last renewal; input, another tab's renewal or the end of its signal lets it go, and a site that
// keeps users signed in never waits. Once the session has ended, a poll waits for the next sign-in.
import type { UmbAuthSession } from '@umbraco-cms/backoffice/auth';
import { describe, expect, it } from 'vitest';
import { IdleGate, pollWaitsForInput } from './idle-gate';

function setup(keepUserLoggedIn = false) {
  let now = 1_000;
  let emit: (session: UmbAuthSession | undefined) => void = () => undefined;
  let present: UmbAuthSession | undefined = { accessTokenExpiresAt: 1_300, expiresAt: 2_200 };
  const gate = new IdleGate(() => now);
  const target = new EventTarget();
  gate.start(
    {
      keepUserLoggedIn,
      session$: {
        // As Umbraco's state, it says the session it holds at once.
        subscribe(next) {
          emit = next;
          next(present);
        },
      },
    },
    target,
    undefined,
  );
  return {
    gate,
    at: (seconds: number) => {
      now = seconds;
    },
    session: (accessTokenExpiresAt: number) => {
      present = { accessTokenExpiresAt, expiresAt: accessTokenExpiresAt + 900 };
      emit(present);
    },
    timedOut: () => {
      present = undefined;
      emit(undefined);
    },
    input: () => target.dispatchEvent(new Event('pointerdown')),
  };
}

const settled = async (promise: Promise<void>): Promise<boolean> =>
  Promise.race([promise.then(() => true), new Promise<boolean>((resolve) => setTimeout(() => resolve(false), 20))]);

describe('the idle gate', () => {
  it('waits only for an expired token, renewed since the last input, on a site that signs idle people out', () => {
    const base = { now: 100, accessTokenExpiresAt: 90, sessionChangedAt: 50, lastInputAt: 40, keepUserLoggedIn: false };
    expect(pollWaitsForInput(base)).toBe(true);
    expect(pollWaitsForInput({ ...base, accessTokenExpiresAt: 120 })).toBe(false);
    expect(pollWaitsForInput({ ...base, lastInputAt: 60 })).toBe(false);
    expect(pollWaitsForInput({ ...base, keepUserLoggedIn: true })).toBe(false);
    expect(pollWaitsForInput({ ...base, accessTokenExpiresAt: undefined }), 'nobody is signed in').toBe(true);
    expect(pollWaitsForInput({ ...base, accessTokenExpiresAt: undefined, keepUserLoggedIn: true })).toBe(true);
  });

  it('holds calls once the session has ended, until the next sign-in, and nothing before it follows one', async () => {
    expect(new IdleGate(() => 1_000).idle, 'a gate that follows no session').toBe(false);

    const t = setup();
    expect(t.gate.idle).toBe(false);
    t.timedOut();
    expect(t.gate.idle).toBe(true);
    const waiting = t.gate.untilActive();
    t.input();
    expect(await settled(waiting), 'input alone signs nobody in').toBe(false);
    t.session(2_000);
    expect(await settled(waiting)).toBe(true);
    expect(t.gate.idle).toBe(false);
  });

  it('lets calls go while the token is valid, and once after the page opened', async () => {
    const t = setup();
    t.session(1_300);
    expect(t.gate.idle).toBe(false);
    t.at(1_400);
    expect(t.gate.idle, 'opening the page counts as use').toBe(false);
    expect(await settled(t.gate.untilActive())).toBe(true);
  });

  it('holds calls after a renewal nobody asked for, until someone uses the backoffice', async () => {
    const t = setup();
    t.session(1_300);
    t.at(1_310);
    t.session(1_610);
    t.at(1_700);
    expect(t.gate.idle).toBe(true);
    const waiting = t.gate.untilActive();
    expect(await settled(waiting)).toBe(false);
    t.input();
    expect(await settled(waiting)).toBe(true);
    expect(t.gate.idle).toBe(false);
  });

  it('lets calls go when another tab renewed the session', async () => {
    const t = setup();
    t.session(1_300);
    t.at(1_310);
    t.session(1_610);
    t.at(1_700);
    const waiting = t.gate.untilActive();
    t.session(1_900);
    expect(await settled(waiting)).toBe(true);
  });

  it('never holds a site that keeps users signed in, and ends a wait when its signal ends', async () => {
    const kept = setup(true);
    kept.session(1_300);
    kept.at(1_310);
    kept.session(1_610);
    kept.at(1_700);
    expect(kept.gate.idle).toBe(false);

    const t = setup();
    t.session(1_300);
    t.at(1_310);
    t.session(1_610);
    t.at(1_700);
    const abort = new AbortController();
    const waiting = t.gate.untilActive(abort.signal);
    abort.abort();
    expect(await settled(waiting)).toBe(true);
  });
});
