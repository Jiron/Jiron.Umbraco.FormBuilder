/**
 * Fitting a form to its page (ROADMAP 6.4l, ADR-0022 as amended in 2.4), in the frame, as both show before the visitor
 * touches anything. The ground check: a form with no scheme class, on itself or an ancestor (ADR-0106), takes the dark
 * tokens when the first opaque background behind it is dark, so its text never sits dark on dark; a class the site set
 * is never changed, and a form whose theme has no dark scheme (`data-jfb-light-only`) never gets one. It runs again when
 * the system's scheme changes and when a site's own switch may have changed the ground: a class, `style` or `data-*`
 * attribute of the document element or the body, and the end of a background's transition behind a form (27.1). Step
 * folding: the steps fold into "Page 2 of 6" when a word of a step name is wider than its step, so no name breaks inside
 * a word where the browser cannot hyphenate, after layout, once the fonts are in, and on every change of width.
 */
import { proto } from '@jfb/logic/proto';

const SCHEME = 'jfb-scheme-';
const DARK = `${SCHEME}dark`;
const COMPACT = 'data-jfb-compact';
/** On a form root whose theme offers no dark scheme (27.1). */
const LIGHT_ONLY = 'data-jfb-light-only';
/** Below this luminance white text has more contrast than black text, so the ground counts as dark. */
const DARK_BELOW = 0.179;

/** A colour's red, green, blue and alpha, each from 0 to 255; null for one that cannot be read. */
export type ReadColour = (css: string) => readonly [number, number, number, number] | null;

/** A word's width in the given font, in CSS pixels; null where the browser cannot measure it. */
export type MeasureWord = (word: string, font: string) => number | null;

let canvas: CanvasRenderingContext2D | null | undefined;

/** A small canvas, which reads any way a browser writes a computed colour, `oklch()` and `color()` among them. */
function context(): CanvasRenderingContext2D | null {
  if (canvas === undefined) {
    const element = document.createElement('canvas');
    element.width = 1;
    element.height = 1;
    canvas = element.getContext('2d', { willReadFrequently: true });
  }
  return canvas;
}

const readColour: ReadColour = (css) => {
  const paint = context();
  if (paint === null) return null;
  paint.clearRect(0, 0, 1, 1);
  paint.fillStyle = '#000';
  paint.fillStyle = css;
  paint.fillRect(0, 0, 1, 1);
  const [red = 0, green = 0, blue = 0, alpha = 0] = paint.getImageData(0, 0, 1, 1).data;
  return [red, green, blue, alpha];
};

const measureWord: MeasureWord = (word, font) => {
  const paint = context();
  if (paint === null) return null;
  paint.font = font;
  return paint.measureText(word).width;
};

const luminance = (red: number, green: number, blue: number): number => {
  const [r = 0, g = 0, b = 0] = [red, green, blue].map((value) => {
    const part = value / 255;
    return part <= 0.04045 ? part / 12.92 : ((part + 0.055) / 1.055) ** 2.4;
  });
  return 0.2126 * r + 0.7152 * g + 0.0722 * b;
};

const hasScheme = (form: Element): boolean => {
  for (let element: Element | null = form; element !== null; element = proto(element).parentElement) {
    if ([...proto(element).classList].some((name) => name.startsWith(SCHEME))) return true;
  }
  return false;
};

/** The forms the ground check made dark, whose class it may take back; a site's own stays. */
const madeDark = new WeakSet<Element>();

/** Gives a form the dark tokens on a dark ground, or takes them back on a light one when the check gave them. */
export function checkGround(form: Element, read: ReadColour = readColour): void {
  if (madeDark.has(form)) {
    proto(form).classList.remove(DARK);
    madeDark.delete(form);
  }
  if (hasScheme(form) || proto(form).hasAttribute(LIGHT_ONLY)) return;
  const view = proto(form).ownerDocument.defaultView;
  for (let element = proto(form).parentElement; element !== null && view !== null; element = element.parentElement) {
    const colour = read(view.getComputedStyle(element).backgroundColor);
    if (colour === null || colour[3] !== 255) continue;
    if (luminance(colour[0], colour[1], colour[2]) < DARK_BELOW) {
      proto(form).classList.add(DARK);
      madeDark.add(form);
    }
    return;
  }
}

