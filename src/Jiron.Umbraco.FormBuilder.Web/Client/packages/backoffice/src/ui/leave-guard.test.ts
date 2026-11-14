// A screen with changes not saved yet asks before the backoffice leaves it (ROADMAP 21.1): the router's event is
// stopped, Umbraco's dialog asks, Discard goes on to the address, and anything else stays.
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { testHost } from '../testing';

const dialog = vi.hoisted(() => ({ opened: 0, answer: 'discard' as 'discard' | 'stay' }));

vi.mock('@umbraco-cms/backoffice/modal', async (original) => ({
  ...(await original<typeof import('@umbraco-cms/backoffice/modal')>()),
  umbOpenModal: async () => {
    dialog.opened += 1;
    if (dialog.answer === 'stay') throw new Error('stay');
  },
}));

const { LeaveGuard } = await import('./leave-guard');

const settled = () => new Promise((resolve) => setTimeout(resolve, 0));

/** A screen on the page with its guard, once the guard has joined it. */
async function guarded(changed: () => boolean) {
  const host = testHost();
  new LeaveGuard(host, changed);
  await settled();
  return host;
}

/** The router's event for an address, as it raises it before the address changes. */
function leaving(url: string): CustomEvent {
  const event = new CustomEvent('willchangestate', { detail: { url }, cancelable: true });
  window.dispatchEvent(event);
  return event;
}

beforeEach(() => {
  dialog.opened = 0;
  dialog.answer = 'discard';
  history.replaceState({}, '', '/umbraco/section/jfb-forms/settings/general');
});

afterEach(() => {
  document.body.replaceChildren();
  vi.restoreAllMocks();
});

describe('leaving a screen', () => {
  it('goes on without a word while nothing has changed', async () => {
    await guarded(() => false);
    expect(leaving('/umbraco/section/content').defaultPrevented).toBe(false);
    expect(dialog.opened).toBe(0);
  });

  it('asks while changes are not saved, and Discard goes on to the address asked for', async () => {
    await guarded(() => true);
    const pushed = vi.spyOn(history, 'pushState');
    expect(leaving('/umbraco/section/content').defaultPrevented).toBe(true);
    await settled();
    expect(dialog.opened).toBe(1);
    expect(pushed).toHaveBeenCalledWith({}, '', '/umbraco/section/content');
  });

  it('lets the address it pushes after Discard through, and asks again the next time', async () => {
    await guarded(() => true);
    // As the router does: pushing an address raises the event first.
    const raised: boolean[] = [];
    vi.spyOn(history, 'pushState').mockImplementation((_state, _unused, url) => {
      raised.push(leaving(String(url)).defaultPrevented);
    });
    leaving('/umbraco/section/content');
    await settled();
    expect(raised, 'the push after Discard goes through').toEqual([false]);
    expect(leaving('/umbraco/section/media').defaultPrevented, 'a later leaving asks again').toBe(true);
  });

  it('stays, with nothing pushed, when the dialog is closed any other way', async () => {
    dialog.answer = 'stay';
    await guarded(() => true);
    const pushed = vi.spyOn(history, 'pushState');
    expect(leaving('/umbraco/section/content').defaultPrevented).toBe(true);
    await settled();
    expect(pushed).not.toHaveBeenCalled();
  });

  it('takes an address below its own, such as a dialog the address names, as staying', async () => {
    await guarded(() => true);
    expect(leaving('/umbraco/section/jfb-forms/settings/general/modal/media-picker').defaultPrevented).toBe(false);
  });

  it('asks no more once its screen has left the page', async () => {
    const host = await guarded(() => true);
    host.remove();
    expect(leaving('/umbraco/section/content').defaultPrevented).toBe(false);
  });
});
