// "Create a form block type..." (docs/ux/information-architecture.md, section 10, ADR-0130): the dialog says what it creates and that it
// never changes existing types. Once it has, the same dialog says the one step left.
import { css, html, nothing } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import { setupCreateFormBlock } from '../api';
import { jfbBackofficeTokens } from '../styles/tokens';
import type { FormBlockModalValue } from './form-block-modal.token';

export class FormBlockModalElement extends UmbModalBaseElement<never, FormBlockModalValue> {
  static override properties = {
    ...UmbModalBaseElement.properties,
    _creating: { state: true },
    _created: { state: true },
    _failed: { state: true },
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
    `,
  ];

  declare private _creating: boolean;
  declare private _created: boolean;
  declare private _failed: boolean;

  constructor() {
    super();
    this._creating = false;
    this._created = false;
    this._failed = false;
  }

  async #create() {
    this._creating = true;
    this._failed = false;
    const { error } = await tryExecute(this, setupCreateFormBlock(), { disableNotifications: true });
    this._creating = false;
    this._failed = Boolean(error);
    this._created = !error;
    if (!error) this.updateValue({ created: true });
  }

  override render() {
    return html`
      <uui-dialog-layout headline=${this.localize.term('jfbOverview_blockHeadline')}>
        ${
          this._created
            ? html`<p role="status">${this.localize.term('jfbOverview_blockCreated')}</p>`
            : html`
                <p>${this.localize.term('jfbOverview_blockText')}</p>
                ${this._failed ? html`<p role="alert">${this.localize.term('jfbOverview_blockFailed')}</p>` : nothing}
              `
        }
        ${
          this._created
            ? html`
                <uui-button
                  slot="actions"
                  label=${this.localize.term('general_close')}
                  @click=${this._submitModal}
                ></uui-button>
                <uui-button
                  slot="actions"
                  look="primary"
                  color="positive"
                  href="section/settings/workspace/document-type-root"
                  label=${this.localize.term('jfbOverview_blockOpenTypes')}
                  @click=${this._submitModal}
                ></uui-button>
              `
            : html`
                <uui-button
                  slot="actions"
                  label=${this.localize.term('general_cancel')}
                  @click=${this._rejectModal}
                ></uui-button>
                <uui-button
                  slot="actions"
                  look="primary"
                  color="positive"
                  label=${this.localize.term('jfbOverview_blockCreate')}
                  .state=${this._creating ? 'waiting' : undefined}
                  @click=${this.#create}
                ></uui-button>
              `
        }
      </uui-dialog-layout>
    `;
  }
}

if (!customElements.get('jfb-form-block-modal')) customElements.define('jfb-form-block-modal', FormBlockModalElement);

export { FormBlockModalElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-form-block-modal': FormBlockModalElement;
  }
}
