// The Recycle Bin's screen (docs/ux/information-architecture.md, section 3; ADR-0166): the trashed forms and folders the user sees,
// with when and by whom each went there, each with its "..." (Restore..., Delete...). The footer's one action is Empty recycle
// bin...; an empty bin says so in one sentence and has no footer, since there is nothing to empty.
import { css, html, nothing } from '@umbraco-cms/backoffice/external/lit';
import { UMB_ACTION_EVENT_CONTEXT } from '@umbraco-cms/backoffice/action';
import { UmbRequestReloadChildrenOfEntityEvent } from '@umbraco-cms/backoffice/entity-action';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import { libraryListTrash, type LibraryRowResponseModel } from '../api';
import { TRASHED_FOLDER_ENTITY_TYPE, TRASHED_FORM_ENTITY_TYPE } from '../section/entity-types';
import { jfbBackofficeTokens, jfbVisuallyHidden } from '../styles/tokens';
import { emptyBin } from './actions/trash-actions';
import { changedText } from './rows';
import { ListStore } from './stores';
import './entity-menu.element';

export class RecycleBinElement extends UmbLitElement {
  static override properties = {
    unique: { attribute: false },
    entityType: { attribute: false },
  };

  static override styles = [
    jfbBackofficeTokens,
    jfbVisuallyHidden,
    css`
      :host {
        display: block;
        height: 100%;
      }

      /* A screen's title: Umbraco's sidebars that open over it head themselves with an h3. */
      h2 {
        margin: 0;
        font-size: var(--jfb-bo-type-display);
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

      .item {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-3);
        overflow-wrap: anywhere;
      }

      .menu {
        width: 1%;
        text-align: end;
      }

      .muted {
        color: var(--jfb-bo-color-text-muted);
      }

      .empty {
        max-width: 36rem;
        margin: var(--uui-size-layout-2) auto;
        text-align: center;
      }

      .skeleton {
        display: block;
        height: 12px;
        margin: var(--uui-size-space-5);
        border-radius: var(--uui-border-radius);
        background: var(--uui-color-surface-alt);
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
    `,
  ];

  declare unique: string | null;
  declare entityType: string;

  readonly #list = new ListStore(
    (skip, take) => tryExecute(this, libraryListTrash({ query: { skip, take } }), { disableNotifications: true }),
    () => this.requestUpdate(),
  );

  #events: EventTarget | undefined;
  readonly #onChange = () => void this.#list.load();

  constructor() {
    super();
    this.consumeContext(UMB_ACTION_EVENT_CONTEXT, (events) => {
      this.#events?.removeEventListener(UmbRequestReloadChildrenOfEntityEvent.TYPE, this.#onChange);
      this.#events = events;
      events?.addEventListener(UmbRequestReloadChildrenOfEntityEvent.TYPE, this.#onChange);
    });
  }

  override connectedCallback() {
    super.connectedCallback();
    void this.#list.load();
  }

  override disconnectedCallback() {
    super.disconnectedCallback();
    this.#events?.removeEventListener(UmbRequestReloadChildrenOfEntityEvent.TYPE, this.#onChange);
  }

  #row(row: LibraryRowResponseModel) {
    const item = row.item;
    const isFolder = item.kind === 'folder';
    const trashed = changedText(row, new Date(), this.localize.lang() || 'en', {
      today: this.localize.term('jfbLibrary_today'),
      yesterday: this.localize.term('jfbLibrary_yesterday'),
      by: (day, name) => this.localize.term('jfbLibrary_dayBy', day, name),
    });
    return html`<tr>
      <td>
        <span class="item"><uui-icon name=${isFolder ? 'icon-folder' : 'icon-document'}></uui-icon>${item.name}</span>
      </td>
      <td class="muted">${trashed}</td>
      <td class="menu">
        <jfb-entity-menu
          entity-type=${isFolder ? TRASHED_FOLDER_ENTITY_TYPE : TRASHED_FORM_ENTITY_TYPE}
          .unique=${item.key}
          label=${this.localize.term('jfbLibrary_moreActionsFor', item.name)}
        ></jfb-entity-menu>
      </td>
    </tr>`;
  }

  #body() {
    const rows = this.#list.rows;
    if (rows.state === 'loading')
      return html`<uui-box class="box--flush" aria-busy="true">
        <span class="skeleton" style="width: 52%"></span>
        <span class="skeleton" style="width: 33%"></span>
        <span class="visually-hidden">${this.localize.term('jfbLibrary_loading')}</span>
      </uui-box>`;
    if (rows.state === 'failed')
      return html`<uui-box>
        <div class="failed" role="alert">
          <span>${this.localize.term('jfbLibrary_binFailed')}</span>
          <uui-button
            look="outline"
            label=${this.localize.term('jfbLibrary_tryAgain')}
            @click=${() => void this.#list.load()}
          ></uui-button>
        </div>
      </uui-box>`;
    if (rows.value.length === 0) return html`<p class="empty">${this.localize.term('jfbLibrary_binEmpty')}</p>`;
    return html`<uui-box class="box--flush">
      <table>
        <thead>
          <tr>
            <th scope="col">${this.localize.term('jfbLibrary_columnName')}</th>
            <th scope="col">${this.localize.term('jfbLibrary_columnTrashed')}</th>
            <th scope="col"><span class="visually-hidden">${this.localize.term('jfbLibrary_columnActions')}</span></th>
          </tr>
        </thead>
        <tbody>
          ${rows.value.map((row) => this.#row(row))}
        </tbody>
      </table>
      ${
        this.#list.hasMore
          ? html`<div class="more">
              <uui-button
                look="secondary"
                label=${this.localize.term('jfbLibrary_loadMore')}
                @click=${() => void this.#list.more()}
              ></uui-button>
            </div>`
          : nothing
      }
    </uui-box>`;
  }

  override render() {
    const filled = this.#list.rows.state === 'ready' && this.#list.rows.value.length > 0;
    return html`<umb-body-layout>
      <h2 slot="header">${this.localize.term('jfbLibrary_recycleBin')}</h2>
      ${this.#body()}
      ${
        filled
          ? html`<uui-button
              slot="actions"
              look="primary"
              color="danger"
              label=${this.localize.term('jfbLibrary_emptyBin')}
              @click=${() => void emptyBin(this).catch(() => undefined)}
            ></uui-button>`
          : nothing
      }
    </umb-body-layout>`;
  }
}

if (!customElements.get('jfb-recycle-bin')) customElements.define('jfb-recycle-bin', RecycleBinElement);

export { RecycleBinElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-recycle-bin': RecycleBinElement;
  }
}
