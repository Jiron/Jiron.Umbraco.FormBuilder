// "Duplicate..." (docs/ux/information-architecture.md, section 3; ADR-0046, ADR-0166): the copy's name, whether it takes the
// form's actions (only when the form has some and the user may manage them where the copy lands), and for a restricted form
// the one thing that surprises: the copy takes its folder's access instead of the form's restriction.
import { css, html, nothing } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import { formDraft } from '../../api';
import { jfbBackofficeTokens } from '../../styles/tokens';
import { MAX_FOLDER_NAME } from './folder-name.modal.element';
import type { DuplicateModalData, DuplicateModalValue } from './tokens';

export class DuplicateModalElement extends UmbModalBaseElement<DuplicateModalData, DuplicateModalValue> {
  static override properties = {
    ...UmbModalBaseElement.properties,
    _name: { state: true },
    _actions: { state: true },
    _includeActions: { state: true },
    _counting: { state: true },
    _blank: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    css`
      uui-input {
        width: 100%;
      }

      p {
        margin: var(--uui-size-space-4) 0 0;
      }

      uui-toggle {
        display: block;
        margin-top: var(--uui-size-space-5);
      }

      .error {
        margin-top: var(--uui-size-space-2);
        color: var(--jfb-bo-color-danger-text);
      }
    `,
  ];

  declare private _name: string;
  /** How many actions the form's draft has; 0 until known. */
  declare private _actions: number;
  declare private _includeActions: boolean;
  /** Whether the actions are still being counted; the copy waits for it, so that it does not leave them out unasked. */
  declare private _counting: boolean;
  declare private _blank: boolean;

  override connectedCallback() {
    super.connectedCallback();
    const form = this.data?.form;
    this._name = form ? this.localize.term('jfbLibrary_copyName', form.name) : '';
    this._actions = 0;
    this._includeActions = true;
    this._blank = false;
    this._counting = Boolean(form && this.data?.mayTakeActions);
    if (form && this._counting) void this.#countActions(form.key);
  }

  async #countActions(key: string) {
    const { data } = await tryExecute(this, formDraft({ path: { key } }), { disableNotifications: true });
    this._actions = ((data?.definition as { actions?: unknown[] } | null | undefined)?.actions ?? []).length;
    this._counting = false;
  }

  #confirm(event?: Event) {
    event?.preventDefault();
    if (this._counting) return;
    const name = this._name.trim();
    if (!name) {
      this._blank = true;
      return;
    }

    this.updateValue({ name, includeActions: this._actions > 0 && this._includeActions });
    this._submitModal();
  }

  override render() {
    const form = this.data?.form;
    if (!form) return nothing;
    const where =
      this.data?.folderName ??
      this.localize.term(this.data?.inFolder ? 'jfbLibrary_sameFolder' : 'jfbLibrary_topOfForms');
    return html`<uui-dialog-layout headline=${this.localize.term('jfbLibrary_duplicateHeadline', form.name)}>
      <form @submit=${this.#confirm}>
        <uui-label for="name" required>${this.localize.term('jfbLibrary_copysName')}</uui-label>
        <uui-input
          id="name"
          label=${this.localize.term('jfbLibrary_copysName')}
          maxlength=${MAX_FOLDER_NAME}
          .value=${this._name}
          ?error=${this._blank}
          @input=${(event: Event) => {
            this._name = (event.target as HTMLInputElement).value;
            this._blank = false;
          }}
          @keydown=${(event: KeyboardEvent) => event.key === 'Enter' && this.#confirm(event)}
          autofocus
        ></uui-input>
        ${this._blank ? html`<p class="error" role="alert">${this.localize.term('jfbLibrary_nameNeeded')}</p>` : nothing}
        ${
          this._actions > 0
            ? html`<uui-toggle
                label=${this.localize.term('jfbLibrary_copyActionsToo', this._actions)}
                ?checked=${this._includeActions}
                @change=${(event: Event) => (this._includeActions = (event.target as HTMLInputElement).checked)}
              ></uui-toggle>`
            : nothing
        }
        ${form.isRestricted ? html`<p>${this.localize.term('jfbLibrary_duplicateRestricted', form.name, where)}</p>` : nothing}
      </form>
      <uui-button slot="actions" label=${this.localize.term('general_cancel')} @click=${this._rejectModal}></uui-button>
      <uui-button
        slot="actions"
        look="primary"
        color="positive"
        label=${this.localize.term('jfbLibrary_duplicate')}
        ?disabled=${this._counting}
        @click=${() => this.#confirm()}
      ></uui-button>
    </uui-dialog-layout>`;
  }
}

if (!customElements.get('jfb-duplicate-modal')) customElements.define('jfb-duplicate-modal', DuplicateModalElement);

export { DuplicateModalElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-duplicate-modal': DuplicateModalElement;
  }
}
