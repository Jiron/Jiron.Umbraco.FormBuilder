// Two versions compared in words (ROADMAP 10.3a, IA 5.6, ADR-0196): fields, then settings, actions and rules, each part
// what was added, removed or moved in sentences, then a table of what changed with both values, the older first. Where
// the sidebar is narrow, each row stacks its values under its name, each said by its version. It reads as it opens,
// with Try again where it gets no answer. Beside Close, a version compared with the current draft offers Restore and
// publish... and the primary Restore as a new draft once it is read (ROADMAP 10.3b, ADR-0198); the caller restores. A
// draft with changes not saved yet says that they are left out, as a restore saves them first. Where the buttons don't
// fit the footer's row they wrap, and a narrow sidebar stacks them, the primary first, so none is cut off.
import { css, html, nothing, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { jfbBackofficeTokens } from '../../styles/tokens';
import { jfbSkeleton, skeletonLines } from '../../ui/skeleton';
import type { ChangePart } from './change-words';
import type { ComparisonModalData, ComparisonModalValue, ComparisonRead } from './comparison.modal.token';

export class ComparisonModalElement extends UmbModalBaseElement<ComparisonModalData, ComparisonModalValue> {
  static override properties = {
    ...UmbModalBaseElement.properties,
    _read: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    jfbSkeleton,
    css`
      :host {
        /* The footer's own height, which the actions keep while they fit one row. */
        --jfb-footer-height: var(--umb-footer-layout-height, 54px);
      }

      /* The layout, not the host, is the container: the host stays as Umbraco's sidebar places it, so the layout's
         height still comes from the sidebar. */
      umb-body-layout {
        --umb-footer-layout-height: auto;
        container: jfb-comparison / inline-size;
      }

      .actions {
        display: flex;
        flex-wrap: wrap;
        justify-content: flex-end;
        align-items: center;
        gap: var(--uui-size-space-2);
        box-sizing: border-box;
        min-block-size: var(--jfb-footer-height);
        padding-block: var(--uui-size-space-2);
      }

      @container jfb-comparison (width < 32rem) {
        .actions {
          flex-direction: column-reverse;
          align-items: stretch;
        }
      }

      .room {
        display: grid;
        gap: var(--uui-size-space-6);
        container-type: inline-size;
      }

      p {
        margin: 0;
      }

      .made,
      .unsaved {
        color: var(--jfb-bo-color-text-muted);
      }

      .part {
        display: grid;
        gap: var(--uui-size-space-3);
      }

      h4 {
        margin: 0;
        font-size: var(--uui-type-default-size);
        font-weight: 700;
      }

      ul {
        display: grid;
        gap: var(--uui-size-space-2);
        margin: 0;
        padding-inline-start: var(--uui-size-space-5);
      }

      li,
      th,
      td {
        overflow-wrap: anywhere;
      }

      table {
        width: 100%;
        border-collapse: collapse;
        table-layout: fixed;
      }

      th,
      td {
        padding: var(--uui-size-space-3) var(--uui-size-space-4) var(--uui-size-space-3) 0;
        border-top: 1px solid var(--uui-color-divider);
        text-align: start;
        vertical-align: top;
      }

      thead th {
        border-top: 0;
        font-weight: 700;
      }

      tbody th {
        font-weight: 400;
      }

      .older {
        color: var(--jfb-bo-color-text-muted);
      }

      .failed {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-4);
      }

      /* A narrow sidebar stacks each row: what changed, then each version's value under its name, so no text is squeezed
         into a third of the width. */
      @container (width < 32rem) {
        thead {
          position: absolute;
          width: 1px;
          height: 1px;
          overflow: hidden;
          clip-path: inset(50%);
        }

        tr {
          display: grid;
          gap: var(--uui-size-space-1);
          padding-block: var(--uui-size-space-3);
          border-top: 1px solid var(--uui-color-divider);
        }

        tbody th,
        tbody td {
          padding: 0;
          border: 0;
        }

        tbody th {
          font-weight: 700;
        }

        tbody td::before {
          /* The cell's own header names it for a screen reader; the version's name is only for sight. */
          content: attr(data-side) ': ' / '';
          color: var(--jfb-bo-color-text-muted);
        }
      }
    `,
  ];

  declare private _read: ComparisonRead | undefined;

  constructor() {
    super();
    this._read = undefined;
  }

  override connectedCallback() {
    super.connectedCallback();
    void this.#load();
  }

  #term = (key: string, ...args: unknown[]) => this.localize.term(`jfbHistory_${key}`, ...args);

  async #load() {
    const read = this.data?.read;
    if (!read) return;
    this._read = undefined;
    this._read = await read().catch((): ComparisonRead => ({ kind: 'failed' }));
  }

  #restore(restore: ComparisonModalValue['restore']) {
    this.value = { restore };
    this._submitModal();
  }

  #part(part: ChangePart): TemplateResult {
    const older = this.data?.olderName ?? '';
    const newer = this.data?.newerName ?? '';
    return html`<div class="part" data-part=${part.name}>
      <h4>${this.#term(`part_${part.name}`)}</h4>
      ${
        part.lines.length > 0
          ? html`<ul>
              ${part.lines.map((line) => html`<li>${line}</li>`)}
            </ul>`
          : nothing
      }
      ${
        part.rows.length > 0
          ? // The roles keep the table a table where a narrow sidebar stacks its rows, which some browsers then forget.
            html`<table role="table">
              <thead role="rowgroup">
                <tr role="row">
                  <th role="columnheader" scope="col">${this.#term('compare_changed')}</th>
                  <th role="columnheader" scope="col">${older}</th>
                  <th role="columnheader" scope="col">${newer}</th>
                </tr>
              </thead>
              <tbody role="rowgroup">
                ${part.rows.map(
                  (row) =>
                    html`<tr role="row">
                      <th role="rowheader" scope="row">${row.what}</th>
                      <td role="cell" class="older" data-side=${older}>${row.older}</td>
                      <td role="cell" data-side=${newer}>${row.newer}</td>
                    </tr>`,
                )}
              </tbody>
            </table>`
          : nothing
      }
    </div>`;
  }

  #body(): TemplateResult {
    const read = this._read;
    if (!read) return skeletonLines(this.#term('compare_loading'), [64, 48, 56]);
    if (read.kind === 'failed')
      return html`<div class="failed" role="alert">
        <span>${this.#term('compare_failed')}</span>
        <uui-button look="outline" label=${this.#term('retry')} @click=${() => void this.#load()}></uui-button>
      </div>`;
    if (read.kind === 'unreadable') return html`<p role="status">${this.#term('compare_unreadable', read.name)}</p>`;
    if (read.parts.length === 0) return html`<p role="status">${this.#term('compare_none')}</p>`;
    return html`${read.parts.map((part) => this.#part(part))}`;
  }

  override render() {
    const data = this.data;
    if (!data) return nothing;
    return html`<umb-body-layout headline=${data.headline}>
      <div class="room">
        ${data.made ? html`<p class="made">${data.made}</p>` : nothing}
        ${data.restorable && data.unsaved ? html`<p class="unsaved">${this.#term('compare_unsaved')}</p>` : nothing}
        ${this.#body()}
      </div>
      <div slot="actions" class="actions">
        <uui-button label=${this.#term('compare_close')} @click=${this._rejectModal}></uui-button>
        ${
          // A restore acts on what the comparison shows, so its buttons come once it is read.
          data.restorable && this._read?.kind === 'compared'
            ? html`${
                  data.publishable
                    ? html`<uui-button
                        look="secondary"
                        label=${this.#term('restoreAndPublish')}
                        @click=${() => this.#restore('publish')}
                      ></uui-button>`
                    : nothing
                }
                <uui-button
                  look="primary"
                  color="positive"
                  label=${this.#term('restore')}
                  @click=${() => this.#restore('draft')}
                ></uui-button>`
            : nothing
        }
      </div>
    </umb-body-layout>`;
  }
}

if (!customElements.get('jfb-version-comparison-modal'))
  customElements.define('jfb-version-comparison-modal', ComparisonModalElement);

export { ComparisonModalElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-version-comparison-modal': ComparisonModalElement;
  }
}
