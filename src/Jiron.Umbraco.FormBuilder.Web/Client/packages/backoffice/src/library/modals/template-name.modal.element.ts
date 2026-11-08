// A template's name and description (ROADMAP 12.1, ADR-0217): a form or a group saved as a template, or a saved template
// renamed. The name is needed; the description is one or two lines for the gallery's card. Enter in the name confirms.
import { css, html, nothing } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { jfbBackofficeTokens } from '../../styles/tokens';
import type { TemplateNameModalData, TemplateNameModalValue } from './tokens';

/** The longest name a template takes. */
export const MAX_TEMPLATE_NAME = 255;

/** The longest description a template takes. */
export const MAX_TEMPLATE_DESCRIPTION = 2000;

export class TemplateNameModalElement extends UmbModalBaseElement<TemplateNameModalData, TemplateNameModalValue> {
  static override properties = {
    ...UmbModalBaseElement.properties,
    _name: { state: true },
    _description: { state: true },
    _blank: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    css`
      /* As wide as a sentence reads well, as the duplicate's dialog: its note wraps rather than widening it. */
      uui-dialog-layout {
        box-sizing: border-box;
        inline-size: min(520px, calc(100vw - 2 * var(--uui-size-space-6)));
      }

      form {
        display: grid;
        gap: var(--uui-size-space-2);
      }

      uui-input,
      uui-textarea {
        width: 100%;
      }

      uui-label:not(:first-child) {
        margin-top: var(--uui-size-space-4);
      }

      .optional {
        font-weight: 400;
        color: var(--jfb-bo-color-text-muted);
      }

      .note {
        margin: var(--uui-size-space-4) 0 0;
        color: var(--jfb-bo-color-text-muted);
      }

      .error {
        margin: 0;
        color: var(--jfb-bo-color-danger-text);
      }
    `,
  ];

  declare private _name: string;
  declare private _description: string;
  declare private _blank: boolean;

  override connectedCallback() {
    super.connectedCallback();
    this._name = this.data?.name ?? '';
    this._description = this.data?.description ?? '';
    this._blank = false;
  }

  #confirm(event?: Event) {
    event?.preventDefault();
    const name = this._name.trim();
    if (!name) {
      this._blank = true;
      return;
    }

    this.updateValue({ name, description: this._description.trim() || null });
    this._submitModal();
  }

  override render() {
    return html`<uui-dialog-layout headline=${this.data?.headline ?? ''}>
      <form @submit=${this.#confirm}>
        <uui-label for="name" required>${this.localize.term('jfbLibrary_templateName')}</uui-label>
        <uui-input
          id="name"
          label=${this.localize.term('jfbLibrary_templateName')}
          maxlength=${MAX_TEMPLATE_NAME}
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
        <uui-label for="description"
          >${this.localize.term('jfbLibrary_templateDescription')}
          <span class="optional">${this.localize.term('jfbLibrary_optional')}</span></uui-label
        >
        <uui-textarea
          id="description"
          label=${this.localize.term('jfbLibrary_templateDescription')}
          maxlength=${MAX_TEMPLATE_DESCRIPTION}
          rows="3"
          .value=${this._description}
          @input=${(event: Event) => (this._description = (event.target as HTMLTextAreaElement).value)}
        ></uui-textarea>
        ${this.data?.note ? html`<p class="note">${this.data.note}</p>` : nothing}
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

if (!customElements.get('jfb-template-name-modal'))
  customElements.define('jfb-template-name-modal', TemplateNameModalElement);

export { TemplateNameModalElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-template-name-modal': TemplateNameModalElement;
  }
}
