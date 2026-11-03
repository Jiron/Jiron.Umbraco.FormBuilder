// A form or folder in the Recycle Bin, opened from the bin's tree (docs/ux/information-architecture.md, section 3; ADR-0166): its name
// with its "..." (Restore..., Delete...), and one sentence on where it is and since when. Nothing in the bin can be changed.
import { css, html, type PropertyValues } from '@umbraco-cms/backoffice/external/lit';
import { UMB_ACTION_EVENT_CONTEXT } from '@umbraco-cms/backoffice/action';
import { UmbRequestReloadChildrenOfEntityEvent } from '@umbraco-cms/backoffice/entity-action';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import { folderGet, formGet } from '../api';
import { TRASHED_FOLDER_ENTITY_TYPE } from '../section/entity-types';
import { jfbBackofficeTokens } from '../styles/tokens';
import { dayText } from './rows';
import './entity-menu.element';

export class TrashedItemElement extends UmbLitElement {
  static override properties = {
    unique: { attribute: false },
    entityType: { attribute: false },
    _name: { state: true },
    _since: { state: true },
    _gone: { state: true },
    _loaded: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    css`
      :host {
        display: block;
        height: 100%;
      }

      .header {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-3);
        min-width: 0;
      }

      /* A screen's title: Umbraco's sidebars that open over it head themselves with an h3. */
      h2 {
        margin: 0;
        font-size: var(--jfb-bo-type-display);
        overflow-wrap: anywhere;
      }

      p {
        margin: 0;
      }
    `,
  ];

  declare unique: string | null;
  declare entityType: string;
  declare private _name: string;
  declare private _since: string | undefined;
  declare private _gone: boolean;
  /** Whether the item's first answer came: until then the screen says it is loading, and names nothing. */
  declare private _loaded: boolean;

  constructor() {
    super();
    this.unique = null;
    this.entityType = TRASHED_FOLDER_ENTITY_TYPE;
    this._name = '';
    this._gone = false;
    this._loaded = false;
    this.consumeContext(UMB_ACTION_EVENT_CONTEXT, (events) => {
      this.#events?.removeEventListener(UmbRequestReloadChildrenOfEntityEvent.TYPE, this.#onChange);
      this.#events = events;
      events?.addEventListener(UmbRequestReloadChildrenOfEntityEvent.TYPE, this.#onChange);
    });
  }

  #events: EventTarget | undefined;
  // A restore or a delete changes what this screen says.
  readonly #onChange = () => void this.#load();

  override disconnectedCallback() {
    super.disconnectedCallback();
    this.#events?.removeEventListener(UmbRequestReloadChildrenOfEntityEvent.TYPE, this.#onChange);
  }

  protected override willUpdate(changed: PropertyValues) {
    if (changed.has('unique')) {
      this._loaded = false;
      void this.#load();
    }
  }

  async #load() {
    const key = this.unique;
    if (!key) return;
    const { data } =
      this.entityType === TRASHED_FOLDER_ENTITY_TYPE
        ? await tryExecute(this, folderGet({ path: { key } }), { disableNotifications: true })
        : await tryExecute(this, formGet({ path: { key } }), { disableNotifications: true });
    if (key !== this.unique) return;
    this._loaded = true;
    this._gone = !data;
    this._name = data?.name ?? '';
    this._since = data?.trashedUtc
      ? dayText(new Date(data.trashedUtc), new Date(), this.localize.lang() || 'en', {
          today: this.localize.term('jfbLibrary_today'),
          yesterday: this.localize.term('jfbLibrary_yesterday'),
        })
      : undefined;
  }

  override render() {
    if (!this._loaded) {
      return html`<umb-body-layout>
        <uui-box><p aria-busy="true">${this.localize.term('jfbLibrary_loading')}</p></uui-box>
      </umb-body-layout>`;
    }

    return html`<umb-body-layout>
      <div class="header" slot="header">
        <h2>${this._name}</h2>
        ${
          this._gone
            ? ''
            : html`<jfb-entity-menu
                entity-type=${this.entityType}
                .unique=${this.unique}
                label=${this.localize.term('jfbLibrary_moreActionsFor', this._name)}
              ></jfb-entity-menu>`
        }
      </div>
      <uui-box>
        <p>
          ${
            this._gone
              ? this.localize.term('jfbLibrary_itemGone')
              : this._since
                ? this.localize.term('jfbLibrary_inBinSince', this._since)
                : this.localize.term('jfbLibrary_notInBin')
          }
        </p>
      </uui-box>
    </umb-body-layout>`;
  }
}

if (!customElements.get('jfb-trashed-item')) customElements.define('jfb-trashed-item', TrashedItemElement);

export { TrashedItemElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-trashed-item': TrashedItemElement;
  }
}
