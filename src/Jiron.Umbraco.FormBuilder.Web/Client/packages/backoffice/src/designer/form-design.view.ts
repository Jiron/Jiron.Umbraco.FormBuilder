// The Design view (ROADMAP 9.3a): until the canvas and palette of 9.3b take its place, the form's pages, groups and
// fields as an outline, each field a button that selects it, the selected one marked. A link that names a field
// (`?field=`, ADR-0166) opens the view with that field selected and in sight.
import { css, html, nothing, type PropertyValues } from '@umbraco-cms/backoffice/external/lit';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import type { FieldTypeResponseModel } from '../api';
import { jfbBackofficeTokens } from '../styles/tokens';
import { FORM_WORKSPACE_CONTEXT } from './constants';
import type { DesignField, DesignGroup, DesignPage, LocalizedText } from './definition';
import { select } from './editor-model';
import type { EditorView, FormWorkspaceContext } from './form-workspace.context';

/** A text in the form's default language, else its first one. */
export const textOf = (text: LocalizedText | null | undefined): string =>
  text?.[''] ?? Object.values(text ?? {}).find((value) => value !== '') ?? '';

export class FormDesignViewElement extends UmbLitElement {
  static override properties = {
    _view: { state: true },
    _types: { state: true },
  };

  static override styles = [
    jfbBackofficeTokens,
    css`
      :host {
        display: block;
        padding: var(--uui-size-layout-1);
      }

      uui-box + uui-box {
        margin-top: var(--uui-size-space-5);
      }

      h3 {
        margin: var(--uui-size-space-5) 0 var(--uui-size-space-2);
        font-size: var(--uui-type-default-size);
        color: var(--jfb-bo-color-text-muted);
      }

      h3:first-of-type {
        margin-top: 0;
      }

      ul {
        list-style: none;
        margin: 0;
        padding: 0;
      }

      ul ul {
        margin-inline-start: var(--uui-size-space-6);
      }

      button {
        display: flex;
        align-items: center;
        gap: var(--uui-size-space-3);
        width: 100%;
        min-height: 44px;
        padding: var(--uui-size-space-2) var(--uui-size-space-4);
        border: 1px solid transparent;
        border-radius: var(--uui-border-radius);
        background: none;
        color: inherit;
        font: inherit;
        text-align: start;
        cursor: pointer;
      }

      button:hover {
        background: var(--uui-color-surface-emphasis);
      }

      button:focus-visible {
        outline: 2px solid var(--uui-color-focus);
        outline-offset: 1px;
      }

      button[aria-current='true'] {
        border-color: var(--uui-color-selected);
        background: var(--jfb-bo-color-selected);
        color: var(--uui-color-selected-contrast);
      }

      .label {
        flex: 1 1 auto;
        min-width: 0;
        overflow-wrap: anywhere;
      }

      .type {
        flex: none;
        color: var(--jfb-bo-color-text-muted);
      }

      button[aria-current='true'] .type {
        color: inherit;
      }

      .empty {
        margin: 0;
        color: var(--jfb-bo-color-text-muted);
      }
    `,
  ];

  declare private _view: EditorView | undefined;
  declare private _types: Readonly<Record<string, FieldTypeResponseModel>>;
  #context: FormWorkspaceContext | undefined;
  /** Whether the selection a link named was brought into sight. */
  #shown = false;

  constructor() {
    super();
    this._types = {};
    this.consumeContext(FORM_WORKSPACE_CONTEXT, (context) => {
      this.#context = context;
      this.observe(context?.editor, (view) => (this._view = view), 'editor');
      this.observe(context?.fieldTypes, (types) => (this._types = types ?? {}), 'types');
    });
  }

  protected override updated(changed: PropertyValues) {
    super.updated(changed);
    const selection = this._view?.state.selection;
    if (this.#shown || !selection) return;
    this.#shown = true;
    this.shadowRoot?.querySelector('[aria-current="true"]')?.scrollIntoView({ block: 'center' });
  }

  #select(key: string) {
    const context = this.#context;
    if (!context) return;
    const current = context.getEditorState();
    context.edit(select(current, key), '');
  }

  #field(field: DesignField): unknown {
    const type = Object.hasOwn(this._types, field.type) ? this._types[field.type] : undefined;
    const label = textOf(field.label) || field.alias || this.localize.term('jfbDesigner_untitledField');
    const selected = this._view?.state.selection === field.key;
    return html`<li>
      <button type="button" aria-current=${selected ? 'true' : 'false'} @click=${() => this.#select(field.key)}>
        <uui-icon name=${type?.icon ?? 'icon-document'}></uui-icon>
        <span class="label">${label}</span>
        <span class="type">${type ? this.localize.string(type.name) : field.type}</span>
      </button>
      ${
        field.fields?.length
          ? html`<ul>
              ${field.fields.map((child) => this.#field(child))}
            </ul>`
          : nothing
      }
    </li>`;
  }

  #group(group: DesignGroup) {
    const title = textOf(group.title);
    return html`${title ? html`<h3>${title}</h3>` : nothing}
    ${
      group.fields.length
        ? html`<ul>
            ${group.fields.map((field) => this.#field(field))}
          </ul>`
        : html`<p class="empty">${this.localize.term('jfbDesigner_emptyGroup')}</p>`
    }`;
  }

  #page(page: DesignPage, index: number) {
    const title = textOf(page.title) || this.localize.term('jfbDesigner_pageNumber', index + 1);
    return html`<uui-box headline=${title} headline-variant="h2">
      ${
        page.groups.length
          ? page.groups.map((group) => this.#group(group))
          : html`<p class="empty">${this.localize.term('jfbDesigner_emptyPage')}</p>`
      }
    </uui-box>`;
  }

  override render() {
    const pages = this._view?.state.definition.pages ?? [];
    return pages.map((page, index) => this.#page(page, index));
  }
}

if (!customElements.get('jfb-form-design-view')) customElements.define('jfb-form-design-view', FormDesignViewElement);

export { FormDesignViewElement as element };

declare global {
  interface HTMLElementTagNameMap {
    'jfb-form-design-view': FormDesignViewElement;
  }
}
