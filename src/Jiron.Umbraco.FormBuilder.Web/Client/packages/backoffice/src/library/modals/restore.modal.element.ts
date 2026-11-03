// "Restore..." (docs/ux/information-architecture.md, section 3; ADR-0166): where the item goes, its folder or, when that folder is
// in the Recycle Bin too, the top of Forms; and for a form, that its entries come back and pages that pick it show it again.
import { css, html, nothing } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import { folderGet, formGet, formHoldings } from '../../api';
import { jfbBackofficeTokens } from '../../styles/tokens';
import type { RestoreModalData, RestoreModalValue } from './tokens';

/** Where a restore lands, as its sidebar says it: the folder's name, or the top of Forms because the folder is in the bin too. */
interface Landing {
  /** Whether the item goes back into a folder; its name is null for a folder that did not read. */
  inFolder: boolean;
  folderName: string | null;
  folderTrashed: boolean;
  entries: number | null;
}

export class RestoreModalElement extends UmbModalBaseElement<RestoreModalData, RestoreModalValue> {
  static override properties = {
    ...UmbModalBaseElement.properties,
    _landing: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    css`
      p {
        margin: 0 0 var(--uui-size-space-4);
      }

      p:last-child {
        margin-bottom: 0;
      }

      .muted {
        color: var(--jfb-bo-color-text-muted);
      }
    `,
  ];

  declare private _landing: Landing | undefined;

  override connectedCallback() {
    super.connectedCallback();
    void this.#load();
  }

  async #load() {
    const data = this.data;
    if (!data) return;
    let folderKey: string | null | undefined;
    let entries: number | null = null;
    if (data.kind === 'form') {
      const [{ data: form }, { data: held }] = await Promise.all([
        tryExecute(this, formGet({ path: { key: data.key } }), { disableNotifications: true }),
        tryExecute(this, formHoldings({ path: { key: data.key } }), { disableNotifications: true }),
      ]);
      folderKey = form?.folderKey;
      entries = held?.entries ?? null;
    } else {
      folderKey = (await tryExecute(this, folderGet({ path: { key: data.key } }), { disableNotifications: true })).data
        ?.parentKey;
    }

    const folder = folderKey
      ? (await tryExecute(this, folderGet({ path: { key: folderKey } }), { disableNotifications: true })).data
      : undefined;
    this._landing = {
      inFolder: Boolean(folderKey),
      folderName: folder?.name ?? null,
      folderTrashed: Boolean(folder?.trashedUtc),
      entries,
    };
  }

  #where(): string {
    const landing = this._landing;
    const name = this.data?.name ?? '';
    if (!landing?.inFolder) return this.localize.term('jfbLibrary_restoreToTop', name);
    if (!landing.folderName) return this.localize.term('jfbLibrary_restoreToWhereItWas', name);
    return landing.folderTrashed
      ? this.localize.term('jfbLibrary_restoreFolderTrashed', name, landing.folderName)
      : this.localize.term('jfbLibrary_restoreToFolder', name, landing.folderName);
  }

  // What comes back with it: a folder's contents, a form's entries; a form without entries brings nothing more to say.
  #along(landing: Landing) {
    if (this.data?.kind !== 'form')
      return html`<p class="muted">${this.localize.term('jfbLibrary_restoreFolderComesBack')}</p>`;
    const entries = landing.entries ?? 0;
    return entries > 0
      ? html`<p class="muted">${this.localize.term('jfbLibrary_restoreFormComesBack', entries)}</p>`
      : nothing;
  }

  override render() {
    const landing = this._landing;
    return html`<umb-body-layout headline=${this.localize.term('jfbLibrary_restore')}>
      <uui-box>
        ${
          landing
            ? html`<p>${this.#where()}</p>
                ${this.#along(landing)}`
            : html`<p aria-busy="true">${this.localize.term('jfbLibrary_loading')}</p>`
        }
      </uui-box>
      <div slot="actions">
        <uui-button label=${this.localize.term('general_cancel')} @click=${this._rejectModal}></uui-button>
        <uui-button
          look="primary"
          color="positive"
          label=${this.localize.term('jfbLibrary_restore')}
          ?disabled=${!landing}
          @click=${() => {
            this.updateValue({ restore: true });
            this._submitModal();
          }}
        ></uui-button>
      </div>
    </umb-body-layout>`;
  }
}

if (!customElements.get('jfb-restore-modal')) customElements.define('jfb-restore-modal', RestoreModalElement);

export { RestoreModalElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-restore-modal': RestoreModalElement;
  }
}
