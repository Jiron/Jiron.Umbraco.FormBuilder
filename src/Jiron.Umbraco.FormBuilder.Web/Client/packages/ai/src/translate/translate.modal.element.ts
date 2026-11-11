// "Translate with AI" (docs/ux/information-architecture.md, section 5; ADR-0018): it says first why the AI cannot
// translate, when it cannot, instead of offering a choice that cannot work; else one of the site's other languages, the
// language the designer shows first. **Translate** saves what is not saved yet and asks for the saved draft's texts that
// lack the language (or all of them, when the editor asks); the preview lists each text beside its translation, which
// the editor may change or untick; **Apply** writes them into the open draft as one step Undo takes back, saved when the
// editor saves.
import { css, html, nothing } from '@umbraco-cms/backoffice/external/lit';
import { UmbLanguageCollectionRepository } from '@umbraco-cms/backoffice/language';
import { UmbModalBaseElement } from '@umbraco-cms/backoffice/modal';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import { aiFormTranslate, aiStatus } from '../api';
import { problemKey, refusalOf, refusalText, type Refusal } from '../problems';
import { changesOf, plainOf, rowsOf, withRow, type TranslationRow } from './translate.model';
import type { TranslateModalData } from './translate.modal.token';

interface Language {
  readonly culture: string;
  readonly name: string;
}

export class TranslateModalElement extends UmbModalBaseElement<TranslateModalData, never> {
  static override properties = {
    ...UmbModalBaseElement.properties,
    _languages: { state: true },
    _culture: { state: true },
    _replace: { state: true },
    _busy: { state: true },
    _rows: { state: true },
    _left: { state: true },
    _problem: { state: true },
    _setup: { state: true },
  };

  declare _languages: readonly Language[] | undefined;
  declare _culture: string;
  declare _replace: boolean;
  declare _busy: boolean;
  /** The preview, once the AI answered: undefined before. */
  declare _rows: readonly TranslationRow[] | undefined;
  declare _left: number;
  declare _problem: Refusal | 'changed' | null;
  /** Why the AI cannot translate, from its status; null while it can, undefined before it is read. */
  declare _setup: string | null | undefined;

  constructor() {
    super();
    this._languages = undefined;
    this._culture = '';
    this._replace = false;
    this._busy = false;
    this._rows = undefined;
    this._left = 0;
    this._problem = null;
    this._setup = undefined;
  }

  override connectedCallback() {
    super.connectedCallback();
    void this.#readLanguages();
    void this.#readStatus();
  }

