// A form as visitors see it, read-only (ROADMAP 9.3b, ADR-0166, ADR-0168): its pages one after another, each written by
// the headless element's views from the view models the designer builds, in the default theme. The template gallery
// shows a template with it, in the user's language where the template has it. The drawing is for the eye only: neither
// the keyboard nor a screen reader enters it, and screen readers hear the fields' labels once, as a sentence.
import { css, html, type PropertyValues } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { writePage } from '@jfb/element/views';
import { previewControl } from '../../extending/preview-control';
import type { FieldTypeResponseModel } from '../../api';
import { jfbBackofficeTokens, jfbVisuallyHidden } from '../../styles/tokens';
import { normalise, type DesignField } from '../definition';
import { asEnhanced, formSettings, pageModel, textIn, type PreviewContext } from './field-models';
import { readLook, readTypes, themeSheets, type CanvasLook } from './look';

/** The labels of a definition's fields, in order, a repeatable group's after it, for screen readers. */
export function fieldLabels(definition: unknown, culture: string): string[] {
  const labels: string[] = [];
  const walk = (fields: readonly DesignField[] | null | undefined) => {
    for (const field of fields ?? []) {
      const label = textIn(field.label, culture);
      if (label) labels.push(label);
      walk(field.fields);
    }
  };
  for (const page of normalise(definition).pages) for (const group of page.groups) walk(group.fields);
  return labels;
}

export class FormPreviewElement extends UmbLitElement {
  static override properties = {
    definition: { attribute: false },
    look: { attribute: false },
    types: { attribute: false },
    _sheets: { state: true },
    _failed: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    jfbVisuallyHidden,
    css`
      :host {
        display: block;
      }

      /* The visitor's page: white, whatever the backoffice's theme, as the default theme's light scheme expects. */
      .sheet {
        padding: var(--uui-size-space-5);
        border: 1px solid var(--uui-color-border);
        border-radius: var(--uui-border-radius);
        background: #fff;
        color: #1d2433;
      }

      .failed {
        margin: 0;
      }

      .loading {
        display: grid;
        place-items: center;
        min-height: 120px;
      }
    `,
  ];

  declare definition: unknown;
  /** What the preview draws with; read from the server when no one gives it. */
  declare look: CanvasLook | undefined;
  /** The field types by alias; read from the server when no one gives them. */
  declare types: Readonly<Record<string, FieldTypeResponseModel>> | undefined;
  declare private _sheets: readonly CSSStyleSheet[];
  /** Whether what the preview draws with could not be read. */
  declare private _failed: boolean;

  constructor() {
    super();
    this._sheets = [];
    this._failed = false;
  }

  override connectedCallback() {
    super.connectedCallback();
    void this.#read();
  }

  async #read() {
    const culture = this.localize.lang() || undefined;
    const [look, types] = await Promise.all([this.look ?? readLook(this, culture), this.types ?? readTypes(this)]);
    this.look ??= look;
    this.types ??= types;
    this._failed = !look || !types;
    if (look) this._sheets = await themeSheets(look.stylesheets);
  }

  protected override updated(changed: PropertyValues) {
    super.updated(changed);
    const root = this.shadowRoot;
    if (changed.has('_sheets') && root && 'adoptedStyleSheets' in root) {
      // The theme first, so the preview's own rules come after it.
      const own = FormPreviewElement.elementStyles.flatMap((style) =>
        style instanceof CSSStyleSheet ? [style] : style.styleSheet ? [style.styleSheet] : [],
      );
      root.adoptedStyleSheets = [...this._sheets, ...own];
    }
  }

  override render() {
    const { look, types } = this;
    if (this._failed) return html`<p class="failed">${this.localize.term('jfbDesigner_previewFailed')}</p>`;
    if (!look || !types) return html`<div class="loading"><uui-loader></uui-loader></div>`;
    const culture = this.localize.lang() || look.culture;
    const definition = normalise(this.definition);
    const context: PreviewContext = {
      look,
      types,
      culture,
      prefix: 'jfb-preview',
      ...formSettings(definition, look, culture),
    };
    const pages = definition.pages.map((page) => {
      const written = writePage({ document, api: '', control: previewControl }, look.classes, pageModel(page, context));
      asEnhanced(written);
      return written;
    });
    return html`<div class="sheet">
        <div class="jfb-form jfb-theme-default" lang=${culture} aria-hidden="true" inert>${pages}</div>
      </div>
      <p class="visually-hidden">
        ${this.localize.term('jfbDesigner_previewFields', fieldLabels(definition, culture).join(', '))}
      </p>`;
  }
}

if (!customElements.get('jfb-form-preview')) customElements.define('jfb-form-preview', FormPreviewElement);

declare global {
  interface HTMLElementTagNameMap {
    'jfb-form-preview': FormPreviewElement;
  }
}
