// The groups far from the canvas's view (section 4.12, ADR-0176): marked at the height they have, cleared when near,
// and watched against the canvas's viewport one view's height above and below, each group once and until it is gone.
import { afterEach, describe, expect, it } from 'vitest';
import { FarGroups, markFar, NEAR_MARGIN, type FarEntry } from './canvas-far';

const entry = (target: Element, isIntersecting: boolean, height = 0): FarEntry => ({
  target,
  isIntersecting,
  boundingClientRect: { height } as DOMRectReadOnly,
});

/** An observer that records what it is asked, which the test stands in for the browser's. */
class FakeObserver {
  static made: FakeObserver[] = [];
  readonly watched = new Set<Element>();
  disconnected = false;
  constructor(
    readonly report: (entries: FarEntry[]) => void,
    readonly options: IntersectionObserverInit,
  ) {
    FakeObserver.made.push(this);
  }
  observe(target: Element) {
    this.watched.add(target);
  }
  unobserve(target: Element) {
    this.watched.delete(target);
  }
  disconnect() {
    this.disconnected = true;
    this.watched.clear();
  }
}

const real = globalThis.IntersectionObserver;

afterEach(() => {
  globalThis.IntersectionObserver = real;
  FakeObserver.made = [];
  document.body.replaceChildren();
});

describe('a group far from the view', () => {
  it('is marked at the height it has, so nothing around it moves, and cleared when it comes near', () => {
    const group = document.createElement('fieldset');
    markFar([entry(group, false, 412.4)]);
    expect([group.hasAttribute('data-far'), group.style.getPropertyValue('contain-intrinsic-size')]).toEqual([
      true,
      'auto 412px',
    ]);
    markFar([entry(group, true)]);
    expect(group.hasAttribute('data-far')).toBe(false);
  });

  it("is watched against the canvas's viewport, each group once, and left once it is gone", () => {
    globalThis.IntersectionObserver = FakeObserver as unknown as typeof IntersectionObserver;
    const viewport = document.createElement('div');
    const [one, two] = [document.createElement('div'), document.createElement('div')];
    viewport.append(one, two);
    document.body.append(viewport);
    const far = new FarGroups();
    far.within(viewport);
    const observer = FakeObserver.made[0];
    expect(observer?.options).toEqual({ root: viewport, rootMargin: NEAR_MARGIN });
    expect(NEAR_MARGIN).toBe('100% 0px');
    far.watch([one, two]);
    far.watch([one, two]);
    expect([...(observer?.watched ?? [])]).toEqual([one, two]);
    observer?.report([entry(two, false, 300)]);
    expect(two.hasAttribute('data-far')).toBe(true);
    two.remove();
    far.watch([one]);
    expect([...(observer?.watched ?? [])]).toEqual([one]);
    // Another viewport, or none, ends the watch.
    far.within(null);
    expect([observer?.disconnected, FakeObserver.made.length]).toEqual([true, 1]);
    far.watch([one]);
  });

  it('is never marked where the browser has no IntersectionObserver', () => {
    globalThis.IntersectionObserver = undefined as unknown as typeof IntersectionObserver;
    const far = new FarGroups();
    far.within(document.createElement('div'));
    far.watch([document.createElement('div')]);
    far.stop();
    expect(FakeObserver.made).toEqual([]);
  });
});
