// @ts-check
// The colour sample's preview in the designer (docs/extending.md, section 7): what the canvas and the preview draw in a
// colour field's place, between its label and its help, which the designer draws. It is given the field with its
// type's settings, the field's own over the defaults, and draws the swatches as the website's view does; it takes no
// input, as the canvas makes it inert and a click selects the field.
import { css, html, LitElement, nothing, styleMap } from '@umbraco-cms/backoffice/external/lit';

/** @typedef {import('../../../../../src/Jiron.Umbraco.FormBuilder.Web/Client/packages/backoffice/public/types/jiron-form-builder').JfbPreviewField} JfbPreviewField */

const HEX = /^#[0-9a-f]{6}$/;

/**
 * The colours a field offers that can be drawn: each with a name and a value such as #1f6feb, which is all that goes
 * into a style.
 * @param {unknown} value
 * @returns {{ name: string, value: string }[]}
 */
function drawable(value) {
  if (!Array.isArray(value)) return [];
  return value.flatMap((item) =>
    item !== null &&
    typeof item === 'object' &&
    typeof item.name === 'string' &&
    typeof item.value === 'string' &&
    HEX.test(item.value)
      ? [{ name: item.name, value: item.value }]
      : [],
  );
}

export default class SampleColourPreview extends LitElement {
  static properties = {
    field: { attribute: false },
    culture: {},
  };

  constructor() {
    super();
    /** @type {JfbPreviewField | undefined} */
    this.field = undefined;
    /** The language the designer shows; a colour's name is one text for every language. */
    this.culture = '';
  }

  render() {
    const settings = this.field?.settings ?? {};
    const swatches = drawable(settings['colours']);
    const names = settings['showNames'] !== false;
    if (swatches.length === 0) return nothing;
    return html`<ul>
      ${swatches.map(
        (swatch) =>
          html`<li>
            <span class="swatch" style=${styleMap({ backgroundColor: swatch.value })}></span>
            <span class=${names ? 'name' : 'name hidden'}>${swatch.name}</span>
          </li>`,
      )}
    </ul>`;
  }

  static styles = css`
    :host {
      display: block;
    }
    ul {
      display: flex;
      flex-wrap: wrap;
      gap: 0.75rem 1rem;
      margin: 0;
      padding: 0;
      list-style: none;
    }
    li {
      display: flex;
      align-items: center;
      gap: 0.5rem;
    }
    .swatch {
      inline-size: 2rem;
      block-size: 2rem;
      border-radius: 50%;
      box-shadow: inset 0 0 0 1px rgb(0 0 0 / 0.15);
    }
    .hidden {
      position: absolute;
      inline-size: 1px;
      block-size: 1px;
      overflow: hidden;
      clip-path: inset(50%);
      white-space: nowrap;
    }
  `;
}

if (!customElements.get('sample-colour-preview')) customElements.define('sample-colour-preview', SampleColourPreview);
