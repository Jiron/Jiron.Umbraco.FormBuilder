// The wait for the focus a form's workspace gives its name box as it opens (ROADMAP 10.2a, ADR-0188): over once the box
// holds the focus, at once where it does, and after the longest wait where no box takes it.
import { afterEach, describe, expect, it, vi } from 'vitest';
import { afterNameFocus, NAME_FOCUS_WAIT } from './name-focus';

/** A name box as Umbraco's workspace header draws one: its input inside its shadow root. */
function nameBox() {
  const box = document.createElement('umb-workspace-header-name-editable');
  const input = document.createElement('input');
  box.attachShadow({ mode: 'open' }).append(input);
  document.body.append(box);
  return input;
}

afterEach(() => {
  document.body.replaceChildren();
  vi.useRealTimers();
});

describe('the wait for the name box', () => {
  it('is over once the name box takes the focus, and not for a focus elsewhere', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    const other = document.body.appendChild(document.createElement('button'));
    const input = nameBox();
    let over = false;
    void afterNameFocus().then(() => (over = true));
    other.focus();
    await vi.advanceTimersByTimeAsync(NAME_FOCUS_WAIT - 1);
    expect(over).toBe(false);
    input.focus();
    await Promise.resolve();
    expect(over).toBe(true);
  });

  it('is over at once where the name box holds the focus', async () => {
    nameBox().focus();
    let over = false;
    void afterNameFocus().then(() => (over = true));
    await Promise.resolve();
    expect(over).toBe(true);
  });

  it('is over after the longest wait where no name box takes the focus', async () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    let over = false;
    void afterNameFocus().then(() => (over = true));
    await vi.advanceTimersByTimeAsync(NAME_FOCUS_WAIT);
    expect(over).toBe(true);
  });
});
