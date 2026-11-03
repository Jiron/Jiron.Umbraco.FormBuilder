// The backoffice's pointer drag (ADR-0016, ADR-0169): a press becomes a drag after 6 px, a drop swallows its click,
// Escape and a lost pointer end it without a drop, one drag runs at a time, and the area scrolls near its edge.
import { afterEach, describe, expect, it } from 'vitest';
import { DRAG_THRESHOLD, dragFrom, isDragging, SCROLL_EDGE, type DragHooks } from './pointer-drag';

// The values ADR-0016 names, so a changed constant fails here.
it('starts a drag after 6 px and scrolls within 48 px of an edge', () => {
  expect([DRAG_THRESHOLD, SCROLL_EDGE]).toEqual([6, 48]);
});

/** A pointer event as jsdom can make one: a mouse event with the pointer's id and whether it is the primary one. */
function pointer(type: string, x: number, y: number, extra: { id?: number; button?: number } = {}): PointerEvent {
  const event = new MouseEvent(type, {
    clientX: x,
    clientY: y,
    button: extra.button ?? 0,
    bubbles: true,
    cancelable: true,
  });
  Object.defineProperty(event, 'pointerId', { value: extra.id ?? 1 });
  Object.defineProperty(event, 'isPrimary', { value: true });
  return event as PointerEvent;
}

const frame = () => new Promise<void>((resolve) => requestAnimationFrame(() => resolve()));

function hooksLog() {
  const calls: string[] = [];
  const hooks: DragHooks = {
    begin: (x, y) => calls.push(`begin ${x},${y}`),
    over: (x, y) => calls.push(`over ${x},${y}`),
    drop: (x, y) => calls.push(`drop ${x},${y}`),
    cancel: () => calls.push('cancel'),
  };
  return { calls, hooks };
}

function handleOf(): HTMLButtonElement {
  const handle = document.createElement('button');
  document.body.append(handle);
  return handle;
}

afterEach(() => {
  // A test that leaves a drag going ends it, so the next one starts free.
  window.dispatchEvent(pointer('pointercancel', 0, 0));
  window.dispatchEvent(pointer('pointercancel', 0, 0, { id: 2 }));
  document.body.replaceChildren();
});

