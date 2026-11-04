// The canvas's drags (ADR-0016, ADR-0169) over a laid-out page whose boxes the test gives: where a field, a group, a
// new field and a page land, a refusal shown at the line and asked once per place, a step's page end and the page shown
// after resting on it, Escape, and the settle after a drop.
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { must } from '../../testing';
import type { Place } from '../editor-model';
import { CanvasDrag, STEP_DWELL_MS, SETTLE_MS, type CanvasDragHost, type DragSource } from './canvas-drag';

// The values ADR-0016 and ADR-0169 name, so a changed constant fails here.
const DWELL = 600;
const SETTLE = 150;

function pointer(type: string, x: number, y: number): PointerEvent {
  const event = new MouseEvent(type, { clientX: x, clientY: y, button: 0, bubbles: true, cancelable: true });
  Object.defineProperty(event, 'pointerId', { value: 1 });
  Object.defineProperty(event, 'isPrimary', { value: true });
  return event as PointerEvent;
}

/** Gives an element a box, left, top, right, bottom. */
function lay(element: Element | null, left: number, top: number, right: number, bottom: number) {
  if (element)
    element.getBoundingClientRect = () =>
      ({ left, top, right, bottom, width: right - left, height: bottom - top }) as DOMRect;
}

// Two steps; page one with the groups g1 (fields a, b) and g2 (field c) one below the other.
const MARKUP = `
  <nav class="bo-steps"><button class="bo-step" data-page="p1">One</button><button class="bo-step" data-page="p2">Two</button></nav>
  <div class="bo-viewport">
    <section data-groups="p1">
      <div data-group="g1"><div data-fields="g1"><jfb-canvas-field key="a"></jfb-canvas-field><jfb-canvas-field key="b"></jfb-canvas-field></div></div>
      <div data-group="g2"><div data-fields="g2"><jfb-canvas-field key="c"></jfb-canvas-field></div></div>
    </section>
  </div>`;

interface Heard {
  readonly drops: [DragSource, Place][];
  readonly refused: [DragSource, string][];
  readonly shown: string[];
  readonly asked: (string | null)[];
}

function canvasOf(refusal: (place: Place | null) => string | null = () => null) {
  const host = document.createElement('div');
  document.body.append(host);
  const root = host.attachShadow({ mode: 'open' });
  root.innerHTML = MARKUP;
  const find = (selector: string) => root.querySelector(selector);
  lay(find('.bo-steps'), 0, 0, 400, 40);
  lay(find('[data-page="p1"]'), 0, 0, 100, 40);
  lay(find('[data-page="p2"]'), 120, 0, 220, 40);
  lay(find('.bo-viewport'), 0, 60, 400, 600);
  lay(find('[data-groups="p1"]'), 0, 60, 400, 400);
  lay(find('[data-group="g1"]'), 0, 60, 400, 220);
  lay(find('[data-fields="g1"]'), 0, 80, 400, 220);
  lay(find('[key="a"]'), 0, 80, 400, 140);
  lay(find('[key="b"]'), 0, 160, 400, 220);
  lay(find('[data-group="g2"]'), 0, 240, 400, 360);
  lay(find('[data-fields="g2"]'), 0, 260, 400, 360);
  lay(find('[key="c"]'), 0, 260, 400, 320);
  const heard: Heard = { drops: [], refused: [], shown: [], asked: [] };
  const dragHost: CanvasDragHost = {
    root: () => root,
    shownPage: () => 'p1',
    pageEnd: (source, page) =>
      source.kind === 'group' ? { container: page, index: 9 } : { container: `${page}-last`, index: 9 },
    refusalOf: (_source, place) => {
      heard.asked.push(place ? `${place.container}:${place.index}` : null);
      return refusal(place);
    },
    show: (page) => heard.shown.push(page),
    drop: (source, place) => heard.drops.push([source, place]),
    refused: (source, reason) => heard.refused.push([source, reason]),
  };
  return { drag: new CanvasDrag(dragHost), root, heard, find };
}

const field = (key: string): DragSource => ({ kind: 'field', key, label: key });

/** Presses on a handle at a point, moves through the points given and lets go at the last unless told not to. */
function dragAlong(drag: CanvasDrag, source: DragSource, points: [number, number][], release = true) {
  const handle = document.createElement('button');
  document.body.append(handle);
  const [start, ...rest] = points;
  drag.start(pointer('pointerdown', start?.[0] ?? 0, start?.[1] ?? 0), handle, source);
  for (const [x, y] of rest) window.dispatchEvent(pointer('pointermove', x, y));
  const end = points.at(-1) ?? [0, 0];
  if (release) window.dispatchEvent(pointer('pointerup', end[0], end[1]));
}

beforeEach(() => document.body.replaceChildren());

afterEach(() => {
  window.dispatchEvent(pointer('pointercancel', 0, 0));
  vi.useRealTimers();
});

