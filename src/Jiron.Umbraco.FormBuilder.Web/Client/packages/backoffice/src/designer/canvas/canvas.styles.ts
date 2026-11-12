// The canvas's own rules (ROADMAP 9.3b, ADR-0168), after the default theme's in the canvas's shadow root: the toolbar,
// the steps and the sheet in Umbraco's colours, and the designer's controls laid over the form, each prefixed bo- so
// that none reaches the form's own look. The form itself keeps the theme's light colours in every backoffice theme, as
// visitors see it. The controls over a field are positioned out of the flow, so the theme's grid of a group's fields,
// its rows shared between columns and its spacing between a field's parts hold as on the website; only the room above
// each field and group, where a selected element's bar stands, is the canvas's own (ADR-0182).
import { css } from '@umbraco-cms/backoffice/external/lit';

export const canvasChrome = css`
  :host {
    --bo-accent: var(--uui-color-selected, #3544b1);
    --bo-on-accent: var(--uui-color-selected-contrast, #fff);
    --bo-focus: var(--jfb-bo-color-focus);
    /*
     * The sheet is white in every theme, so what the designer draws on it has fixed colours (docs/ux/tokens.md, "The
     * designer's sheet"): its ink, quiet text and dashed lines, its controls' ring, the two kinds of chip and a
     * dragged element's place.
     */
    --bo-sheet-ink: #1d2433;
    --bo-sheet-quiet: #4a5263;
    --bo-sheet-line: #8a92a0;
    --bo-sheet-focus: #2956b4;
    --bo-sheet-slot: #eceff3;
    --bo-chip-missing-line: #d9b44a;
    --bo-chip-missing-fill: #fff6d9;
    --bo-chip-missing-ink: #533d00;
    --bo-chip-rule-line: #b8bfdc;
    --bo-chip-rule-fill: #eef0f8;
    --bo-chip-rule-ink: #1f2a5c;
    /*
     * The canvas's own room above each field and group (ADR-0182): a selected element's bar and handle stand 36 px
     * above its frame, 40 px for a group's, and with the theme's spacing this room keeps them off the element above,
     * a legend or a page title, so nothing moves when an element is selected.
     */
    --bo-room: 20px;
    --bo-sheet-padding: 32px;
    display: flex;
    flex-direction: column;
    min-width: 0;
    min-height: 0;
    background: var(--uui-color-background);
    color: var(--uui-color-text);
    font-family: var(--uui-font-family);
  }

  /* While the look is read: the sheet with still bones where its title and fields will be. */
  .bo-waiting {
    padding-block-start: var(--uui-size-space-4);
  }

  .bo-waiting .bo-sheet {
    display: grid;
    gap: 12px;
    align-content: start;
  }

  .bo-bone {
    display: block;
    block-size: 12px;
    border-radius: var(--uui-border-radius);
    background: var(--bo-sheet-slot);
  }

  .bo-bone-title {
    inline-size: 40%;
    block-size: 24px;
    margin-block-end: 12px;
  }

  .bo-bone-box {
    block-size: 44px;
    margin-block-end: 12px;
  }

  .bo-said {
    position: absolute;
    width: 1px;
    height: 1px;
    overflow: hidden;
    clip-path: inset(50%);
    white-space: nowrap;
  }

  .bo-bar,
  .bo-steps {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: var(--uui-size-space-3);
    padding: var(--uui-size-space-4) var(--uui-size-layout-1) 0;
  }

  .bo-widths {
    display: inline-flex;
    border: 1px solid var(--jfb-bo-color-control-border);
    border-radius: var(--uui-border-radius);
    overflow: hidden;
  }

  .bo-widths button {
    min-height: 36px;
    padding: 0 var(--uui-size-space-4);
    border: 0;
    border-inline-start: 1px solid var(--jfb-bo-color-control-border);
    background: var(--uui-color-surface);
    color: var(--uui-color-text);
    font: inherit;
    cursor: pointer;
  }

  .bo-widths button:first-child {
    border-inline-start: 0;
  }

  @media (hover: hover) {
    .bo-widths button:hover {
      background: var(--uui-color-surface-emphasis);
    }
  }

  .bo-widths button[aria-pressed='true'] {
    background: var(--bo-accent);
    color: var(--bo-on-accent);
    font-weight: 700;
  }

  .bo-steps button:focus-visible {
    outline: 2px solid var(--bo-focus);
    outline-offset: 2px;
  }

  /* Inside a box that clips its buttons, the ring lies inside the button, white on the width shown. */
  :is(.bo-widths, .bo-menu) button:focus-visible {
    position: relative;
    outline: 2px solid var(--bo-focus);
    outline-offset: -4px;
  }

  .bo-widths button[aria-pressed='true']:focus-visible {
    outline-color: var(--bo-on-accent);
  }

  .bo-narrowed {
    color: var(--jfb-bo-color-text-muted);
    font-size: var(--uui-type-small-size);
  }

  .bo-more {
    margin-inline-start: auto;
  }

  /* The form check's badge sits at the bar's end, before its "...". */
  .bo-check {
    display: inline-flex;
    align-items: center;
    gap: var(--uui-size-space-2);
    min-height: 36px;
    margin-inline-start: auto;
    padding: 0 var(--uui-size-space-4);
    border: 1px solid var(--jfb-bo-color-control-border);
    border-radius: var(--uui-border-radius);
    background: var(--uui-color-surface);
    color: var(--uui-color-text);
    font: inherit;
    cursor: pointer;
  }

  .bo-check + .bo-more {
    margin-inline-start: 0;
  }

  @media (hover: hover) {
    .bo-check:hover {
      background: var(--uui-color-surface-emphasis);
    }
  }

  .bo-check:focus-visible {
    outline: 2px solid var(--bo-focus);
    outline-offset: 2px;
  }

  .bo-check[data-state='checking'],
  .bo-check[data-state='failed'] {
    color: var(--jfb-bo-color-text-muted);
  }

  .bo-check[data-state='error'] uui-icon {
    color: var(--jfb-bo-color-danger-text);
  }

  .bo-check[data-state='warning'] uui-icon {
    color: var(--jfb-bo-color-warning-text);
  }

  .bo-check[data-state='clear'] uui-icon {
    color: var(--jfb-bo-color-positive-text);
  }

  .bo-language {
    display: inline-flex;
    align-items: center;
    gap: var(--uui-size-space-2);
    font-size: var(--uui-type-small-size);
  }

  .bo-language select {
    min-height: 36px;
    padding: 0 var(--uui-size-space-3);
    border: 1px solid var(--jfb-bo-color-control-border);
    border-radius: var(--uui-border-radius);
    background: var(--uui-color-surface);
    color: var(--uui-color-text);
    font: inherit;
    font-size: var(--uui-type-default-size);
  }

  .bo-language select:focus-visible {
    outline: 2px solid var(--bo-focus);
    outline-offset: 2px;
  }

  .bo-menu {
    display: grid;
    min-width: 200px;
    padding: var(--uui-size-space-2) 0;
    border-radius: var(--uui-border-radius);
    background: var(--uui-color-surface);
    box-shadow: var(--uui-shadow-depth-3);
    transform-origin: top right;
    transition:
      opacity var(--jfb-bo-motion-fast) var(--jfb-bo-motion-ease),
      transform var(--jfb-bo-motion-fast) var(--jfb-bo-motion-ease);
  }

  @starting-style {
    .bo-menu {
      opacity: 0;
      transform: scale(0.97);
    }
  }

  .bo-menu button {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: var(--uui-size-space-5);
    min-height: 36px;
    padding: 0 var(--uui-size-space-4);
    border: 0;
    background: none;
    color: var(--uui-color-text);
    font: inherit;
    text-align: start;
    cursor: pointer;
  }

  @media (hover: hover) {
    .bo-menu button:hover:not(:disabled) {
      background: var(--uui-color-surface-emphasis);
    }
  }

  .bo-menu button:disabled {
    color: var(--uui-color-disabled-contrast);
    cursor: default;
  }

  .bo-menu kbd {
    color: var(--jfb-bo-color-text-muted);
    font: inherit;
    font-size: var(--uui-type-small-size);
  }

  .bo-steps {
    padding-block: var(--uui-size-space-4);
  }

  .bo-step {
    display: inline-flex;
    align-items: center;
    gap: var(--uui-size-space-2);
    min-height: 36px;
    max-width: 100%;
    padding: 0 var(--uui-size-space-4);
    border: 1px solid var(--jfb-bo-color-control-border);
    border-radius: var(--jfb-bo-radius-round);
    background: var(--uui-color-surface);
    color: var(--uui-color-text);
    font: inherit;
    overflow-wrap: anywhere;
    cursor: pointer;
  }

  /* A page's step drags it among the steps; the steps still scroll the page up and down under a finger. */
  .bo-step[data-page] {
    touch-action: pan-y;
    user-select: none;
  }

  @media (hover: hover) {
    .bo-step:hover {
      background: var(--uui-color-surface-emphasis);
    }
  }

  .bo-step[aria-current='step'] {
    border-color: var(--bo-accent);
    background: var(--bo-accent);
    color: var(--bo-on-accent);
    font-weight: 700;
  }

  .bo-step-add {
    border-color: transparent;
    background: none;
    color: var(--uui-color-interactive);
  }

  @media (hover: hover) {
    .bo-step-add:hover {
      color: var(--uui-color-interactive-emphasis);
    }
  }

  /* A layer of its own, so the browser scrolls it without painting the sheet again (ADR-0169). The host's colour shows
     through it: a scroller's own background is painted with its contents, so every move, duplicate or delete painted
     the whole view again, where only what changed needs it (ADR-0221). */
  .bo-viewport {
    flex: 1 1 auto;
    min-height: 0;
    position: relative;
    overflow: auto;
    padding: 0 var(--uui-size-layout-1) var(--uui-size-layout-1);
    will-change: scroll-position;
  }

  .bo-viewport:focus-visible {
    outline: 2px solid var(--bo-focus);
    outline-offset: -2px;
  }

  /* The visitor's page: white, whatever the backoffice's theme, as the default theme's light scheme expects. */
  .bo-sheet {
    container: bo-sheet / inline-size;
    box-sizing: border-box;
    inline-size: min(100%, calc(var(--bo-width) + 2 * var(--bo-sheet-padding)));
    margin-inline: auto;
    padding: var(--bo-sheet-padding);
    border: 1px solid var(--uui-color-border);
    border-radius: var(--uui-border-radius);
    background: #fff;
    color: var(--bo-sheet-ink);
  }

  @media (max-width: 620px) {
    :host {
      --bo-sheet-padding: 16px;
    }
  }

  /* ------------------------------------------------------------------------------------------------------------
   * The controls over the form. The page's, a group's and a field's buttons each cover their element, one above the
   * other, so a click selects what is under the pointer; the insert points and a selected element's tools lie above.
   * ---------------------------------------------------------------------------------------------------------- */

  .jfb-form :is(.bo-page, .bo-group, .bo-field) {
    position: relative;
  }

  .jfb-form :is(.jfb-fields > .bo-field, .bo-page > .bo-group) {
    margin-block-start: var(--bo-room);
  }

  /*
   * A group a view away from what is in sight: its styling, layout and painting wait until it comes near, at the
   * height it had (canvas-far.ts, ADR-0176). It stays in the accessibility tree, and the browser still finds its words.
   */
  .jfb-form .bo-group[data-far] {
    content-visibility: auto;
  }

  /*
   * The field's whole box, whatever the theme aligns its parts by: a theme's start alignment would size this layer to
   * its contents, which lie outside the flow, and leave it no height. Its class named twice outranks a theme's rule for the
   * parts of fields side by side.
   */
  .jfb-form .jfb-fields > .bo-field.bo-field > .bo-chrome {
    position: absolute;
    inset: 0;
    align-self: stretch;
    justify-self: stretch;
    margin: 0;
    pointer-events: none;
  }

  .bo-chrome > * {
    pointer-events: auto;
  }

  /*
   * The button over a field, a group or a page, whose ring shows what is selected, focused or under the pointer. Its
   * corners stay square, as a selection box's do. The ring under the pointer is dashed, and a dashed line around rounded
   * corners is drawn as a path, slowly on some graphics chips: the hover moving to the next field as a move by the
   * keyboard went through took up to 112 ms to show on a form of 300 fields. Every dashed line on the canvas runs along
   * square corners (ADR-0242).
   */
  .jfb-form .bo-select {
    position: absolute;
    inset: -8px;
    z-index: 2;
    margin: 0;
    padding: 0;
    border: 0;
    border-radius: 0;
    background: transparent;
    cursor: pointer;
  }

  .jfb-form .bo-select-group {
    inset: -12px;
    z-index: 1;
  }

  .jfb-form .bo-select-page {
    inset: -20px;
    z-index: 0;
  }

  @media (hover: hover) {
    .jfb-form .bo-select:hover {
      outline: 1px dashed color-mix(in srgb, var(--bo-accent) 70%, transparent);
    }
  }

  .jfb-form .bo-select:focus-visible {
    outline: 2px solid var(--bo-sheet-focus);
    outline-offset: 2px;
  }

  .jfb-form .bo-select[aria-current='true'] {
    outline: 2px solid var(--bo-accent);
    outline-offset: 0;
  }

  .jfb-form .bo-select[aria-current='true']:focus-visible {
    outline: 2px solid var(--bo-accent);
    box-shadow:
      0 0 0 4px #fff,
      0 0 0 6px var(--bo-sheet-focus);
  }

  /*
   * A field in a group with columns holds the space below it inside its box; the outline stays around the field. Its
   * box is too narrow for a bar with words and a pill beside it: the bar shows its icons, an insert point its "+" alone.
   */
  @container jfb-form (min-width: 480px) {
    :is(.jfb-columns-2, .jfb-columns-3, .jfb-columns-4) > .jfb-fields > .bo-field > .bo-chrome > .bo-select {
      inset-block-end: calc(var(--jfb-space-5, 24px) - 8px);
    }

    :is(.jfb-columns-2, .jfb-columns-3, .jfb-columns-4) > .jfb-fields > .bo-field > .bo-chrome > .bo-insert-after {
      justify-content: flex-start;
      inset-block-start: calc(100% - var(--jfb-space-5, 24px));
      padding-inline-start: 28px;
    }

    :is(.jfb-columns-2, .jfb-columns-3, .jfb-columns-4)
      > .jfb-fields
      > .bo-field
      > .bo-chrome
      > .bo-insert-after
      > .bo-pill {
      padding: 3px;
    }

    :is(.jfb-columns-2, .jfb-columns-3, .jfb-columns-4)
      > .jfb-fields
      > .bo-field
      > .bo-chrome
      > :is(.bo-insert-after, .bo-tools)
      :is(.bo-pill-text, .bo-tool-text) {
      display: none;
    }

    :is(.jfb-columns-2, .jfb-columns-3, .jfb-columns-4) > .jfb-fields > .bo-field > .bo-chrome > .bo-tools > button {
      padding: 0 8px;
    }
  }

  .jfb-form .bo-tools {
    position: absolute;
    inset-block-end: calc(100% + 8px);
    inset-inline-end: -8px;
    z-index: 4;
    display: flex;
    margin: 0;
    border-radius: var(--uui-border-radius) var(--uui-border-radius) 0 0;
    background: var(--bo-accent);
    overflow: hidden;
  }

  .jfb-form .bo-tools button {
    display: inline-flex;
    align-items: center;
    gap: 6px;
    min-height: 28px;
    margin: 0;
    padding: 0 10px;
    border: 0;
    background: none;
    color: var(--bo-on-accent);
    font: 600 12px/1.5 var(--uui-font-family);
    white-space: nowrap;
    cursor: pointer;
  }

  /*
   * On a narrow sheet, as beside the inspector in a view up to 1024 px, the bar's buttons show their icons alone: the
   * German bar with its words is about 370 px wide and stays clear of the handle and the "+" of the insert point above.
   */
  @container bo-sheet (max-width: 430px) {
    .jfb-form .bo-tools .bo-tool-text {
      display: none;
    }

    .jfb-form .bo-tools > button {
      padding: 0 8px;
    }
  }

  /* A darker shade: a lighter one takes the white label under 4.5:1 in the dark theme. */
  @media (hover: hover) {
    .jfb-form .bo-tools button:hover {
      background: color-mix(in srgb, #000 20%, transparent);
    }
  }

  .jfb-form .bo-tools button:focus-visible {
    outline: 2px solid var(--bo-on-accent);
    outline-offset: -4px;
  }

  .jfb-form .bo-tools uui-icon {
    font-size: 14px;
  }

  /* The bar's "..." opens its menu in the top layer, in Umbraco's colours like the toolbar's. */
  .jfb-form .bo-tools .bo-menu button {
    justify-content: flex-start;
    min-height: 36px;
    padding: 0 var(--uui-size-space-4);
    color: var(--uui-color-text);
    font: 400 14px/1.4 var(--uui-font-family);
  }

  @media (hover: hover) {
    .jfb-form .bo-tools .bo-menu button:hover {
      background: var(--uui-color-surface-emphasis);
    }
  }

  .jfb-form .bo-tools .bo-menu button:focus-visible {
    outline: 2px solid var(--bo-focus);
    outline-offset: -2px;
  }

  /*
   * An element's chips, out of the flow and gone while it is selected: a field without a text in the language shown,
   * and each rule in use (ADR-0178). Out of the flow, so selecting an element moves nothing below it, and the theme's
   * rows shared between columns hold; a click passes through to the element, whose inspector lists its rules. Each chip
   * gives up width before the next. A field's sit at its top end, just above its label, in the gap the insert point
   * above it shares, which they leave to it while it is in use; a group's and a page's sit on the box's bottom edge, so
   * they never meet its first field's.
   */
  .jfb-form .bo-chips {
    position: absolute;
    inset-inline-end: 8px;
    z-index: 3;
    display: flex;
    gap: 4px;
    max-inline-size: calc(100% - 48px);
    pointer-events: none;
  }

  .jfb-form .bo-field > .bo-chrome > .bo-chips {
    inset-block-end: calc(100% - 2px);
  }

  .jfb-form :is(.bo-group, .bo-page) > .bo-chips {
    inset-block-end: -11px;
  }

  .jfb-form
    .bo-field:has(> .bo-chrome > .bo-insert-after:is(:hover, :focus-visible, [data-active]))
    + .bo-field
    > .bo-chrome
    > .bo-chips,
  .jfb-form .bo-insert-first:is(:hover, :focus-visible, [data-active]) + .bo-field > .bo-chrome > .bo-chips {
    visibility: hidden;
  }

  .jfb-form .bo-chip {
    flex: 0 1 auto;
    min-inline-size: 0;
    overflow: hidden;
    padding: 1px 8px;
    border: 1px solid var(--bo-chip-missing-line);
    border-radius: var(--jfb-bo-radius-round);
    background: var(--bo-chip-missing-fill);
    color: var(--bo-chip-missing-ink);
    font: 600 var(--uui-type-small-size) / 1.5 var(--uui-font-family);
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  /* A rule is information, not a problem: a cool neutral beside the translation chip's amber. */
  .jfb-form .bo-rule-chip {
    display: inline-flex;
    align-items: center;
    gap: 4px;
    border-color: var(--bo-chip-rule-line);
    background: var(--bo-chip-rule-fill);
    color: var(--bo-chip-rule-ink);
  }

  .jfb-form .bo-rule-chip > uui-icon {
    flex: none;
    font-size: var(--uui-type-small-size);
  }

  .jfb-form .bo-rule-chip > span {
    min-inline-size: 0;
    overflow: hidden;
    text-overflow: ellipsis;
  }

  /* An insert point: a line across the gap with "Add a field here" on it, in sight on hover and on focus, at once: the
     pointer reveals one at every gap it crosses, and a fade would paint the whole sheet again for each frame of it. */
  .jfb-form .bo-insert {
    position: absolute;
    inset-inline: 0;
    z-index: 3;
    display: flex;
    align-items: center;
    justify-content: center;
    height: 24px;
    margin: 0;
    padding: 0;
    border: 0;
    background: transparent;
    color: var(--bo-on-accent);
    font: 600 12px/20px var(--uui-font-family);
    cursor: pointer;
    opacity: 0;
  }

  .jfb-form .bo-insert::before {
    content: '';
    position: absolute;
    inset-inline: 0;
    top: 11px;
    border-top: 2px solid var(--bo-accent);
  }

  .jfb-form .bo-pill {
    position: relative;
    display: inline-flex;
    align-items: center;
    gap: 4px;
    padding: 2px 12px 2px 8px;
    border-radius: var(--jfb-bo-radius-round);
    background: var(--bo-accent);
    transition: transform var(--jfb-bo-motion-fast) var(--jfb-bo-motion-ease);
  }

  .jfb-form .bo-insert:active > .bo-pill {
    transform: scale(0.97);
  }

  .jfb-form .bo-pill > uui-icon {
    font-size: 14px;
  }

  .jfb-form .bo-insert:is(:focus-visible, [data-active]) {
    opacity: 1;
  }

  @media (hover: hover) {
    .jfb-form .bo-insert:hover {
      opacity: 1;
    }
  }

  .jfb-form .bo-insert:focus {
    outline: none;
  }

  .jfb-form .bo-insert:focus-visible > .bo-pill {
    box-shadow:
      0 0 0 2px #fff,
      0 0 0 4px var(--bo-sheet-focus);
  }

  .jfb-form .bo-insert-after {
    inset-block-start: 100%;
  }

  .jfb-form .jfb-fields {
    position: relative;
  }

  /* In the room above the group's first field, below a legend. */
  .jfb-form .bo-insert-first {
    inset-block-start: calc(var(--bo-room) - 24px);
  }

  /* The insert point right above the selected element lies under its bar: the "+" alone, at its start by the handle. */
  .jfb-form .bo-insert[data-beside-tools] {
    justify-content: flex-start;
    padding-inline-start: 28px;
  }

  .jfb-form .bo-insert[data-beside-tools] > .bo-pill {
    padding: 3px;
  }

  .jfb-form .bo-insert[data-beside-tools] .bo-pill-text {
    display: none;
  }

  .jfb-form .bo-row-start {
    display: contents;
  }

  /* A repeatable group's own fields lie above the group's button, which covers the whole group. */
  .jfb-form .bo-field .bo-field > .bo-chrome > .bo-select {
    z-index: 5;
  }

  .jfb-form .bo-field .bo-field > .bo-chrome > .bo-insert,
  .jfb-form .bo-field .bo-row-start > :is(.bo-insert, .bo-empty-group) {
    z-index: 6;
  }

  .jfb-form .bo-field .bo-field > .bo-chrome > .bo-tools {
    z-index: 7;
  }

  /* A group without fields, and the end of a page: places to add one, always in sight. */
  .jfb-form :is(.bo-empty-group, .bo-add-field) {
    display: flex;
    align-items: center;
    justify-content: center;
    gap: 8px;
    position: relative;
    z-index: 3;
    min-height: 44px;
    margin: 0;
    padding: 8px 16px;
    border: 1px dashed var(--bo-sheet-line);
    border-radius: 0;
    background: none;
    color: var(--bo-sheet-quiet);
    font: inherit;
    cursor: pointer;
  }

  .jfb-form .bo-empty-group {
    grid-column: 1 / -1;
  }

  .jfb-form .bo-empty-group[data-active] {
    border-color: var(--bo-accent);
    color: var(--bo-accent);
    background: color-mix(in srgb, var(--bo-accent) 6%, transparent);
  }

  @media (hover: hover) {
    .jfb-form :is(.bo-empty-group, .bo-add-field):hover {
      border-color: var(--bo-accent);
      color: var(--bo-accent);
      background: color-mix(in srgb, var(--bo-accent) 6%, transparent);
    }
  }

  .jfb-form :is(.bo-empty-group, .bo-add-field, .bo-link):focus-visible {
    outline: 2px solid var(--bo-sheet-focus);
    outline-offset: 2px;
  }

  /* A press answers at once, as Umbraco's own buttons do: a wide place to add a field sinks, a button shrinks. */
  .jfb-form :is(.bo-empty-group, .bo-add-field, .bo-tools button),
  :is(.bo-widths button, .bo-check, .bo-step-add, .bo-step:not([data-page])) {
    transition: transform var(--jfb-bo-motion-fast) var(--jfb-bo-motion-ease);
  }

  .jfb-form :is(.bo-empty-group, .bo-add-field):active {
    transform: translateY(1px);
  }

  .jfb-form .bo-tools button:active,
  :is(.bo-widths button, .bo-check, .bo-step-add, .bo-step:not([data-page])):active {
    transform: scale(0.97);
  }

  .jfb-form .bo-empty {
    position: relative;
    z-index: 3;
    margin: 0;
    color: var(--bo-sheet-quiet);
    text-align: center;
  }

  .jfb-form .bo-link {
    margin: 0;
    padding: 0;
    border: 0;
    background: none;
    color: var(--bo-accent);
    font: inherit;
    text-decoration: underline;
    text-underline-offset: 0.2em;
    cursor: pointer;
  }

  /* A field visitors don't see: a quiet note in its place. */
  .jfb-form .bo-note {
    margin: 0;
    padding: 10px 14px;
    border: 1px dashed var(--bo-sheet-line);
    border-radius: 0;
    color: var(--bo-sheet-quiet);
    font-size: 0.875em;
  }

  .jfb-form .bo-label-box {
    box-sizing: border-box;
    inline-size: min(100%, 32em);
    margin: 0;
    padding: 2px 6px;
    border: 2px solid var(--bo-accent);
    border-radius: var(--uui-border-radius);
    background: #fff;
    color: inherit;
    font: inherit;
  }

  .jfb-form .bo-label-box:focus {
    outline: 2px solid var(--bo-sheet-focus);
    outline-offset: 1px;
  }

  /*
   * The handle, "Move Email address": a tab on the frame's top left corner, as the selected element's tools are on its
   * right, shown on hover, on keyboard focus and while selected (ADR-0016). It drags the element; Alt+Up and Alt+Down on
   * it move it one place. A field in a repeatable group shows its own, not the group's. Hidden, it takes no pointer, so
   * it never catches a click meant for the field above it. Its own button and itself say when it shows, as siblings:
   * a rule that asks whether a field holds the pointer would be checked for every field at every move of the mouse. It
   * shows at once: every selection, focus and hover reveals one and hides another, and a fade would paint the whole sheet
   * again for each frame of it (a form of 300 fields, ADR-0169).
   */
  .jfb-form .bo-handle {
    position: absolute;
    inset-block-end: calc(100% + 8px);
    inset-inline-start: -8px;
    z-index: 4;
    display: grid;
    place-items: center;
    width: 28px;
    height: 28px;
    margin: 0;
    padding: 0;
    border: 0;
    border-radius: var(--uui-border-radius) var(--uui-border-radius) 0 0;
    background: var(--bo-accent);
    color: var(--bo-on-accent);
    opacity: 0;
    pointer-events: none;
    cursor: grab;
    touch-action: none;
    user-select: none;
  }

  /* Above the gap it shares with the insert point below the field before it, which it covers only while it shows. */
  .jfb-form .bo-handle-group {
    inset-block-end: calc(100% + 12px);
    inset-inline-start: -12px;
    z-index: 4;
  }

  :host(:not([data-dragging])) .jfb-form .bo-select:is(:focus-visible, [aria-current='true']) ~ .bo-handle,
  :host(:not([data-dragging])) .jfb-form .bo-handle:focus-visible {
    opacity: 1;
    pointer-events: auto;
  }

  @media (hover: hover) {
    :host(:not([data-dragging])) .jfb-form .bo-select:hover ~ .bo-handle,
    :host(:not([data-dragging])) .jfb-form .bo-handle:hover {
      opacity: 1;
      pointer-events: auto;
    }
  }

  .jfb-form .bo-handle:focus-visible {
    outline: 2px solid var(--bo-sheet-focus);
    outline-offset: 2px;
  }

  .jfb-form .bo-field .bo-field > .bo-chrome > .bo-handle {
    z-index: 7;
  }

  /*
   * Why the form refused the element's last move, beside its handle until the next change. It stays below the selected
   * element's tools and leaves them their room on the right, growing upwards on a narrow element.
   */
  .jfb-form .bo-refusal {
    position: absolute;
    inset-block-end: calc(100% + 8px);
    inset-inline-start: 24px;
    z-index: 3;
    max-inline-size: max(140px, min(320px, calc(100% - 300px)));
    margin: 0;
    padding: 4px 10px;
    border-radius: var(--uui-border-radius);
    background: var(--uui-color-danger);
    color: var(--uui-color-danger-contrast);
    font: 600 12px/1.5 var(--uui-font-family);
  }

  .jfb-form .bo-group > .bo-refusal {
    inset-block-end: calc(100% + 12px);
    inset-inline-start: 20px;
  }

  .jfb-form .bo-page > .bo-refusal {
    inset-block: -16px auto;
    inset-inline-start: 16px;
  }

  /*
   * A drag, in the top layer above everything else: a dashed slot keeps the dragged element's place, a line shows
   * where it would land, in the colour of a refusal with the reason beside it where the form doesn't take it, and a
   * copy with its name follows the pointer. The line jumps from place to place: it follows the pointer.
   */
  :host([data-dragging]),
  :host([data-dragging]) * {
    cursor: grabbing;
    user-select: none;
  }

  /* While something is dragged, the line alone says where it lands: the selection, its tools, the handles (above),
     the insert points and a refusal's note step back until the drop. */
  :host([data-dragging]) .jfb-form :is(.bo-tools, .bo-insert, .bo-refusal, .bo-chips) {
    visibility: hidden;
  }

  :host([data-dragging]) .jfb-form .bo-select {
    outline: none;
    box-shadow: none;
  }

  .bo-drag {
    position: fixed;
    inset: 0;
    width: auto;
    height: auto;
    max-width: none;
    max-height: none;
    margin: 0;
    padding: 0;
    border: 0;
    background: transparent;
    overflow: visible;
    pointer-events: none;
  }

  .bo-marks {
    position: fixed;
    inset: 0;
  }

  .bo-slot,
  .bo-line,
  .bo-follow {
    position: fixed;
    inset-block-start: 0;
    inset-inline-start: 0;
  }

  /* The copy, and why the form refuses the place under it: they follow the pointer, never cut off by an area's edge. */
  .bo-follow {
    display: grid;
    justify-items: start;
    gap: 6px;
  }

  .bo-slot {
    box-sizing: border-box;
    border: 1px dashed var(--jfb-bo-color-control-border);
    border-radius: 0;
    background: var(--uui-color-surface-emphasis);
  }

  .bo-slot[data-sheet] {
    border-color: var(--bo-sheet-line);
    background: var(--bo-sheet-slot);
  }

  .bo-line {
    background: var(--bo-accent);
  }

  .bo-line::before,
  .bo-line::after {
    content: '';
    position: absolute;
    inset-block-start: -3px;
    width: 8px;
    height: 8px;
    border-radius: 50%;
    background: inherit;
  }

  .bo-line::before {
    inset-inline-start: -4px;
  }

  .bo-line::after {
    inset-inline-end: -4px;
  }

  /* Between the steps the line stands upright, its dots at its top and bottom. */
  .bo-line[data-upright]::before,
  .bo-line[data-upright]::after {
    inset-inline-start: -3px;
    inset-inline-end: auto;
  }

  .bo-line[data-upright]::before {
    inset-block-start: -4px;
  }

  .bo-line[data-upright]::after {
    inset-block-start: auto;
    inset-block-end: -4px;
  }

  .bo-line[data-refused] {
    background: var(--uui-color-danger);
  }

  .bo-why {
    max-inline-size: min(320px, 90vw);
    margin: 0;
    padding: 4px 10px;
    border-radius: var(--uui-border-radius);
    background: var(--uui-color-danger);
    color: var(--uui-color-danger-contrast);
    font: 600 12px/1.5 var(--uui-font-family);
  }

  .bo-why:empty,
  .bo-slot[hidden],
  .bo-line[hidden] {
    display: none;
  }

  .bo-ghost {
    display: inline-flex;
    align-items: center;
    gap: 8px;
    max-inline-size: 280px;
    padding: 8px 12px;
    border-radius: var(--uui-border-radius);
    background: var(--uui-color-surface);
    box-shadow: var(--uui-shadow-depth-3);
    color: var(--uui-color-text);
    font: 700 14px/1.4 var(--uui-font-family);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }

  .bo-step[data-drop] {
    outline: 2px solid var(--bo-accent);
    outline-offset: 3px;
  }

  .bo-step[data-drop='refused'] {
    outline-color: var(--uui-color-danger);
  }

  .bo-after {
    display: grid;
    gap: var(--uui-size-space-5);
    justify-items: start;
  }

  @media (prefers-reduced-motion: reduce) {
    .jfb-form :is(.bo-empty-group, .bo-add-field, .bo-tools button, .bo-pill),
    :is(.bo-widths button, .bo-check, .bo-step-add, .bo-step:not([data-page])) {
      transition: none;
    }

    .jfb-form :is(.bo-empty-group, .bo-add-field, .bo-tools button):active,
    .jfb-form .bo-insert:active > .bo-pill,
    :is(.bo-widths button, .bo-check, .bo-step-add, .bo-step:not([data-page])):active {
      transform: none;
    }

    .bo-menu {
      transition-property: opacity;
    }
  }

  @media (forced-colors: active) {
    .jfb-form .bo-select[aria-current='true'],
    .jfb-form .bo-select:focus-visible {
      outline-color: Highlight;
    }

    .jfb-form .bo-insert::before {
      border-top-color: CanvasText;
    }

    .jfb-form .bo-handle {
      border: 1px solid ButtonText;
      background: ButtonFace;
      color: ButtonText;
    }

    .bo-line,
    .bo-line[data-refused] {
      background: Highlight;
    }

    .bo-slot {
      border-color: CanvasText;
    }

    .jfb-form .bo-refusal,
    .bo-why,
    .bo-ghost {
      border: 1px solid CanvasText;
    }

    .bo-step[data-drop],
    .bo-step[data-drop='refused'] {
      outline-color: Highlight;
    }
  }
`;
