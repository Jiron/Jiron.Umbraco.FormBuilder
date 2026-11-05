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

  .flag[data-severity='warning'] {
    color: var(--jfb-bo-color-warning-text);
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
