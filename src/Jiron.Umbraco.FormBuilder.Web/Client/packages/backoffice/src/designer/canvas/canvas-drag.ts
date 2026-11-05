// The canvas's drags (ADR-0016, ADR-0169). A field moves between the fields of the groups and repeatable groups of the
// page shown, a group between the groups of that page, a page between the steps, and a field type from the palette
// lands where a field would. Over a page's step, a field or group lands at the end of that page, and resting there
// shows the page. While it lasts, a dashed slot keeps the dragged element's place, a copy with its name follows the
// pointer, and a line shows where it would land, in the colour of a refusal and with the reason beside it where the
// form can't take it there. The drop itself is the Design view's, through the canvas's request.
import { containerAt, holds, indexAt, lineAt, type Box } from '../drag/drop-place';
import { dragFrom } from '../drag/pointer-drag';
import type { Place } from '../editor-model';

/** What is dragged: an element of the form by its key, or a new field of a type. */
export type DragSource =
  | { readonly kind: 'field' | 'group' | 'page'; readonly key: string; readonly label: string }
  | { readonly kind: 'new'; readonly type: string; readonly label: string };

/** How long the pointer rests on a page's step before that page shows. */
export const STEP_DWELL_MS = 600;

/** How long a drop takes to settle from where the dragged copy was. */
export const SETTLE_MS = 150;

/** How far above or below the steps a dragged page still lands among them. */
const STEP_REACH = 24;

/** What the canvas gives its drags. */
export interface CanvasDragHost {
  /** The canvas's shadow root, which holds the steps, the page shown and the overlay. */
  root(): ShadowRoot;
  /** The key of the page shown, or null after sending. */
  shownPage(): string | null;
  /** The place at the end of a page for what is dragged; null where there is none. */
  pageEnd(source: DragSource, page: string): Place | null;
  /** Why the form can't take it there, in words; null when it can. */
  refusalOf(source: DragSource, place: Place | null): string | null;
  /** Shows a page, whose step the pointer rests on. */
  show(page: string): void;
  /** It was dropped where the form takes it. */
  drop(source: DragSource, place: Place): void;
  /** It was dropped where the form doesn't take it. */
  refused(source: DragSource, reason: string): void;
}

interface Target {
  readonly place: Place | null;
  readonly reason: string | null;
  /** The line across a container, or down between the steps; none for a step's page end. */
  readonly line: {
    readonly left: number;
    readonly top: number;
    readonly width: number;
    readonly height: number;
  } | null;
  /** The step the pointer is over, or null. */
  readonly step: Element | null;
  /** The area the marks stay inside. */
  readonly area: Box | null;
}

const rect = (element: Element): Box => element.getBoundingClientRect();
const childrenMatching = (parent: Element, selector: string) =>
  [...parent.children].filter((child) => child.matches(selector));

export class CanvasDrag {
  readonly #host: CanvasDragHost;
  #source: DragSource | null = null;
  #target: Target | null = null;
  #layer: HTMLElement | null = null;
  #dragged: Element | null = null;
  #dwell = 0;
  #dwellOn: string | null = null;
  /** Where the dragged copy was when it was dropped, and what it was, for the settle. */
  #dropped: { readonly from: Box; readonly source: DragSource } | null = null;
  /** Each place's refusal, asked once while the pointer stays over it: the form is checked once per place. */
  #reasons = new Map<string, string | null>();

  constructor(host: CanvasDragHost) {
    this.#host = host;
  }

  /** Whether a drag of this canvas is under way. */
  get active(): boolean {
    return this.#source !== null;
  }

