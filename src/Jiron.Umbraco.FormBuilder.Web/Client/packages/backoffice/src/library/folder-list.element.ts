// A folder's screen, and the top of Forms (docs/ux/information-architecture.md, section 3; ADR-0166): its folders and forms with
// their entries of the last 30 days and when each last changed. Their states show in the tree's marks, not again here. A
// folder's name in the header is editable, which is its rename, beside the tree's "..." without Rename...; the footer's one
// action is Create form, in this folder.
import { css, html, nothing, type PropertyValues } from '@umbraco-cms/backoffice/external/lit';
import { UMB_ACTION_EVENT_CONTEXT } from '@umbraco-cms/backoffice/action';
import { UmbRequestReloadChildrenOfEntityEvent } from '@umbraco-cms/backoffice/entity-action';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import { folderGet, libraryListItems, type LibraryRowResponseModel } from '../api';
import { myPermissions } from '../section/my-permissions';
import { FOLDER_ENTITY_TYPE, FORM_ENTITY_TYPE, LIBRARY_ROOT_ENTITY_TYPE, screenPath } from '../section/entity-types';
import { jfbBackofficeTokens, jfbVisuallyHidden } from '../styles/tokens';
import { renameFolder } from './actions/folder-actions';
import { createBlankForm, createFromTemplate } from './actions/new-form';
import { FolderScreenContext } from './folder-screen.context';
import { changedText, entriesText, showsEntries } from './rows';
import { ListStore } from './stores';
import './entity-menu.element';

export class FolderListElement extends UmbLitElement {
  static override properties = {
    unique: { attribute: false },
    entityType: { attribute: false },
    _name: { state: true },
    _canDesign: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    jfbVisuallyHidden,
    css`
      :host {
        display: block;
        height: 100%;
      }

      .header {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-3);
        width: 100%;
        min-width: 0;
      }

      .name {
        flex: 1;
        min-width: 0;
        --uui-input-border-color: transparent;
        --uui-input-background-color: transparent;
        font-size: var(--jfb-bo-type-display);
        font-weight: 700;
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
        padding: var(--uui-size-space-4) var(--uui-size-space-5);
        border-top: 1px solid var(--uui-color-divider);
        vertical-align: middle;
      }

      .end {
        text-align: end;
        white-space: nowrap;
        font-variant-numeric: tabular-nums;
      }

      .item {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-3);
        min-width: 0;
      }

      .item a {
        color: var(--uui-color-text);
        text-decoration: none;
        overflow-wrap: anywhere;
      }

      .item a:hover,
      .item a:focus-visible {
        color: var(--uui-color-interactive-emphasis);
        text-decoration: underline;
      }

      .muted {
        color: var(--jfb-bo-color-text-muted);
      }

      .empty {
        display: grid;
        justify-items: center;
        gap: var(--uui-size-space-4);
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

      @media (max-width: 700px) {
        .changed {
          display: none;
        }
      }
    `,
  ];

  declare unique: string | null;
  declare entityType: string;
  declare private _name: string;
  /** Whether the user may create forms here; false until known. */
  declare private _canDesign: boolean;

