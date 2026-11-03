// The widths the canvas shows a form at (docs/ux/information-architecture.md section 5.1): the form's own width on a
// visitor's phone, tablet and desktop window.
export const CANVAS_WIDTHS = { phone: 375, tablet: 768, desktop: 960 } as const;

/** One of the canvas widths. */
export type CanvasWidth = keyof typeof CANVAS_WIDTHS;
