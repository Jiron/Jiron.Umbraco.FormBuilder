// "Suggest help text" beside a field's help text in the inspector (docs/ux/information-architecture.md, section 5;
// ADR-0018), while the designer shows the site's default language: it saves what is not saved yet, as Preview does,
// then lists a help text and up to three improvements to the field's own texts, each with **Use**, which writes it into
// the open draft as one step Undo takes back. The list opens in the inspector itself, under the label's row and above
// the box, so nothing covers the text that **Use** changes (docs/ux/review-15.3.md).
import { css, html, nothing, type PropertyValues } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import type {
  JfbFieldText,
  JfbFieldTextActionElement,
  JfbFormDesigner,
} from '../../../backoffice/public/types/jiron-form-builder';
import { aiFormSuggest, aiStatus, type AiSuggestedTextResponseModel } from '../api';
import { refusal, refusalOf, refusalText, type ProblemCode, type Refusal } from '../problems';

export class SuggestElement extends UmbLitElement implements JfbFieldTextActionElement {
  static override properties = {
    designer: { attribute: false },
    fieldKey: {},
    text: {},
    language: {},
    _open: { state: true },
    _busy: { state: true },
    _problem: { state: true },
    _suggestions: { state: true },
  };

  declare designer: JfbFormDesigner | undefined;
  declare fieldKey: string;
  declare text: JfbFieldText;
  declare language: string;
  declare _open: boolean;
  declare _busy: boolean;
  declare _problem: Refusal | null;
  /** What the AI suggested, once it answered; undefined before. */
  declare _suggestions: readonly AiSuggestedTextResponseModel[] | undefined;

  #asking = 0;

  constructor() {
    super();
    this.designer = undefined;
    this.fieldKey = '';
    this.text = 'help';
    this.language = '';
    this._open = false;
    this._busy = false;
    this._problem = null;
    this._suggestions = undefined;
  }

  // Asks as the list opens: first whether the AI can answer, saving nothing when it cannot; then, the draft saved,
  // for the suggestions. A field changed meanwhile drops the answer.
  async #ask() {
    const designer = this.designer;
    const field = this.fieldKey;
    if (!designer || !field) return;
    const asking = ++this.#asking;
    this._busy = true;
    this._problem = null;
    this._suggestions = undefined;
    const { data: status } = await tryExecute(this, aiStatus(), { disableNotifications: true });
    if (asking !== this.#asking) return;
    if (!status?.ready) {
      this._busy = false;
      this._problem = refusal((status?.problem ?? 'failed') as ProblemCode);
      return;
    }

    if (!(await designer.save())) {
      this._busy = false;
      this.#close();
      return;
    }

    const { data, error } = await tryExecute(
      this,
      aiFormSuggest({ path: { key: designer.formKey }, body: { fieldKey: field } }),
      { disableNotifications: true },
    );
    if (asking !== this.#asking) return;
    this._busy = false;
    if (!data) {
      this._problem = refusalOf(error);
      return;
    }

    this._suggestions = [...(data.help ? [data.help] : []), ...data.improvements];
  }

  #use(suggestion: AiSuggestedTextResponseModel) {
    const what = this.localize.term(`jfbAI_suggestSetting_${suggestion.setting}`);
    const used = this.designer?.changeTexts(
      [{ path: suggestion.path, culture: '', text: suggestion.text }],
      this.localize.term('jfbAI_suggestUsed', what),
    );
    if (!used) {
      this._problem = refusal('ai_field_missing');
      return;
    }

    const index = (this._suggestions ?? []).indexOf(suggestion);
    this._suggestions = (this._suggestions ?? []).filter((other) => other !== suggestion);
    if (this._suggestions.length === 0) {
      this.#close();
      return;
    }

    // The focus goes to the Use of what took the used one's place.
    const next = Math.min(index, this._suggestions.length - 1);
    void this.updateComplete.then(() => this.shadowRoot?.querySelectorAll<HTMLElement>('li uui-button')[next]?.focus());
  }

  #toggle() {
    if (this._open) {
      this.#close();
      return;
    }

