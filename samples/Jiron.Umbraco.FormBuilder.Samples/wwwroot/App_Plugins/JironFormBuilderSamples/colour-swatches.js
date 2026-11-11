// @ts-check
// The colour sample's own editor of a setting (docs/extending.md, section 7): the colours a colour field offers, each a
// name and a value, which no built-in editor draws. Its `jfbSettingEditor` manifest names the editor `sample.swatches`,
// and every setting whose C# attribute says `Editor = "sample.swatches"` is drawn with it, under the setting's label and
// help, which the designer draws. It sets `value` and says `change`; the designer keeps the value, and the server
// checks it when the form is published (ColourSwatch.Problem).
//
// Plain JavaScript that the backoffice loads as it is: Lit and Umbraco's mixins come from the backoffice's import map,
// and the types from Jiron Form Builder's declarations, which a site copies from
// /App_Plugins/JironFormBuilder/types/jiron-form-builder.d.ts.
import { css, html, LitElement, nothing, repeat } from '@umbraco-cms/backoffice/external/lit';
import { UmbElementMixin } from '@umbraco-cms/backoffice/element-api';

/** @typedef {import('../../../../../src/Jiron.Umbraco.FormBuilder.Web/Client/packages/backoffice/public/types/jiron-form-builder').JfbSetting} JfbSetting */
/** @typedef {{ name: string, value: string }} Swatch */

/** The most colours a field offers, as the server allows (ColourSwatch.Most). */
const MOST = 24;

/** The colours a new one starts as, the first the field does not offer yet. */
const STARTS = ['#1f6feb', '#2da44e', '#cf222e', '#bf8700', '#8250df', '#0a7ea4', '#bc4c00', '#57606a'];

const HEX = /^#[0-9a-f]{6}$/;

/**
 * The colours of a setting's value, each with a name and a value the colour input can show.
 * @param {unknown} value
 * @returns {Swatch[]}
 */
export function readSwatches(value) {
  if (!Array.isArray(value)) return [];
  return value.map((item) => {
    const name = item !== null && typeof item === 'object' && typeof item.name === 'string' ? item.name : '';
    const colour = item !== null && typeof item === 'object' && typeof item.value === 'string' ? item.value : '';
    return { name, value: HEX.test(colour) ? colour : '#000000' };
  });
}

export default class SampleSwatchesEditor extends UmbElementMixin(LitElement) {
  static properties = {
    value: { attribute: false },
    setting: { attribute: false },
    label: {},
    culture: {},
    invalid: { type: Boolean, reflect: true },
  };

  constructor() {
    super();
    /** @type {unknown} The setting's value: a list of `{ name, value }`. */
    this.value = undefined;
    /** @type {JfbSetting | undefined} */
    this.setting = undefined;
    /** The setting's label, the list's accessible name. */
    this.label = '';
    /** The language the designer edits texts in; a colour's name is one text for every language. */
    this.culture = '';
    /** Whether the value given last cannot be kept, which the designer says under the editor. */
    this.invalid = false;
  }

