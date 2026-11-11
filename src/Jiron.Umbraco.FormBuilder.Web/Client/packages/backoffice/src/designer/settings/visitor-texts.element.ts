// A site's or a form's own wording of the package's visitor texts (ROADMAP 14.2, ADR-0230): the texts reworded, each
// named by the package's words in the language shown, with its wording there in a box; and "Reword a text", a list of
// the package's texts by group, which adds one. The package's words name each row once, so the box shows no
// placeholder. A box emptied keeps its row until it is taken away, so a wording can be typed again; an empty wording
// leaves the package's words. The Settings view and Settings > General share it.
import { css, html, live, nothing } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { tryExecute } from '@umbraco-cms/backoffice/resources';
import { visitorTextsGet, type VisitorTextsResponseModel } from '../../api';
import { controlStyles } from '../inspector/control.styles';
import {
  rowsOf,
  withoutWording,
  withWording,
  wordingProblem,
  type VisitorTextRow,
  type VisitorTexts,
  type WordingProblem,
} from './visitor-texts';

/** The package's texts by culture, asked for once per culture. */
const catalogues = new Map<string, Promise<VisitorTextsResponseModel | null>>();

export class VisitorTextsElement extends UmbLitElement {
  static override properties = {
    texts: { attribute: false },
    culture: {},
    disabled: { type: Boolean },
    _catalogue: { state: true },
    _chosen: { state: true },
  };

  static override styles = [
    controlStyles,
    css`
      :host {
        display: block;
      }

      ul {
        display: grid;
        gap: var(--uui-size-space-4);
        margin: 0 0 var(--uui-size-space-4);
        padding: 0;
        list-style: none;
      }

      .label-row .label {
        flex: 1;
      }

      .remove {
        display: inline-grid;
        flex: none;
        place-items: center;
        inline-size: 28px;
        block-size: 28px;
        margin: 0;
        padding: 0;
        border: 0;
        border-radius: var(--uui-border-radius);
        background: none;
        color: var(--uui-color-interactive);
        font: inherit;
        cursor: pointer;
      }

      .remove:hover {
        background: var(--uui-color-surface-emphasis);
        color: var(--uui-color-interactive-emphasis);
      }

      .remove:focus-visible {
        outline: 2px solid var(--uui-color-focus);
        outline-offset: 1px;
      }
    `,
  ];

  /** The texts reworded, as the definition or the settings hold them. */
  declare texts: VisitorTexts | undefined;
  /** The culture the wording is in: the language shown, its own code. */
  declare culture: string;
  /** Whether the wording can only be read. */
  declare disabled: boolean;
  declare private _catalogue: VisitorTextsResponseModel | null;
  declare private _chosen: ReadonlySet<string>;
  #asked = '';

  constructor() {
    super();
    this.texts = undefined;
    this.culture = '';
    this.disabled = false;
    this._catalogue = null;
    this._chosen = new Set();
  }

  override updated(): void {
    if (this.culture === this.#asked) return;
    const culture = (this.#asked = this.culture);
    let asked = catalogues.get(culture);
    if (asked === undefined) {
      asked = tryExecute(this, visitorTextsGet({ query: { culture } }), { disableNotifications: true }).then(
        ({ data }) => data ?? null,
      );
      catalogues.set(culture, asked);
    }
    void asked.then((catalogue) => {
      if (catalogue === null) catalogues.delete(culture);
      if (this.#asked === culture) this._catalogue = catalogue;
    });
  }

  #changed(texts: VisitorTexts) {
    this.dispatchEvent(new CustomEvent('change', { detail: { texts }, bubbles: true, composed: true }));
  }

  #problem(problem: WordingProblem): string {
    switch (problem.kind) {
      case 'line':
        return this.localize.term('jfbSettings_textsLine');
      case 'long':
        return this.localize.term('jfbSettings_textsLong', problem.most);
      case 'values':
        return problem.values.length > 0
          ? this.localize.term('jfbSettings_textsValues', problem.values.join(', '))
          : this.localize.term('jfbSettings_textsNoValues');
    }
  }

  #row(row: VisitorTextRow) {
    const id = `text-${row.key.replaceAll('.', '-')}`;
    const problem = row.wording === '' ? null : wordingProblem(row.wording, row.values);
    return html`<li class="item">
      <div class="label-row">
        <label class="label" for=${id}>${row.text}</label>
        ${
          this.disabled
            ? nothing
            : html`<button
                type="button"
                class="remove"
                aria-label=${this.localize.term('jfbSettings_textsRemove', row.text)}
                title=${this.localize.term('jfbSettings_textsRemove', row.text)}
                @click=${() => {
                  const chosen = new Set(this._chosen);
                  chosen.delete(row.key);
                  this._chosen = chosen;
                  this.#changed(withoutWording(this.texts, row.key, this.culture));
                }}
              >
                <uui-icon name="icon-trash" aria-hidden="true"></uui-icon>
              </button>`
        }
      </div>
      <input
        type="text"
        id=${id}
        lang=${this.culture || nothing}
        autocomplete="off"
        ?disabled=${this.disabled}
        .value=${live(row.wording)}
        aria-invalid=${problem ? 'true' : 'false'}
        aria-describedby=${problem ? `${id}-why` : nothing}
        @input=${(event: Event) =>
          this.#changed(withWording(this.texts, row.key, this.culture, (event.target as HTMLInputElement).value))}
      />
      ${problem ? html`<p class="why" id=${`${id}-why`}>${this.#problem(problem)}</p>` : nothing}
    </li>`;
  }

  #choose(event: Event) {
    const select = event.target as HTMLSelectElement;
    const key = select.value;
    select.value = '';
    if (!key) return;
    this._chosen = new Set([...this._chosen, key]);
    void this.updateComplete.then(() => this.shadowRoot?.getElementById(`text-${key.replaceAll('.', '-')}`)?.focus());
  }

  override render() {
    const catalogue = this._catalogue;
    if (catalogue === null) return nothing;
    const rows = rowsOf(this.texts, this.culture, catalogue, this._chosen);
    const listed = new Set(rows.map((row) => row.key));
    return html`
      ${
        rows.length > 0
          ? html`<ul>
              ${rows.map((row) => this.#row(row))}
            </ul>`
          : nothing
      }
      ${
        this.disabled
          ? nothing
          : html`<div class="item">
              <label class="label" for="choose">${this.localize.term('jfbSettings_textsAdd')}</label>
              <select id="choose" @change=${(event: Event) => this.#choose(event)}>
                <option value="" selected>${this.localize.term('jfbSettings_textsChoose')}</option>
                ${catalogue.groups.map(
                  (group) =>
                    html`<optgroup label=${this.localize.term(`jfbSettings_textsGroup_${group.alias}`)}>
                      ${group.texts
                        .filter((text) => !listed.has(text.key))
                        .map((text) => html`<option value=${text.key}>${text.text}</option>`)}
                    </optgroup>`,
                )}
              </select>
            </div>`
      }
    `;
  }
}

if (!customElements.get('jfb-visitor-texts')) customElements.define('jfb-visitor-texts', VisitorTextsElement);

declare global {
  interface HTMLElementTagNameMap {
    'jfb-visitor-texts': VisitorTextsElement;
  }
}
