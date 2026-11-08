// Activity > Audit log (ROADMAP 12.4b, IA 9, ADR-0041, ADR-0219): who did what, when, on which form or setting, the newest
// first. A screen for reading, so it has no primary action; Export writes the filtered log as CSV. The search finds people
// and items; the filters narrow by what and when, the last 30 days at first, and by one person, chosen from an action's
// details and shown as a chip until cleared. A row opens its details. The sidebar names the screen (ADR-0021).
import { css, html, nothing, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { umbOpenModal } from '@umbraco-cms/backoffice/modal';
import { UMB_NOTIFICATION_CONTEXT } from '@umbraco-cms/backoffice/notification';
import type { AuditEntryResponseModel } from '../api';
import { controlStyles } from '../designer/inspector/control.styles';
import { saveBlob } from '../library/actions/export-form.action';
import { jfbBackofficeTokens, jfbVisuallyHidden } from '../styles/tokens';
import { jfbSkeleton, skeletonLines } from '../ui/skeleton';
import { exportAudit, readAudit, type AuditFilters } from './activity-api';
import { dateOf, loadAuditWords, whenOf } from './activity-words';
import { AUDIT_ENTRY_MODAL } from './audit-entry.modal.token';
import { factsOf, sentenceOf, whereOf, whoOf, type AuditNames } from './audit-words';

/** How many actions a page holds. */
const PAGE = 50;

/** The spans the When filter offers, in days; 0 is all time. */
const SPANS = [1, 7, 30, 90, 0] as const;

type List =
  | { readonly state: 'loading' }
  | { readonly state: 'failed' | 'forbidden' | 'tooLong' }
  | {
      readonly state: 'ready';
      readonly items: readonly AuditEntryResponseModel[];
      readonly total: number;
      readonly names: AuditNames;
    };

const FIRST_FILTERS: AuditFilters = { what: '', user: '', days: 30, search: '' };

export class AuditScreenElement extends UmbLitElement {
  static override properties = {
    _list: { state: true },
    _worded: { state: true },
    _more: { state: true },
    _filters: { state: true },
    _userName: { state: true },
    _exporting: { state: true },
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

      .tools {
        display: grid;
        grid-template-columns: minmax(12rem, 2fr) repeat(2, minmax(9rem, 1fr)) auto;
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

      .export {
        justify-self: end;
      }

      .chips {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-3);
        margin: 0;
      }

      .chip {
        display: inline-flex;
        align-items: center;
        gap: var(--uui-size-space-2);
        padding: var(--uui-size-space-1) var(--uui-size-space-3);
        border: 1px solid var(--uui-color-border);
        border-radius: 999px;
        background: var(--uui-color-surface);
        color: var(--uui-color-text);
        font: inherit;
        cursor: pointer;
      }

      .chip:hover,
      .chip:focus-visible {
        border-color: var(--uui-color-border-emphasis);
      }

      .chip:focus-visible {
        outline: 2px solid var(--jfb-bo-color-focus);
        outline-offset: 2px;
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

      .when {
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

      /* Where gives way first on a narrow workspace, then who: when and what say the most. */
      @container (max-width: 760px) {
        .tools {
          grid-template-columns: repeat(2, minmax(0, 1fr));
        }

        .tool--search,
        .export {
          grid-column: 1 / -1;
        }

        .where {
          display: none;
        }
      }

      @container (max-width: 520px) {
        .tools {
          grid-template-columns: minmax(0, 1fr);
        }

        .who {
          display: none;
        }
      }
    `,
  ];

  declare private _list: List;
  declare private _worded: boolean;
  declare private _more: boolean;
  declare private _filters: AuditFilters;
  /** The name of the one person the log shows, while it shows one. */
  declare private _userName: string;
  declare private _exporting: boolean;

  #generation = 0;
  #typing: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    super();
    this._list = { state: 'loading' };
    this._worded = false;
    this._more = false;
    this._filters = FIRST_FILTERS;
    this._userName = '';
    this._exporting = false;
  }

  override connectedCallback() {
    super.connectedCallback();
    void loadAuditWords(this.localize.lang())
      .catch(() => undefined)
      .then(() => (this._worded = true));
    void this.#read();
  }

  override disconnectedCallback() {
    super.disconnectedCallback();
    clearTimeout(this.#typing);
  }

  #term = (key: string, ...args: unknown[]) => this.localize.term(`jfbActivity_${key}`, ...args);

  #section = (key: string) => this.localize.term(`jfbSection_${key}`);

  /** A setting's name from the global settings' words, or '' for one they have none for. */
  #setting = (section: string, field: string) => {
    const key = `jfbGlobalSettings_${section}_${field}`;
    const said = this.localize.term(key);
    return said === key ? '' : said;
  };

  async #read() {
    const generation = ++this.#generation;
    if (this._list.state !== 'ready') this._list = { state: 'loading' };
    const read = await readAudit(this, this._filters, 0, PAGE);
    if (generation !== this.#generation) return;
    this._list =
      read === undefined || typeof read === 'string'
        ? { state: read ?? 'failed' }
        : { state: 'ready', items: read.items, total: read.total, names: read.names };
  }

  async #loadMore() {
    const shown = this._list;
    if (shown.state !== 'ready' || this._more) return;
    const generation = this.#generation;
    this._more = true;
    const read = await readAudit(this, this._filters, shown.items.length, PAGE).finally(() => (this._more = false));
    if (generation !== this.#generation || read === undefined || typeof read === 'string') return;
    const known = new Set(shown.items.map((item) => item.id));
    this._list = {
      state: 'ready',
      items: [...shown.items, ...read.items.filter((item) => !known.has(item.id))],
      total: read.total,
      names: { ...shown.names, ...read.names },
    };
    await this.updateComplete;
    this.shadowRoot
      ?.querySelectorAll<HTMLElement>('tbody tr')
      [shown.items.length]?.querySelector<HTMLElement>('button')
      ?.focus();
  }

  #filter(change: Partial<AuditFilters>) {
    this._filters = { ...this._filters, ...change };
    void this.#read();
  }

  #search(text: string) {
    clearTimeout(this.#typing);
    this.#typing = setTimeout(() => this.#filter({ search: text }), 300);
  }

  async #open(entry: AuditEntryResponseModel, names: AuditNames) {
    const value = await umbOpenModal(this, AUDIT_ENTRY_MODAL, { data: { entry, names } }).catch(() => undefined);
    if (value?.user) {
      this._userName = value.user.name;
      this.#filter({ user: value.user.key });
    }
  }

  #clearUser() {
    this._userName = '';
    this.#filter({ user: '' });
    this.shadowRoot?.querySelector<HTMLElement>('#search')?.focus();
  }

  async #export() {
    if (this._exporting) return;
    this._exporting = true;
    const file = await exportAudit(this, this._filters).finally(() => (this._exporting = false));
    if (file) {
      saveBlob(`audit-log-${new Date().toISOString().slice(0, 10)}.csv`, file);
      return;
    }
    const notifications = await this.getContext(UMB_NOTIFICATION_CONTEXT);
    notifications?.peek('danger', { data: { message: this.#term('exportFailed') } });
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
        <label for="search">${this.#term('auditSearch')}</label>
        <input
          id="search"
          type="search"
          .value=${filters.search}
          @input=${(event: Event) => this.#search((event.target as HTMLInputElement).value)}
        />
      </div>
      ${select(
        'what',
        this.#term('filterWhat'),
        filters.what,
        (['', 'forms', 'entries', 'library', 'settings'] as const).map((key) => [
          key,
          this.#term(`what_${key || 'all'}`),
        ]),
        (value) => this.#filter({ what: value as AuditFilters['what'] }),
      )}
      ${select(
        'when',
        this.#term('filterWhen'),
        String(filters.days),
        SPANS.map((days) => [String(days), this.#term(`when_${days || 'all'}`)]),
        (value) => this.#filter({ days: Number(value) }),
      )}
      <uui-button
        class="export"
        look="outline"
        label=${this.#term('export')}
        .state=${this._exporting ? 'waiting' : undefined}
        ?disabled=${this._list.state !== 'ready' || this._list.total === 0}
        @click=${() => void this.#export()}
      ></uui-button>
    </div>`;
  }

  #chips(): TemplateResult | typeof nothing {
    if (!this._filters.user) return nothing;
    return html`<p class="chips">
      <button
        type="button"
        class="chip"
        aria-label=${this.#term('whoClear', this._userName)}
        @click=${() => this.#clearUser()}
      >
        ${this.#term('chipWho', this._userName)}<uui-icon name="icon-wrong" aria-hidden="true"></uui-icon>
      </button>
    </p>`;
  }

  #row(entry: AuditEntryResponseModel, names: AuditNames, now: Date): TemplateResult {
    const at = dateOf(entry.occurredUtc);
    const facts = factsOf(entry, names, this.#term, this.#setting);
    return html`<tr>
      <td class="when muted">${at ? whenOf(at, now, this.localize.lang()) : ''}</td>
      <td class="who">${whoOf(entry, this.#term)}</td>
      <td>
        <button type="button" class="open" aria-haspopup="dialog" @click=${() => void this.#open(entry, names)}>
          ${sentenceOf(entry, facts, this.#term)}
        </button>
      </td>
      <td class="where">${whereOf(entry, names, this.#term, this.#section)}</td>
    </tr>`;
  }

  #body(): TemplateResult {
    const list = this._list;
    switch (list.state) {
      case 'loading':
        return html`<uui-box class="box--flush">${skeletonLines(this.#term('auditLoading'))}</uui-box>`;
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
            <span>${this.#term(list.state === 'tooLong' ? 'tooLong' : 'auditFailed')}</span>
            <uui-button look="outline" label=${this.#term('retry')} @click=${() => void this.#read()}></uui-button>
          </div>
        </uui-box>`;
      case 'ready': {
        const filtered = this._filters !== FIRST_FILTERS;
        if (list.items.length === 0) {
          return html`<p class="empty">${this.#term(filtered ? 'auditNoMatch' : 'auditEmpty')}</p>`;
        }
        const now = new Date();
        return html`<uui-box class="box--flush">
          <table>
            <caption class="visually-hidden">
              ${this.#term('auditCaption')}
            </caption>
            <thead>
              <tr>
                <th scope="col">${this.#term('columnWhen')}</th>
                <th scope="col" class="who">${this.#term('columnWho')}</th>
                <th scope="col">${this.#term('columnWhat')}</th>
                <th scope="col" class="where">${this.#term('columnWhere')}</th>
              </tr>
            </thead>
            <tbody>
              ${list.items.map((entry) => this.#row(entry, list.names, now))}
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
    return html`<umb-body-layout>
      <div class="page">
        ${this._list.state === 'forbidden' ? nothing : html`${this.#tools()}${this.#chips()}`} ${this.#body()}
      </div>
    </umb-body-layout>`;
  }
}

if (!customElements.get('jfb-audit-screen')) customElements.define('jfb-audit-screen', AuditScreenElement);

export { AuditScreenElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-audit-screen': AuditScreenElement;
  }
}
