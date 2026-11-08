// A library kind's list screen (ROADMAP 12.1, IA 7, ADR-0214, ADR-0215): the items by name, each with what its kind adds
// and the forms that use it, a row opening the item. The sidebar names the screen, so it has no title of its own
// (ADR-0021); the footer holds Create and what else the kind offers. With no item, one sentence says what they are for.
import { css, html, nothing, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { jfbBackofficeTokens, jfbVisuallyHidden } from '../../styles/tokens';
import { jfbSkeleton, skeletonLines } from '../../ui/skeleton';
import { itemPath, type LibraryKind } from './library-items';

/** How many items a page holds. */
const PAGE = 100;

/** What every row of a list holds. */
export interface LibraryRow {
  readonly key: string;
  readonly name: string;
  readonly usedBy: number;
}

/** A page of rows; 'forbidden' for a user without the library, undefined without an answer. */
export type RowsAnswer<Row> = { readonly items: Row[]; readonly total: number } | 'forbidden' | undefined;

type Rows<Row> =
  | { readonly state: 'loading' }
  | { readonly state: 'failed' | 'forbidden' }
  | { readonly state: 'ready'; readonly rows: readonly Row[]; readonly total: number };

export abstract class LibraryItemsScreen<Row extends LibraryRow> extends UmbLitElement {
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

      .name > uui-icon,
      .name > uui-tag {
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

      /* What a kind adds gives way first on a narrow workspace: the name and the forms that use it say the most. */
      @container (max-width: 480px) {
        .extra {
          display: none;
        }
      }
    `,
  ];

  declare private _rows: Rows<Row>;
  /** Whether the next page is being read. */
  declare private _more: boolean;
  /** Whether the screen's words are loaded: until then it shows its skeleton, so no key ever shows. */
  declare private _worded: boolean;

  #generation = 0;

  /** The kind whose items the screen lists. */
  protected abstract readonly kind: LibraryKind;

  /** A page of the items. */
  protected abstract readRows(skip: number, take: number): Promise<RowsAnswer<Row>>;

  /** Create, the footer's primary action. */
  protected abstract create(): Promise<void>;

  /** What stands before a row's name, such as the item's swatch; the icon without it. */
  protected lead?(row: Row): TemplateResult;

  /** The icon before each name. */
  protected icon = 'icon-folder';

  /** A mark after a row's name, such as the site's default. */
  protected mark?(row: Row): TemplateResult | typeof nothing;

  /** The heading of the column the kind adds, if any, and each row's cell in it. */
  protected extraHead?(): string;

  protected extraCell?(row: Row): TemplateResult | string;

  /** The footer's actions besides Create. */
  protected footer(): TemplateResult | typeof nothing {
    return nothing;
  }

  constructor() {
    super();
    this._rows = { state: 'loading' };
    this._more = false;
    this._worded = false;
  }

  override connectedCallback() {
    super.connectedCallback();
    void this.kind
      .load(this.localize.lang())
      .catch(() => undefined)
      .then(() => (this._worded = true));
    void this.reload();
  }

  protected term = (key: string, ...args: unknown[]) => this.localize.term(`${this.kind.words}_${key}`, ...args);

  /** Reads the first page again, as after an import. */
  protected async reload() {
    const generation = ++this.#generation;
    if (this._rows.state !== 'ready') this._rows = { state: 'loading' };
    const read = await this.readRows(0, PAGE);
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
    const read = await this.readRows(shown.rows.length, PAGE).finally(() => (this._more = false));
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

  #row(row: Row, extra: boolean): TemplateResult {
    return html`<tr>
      <td>
        <span class="name"
          >${this.lead?.(row) ?? html`<uui-icon name=${this.icon} aria-hidden="true"></uui-icon>`}<a
            href=${itemPath(this.kind, row.key)}
            >${row.name}</a
          >${this.mark?.(row) ?? nothing}</span
        >
      </td>
      ${extra ? html`<td class="extra">${this.extraCell?.(row) ?? ''}</td>` : nothing}
      <td class=${row.usedBy === 0 ? 'end muted' : 'end'}>${this.term('usedBy', row.usedBy)}</td>
    </tr>`;
  }

  #body(): TemplateResult {
    const rows = this._rows;
    if (!this._worded) return html`<uui-box class="box--flush">${skeletonLines('')}</uui-box>`;
    switch (rows.state) {
      case 'loading':
        return html`<uui-box class="box--flush">${skeletonLines(this.term('loading'))}</uui-box>`;
      case 'forbidden':
        return html`<uui-box>
          <p role="alert">
            ${this.localize.term('jfbLibrary_missingPermission', this.localize.term('jfbLibrary_permission_manageLibrary'))}
          </p>
        </uui-box>`;
      case 'failed':
        return html`<uui-box>
          <div class="failed" role="alert">
            <span>${this.term('failed')}</span>
            <uui-button look="outline" label=${this.term('retry')} @click=${() => void this.reload()}></uui-button>
          </div>
        </uui-box>`;
      case 'ready': {
        if (rows.rows.length === 0) return html`<p class="empty">${this.term('empty')}</p>`;
        const extra = this.extraHead?.();
        return html`<uui-box class="box--flush">
          <table>
            <thead>
              <tr>
                <th scope="col">${this.term('columnName')}</th>
                ${extra === undefined ? nothing : html`<th scope="col" class="extra">${extra}</th>`}
                <th scope="col" class="end">${this.term('columnUsedBy')}</th>
              </tr>
            </thead>
            <tbody>
              ${rows.rows.map((row) => this.#row(row, extra !== undefined))}
            </tbody>
          </table>
          ${
            rows.rows.length < rows.total
              ? html`<div class="more">
                  <uui-button
                    look="secondary"
                    label=${this.term('loadMore')}
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
    const ready = this._worded && this._rows.state === 'ready';
    return html`<umb-body-layout>
      ${this.#body()} ${ready ? this.footer() : nothing}
      ${
        ready
          ? html`<uui-button
              slot="actions"
              look="primary"
              color="positive"
              label=${this.term('create')}
              @click=${() => void this.create()}
            ></uui-button>`
          : nothing
      }
    </umb-body-layout>`;
  }
}
