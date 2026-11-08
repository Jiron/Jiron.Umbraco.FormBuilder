// The matrix of groups and permissions (ROADMAP 12.4a, IA 8, ADR-0218), on Settings > Access and in a folder's or form's
// Permissions sidebar: a table whose rows are the permissions, in their parts, and whose columns are the user groups,
// each cell a box named by its permission and its group. A site can have any number of groups, so the table scrolls
// sideways inside its box with the permission column fixed, and the box can be scrolled with the keyboard.
import { css, html, live, nothing, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';

/** A column: a user group. */
export interface MatrixColumn {
  readonly key: string;
  readonly name: string;
  /** Whether its boxes can't change, as the administrators' can't. */
  readonly fixed: boolean;
  /** Takes the group off the matrix, where one may: a folder's or form's sidebar. */
  readonly remove?: () => void;
}

/** A part of the permissions, with its rows. */
export interface MatrixPart {
  readonly label: string;
  readonly permissions: readonly { readonly name: string; readonly label: string }[];
}

/** What the matrix draws and how its boxes answer. */
export interface MatrixOptions {
  readonly id: string;
  /** What the table is, said to screen readers as its caption. */
  readonly caption: string;
  /** The first column's header. */
  readonly permissionHeader: string;
  readonly columns: readonly MatrixColumn[];
  readonly parts: readonly MatrixPart[];
  readonly granted: (group: string, permission: string) => boolean;
  /** Whether every box is read-only, as the host's configuration makes them. */
  readonly readOnly: boolean;
  readonly toggle: (group: string, permission: string, on: boolean) => void;
  /** A box's name: the permission and the group, "Edit entries, Customer service". */
  readonly cellLabel: (permission: string, group: string) => string;
  /** The remove button's name for a group. */
  readonly removeLabel?: (group: string) => string;
}

export const matrixStyles = css`
  /*
   * A shade at an edge where more columns lie beyond it, as overlay scrollbars show none until scrolled: the covers
   * scroll with the table and hide the shades at its ends.
   */
  .matrix {
    overflow-x: auto;
    max-inline-size: 100%;
    border: 1px solid var(--uui-color-border);
    border-radius: var(--uui-border-radius);
    background:
      linear-gradient(to left, var(--uui-color-surface) 40%, transparent) right / 2.5rem 100% no-repeat local,
      radial-gradient(farthest-side at 100% 50%, rgb(0 0 0 / 14%), transparent) right / 0.75rem 100% no-repeat scroll,
      var(--uui-color-surface);
  }

  .matrix:focus-visible {
    outline: 2px solid var(--jfb-bo-color-focus);
    outline-offset: 2px;
  }

  .matrix table {
    border-collapse: separate;
    border-spacing: 0;
    min-inline-size: 100%;
  }

  .matrix th,
  .matrix td {
    padding: var(--uui-size-space-2) var(--uui-size-space-4);
    border-block-end: 1px solid var(--uui-color-divider);
    text-align: center;
    white-space: nowrap;
  }

  .matrix thead th {
    font-weight: 600;
    vertical-align: bottom;
  }

  .matrix th[scope='row'],
  .matrix .corner {
    position: sticky;
    inset-inline-start: 0;
    z-index: 1;
    background: var(--uui-color-surface);
    text-align: start;
    font-weight: 400;
    border-inline-end: 1px solid var(--uui-color-divider);
  }

  .matrix .corner {
    font-weight: 600;
  }

  .matrix .part th {
    position: sticky;
    inset-inline-start: 0;
    background: var(--uui-color-surface-alt);
    color: var(--jfb-bo-color-text-muted);
    font-size: var(--uui-type-small-size);
    font-weight: 600;
    text-align: start;
  }

  .matrix tbody tr:last-child td,
  .matrix tbody:last-child tr:last-child th {
    border-block-end: 0;
  }

  .matrix .group {
    display: inline-flex;
    align-items: center;
    gap: var(--uui-size-space-1);
  }

  .matrix input[type='checkbox'] {
    margin: 0;
    vertical-align: middle;
  }
`;

/** The matrix of the columns' grants. */
export function accessMatrix(options: MatrixOptions): TemplateResult {
  const { columns } = options;
  return html`<div class="matrix" role="region" aria-labelledby=${`${options.id}-caption`} tabindex="0">
    <table id=${options.id}>
      <caption id=${`${options.id}-caption`} class="visually-hidden">
        ${options.caption}
      </caption>
      <thead>
        <tr>
          <th scope="col" class="corner">${options.permissionHeader}</th>
          ${columns.map(
            (column) =>
              html`<th scope="col">
                <span class="group">
                  <span>${column.name}</span>
                  ${
                    column.remove && !options.readOnly
                      ? html`<uui-button
                          compact
                          look="secondary"
                          label=${options.removeLabel?.(column.name) ?? column.name}
                          @click=${column.remove}
                          ><uui-icon name="icon-remove" aria-hidden="true"></uui-icon
                        ></uui-button>`
                      : nothing
                  }
                </span>
              </th>`,
          )}
        </tr>
      </thead>
      ${options.parts.map(
        (part) =>
          html`<tbody>
            <tr class="part">
              <th scope="colgroup" colspan=${columns.length + 1}>${part.label}</th>
            </tr>
            ${part.permissions.map(
              (permission) =>
                html`<tr>
                  <th scope="row">${permission.label}</th>
                  ${columns.map(
                    (column) =>
                      html`<td>
                        <input
                          type="checkbox"
                          .checked=${live(options.granted(column.key, permission.name))}
                          ?disabled=${options.readOnly || column.fixed}
                          aria-label=${options.cellLabel(permission.label, column.name)}
                          @change=${(event: Event) =>
                            options.toggle(column.key, permission.name, (event.target as HTMLInputElement).checked)}
                        />
                      </td>`,
                  )}
                </tr>`,
            )}
          </tbody>`,
      )}
    </table>
  </div>`;
}