    this._open = true;
    void this.#ask();
  }

  // Closes the list, drops an answer still on its way, and gives the focus back to the button.
  #close() {
    const had = this._open;
    this.#drop();
    if (had) void this.updateComplete.then(() => this.shadowRoot?.querySelector<HTMLElement>('.trigger')?.focus());
  }

  #drop() {
    this._open = false;
    this._busy = false;
    this.#asking++;
  }

  protected override updated(changed: PropertyValues<this>) {
    super.updated(changed);
    // Another field, or another language, closes the list: what it holds was asked for the field before.
    const before = changed.get('fieldKey') as string | undefined;
    if (this._open && ((before !== undefined && before !== this.fieldKey) || this.language !== '')) this.#drop();
  }

  override render() {
    // Suggestions are written in the site's default language, where the field's texts start.
    if (this.language !== '') return nothing;
    return html`<button
        type="button"
        class="trigger"
        aria-expanded=${this._open ? 'true' : 'false'}
        aria-controls="suggestions"
        @click=${() => this.#toggle()}
      >
        <uui-icon name="icon-wand" aria-hidden="true"></uui-icon><span>${this.localize.term('jfbAI_suggest')}</span>
      </button>
      ${this._open ? this.#panel() : nothing}`;
  }

  #panel() {
    return html`<div
      id="suggestions"
      class="panel"
      role="group"
      aria-label=${this.localize.term('jfbAI_suggestHeadline')}
      @keydown=${(event: KeyboardEvent) => {
        if (event.key !== 'Escape') return;
        event.stopPropagation();
        this.#close();
      }}
    >
      <p class="busy" role="status">${this._busy ? this.localize.term('jfbAI_suggesting') : nothing}</p>
      <p class="problem" role="alert">
        ${this._problem ? refusalText((key) => this.localize.term(key), this._problem) : nothing}
      </p>
      ${this.#list()}
    </div>`;
  }

  #list() {
    const suggestions = this._suggestions;
    if (suggestions === undefined) return nothing;
    if (suggestions.length === 0) return html`<p>${this.localize.term('jfbAI_suggestNone')}</p>`;
    return html`<ul>
      ${suggestions.map((suggestion) => {
        const what = this.localize.term(`jfbAI_suggestSetting_${suggestion.setting}`);
        return html`<li>
          <span class="what">${what}</span>
          <p class="text">${suggestion.text}</p>
          ${suggestion.reason ? html`<p class="reason">${suggestion.reason}</p>` : nothing}
          <uui-button
            compact
            look="secondary"
            label=${this.localize.term('jfbAI_suggestUseText', what, suggestion.text)}
            @click=${() => this.#use(suggestion)}
            >${this.localize.term('jfbAI_suggestUse')}</uui-button
          >
        </li>`;
      })}
    </ul>`;
  }

  static override styles = css`
    /* The button and the list are items of the label's row: the button at its end, the list on a line of its own. */
    :host {
      display: contents;
    }

    .trigger {
      display: inline-flex;
      align-items: center;
      gap: var(--uui-size-space-1);
      min-block-size: 28px;
      padding: 0 var(--uui-size-space-2);
      font: inherit;
      font-size: var(--uui-type-small-size);
      color: var(--uui-color-interactive);
      background: none;
      border: 0;
      border-radius: var(--uui-border-radius);
      cursor: pointer;
    }

    .trigger:hover {
      color: var(--uui-color-interactive-emphasis);
      background: var(--uui-color-surface-emphasis);
    }

    .trigger:focus-visible {
      outline: 2px solid var(--uui-color-focus);
      outline-offset: 1px;
    }

    .trigger[aria-expanded='true'] {
      background: var(--uui-color-surface-alt);
    }

    .panel {
      box-sizing: border-box;
      flex: 1 0 100%;
      display: grid;
      gap: var(--uui-size-space-3);
      margin-block: var(--uui-size-space-2);
      padding: var(--uui-size-space-3) var(--uui-size-space-4);
      /* The surface, edged, so each Use, in the secondary look, keeps its own shape on it. */
      background: var(--uui-color-surface);
      border: 1px solid var(--uui-color-border);
      border-radius: var(--uui-border-radius);
    }

    p {
      margin: 0;
    }

    ul {
      display: grid;
      gap: var(--uui-size-space-4);
      margin: 0;
      padding: 0;
      list-style: none;
    }

    li {
      display: grid;
      gap: var(--uui-size-space-1);
      justify-items: start;
    }

    .what {
      font-size: var(--uui-type-small-size);
      font-weight: 700;
      color: var(--uui-color-text-alt);
    }

    .text {
      overflow-wrap: anywhere;
    }

    .reason {
      font-size: var(--uui-type-small-size);
      color: var(--uui-color-text-alt);
    }

    .problem {
      color: var(--uui-color-danger-standalone);
    }

    .busy:empty,
    .problem:empty {
      display: none;
    }
  `;
}

if (!customElements.get('jfb-ai-suggest')) customElements.define('jfb-ai-suggest', SuggestElement);

export { SuggestElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-ai-suggest': SuggestElement;
  }
}
