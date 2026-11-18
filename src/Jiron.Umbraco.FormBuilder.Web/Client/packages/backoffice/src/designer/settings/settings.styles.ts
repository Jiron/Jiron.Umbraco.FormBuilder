// The Settings view's look (ROADMAP 10.1a, IA 5.2, docs/ux/tokens.md, ADR-0258): the groups as a list on Umbraco's
// surface, each row its name and its line, the row picked marked as the open entry is in the entries' list (the
// backoffice's own current colour, which keeps its text readable in the dark theme) and without its line, and beside
// it the group picked, its settings' boxes as the inspector draws them (control.styles.ts) and no wider than a text
// box reads well. Where the view has no room for both, the list stands alone, every row with its line, or the group
// in its place. On a phone two boxes side by side go one under the other, so no text is squashed.
import { css } from '@umbraco-cms/backoffice/external/lit';

export const settingsStyles = css`
  :host {
    container-type: inline-size;
    display: block;
    padding: var(--uui-size-layout-1);
  }

  /* The view takes the workspace's width: the list, and the group picked at a width text boxes read well in. */
  .page {
    display: grid;
    grid-template-columns: minmax(13rem, 18rem) minmax(0, 46rem);
    gap: var(--uui-size-space-6);
    align-items: start;
  }

  .groups ul {
    display: grid;
    margin: 0;
    padding: var(--uui-size-space-2) 0;
    border-radius: var(--uui-border-radius);
    background: var(--uui-color-surface);
    box-shadow: var(--uui-shadow-depth-1);
    list-style: none;
  }

  .groups button {
    display: grid;
    gap: var(--uui-size-space-1);
    inline-size: 100%;
    padding: var(--uui-size-space-3) var(--uui-size-space-5);
    border: 0;
    background: none;
    color: inherit;
    font: inherit;
    text-align: start;
    cursor: pointer;
  }

  @media (hover: hover) {
    .groups button:hover {
      background: var(--uui-color-surface-emphasis);
    }
  }

  .groups button[aria-current='true'] {
    background: var(--jfb-bo-color-current);
    color: var(--uui-color-current-contrast);
  }

  .groups button:focus-visible {
    outline: 2px solid var(--jfb-bo-color-focus);
    outline-offset: -2px;
  }

  .group-name {
    font-weight: 700;
  }

  .summary {
    font-size: var(--uui-type-small-size);
    overflow-wrap: anywhere;
  }

  .groups button:not([aria-current='true']) .summary {
    color: var(--jfb-bo-color-text-muted);
  }

  .flag {
    color: var(--jfb-bo-color-danger-text);
    font-weight: 600;
  }

  .flag[data-severity='warning'] {
    color: var(--jfb-bo-color-warning-text);
  }

  .picked {
    display: grid;
    gap: var(--uui-size-space-4);
  }

  .picked-head {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: var(--uui-size-space-3);
    min-block-size: 36px;
  }

  .picked-head h2 {
    margin: 0;
    font-size: var(--uui-type-h5-size);
  }

  .picked-head .bo-language {
    margin-inline-start: auto;
  }

  .note {
    margin: 0;
    color: var(--jfb-bo-color-text-muted);
    font-size: var(--uui-type-small-size);
  }

  /* "All settings", only where the group stands in the list's place. */
  .back {
    display: none;
    align-items: center;
    gap: var(--uui-size-space-2);
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

  .back:focus-visible {
    outline: 2px solid var(--jfb-bo-color-focus);
    outline-offset: 2px;
  }

  /* Beside its group, the row picked says its name alone: its settings say the rest (say it once), as an open group
     had no line. The rows keep their height, so a pick moves none of them. */
  @container (min-width: 700px) {
    .groups button {
      align-content: center;
      min-block-size: calc(2lh + var(--uui-size-space-1) + 2 * var(--uui-size-space-3));
    }

    .groups button[aria-current='true'] .summary {
      display: none;
    }
  }

  @container (max-width: 699px) {
    .page {
      grid-template-columns: minmax(0, 1fr);
    }

    .page:not(.in-group) .picked,
    .page.in-group .groups {
      display: none;
    }

    .back {
      display: inline-flex;
    }
  }

  .group-body {
    display: grid;
    gap: var(--uui-size-space-6);
    padding: var(--uui-size-space-5) var(--uui-size-space-5) var(--uui-size-space-6);
    border-radius: var(--uui-border-radius);
    background: var(--uui-color-surface);
    box-shadow: var(--uui-shadow-depth-1);
  }

  .group-body:focus-visible {
    outline: 2px solid var(--jfb-bo-color-focus);
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

  .more-body[hidden] {
    display: none;
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

  /* The form's alias with Copy beside it, which goes under it where the alias is long; the alias itself wraps anywhere. */
  .alias {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    gap: var(--uui-size-space-3);
  }

  .alias code {
    min-width: 0;
  }
`;