  /** Follows a press on a handle, a step or a palette item; a press that never moves stays a click. */
  start(event: PointerEvent, handle: Element, source: DragSource): void {
    dragFrom(event, handle, {
      begin: (x, y) => this.#begin(source, x, y),
      over: (x, y) => this.#over(x, y),
      drop: (x, y) => this.#drop(x, y),
      cancel: () => this.#end(),
      scroller: () => (source.kind === 'page' ? null : this.#host.root().querySelector('.bo-viewport')),
    });
  }

  /**
   * Settles what a drop just placed from where the dragged copy was, over 150 ms with a strong ease-out, so the eye
   * follows where it landed; at once with reduced motion, and never after a keyboard move. A new field is found by its
   * key, which the drop selected; an element the form didn't move settles back into its place.
   */
  settle(selected: string | null): void {
    const dropped = this.#dropped;
    this.#dropped = null;
    if (!dropped) return;
    const { from, source } = dropped;
    const element =
      source.kind === 'new'
        ? selected
          ? this.#host.root().querySelector(`jfb-canvas-field[key="${selected}"]`)
          : null
        : this.#elementOf(source);
    if (!element || typeof element.animate !== 'function') return;
    const view = element.ownerDocument.defaultView;
    if (view?.matchMedia?.('(prefers-reduced-motion: reduce)').matches) return;
    const to = rect(element);
    const x = from.left - to.left;
    const y = from.top - to.top;
    if (Math.abs(x) < 1 && Math.abs(y) < 1) return;
    element.animate([{ transform: `translate(${x}px, ${y}px)` }, { transform: 'none' }], {
      duration: SETTLE_MS,
      easing: 'cubic-bezier(0.23, 1, 0.32, 1)',
    });
  }

  #begin(source: DragSource, x: number, y: number) {
    this.#source = source;
    this.#reasons.clear();
    this.#dragged = this.#elementOf(source);
    this.#dragged?.setAttribute('data-dragged', '');
    this.#host.root().host.setAttribute('data-dragging', '');
    const layer = document.createElement('div');
    layer.className = 'bo-drag';
    layer.setAttribute('popover', 'manual');
    layer.setAttribute('aria-hidden', 'true');
    const marks = document.createElement('div');
    marks.className = 'bo-marks';
    const slot = document.createElement('div');
    slot.className = 'bo-slot';
    // A field's or a group's place lies on the white sheet, a page's among the steps on Umbraco's surface.
    slot.toggleAttribute('data-sheet', source.kind !== 'page');
    const line = document.createElement('div');
    line.className = 'bo-line';
    marks.append(slot, line);
    // The copy and, where the form refuses the place, why: both follow the pointer, never cut off by an area's edge.
    const follow = document.createElement('div');
    follow.className = 'bo-follow';
    const ghost = document.createElement('div');
    ghost.className = 'bo-ghost';
    const grip = document.createElement('uui-icon');
    grip.setAttribute('name', source.kind === 'new' ? 'icon-add' : 'icon-grip');
    const name = document.createElement('span');
    name.textContent = source.label;
    ghost.append(grip, name);
    const why = document.createElement('p');
    why.className = 'bo-why';
    follow.append(ghost, why);
    layer.append(marks, follow);
    this.#host.root().append(layer);
    // The top layer: above the backoffice's own layers, and never cut off by a scrolling area.
    (layer as HTMLElement & { showPopover?: () => void }).showPopover?.();
    this.#layer = layer;
    this.#over(x, y);
  }

