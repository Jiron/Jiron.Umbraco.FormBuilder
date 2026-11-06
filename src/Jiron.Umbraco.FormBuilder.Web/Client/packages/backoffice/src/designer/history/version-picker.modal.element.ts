// Restore an earlier version... (ROADMAP 10.3b, IA 5.6, ADR-0198): the form's earlier versions by date, newest first, the
// published ones and, with Show draft saves, the draft saves too, each a button with its name, the live one's chip and
// when and by whom; the current draft is not among them, as there is nothing to compare it with. Choosing one opens its
// comparison with the current draft on top, with the same two restores as in History; a restore there closes both.
import { css, html, nothing, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import type { FormVersionEntryResponseModel } from '../../api';
import { jfbBackofficeTokens } from '../../styles/tokens';
import { jfbSkeleton, skeletonLines } from '../../ui/skeleton';
import { madeAt, momentText, rowName } from './history-model';
import type { VersionPickerModalData } from './version-picker.modal.token';

export class VersionPickerModalElement extends UmbModalBaseElement<VersionPickerModalData, never> {
  static override properties = {
    ...UmbModalBaseElement.properties,
    _state: { state: true },
    _rows: { state: true },
    _total: { state: true },
    _drafts: { state: true },
    _more: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    jfbSkeleton,
    css`
      .room {
        display: grid;
        gap: var(--uui-size-space-4);
      }

      .lead {
        margin: 0;
      }

      ul {
        display: grid;
        margin: 0;
        padding: 0;
        list-style: none;
        border-top: 1px solid var(--uui-color-divider);
      }

      li {
        border-bottom: 1px solid var(--uui-color-divider);
      }

      .version {
        display: grid;
        gap: 2px;
        inline-size: 100%;
        padding: var(--uui-size-space-3) var(--uui-size-space-2);
        border: 0;
        background: none;
        color: var(--uui-color-text);
        font: inherit;
        text-align: start;
        cursor: pointer;
      }

      @media (hover: hover) {
        .version:hover {
          background: var(--uui-color-surface-emphasis);
        }
      }

      .version:focus-visible {
        outline: 2px solid var(--jfb-bo-color-focus);
        outline-offset: -2px;
      }

      .title {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-2);
        font-weight: 700;
      }

      .draft .title {
        font-weight: 400;
      }

      .made {
        color: var(--jfb-bo-color-text-muted);
        font-size: var(--uui-type-small-size);
      }

      .failed {
        display: flex;
        flex-wrap: wrap;
        align-items: center;
        gap: var(--uui-size-space-4);
      }

      .more {
        justify-self: center;
      }
    `,
  ];

  declare private _state: 'loading' | 'ready' | 'failed';
  declare private _rows: readonly FormVersionEntryResponseModel[];
  declare private _total: number;
  declare private _drafts: boolean;
  declare private _more: boolean;

  /** Counts the reads, so only the latest one's answer is shown. */
  #reads = 0;
  #choosing = false;

  constructor() {
    super();
    this._state = 'loading';
    this._rows = [];
    this._total = 0;
    this._drafts = false;
    this._more = false;
  }

  override connectedCallback() {
    super.connectedCallback();
    void this.#read();
  }

  #term = (key: string, ...args: unknown[]) => this.localize.term(`jfbHistory_${key}`, ...args);
  #full = (key: string, ...args: unknown[]) => this.localize.term(key, ...args);

  /** The versions a list shows: every one but the current draft. */
  #earlier = (rows: readonly FormVersionEntryResponseModel[]) => rows.filter((row) => !row.version.isDraft);

  async #read() {
    const read = ++this.#reads;
    this._state = 'loading';
    const page = await this.data?.read(this._drafts, 0).catch(() => undefined);
    if (read !== this.#reads) return;
    if (!page) {
      this._state = 'failed';
      return;
    }
    this._rows = page.items;
    this._total = page.total;
    this._state = 'ready';
  }

  async #showMore() {
    const read = this.#reads;
    const before = this._rows.length;
    const shown = this.#earlier(this._rows).length;
    this._more = true;
    const page = await this.data?.read(this._drafts, before).catch(() => undefined);
    this._more = false;
    if (read !== this.#reads || !page) return;
    const known = new Set(this._rows.map((row) => row.version.number));
    this._rows = [...this._rows, ...page.items.filter((item) => !known.has(item.version.number))];
    this._total = page.total;
    await this.updateComplete;
    // The focus goes to the first new version, where the keyboard reads on, as the button may be gone.
    this.shadowRoot?.querySelectorAll<HTMLElement>('.version')[shown]?.focus();
  }

  async #choose(entry: FormVersionEntryResponseModel) {
    if (this.#choosing) return;
    this.#choosing = true;
    try {
      if (await this.data?.choose(entry)) this._submitModal();
    } finally {
      this.#choosing = false;
    }
  }

  #row(entry: FormVersionEntryResponseModel): TemplateResult {
    const version = entry.version;
    const when = momentText(madeAt(version), new Date(), this.localize.lang(), this.#full);
    return html`<li class=${version.kind === 'draft' ? 'draft' : ''}>
      <button type="button" class="version" data-number=${version.number} @click=${() => void this.#choose(entry)}>
        <span class="title">
          ${rowName(version, this.#full)}
          ${
            version.isLive
              ? html`<uui-tag color="positive" look="primary">${this.#term('published')}</uui-tag>`
              : nothing
          }
        </span>
        <span class="made">${entry.by ? this.#term('whenBy', when, entry.by) : when}</span>
      </button>
    </li>`;
  }

  #list(): TemplateResult {
    if (this._state === 'loading') return skeletonLines(this.#term('loading'), [70, 64, 58]);
    if (this._state === 'failed')
      return html`<div class="failed" role="alert">
        <span>${this.#term('failed')}</span>
        <uui-button look="outline" label=${this.#term('retry')} @click=${() => void this.#read()}></uui-button>
      </div>`;
    const rows = this.#earlier(this._rows);
    if (rows.length === 0) return html`<p role="status">${this.#term('pick_empty')}</p>`;
    return html`<ul>
        ${rows.map((row) => this.#row(row))}
      </ul>
      ${
        this._rows.length < this._total
          ? html`<uui-button
              class="more"
              look="secondary"
              label=${this.#term('showMore')}
              .state=${this._more ? 'waiting' : undefined}
              @click=${() => void this.#showMore()}
            ></uui-button>`
          : nothing
      }`;
  }

  override render() {
    return html`<umb-body-layout headline=${this.#term('pick_headline')}>
      <div class="room">
        <p class="lead">${this.#term('pick_lead')}</p>
        <uui-toggle
          label=${this.#term('showDrafts')}
          .checked=${this._drafts}
          @change=${(event: Event) => {
            this._drafts = (event.target as HTMLInputElement).checked;
            void this.#read();
          }}
        ></uui-toggle>
        ${this.#list()}
      </div>
      <uui-button slot="actions" label=${this.#term('compare_close')} @click=${this._rejectModal}></uui-button>
    </umb-body-layout>`;
  }
}

if (!customElements.get('jfb-version-picker-modal'))
  customElements.define('jfb-version-picker-modal', VersionPickerModalElement);

export { VersionPickerModalElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-version-picker-modal': VersionPickerModalElement;
  }
}
