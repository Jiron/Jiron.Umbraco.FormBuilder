// A folder's name (docs/ux/information-architecture.md, sections 3 and 4): a new folder's, or a folder's new one. Enter confirms.
import { css, html, nothing } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { jfbBackofficeTokens } from '../../styles/tokens';
import type { FolderNameModalData, FolderNameModalValue } from './tokens';

/** The longest name a folder takes. */
export const MAX_FOLDER_NAME = 255;

export class FolderNameModalElement extends UmbModalBaseElement<FolderNameModalData, FolderNameModalValue> {
  static override properties = {
    ...UmbModalBaseElement.properties,
    _name: { state: true },
    _blank: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    css`
      uui-input {
        width: 100%;
      }

      .error {
        margin: var(--uui-size-space-2) 0 0;
        color: var(--jfb-bo-color-danger-text);
      }
    `,
  ];

  declare private _name: string;
  declare private _blank: boolean;

  override connectedCallback() {
    super.connectedCallback();
    this._name = this.data?.name ?? '';
    this._blank = false;
  }

  #confirm(event?: Event) {
    event?.preventDefault();
    const name = this._name.trim();
    if (!name) {
      this._blank = true;
      return;
    }

    this.updateValue({ name });
    this._submitModal();
  }

  override render() {
    return html`<uui-dialog-layout headline=${this.data?.headline ?? ''}>
      <form @submit=${this.#confirm}>
        <uui-label for="name" required>${this.localize.term('jfbLibrary_name')}</uui-label>
        <uui-input
          id="name"
          label=${this.localize.term('jfbLibrary_name')}
          maxlength=${MAX_FOLDER_NAME}
          .value=${this._name}
          ?error=${this._blank}
          aria-describedby=${this._blank ? 'blank' : ''}
          @input=${(event: Event) => {
            this._name = (event.target as HTMLInputElement).value;
            this._blank = false;
          }}
          @keydown=${(event: KeyboardEvent) => event.key === 'Enter' && this.#confirm(event)}
          autofocus
        ></uui-input>
        ${this._blank ? html`<p id="blank" class="error" role="alert">${this.localize.term('jfbLibrary_nameNeeded')}</p>` : nothing}
      </form>
      <uui-button slot="actions" label=${this.localize.term('general_cancel')} @click=${this._rejectModal}></uui-button>
      <uui-button
        slot="actions"
        look="primary"
        color="positive"
        label=${this.data?.confirmLabel ?? ''}
        @click=${() => this.#confirm()}
      ></uui-button>
    </uui-dialog-layout>`;
  }
}

if (!customElements.get('jfb-folder-name-modal'))
  customElements.define('jfb-folder-name-modal', FolderNameModalElement);

export { FolderNameModalElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-folder-name-modal': FolderNameModalElement;
  }
}
