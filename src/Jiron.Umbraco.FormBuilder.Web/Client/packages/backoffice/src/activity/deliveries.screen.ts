// Activity > Deliveries (ROADMAP 12.4b, IA 9, ADR-0219): every email and webhook run, problems first (failed, trying
// again, uncertain, limited), then the newest; its state, whom it went to, what it is, its form and when. The search finds
// recipients and forms; the filters narrow by state, kind and time, the last 30 days at first. On a narrow workspace
// what a delivery is goes under whom it went to, and its form gives way. A row opens its details,
// where a failed or uncertain one offers Retry. While the host pauses sending, one line above the list says so. The
// sidebar names the screen, so it has no title of its own (ADR-0021).
import { css, html, nothing, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { umbOpenModal } from '@umbraco-cms/backoffice/modal';
import type { DeliveryResponseModel } from '../api';
import { controlStyles } from '../designer/inspector/control.styles';
import { jfbBackofficeTokens, jfbVisuallyHidden } from '../styles/tokens';
import { jfbSkeleton, skeletonLines } from '../ui/skeleton';
import { readDeliveries, type DeliveryFilters } from './activity-api';
import { loadDeliveryWords } from './activity-words';
import { DELIVERY_MODAL } from './delivery.modal.token';
import { isProblem, toneOf, toOf, whatOf, whenOfDelivery } from './delivery-words';
import { configKey } from '../ui/config-key';

/** How many deliveries a page holds. */
const PAGE = 50;

/** The host key that pauses sending (ADR-0219). */
export const PAUSED_KEY = 'JironFormBuilder:Actions:Paused';

/**
 * As narrow as this or narrower, what a delivery is goes under whom it went to rather than in a column of its own, and
 * its form gives way, so each fact keeps its line beside a long address (760 px; the Phase 12 review).
 */
const STACKS = 760;

/** The spans the When filter offers, in days; 0 is all time. */
const SPANS = [7, 30, 90, 0] as const;

type List =
  | { readonly state: 'loading' }
  | { readonly state: 'failed' | 'forbidden' | 'tooLong' }
  | {
      readonly state: 'ready';
      readonly items: readonly DeliveryResponseModel[];
      readonly total: number;
      readonly paused: boolean;
    };

const FIRST_FILTERS: DeliveryFilters = { states: '', kind: '', days: 30, search: '' };

export class DeliveriesScreenElement extends UmbLitElement {
  static override properties = {
    _list: { state: true },
    _worded: { state: true },
    _more: { state: true },
    _filters: { state: true },
    _stacked: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    jfbVisuallyHidden,
    jfbSkeleton,
    controlStyles,
    css`
      :host {
        display: block;
        block-size: 100%;
        container-type: inline-size;
      }

      .page {
        display: grid;
        grid-template-columns: minmax(0, 1fr);
        gap: var(--uui-size-space-5);
      }

      /* A note as the publish dialog draws one: a quiet frame, its weight said by the icon beside its words. */
      .paused {
        display: flex;
        align-items: flex-start;
        gap: var(--uui-size-space-3);
        margin: 0;
        padding: var(--uui-size-space-4) var(--uui-size-space-5);
        border: 1px solid var(--jfb-bo-color-control-border);
        border-radius: var(--uui-border-radius);
        background: var(--uui-color-surface);
        overflow-wrap: anywhere;
      }

      .paused uui-icon {
        flex: none;
        margin-block-start: 2px;
        font-size: 16px;
        color: var(--jfb-bo-color-warning-text);
      }

      .paused code {
        font-size: 0.95em;
      }

      .tools {
        display: grid;
        grid-template-columns: minmax(12rem, 2fr) repeat(3, minmax(9rem, 1fr));
        gap: var(--uui-size-space-3) var(--uui-size-space-4);
        align-items: end;
      }

      .tool {
        display: grid;
        gap: var(--uui-size-space-1);
      }

      .tool label {
        font-weight: 600;
        font-size: var(--uui-type-small-size);
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
        padding: var(--uui-size-space-3) var(--uui-size-space-5);
        border-top: 1px solid var(--uui-color-divider);
        vertical-align: middle;
      }

      tbody tr:hover {
        background: var(--uui-color-surface-emphasis);
      }

      .open {
        padding: 0;
        border: 0;
        background: none;
        color: var(--uui-color-text);
        font: inherit;
        text-align: start;
        overflow-wrap: anywhere;
        cursor: pointer;
      }

      .open:hover,
      .open:focus-visible {
        color: var(--uui-color-interactive-emphasis);
        text-decoration: underline;
      }

      .open:focus-visible {
        outline: 2px solid var(--jfb-bo-color-focus);
        outline-offset: 2px;
      }

      .end {
        text-align: end;
        white-space: nowrap;
        font-variant-numeric: tabular-nums;
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

      /* Narrow (STACKS), what it is sits under whom it went to. */
      .what-below {
        display: block;
        margin-block-start: var(--uui-size-space-1);
        color: var(--jfb-bo-color-text-muted);
      }

      /* Its form gives way on a narrow workspace: the state, whom it went to, what it is and when say the most. */
      @container (max-width: 760px) {
        .tools {
          grid-template-columns: repeat(3, minmax(0, 1fr));
        }

        .tool--search {
          grid-column: 1 / -1;
        }

        .form {
          display: none;
        }
      }

      @container (max-width: 520px) {
        .tools {
          grid-template-columns: minmax(0, 1fr);
        }
      }
    `,
  ];

  declare private _list: List;
  declare private _worded: boolean;
  declare private _more: boolean;
  declare private _filters: DeliveryFilters;
  /** Whether the screen is too narrow for a column of what each delivery is, so it goes under whom it went to. */
  declare private _stacked: boolean;

  #generation = 0;
  #typing: ReturnType<typeof setTimeout> | undefined;
  #resize = new ResizeObserver(([entry]) => {
    const width = entry?.contentRect.width;
    const stacked = width !== undefined && width <= STACKS;
    if (stacked !== this._stacked) this._stacked = stacked;
  });

  constructor() {
    super();
    this._list = { state: 'loading' };
    this._worded = false;
    this._more = false;
    this._filters = FIRST_FILTERS;
    this._stacked = false;
  }

  override connectedCallback() {
    super.connectedCallback();
    void loadDeliveryWords(this.localize.lang())
      .catch(() => undefined)
      .then(() => (this._worded = true));
    void this.#read();
    this.#resize.observe(this);
  }

  override disconnectedCallback() {
    super.disconnectedCallback();
    clearTimeout(this.#typing);
    this.#resize.disconnect();
  }

  #term = (key: string, ...args: unknown[]) => this.localize.term(`jfbActivity_${key}`, ...args);

  #typeName = (name: string) => this.localize.string(name);

  async #read() {
    const generation = ++this.#generation;
    if (this._list.state !== 'ready') this._list = { state: 'loading' };
    const read = await readDeliveries(this, this._filters, 0, PAGE);
    if (generation !== this.#generation) return;
    this._list =
      read === undefined || typeof read === 'string'
        ? { state: read ?? 'failed' }
        : { state: 'ready', items: read.items, total: read.total, paused: read.paused };
  }

  async #loadMore() {
    const shown = this._list;
    if (shown.state !== 'ready' || this._more) return;
    const generation = this.#generation;
    this._more = true;
    const read = await readDeliveries(this, this._filters, shown.items.length, PAGE).finally(
      () => (this._more = false),
    );
    if (generation !== this.#generation || read === undefined || typeof read === 'string') return;
    const known = new Set(shown.items.map((item) => `${item.kind}:${item.id}`));
    this._list = {
      ...shown,
      items: [...shown.items, ...read.items.filter((item) => !known.has(`${item.kind}:${item.id}`))],
      total: read.total,
    };
    await this.updateComplete;
    this.shadowRoot
      ?.querySelectorAll<HTMLElement>('tbody tr')
      [shown.items.length]?.querySelector<HTMLElement>('button')
      ?.focus();
  }

  #filter(change: Partial<DeliveryFilters>) {
    this._filters = { ...this._filters, ...change };
    void this.#read();
  }

  #search(text: string) {
    clearTimeout(this.#typing);
    this.#typing = setTimeout(() => this.#filter({ search: text }), 300);
  }

  async #open(item: DeliveryResponseModel) {
    const value = await umbOpenModal(this, DELIVERY_MODAL, {
      data: { kind: item.kind === 'email' ? 'email' : 'run', id: item.id },
    }).catch(() => undefined);
    if (value?.retried) await this.#read();
  }

  #tools(): TemplateResult {
    const filters = this._filters;
    const select = (
      id: string,
      label: string,
      value: string,
      options: readonly (readonly [string, string])[],
      change: (value: string) => void,
    ) =>
      html`<div class="tool">
        <label for=${id}>${label}</label>
        <select id=${id} .value=${value} @change=${(event: Event) => change((event.target as HTMLSelectElement).value)}>
          ${options.map(([key, text]) => html`<option value=${key} ?selected=${key === value}>${text}</option>`)}
        </select>
      </div>`;
    return html`<div class="tools">
      <div class="tool tool--search">
        <label for="search">${this.#term('deliveriesSearch')}</label>
        <input
          id="search"
          type="search"
          .value=${filters.search}
          @input=${(event: Event) => this.#search((event.target as HTMLInputElement).value)}
        />
      </div>
      ${select(
        'states',
        this.#term('filterState'),
        filters.states,
        (['', 'problems', 'waiting', 'delivered', 'skipped'] as const).map((key) => [
          key,
          this.#term(`states_${key || 'all'}`),
        ]),
        (value) => this.#filter({ states: value as DeliveryFilters['states'] }),
      )}
      ${select(
        'kind',
        this.#term('filterKind'),
        filters.kind,
        (['', 'email', 'webhook', 'other'] as const).map((key) => [key, this.#term(`kind_${key || 'all'}`)]),
        (value) => this.#filter({ kind: value as DeliveryFilters['kind'] }),
      )}
      ${select(
        'when',
        this.#term('filterWhen'),
        String(filters.days),
        SPANS.map((days) => [String(days), this.#term(`when_${days || 'all'}`)]),
        (value) => this.#filter({ days: Number(value) }),
      )}
    </div>`;
  }

  #row(item: DeliveryResponseModel, now: Date): TemplateResult {
    const culture = this.localize.lang();
    const to = toOf(item, this.#term) || this.#term('gone_other');
    const what = whatOf(item, this.#term, this.#typeName);
    return html`<tr>
      <td>
        <uui-tag look=${isProblem(item) ? 'primary' : 'secondary'} color=${toneOf(item.state)}
          >${this.#term(`state_${item.state}`)}</uui-tag
        >
      </td>
      <td>
        <button type="button" class="open" aria-haspopup="dialog" @click=${() => void this.#open(item)}>${to}</button>
        ${this._stacked ? html`<span class="what-below">${what}</span>` : nothing}
      </td>
      ${this._stacked ? nothing : html`<td class="what">${what}</td>`}
      <td class="form">${item.formName ?? html`<span class="muted">${this.#term('gone_other')}</span>`}</td>
      <td class="end muted">${whenOfDelivery(item, now, culture, this.#term)}</td>
    </tr>`;
  }

  #body(): TemplateResult {
    const list = this._list;
    switch (list.state) {
      case 'loading':
        return html`<uui-box class="box--flush">${skeletonLines(this.#term('deliveriesLoading'))}</uui-box>`;
      case 'forbidden':
        return html`<uui-box>
          <p role="alert">
            ${this.localize.term('jfbLibrary_missingPermission', this.localize.term('jfbLibrary_permission_manageSettings'))}
          </p>
        </uui-box>`;
      case 'failed':
      case 'tooLong':
        return html`<uui-box>
          <div class="failed" role="alert">
            <span>${this.#term(list.state === 'tooLong' ? 'tooLong' : 'deliveriesFailed')}</span>
            <uui-button look="outline" label=${this.#term('retry')} @click=${() => void this.#read()}></uui-button>
          </div>
        </uui-box>`;
      case 'ready': {
        const filtered = this._filters !== FIRST_FILTERS;
        if (list.items.length === 0) {
          return html`<p class="empty">${this.#term(filtered ? 'deliveriesNoMatch' : 'deliveriesEmpty')}</p>`;
        }
        const now = new Date();
        return html`<uui-box class="box--flush">
          <table>
            <caption class="visually-hidden">
              ${this.#term('deliveriesCaption')}
            </caption>
            <thead>
              <tr>
                <th scope="col">${this.#term('columnState')}</th>
                <th scope="col">${this.#term('columnTo')}</th>
                ${this._stacked ? nothing : html`<th scope="col" class="what">${this.#term('columnWhat')}</th>`}
                <th scope="col" class="form">${this.#term('columnForm')}</th>
                <th scope="col" class="end">${this.#term('columnWhen')}</th>
              </tr>
            </thead>
            <tbody>
              ${list.items.map((item) => this.#row(item, now))}
            </tbody>
          </table>
          ${
            list.items.length < list.total
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
  }

  override render() {
    if (!this._worded) {
      return html`<umb-body-layout><uui-box class="box--flush">${skeletonLines('')}</uui-box></umb-body-layout>`;
    }
    const list = this._list;
    const paused = list.state === 'ready' && list.paused;
    return html`<umb-body-layout>
      <div class="page">
        ${
          paused
            ? html`<p class="paused" role="status">
                <uui-icon name="icon-alert" aria-hidden="true"></uui-icon><span>${this.#pausedLine()}</span>
              </p>`
            : nothing
        }
        ${list.state === 'forbidden' ? nothing : this.#tools()} ${this.#body()}
      </div>
    </umb-body-layout>`;
  }

  // The key stands as code within the sentence, so it reads as the setting it is.
  #pausedLine(): TemplateResult {
    const marker = '[[key]]';
    const [before, after] = this.#term('paused', marker).split(marker);
    return html`${before}${configKey(PAUSED_KEY)}${after}`;
  }
}

if (!customElements.get('jfb-deliveries-screen'))
  customElements.define('jfb-deliveries-screen', DeliveriesScreenElement);

export { DeliveriesScreenElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-deliveries-screen': DeliveriesScreenElement;
  }
}
