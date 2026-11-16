// The moves between the form's views (ROADMAP 9.3a, 9.5f): the address of a view, the view an address shows, Design at
// the form's own address, no move to the view shown, which Umbraco's router would build anew, and a Fix it led to the
// view that makes it, handed on only to a view that takes it up (ADR-0184); and the wait until the router stays still,
// before a sidebar opens for a Fix it handed on (ADR-0227).
import { afterEach, describe, expect, it, vi } from 'vitest';
import type { FixTarget } from './check/form-check';
import {
  FIRST_VIEW,
  followFix,
  goToView,
  ROUTER_QUIET,
  ROUTER_WAIT,
  routerSettled,
  shownView,
  viewPath,
} from './form-views';

const FORM = '/umbraco/section/jfb-forms/workspace/jfb-form/edit/3f2504e0-4f89-41d3-9a0c-0305e82c3301';

afterEach(() => {
  history.replaceState(null, '', '/');
  vi.restoreAllMocks();
});

describe("the form's views", () => {
  it('put a view in place of the one an address names, or after the form', () => {
    expect(viewPath(FORM, 'settings')).toBe(`${FORM}/view/settings`);
    expect(viewPath(`${FORM}/`, 'settings')).toBe(`${FORM}/view/settings`);
    expect(viewPath(`${FORM}/view/design`, 'actions')).toBe(`${FORM}/view/actions`);
  });

  it("read the view an address shows, Design at the form's own address", () => {
    expect(shownView(`${FORM}/view/settings`)).toBe('settings');
    expect(shownView(`${FORM}/view/settings/`)).toBe('settings');
    expect(shownView(FORM)).toBe(FIRST_VIEW);
    expect(FIRST_VIEW).toBe('design');
  });

  it('move to another view, and not to the one shown', () => {
    const moves: string[] = [];
    vi.spyOn(window, 'dispatchEvent').mockImplementation((event) => {
      if (event.type === 'popstate') moves.push(window.location.pathname);
      return true;
    });
    history.replaceState(null, '', FORM);
    goToView('design');
    expect(moves).toEqual([]);
    goToView('settings');
    expect(moves).toEqual([`${FORM}/view/settings`]);
    goToView('settings');
    goToView('design');
    expect(moves).toEqual([`${FORM}/view/settings`, `${FORM}/view/design`]);
  });

  it('lead a Fix it to its view, handing it on only to one that takes it up', () => {
    const moves: string[] = [];
    vi.spyOn(window, 'dispatchEvent').mockImplementation((event) => {
      if (event.type === 'popstate') moves.push(window.location.pathname);
      return true;
    });
    history.replaceState(null, '', `${FORM}/view/design`);
    const handed: FixTarget[] = [];
    const settings: FixTarget = { kind: 'view', view: 'settings', path: '$.settings.availability.members' };
    followFix(settings, (fix) => handed.push(fix));
    expect(handed).toEqual([settings]);
    expect(moves).toEqual([`${FORM}/view/settings`]);
    // The Actions view takes one up too (ROADMAP 10.2a).
    const actions: FixTarget = { kind: 'view', view: 'actions', path: '$.actions[0]', key: 'a' };
    followFix(actions, (fix) => handed.push(fix));
    expect(handed).toEqual([settings, actions]);
    expect(moves).toEqual([`${FORM}/view/settings`, `${FORM}/view/actions`]);
    const element: FixTarget = { kind: 'picker', page: null };
    followFix(element, (fix) => handed.push(fix));
    expect(handed).toEqual([settings, actions, element]);
    expect(moves).toEqual([`${FORM}/view/settings`, `${FORM}/view/actions`, `${FORM}/view/design`]);
  });
});

describe('the wait until the router stays still', () => {
  afterEach(() => vi.useRealTimers());

  /** Whether the wait is over after these ms, the router moving at the times given. */
  async function overAfter(ms: number, moves: readonly { at: number; type: string }[] = []): Promise<boolean> {
    vi.useFakeTimers();
    const router = new EventTarget();
    let over = false;
    void routerSettled(router).then(() => (over = true));
    let now = 0;
    for (const move of moves.filter((one) => one.at < ms).sort((a, b) => a.at - b.at)) {
      await vi.advanceTimersByTimeAsync(move.at - now);
      now = move.at;
      router.dispatchEvent(new Event(move.type));
    }
    await vi.advanceTimersByTimeAsync(ms - now);
    return over;
  }

  it('is over once the router has not moved for its quiet time, and not before', async () => {
    expect(ROUTER_QUIET).toBe(200);
    expect(await overAfter(ROUTER_QUIET - 1)).toBe(false);
    expect(await overAfter(ROUTER_QUIET)).toBe(true);
  });

  it("starts again with each move of the router: a navigation, its end, the history's own", async () => {
    for (const type of [
      'navigationstart',
      'navigationsuccess',
      'navigationend',
      'navigationcancel',
      'popstate',
      'changestate',
    ]) {
      const moves = [{ at: 150, type }];
      expect(await overAfter(150 + ROUTER_QUIET - 1, moves), type).toBe(false);
      expect(await overAfter(150 + ROUTER_QUIET, moves), type).toBe(true);
    }
    // Another event is no move of the router.
    expect(await overAfter(ROUTER_QUIET, [{ at: 150, type: 'click' }])).toBe(true);
  });

  it('is over at its longest wait, however long the router keeps moving', async () => {
    expect(ROUTER_WAIT).toBe(2000);
    const moves = Array.from({ length: 40 }, (_, index) => ({ at: (index + 1) * 100, type: 'navigationsuccess' }));
    expect(await overAfter(ROUTER_WAIT - 1, moves)).toBe(false);
    expect(await overAfter(ROUTER_WAIT, moves)).toBe(true);
  });

  it('stops listening once it is over', async () => {
    vi.useFakeTimers();
    const router = new EventTarget();
    const removed = vi.spyOn(router, 'removeEventListener');
    const added = vi.spyOn(router, 'addEventListener');
    const over = routerSettled(router);
    await vi.advanceTimersByTimeAsync(ROUTER_QUIET);
    await over;
    expect(removed.mock.calls.map(([type]) => type)).toEqual(added.mock.calls.map(([type]) => type));
    expect(vi.getTimerCount()).toBe(0);
  });
});
