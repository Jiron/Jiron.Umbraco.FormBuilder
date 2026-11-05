// The inspector's look (ROADMAP 9.4a, docs/ux/tokens.md): Umbraco's surface and tokens, the browser's own boxes drawn
// as UUI draws its own, and a switch that is a checkbox underneath. Nothing moves when a section opens: it opens many
// times a day, so it opens at once.
import { css } from '@umbraco-cms/backoffice/external/lit';

export const inspectorStyles = css`
  :host {
    display: flex;
    flex-direction: column;
    min-height: 0;
    background: var(--uui-color-surface);
    border-inline-start: 1px solid var(--uui-color-border);
    color: var(--uui-color-text);
  }

  :host([mode='rail']) {
    align-items: center;
    gap: var(--uui-size-space-2);
    padding-block: var(--uui-size-space-4);
  }

  .head {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: var(--uui-size-space-2);
    padding: var(--uui-size-space-3) var(--uui-size-space-3) var(--uui-size-space-3) var(--uui-size-space-5);
    border-block-end: 1px solid var(--uui-color-border);
  }

  .head h2 {
    flex: 1 1 auto;
    min-width: 0;
    margin: 0;
    font-size: var(--jfb-bo-type-title);
    font-weight: 700;
    line-height: 1.3;
    overflow-wrap: anywhere;
  }

  .head h2:focus {
    outline: none;
  }

  .head h2:focus-visible {
    outline: 2px solid var(--jfb-bo-color-focus);
    outline-offset: 2px;
  }

  .tools {
    display: flex;
    flex-wrap: wrap;
    gap: var(--uui-size-space-1);
    inline-size: 100%;
    order: 3;
  }

  .tools button,
  .menu button,
  .more {
    display: inline-flex;
    align-items: center;
    gap: var(--uui-size-space-2);
    min-height: 32px;
    padding: 0 var(--uui-size-space-3);
    border: 1px solid var(--jfb-bo-color-control-border);
    border-radius: var(--uui-border-radius);
    background: var(--uui-color-surface);
    color: var(--uui-color-text);
    font: inherit;
    cursor: pointer;
  }

  @media (hover: hover) {
    .tools button:hover,
    .menu button:hover,
    .more:hover {
      background: var(--uui-color-surface-emphasis);
    }
  }

  .tools button,
  .more {
    transition: transform var(--jfb-bo-motion-fast) var(--jfb-bo-motion-ease);
  }

  .tools button:active,
  .more:active {
    transform: scale(0.97);
  }

  .tools button:focus-visible,
  .more:focus-visible {
    outline: 2px solid var(--jfb-bo-color-focus);
    outline-offset: 2px;
  }

  .menu button:focus-visible {
    outline: 2px solid var(--jfb-bo-color-focus);
    outline-offset: -2px;
  }

  .menu {
    display: grid;
    gap: var(--uui-size-space-1);
    padding: var(--uui-size-space-2);
    border-radius: var(--uui-border-radius);
    background: var(--uui-color-surface);
    box-shadow: var(--uui-shadow-depth-3);
    /* It opens from its button with a short fade and a slight growth, as the canvas's menus do. */
    transform-origin: top right;
    transition:
      opacity var(--jfb-bo-motion-fast) var(--jfb-bo-motion-ease),
      transform var(--jfb-bo-motion-fast) var(--jfb-bo-motion-ease);
  }

  @starting-style {
    .menu {
      opacity: 0;
      transform: scale(0.97);
    }
  }

  .menu button {
    justify-content: flex-start;
    border-color: transparent;
  }

  /* Nothing selected: one quiet sentence under the heading. */
  .empty {
    margin: 0;
    padding: var(--uui-size-space-5);
    color: var(--jfb-bo-color-text-muted);
  }

  .note {
    margin: 0;
    padding: var(--uui-size-space-3) var(--uui-size-space-5);
    border-block-end: 1px solid var(--uui-color-border);
    background: var(--uui-color-surface-alt);
    font-size: var(--uui-type-small-size);
  }

  .body {
    flex: 1 1 auto;
    min-height: 0;
    overflow: auto;
    overscroll-behavior: contain;
  }

  .section + .section {
    border-block-start: 1px solid var(--uui-color-border);
  }

  .section-head {
    margin: 0;
    font: inherit;
  }

  .section-head button {
    display: grid;
    grid-template-columns: auto minmax(0, 1fr);
    column-gap: var(--uui-size-space-3);
    align-items: center;
    inline-size: 100%;
    min-height: 44px;
    padding: var(--uui-size-space-3) var(--uui-size-space-5) var(--uui-size-space-3) var(--uui-size-space-4);
    border: 0;
    background: none;
    color: inherit;
    font: inherit;
    text-align: start;
    cursor: pointer;
  }

  @media (hover: hover) {
    .section-head button:hover {
      background: var(--uui-color-surface-emphasis);
    }
  }

  .section-head button:focus-visible {
    outline: 2px solid var(--jfb-bo-color-focus);
    outline-offset: -2px;
  }

  .section-name {
    font-weight: 700;
  }

  .summary {
    grid-column: 2;
    overflow: hidden;
    color: var(--jfb-bo-color-text-muted);
    font-size: var(--uui-type-small-size);
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .section-line {
    display: flex;
    align-items: center;
  }

  .section-line .section-head {
    flex: 1 1 auto;
    min-inline-size: 0;
  }

  .section-line .add-rule {
    flex: none;
    margin-inline-end: var(--uui-size-space-4);
  }

  .add-rule,
  .rule {
    padding: var(--uui-size-space-1) 0;
    border: 0;
    background: none;
    color: var(--uui-color-interactive);
    font: inherit;
    text-align: start;
    text-decoration: underline;
    text-underline-offset: 0.2em;
    cursor: pointer;
  }

  .add-rule {
    justify-self: start;
  }

  @media (hover: hover) {
    .add-rule:hover,
    .rule:hover {
      color: var(--uui-color-interactive-emphasis);
    }
  }

  .add-rule:focus-visible,
  .rule:focus-visible {
    outline: 2px solid var(--jfb-bo-color-focus);
    outline-offset: 2px;
    border-radius: var(--uui-border-radius);
  }

  .rules {
    display: grid;
    gap: var(--uui-size-space-2);
    margin: 0;
    padding: 0;
    list-style: none;
  }

  .rules li {
    display: flex;
    flex-wrap: wrap;
    align-items: baseline;
    gap: var(--uui-size-space-2);
  }

  .off {
    padding: 0 var(--uui-size-space-2);
    border: 1px solid var(--jfb-bo-color-control-border);
    border-radius: var(--uui-border-radius);
    color: var(--jfb-bo-color-text-muted);
    font-size: var(--uui-type-small-size);
  }

  .section-body {
    display: grid;
    gap: var(--uui-size-space-5);
    padding: var(--uui-size-space-2) var(--uui-size-space-5) var(--uui-size-space-6);
  }

  .item {
    display: grid;
    gap: var(--uui-size-space-2);
    min-width: 0;
    margin: 0;
    padding: 0;
    border: 0;
  }

  .label {
    margin: 0;
    padding: 0;
    font-weight: 600;
  }

  .help,
  .why {
    margin: 0;
    font-size: var(--uui-type-small-size);
    line-height: 1.4;
  }

  .help {
    color: var(--jfb-bo-color-text-muted);
  }

  .why {
    color: var(--jfb-bo-color-danger-text);
  }

  /* Try it's answer, said in the colour of what it means as well as in words. */
  .help[data-result='match'] {
    color: var(--jfb-bo-color-positive-text);
    font-weight: 600;
  }

  .help[data-result='miss'] {
    color: var(--jfb-bo-color-danger-text);
    font-weight: 600;
  }

  input:not([type='checkbox']),
  select,
  textarea {
    box-sizing: border-box;
    inline-size: 100%;
    min-height: 36px;
    padding: var(--uui-size-space-2) var(--uui-size-space-3);
    border: 1px solid var(--jfb-bo-color-control-border);
    border-radius: var(--uui-border-radius);
    background: var(--uui-color-surface);
    color: var(--uui-color-text);
    font: inherit;
  }

  textarea {
    resize: vertical;
  }

  input::placeholder,
  textarea::placeholder {
    color: var(--jfb-bo-color-text-muted);
    opacity: 1;
  }

  input:focus-visible,
  select:focus-visible,
  textarea:focus-visible {
    outline: 2px solid var(--jfb-bo-color-focus);
    outline-offset: 0;
    border-color: var(--jfb-bo-color-focus);
  }

  /* As specific as the boxes' own rule above, so a box with a problem is marked. */
  input[aria-invalid='true'],
  select[aria-invalid='true'],
  textarea[aria-invalid='true'] {
    border-color: var(--jfb-bo-color-danger-text);
    border-width: 2px;
  }

  /* As specific as the boxes' own rule above, whose font would win otherwise: an alias, a formula, a pattern. */
  .code,
  input.code,
  textarea.code,
  code {
    font-family: var(--jfb-bo-font-mono);
  }

  code {
    overflow-wrap: anywhere;
  }

  .item-switch {
    display: flex;
    align-items: center;
    gap: var(--uui-size-space-3);
  }

  .item-switch label {
    flex: 1 1 auto;
    cursor: pointer;
  }

  .item-group,
  .item-stack {
    gap: var(--uui-size-space-3);
  }

  .item-group > .item-group,
  .item-group > .item {
    padding-inline-start: var(--uui-size-space-4);
    border-inline-start: 2px solid var(--uui-color-border);
  }

  .row {
    display: grid;
    grid-template-columns: minmax(0, 1fr) minmax(0, 1.4fr);
    gap: var(--uui-size-space-2);
  }

  /* A checkbox drawn as a switch: role="switch" says on and off, and the box stays the browser's own underneath. */
  input[type='checkbox'] {
    flex: none;
    inline-size: 18px;
    block-size: 18px;
    margin: 0;
    accent-color: var(--uui-color-selected);
    cursor: pointer;
  }

  input[role='switch'] {
    appearance: none;
    position: relative;
    inline-size: 36px;
    block-size: 20px;
    border: 1px solid var(--jfb-bo-color-control-border);
    border-radius: var(--jfb-bo-radius-round);
    background: var(--uui-color-border);
    transition: background-color var(--jfb-bo-motion-fast) ease;
  }

  input[role='switch']::before {
    content: '';
    position: absolute;
    inset-block-start: 2px;
    inset-inline-start: 2px;
    inline-size: 14px;
    block-size: 14px;
    border-radius: 50%;
    background: var(--uui-color-surface);
    box-shadow: var(--uui-shadow-depth-1);
    transition: transform var(--jfb-bo-motion-fast) var(--jfb-bo-motion-ease);
  }

  input[role='switch']:checked {
    border-color: var(--jfb-bo-color-selected);
    background: var(--jfb-bo-color-selected);
  }

  input[role='switch']:checked::before {
    transform: translateX(16px);
  }

  :host(:dir(rtl)) input[role='switch']:checked::before {
    transform: translateX(-16px);
  }

  input[type='checkbox']:focus-visible {
    outline: 2px solid var(--jfb-bo-color-focus);
    outline-offset: 2px;
  }

  .locked {
    display: flex;
    align-items: center;
    gap: var(--uui-size-space-2);
    margin: 0;
    padding: var(--uui-size-space-2) var(--uui-size-space-3);
    border: 1px dashed var(--jfb-bo-color-control-border);
    border-radius: var(--uui-border-radius);
    background: var(--uui-color-surface-alt);
  }

  .more {
    justify-self: start;
    border-color: transparent;
    padding-inline: var(--uui-size-space-2);
    color: var(--uui-color-interactive);
    font-weight: 600;
  }

  /* The option editor's rows (9.4b): a handle, the label, details and remove on one line, the details below it. */
  jfb-option-editor {
    display: contents;
  }

  .option-rows {
    display: grid;
    gap: var(--uui-size-space-2);
    margin: 0;
    padding: 0;
    list-style: none;
  }

  .option-row {
    position: relative;
  }

  .option-row[data-dragged] {
    opacity: 0.5;
  }

  .option-row[data-line-before]::before,
  .option-row[data-line-after]::after {
    content: '';
    position: absolute;
    inset-inline: 0;
    border-block-start: 2px solid var(--jfb-bo-color-selected);
  }

  .option-row[data-line-before]::before {
    inset-block-start: calc(-1 * var(--uui-size-space-1) - 1px);
  }

  .option-row[data-line-after]::after {
    inset-block-end: calc(-1 * var(--uui-size-space-1) - 1px);
  }

  .row-line {
    display: flex;
    align-items: center;
    gap: var(--uui-size-space-1);
  }

  .row-line > .row-label {
    flex: 1 1 auto;
    min-inline-size: 0;
  }

  .row-handle,
  .row-tool {
    display: inline-grid;
    flex: none;
    place-items: center;
    inline-size: 32px;
    block-size: 32px;
    margin: 0;
    padding: 0;
    border: 0;
    border-radius: var(--uui-border-radius);
    background: none;
    color: var(--jfb-bo-color-text-muted);
    cursor: pointer;
  }

  .row-handle {
    cursor: grab;
    touch-action: none;
  }

  @media (hover: hover) {
    .row-handle:hover,
    .row-tool:hover {
      background: var(--uui-color-surface-emphasis);
      color: var(--uui-color-text);
    }
  }

  .row-handle:focus-visible,
  .row-tool:focus-visible {
    outline: 2px solid var(--jfb-bo-color-focus);
    outline-offset: -2px;
  }

  /* A grid's "Needs an answer", once over the rows' boxes, which end where it ends. */
  .rows-head {
    display: flex;
    align-items: baseline;
    justify-content: space-between;
    gap: var(--uui-size-space-3);
  }

  .rows-caption {
    padding-inline-end: calc(32px + var(--uui-size-space-1));
    color: var(--jfb-bo-color-text-muted);
    font-size: var(--uui-type-small-size);
    text-align: end;
  }

  .row-check {
    display: inline-flex;
    flex: none;
    justify-content: flex-end;
    inline-size: 32px;
  }

  .row-details {
    display: grid;
    gap: var(--uui-size-space-4);
    margin: var(--uui-size-space-2) 0 var(--uui-size-space-3);
    margin-inline-start: 36px;
    padding-inline-start: var(--uui-size-space-3);
    border-inline-start: 2px solid var(--uui-color-border);
  }

  .row-actions {
    display: flex;
    flex-wrap: wrap;
    gap: var(--uui-size-space-2);
  }

  .paste {
    display: grid;
    gap: var(--uui-size-space-2);
  }

  .check {
    display: flex;
    align-items: center;
    gap: var(--uui-size-space-2);
    cursor: pointer;
  }

  @media (forced-colors: active) {
    .option-row[data-line-before]::before,
    .option-row[data-line-after]::after {
      border-color: Highlight;
    }

    input[role='switch'] {
      border-color: CanvasText;
    }

    input[role='switch']::before {
      background: CanvasText;
    }

    input[role='switch']:checked {
      background: Highlight;
    }
  }

  /* With reduced motion the knob jumps and a press stays still; the track's colour keeps its change. */
  @media (prefers-reduced-motion: reduce) {
    input[role='switch']::before,
    .tools button,
    .more {
      transition: none;
    }

    .tools button:active,
    .more:active {
      transform: none;
    }

    .menu {
      transition-property: opacity;
    }
  }
`;
