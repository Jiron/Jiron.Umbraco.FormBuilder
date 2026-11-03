// The publish dialog (docs/ux/information-architecture.md, section 5.7): it asks before the draft goes live, with an
// optional comment that the version keeps. What changed since the live version and the form check's result join it with
// their steps.
import { css, html } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { jfbBackofficeTokens } from '../styles/tokens';
import type { PublishModalData, PublishModalValue } from './publish.modal.token';

/** The longest comment a version keeps. */
export const MAX_COMMENT = 500;

export class PublishModalElement extends UmbModalBaseElement<PublishModalData, PublishModalValue> {
  static override properties = {
    ...UmbModalBaseElement.properties,
    _comment: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    css`
      p {
        margin: 0 0 var(--uui-size-space-5);
      }

      uui-textarea {
        width: 100%;
      }
    `,
  ];

  declare private _comment: string;

  override connectedCallback() {
    super.connectedCallback();
    this._comment = '';
  }

  #confirm() {
    this.updateValue({ comment: this._comment });
    this._submitModal();
  }

  override render() {
    const name = this.data?.name ?? '';
    return html`<uui-dialog-layout headline=${this.localize.term('jfbDesigner_publishHeadline', name)}>
      <p>${this.localize.term('jfbDesigner_publishText')}</p>
      <uui-label for="comment">${this.localize.term('jfbDesigner_comment')}</uui-label>
      <uui-textarea
        id="comment"
        label=${this.localize.term('jfbDesigner_comment')}
        maxlength=${MAX_COMMENT}
        .value=${this._comment}
        @input=${(event: Event) => (this._comment = (event.target as HTMLTextAreaElement).value)}
      ></uui-textarea>
      <uui-button slot="actions" label=${this.localize.term('general_cancel')} @click=${this._rejectModal}></uui-button>
      <uui-button
        slot="actions"
        look="primary"
        color="positive"
        label=${this.localize.term('jfbDesigner_publishConfirm')}
        @click=${() => this.#confirm()}
      ></uui-button>
    </uui-dialog-layout>`;
  }
}

if (!customElements.get('jfb-publish-modal')) customElements.define('jfb-publish-modal', PublishModalElement);

export { PublishModalElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-publish-modal': PublishModalElement;
  }
}
