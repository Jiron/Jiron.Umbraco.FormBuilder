// The Paste questions dialog (docs/ux/information-architecture.md section 5.1): one question per line, then the fields
// it will add with the type guessed for each, which the editor changes afterwards; the primary button says how many.
// The box is the browser's own, so its label and its help reach screen readers.
import { css, html, nothing } from '@umbraco-cms/backoffice/external/lit';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { jfbBackofficeTokens } from '../../styles/tokens';
import { MOST_PASTED, questionsOf, type PastedQuestion } from '../new-elements';
import type { PasteQuestionsModalData, PasteQuestionsModalValue } from './paste-questions.modal.token';

export class PasteQuestionsModalElement extends UmbModalBaseElement<PasteQuestionsModalData, PasteQuestionsModalValue> {
  static override properties = {
    ...UmbModalBaseElement.properties,
    _text: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    css`
      uui-dialog-layout {
        max-width: 640px;
      }

      .help {
        margin: 0 0 var(--uui-size-space-3);
        color: var(--jfb-bo-color-text-muted);
      }

      label {
        display: block;
        font-weight: 700;
      }

      textarea {
        box-sizing: border-box;
        width: 100%;
        min-height: 140px;
        padding: var(--uui-size-space-3) var(--uui-size-space-4);
        border: 1px solid var(--uui-color-border-standalone, var(--uui-color-border));
        border-radius: var(--uui-border-radius);
        background: var(--uui-color-surface);
        color: var(--uui-color-text);
        font: inherit;
        line-height: 1.5;
        resize: vertical;
      }

      textarea:hover {
        border-color: var(--uui-color-border-emphasis);
      }

      textarea:focus-visible {
        outline: 2px solid var(--uui-color-focus);
        outline-offset: 1px;
      }

      h3 {
        margin: var(--uui-size-space-6) 0 var(--uui-size-space-3);
        font-size: var(--uui-type-default-size);
      }

      table {
        width: 100%;
        border-collapse: collapse;
      }

      th,
      td {
        padding: var(--uui-size-space-3) var(--uui-size-space-4);
        border-bottom: 1px solid var(--uui-color-border);
        text-align: start;
        vertical-align: top;
      }

      th {
        font-weight: 700;
      }

      td:first-child {
        overflow-wrap: anywhere;
      }

      td:last-child,
      th:last-child {
        width: 34%;
      }

      .more {
        margin: var(--uui-size-space-3) 0 0;
        color: var(--jfb-bo-color-text-muted);
      }
    `,
  ];

  declare private _text: string;

  override connectedCallback() {
    super.connectedCallback();
    this._text = '';
  }

  #questions(): PastedQuestion[] {
    return questionsOf(this._text, new Set(Object.keys(this.data?.types ?? {})));
  }

  #add(questions: PastedQuestion[]) {
    if (questions.length === 0) return;
    this.updateValue({ questions });
    this._submitModal();
  }

  #typeName(type: string): string {
    const types = this.data?.types ?? {};
    return Object.hasOwn(types, type) ? (types[type] ?? type) : type;
  }

  override render() {
    const questions = this.#questions();
    const lines = this._text.split(/\r?\n/).filter((line) => line.trim().length > 0).length;
    return html`<uui-dialog-layout headline=${this.localize.term('jfbDesigner_pasteHeadline')}>
      <label for="questions">${this.localize.term('jfbDesigner_pasteLabel')}</label>
      <p class="help" id="questions-help">${this.localize.term('jfbDesigner_pasteHelp')}</p>
      <textarea
        id="questions"
        aria-describedby="questions-help"
        spellcheck="true"
        .value=${this._text}
        @input=${(event: Event) => (this._text = (event.target as HTMLTextAreaElement).value)}
      ></textarea>
      ${
        questions.length > 0
          ? html`<h3 id="fields">${this.localize.term('jfbDesigner_pasteFields')}</h3>
              <table aria-labelledby="fields">
                <thead>
                  <tr>
                    <th scope="col">${this.localize.term('jfbDesigner_pasteFieldLabel')}</th>
                    <th scope="col">${this.localize.term('jfbDesigner_pasteFieldType')}</th>
                  </tr>
                </thead>
                <tbody>
                  ${questions.map(
                    (question) =>
                      html`<tr>
                        <td>${question.label}</td>
                        <td>${this.#typeName(question.type)}</td>
                      </tr>`,
                  )}
                </tbody>
              </table>
              ${lines > MOST_PASTED ? html`<p class="more">${this.localize.term('jfbDesigner_pasteMost', MOST_PASTED)}</p>` : nothing}`
          : nothing
      }
      <uui-button slot="actions" label=${this.localize.term('general_cancel')} @click=${this._rejectModal}></uui-button>
      <uui-button
        slot="actions"
        look="primary"
        color="positive"
        ?disabled=${questions.length === 0}
        label=${this.localize.term('jfbDesigner_pasteAdd', questions.length)}
        @click=${() => this.#add(questions)}
      ></uui-button>
    </uui-dialog-layout>`;
  }
}

if (!customElements.get('jfb-paste-questions-modal'))
  customElements.define('jfb-paste-questions-modal', PasteQuestionsModalElement);

export { PasteQuestionsModalElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-paste-questions-modal': PasteQuestionsModalElement;
  }
}
