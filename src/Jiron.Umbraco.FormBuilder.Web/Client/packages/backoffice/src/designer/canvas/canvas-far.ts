// The groups of the page shown that lie far from the canvas's view (section 4.12, ADR-0176). A group a whole view above
// or below what is in sight is marked far, and the browser then skips its styling, layout and painting until it comes
// near again (`content-visibility`, canvas.styles.ts), so a form of hundreds of fields costs what a screenful costs at
// every selection and edit. A group in reach is never contained, so the controls at its edges stack and take clicks
// as they always do; a far group keeps the height it had, so nothing around it moves.

/** How far beyond the view a group still counts as near: one view's height above and below. */
export const NEAR_MARGIN = '100% 0px';

/** What the canvas reads of an observer's entry. */
export type FarEntry = Pick<IntersectionObserverEntry, 'target' | 'isIntersecting' | 'boundingClientRect'>;

/** Marks each group the entries say is far, at the height it has now, and clears the mark of each that is near. */
export function markFar(entries: readonly FarEntry[]): void {
  for (const entry of entries) {
    const group = entry.target as HTMLElement;
    if (entry.isIntersecting) {
      group.removeAttribute('data-far');
      continue;
    }
    group.style.setProperty('contain-intrinsic-size', `auto ${Math.round(entry.boundingClientRect.height)}px`);
    group.setAttribute('data-far', '');
  }
}

/** Watches the groups of a canvas's page against its viewport; none where the browser has no IntersectionObserver. */
export class FarGroups {
  #observer: IntersectionObserver | null = null;
  readonly #watched = new Set<Element>();

  /** Watches against another viewport, or none. */
  within(viewport: Element | null): void {
    this.stop();
    if (viewport && typeof IntersectionObserver === 'function')
      this.#observer = new IntersectionObserver((entries) => markFar(entries), {
        root: viewport,
        rootMargin: NEAR_MARGIN,
      });
  }

  /** Watches the groups given, and leaves those gone from the page. */
  watch(groups: Iterable<Element>): void {
    const observer = this.#observer;
    if (!observer) return;
    for (const group of this.#watched)
      if (!group.isConnected) {
        observer.unobserve(group);
        this.#watched.delete(group);
      }
    for (const group of groups)
      if (!this.#watched.has(group)) {
        observer.observe(group);
        this.#watched.add(group);
      }
  }

  /** Ends the watch. */
  stop(): void {
    this.#observer?.disconnect();
    this.#observer = null;
    this.#watched.clear();
  }
}
