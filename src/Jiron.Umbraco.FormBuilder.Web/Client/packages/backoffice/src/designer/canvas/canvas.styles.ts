// The canvas's own rules (ROADMAP 9.3b, ADR-0168), after the default theme's in the canvas's shadow root: the toolbar,
// the steps and the sheet in Umbraco's colours, and the designer's controls laid over the form, each prefixed bo- so
// that none reaches the form's own look. The form itself keeps the theme's light colours in every backoffice theme, as
// visitors see it. The controls over a field are positioned out of the flow, so the theme's grid of a group's fields,
// its rows shared between columns and its spacing between a field's parts hold as on the website.
import { css } from '@umbraco-cms/backoffice/external/lit';

export const canvasChrome = css`
  :host {
    --bo-accent: var(--uui-color-selected, #3544b1);
    --bo-on-accent: var(--uui-color-selected-contrast, #fff);
    --bo-focus: var(--uui-color-focus, #3879ff);
    --bo-sheet-padding: 32px;
    display: flex;
    flex-direction: column;
    min-width: 0;
    min-height: 0;
    background: var(--uui-color-background);
    color: var(--uui-color-text);
    font-family: var(--uui-font-family);
  }

  .bo-loading {
    display: grid;
    place-items: center;
    padding: var(--uui-size-layout-3);
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

  .bo-widths button:hover {
    background: var(--uui-color-surface-emphasis);
  }

  .bo-widths button[aria-pressed='true'] {
    background: var(--bo-accent);
    color: var(--bo-on-accent);
    font-weight: 700;
  }

  :is(.bo-widths, .bo-steps, .bo-menu) button:focus-visible {
    position: relative;
    outline: 2px solid var(--bo-focus);
    outline-offset: 2px;
  }

  .bo-narrowed {
    color: var(--jfb-bo-color-text-muted);
    font-size: var(--uui-type-small-size);
  }

  .bo-more {
    margin-inline-start: auto;
  }

  .bo-menu {
    display: grid;
    min-width: 200px;
    padding: var(--uui-size-space-2) 0;
    border-radius: var(--uui-border-radius);
    background: var(--uui-color-surface);
    box-shadow: var(--uui-shadow-depth-3);
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

  .bo-menu button:hover:not(:disabled) {
    background: var(--uui-color-surface-emphasis);
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

  .bo-step:hover {
    background: var(--uui-color-surface-emphasis);
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

  .bo-step-add:hover {
    color: var(--uui-color-interactive-emphasis);
  }

  .bo-viewport {
    flex: 1 1 auto;
    min-height: 0;
    overflow: auto;
    padding: 0 var(--uui-size-layout-1) var(--uui-size-layout-1);
  }

  .bo-viewport:focus-visible {
    outline: 2px solid var(--bo-focus);
    outline-offset: -2px;
  }

  /* The visitor's page: white, whatever the backoffice's theme, as the default theme's light scheme expects. */
  .bo-sheet {
    box-sizing: border-box;
    inline-size: min(100%, calc(var(--bo-width) + 2 * var(--bo-sheet-padding)));
    margin-inline: auto;
    padding: var(--bo-sheet-padding);
    border: 1px solid var(--uui-color-border);
    border-radius: var(--uui-border-radius);
    background: #fff;
    color: #1d2433;
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

  .jfb-form .bo-select {
    position: absolute;
    inset: -8px;
    z-index: 2;
    margin: 0;
    padding: 0;
    border: 0;
    border-radius: 8px;
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

  .jfb-form .bo-select:hover {
    outline: 1px dashed color-mix(in srgb, var(--bo-accent) 70%, transparent);
  }

  .jfb-form .bo-select:focus-visible {
    outline: 2px solid var(--bo-focus);
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
      0 0 0 6px var(--bo-focus);
  }

  /* A field in a group with columns holds the space below it inside its box; the outline stays around the field. */
  @container jfb-form (min-width: 480px) {
    :is(.jfb-columns-2, .jfb-columns-3, .jfb-columns-4) > .jfb-fields > .bo-field > .bo-chrome > .bo-select {
      inset-block-end: calc(var(--jfb-space-5, 24px) - 8px);
    }

    :is(.jfb-columns-2, .jfb-columns-3, .jfb-columns-4) > .jfb-fields > .bo-field > .bo-chrome > .bo-insert-after {
      inset-block-start: calc(100% - var(--jfb-space-5, 24px));
    }
  }

  .jfb-form .bo-tools {
    position: absolute;
    inset-block-end: calc(100% + 8px);
    inset-inline-end: -8px;
    z-index: 4;
    display: flex;
    margin: 0;
    border-radius: 6px 6px 0 0;
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
    cursor: pointer;
  }

  .jfb-form .bo-tools button:hover {
    background: color-mix(in srgb, var(--bo-on-accent) 18%, transparent);
  }

  .jfb-form .bo-tools button:focus-visible {
    outline: 2px solid var(--bo-on-accent);
    outline-offset: -4px;
  }

  .jfb-form .bo-tools uui-icon {
    font-size: 14px;
  }

  /* An insert point: a line across the gap with "Add a field here" on it, in sight on hover and on focus. */
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
    transition: opacity 120ms cubic-bezier(0.23, 1, 0.32, 1);
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
    border-radius: 999px;
    background: var(--bo-accent);
  }

  .jfb-form .bo-pill > uui-icon {
    font-size: 14px;
  }

  .jfb-form .bo-insert:is(:hover, :focus, [data-active]) {
    opacity: 1;
  }

  .jfb-form .bo-insert:focus {
    outline: none;
  }

  .jfb-form .bo-insert:focus-visible > .bo-pill {
    box-shadow:
      0 0 0 2px #fff,
      0 0 0 4px var(--bo-focus);
  }

  .jfb-form .bo-insert-after {
    inset-block-start: 100%;
  }

  .jfb-form .jfb-fields {
    position: relative;
  }

  .jfb-form .bo-insert-first {
    inset-block-start: -24px;
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
    border: 1px dashed #8a92a0;
    border-radius: 8px;
    background: none;
    color: #3b4352;
    font: inherit;
    cursor: pointer;
  }

  .jfb-form .bo-empty-group {
    grid-column: 1 / -1;
  }

  .jfb-form :is(.bo-empty-group, .bo-add-field):hover,
  .jfb-form .bo-empty-group[data-active] {
    border-color: var(--bo-accent);
    color: var(--bo-accent);
    background: color-mix(in srgb, var(--bo-accent) 6%, transparent);
  }

  .jfb-form :is(.bo-empty-group, .bo-add-field, .bo-link):focus-visible {
    outline: 2px solid var(--bo-focus);
    outline-offset: 2px;
  }

  /* A press answers at once, as Umbraco's own buttons do. */
  .jfb-form :is(.bo-empty-group, .bo-add-field, .bo-tools button) {
    transition: transform 120ms cubic-bezier(0.23, 1, 0.32, 1);
  }

  .jfb-form :is(.bo-empty-group, .bo-add-field, .bo-tools button):active {
    transform: scale(0.98);
  }

  .jfb-form .bo-empty {
    position: relative;
    z-index: 3;
    margin: 0;
    color: #3b4352;
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
    border: 1px dashed #8a92a0;
    border-radius: 8px;
    color: #4a5263;
    font-size: 0.875em;
  }

  .jfb-form .bo-label-box {
    box-sizing: border-box;
    inline-size: min(100%, 32em);
    margin: 0;
    padding: 2px 6px;
    border: 2px solid var(--bo-accent);
    border-radius: 4px;
    background: #fff;
    color: inherit;
    font: inherit;
  }

  .jfb-form .bo-label-box:focus {
    outline: 2px solid var(--bo-focus);
    outline-offset: 1px;
  }

  .bo-after {
    display: grid;
    gap: var(--uui-size-space-5);
    justify-items: start;
  }

  @media (prefers-reduced-motion: reduce) {
    .jfb-form :is(.bo-insert, .bo-empty-group, .bo-add-field, .bo-tools button) {
      transition: none;
    }

    .jfb-form :is(.bo-empty-group, .bo-add-field, .bo-tools button):active {
      transform: none;
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
  }
`;
