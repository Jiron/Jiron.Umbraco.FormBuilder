// The Settings view's look (ROADMAP 10.1a, IA 5.2, docs/ux/tokens.md): one column of groups on Umbraco's surface, each a
// box whose header opens it, the settings' boxes as the inspector draws them (control.styles.ts), and nothing that moves
// when a group opens: groups open many times a day, so they open at once. On a phone a group's line goes under its name
// and two boxes side by side go one under the other, so no text is squashed.
import { css } from '@umbraco-cms/backoffice/external/lit';

export const settingsStyles = css`
  :host {
    display: block;
    padding: var(--uui-size-layout-1);
  }

  .page {
    container-type: inline-size;
    display: grid;
    gap: var(--uui-size-space-5);
    max-inline-size: 760px;
    margin-inline: auto;
  }

  .toolbar {
    display: grid;
    gap: var(--uui-size-space-3);
  }

  .language {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: var(--uui-size-space-3);
    font-weight: 600;
  }

  .language select {
    inline-size: auto;
    max-inline-size: 100%;
    font-weight: 400;
  }

  .note {
    margin: 0;
    color: var(--jfb-bo-color-text-muted);
    font-size: var(--uui-type-small-size);
  }

  .group {
    border-radius: var(--uui-border-radius);
    background: var(--uui-color-surface);
    box-shadow: var(--uui-shadow-depth-1);
  }

  .group-head {
    margin: 0;
    font: inherit;
  }

  .group-head button {
    display: grid;
    grid-template-columns: auto minmax(0, 1fr);
    column-gap: var(--uui-size-space-3);
    row-gap: var(--uui-size-space-1);
    align-items: center;
    inline-size: 100%;
    min-block-size: 52px;
    padding: var(--uui-size-space-4) var(--uui-size-space-5);
    border: 0;
    border-radius: var(--uui-border-radius);
    background: none;
    color: inherit;
    font: inherit;
    text-align: start;
    cursor: pointer;
  }

  @media (hover: hover) {
    .group-head button:hover {
      background: var(--uui-color-surface-emphasis);
    }
  }

  .group-head button:focus-visible {
    outline: 2px solid var(--jfb-bo-color-focus);
    outline-offset: -2px;
  }

  .group-name {
    font-weight: 700;
  }

  /* A closed group's line: under the name on a phone, at the end of the row where there is room for it. */
  .summary {
    grid-column: 2;
    color: var(--jfb-bo-color-text-muted);
    font-size: var(--uui-type-small-size);
    overflow-wrap: anywhere;
  }

  .flag {
    color: var(--jfb-bo-color-danger-text);
    font-weight: 600;
  }

  @container (min-width: 560px) {
    .group-head button {
      grid-template-columns: auto max-content minmax(0, 1fr);
    }

    .summary {
      grid-column: 3;
      justify-self: end;
      text-align: end;
    }
  }

  .group-body {
    display: grid;
    gap: var(--uui-size-space-6);
    padding: var(--uui-size-space-5) var(--uui-size-space-5) var(--uui-size-space-6);
    border-block-start: 1px solid var(--uui-color-divider-standalone);
  }

  .more-toggle {
    display: inline-flex;
    align-items: center;
    gap: var(--uui-size-space-2);
    justify-self: start;
    min-block-size: 36px;
    padding: 0 var(--uui-size-space-2);
    border: 0;
    border-radius: var(--uui-border-radius);
    background: none;
    color: var(--uui-color-interactive);
    font: inherit;
    font-weight: 600;
    cursor: pointer;
  }

  @media (hover: hover) {
    .more-toggle:hover {
      color: var(--uui-color-interactive-emphasis);
    }
  }

  .more-toggle .flag {
    margin-inline-start: var(--uui-size-space-2);
    font-size: var(--uui-type-small-size);
  }

  .more-toggle:focus-visible {
    outline: 2px solid var(--jfb-bo-color-focus);
    outline-offset: 2px;
  }

  .more-body {
    display: grid;
    gap: var(--uui-size-space-6);
    padding-block-start: var(--uui-size-space-5);
    border-block-start: 1px solid var(--uui-color-divider-standalone);
  }

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

  /* A chosen provider's own settings, under the choices, lined up with the choices' names. */
  .provider-settings {
    margin-inline-start: calc(var(--uui-size-space-3) + 9px);
  }

  .checks {
    display: grid;
    gap: var(--uui-size-space-3);
    margin: 0;
    padding: 0;
    list-style: none;
  }

  .checks label {
    display: flex;
    align-items: center;
    gap: var(--uui-size-space-3);
    cursor: pointer;
  }

  .skeleton {
    display: grid;
    gap: var(--uui-size-space-5);
  }

  .skeleton span {
    display: block;
    block-size: 52px;
    border-radius: var(--uui-border-radius);
    background: var(--uui-color-surface-alt);
  }

  .skeleton span:first-child {
    block-size: 320px;
  }

  .failed {
    display: grid;
    justify-items: start;
    gap: var(--uui-size-space-4);
  }

  .failed p {
    margin: 0;
  }
`;
