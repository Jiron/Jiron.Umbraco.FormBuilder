// Library > Option lists (ROADMAP 12.1, IA 7, ADR-0214): the lists by name, each with its kind and the forms that use
// it, a row opening the list. The sidebar names the screen, so it has no title of its own (ADR-0021); the footer's one
// action is Create option list, which asks the kind first. With no list, one sentence says what lists are for.
import { css, html, nothing, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { umbOpenModal } from '@umbraco-cms/backoffice/modal';
import type { OptionListRowResponseModel } from '../api';
import { open } from '../library/library-changes';
import { jfbBackofficeTokens, jfbVisuallyHidden } from '../styles/tokens';
import { jfbSkeleton, skeletonLines } from '../ui/skeleton';
import { listPath, newListPath } from './constants';
import { NEW_OPTION_LIST_MODAL } from './new-option-list.modal.token';
import { isKind } from './option-list-model';
import { loadOptionListWords } from './option-list-words';
import { readRows } from './option-lists-api';

/** How many lists a page holds. */
const PAGE = 100;

type Rows =
  | { readonly state: 'loading' }
  | { readonly state: 'failed' | 'forbidden' }
  | { readonly state: 'ready'; readonly rows: readonly OptionListRowResponseModel[]; readonly total: number };

export class OptionListsScreenElement extends UmbLitElement {
  static override properties = {
    _rows: { state: true },
    _more: { state: true },
    _worded: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    jfbVisuallyHidden,
    jfbSkeleton,
    css`
      :host {
        display: block;
        block-size: 100%;
        container-type: inline-size;
      }

      .box--flush {
        --uui-box-default-padding: 0;
      }

      table {
        width: 100%;
        border-collapse: collapse;
      }

      th {
        padding: var(--uui-size-space-4) var(--uui-size-space-5);
        font-weight: 700;
        text-align: start;
      }

      td {
        padding: var(--uui-size-space-4) var(--uui-size-space-5);
        border-top: 1px solid var(--uui-color-divider);
        vertical-align: middle;
      }

      .end {
        text-align: end;
        white-space: nowrap;
        font-variant-numeric: tabular-nums;
      }

      .name {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-3);
        min-width: 0;
      }

      .name > uui-icon {
        flex: none;
      }

      .name a {
        color: var(--uui-color-text);
        text-decoration: none;
        overflow-wrap: anywhere;
      }

      .name a:hover,
      .name a:focus-visible {
        color: var(--uui-color-interactive-emphasis);
        text-decoration: underline;
      }

      .muted {
        color: var(--jfb-bo-color-text-muted);
      }

      .empty {
        max-width: 36rem;
        margin: var(--uui-size-layout-2) auto;
        text-align: center;
      }

      .failed {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-4);
      }

      .more {
        display: flex;
        justify-content: center;
        padding: var(--uui-size-space-4);
        border-top: 1px solid var(--uui-color-divider);
      }

      /* The kind gives way first on a narrow workspace: the name and the forms that use the list say the most. */
      @container (max-width: 480px) {
        .kind {
          display: none;
        }
      }
    `,
  ];

  declare private _rows: Rows;
  /** Whether the next page is being read. */
  declare private _more: boolean;
  /** Whether the screen's words are loaded: until then it shows its skeleton, so no key ever shows. */
  declare private _worded: boolean;

  #generation = 0;

  constructor() {
    super();
    this._rows = { state: 'loading' };
    this._more = false;
    this._worded = false;
  }

  override connectedCallback() {
    super.connectedCallback();
    void loadOptionListWords(this.localize.lang())
      .catch(() => undefined)
      .then(() => (this._worded = true));
    void this.#load();
  }

  #term = (key: string, ...args: unknown[]) => this.localize.term(`jfbOptionLists_${key}`, ...args);

  async #load() {
    const generation = ++this.#generation;
    if (this._rows.state !== 'ready') this._rows = { state: 'loading' };
    const read = await readRows(this, 0, PAGE);
    if (generation !== this.#generation) return;
    this._rows =
      read === undefined || read === 'forbidden'
        ? { state: read ?? 'failed' }
        : { state: 'ready', rows: read.items, total: read.total };
  }

  /** The next page; the focus goes to its first row's link, where the keyboard reads on, as the button may be gone. */
  async #loadMore() {
    const shown = this._rows;
    if (shown.state !== 'ready' || this._more) return;
    const generation = this.#generation;
    this._more = true;
    const read = await readRows(this, shown.rows.length, PAGE).finally(() => (this._more = false));
    if (generation !== this.#generation || read === undefined || read === 'forbidden') return;
    const known = new Set(shown.rows.map((row) => row.key));
    this._rows = {
      state: 'ready',
      rows: [...shown.rows, ...read.items.filter((row) => !known.has(row.key))],
      total: read.total,
    };
    await this.updateComplete;
    this.shadowRoot
      ?.querySelectorAll<HTMLElement>('tbody tr')
      [shown.rows.length]?.querySelector<HTMLElement>('a')
      ?.focus();
  }

  async #create() {
    const chosen = await umbOpenModal(this, NEW_OPTION_LIST_MODAL, { data: {} }).catch(() => undefined);
    if (chosen) open(newListPath(chosen.kind));
  }

  #row(row: OptionListRowResponseModel): TemplateResult {
    const kind = isKind(row.kind) ? row.kind : 'unknown';
    return html`<tr>
      <td>
        <span class="name">
          <uui-icon name="icon-bulleted-list" aria-hidden="true"></uui-icon>
          <a href=${listPath(row.key)}>${row.name}</a>
        </span>
      </td>
      <td class="kind">${this.#term(`kind_${kind}`)}</td>
      <td class=${row.usedBy === 0 ? 'end muted' : 'end'}>${this.#term('usedBy', row.usedBy)}</td>
    </tr>`;
  }

  #body(): TemplateResult {
    const rows = this._rows;
    if (!this._worded) return html`<uui-box class="box--flush">${skeletonLines('')}</uui-box>`;
    switch (rows.state) {
      case 'loading':
        return html`<uui-box class="box--flush">${skeletonLines(this.#term('loading'))}</uui-box>`;
      case 'forbidden':
        return html`<uui-box>
          <p role="alert">
            ${this.localize.term('jfbLibrary_missingPermission', this.localize.term('jfbLibrary_permission_manageLibrary'))}
          </p>
        </uui-box>`;
      case 'failed':
        return html`<uui-box>
          <div class="failed" role="alert">
            <span>${this.#term('failed')}</span>
            <uui-button look="outline" label=${this.#term('retry')} @click=${() => void this.#load()}></uui-button>
          </div>
        </uui-box>`;
      case 'ready':
        if (rows.rows.length === 0) return html`<p class="empty">${this.#term('empty')}</p>`;
        return html`<uui-box class="box--flush">
          <table>
            <thead>
              <tr>
                <th scope="col">${this.#term('columnName')}</th>
                <th scope="col" class="kind">${this.#term('columnKind')}</th>
                <th scope="col" class="end">${this.#term('columnUsedBy')}</th>
              </tr>
            </thead>
            <tbody>
              ${rows.rows.map((row) => this.#row(row))}
            </tbody>
          </table>
          ${
            rows.rows.length < rows.total
              ? html`<div class="more">
                  <uui-button
                    look="secondary"
                    label=${this.#term('loadMore')}
                    .state=${this._more ? 'waiting' : undefined}
                    @click=${() => void this.#loadMore()}
                  ></uui-button>
                </div>`
              : nothing
          }
        </uui-box>`;
    }
  }

  override render() {
    const creatable = this._worded && this._rows.state === 'ready';
    return html`<umb-body-layout>
      ${this.#body()}
      ${
        creatable
          ? html`<uui-button
              slot="actions"
              look="primary"
              color="positive"
              label=${this.#term('create')}
              @click=${() => void this.#create()}
            ></uui-button>`
          : nothing
      }
    </umb-body-layout>`;
  }
}

if (!customElements.get('jfb-option-lists-screen'))
  customElements.define('jfb-option-lists-screen', OptionListsScreenElement);

export { OptionListsScreenElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-option-lists-screen': OptionListsScreenElement;
  }
}