  render() {
    const swatches = readSwatches(this.value);
    return html`
      <ul aria-label=${this.label}>
        ${repeat(
          swatches,
          (_swatch, index) => index,
          (swatch, index) => {
            const called = swatch.name || this.localize.term('jfbSamples_colourName', index + 1);
            return html`<li>
              <input
                type="color"
                .value=${swatch.value}
                aria-label=${this.localize.term('jfbSamples_colourValue', called)}
                @change=${(/** @type {Event} */ event) => this.#change(index, { value: inputOf(event).value })}
              />
              <uui-input
                label=${this.localize.term('jfbSamples_colourName', index + 1)}
                .value=${swatch.name}
                maxlength="40"
                required
                @change=${(/** @type {Event} */ event) => this.#change(index, { name: inputOf(event).value.trim() })}
              ></uui-input>
              ${
                swatches.length > 1
                  ? html`<uui-button
                      compact
                      look="secondary"
                      label=${this.localize.term('jfbSamples_removeColour', called)}
                      @click=${() => this.#remove(index)}
                      ><uui-icon name="icon-trash"></uui-icon
                    ></uui-button>`
                  : nothing
              }
            </li>`;
          },
        )}
      </ul>
      ${swatches.length === 0 ? html`<p class="empty">${this.localize.term('jfbSamples_noColours')}</p>` : nothing}
      ${
        swatches.length < MOST
          ? html`<uui-button
              class="add"
              look="placeholder"
              label=${this.localize.term('jfbSamples_addColour')}
              @click=${this.#add}
            >
              <uui-icon name="icon-add"></uui-icon>${this.localize.term('jfbSamples_addColour')}
            </uui-button>`
          : nothing
      }
    `;
  }

  /**
   * Changes one colour.
   * @param {number} index
   * @param {Partial<Swatch>} change
   */
  #change(index, change) {
    this.#give(readSwatches(this.value).map((swatch, at) => (at === index ? { ...swatch, ...change } : swatch)));
  }

  /** Adds a colour the field does not offer yet, named so that it can be kept, and puts the focus on its name. */
  #add() {
    const swatches = readSwatches(this.value);
    const value = STARTS.find((start) => !swatches.some((swatch) => swatch.value === start)) ?? '#57606a';
    this.#give([...swatches, { name: this.localize.term('jfbSamples_newColour'), value }]);
    void this.updateComplete.then(() => {
      const names = this.renderRoot.querySelectorAll('uui-input');
      const added = /** @type {HTMLElement | undefined} */ (names[names.length - 1]);
      added?.focus();
    });
  }

  /**
   * Removes a colour, and puts the focus on what takes its place: the next colour's remove button, or Add a colour once
   * the last colour left has none. The last colour is never removed: a field offers at least one, and a field with none
   * would hold the type's default colours instead.
   * @param {number} index
   */
  #remove(index) {
    const swatches = readSwatches(this.value).filter((_swatch, at) => at !== index);
    this.#give(swatches);
    void this.updateComplete.then(() => {
      const removes = this.renderRoot.querySelectorAll('li uui-button');
      const next = /** @type {HTMLElement | null} */ (
        removes[Math.min(index, removes.length - 1)] ?? this.renderRoot.querySelector('.add')
      );
      next?.focus();
    });
  }

  /**
   * Keeps a new value and says so.
   * @param {Swatch[]} swatches
   */
  #give(swatches) {
    this.value = swatches;
    this.dispatchEvent(new Event('change', { bubbles: true }));
  }

  static styles = css`
    :host {
      display: block;
    }
    ul {
      display: grid;
      gap: var(--uui-size-space-2);
      margin: 0 0 var(--uui-size-space-3);
      padding: 0;
      list-style: none;
    }
    ul:empty {
      display: none;
    }
    li {
      display: grid;
      grid-template-columns: auto 1fr auto;
      align-items: center;
      gap: var(--uui-size-space-3);
    }
    input[type='color'] {
      inline-size: 2.25rem;
      block-size: 2.25rem;
      padding: 0;
      border: 1px solid var(--uui-color-border-standalone);
      border-radius: var(--uui-border-radius);
      background: none;
      cursor: pointer;
    }
    input[type='color']::-webkit-color-swatch-wrapper {
      padding: 2px;
    }
    input[type='color']::-webkit-color-swatch {
      border: none;
      border-radius: calc(var(--uui-border-radius) - 1px);
    }
    input[type='color']::-moz-color-swatch {
      border: none;
      border-radius: calc(var(--uui-border-radius) - 1px);
    }
    input[type='color']:focus-visible {
      outline: 2px solid var(--uui-color-focus);
      outline-offset: 1px;
    }
    uui-input {
      inline-size: 100%;
    }
    .empty {
      margin: 0 0 var(--uui-size-space-3);
      color: var(--uui-color-text-alt);
    }
    .add {
      inline-size: 100%;
    }
    .add uui-icon {
      margin-inline-end: var(--uui-size-space-2);
    }
    :host([invalid]) ul {
      padding-inline-start: var(--uui-size-space-3);
      border-inline-start: 2px solid var(--uui-color-danger);
    }
  `;
}

/**
 * The input an event comes from.
 * @param {Event} event
 * @returns {{ value: string }}
 */
function inputOf(event) {
  return /** @type {{ value: string }} */ (/** @type {unknown} */ (event.target));
}

if (!customElements.get('sample-swatches-editor'))
  customElements.define('sample-swatches-editor', SampleSwatchesEditor);