/** The form roots each document's ground is followed for, and what follows it, once per document. */
const followed = new WeakMap<Document, Map<Element, (form: Element) => void>>();

/** Whether an attribute's change can be a site's switch of its ground: a class, a style or a `data-*` attribute. */
const switches = (name: string | null): boolean =>
  name === 'class' || name === 'style' || name?.startsWith('data-') === true;

/**
 * Checks a form root's ground again whenever a site's own switch may have changed it (27.1): a change of a class, `style`
 * or `data-*` attribute of the document element or the body, and the end of a background colour's transition on an
 * element behind it. One observer and one listener per document, whatever the number of forms; a form taken off the page
 * is let go. A form whose theme has no dark scheme has nothing to follow.
 */
export function followGround(form: Element, check: (form: Element) => void = (root) => checkGround(root)): void {
  if (proto(form).hasAttribute(LIGHT_ONLY)) return;
  const page = proto(form).ownerDocument;
  let roots = followed.get(page);
  if (roots === undefined) {
    const all = new Map<Element, (form: Element) => void>();
    roots = all;
    followed.set(page, all);
    const again = (behind?: EventTarget | null): void => {
      for (const [root, checks] of all) {
        if (!proto(root).isConnected) all.delete(root);
        else if (behind === undefined || (behind instanceof Node && behind !== root && proto(behind).contains(root)))
          checks(root);
      }
    };
    if (typeof MutationObserver !== 'undefined') {
      const observer = new MutationObserver((changes) => {
        if (changes.some((change) => switches(change.attributeName))) again();
      });
      observer.observe(page.documentElement, { attributes: true });
      if (page.body !== null) observer.observe(page.body, { attributes: true });
    }
    page.addEventListener(
      'transitionend',
      (event) => {
        if (event.propertyName === 'background-color') again(event.target);
      },
      true,
    );
  }
  roots.set(form, check);
}

/** The name's own words: its text, without what its marks add. */
const wordsOf = (name: Element): string[] =>
  [...name.childNodes]
    .filter((node) => node.nodeType === 3)
    .flatMap((node) => (node.textContent ?? '').split(/\s+/))
    .filter((word) => word.length > 0);

/** Folds steps a word of whose names is wider than its step, and unfolds them once none is. */
export function foldSteps(steps: Element, measure: MeasureWord = measureWord): void {
  steps.removeAttribute(COMPACT);
  const view = steps.ownerDocument.defaultView;
  if (view === null) return;
  const tooLong = [...steps.querySelectorAll(':scope > ol > li > span')].some((name) => {
    const style = view.getComputedStyle(name);
    // The themes fold them already, by the form's width or the number of steps.
    if (style.position === 'absolute') return false;
    const room = name.getBoundingClientRect().width;
    return wordsOf(name).some((word) => (measure(word, style.font) ?? 0) > room + 0.5);
  });
  if (tooLong) steps.setAttribute(COMPACT, '');
}

/** Keeps a form's steps folded as they fit: from now, once the fonts are in, and on each change of width. */
export function followSteps(steps: Element): void {
  let width = -1;
  const fold = (): void => {
    width = steps.getBoundingClientRect().width;
    foldSteps(steps);
  };
  fold();
  void steps.ownerDocument.fonts?.ready.then(fold);
  // A rule may show or hide a step (ADR-0155); folding changes no step, so it never answers itself.
  const list = steps.querySelector(':scope > ol');
  if (list !== null) new MutationObserver(fold).observe(list, { childList: true });
  if (typeof ResizeObserver === 'undefined') return;
  new ResizeObserver(() => {
    // Folding changes the steps' height and never their width, so it never answers itself.
    if (steps.getBoundingClientRect().width !== width) fold();
  }).observe(steps);
}