  #elementOf(source: DragSource): Element | null {
    const root = this.#host.root();
    if (source.kind === 'field') return root.querySelector(`jfb-canvas-field[key="${source.key}"]`);
    if (source.kind === 'group') return root.querySelector(`[data-group="${source.key}"]`);
    if (source.kind === 'page') return root.querySelector(`[data-page="${source.key}"]`);
    return null;
  }

  #over(x: number, y: number) {
    const source = this.#source;
    const layer = this.#layer;
    if (!source || !layer) return;
    const target = this.#targetAt(source, x, y);
    this.#target = target;
    this.#rest(source, target.step);
    this.#draw(layer, x, y, target);
  }

  // A page's step the pointer rests on shows that page, so a drop can go exactly where it should.
  #rest(source: DragSource, step: Element | null) {
    const page = source.kind === 'page' ? null : (step?.getAttribute('data-page') ?? null);
    if (page === this.#dwellOn) return;
    clearTimeout(this.#dwell);
    this.#dwellOn = page;
    if (page && page !== this.#host.shownPage())
      this.#dwell = window.setTimeout(() => this.#host.show(page), STEP_DWELL_MS);
  }

  #targetAt(source: DragSource, x: number, y: number): Target {
    const root = this.#host.root();
    const steps = root.querySelector('.bo-steps');
    if (source.kind === 'page') return this.#pageTarget(source, steps, x, y);

    const step = steps
      ? (childrenMatching(steps, '[data-page]').find((each) => holds(rect(each), x, y)) ?? null)
      : null;
    if (step) {
      const place = this.#host.pageEnd(source, step.getAttribute('data-page') ?? '');
      return { place, reason: this.#reason(source, place), line: null, step, area: null };
    }

    const viewport = root.querySelector('.bo-viewport');
    const area = viewport ? rect(viewport) : null;
    const dragged = this.#dragged;
    const containers = [
      ...root.querySelectorAll<HTMLElement>(source.kind === 'group' ? '[data-groups]' : '[data-fields]'),
    ]
      // A group far from the view is never under the pointer, and measuring it would make the browser draw it.
      .filter((container) => !(dragged && dragged.contains(container)) && !container.closest('[data-far]'))
      .map((element) => ({ element, box: rect(element) }));
    const container = containerAt(containers, x, y);
    if (!container) return { place: null, reason: null, line: null, step: null, area };
    const key = container.element.getAttribute(source.kind === 'group' ? 'data-groups' : 'data-fields') ?? '';
    const children = childrenMatching(container.element, source.kind === 'group' ? '[data-group]' : 'jfb-canvas-field');
    const boxes = children.map(rect);
    const others = boxes.filter((_, at) => children[at] !== dragged);
    const index = indexAt(others, x, y, false, boxes);
    const line = lineAt(others, index, container.box);
    const place: Place = { container: key, index };
    return {
      place,
      reason: this.#reason(source, place),
      line: { left: line.left, top: line.y - 1, width: line.right - line.left, height: 2 },
      step: null,
      area,
    };
  }

  // A page moves among the steps, before the step it comes before; the line stands between them. Let go away from the
  // steps, it stays where it is.
  #pageTarget(source: DragSource, steps: Element | null, x: number, y: number): Target {
    const near = steps ? rect(steps) : null;
    if (!steps || !near || y < near.top - STEP_REACH || y > near.bottom + STEP_REACH)
      return { place: null, reason: null, line: null, step: null, area: null };
    const others = childrenMatching(steps, '[data-page]')
      .filter((element) => element !== this.#dragged)
      .map(rect);
    const index = indexAt(others, x, y, true);
    const before = others[index];
    const last = others[others.length - 1];
    const edge = before ? before.left - 5 : last ? last.right + 5 : rect(steps).left;
    const box = before ?? last ?? rect(steps);
    const place: Place = { container: null, index };
    return {
      place,
      reason: this.#reason(source, place),
      line: { left: edge - 1, top: box.top, width: 2, height: box.bottom - box.top },
      step: null,
      area: rect(steps),
    };
  }

  #reason(source: DragSource, place: Place | null): string | null {
    const at = place ? `${place.container ?? ''}:${place.index}` : '';
    if (!this.#reasons.has(at)) this.#reasons.set(at, this.#host.refusalOf(source, place));
    return this.#reasons.get(at) ?? null;
  }

  #draw(layer: HTMLElement, x: number, y: number, target: Target) {
    const follow = layer.querySelector<HTMLElement>('.bo-follow');
    const marks = layer.querySelector<HTMLElement>('.bo-marks');
    const slot = layer.querySelector<HTMLElement>('.bo-slot');
    const line = layer.querySelector<HTMLElement>('.bo-line');
    const why = layer.querySelector<HTMLElement>('.bo-why');
    if (follow) follow.style.transform = `translate(${Math.round(x + 14)}px, ${Math.round(y + 14)}px)`;
    if (marks) {
      const area = target.area;
      marks.style.clipPath = area
        ? `inset(${area.top}px calc(100% - ${area.right}px) calc(100% - ${area.bottom}px) ${area.left}px)`
        : '';
    }
    if (slot) {
      const dragged = this.#dragged ? rect(this.#dragged) : null;
      slot.hidden = !dragged;
      if (dragged) place(slot, dragged.left, dragged.top, dragged.right - dragged.left, dragged.bottom - dragged.top);
    }
    if (line) {
      line.hidden = !target.line;
      line.toggleAttribute('data-refused', target.reason !== null);
      line.toggleAttribute('data-upright', (target.line?.height ?? 0) > (target.line?.width ?? 0));
      if (target.line) place(line, target.line.left, target.line.top, target.line.width, target.line.height);
    }
    if (why) why.textContent = target.reason ?? '';
    for (const step of this.#host.root().querySelectorAll('[data-drop]'))
      if (step !== target.step) step.removeAttribute('data-drop');
    if (target.step) target.step.setAttribute('data-drop', target.reason === null ? '' : 'refused');
  }

  #drop(x: number, y: number) {
    const source = this.#source;
    this.#over(x, y);
    const target = this.#target;
    const ghost = this.#layer?.querySelector('.bo-ghost');
    const from = ghost ? rect(ghost) : null;
    this.#end();
    if (!source || !target) return;
    if (target.reason !== null) {
      this.#host.refused(source, target.reason);
      return;
    }
    if (!target.place) return;
    if (from) this.#dropped = { from, source };
    this.#host.drop(source, target.place);
  }

  #end() {
    clearTimeout(this.#dwell);
    this.#dwellOn = null;
    this.#layer?.remove();
    this.#layer = null;
    this.#dragged?.removeAttribute('data-dragged');
    this.#dragged = null;
    this.#source = null;
    this.#target = null;
    this.#reasons.clear();
    this.#host.root().host.removeAttribute('data-dragging');
    for (const step of this.#host.root().querySelectorAll('[data-drop]')) step.removeAttribute('data-drop');
  }
}

function place(element: HTMLElement, left: number, top: number, width: number, height: number) {
  element.style.transform = `translate(${Math.round(left)}px, ${Math.round(top)}px)`;
  element.style.width = `${Math.max(0, Math.round(width))}px`;
  element.style.height = `${Math.max(0, Math.round(height))}px`;
}
