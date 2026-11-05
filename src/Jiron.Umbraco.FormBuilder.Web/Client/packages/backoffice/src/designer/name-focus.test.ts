// The wait for the focus a form's workspace gives its name box as it opens (ROADMAP 10.2a, ADR-0188): over once the box
// has held the focus past its next look, counted from its last take, from the start where it has it already; after the
// longest wait where no box takes it; and over without the link's focus once the user presses something first.
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { afterNameFocus, NAME_FOCUS_HOLD, NAME_FOCUS_WAIT } from './name-focus';

/** A name box as Umbraco's workspace header draws one: its input inside its shadow root. */
function nameBox() {
  const box = document.createElement('umb-workspace-header-name-editable');
  const input = document.createElement('input');
  box.attachShadow({ mode: 'open' }).append(input);
  document.body.append(box);
  return input;
}

/** The wait, and what it came to so far: undefined while it lasts. */
function waiting() {
  const state: { free: boolean | undefined } = { free: undefined };
  void afterNameFocus().then((free) => (state.free = free));
  return state;
}

beforeEach(() => {
  vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
});

afterEach(() => {
  document.body.replaceChildren();
  vi.useRealTimers();
});

describe('the wait for the name box', () => {
  it('is over once the name box has held the focus past its next look, and not for a focus elsewhere', async () => {
    const other = document.body.appendChild(document.createElement('button'));
    const input = nameBox();
    const wait = waiting();
    other.focus();
    await vi.advanceTimersByTimeAsync(NAME_FOCUS_WAIT - NAME_FOCUS_HOLD - 1);
    expect(wait.free).toBeUndefined();
    input.focus();
    await vi.advanceTimersByTimeAsync(NAME_FOCUS_HOLD - 1);
    expect(wait.free).toBeUndefined();
    await vi.advanceTimersByTimeAsync(1);
    expect(wait.free).toBe(true);
  });

  it("counts the hold from the box's last take of the focus", async () => {
    const other = document.body.appendChild(document.createElement('button'));
    const input = nameBox();
    const wait = waiting();
    input.focus();
    await vi.advanceTimersByTimeAsync(NAME_FOCUS_HOLD - 50);
    other.focus();
    input.focus();
    await vi.advanceTimersByTimeAsync(100);
    expect(wait.free).toBeUndefined();
    await vi.advanceTimersByTimeAsync(NAME_FOCUS_HOLD);
    expect(wait.free).toBe(true);
  });

  it('holds from the start where the name box has the focus already', async () => {
    nameBox().focus();
    const wait = waiting();
    await vi.advanceTimersByTimeAsync(NAME_FOCUS_HOLD - 1);
    expect(wait.free).toBeUndefined();
    await vi.advanceTimersByTimeAsync(1);
    expect(wait.free).toBe(true);
  });

  it("is over without the link's focus once the user presses a key or a pointer first", async () => {
    for (const press of [
      new KeyboardEvent('keydown', { key: 'Tab', bubbles: true }),
      new Event('pointerdown', { bubbles: true }),
    ]) {
      nameBox().focus();
      const wait = waiting();
      document.body.dispatchEvent(press);
      await vi.advanceTimersByTimeAsync(0);
      expect(wait.free).toBe(false);
      document.body.replaceChildren();
    }
  });

  it('is over after the longest wait where no name box takes the focus', async () => {
    const wait = waiting();
    await vi.advanceTimersByTimeAsync(NAME_FOCUS_WAIT);
    expect(wait.free).toBe(true);
  });
});
