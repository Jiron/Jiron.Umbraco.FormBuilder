// The insights' tables (ROADMAP 11.5 and 11.5b, ADR-0211, ADR-0212): what "Show as tables" swaps a chart for, in the
// Overview and in the Answers alike. The first column names each row, the columns that hold numbers are aligned to the
// end, and a caption names the table for assistive technology; a long table scrolls inside its box.
import { css, html, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';

/** The tables' look; the host also carries `jfbVisuallyHidden`, which hides the caption. */
export const jfbInsightTable = css`
  table {
    inline-size: 100%;
    border-collapse: collapse;
    font-variant-numeric: tabular-nums;
  }

  th,
  td {
    padding: var(--uui-size-space-2) var(--uui-size-space-3);
    border-block-start: 1px solid var(--uui-color-divider);
    text-align: start;
    font-weight: 400;
  }

  thead th {
    border-block-start: 0;
    font-weight: 700;
  }

  th:first-child,
  td:first-child {
    padding-inline-start: 0;
  }

  .numeric {
    text-align: end;
  }

  .scroll {
    max-block-size: 22rem;
    position: relative;
    overflow: auto;
  }
`;

/** A table: the first column names each row, and the columns `numbers` says hold numbers are aligned to the end. */
export function insightTable(
  caption: string,
  columns: readonly string[],
  rows: readonly (readonly string[])[],
  numbers: (column: number) => boolean = (column) => column > 0,
): TemplateResult {
  return html`<div class="scroll">
    <table>
      <caption class="visually-hidden">
        ${caption}
      </caption>
      <thead>
        <tr>
          ${columns.map((column, index) => html`<th scope="col" class=${numbers(index) ? 'numeric' : ''}>${column}</th>`)}
        </tr>
      </thead>
      <tbody>
        ${rows.map(
          (row) =>
            html`<tr>
              ${row.map((cell, index) =>
                index === 0
                  ? html`<th scope="row">${cell}</th>`
                  : html`<td class=${numbers(index) ? 'numeric' : ''}>${cell}</td>`,
              )}
            </tr>`,
        )}
      </tbody>
    </table>
  </div>`;
}
