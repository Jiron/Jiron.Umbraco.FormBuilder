// The create dialog of option lists (ROADMAP 12.1, IA 7, ADR-0214): where the new list's options come from, asked first
// as Umbraco's create dialog asks, each kind with one line on it. Choosing one is the action; the list screen then opens
// the new list's workspace.
import { css, html } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { KINDS, type ListKind } from './option-list-model';
import type { NewOptionListModalData, NewOptionListModalValue } from './new-option-list.modal.token';

/** Each kind's icon, as the list screen and the workspace show none of them twice. */
export const KIND_ICONS: Readonly<Record<ListKind, string>> = {
  manual: 'icon-bulleted-list',
  content: 'icon-document',
  dataType: 'icon-autofill',
  url: 'icon-link',
  file: 'icon-cloud-upload',
  countries: 'icon-globe',
  code: 'icon-code',
};

export class NewOptionListModalElement extends UmbModalBaseElement<NewOptionListModalData, NewOptionListModalValue> {
  static override styles = css`
    p {
      margin: 0 0 var(--uui-size-space-4);
    }
  `;

  #term = (key: string, ...args: unknown[]) => this.localize.term(`jfbOptionLists_${key}`, ...args);

  #choose(kind: ListKind) {
    this.updateValue({ kind });
    this._submitModal();
  }

  override render() {
    return html`<uui-dialog-layout headline=${this.#term('newList')}>
      <p>${this.#term('newListHelp')}</p>
      <uui-ref-list>
        ${KINDS.map(
          (kind) =>
            html`<umb-ref-item
              name=${this.#term(`kind_${kind}`)}
              detail=${this.#term(`kindHelp_${kind}`)}
              icon=${KIND_ICONS[kind]}
              @open=${() => this.#choose(kind)}
            ></umb-ref-item>`,
        )}
      </uui-ref-list>
      <uui-button slot="actions" label=${this.localize.term('general_cancel')} @click=${this._rejectModal}></uui-button>
    </uui-dialog-layout>`;
  }
}

if (!customElements.get('jfb-new-option-list-modal'))
  customElements.define('jfb-new-option-list-modal', NewOptionListModalElement);

export { NewOptionListModalElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-new-option-list-modal': NewOptionListModalElement;
  }
}
