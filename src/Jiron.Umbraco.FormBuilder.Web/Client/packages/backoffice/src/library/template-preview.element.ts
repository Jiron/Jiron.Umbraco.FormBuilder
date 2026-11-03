// A template's form as the gallery previews it (docs/ux/information-architecture.md, section 4; ADR-0166): its pages, their titles
// and its fields as plain controls, drawn from the definition, in the user's language where the template has it. It shows what
// a form will ask for; the designer's own preview elements (9.3b) take its place. Screen readers get the fields as a list.
import { css, html, nothing, type TemplateResult } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import { jfbBackofficeTokens, jfbVisuallyHidden } from '../styles/tokens';

/** A text of a definition: by culture, the default under "". */
type Texts = Record<string, string> | undefined;

interface PreviewField {
  type?: string;
  label?: Texts;
  options?: { items?: { label?: Texts }[] };
  fields?: PreviewField[];
}

interface PreviewPage {
  title?: Texts;
  groups?: { title?: Texts; fields?: PreviewField[] }[];
}

/** How many options a choice shows before it stops. */
const OPTIONS_SHOWN = 4;

/** A text in a culture: the culture itself, its language, then the default; any letter case. */
export function textIn(texts: Texts, culture: string): string {
  if (!texts) return '';
  const wanted = culture.toLowerCase();
  const language = wanted.split('-')[0] ?? wanted;
  const entries = Object.entries(texts).map(([key, text]) => [key.toLowerCase(), text] as const);
  return (
    entries.find(([key]) => key === wanted)?.[1] ??
    entries.find(([key]) => key === language || key.split('-')[0] === language)?.[1] ??
    texts[''] ??
    ''
  );
}

/** The labels of a definition's fields, in order, children after their repeater, for screen readers. */
export function fieldLabels(definition: unknown, culture: string): string[] {
  const pages = ((definition as { pages?: PreviewPage[] } | null)?.pages ?? []) as PreviewPage[];
  const labels: string[] = [];
  const walk = (fields: PreviewField[] | undefined) => {
    for (const field of fields ?? []) {
      const label = textIn(field.label, culture);
      if (label) labels.push(label);
      walk(field.fields);
    }
  };
  for (const page of pages) for (const group of page.groups ?? []) walk(group.fields);
  return labels;
}

const PICK = new Set(['dropdown', 'country']);
const LIST = new Set(['radio', 'checkboxes', 'ranking']);
const TICK = new Set(['checkbox', 'consent']);
const DROP = new Set(['file', 'signature', 'image']);
const NONE = new Set(['hidden', 'captcha']);

export class TemplatePreviewElement extends UmbLitElement {
  static override properties = {
    definition: { attribute: false },
  };

  static override styles = [
    jfbBackofficeTokens,
    jfbVisuallyHidden,
    css`
      :host {
        display: block;
      }

      .sheet {
        display: grid;
        gap: var(--uui-size-space-5);
        padding: var(--uui-size-space-5);
        border: 1px solid var(--uui-color-border);
        border-radius: var(--uui-border-radius);
        background: var(--uui-color-surface);
      }

      h3 {
        margin: 0;
        font-size: var(--uui-type-h5-size);
      }

      .field {
        display: grid;
        gap: var(--uui-size-space-2);
      }

      .label {
        font-weight: 600;
      }

      .box {
        height: 32px;
        border: 1px solid var(--jfb-bo-color-control-border);
        border-radius: var(--uui-border-radius);
        background: var(--uui-color-surface);
      }

      .box--tall {
        height: 88px;
      }

      .box--pick {
        display: flex;
        align-items: center;
        justify-content: flex-end;
        padding-inline-end: var(--uui-size-space-3);
        color: var(--jfb-bo-color-text-muted);
      }

      .box--drop {
        height: 56px;
        border-style: dashed;
      }

      .option {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-3);
      }

      .mark {
        flex: none;
        width: 16px;
        height: 16px;
        border: 1px solid var(--jfb-bo-color-control-border);
        border-radius: var(--uui-border-radius);
      }

      .mark--round {
        border-radius: var(--jfb-bo-radius-round);
      }

      .repeat {
        display: grid;
        gap: var(--uui-size-space-4);
        padding: var(--uui-size-space-4);
        border: 1px dashed var(--uui-color-border);
        border-radius: var(--uui-border-radius);
      }

      .heading {
        font-weight: 700;
      }

      .paragraph {
        color: var(--jfb-bo-color-text-muted);
      }

      hr {
        width: 100%;
        margin: 0;
        border: 0;
        border-top: 1px solid var(--uui-color-divider);
      }
    `,
  ];

  declare definition: unknown;

  #culture(): string {
    return this.localize.lang() || 'en';
  }

  #field(field: PreviewField): TemplateResult | typeof nothing {
    const type = field.type ?? 'text';
    if (NONE.has(type)) return nothing;
    const label = textIn(field.label, this.#culture());
    if (type === 'heading') return html`<div class="heading">${label}</div>`;
    if (type === 'paragraph') return html`<div class="paragraph">${label}</div>`;
    if (type === 'divider') return html`<hr />`;
    if (type === 'repeater')
      return html`<div class="field">
        <span class="label">${label}</span>
        <div class="repeat">${(field.fields ?? []).map((child) => this.#field(child))}</div>
      </div>`;
    if (TICK.has(type)) return html`<div class="option"><span class="mark"></span><span>${label}</span></div>`;
    return html`<div class="field">
      <span class="label">${label}</span>
      ${this.#control(type, field)}
    </div>`;
  }

  #control(type: string, field: PreviewField): TemplateResult {
    if (type === 'textarea' || type === 'address' || type === 'grid') return html`<div class="box box--tall"></div>`;
    if (PICK.has(type)) return html`<div class="box box--pick"><uui-icon name="icon-navigation-down"></uui-icon></div>`;
    if (DROP.has(type)) return html`<div class="box box--drop"></div>`;
    if (LIST.has(type) || type === 'yesno') {
      const options =
        type === 'yesno'
          ? [this.localize.term('jfbLibrary_previewYes'), this.localize.term('jfbLibrary_previewNo')]
          : (field.options?.items ?? []).slice(0, OPTIONS_SHOWN).map((option) => textIn(option.label, this.#culture()));
      const round = type !== 'checkboxes';
      return html`${options.map(
        (option) =>
          html`<div class="option">
            <span class="mark ${round ? 'mark--round' : ''}"></span><span>${option}</span>
          </div>`,
      )}`;
    }

    // A line for anything else that takes an answer: text, numbers, dates, a name.
    return html`<div class="box"></div>`;
  }

  override render() {
    const pages = ((this.definition as { pages?: PreviewPage[] } | null)?.pages ?? []) as PreviewPage[];
    const labels = fieldLabels(this.definition, this.#culture());
    return html`
      <div class="sheet" aria-hidden="true" inert>
        ${pages.map(
          (page) => html`
            ${textIn(page.title, this.#culture()) ? html`<h3>${textIn(page.title, this.#culture())}</h3>` : nothing}
            ${(page.groups ?? []).map((group) => (group.fields ?? []).map((field) => this.#field(field)))}
          `,
        )}
      </div>
      <p class="visually-hidden">${this.localize.term('jfbLibrary_previewFields', labels.join(', '))}</p>
    `;
  }
}

if (!customElements.get('jfb-template-preview')) customElements.define('jfb-template-preview', TemplatePreviewElement);

declare global {
  interface HTMLElementTagNameMap {
    'jfb-template-preview': TemplatePreviewElement;
  }
}