  readonly #screen = new FolderScreenContext(this);
  readonly #list = new ListStore(
    (skip, take) =>
      tryExecute(
        this,
        libraryListItems({ query: this.unique ? { folderKey: this.unique, skip, take } : { skip, take } }),
        {
          disableNotifications: true,
        },
      ),
    () => {
      this.#screen.setEmpty(this.#list.rows.state === 'ready' && this.#list.rows.value.length === 0);
      this.requestUpdate();
    },
  );

  constructor() {
    super();
    this.unique = null;
    this.entityType = LIBRARY_ROOT_ENTITY_TYPE;
    this._name = '';
    this._canDesign = false;
    this.consumeContext(UMB_ACTION_EVENT_CONTEXT, (events) => {
      this.#events?.removeEventListener(UmbRequestReloadChildrenOfEntityEvent.TYPE, this.#onChange);
      this.#events = events;
      events?.addEventListener(UmbRequestReloadChildrenOfEntityEvent.TYPE, this.#onChange);
    });
  }

  #events: EventTarget | undefined;
  // Any change in the library may touch this list: an item moved in or out, renamed, trashed or restored.
  readonly #onChange = () => void this.#load();

  override disconnectedCallback() {
    super.disconnectedCallback();
    this.#events?.removeEventListener(UmbRequestReloadChildrenOfEntityEvent.TYPE, this.#onChange);
  }

  protected override willUpdate(changed: PropertyValues) {
    if (changed.has('unique')) void this.#load();
  }

  async #load() {
    const key = this.unique;
    void this.#list.load();
    if (key) {
      const { data } = await tryExecute(this, folderGet({ path: { key } }), { disableNotifications: true });
      if (key !== this.unique) return;
      this._name = data?.name ?? '';
      this._canDesign = data?.permissions.includes('design') ?? false;
    } else {
      this._name = this.localize.term('jfbSection_forms');
      this._canDesign = (await myPermissions(this))?.global.includes('design') ?? false;
    }
  }

  async #rename(event: Event) {
    const input = event.target as HTMLInputElement;
    const name = input.value.trim();
    if (!this.unique || !name || name === this._name) {
      input.value = this._name;
      return;
    }

    if (await renameFolder(this, this.unique, name)) this._name = name;
    else input.value = this._name;
  }

  #row(row: LibraryRowResponseModel, withEntries: boolean) {
    const culture = this.localize.lang() || 'en';
    const item = row.item;
    const isFolder = item.kind === 'folder';
    const changed = changedText(row, new Date(), culture, {
      today: this.localize.term('jfbLibrary_today'),
      yesterday: this.localize.term('jfbLibrary_yesterday'),
      by: (day, name) => this.localize.term('jfbLibrary_dayBy', day, name),
    });
    return html`<tr>
      <td>
        <span class="item">
          <uui-icon name=${isFolder ? 'icon-folder' : 'icon-document'}></uui-icon>
          <a href=${screenPath(isFolder ? FOLDER_ENTITY_TYPE : FORM_ENTITY_TYPE, item.key)}>${item.name}</a>
        </span>
      </td>
      ${withEntries ? html`<td class="end">${entriesText(row.recentEntries, culture, this.localize.term('jfbLibrary_none'))}</td>` : nothing}
      <td class="end muted changed">${changed}</td>
    </tr>`;
  }

  #body() {
    const rows = this.#list.rows;
    if (rows.state === 'loading')
      return html`<uui-box class="box--flush" aria-busy="true">
        <span class="skeleton" style="width: 58%"></span>
        <span class="skeleton" style="width: 37%"></span>
        <span class="visually-hidden">${this.localize.term('jfbLibrary_loading')}</span>
      </uui-box>`;
    if (rows.state === 'failed')
      return html`<uui-box>
        <div class="failed" role="alert">
          <span>${this.localize.term('jfbLibrary_listFailed')}</span>
          <uui-button
            look="outline"
            label=${this.localize.term('jfbLibrary_tryAgain')}
            @click=${() => void this.#list.load()}
          ></uui-button>
        </div>
      </uui-box>`;
    if (rows.value.length === 0)
      return html`<div class="empty">
        <p>${this.localize.term(this._canDesign ? 'jfbLibrary_emptyFolder' : 'jfbLibrary_emptyFolderReadOnly')}</p>
        ${
          this._canDesign
            ? html`<uui-button
                look="outline"
                label=${this.localize.term('jfbLibrary_chooseTemplate')}
                @click=${() => void createFromTemplate(this, this.unique).catch(() => undefined)}
              ></uui-button>`
            : nothing
        }
      </div>`;

    const withEntries = showsEntries(rows.value);
    return html`<uui-box class="box--flush">
      <table>
        <thead>
          <tr>
            <th scope="col">${this.localize.term('jfbLibrary_columnName')}</th>
            ${withEntries ? html`<th scope="col" class="end">${this.localize.term('jfbLibrary_columnEntries')}</th>` : nothing}
            <th scope="col" class="end changed">${this.localize.term('jfbLibrary_columnChanged')}</th>
          </tr>
        </thead>
        <tbody>
          ${rows.value.map((row) => this.#row(row, withEntries))}
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

  #header() {
    const more = this.localize.term('jfbLibrary_moreActionsFor', this._name);
    return html`<div class="header" slot="header">
      ${
        this.unique && this._canDesign
          ? html`<uui-input
              class="name"
              label=${this.localize.term('jfbLibrary_folderName')}
              .value=${this._name}
              maxlength="255"
              @change=${this.#rename}
              @keydown=${(event: KeyboardEvent) => {
                if (event.key === 'Enter') (event.target as HTMLElement).blur();
                if (event.key === 'Escape') (event.target as HTMLInputElement).value = this._name;
              }}
            ></uui-input>`
          : html`<h2>${this._name}</h2>`
      }
      <jfb-entity-menu entity-type=${this.entityType} .unique=${this.unique} label=${more}></jfb-entity-menu>
    </div>`;
  }

  override render() {
    return html`<umb-body-layout>
      ${this.#header()} ${this.#body()}
      ${
        this._canDesign
          ? html`<uui-button
              slot="actions"
              look="primary"
              color="positive"
              label=${this.localize.term('jfbLibrary_createForm')}
              @click=${() => void createBlankForm(this, this.unique)}
            ></uui-button>`
          : nothing
      }
    </umb-body-layout>`;
  }
}

if (!customElements.get('jfb-folder-list')) customElements.define('jfb-folder-list', FolderListElement);

export { FolderListElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-folder-list': FolderListElement;
  }
}
