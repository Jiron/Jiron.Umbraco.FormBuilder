// The name a list's filters are saved under (ROADMAP 11.1, IA 6.1, ADR-0207): Umbraco's dialog with one box of at most 60
// characters. Saving happens here, so a name another set of the form has is said under the box, and the dialog stays.
import { css, html, nothing } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { jfbBackofficeTokens } from '../styles/tokens';
import { MOST_SET_NAME, type EntrySetNameModalData } from './entry-modals';

export class EntrySetNameModalElement extends UmbModalBaseElement<EntrySetNameModalData, never> {
  static override properties = {
    ...UmbModalBaseElement.properties,
    _name: { state: true },
    _problem: { state: true },
    _saving: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    css`
      uui-input {
        width: 100%;
      }

      .problem {
        margin: var(--uui-size-space-2) 0 0;
        color: var(--jfb-bo-color-danger-text);
      }
    `,
  ];

  declare private _name: string;
  declare private _problem: string;
  declare private _saving: boolean;

  constructor() {
    super();
    this._name = '';
    this._problem = '';
    this._saving = false;
  }

  #term = (key: string, ...args: unknown[]) => this.localize.term(`jfbEntries_${key}`, ...args);

  async #save() {
    const name = this._name.trim();
    if (!name) {
      this._problem = this.#term('edit_needsValue');
      return;
    }

    this._saving = true;
    const problem = await this.data?.save(name);
    this._saving = false;
    if (problem) {
      this._problem = problem;
      return;
    }

    this._submitModal();
  }

  override render() {
    return html`<uui-dialog-layout headline=${this.#term('set_headline')}>
      <form
        @submit=${(event: Event) => {
          event.preventDefault();
          void this.#save();
        }}
      >
        <uui-label for="name">${this.#term('set_name')}</uui-label>
        <uui-input
          id="name"
          label=${this.#term('set_name')}
          maxlength=${MOST_SET_NAME}
          .value=${this._name}
          @input=${(event: Event) => {
            this._name = (event.target as HTMLInputElement).value;
            this._problem = '';
          }}
        ></uui-input>
        ${this._problem ? html`<p class="problem" role="alert">${this._problem}</p>` : nothing}
      </form>
      <uui-button slot="actions" label=${this.#term('edit_cancel')} @click=${this._rejectModal}></uui-button>
      <uui-button
        slot="actions"
        look="primary"
        color="positive"
        label=${this.#term('set_save')}
        .state=${this._saving ? 'waiting' : undefined}
        @click=${() => void this.#save()}
      ></uui-button>
    </uui-dialog-layout>`;
  }
}

if (!customElements.get('jfb-entry-set-name-modal'))
  customElements.define('jfb-entry-set-name-modal', EntrySetNameModalElement);

export { EntrySetNameModalElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-entry-set-name-modal': EntrySetNameModalElement;
  }
}
