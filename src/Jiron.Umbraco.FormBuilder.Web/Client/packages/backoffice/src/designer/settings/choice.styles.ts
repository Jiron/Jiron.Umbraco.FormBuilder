// The boxes of a setting as the Settings view and an outcome's sidebar arrange them (ROADMAP 10.1a, 10.1b): a group of
// boxes under its legend, two side by side, short boxes, a setting with what its choice brings under it, a problem's
// line with its sign, and a list of choices, each a radio with its name and its line, the row a target.
import { css } from '@umbraco-cms/backoffice/external/lit';

export const choiceStyles = css`
  fieldset.item {
    min-inline-size: 0;
    gap: var(--uui-size-space-3);
  }

  legend.label {
    margin-block-end: var(--uui-size-space-2);
  }

  /* Two boxes that belong together side by side, one under the other on a phone. */
  .pair {
    display: grid;
    grid-template-columns: repeat(2, minmax(0, 1fr));
    gap: var(--uui-size-space-4);
  }

  @container (max-width: 480px) {
    .pair {
      grid-template-columns: minmax(0, 1fr);
    }
  }

  .pair .item > .label {
    font-weight: 400;
  }

  .short {
    max-inline-size: 22rem;
  }

  input.number {
    max-inline-size: 8rem;
  }

  /* A setting with the settings its choice brings: those under it, indented, the setting itself where it was. */
  .with-detail {
    display: grid;
    gap: var(--uui-size-space-4);
    min-inline-size: 0;
  }

  .detail {
    display: grid;
    gap: var(--uui-size-space-5);
    min-inline-size: 0;
    padding-inline-start: var(--uui-size-space-4);
    border-inline-start: 2px solid var(--uui-color-border);
  }

  /* A problem's words with its sign, the weight said by the sign and in words as well as by the colour. */
  .why {
    display: flex;
    align-items: flex-start;
    gap: var(--uui-size-space-2);
  }

  .why uui-icon {
    flex: none;
    margin-block-start: 2px;
    font-size: 14px;
  }

  .why[data-severity='warning'] {
    color: var(--jfb-bo-color-warning-text);
  }

  .why[data-severity='hint'] {
    color: var(--jfb-bo-color-text-muted);
  }

  /* A switch that can't be turned now says so by its look; its help says why. */
  input[role='switch']:disabled,
  input[role='switch']:disabled + .label {
    cursor: not-allowed;
    opacity: 0.55;
  }

  .example code {
    font-weight: 600;
  }

  /* The protection's choices: a radio, its name and the line under it, the whole row a target. */
  .choices {
    display: grid;
    gap: var(--uui-size-space-1);
  }

  .choice {
    display: grid;
    grid-template-columns: auto minmax(0, 1fr);
    column-gap: var(--uui-size-space-3);
    row-gap: var(--uui-size-space-1);
    align-items: start;
    padding: var(--uui-size-space-3);
    border-radius: var(--uui-border-radius);
    cursor: pointer;
  }

  @media (hover: hover) {
    .choice:hover {
      background: var(--uui-color-surface-emphasis);
    }
  }

  .choice-name {
    font-weight: 600;
  }

  .choice-line {
    grid-column: 2;
    color: var(--jfb-bo-color-text-muted);
    font-size: var(--uui-type-small-size);
  }

  /* A provider without its keys: its name in the muted colour, which keeps the contrast text needs. */
  .choice[data-dimmed] .choice-name {
    color: var(--jfb-bo-color-text-muted);
    font-weight: 400;
  }

  /* The browser's own radio, not the boxes' look, which every input but a checkbox takes from control.styles.ts. */
  input[type='radio'] {
    flex: none;
    box-sizing: border-box;
    inline-size: 18px;
    block-size: 18px;
    min-height: 0;
    margin: 0;
    padding: 0;
    accent-color: var(--uui-color-selected);
    cursor: pointer;
  }

  input[type='radio']:focus-visible {
    outline: 2px solid var(--jfb-bo-color-focus);
    outline-offset: 2px;
  }
`;
