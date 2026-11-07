// The widths the canvas shows a form at (docs/ux/information-architecture.md section 5.1): the form's own width on a
// visitor's phone, tablet and desktop window.
export const CANVAS_WIDTHS = { phone: 375, tablet: 768, desktop: 960 } as const;

/** One of the canvas widths. */
export type CanvasWidth = keyof typeof CANVAS_WIDTHS;

// Where this browser keeps the canvas's width chosen last, which the preview starts at too (ADR-0203).
const WIDTH_KEY = 'jfb.designer.width';

/** The canvas width this browser chose last; Desktop at first. */
export function readWidth(): CanvasWidth {
  try {
    const kept = localStorage.getItem(WIDTH_KEY);
    return kept && Object.hasOwn(CANVAS_WIDTHS, kept) ? (kept as CanvasWidth) : 'desktop';
  } catch {
    return 'desktop';
  }
}

/** Keeps the width chosen, for the canvas and the preview to start from. */
export function keepWidth(width: CanvasWidth): void {
  try {
    localStorage.setItem(WIDTH_KEY, width);
  } catch {
    // A browser that keeps nothing starts at Desktop next time.
  }
}