describe('a drag', () => {
  it('stays a click while the pointer moves less than 6 px', () => {
    const { calls, hooks } = hooksLog();
    const handle = handleOf();
    let clicks = 0;
    handle.addEventListener('click', () => clicks++);
    dragFrom(pointer('pointerdown', 10, 10), handle, hooks);
    window.dispatchEvent(pointer('pointermove', 10 + DRAG_THRESHOLD - 1, 10));
    window.dispatchEvent(pointer('pointerup', 10 + DRAG_THRESHOLD - 1, 10));
    handle.click();
    expect(calls).toEqual([]);
    expect(clicks).toBe(1);
    expect(isDragging()).toBe(false);
  });

  it('begins once the pointer has moved 6 px, follows it, and drops where it is let go', () => {
    const { calls, hooks } = hooksLog();
    const handle = handleOf();
    dragFrom(pointer('pointerdown', 10, 10), handle, hooks);
    expect(isDragging()).toBe(true);
    window.dispatchEvent(pointer('pointermove', 10, 10 + DRAG_THRESHOLD));
    window.dispatchEvent(pointer('pointermove', 40, 80));
    window.dispatchEvent(pointer('pointerup', 42, 82));
    expect(calls).toEqual(['begin 10,16', 'over 10,16', 'over 40,80', 'drop 42,82']);
    expect(isDragging()).toBe(false);
  });

  it("swallows the click a drop's release sends, and no later one", async () => {
    const { hooks } = hooksLog();
    const handle = handleOf();
    let clicks = 0;
    handle.addEventListener('click', () => clicks++);
    dragFrom(pointer('pointerdown', 0, 0), handle, hooks);
    window.dispatchEvent(pointer('pointermove', 0, 20));
    window.dispatchEvent(pointer('pointerup', 0, 20));
    handle.click();
    expect(clicks).toBe(0);
    await new Promise((resolve) => setTimeout(resolve, 5));
    handle.click();
    expect(clicks).toBe(1);
  });

  it('ends without a drop on Escape, which goes no further', () => {
    const { calls, hooks } = hooksLog();
    const handle = handleOf();
    let heard = 0;
    document.addEventListener('keydown', () => heard++);
    dragFrom(pointer('pointerdown', 0, 0), handle, hooks);
    window.dispatchEvent(pointer('pointermove', 0, 20));
    const escape = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
    handle.dispatchEvent(escape);
    window.dispatchEvent(pointer('pointerup', 0, 30));
    expect(calls).toEqual(['begin 0,20', 'over 0,20', 'cancel']);
    expect(escape.defaultPrevented).toBe(true);
    expect(heard).toBe(0);
  });

  it('leaves Escape to the page before the drag begins', () => {
    const { calls, hooks } = hooksLog();
    let heard = 0;
    document.addEventListener('keydown', () => heard++);
    dragFrom(pointer('pointerdown', 0, 0), handleOf(), hooks);
    const escape = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
    document.body.dispatchEvent(escape);
    expect([escape.defaultPrevented, heard, isDragging()]).toEqual([false, 1, true]);
    window.dispatchEvent(pointer('pointermove', 0, 20));
    window.dispatchEvent(pointer('pointerup', 0, 20));
    expect(calls).toEqual(['begin 0,20', 'over 0,20', 'drop 0,20']);
  });

  it('clicks nothing with the release that follows an Escape', async () => {
    const { hooks } = hooksLog();
    const handle = handleOf();
    let clicks = 0;
    handle.addEventListener('click', () => clicks++);
    dragFrom(pointer('pointerdown', 0, 0), handle, hooks);
    window.dispatchEvent(pointer('pointermove', 0, 20));
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
    window.dispatchEvent(pointer('pointerup', 0, 20));
    handle.click();
    expect(clicks).toBe(0);
    await new Promise((resolve) => setTimeout(resolve, 5));
    handle.click();
    expect(clicks).toBe(1);
  });

  it('lets a finger moving up or down scroll a handle that lets the page scroll, and drags it sideways', () => {
    const { calls, hooks } = hooksLog();
    const handle = handleOf();
    handle.style.touchAction = 'pan-y';
    const touch = (type: string, x: number, y: number) => {
      const event = pointer(type, x, y);
      Object.defineProperty(event, 'pointerType', { value: 'touch' });
      return event;
    };
    dragFrom(touch('pointerdown', 0, 0), handle, hooks);
    window.dispatchEvent(touch('pointermove', 2, 12));
    expect([calls, isDragging()]).toEqual([[], false]);
    dragFrom(touch('pointerdown', 0, 0), handle, hooks);
    window.dispatchEvent(touch('pointermove', 12, 2));
    window.dispatchEvent(touch('pointerup', 12, 2));
    expect(calls).toEqual(['begin 12,2', 'over 12,2', 'drop 12,2']);
  });

  it('ends without a drop when the browser takes the pointer', () => {
    const { calls, hooks } = hooksLog();
    dragFrom(pointer('pointerdown', 0, 0), handleOf(), hooks);
    window.dispatchEvent(pointer('pointermove', 0, 20));
    window.dispatchEvent(pointer('pointercancel', 0, 20));
    expect(calls.at(-1)).toBe('cancel');
    expect(isDragging()).toBe(false);
  });

  it('runs one at a time: a second press, or another pointer, while one lasts is ignored', () => {
    const first = hooksLog();
    const second = hooksLog();
    dragFrom(pointer('pointerdown', 0, 0), handleOf(), first.hooks);
    dragFrom(pointer('pointerdown', 0, 0, { id: 2 }), handleOf(), second.hooks);
    window.dispatchEvent(pointer('pointermove', 0, 30, { id: 2 }));
    window.dispatchEvent(pointer('pointermove', 0, 20));
    window.dispatchEvent(pointer('pointerup', 0, 20));
    expect(first.calls).toEqual(['begin 0,20', 'over 0,20', 'drop 0,20']);
    expect(second.calls).toEqual([]);
  });

  it('never starts from a button other than the main one', () => {
    const { calls, hooks } = hooksLog();
    dragFrom(pointer('pointerdown', 0, 0, { button: 2 }), handleOf(), hooks);
    expect(isDragging()).toBe(false);
    window.dispatchEvent(pointer('pointermove', 0, 30));
    expect(calls).toEqual([]);
  });

  it('scrolls its area while the pointer is near its top or bottom edge or below it, and not above it or beside it', async () => {
    const { calls, hooks } = hooksLog();
    const area = document.createElement('div');
    let scrolled = 100;
    Object.defineProperty(area, 'scrollTop', {
      get: () => scrolled,
      set: (value: number) => (scrolled = Math.max(0, value)),
    });
    area.getBoundingClientRect = () => ({ left: 0, top: 0, right: 300, bottom: 400 }) as DOMRect;
    document.body.append(area);
    dragFrom(pointer('pointerdown', 100, 200), handleOf(), { ...hooks, scroller: () => area });
    window.dispatchEvent(pointer('pointermove', 100, 400 - SCROLL_EDGE / 2));
    await frame();
    await frame();
    const down = scrolled;
    expect(down).toBeGreaterThan(100);
    expect(calls.filter((call) => call.startsWith('over')).length).toBeGreaterThan(1);
    window.dispatchEvent(pointer('pointermove', 100, 10));
    await frame();
    await frame();
    expect(scrolled).toBeLessThan(down);
    // Below the bottom edge, where a hand overshoots, it goes on scrolling down, as fast as it goes.
    window.dispatchEvent(pointer('pointermove', 100, 500));
    const below = scrolled;
    await frame();
    await frame();
    expect(scrolled).toBeGreaterThan(below);
    // Above the top edge lie the steps, and beside the area other things: it stays.
    for (const [x, y] of [
      [100, -20],
      [400, 390],
    ]) {
      window.dispatchEvent(pointer('pointermove', x ?? 0, y ?? 0));
      const still = scrolled;
      await frame();
      await frame();
      expect(scrolled, `at ${x},${y}`).toBe(still);
    }
    window.dispatchEvent(pointer('pointerup', 100, 500));
  });
});