describe('a drag on the canvas', () => {
  it('drops a field between the fields of the group under the pointer, the dragged one left out', () => {
    const { drag, heard } = canvasOf();
    // From c, up into g1 between a and b.
    dragAlong(drag, field('c'), [
      [200, 290],
      [200, 270],
      [200, 150],
    ]);
    expect(heard.drops).toEqual([[field('c'), { container: 'g1', index: 1 }]]);
    expect(drag.active).toBe(false);
  });

  it('never measures nor drops into a group far from the view, whose drawing waits (ADR-0176)', () => {
    const { drag, heard, find } = canvasOf();
    find('[data-group="g2"]')?.setAttribute('data-far', '');
    const fields = must(find('[data-fields="g2"]'), "g2's fields");
    const measured = vi.spyOn(fields, 'getBoundingClientRect');
    dragAlong(drag, field('a'), [
      [200, 100],
      [200, 120],
      [200, 290],
    ]);
    // The group in reach takes it, at its end.
    expect(heard.drops).toEqual([[field('a'), { container: 'g1', index: 1 }]]);
    expect(measured).not.toHaveBeenCalled();
  });

  it('drops at the end of a group below its last field, and counts places without the dragged field', () => {
    const { drag, heard } = canvasOf();
    dragAlong(drag, field('a'), [
      [200, 100],
      [200, 120],
      [200, 215],
    ]);
    // Below b in g1 without a, so place 1: after b.
    expect(heard.drops).toEqual([[field('a'), { container: 'g1', index: 1 }]]);
  });

  it('drops a group among the groups of the page, and a new field where a field would land', () => {
    const { drag, heard } = canvasOf();
    const group: DragSource = { kind: 'group', key: 'g2', label: 'g2' };
    dragAlong(drag, group, [
      [10, 250],
      [10, 230],
      [10, 70],
    ]);
    const made: DragSource = { kind: 'new', type: 'text', label: 'Short text' };
    dragAlong(drag, made, [
      [500, 500],
      [480, 500],
      [100, 270],
    ]);
    expect(heard.drops).toEqual([
      [group, { container: 'p1', index: 0 }],
      [made, { container: 'g2', index: 0 }],
    ]);
  });

  it('shows the line in the colour of a refusal with why, asks once per place, and says the refusal on a drop', () => {
    const { drag, heard, root } = canvasOf((place) =>
      place?.container === 'g2' ? "A repeatable group can't hold that." : null,
    );
    dragAlong(
      drag,
      field('a'),
      [
        [200, 100],
        [200, 120],
        [200, 330],
        [210, 335],
        [220, 340],
      ],
      false,
    );
    const line = root.querySelector<HTMLElement>('.bo-line');
    expect(line?.hasAttribute('data-refused')).toBe(true);
    expect(root.querySelector('.bo-why')?.textContent).toBe("A repeatable group can't hold that.");
    expect(root.host.hasAttribute('data-dragging')).toBe(true);
    expect(root.querySelector('[key="a"]')?.hasAttribute('data-dragged')).toBe(true);
    expect(heard.asked.filter((place) => place === 'g2:1')).toHaveLength(1);
    window.dispatchEvent(pointer('pointerup', 220, 340));
    expect(heard.drops).toEqual([]);
    expect(heard.refused).toEqual([[field('a'), "A repeatable group can't hold that."]]);
    expect(root.querySelector('.bo-drag')).toBeNull();
    expect(root.host.hasAttribute('data-dragging')).toBe(false);
    expect(root.querySelector('[data-dragged]')).toBeNull();
  });

  it("drops on a page's step at that page's end, and shows the page after the pointer rests there", () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    const { drag, heard, find } = canvasOf();
    dragAlong(
      drag,
      field('a'),
      [
        [200, 100],
        [200, 120],
        [150, 20],
      ],
      false,
    );
    expect(find('[data-page="p2"]')?.getAttribute('data-drop')).toBe('');
    expect(STEP_DWELL_MS).toBe(DWELL);
    vi.advanceTimersByTime(DWELL - 1);
    expect(heard.shown).toEqual([]);
    vi.advanceTimersByTime(1);
    expect(heard.shown).toEqual(['p2']);
    window.dispatchEvent(pointer('pointerup', 150, 20));
    expect(heard.drops).toEqual([[field('a'), { container: 'p2-last', index: 9 }]]);
    expect(find('[data-drop]')).toBeNull();
  });

  it('never shows the page already shown, nor a page for a page dragged', () => {
    vi.useFakeTimers({ toFake: ['setTimeout', 'clearTimeout'] });
    const { drag, heard } = canvasOf();
    dragAlong(
      drag,
      field('a'),
      [
        [200, 100],
        [200, 120],
        [50, 20],
      ],
      false,
    );
    vi.advanceTimersByTime(DWELL * 2);
    window.dispatchEvent(pointer('pointercancel', 50, 20));
    dragAlong(
      drag,
      { kind: 'page', key: 'p1', label: 'One' },
      [
        [50, 20],
        [60, 20],
        [200, 20],
      ],
      false,
    );
    vi.advanceTimersByTime(DWELL * 2);
    expect(heard.shown).toEqual([]);
  });

  it('moves a page among the steps, before the step it comes before, the line upright between them', () => {
    const { drag, heard, root } = canvasOf();
    const page: DragSource = { kind: 'page', key: 'p2', label: 'Two' };
    dragAlong(
      drag,
      page,
      [
        [150, 20],
        [140, 20],
        [20, 20],
      ],
      false,
    );
    expect(root.querySelector('.bo-line')?.hasAttribute('data-upright')).toBe(true);
    window.dispatchEvent(pointer('pointerup', 20, 20));
    expect(heard.drops).toEqual([[page, { container: null, index: 0 }]]);
    dragAlong(
      drag,
      field('a'),
      [
        [200, 100],
        [200, 120],
        [200, 300],
      ],
      false,
    );
    expect(root.querySelector('.bo-line')?.hasAttribute('data-upright')).toBe(false);
  });

  it('shows why beside the copy that follows the pointer, also over a step', () => {
    const { drag, root } = canvasOf((place) => (place?.container === 'p2-last' ? 'Not there.' : null));
    dragAlong(
      drag,
      field('a'),
      [
        [200, 100],
        [200, 120],
        [150, 20],
      ],
      false,
    );
    const why = root.querySelector('.bo-follow .bo-why');
    expect(why?.textContent).toBe('Not there.');
    expect(root.querySelector<HTMLElement>('.bo-line')?.hidden).toBe(true);
    expect(root.querySelector<HTMLElement>('.bo-follow')?.style.transform).toBe('translate(164px, 34px)');
    expect(root.querySelector('.bo-follow .bo-ghost')?.textContent).toBe('a');
  });

  it('leaves a page where it is when it is let go away from the steps', () => {
    const { drag, heard } = canvasOf();
    dragAlong(drag, { kind: 'page', key: 'p2', label: 'Two' }, [
      [150, 20],
      [140, 20],
      [20, 300],
    ]);
    expect(heard.drops).toEqual([]);
    expect(heard.refused).toEqual([]);
  });

  it('ends on Escape without a drop, leaving nothing behind', () => {
    const { drag, heard, root } = canvasOf();
    dragAlong(
      drag,
      field('a'),
      [
        [200, 100],
        [200, 120],
        [200, 300],
      ],
      false,
    );
    expect(root.querySelector('.bo-drag')).not.toBeNull();
    window.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true }));
    window.dispatchEvent(pointer('pointerup', 200, 300));
    expect(heard.drops).toEqual([]);
    expect(heard.refused).toEqual([]);
    expect(root.querySelector('.bo-drag')).toBeNull();
  });

  it('settles what it dropped from where the dragged copy was, over 150 ms, and once', () => {
    const { drag, find } = canvasOf();
    dragAlong(drag, field('c'), [
      [200, 290],
      [200, 270],
      [200, 150],
    ]);
    const element = find('[key="c"]') as HTMLElement;
    const animate = vi.fn();
    element.animate = animate as unknown as typeof element.animate;
    lay(element, 0, 160, 400, 220);
    drag.settle(null);
    drag.settle(null);
    expect(animate).toHaveBeenCalledTimes(1);
    // The copy has no layout here, so it was at 0, 0.
    expect(animate.mock.calls[0]?.[0]).toEqual([{ transform: 'translate(0px, -160px)' }, { transform: 'none' }]);
    expect(SETTLE_MS).toBe(SETTLE);
    expect(animate.mock.calls[0]?.[1]).toMatchObject({ duration: SETTLE, easing: 'cubic-bezier(0.23, 1, 0.32, 1)' });
  });

  it('settles a new field by the key the drop selected, and nothing with reduced motion', () => {
    const { drag, find } = canvasOf();
    const made: DragSource = { kind: 'new', type: 'text', label: 'Short text' };
    dragAlong(drag, made, [
      [500, 500],
      [480, 500],
      [200, 300],
    ]);
    const element = find('[key="b"]') as HTMLElement;
    const animate = vi.fn();
    element.animate = animate as unknown as typeof element.animate;
    drag.settle('b');
    expect(animate).toHaveBeenCalledTimes(1);

    window.matchMedia = ((query: string) => ({
      matches: query === '(prefers-reduced-motion: reduce)',
    })) as typeof window.matchMedia;
    try {
      dragAlong(drag, made, [
        [500, 500],
        [480, 500],
        [200, 300],
      ]);
      drag.settle('b');
      expect(animate).toHaveBeenCalledTimes(1);
    } finally {
      Reflect.deleteProperty(window, 'matchMedia');
    }
  });
});
