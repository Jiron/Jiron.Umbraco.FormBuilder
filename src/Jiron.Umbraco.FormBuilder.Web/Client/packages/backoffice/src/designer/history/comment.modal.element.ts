// A version's comment (ROADMAP 10.3a, IA 5.6, ADR-0196): one text of at most 500 characters, with Save and Cancel;
// saved empty, it removes the comment. Enter in the box starts a new line, as in the publish dialog's comment.
import { css, html } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { jfbBackofficeTokens } from '../../styles/tokens';
import { MOST_COMMENT, type CommentModalData, type CommentModalValue } from './comment.modal.token';

export class CommentModalElement extends UmbModalBaseElement<CommentModalData, CommentModalValue> {
  static override properties = {
    ...UmbModalBaseElement.properties,
    _comment: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    css`
      uui-textarea {
        width: 100%;
      }

      .help {
        margin: var(--uui-size-space-2) 0 0;
        color: var(--jfb-bo-color-text-muted);
      }
    `,
  ];

  declare private _comment: string;

  constructor() {
    super();
    this._comment = '';
  }

  override connectedCallback() {
    super.connectedCallback();
    this._comment = this.data?.comment ?? '';
  }

  #term = (key: string, ...args: unknown[]) => this.localize.term(`jfbHistory_${key}`, ...args);

  #save() {
    this.value = { comment: this._comment.trim() };
    this._submitModal();
  }

  override render() {
    return html`<uui-dialog-layout headline=${this.#term('comment_headline', this.data?.name ?? '')}>
      <uui-label for="comment">${this.#term('comment_label')}</uui-label>
      <!-- The box's own name holds the help, as a description can't reach into the box's shadow root. -->
      <uui-textarea
        id="comment"
        label=${this.#term('comment_named', this.#term('comment_label'), this.#term('comment_help'))}
        maxlength=${MOST_COMMENT}
        .value=${this._comment}
        @input=${(event: Event) => (this._comment = (event.target as HTMLTextAreaElement).value)}
      ></uui-textarea>
      <p class="help">${this.#term('comment_help')}</p>
      <uui-button slot="actions" label=${this.localize.term('general_cancel')} @click=${this._rejectModal}></uui-button>
      <uui-button
        slot="actions"
        look="primary"
        color="positive"
        label=${this.#term('comment_save')}
        @click=${() => this.#save()}
      ></uui-button>
    </uui-dialog-layout>`;
  }
}

if (!customElements.get('jfb-version-comment-modal'))
  customElements.define('jfb-version-comment-modal', CommentModalElement);

export { CommentModalElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-version-comment-modal': CommentModalElement;
  }
}