  async #readStatus() {
    const { data } = await tryExecute(this, aiStatus(), { disableNotifications: true });
    this._setup = data ? (data.ready ? null : (data.problem ?? 'failed')) : 'failed';
  }

  async #readLanguages() {
    const { data } = await new UmbLanguageCollectionRepository(this).requestCollection({ skip: 0, take: 200 });
    const others = (data?.items ?? [])
      .filter((language) => !language.isDefault)
      .map((language) => ({ culture: language.unique, name: language.name }));
    this._languages = others;
    const shown = this.data?.designer.getLanguage().toLowerCase() ?? '';
    this._culture = (others.find((language) => language.culture.toLowerCase() === shown) ?? others[0])?.culture ?? '';
    // The language list takes the focus once it can be used, in the frame after the sidebar took it as it opened.
    await this.updateComplete;
    requestAnimationFrame(() => this.shadowRoot?.querySelector<HTMLSelectElement>('select:not([disabled])')?.focus());
  }

  get #languageName(): string {
    return this._languages?.find((language) => language.culture === this._culture)?.name ?? this._culture;
  }

  async #translate() {
    const designer = this.data?.designer;
    if (!designer || !this._culture || this._busy) return;
    this._busy = true;
    this._problem = null;
    // The translation reads the saved draft, so what is not saved yet is saved first, as Preview does; a save refused
    // asks nothing, and the designer says why itself.
    if (!(await designer.save())) {
      this._busy = false;
      return;
    }

    const { data, error } = await tryExecute(
      this,
      aiFormTranslate({
        path: { key: designer.formKey },
        body: { culture: this._culture, replaceExisting: this._replace },
      }),
      { disableNotifications: true },
    );
    this._busy = false;
    if (!data) {
      this._problem = refusalOf(error);
      return;
    }

    this._culture = data.culture;
    this._rows = rowsOf(data.texts);
    this._left = data.left;
  }

  #apply() {
    const designer = this.data?.designer;
    const rows = this._rows ?? [];
    const changes = changesOf(rows, this._culture);
    if (!designer || changes.length === 0) return;
    const said = this.localize.term('jfbAI_translateApplied', changes.length, this.#languageName);
    if (!designer.changeTexts(changes, said)) {
      this._problem = 'changed';
      this._rows = undefined;
      return;
    }

    this._submitModal();
  }

  override render() {
    const previewing = this._rows !== undefined;
    const used = (this._rows ?? []).filter((row) => row.use).length;
    return html`<umb-body-layout headline=${this.localize.term('jfbAI_translateHeadline')}>
      <div class="body">
        ${this.#lead()} ${previewing ? this.#preview() : nothing}
        <p class="problem" role="alert">${this.#problemText()}</p>
        <p class="busy" role="status">${this._busy ? this.localize.term('jfbAI_translating') : nothing}</p>
      </div>
      <div slot="actions">
        <uui-button label=${this.localize.term('general_cancel')} @click=${this._rejectModal}></uui-button>
        ${
          previewing && (this._rows?.length ?? 0) > 0
            ? html`<uui-button
                look="primary"
                color="positive"
                label=${this.localize.term('jfbAI_translateApply')}
                ?disabled=${used === 0}
                @click=${() => this.#apply()}
              ></uui-button>`
            : html`<uui-button
                look="primary"
                label=${this.localize.term('jfbAI_translateSubmit')}
                ?disabled=${!this._culture || this._setup !== null}
                .state=${this._busy ? 'waiting' : undefined}
                @click=${() => void this.#translate()}
              ></uui-button>`
        }
      </div>
    </umb-body-layout>`;
  }

  // What the sidebar opens with: why the AI cannot translate, that the site has one language, or the choice.
  #lead() {
    if (this._setup) return html`<p class="setup">${this.localize.term(problemKey(this._setup))}</p>`;
    if (this._languages?.length === 0) return html`<p>${this.localize.term('jfbAI_translateOneLanguage')}</p>`;
    return this.#choice();
  }

  #problemText() {
    if (this._problem === 'changed') return this.localize.term('jfbAI_translateChanged');
    return this._problem ? refusalText((key) => this.localize.term(key), this._problem) : nothing;
  }

  #choice() {
    const languages = this._languages ?? [];
    return html`<div class="choice">
      <label for="language">${this.localize.term('jfbAI_translateLanguage')}</label>
      <select
        id="language"
        ?disabled=${this._busy || languages.length === 0}
        @change=${(event: Event) => {
          this._culture = (event.target as HTMLSelectElement).value;
          this._rows = undefined;
        }}
      >
        ${languages.map(
          (language) =>
            html`<option value=${language.culture} ?selected=${language.culture === this._culture}>
              ${language.name}
            </option>`,
        )}
      </select>
      <uui-checkbox
        label=${this.localize.term('jfbAI_translateReplace', this.#languageName)}
        ?checked=${this._replace}
        ?disabled=${this._busy}
        @change=${(event: Event) => {
          this._replace = (event.target as HTMLInputElement).checked;
          this._rows = undefined;
        }}
      ></uui-checkbox>
    </div>`;
  }

  #preview() {
    const rows = this._rows ?? [];
    if (rows.length === 0)
      return html`<p class="lead">${this.localize.term('jfbAI_translateNone', this.#languageName)}</p>`;
    return html`<p class="lead">${this.localize.term('jfbAI_translateCount', rows.length, this.#languageName)}</p>
      ${this._left > 0 ? html`<p class="note">${this.localize.term('jfbAI_translateLeft', this._left)}</p>` : nothing}
      <table>
        <thead>
          <tr>
            <th scope="col"><span class="visually-hidden">${this.localize.term('jfbAI_translateApply')}</span></th>
            <th scope="col">${this.localize.term('jfbAI_translateSource')}</th>
            <th scope="col">${this.localize.term('jfbAI_translateTarget')}</th>
          </tr>
        </thead>
        <tbody>
          ${rows.map((row, index) => this.#row(row, index))}
        </tbody>
      </table>`;
  }

  #row(row: TranslationRow, index: number) {
    const source = row.html ? plainOf(row.source) : row.source;
    return html`<tr>
      <td>
        <input
          type="checkbox"
          .checked=${row.use}
          aria-label=${this.localize.term('jfbAI_translateUse', source)}
          @change=${(event: Event) => (this._rows = withRow(this._rows ?? [], row.path, { use: (event.target as HTMLInputElement).checked }))}
        />
      </td>
      <td class="source">${source}</td>
      <td>
        ${
          row.html
            ? html`<span lang=${this._culture}>${plainOf(row.text)}</span>
                <span class="note">${this.localize.term('jfbAI_translateRichText')}</span>`
            : html`<input
                type="text"
                class="text"
                lang=${this._culture}
                aria-label=${`${this.localize.term('jfbAI_translateTarget')} ${index + 1}: ${source}`}
                ?disabled=${!row.use}
                .value=${row.text}
                @input=${(event: Event) => (this._rows = withRow(this._rows ?? [], row.path, { text: (event.target as HTMLInputElement).value }))}
              />`
        }
      </td>
    </tr>`;
  }

  static override styles = css`
    .body {
      display: grid;
      gap: var(--uui-size-space-4);
    }

    p {
      margin: 0;
    }

    .choice {
      display: grid;
      gap: var(--uui-size-space-2);
      justify-items: start;
    }

    label {
      font-weight: 700;
    }

    select,
    .text {
      box-sizing: border-box;
      min-block-size: var(--uui-size-11);
      padding: 0 var(--uui-size-space-3);
      font: inherit;
      color: inherit;
      background: var(--uui-color-surface);
      border: 1px solid var(--uui-color-border-standalone);
      border-radius: var(--uui-border-radius);
    }

    .text {
      inline-size: 100%;
    }

    select:focus-visible,
    .text:focus-visible,
    input[type='checkbox']:focus-visible {
      outline: 2px solid var(--uui-color-focus);
      outline-offset: 1px;
    }

    table {
      inline-size: 100%;
      border-collapse: collapse;
    }

    th {
      text-align: start;
      font-weight: 700;
      padding: var(--uui-size-space-2);
      border-block-end: 1px solid var(--uui-color-border);
    }

    td {
      padding: var(--uui-size-space-2);
      vertical-align: top;
      border-block-end: 1px solid var(--uui-color-divider);
    }

    td:first-child,
    th:first-child {
      inline-size: 2rem;
    }

    .source {
      inline-size: 40%;
      overflow-wrap: anywhere;
    }

    .note {
      display: block;
      color: var(--uui-color-text-alt);
      font-size: var(--uui-type-small-size);
    }

    .problem {
      color: var(--uui-color-danger-standalone);
    }

    .problem:empty,
    .busy:empty {
      display: none;
    }

    .visually-hidden {
      position: absolute;
      inline-size: 1px;
      block-size: 1px;
      overflow: hidden;
      clip-path: inset(50%);
      white-space: nowrap;
    }
  `;
}

if (!customElements.get('jfb-ai-translate-modal'))
  customElements.define('jfb-ai-translate-modal', TranslateModalElement);

export { TranslateModalElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-ai-translate-modal': TranslateModalElement;
  }
}
