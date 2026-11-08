// The look of a setting's box (ROADMAP 9.4a, docs/ux/tokens.md): its label, help and problem, the browser's own boxes
// drawn as UUI draws its own, and a switch that is a checkbox underneath. The inspector and the Settings view share
// it, so a setting looks the same wherever it is edited.
import { css } from '@umbraco-cms/backoffice/external/lit';

export const controlStyles = css`
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

  /* A number with its unit after it: 90 days. */
  .with-unit {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: var(--uui-size-space-3);
  }

  .with-unit input {
    inline-size: 9rem;
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
`;
