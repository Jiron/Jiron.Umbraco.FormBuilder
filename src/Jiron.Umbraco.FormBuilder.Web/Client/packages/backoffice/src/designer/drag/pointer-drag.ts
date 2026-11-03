// The backoffice's one pointer drag (ADR-0016, ADR-0169), for the designer and every reorderable list: it starts once
// the pointer has moved 6 px from where it went down on a handle, holds the pointer while it lasts, scrolls the list's
// scrolling area while the pointer is within 48 px of its top or bottom edge, or below its bottom edge, where a hand
// overshoots (above its top lie the page's steps, which take drops themselves), and ends with a drop, or without one on
// Escape or a lost pointer. A click that ends a drag, or the release after Escape ended one, does nothing. On a handle
// that lets the page scroll up and down (`touch-action: pan-y`), a finger moving mostly up or down scrolls the page and
// starts no drag. What is dragged, where it may land and what a drop does are the caller's.

/** How far the pointer moves before a press on a handle becomes a drag. */
export const DRAG_THRESHOLD = 6;

/** How near the scrolling area's edge the pointer scrolls it. */
export const SCROLL_EDGE = 48;

/** How far the area scrolls in a frame with the pointer at its very edge. */
const SCROLL_MOST = 16;

/** What a drag tells its caller. Each place is the pointer's, in the window's coordinates. */
export interface DragHooks {
  /** The pointer has moved far enough: the drag has begun. */
  begin(x: number, y: number): void;
  /** The pointer is here now, after it moved or the area scrolled under it. */
  over(x: number, y: number): void;
  /** The pointer was let go here. */
  drop(x: number, y: number): void;
  /** Escape or a lost pointer ended the drag without a drop. */
  cancel(): void;
  /** The area the drag scrolls while the pointer is near its edge, if any. */
  scroller?(): HTMLElement | null;
}

// One drag at a time: a second pointer while one lasts is ignored, so switching fingers never makes the drag jump.
let dragging = false;

/** Whether a drag is under way. */
export const isDragging = () => dragging;

/**
 * Follows a press on a handle: nothing happens until the pointer moves 6 px, and a press that never does stays a
 * click. Call it from the handle's `pointerdown`.
 */
export function dragFrom(event: PointerEvent, handle: Element, hooks: DragHooks): void {
  if (dragging || event.button !== 0 || event.isPrimary === false) return;
  const view = handle.ownerDocument.defaultView;
  if (!view) return;
  dragging = true;
  const id = event.pointerId;
  const start = { x: event.clientX, y: event.clientY };
  const scrolls = event.pointerType === 'touch' && view.getComputedStyle(handle).touchAction === 'pan-y';
  let last = start;
  let begun = false;
  let frame = 0;

  const same = (other: PointerEvent) => other.pointerId === id;

  const scroll = () => {
    const area = hooks.scroller?.();
    if (area) {
      const box = area.getBoundingClientRect();
      const across = last.x >= box.left && last.x <= box.right;
      const fromTop = last.y - box.top;
      const fromBottom = box.bottom - last.y;
      // Faster nearer the edge, and as fast as it goes at it and below it.
      const speed = (from: number) =>
        Math.ceil((Math.min(SCROLL_EDGE, SCROLL_EDGE - from) / SCROLL_EDGE) * SCROLL_MOST);
      const by = !across
        ? 0
        : fromTop >= 0 && fromTop < SCROLL_EDGE
          ? -speed(fromTop)
          : fromTop >= 0 && fromBottom < SCROLL_EDGE
            ? speed(fromBottom)
            : 0;
      if (by !== 0) {
        const before = area.scrollTop;
        area.scrollTop = before + by;
        if (area.scrollTop !== before) hooks.over(last.x, last.y);
      }
    }
    frame = view.requestAnimationFrame(scroll);
  };

  const move = (moved: PointerEvent) => {
    if (!same(moved)) return;
    last = { x: moved.clientX, y: moved.clientY };
    if (!begun) {
      if (Math.hypot(last.x - start.x, last.y - start.y) < DRAG_THRESHOLD) return;
      // The browser scrolls the page under the finger: no drag.
      if (scrolls && Math.abs(last.y - start.y) >= Math.abs(last.x - start.x)) {
        finish();
        return;
      }
      begun = true;
      try {
        handle.setPointerCapture(id);
      } catch {
        // A pointer the browser no longer knows: the window still hears it.
      }
      hooks.begin(last.x, last.y);
      frame = view.requestAnimationFrame(scroll);
    }
    moved.preventDefault();
    hooks.over(last.x, last.y);
  };

  const up = (released: PointerEvent) => {
    if (!same(released)) return;
    finish();
    if (!begun) return;
    swallowClick(view);
    hooks.drop(released.clientX, released.clientY);
  };

  const lost = (cancelled: PointerEvent) => {
    if (!same(cancelled)) return;
    finish();
    if (begun) hooks.cancel();
  };

  const key = (pressed: KeyboardEvent) => {
    // Before the drag begins, Escape is the page's as ever.
    if (pressed.key !== 'Escape' || !begun) return;
    // Escape ends the drag and nothing else: the selection stays as it was, and the release clicks nothing.
    pressed.preventDefault();
    pressed.stopPropagation();
    finish();
    hooks.cancel();
    view.addEventListener('pointerup', () => swallowClick(view), { capture: true, once: true });
  };

  function finish() {
    dragging = false;
    view?.cancelAnimationFrame(frame);
    view?.removeEventListener('pointermove', move, true);
    view?.removeEventListener('pointerup', up, true);
    view?.removeEventListener('pointercancel', lost, true);
    view?.removeEventListener('keydown', key, true);
    try {
      if (handle.hasPointerCapture?.(id)) handle.releasePointerCapture(id);
    } catch {
      // Released already.
    }
  }

  view.addEventListener('pointermove', move, true);
  view.addEventListener('pointerup', up, true);
  view.addEventListener('pointercancel', lost, true);
  view.addEventListener('keydown', key, true);
}

// The click a browser sends once a drag's pointer is let go goes nowhere.
function swallowClick(view: Window) {
  const stop = (click: Event) => {
    click.preventDefault();
    click.stopPropagation();
  };
  view.addEventListener('click', stop, { capture: true, once: true });
  view.setTimeout(() => view.removeEventListener('click', stop, true), 0);
}
