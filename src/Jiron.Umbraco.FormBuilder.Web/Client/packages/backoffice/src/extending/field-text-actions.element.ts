// The site's field text actions beside one of the selected field's texts in the inspector (ADR-0018): each
// `jfbFieldTextAction` whose manifest names the text, made once and kept while its manifest stays, given the open
// form's designer, the field and the language shown. With none, it draws nothing and takes no room.
import { css, html, nothing, type PropertyValues } from '@umbraco-cms/backoffice/external/lit';
import { createExtensionElement } from '@umbraco-cms/backoffice/extension-api';
import { UmbLitElement } from '@umbraco-cms/backoffice/lit-element';
import type {
  JfbFieldText,
  JfbFieldTextActionElement,
  JfbFormDesigner,
  ManifestJfbFieldTextAction,
} from '../../public/types/jiron-form-builder';
import { FORM_WORKSPACE_CONTEXT } from '../designer/constants';
import { designerHost } from '../designer/designer-host';
import { EXTENSIONS_CHANGED, fieldTextActionsFor } from './extension-registry';

export class FieldTextActionsElement extends UmbLitElement {
  static override properties = {
    fieldKey: {},
    text: {},
    language: {},
    _elements: { state: true },
  };

  declare fieldKey: string;
  declare text: JfbFieldText;
  declare language: string;
  declare _elements: readonly JfbFieldTextActionElement[];

  #designer: JfbFormDesigner | undefined;
  readonly #made = new Map<ManifestJfbFieldTextAction, Promise<JfbFieldTextActionElement | undefined>>();
  #making = 0;

  constructor() {
    super();
    this.fieldKey = '';
    this.text = 'help';
    this.language = '';
    this._elements = [];
    this.consumeContext(FORM_WORKSPACE_CONTEXT, (context) => {
      this.#designer = context ? designerHost(context) : undefined;
      this.#hand();
    });
  }

  override connectedCallback() {
    super.connectedCallback();
    window.addEventListener(EXTENSIONS_CHANGED, this.#changed);
    void this.#make();
  }

  override disconnectedCallback() {
    window.removeEventListener(EXTENSIONS_CHANGED, this.#changed);
    super.disconnectedCallback();
  }

  protected override updated(changed: PropertyValues<this>) {
    super.updated(changed);
    if (changed.has('text')) void this.#make();
    else this.#hand();
  }

  protected override render() {
    return this._elements.length === 0 ? nothing : html`${this._elements}`;
  }

  readonly #changed = () => void this.#make();

  // Makes the elements of the manifests that name this text; elements of manifests that went are dropped.
  async #make() {
    const making = ++this.#making;
    const manifests = fieldTextActionsFor(this.text);
    for (const kept of [...this.#made.keys()]) if (!manifests.includes(kept)) this.#made.delete(kept);
    const elements = await Promise.all(
      manifests.map((manifest) => {
        let made = this.#made.get(manifest);
        if (!made) {
          made = createExtensionElement(manifest) as Promise<JfbFieldTextActionElement | undefined>;
          this.#made.set(manifest, made);
        }
        return made;
      }),
    );
    if (making !== this.#making) return;
    this._elements = elements.filter((element): element is JfbFieldTextActionElement => element !== undefined);
    this.#hand();
  }

  // What each element gets, set again on every update: the selected field and the language change under it.
  #hand() {
    for (const element of this._elements) {
      element.designer = this.#designer;
      element.fieldKey = this.fieldKey;
      element.text = this.text;
      element.language = this.language;
    }
  }

  static override styles = css`
    :host {
      display: contents;
    }
  `;
}

if (!customElements.get('jfb-field-text-actions'))
  customElements.define('jfb-field-text-actions', FieldTextActionsElement);

declare global {
  interface HTMLElementTagNameMap {
    'jfb-field-text-actions': FieldTextActionsElement;
  }
}
